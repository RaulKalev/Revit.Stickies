using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stickies.Core.Models;
using Stickies.Core.Persistence;

namespace Stickies.Core.Store
{
    public sealed class CompactionResult
    {
        public CompactionResult(int archived, string skippedReason)
        {
            Archived = archived;
            SkippedReason = skippedReason;
        }

        /// <summary>Issues moved from <c>active/</c> into the archive.</summary>
        public int Archived { get; }

        /// <summary>Why nothing was done (another session holds the lock), or null.</summary>
        public string SkippedReason { get; }

        public bool Ran => SkippedReason == null;
    }

    /// <summary>
    /// Moves done issues out of <c>active/</c> into monthly archive files, so the active folder stays small.
    ///
    /// An issue is archived when it is resolved or closed and nobody has changed it for <see cref="Grace"/> (so the
    /// author still sees a resolved issue to close it). It goes into <c>archive/YYYY-MM.json</c> for the month it was
    /// resolved. A month file is merged by id (a re-archived issue replaces its older copy); other months are never
    /// touched. The active file is deleted only after the archive is written, and only if nobody changed it meanwhile.
    ///
    /// Only one session compacts at a time: it holds <c>compact.lock</c>; a lock older than <see cref="StaleLock"/> is
    /// taken over (its session ended without cleaning up). Dropbox syncs the lock with a delay, so two machines can
    /// rarely compact at once; the merge by id and atomic writes keep that safe.
    /// </summary>
    public sealed class Compactor
    {
        public static readonly TimeSpan Grace = TimeSpan.FromDays(7);
        public static readonly TimeSpan StaleLock = TimeSpan.FromMinutes(10);

        private readonly IssueStore _store;
        private readonly string _owner;
        private readonly Func<DateTimeOffset> _clock;

        public Compactor(IssueStore store, string owner, Func<DateTimeOffset> clock = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _owner = string.IsNullOrWhiteSpace(owner) ? "unknown" : owner;
            _clock = clock ?? (() => DateTimeOffset.Now);
        }

        /// <summary>Test hook: runs after the archive is written, before the active files are deleted.</summary>
        internal Action BeforeDelete { get; set; }

        public static bool IsDue(Issue issue, DateTimeOffset now) => issue.IsDone && now - issue.Modified >= Grace;

        public CompactionResult Run()
        {
            var folder = _store.Folder;
            if (!Directory.Exists(folder.ActiveDir)) return new CompactionResult(0, null);

            var now = _clock();
            var due = _store.LoadActive().Issues.Where(i => IsDue(i, now)).ToList();
            if (due.Count == 0) return new CompactionResult(0, null);

            var lockToken = Guid.NewGuid().ToString("N");
            var reason = TryLock(folder.CompactLock, lockToken, now);
            if (reason != null) return new CompactionResult(0, reason);
            try
            {
                Directory.CreateDirectory(folder.ArchiveDir);
                foreach (var month in due.GroupBy(MonthOf))
                {
                    var path = folder.ArchivePath(month.Key.Year, month.Key.Month);
                    var merged = IssueStore.ReadArchiveFile(path).Where(i => i != null && i.Id != null).ToDictionary(i => i.Id);
                    foreach (var issue in month) merged[issue.Id] = issue;
                    var ordered = merged.Values.OrderBy(i => i.Number ?? int.MaxValue).ThenBy(i => i.Id, StringComparer.Ordinal).ToList();
                    AtomicFile.WriteAllText(path, MarkupJson.Serialize(ordered));
                }

                BeforeDelete?.Invoke();

                var archived = 0;
                foreach (var issue in due)
                {
                    // Reopened or commented on since we read it: keep it active; the archive copy is ignored while it is.
                    var current = _store.Load(issue.Id);
                    if (current == null || !Timestamp.SameSecond(current.Modified, issue.Modified)) continue;
                    try
                    {
                        File.Delete(folder.IssuePath(issue.Id));
                        archived++;
                    }
                    catch (IOException)
                    {
                        // locked by a sync client: the next run deletes it (it is already archived)
                    }
                }
                return new CompactionResult(archived, null);
            }
            finally
            {
                ReleaseLock(folder.CompactLock, lockToken);
            }
        }

        private static DateTime MonthOf(Issue issue)
        {
            var at = (issue.ResolvedAt ?? issue.Modified).ToLocalTime();
            return new DateTime(at.Year, at.Month, 1);
        }

        // ------------------------------------------------------------------ lock

        private sealed class LockFile
        {
            public string Owner { get; set; }
            public string Token { get; set; }
            public DateTimeOffset At { get; set; }
        }

        /// <summary>Takes the lock; returns null when taken, else why not.</summary>
        private string TryLock(string path, string token, DateTimeOffset now)
        {
            try
            {
                var existing = ReadLock(path);
                if (existing != null && now - existing.At < StaleLock)
                    return "Another session (" + existing.Owner + ") is archiving.";

                AtomicFile.WriteAllText(path, MarkupJson.Serialize(new LockFile { Owner = _owner, Token = token, At = now }));
                var mine = ReadLock(path);
                return mine != null && mine.Token == token ? null : "Another session took the archive lock first.";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return "The archive lock could not be taken: " + ex.Message;
            }
        }

        private static void ReleaseLock(string path, string token)
        {
            try
            {
                var current = ReadLock(path);
                if (current != null && current.Token == token) File.Delete(path);
            }
            catch (Exception)
            {
                // a lock left behind goes stale after StaleLock
            }
        }

        private static LockFile ReadLock(string path)
        {
            try
            {
                return File.Exists(path) ? MarkupJson.Deserialize<LockFile>(File.ReadAllText(path)) : null;
            }
            catch (Exception)
            {
                return null; // unreadable: treat as no lock
            }
        }
    }
}
