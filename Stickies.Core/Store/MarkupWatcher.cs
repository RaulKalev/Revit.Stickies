using System;
using System.IO;
using System.Threading;

namespace Stickies.Core.Store
{
    /// <summary>
    /// Watches <c>active/</c> and raises <see cref="Changed"/> once a burst of file changes has been quiet for the
    /// debounce time (500 ms by default). <see cref="Changed"/> is raised on a thread-pool thread: listeners must
    /// marshal to their own thread and must not call the Revit API from it.
    /// </summary>
    public sealed class MarkupWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly Timer _timer;
        private readonly TimeSpan _debounce;
        private int _disposed;

        public MarkupWatcher(MarkupFolder folder, TimeSpan? debounce = null)
        {
            if (folder == null) throw new ArgumentNullException(nameof(folder));
            _debounce = debounce ?? TimeSpan.FromMilliseconds(500);
            _timer = new Timer(_ => Raise(), null, Timeout.Infinite, Timeout.Infinite);

            Directory.CreateDirectory(folder.ActiveDir);
            _watcher = new FileSystemWatcher(folder.ActiveDir)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _watcher.Created += OnFileEvent;
            _watcher.Changed += OnFileEvent;
            _watcher.Deleted += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            _watcher.Error += (s, e) => Schedule(); // e.g. buffer overflow: a full reload catches up
            _watcher.EnableRaisingEvents = true;
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
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _timer.Dispose();
        }
    }
}
