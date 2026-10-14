using System;
using System.IO;
using System.Linq;
using Stickies.Core.Models;
using Stickies.Core.Persistence;
using Stickies.Core.Store;
using Xunit;

namespace Stickies.Core.Tests
{
    public class CompactorTests
    {
        private static readonly TimeSpan Tallinn = TimeSpan.FromHours(3);
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 20, 10, 0, 0, Tallinn);

        private static Issue Make(string id, IssueStatus status, DateTimeOffset modified, int number, DateTimeOffset? resolvedAt = null) => new Issue
        {
            Id = id,
            Number = number,
            Title = "Issue " + number,
            Author = "raul",
            Status = status,
            Created = modified.AddDays(-1),
            Modified = modified,
            ResolvedBy = resolvedAt.HasValue ? "joonas" : null,
            ResolvedAt = resolvedAt
        };

        private static (MarkupFolder folder, IssueStore store) Setup(TempDir dir)
        {
            var folder = new MarkupFolder(Path.Combine(dir.Path, "_stickies"));
            var store = new IssueStore(folder);
            store.Initialize(new MarkupProject { ModelGuid = "m" });
            return (folder, store);
        }

        [Fact]
        public void Archives_only_done_issues_untouched_for_the_grace_period()
        {
            using (var dir = new TempDir())
            {
                var (folder, store) = Setup(dir);
                var old = Now.AddDays(-30);
                store.Save(Make("20261001-100000-aaaaaa", IssueStatus.Closed, old, 1, old));
                store.Save(Make("20261001-100000-bbbbbb", IssueStatus.Resolved, old, 2, old));
                store.Save(Make("20261001-100000-cccccc", IssueStatus.Open, old, 3));              // not done
                store.Save(Make("20261001-100000-dddddd", IssueStatus.InProgress, old, 4));        // not done
                store.Save(Make("20261001-100000-eeeeee", IssueStatus.Resolved, Now.AddDays(-2), 5, Now.AddDays(-2))); // too recent

                var result = new Compactor(store, "raul", () => Now).Run();

                Assert.True(result.Ran);
                Assert.Equal(2, result.Archived);
                Assert.Equal(new[] { 3, 4, 5 }, store.LoadActive().Issues.Select(i => i.Number.Value).OrderBy(n => n));
                var archived = IssueStore.ReadArchiveFile(folder.ArchivePath(2026, 10));
                Assert.Equal(new[] { 1, 2 }, archived.Select(i => i.Number.Value));
                Assert.False(File.Exists(folder.CompactLock));
            }
        }

        [Fact]
        public void Issues_go_into_the_month_they_were_resolved_and_months_are_merged_not_rewritten()
        {
            using (var dir = new TempDir())
            {
                var (folder, store) = Setup(dir);
                var september = new DateTimeOffset(2026, 9, 15, 12, 0, 0, Tallinn);
                var october = new DateTimeOffset(2026, 10, 3, 12, 0, 0, Tallinn);

                // An earlier run archived issue 1 in September.
                AtomicFile.WriteAllText(folder.ArchivePath(2026, 9),
                    MarkupJson.Serialize(new[] { Make("20260901-100000-111111", IssueStatus.Closed, september, 1, september) }));
                var augustText = MarkupJson.Serialize(new[] { Make("20260801-100000-222222", IssueStatus.Closed, september.AddMonths(-1), 9, september.AddMonths(-1)) });
                AtomicFile.WriteAllText(folder.ArchivePath(2026, 8), augustText);

                store.Save(Make("20260910-100000-333333", IssueStatus.Closed, september, 2, september));
                store.Save(Make("20261003-100000-444444", IssueStatus.Resolved, october, 3, october));

                Assert.Equal(2, new Compactor(store, "raul", () => Now).Run().Archived);

                Assert.Equal(new[] { 1, 2 }, IssueStore.ReadArchiveFile(folder.ArchivePath(2026, 9)).Select(i => i.Number.Value));
                Assert.Equal(new[] { 3 }, IssueStore.ReadArchiveFile(folder.ArchivePath(2026, 10)).Select(i => i.Number.Value));
                Assert.Equal(augustText, File.ReadAllText(folder.ArchivePath(2026, 8))); // other months untouched
            }
        }

        [Fact]
        public void An_issue_reopened_during_archiving_stays_active()
        {
            using (var dir = new TempDir())
            {
                var (_, store) = Setup(dir);
                var old = Now.AddDays(-30);
                store.Save(Make("20261001-100000-aaaaaa", IssueStatus.Resolved, old, 1, old));

                var compactor = new Compactor(store, "raul", () => Now)
                {
                    BeforeDelete = () =>
                    {
                        var reopened = store.Load("20261001-100000-aaaaaa");
                        reopened.Status = IssueStatus.Open;
                        reopened.Modified = Now;
                        store.Save(reopened);
                    }
                };

                Assert.Equal(0, compactor.Run().Archived);
                var all = store.LoadAll().Issues;
                var issue = Assert.Single(all);
                Assert.Equal(IssueStatus.Open, issue.Status); // the active copy wins over the archive copy
                Assert.False(issue.IsArchived);
            }
        }

        [Fact]
        public void A_fresh_lock_held_by_another_session_is_respected_and_a_stale_one_taken_over()
        {
            using (var dir = new TempDir())
            {
                var (folder, store) = Setup(dir);
                var old = Now.AddDays(-30);
                store.Save(Make("20261001-100000-aaaaaa", IssueStatus.Closed, old, 1, old));

                File.WriteAllText(folder.CompactLock, MarkupJson.Serialize(new { owner = "joonas", token = "x", at = Now.AddMinutes(-2) }));
                var blocked = new Compactor(store, "raul", () => Now).Run();
                Assert.False(blocked.Ran);
                Assert.Contains("joonas", blocked.SkippedReason);
                Assert.Single(store.LoadActive().Issues);

                File.WriteAllText(folder.CompactLock, MarkupJson.Serialize(new { owner = "joonas", token = "x", at = Now.AddMinutes(-30) }));
                var taken = new Compactor(store, "raul", () => Now).Run();
                Assert.True(taken.Ran);
                Assert.Equal(1, taken.Archived);
                Assert.False(File.Exists(folder.CompactLock));
            }
        }

        [Fact]
        public void Nothing_due_means_no_lock_and_no_archive_file()
        {
            using (var dir = new TempDir())
            {
                var (folder, store) = Setup(dir);
                store.Save(Make("20261001-100000-aaaaaa", IssueStatus.Open, Now.AddDays(-30), 1));
                var result = new Compactor(store, "raul", () => Now).Run();
                Assert.True(result.Ran);
                Assert.Equal(0, result.Archived);
                Assert.False(File.Exists(folder.CompactLock));
                Assert.Empty(Directory.GetFiles(folder.ArchiveDir));
            }
        }

        [Fact]
        public void LoadAll_reads_the_archive_marks_it_and_reports_conflict_copies()
        {
            using (var dir = new TempDir())
            {
                var (folder, store) = Setup(dir);
                var old = Now.AddDays(-30);
                store.Save(Make("20261001-100000-aaaaaa", IssueStatus.Open, old, 1));
                AtomicFile.WriteAllText(folder.ArchivePath(2026, 10), MarkupJson.Serialize(new[]
                {
                    Make("20261001-100000-bbbbbb", IssueStatus.Closed, old, 2, old),
                    Make("20261001-100000-aaaaaa", IssueStatus.Closed, old.AddDays(-5), 1, old.AddDays(-5)) // older copy of an active one
                }));
                File.WriteAllText(Path.Combine(folder.ArchiveDir, "2026-10 (Joonas's conflicted copy 2026-10-20).json"), "[]");

                var result = store.LoadAll();

                Assert.Equal(2, result.Issues.Count);
                Assert.False(result.Issues.Single(i => i.Number == 1).IsArchived);
                Assert.Equal(IssueStatus.Open, result.Issues.Single(i => i.Number == 1).Status);
                Assert.True(result.Issues.Single(i => i.Number == 2).IsArchived);
                Assert.Contains(result.Skipped, s => s.Path.Contains("conflicted copy"));
            }
        }

        [Fact]
        public void Saving_an_archived_issue_makes_it_active_again()
        {
            using (var dir = new TempDir())
            {
                var (folder, store) = Setup(dir);
                var old = Now.AddDays(-30);
                AtomicFile.WriteAllText(folder.ArchivePath(2026, 10), MarkupJson.Serialize(new[] { Make("20261001-100000-bbbbbb", IssueStatus.Closed, old, 2, old) }));
                var archived = store.LoadAll().Issues.Single();
                Assert.True(archived.IsArchived);

                archived.Status = IssueStatus.Open;
                archived.Modified = Now;
                store.Save(archived);

                Assert.False(archived.IsArchived);
                var again = store.LoadAll().Issues.Single();
                Assert.False(again.IsArchived);
                Assert.Equal(IssueStatus.Open, again.Status);
            }
        }
    }
}
