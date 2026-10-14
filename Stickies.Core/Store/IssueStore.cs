using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stickies.Core.Models;
using Stickies.Core.Persistence;

namespace Stickies.Core.Store
{
    /// <summary>A file in <c>active/</c> that was not loaded, and why.</summary>
    public sealed class SkippedFile
    {
        public SkippedFile(string path, string reason)
        {
            Path = path;
            Reason = reason;
        }

        public string Path { get; }
        public string Reason { get; }
    }

    public sealed class IssueLoadResult
    {
        public List<Issue> Issues { get; } = new List<Issue>();

        /// <summary>Files that are not issues (Dropbox "conflicted copy" files, stray files) or could not be read.</summary>
        public List<SkippedFile> Skipped { get; } = new List<SkippedFile>();
    }

    public enum SaveOutcome
    {
        Saved,

        /// <summary>Someone changed the issue since it was loaded; nothing was written.</summary>
        Conflict
    }

    /// <summary>
    /// Reads and writes one project's <see cref="MarkupFolder"/>. Plain file I/O with no Revit dependency, safe to call
    /// from any thread. Writes are atomic. An issue's status change rewrites only its own file, and nothing here
    /// deletes from <c>active/</c> (only the compactor does).
    /// </summary>
    public sealed class IssueStore
    {
        public IssueStore(MarkupFolder folder)
        {
            Folder = folder ?? throw new ArgumentNullException(nameof(folder));
        }

        public MarkupFolder Folder { get; }

        /// <summary>Creates the folder layout, and project.json when it is missing (an existing one is kept).</summary>
        public void Initialize(MarkupProject project)
        {
            Directory.CreateDirectory(Folder.ActiveDir);
            Directory.CreateDirectory(Folder.ArchiveDir);
            if (!File.Exists(Folder.ProjectFile))
                AtomicFile.WriteAllText(Folder.ProjectFile, MarkupJson.Serialize(project));
        }

        /// <summary>project.json, or null when it is missing or unreadable.</summary>
        public MarkupProject ReadProject()
        {
            try
            {
                return File.Exists(Folder.ProjectFile) ? MarkupJson.Deserialize<MarkupProject>(File.ReadAllText(Folder.ProjectFile)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>All issues in <c>active/</c>. Only files named <c>&lt;id&gt;.json</c> are issues; the rest are reported.</summary>
        public IssueLoadResult LoadActive()
        {
            var result = new IssueLoadResult();
            if (!Directory.Exists(Folder.ActiveDir)) return result;

            foreach (var path in Directory.EnumerateFiles(Folder.ActiveDir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                if (path.EndsWith(AtomicFile.TempExtension, StringComparison.OrdinalIgnoreCase)) continue; // a write in progress

                var id = Path.GetFileNameWithoutExtension(path);
                if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !IssueId.IsValid(id))
                {
                    result.Skipped.Add(new SkippedFile(path, "Not an issue file (for example a sync conflict copy)."));
                    continue;
                }

                try
                {
                    var issue = Read(path);
                    if (issue == null || issue.Id != id)
                        result.Skipped.Add(new SkippedFile(path, "The file's id does not match its name."));
                    else
                        result.Issues.Add(issue);
                }
                catch (Exception ex)
                {
                    result.Skipped.Add(new SkippedFile(path, "Could not be read: " + ex.Message));
                }
            }
            return result;
        }

        /// <summary>One issue from <c>active/</c>, or null when it is not there.</summary>
        public Issue Load(string id)
        {
            var path = Folder.IssuePath(id);
            return File.Exists(path) ? Read(path) : null;
        }

        /// <summary>
        /// Writes the issue to its own file. With <paramref name="expectedModified"/> (the Modified value the caller
        /// loaded), the write is refused when the file on disk has changed since, so a colleague's edit is never lost.
        /// </summary>
        public SaveOutcome Save(Issue issue, DateTimeOffset? expectedModified = null)
        {
            if (issue == null) throw new ArgumentNullException(nameof(issue));
            if (!IssueId.IsValid(issue.Id)) throw new ArgumentException("Invalid issue id: " + issue.Id, nameof(issue));
            if (issue.SchemaVersion > Issue.CurrentSchemaVersion)
                throw new InvalidOperationException("The issue was written by a newer Stickies and cannot be changed by this version.");

            if (expectedModified.HasValue)
            {
                var current = Load(issue.Id);
                if (current != null && !Timestamp.SameSecond(current.Modified, expectedModified.Value)) return SaveOutcome.Conflict;
            }

            Directory.CreateDirectory(Folder.ActiveDir);
            AtomicFile.WriteAllText(Folder.IssuePath(issue.Id), MarkupJson.Serialize(issue));
            return SaveOutcome.Saved;
        }

        private static Issue Read(string path) => MarkupJson.Deserialize<Issue>(ReadShared(path));

        /// <summary>Reads while a sync client may hold the file open.</summary>
        private static string ReadShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }
    }
}
