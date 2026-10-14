using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Stickies.Core.Models;
using Stickies.Core.Store;
using Stickies.Infrastructure;
using Stickies.Overlay;
using Stickies.UI.Mvvm;

namespace Stickies.Session
{
    public enum SessionState
    {
        /// <summary>No project document is active.</summary>
        NoModel,

        /// <summary>A family document is active.</summary>
        Family,

        /// <summary>No <c>_stickies</c> folder found: the user creates or chooses one (asked once, then remembered).</summary>
        NeedsFolder,

        Connected,

        Error
    }

    /// <summary>
    /// The active model's markup: where its folder is, its issues (reloaded when anyone's file changes) and this user's
    /// local settings for it. No Revit API here – <see cref="Revit.DocumentTracker"/> feeds it plain data. File work
    /// runs off the UI thread; results and <see cref="Changed"/> arrive on the UI thread.
    /// </summary>
    public sealed class MarkupSession : ObservableObject, IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly LocalSettingsStore _settingsStore;
        private readonly OverlayState _overlay;
        private MarkupWatcher _watcher;
        private System.Threading.Timer _compactTimer;
        private IssueStore _store;
        private int _loadGeneration;
        private bool _applyingSettings;

        private SessionState _state = SessionState.NoModel;
        private ActiveModel _model;
        private LocalSettings _settings;
        private IReadOnlyList<Issue> _issues = new Issue[0];
        private IReadOnlyList<SkippedFile> _skipped = new SkippedFile[0];
        private string _error;

        public MarkupSession(Dispatcher dispatcher, OverlayState overlay, LocalSettingsStore settingsStore = null)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
            _settingsStore = settingsStore ?? new LocalSettingsStore();
            _overlay.Changed += OnOverlayChanged;
        }

        /// <summary>Raised on the UI thread after the issue list was reloaded.</summary>
        public event EventHandler IssuesChanged;

        public SessionState State { get => _state; private set { if (Set(ref _state, value)) RefreshAll(); } }
        public ActiveModel Model => _model;
        public IssueStore Store => _store;
        public LocalSettings Settings => _settings;
        public IReadOnlyList<Issue> Issues => _issues;
        public IReadOnlyList<SkippedFile> Skipped => _skipped;

        public string ModelTitle => _model?.Title;
        public string FolderPath => _store?.Folder.Root;
        public string ProposedFolder => MarkupFolder.NextToModel(_model?.ModelPath);
        public bool CanCreateNextToModel => ProposedFolder != null;
        public string ErrorMessage => _error;

        public bool IsNoModel => State == SessionState.NoModel;
        public bool IsFamily => State == SessionState.Family;
        public bool NeedsFolder => State == SessionState.NeedsFolder;
        public bool IsConnected => State == SessionState.Connected;
        public bool HasError => State == SessionState.Error;

        public int IssueCount => _issues.Count;
        public bool HasIssues => _issues.Count > 0;
        public string IssueSummary => _issues.Count == 1 ? "1 issue" : _issues.Count + " issues";
        public string SkippedSummary => _skipped.Count == 0 ? null
            : _skipped.Count == 1 ? "1 file was skipped (a sync conflict copy or unreadable)"
            : _skipped.Count + " files were skipped (sync conflict copies or unreadable)";
        public string SkippedDetails => string.Join(Environment.NewLine, _skipped.Select(s => Path.GetFileName(s.Path) + " – " + s.Reason));

        // ------------------------------------------------------------------ model

        /// <summary>The active document changed (null = none).</summary>
        public void SetModel(ActiveModel model)
        {
            Disconnect();
            _model = model;
            _settings = null;
            _error = null;

            if (model == null) State = SessionState.NoModel;
            else if (model.IsFamily) State = SessionState.Family;
            else if (string.IsNullOrWhiteSpace(model.ModelGuid)) Fail("This model has no project information to identify it.");
            else
            {
                _settings = _settingsStore.Load(model.ModelGuid);
                ApplyOverlaySetting();

                var root = MarkupFolder.Resolve(_settings.MarkupFolder, model.ModelPath);
                if (root != null && _settings.MarkupFolder != null &&
                    !string.Equals(root, Path.GetFullPath(_settings.MarkupFolder), StringComparison.OrdinalIgnoreCase))
                {
                    _settings.MarkupFolder = root; // an old _markup folder was renamed to _stickies
                    SaveSettings();
                }
                if (root == null) State = SessionState.NeedsFolder;
                else Connect(root);
            }
            RefreshAll(); // the state may be unchanged while the model and folder are not
        }

        /// <summary>Creates <c>_stickies</c> next to the model (central) file and uses it.</summary>
        public void CreateNextToModel()
        {
            if (!CanCreateNextToModel) return;
            UseFolder(ProposedFolder, remember: false);
        }

        /// <summary>Uses a folder the user picked (the project folder or a <c>_stickies</c> folder) and remembers it.</summary>
        public void UseFolder(string picked) => UseFolder(MarkupFolder.FromPickedFolder(picked), remember: true);

        private void UseFolder(string root, bool remember)
        {
            if (_model == null || _settings == null) return;
            try
            {
                new IssueStore(new MarkupFolder(root)).Initialize(new MarkupProject { ModelGuid = _model.ModelGuid, ProjectName = _model.Title });
                _settings.MarkupFolder = remember ? root : null;
                SaveSettings();
                Disconnect();
                Connect(root);
                RefreshAll();
            }
            catch (Exception ex)
            {
                AppLog.Error("Setting up the Stickies folder failed: " + root, ex);
                Fail("Could not set up the Stickies folder " + root + ": " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ connection

        private void Connect(string root)
        {
            try
            {
                var folder = new MarkupFolder(root);
                _store = new IssueStore(folder);
                var project = _store.ReadProject();
                if (project != null && !string.IsNullOrEmpty(project.ModelGuid) && project.ModelGuid != _model.ModelGuid)
                    AppLog.Warn("Stickies folder " + root + " was set up for another model (" + project.ModelGuid + ").");

                _watcher = new MarkupWatcher(folder);
                StartCompaction(_store);
                _watcher.Changed += (s, e) => _dispatcher.BeginInvoke(new Action(Reload));
                State = SessionState.Connected;
                Reload();
            }
            catch (Exception ex)
            {
                AppLog.Error("Opening the Stickies folder failed: " + root, ex);
                Fail("Could not open the Stickies folder " + root + ": " + ex.Message);
            }
        }

        private void Disconnect()
        {
            _loadGeneration++;
            _compactTimer?.Dispose();
            _compactTimer = null;
            _watcher?.Dispose();
            _watcher = null;
            _store = null;
            _issues = new Issue[0];
            _skipped = new SkippedFile[0];
        }

        /// <summary>Reads <c>active/</c> again (off the UI thread); the newest request wins.</summary>
        public void Reload()
        {
            var store = _store;
            if (store == null) return;
            var generation = ++_loadGeneration;
            Task.Run(() => store.LoadAll()).ContinueWith(t => _dispatcher.BeginInvoke(new Action(() =>
            {
                if (generation != _loadGeneration) return; // a newer load or another model took over
                if (t.IsFaulted)
                {
                    AppLog.Error("Loading issues failed", t.Exception?.GetBaseException());
                    return;
                }
                _issues = t.Result.Issues;
                _skipped = t.Result.Skipped;
                foreach (var s in _skipped) AppLog.Warn("Skipped " + s.Path + ": " + s.Reason);
                RefreshAll();
                IssuesChanged?.Invoke(this, EventArgs.Empty);
            })));
        }

        /// <summary>
        /// Saves an issue off the UI thread. With <paramref name="expectedModified"/> a colleague's newer edit is never
        /// overwritten (<see cref="SaveOutcome.Conflict"/>). The list shows the change at once; the watcher confirms it.
        /// </summary>
        public async Task<SaveOutcome> SaveAsync(Issue issue, DateTimeOffset? expectedModified)
        {
            var store = _store ?? throw new InvalidOperationException("No Stickies folder is connected.");
            var outcome = await Task.Run(() => store.Save(issue, expectedModified));
            if (outcome == SaveOutcome.Saved && ReferenceEquals(store, _store))
            {
                _issues = _issues.Where(i => i.Id != issue.Id).Concat(new[] { issue }).ToList();
                RefreshAll();
                IssuesChanged?.Invoke(this, EventArgs.Empty);
            }
            return outcome;
        }

        /// <summary>The stored version of an issue, read fresh from its file (null when it is gone).</summary>
        public Task<Issue> LoadLatestAsync(string id)
        {
            var store = _store;
            return store == null ? Task.FromResult<Issue>(null) : Task.Run(() => store.Load(id));
        }

        // ------------------------------------------------------------------ archiving

        /// <summary>How soon after connecting, and how often after that, done issues are archived.</summary>
        public static TimeSpan CompactFirst { get; set; } = TimeSpan.FromMinutes(1);
        public static TimeSpan CompactEvery { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Archives done issues in the background (file work only, no Revit API): a minute after connecting, then once an
        /// hour. Only one session at a time does it (see <see cref="Compactor"/>).
        /// </summary>
        private void StartCompaction(IssueStore store)
        {
            _compactTimer?.Dispose();
            _compactTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    var result = new Compactor(store, Environment.UserName).Run();
                    if (!result.Ran) AppLog.Info("Archiving skipped: " + result.SkippedReason);
                    else if (result.Archived > 0)
                    {
                        AppLog.Info("Archived " + result.Archived + " done issue(s) in " + store.Folder.ArchiveDir);
                        _dispatcher.BeginInvoke(new Action(() => { if (ReferenceEquals(store, _store)) Reload(); }));
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("Archiving failed", ex);
                }
            }, null, CompactFirst, CompactEvery);
        }

        private void Fail(string message)
        {
            _error = message;
            State = SessionState.Error;
            OnPropertyChanged(nameof(ErrorMessage));
        }

        // ------------------------------------------------------------------ local settings

        private void ApplyOverlaySetting()
        {
            _applyingSettings = true;
            try
            {
                _overlay.IsVisible = _settings.OverlayVisible;
                _overlay.ShowCards = _settings.ShowCards;
            }
            finally { _applyingSettings = false; }
        }

        private void OnOverlayChanged(object sender, EventArgs e)
        {
            if (_applyingSettings || _settings == null) return;
            _settings.OverlayVisible = _overlay.IsVisible;
            _settings.ShowCards = _overlay.ShowCards;
            SaveSettings();
        }

        public void SaveSettings()
        {
            if (_settings == null || _model?.ModelGuid == null) return;
            try { _settingsStore.Save(_model.ModelGuid, _settings); }
            catch (Exception ex) { AppLog.Error("Saving local settings failed", ex); }
        }

        public void Dispose()
        {
            _overlay.Changed -= OnOverlayChanged;
            Disconnect();
        }
    }
}
