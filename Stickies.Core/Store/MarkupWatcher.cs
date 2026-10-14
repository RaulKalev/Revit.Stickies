using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Stickies.Core.Store
{
    /// <summary>
    /// Watches <c>active/</c> and <c>archive/</c> and raises <see cref="Changed"/> once a burst of file changes has been
    /// quiet for the debounce time (500 ms by default). The archive is watched because another session's archiving
    /// deletes active files and writes archive files, and Dropbox may deliver the two at different times.
    /// <see cref="Changed"/> is raised on a thread-pool thread: listeners must marshal to their own thread and must not
    /// call the Revit API from it.
    /// </summary>
    public sealed class MarkupWatcher : IDisposable
    {
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private readonly Timer _timer;
        private readonly TimeSpan _debounce;
        private int _disposed;

        public MarkupWatcher(MarkupFolder folder, TimeSpan? debounce = null)
        {
            if (folder == null) throw new ArgumentNullException(nameof(folder));
            _debounce = debounce ?? TimeSpan.FromMilliseconds(500);
            _timer = new Timer(_ => Raise(), null, Timeout.Infinite, Timeout.Infinite);

            foreach (var dir in new[] { folder.ActiveDir, folder.ArchiveDir })
            {
                Directory.CreateDirectory(dir);
                var watcher = new FileSystemWatcher(dir)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                };
                watcher.Created += OnFileEvent;
                watcher.Changed += OnFileEvent;
                watcher.Deleted += OnFileEvent;
                watcher.Renamed += OnFileEvent;
                watcher.Error += (s, e) => Schedule(); // e.g. buffer overflow: a full reload catches up
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
        }

        public event EventHandler Changed;

        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            // Temporary files of atomic writes come and go; the rename to .json is what matters.
            if (e.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                (e is RenamedEventArgs r && r.OldFullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                Schedule();
        }

        private void Schedule()
        {
            if (_disposed != 0) return;
            try { _timer.Change(_debounce, Timeout.InfiniteTimeSpan); } catch (ObjectDisposedException) { }
        }

        private void Raise()
        {
            if (_disposed == 0) Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _timer.Dispose();
        }
    }
}
