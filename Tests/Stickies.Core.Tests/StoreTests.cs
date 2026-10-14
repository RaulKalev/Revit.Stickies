using System;
using System.IO;
using System.Linq;
using System.Threading;
using Stickies.Core.Models;
using Stickies.Core.Persistence;
using Stickies.Core.Store;
using Xunit;

namespace Stickies.Core.Tests
{
    public class StoreTests
    {
        [Fact]
        public void Initialize_creates_the_layout_and_keeps_an_existing_project_file()
        {
            using (var dir = new TempDir())
            {
                var folder = new MarkupFolder(Path.Combine(dir.Path, "_markup"));
                var store = new IssueStore(folder);

                store.Initialize(new MarkupProject { ModelGuid = "first", ProjectName = "P1" });
                store.Initialize(new MarkupProject { ModelGuid = "second", ProjectName = "P2" });

                Assert.True(Directory.Exists(folder.ActiveDir));
                Assert.True(Directory.Exists(folder.ArchiveDir));
                Assert.Equal("first", store.ReadProject().ModelGuid);
            }
        }

        [Fact]
        public void Saved_issue_loads_back_identical()
        {
            using (var dir = new TempDir())
            {
                var store = new IssueStore(new MarkupFolder(dir.Path));
                var issue = SerializationTests.FullIssue();

                Assert.Equal(SaveOutcome.Saved, store.Save(issue));
                var loaded = store.LoadActive();

                Assert.Empty(loaded.Skipped);
                Assert.Equal(MarkupJson.Serialize(issue), MarkupJson.Serialize(Assert.Single(loaded.Issues)));
                Assert.True(File.Exists(Path.Combine(dir.Path, "active", issue.Id + ".json")));
            }
        }

        [Fact]
        public void Conflict_copies_temp_files_and_broken_files_are_skipped_not_fatal()
        {
            using (var dir = new TempDir())
            {
                var folder = new MarkupFolder(dir.Path);
                var store = new IssueStore(folder);
                var issue = SerializationTests.FullIssue();
                store.Save(issue);

                File.WriteAllText(Path.Combine(folder.ActiveDir, issue.Id + " (Joonas's conflicted copy 2026-10-14).json"), "{}");
                File.WriteAllText(Path.Combine(folder.ActiveDir, "20261014-093012-a3f9c1.json.1a2b3c4d.tmp"), "{ half");
                File.WriteAllText(Path.Combine(folder.ActiveDir, "20261015-120000-bbbbbb.json"), "{ not json");
                File.WriteAllText(Path.Combine(folder.ActiveDir, "20261015-120000-cccccc.json"),
                    MarkupJson.Serialize(new Issue { Id = "20261015-120000-dddddd", Title = "wrong file" }));
                File.WriteAllText(Path.Combine(folder.ActiveDir, "desktop.ini"), "");

                var loaded = store.LoadActive();

                Assert.Equal(issue.Id, Assert.Single(loaded.Issues).Id);
                Assert.Equal(4, loaded.Skipped.Count); // conflict copy, broken json, id mismatch, desktop.ini (tmp is silent)
                Assert.Contains(loaded.Skipped, s => s.Path.Contains("conflicted copy"));
            }
        }

        [Fact]
        public void Save_refuses_to_overwrite_a_newer_edit()
        {
            using (var dir = new TempDir())
            {
                var store = new IssueStore(new MarkupFolder(dir.Path));
                var issue = SerializationTests.FullIssue();
                store.Save(issue);
                var loadedModified = issue.Modified;

                // A colleague resolves it meanwhile.
                var theirs = store.Load(issue.Id);
                theirs.Status = IssueStatus.Resolved;
                theirs.Modified = loadedModified.AddMinutes(5);
                store.Save(theirs);

                // My stale edit is refused …
                issue.Title = "My edit";
                issue.Modified = loadedModified.AddMinutes(6);
                Assert.Equal(SaveOutcome.Conflict, store.Save(issue, loadedModified));
                Assert.Equal(IssueStatus.Resolved, store.Load(issue.Id).Status);

                // … and accepted when based on their version.
                Assert.Equal(SaveOutcome.Saved, store.Save(issue, theirs.Modified));
                Assert.Equal("My edit", store.Load(issue.Id).Title);
            }
        }

        [Fact]
        public void Follow_up_save_is_not_a_conflict_with_sub_second_times()
        {
            using (var dir = new TempDir())
            {
                var store = new IssueStore(new MarkupFolder(dir.Path));
                var issue = SerializationTests.FullIssue();
                issue.Modified = issue.Modified.AddMilliseconds(437); // the file keeps whole seconds
                store.Save(issue);
                issue.Title = "Second edit";
                Assert.Equal(SaveOutcome.Saved, store.Save(issue, issue.Modified));
            }
        }

        [Fact]
        public void Save_rejects_invalid_ids_and_newer_schemas()
        {
            using (var dir = new TempDir())
            {
                var store = new IssueStore(new MarkupFolder(dir.Path));
                Assert.Throws<ArgumentException>(() => store.Save(new Issue { Id = "../evil" }));
                var newer = SerializationTests.FullIssue();
                newer.SchemaVersion = Issue.CurrentSchemaVersion + 1;
                Assert.Throws<InvalidOperationException>(() => store.Save(newer));
            }
        }

        [Fact]
        public void Reading_waits_for_a_briefly_locked_file()
        {
            using (var dir = new TempDir())
            {
                var store = new IssueStore(new MarkupFolder(dir.Path));
                var issue = SerializationTests.FullIssue();
                store.Save(issue);
                var path = Path.Combine(dir.Path, "active", issue.Id + ".json");

                using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var release = new Thread(() => { Thread.Sleep(150); locked.Dispose(); });
                    release.Start();
                    Assert.Equal(issue.Title, store.Load(issue.Id).Title); // succeeds once the lock goes
                    release.Join();
                }
            }
        }

        [Fact]
        public void Atomic_write_replaces_and_leaves_no_temp_files()
        {
            using (var dir = new TempDir())
            {
                var path = Path.Combine(dir.Path, "sub", "file.json");
                AtomicFile.WriteAllText(path, "one");
                AtomicFile.WriteAllText(path, "two");

                Assert.Equal("two", File.ReadAllText(path));
                Assert.Equal(new[] { "file.json" }, Directory.GetFiles(Path.GetDirectoryName(path)).Select(Path.GetFileName).ToArray());
            }
        }

        [Fact]
        public void Folder_resolution_prefers_the_remembered_folder_then_next_to_the_model()
        {
            using (var dir = new TempDir())
            {
                var model = Path.Combine(dir.Path, "Project", "Model.rvt");
                var nextTo = Path.Combine(dir.Path, "Project", "_markup");
                var chosen = Path.Combine(dir.Path, "Elsewhere", "_markup");

                Assert.Null(MarkupFolder.Resolve(null, model));            // nothing yet: ask the user
                Directory.CreateDirectory(nextTo);
                Assert.Equal(nextTo, MarkupFolder.Resolve(null, model));
                Assert.Equal(nextTo, MarkupFolder.Resolve(chosen, model)); // remembered folder gone: fall back
                Directory.CreateDirectory(chosen);
                Assert.Equal(chosen, MarkupFolder.Resolve(chosen, model));

                Assert.Null(MarkupFolder.NextToModel(""));
                Assert.Null(MarkupFolder.NextToModel("Model.rvt"));
                Assert.Equal(chosen, MarkupFolder.FromPickedFolder(chosen));
                Assert.Equal(chosen, MarkupFolder.FromPickedFolder(Path.Combine(dir.Path, "Elsewhere")));
            }
        }

        [Fact]
        public void Local_settings_store_defaults_and_round_trips()
        {
            using (var dir = new TempDir())
            {
                var store = new LocalSettingsStore(dir.Path);
                Assert.True(store.Load("guid-1").OverlayVisible);

                store.Save("guid-1", new LocalSettings { OverlayVisible = false, MarkupFolder = @"D:\x\_markup" });
                Assert.False(store.Load("guid-1").OverlayVisible);
                Assert.True(store.Load("guid-2").OverlayVisible);

                File.WriteAllText(store.PathFor("guid-3"), "{ broken");
                Assert.NotNull(store.Load("guid-3"));
            }
        }

        [Fact]
        public void Watcher_reports_a_burst_of_changes_once()
        {
            using (var dir = new TempDir())
            {
                var folder = new MarkupFolder(dir.Path);
                var store = new IssueStore(folder);
                var count = 0;
                using (var watcher = new MarkupWatcher(folder, TimeSpan.FromMilliseconds(300)))
                using (var signal = new ManualResetEventSlim())
                {
                    watcher.Changed += (s, e) => { Interlocked.Increment(ref count); signal.Set(); };

                    for (var i = 0; i < 5; i++)
                    {
                        var issue = SerializationTests.FullIssue();
                        issue.Id = "20261014-0930" + i.ToString("00") + "-abcdef";
                        store.Save(issue);
                    }

                    Assert.True(signal.Wait(TimeSpan.FromSeconds(10)), "no change event");
                    Thread.Sleep(800);
                    Assert.Equal(1, count);
                }
            }
        }
    }
}
