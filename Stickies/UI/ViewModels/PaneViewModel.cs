using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Stickies.Core.Issues;
using Stickies.Core.Models;
using Stickies.Core.Overlay;
using Stickies.Session;
using Stickies.UI.Mvvm;

namespace Stickies.UI.ViewModels
{
    /// <summary>A labelled choice for combo boxes and segmented controls.</summary>
    public sealed class Choice
    {
        public Choice(string value, string text)
        {
            Value = value;
            Text = text;
        }

        public string Value { get; }
        public string Text { get; }
        public override string ToString() => Text;
    }

    /// <summary>
    /// The issue panel: filter bar, sorted list and the selected issue. Filter and sort are this user's local settings
    /// for the project. Talks to Revit only through <see cref="IRevitHost"/>.
    /// </summary>
    public sealed class PaneViewModel : ObservableObject
    {
        public const string Anyone = "";
        private const string ScopeActive = "active", ScopeResolved = "resolved", ScopeClosed = "closed", ScopeAll = "all";

        private readonly IRevitHost _host;
        private readonly IMarkerSink _markers;
        private readonly Func<DateTimeOffset> _clock;
        private IssueRowViewModel _selected;
        private IssueEditorViewModel _editor;
        private IReadOnlyDictionary<string, TargetState> _targets = new Dictionary<string, TargetState>();
        private bool _loadingSettings, _isCapturing, _rebuilding;
        private string _feedback;

        private string _search, _scope = ScopeAll, _author = Anyone, _assignee = Anyone, _sortColumn = IssueQuery.SortModified;
        private bool _sortDescending = true, _high, _normal, _low;

        public PaneViewModel(MarkupSession session, IRevitHost host, string me, IMarkerSink markers = null, Func<DateTimeOffset> clock = null)
        {
            Session = session;
            _host = host;
            _markers = markers;
            Me = me;
            _clock = clock ?? (() => DateTimeOffset.Now);

            AddIssueCommand = new RelayCommand(AddIssue, () => Session.IsConnected && !_isCapturing && (_editor == null || !_editor.IsNew));
            OpenViewCommand = new RelayCommand(p => OpenView(p as IssueRowViewModel ?? _selected), p => (p as IssueRowViewModel ?? _selected) != null);
            ClearFiltersCommand = new RelayCommand(ClearFilters, () => ActiveFilterCount > 0 || !string.IsNullOrEmpty(_search));
            CloseEditorCommand = new RelayCommand(() => Selected = null);
            ToggleSortDirectionCommand = new RelayCommand(() => SortDescending = !SortDescending);

            Session.PropertyChanged += OnSessionPropertyChanged;
            Session.IssuesChanged += (s, e) => RebuildRows(checkTargets: true);
            if (_markers != null)
            {
                _markers.MarkerClicked += SelectIssue;
                _markers.MarkerDoubleClicked += OpenIssueView;
            }
            LoadSettings();
        }

        public MarkupSession Session { get; }
        public string Me { get; }

        public ICommand AddIssueCommand { get; }
        public ICommand OpenViewCommand { get; }
        public ICommand ClearFiltersCommand { get; }
        public ICommand CloseEditorCommand { get; }
        public ICommand ToggleSortDirectionCommand { get; }

        public ObservableCollection<IssueRowViewModel> Rows { get; } = new ObservableCollection<IssueRowViewModel>();

        public bool HasRows => Rows.Count > 0;
        public bool ShowNoIssues => Session.IsConnected && Session.IssueCount == 0;
        public bool ShowNoMatches => Session.IsConnected && Session.IssueCount > 0 && Rows.Count == 0;
        public string CountText => Rows.Count == Session.IssueCount
            ? (Rows.Count == 1 ? "1 issue" : Rows.Count + " issues")
            : Rows.Count + " of " + Session.IssueCount;

        // ------------------------------------------------------------------ selection and editor

        public IssueRowViewModel Selected
        {
            get => _selected;
            set
            {
                // Rebuilding the list clears it, and the list box then reports "nothing selected": not the user's choice.
                if (_rebuilding) return;
                if (!Set(ref _selected, value)) return;
                Feedback = null; // a new choice: the last message no longer applies
                if (value == null)
                {
                    if (_editor != null && !_editor.IsNew) Editor = null;
                }
                else if (_editor == null || _editor.Id != value.Id)
                {
                    Editor = NewEditor(value.Issue, false);
                    Editor.Target = value.Target;
                }
            }
        }

        public IssueEditorViewModel Editor
        {
            get => _editor;
            private set
            {
                if (!Set(ref _editor, value)) return;
                OnPropertyChanged(nameof(HasEditor));
                PublishMarkers(); // the open issue's marker is highlighted
            }
        }

        public bool HasEditor => _editor != null;

        /// <summary>One line under the list: what just happened (capture prompt, errors).</summary>
        public string Feedback { get => _feedback; private set => Set(ref _feedback, value); }

        public bool IsCapturing { get => _isCapturing; private set => Set(ref _isCapturing, value); }

        private IssueEditorViewModel NewEditor(Issue issue, bool isNew) =>
            new IssueEditorViewModel(issue, isNew, Session, _host, Me, _clock, OnEditorSaved);

        private void OnEditorSaved(IssueEditorViewModel editor)
        {
            if (editor == null) { Editor = null; return; } // a new issue was discarded
            RebuildRows(checkTargets: false);
            var row = Rows.FirstOrDefault(r => r.Id == editor.Id);
            _selected = row;
            OnPropertyChanged(nameof(Selected));
            Feedback = row == null ? "Issue added (hidden by the current filter)." : null;
        }

        // ------------------------------------------------------------------ add / open

        private void AddIssue()
        {
            IsCapturing = true;
            Feedback = "Click the spot for the new issue in the view (Esc cancels).";
            _host.CaptureNewIssue(result =>
            {
                IsCapturing = false;
                Feedback = result.Error;
                if (result.Cancelled || result.Error != null) return;
                _selected = null;
                OnPropertyChanged(nameof(Selected));
                var issue = IssueWorkflow.Create(Me, _clock(), result.Location, result.View);
                issue.Number = IssueNumbers.Next(Session.Issues);
                Editor = NewEditor(issue, true);
            });
        }

        // ------------------------------------------------------------------ markers

        /// <summary>The issues in the list (so the user's filter applies) that have a location, drawn as markers.</summary>
        private void PublishMarkers()
        {
            if (_markers == null) return;
            var openId = _editor?.Id;
            var markers = Session.IsConnected
                ? Rows.Where(r => r.Issue.Location?.Point != null && r.Issue.Location.Point.Length == 3)
                      .Select(r => new MarkerInfo(r.Id, r.Issue.Number, r.Issue.Location.Point, r.Issue.View?.UniqueId, r.Status, r.Id == openId,
                          r.Issue.Title, r.Issue.Body, r.Issue.Assignee, r.Issue.Priority, r.Issue.View?.Type))
                      .ToList()
                : new List<MarkerInfo>();
            _markers.Publish(markers);
        }

        /// <summary>A marker was clicked in a view: select its issue.</summary>
        public void SelectIssue(string id)
        {
            var row = Rows.FirstOrDefault(r => r.Id == id);
            if (row == null || (_editor != null && _editor.IsNew)) return;
            Selected = row;
        }

        /// <summary>A marker was double-clicked: open its view as it was when the issue was added.</summary>
        public void OpenIssueView(string id)
        {
            var row = Rows.FirstOrDefault(r => r.Id == id);
            if (row != null && (_editor == null || !_editor.IsNew)) OpenView(row);
        }

        public void OpenView(IssueRowViewModel row)
        {
            if (row == null) return;
            Selected = row;
            _host.OpenView(row.Issue, result =>
            {
                row.Target = result.Target;
                if (_editor != null && _editor.Id == row.Id) _editor.Target = result.Target;
                Feedback = result.Message;
            });
        }

        // ------------------------------------------------------------------ filter bar

        public IReadOnlyList<Choice> Scopes { get; } = new[]
        {
            new Choice(ScopeActive, "Active"), new Choice(ScopeResolved, "Resolved"),
            new Choice(ScopeClosed, "Closed"), new Choice(ScopeAll, "All")
        };

        public string Scope { get => _scope; set { if (Set(ref _scope, value ?? ScopeAll)) FilterChanged(); } }

        public string SearchText { get => _search; set { if (Set(ref _search, value)) FilterChanged(); } }

        public bool PriorityHigh { get => _high; set { if (Set(ref _high, value)) FilterChanged(); } }
        public bool PriorityNormal { get => _normal; set { if (Set(ref _normal, value)) FilterChanged(); } }
        public bool PriorityLow { get => _low; set { if (Set(ref _low, value)) FilterChanged(); } }

        public string Author
        {
            get => _author;
            set
            {
                if ((value ?? Anyone) == _author) return;
                _author = value ?? Anyone;
                UpdatePeopleChoices(reset: false); // the choice must exist before the combo box sees the value
                OnPropertyChanged();
                FilterChanged();
            }
        }

        public string AssigneeFilter
        {
            get => _assignee;
            set
            {
                if ((value ?? Anyone) == _assignee) return;
                _assignee = value ?? Anyone;
                UpdatePeopleChoices(reset: false);
                OnPropertyChanged();
                FilterChanged();
            }
        }

        /// <summary>Everyone seen in this project's issues, plus the current user.</summary>
        public IReadOnlyList<string> People =>
            Session.Issues.SelectMany(i => new[] { i.Author, i.Assignee }.Concat((i.Comments ?? new List<IssueComment>()).Select(c => c.Author)))
                .Concat(new[] { Me })
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        /// <summary>
        /// Author / assignee filter choices. One collection that only grows (until another project is opened): replacing
        /// it would make the bound combo boxes write "nothing selected" back and silently clear the filter.
        /// </summary>
        public ObservableCollection<Choice> PeopleChoices { get; } = new ObservableCollection<Choice> { new Choice(Anyone, "Anyone") };

        private void UpdatePeopleChoices(bool reset)
        {
            if (reset)
                while (PeopleChoices.Count > 1) PeopleChoices.RemoveAt(PeopleChoices.Count - 1);

            // Keep people a saved filter names, even when no loaded issue mentions them any more.
            var wanted = People.Concat(new[] { _author, _assignee }.Where(p => !string.IsNullOrWhiteSpace(p)));
            foreach (var person in wanted)
            {
                if (PeopleChoices.Any(c => IssueQuery.SameUser(c.Value, person))) continue;
                var choice = new Choice(person, IssueQuery.SameUser(person, Me) ? person + " (me)" : person);
                var index = 1;
                while (index < PeopleChoices.Count && string.Compare(PeopleChoices[index].Value, person, StringComparison.CurrentCultureIgnoreCase) < 0) index++;
                PeopleChoices.Insert(index, choice);
            }
        }

        /// <summary>Filters set in the Filters popover (priority, author, assignee); the scope and search are visible.</summary>
        public int ActiveFilterCount =>
            (_high || _normal || _low ? 1 : 0) + (string.IsNullOrEmpty(_author) ? 0 : 1) + (string.IsNullOrEmpty(_assignee) ? 0 : 1);

        public bool HasActiveFilters => ActiveFilterCount > 0;

        public IReadOnlyList<Choice> SortColumns { get; } = new[]
        {
            new Choice(IssueQuery.SortModified, "Last change"), new Choice(IssueQuery.SortCreated, "Created"),
            new Choice(IssueQuery.SortPriority, "Priority"), new Choice(IssueQuery.SortStatus, "Status"),
            new Choice(IssueQuery.SortTitle, "Title"), new Choice(IssueQuery.SortAssignee, "Assignee")
        };

        public string SortColumn { get => _sortColumn; set { if (Set(ref _sortColumn, value ?? IssueQuery.SortModified)) FilterChanged(); } }

        public bool SortDescending
        {
            get => _sortDescending;
            set { if (Set(ref _sortDescending, value)) { OnPropertyChanged(nameof(SortDirectionText)); FilterChanged(); } }
        }

        public string SortDirectionText => _sortDescending ? "Descending (click for ascending)" : "Ascending (click for descending)";

        private void ClearFilters()
        {
            _loadingSettings = true;
            SearchText = null;
            PriorityHigh = PriorityNormal = PriorityLow = false;
            Author = Anyone;
            AssigneeFilter = Anyone;
            _loadingSettings = false;
            FilterChanged();
        }

        private IssueFilter BuildFilter()
        {
            var filter = new IssueFilter { Text = _search, Author = NullIfEmpty(_author), Assignee = NullIfEmpty(_assignee) };
            switch (_scope)
            {
                case ScopeActive: filter.Statuses.AddRange(new[] { IssueStatus.Open, IssueStatus.InProgress }); break;
                case ScopeResolved: filter.Statuses.Add(IssueStatus.Resolved); break;
                case ScopeClosed: filter.Statuses.Add(IssueStatus.Closed); break;
            }
            if (_high) filter.Priorities.Add(IssuePriority.High);
            if (_normal) filter.Priorities.Add(IssuePriority.Normal);
            if (_low) filter.Priorities.Add(IssuePriority.Low);
            return filter;
        }

        private void FilterChanged()
        {
            if (_loadingSettings) return;
            Feedback = null;
            OnPropertyChanged(nameof(ActiveFilterCount));
            OnPropertyChanged(nameof(HasActiveFilters));
            SaveSettings();
            RebuildRows(checkTargets: false);
        }

        // ------------------------------------------------------------------ list

        private void RebuildRows(bool checkTargets)
        {
            var now = _clock();
            var shown = IssueQuery.Apply(Session.Issues, BuildFilter(), new IssueSort { Column = _sortColumn, Descending = _sortDescending });
            var selectedId = _selected?.Id;

            _rebuilding = true;
            try
            {
                Rows.Clear();
                foreach (var issue in shown)
                {
                    var row = new IssueRowViewModel(issue, now);
                    if (_targets.TryGetValue(issue.Id, out var t)) row.Target = t;
                    Rows.Add(row);
                }
            }
            finally
            {
                _rebuilding = false;
            }

            _selected = selectedId == null ? null : Rows.FirstOrDefault(r => r.Id == selectedId);
            OnPropertyChanged(nameof(Selected));

            if (_editor != null && !_editor.IsNew)
                _editor.OnStoredChanged(Session.Issues.FirstOrDefault(i => i.Id == _editor.Id));

            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(ShowNoIssues));
            OnPropertyChanged(nameof(ShowNoMatches));
            OnPropertyChanged(nameof(CountText));
            PublishMarkers();
            OnPropertyChanged(nameof(People));
            UpdatePeopleChoices(reset: false);

            if (checkTargets && Session.IsConnected && Session.Issues.Count > 0)
            {
                _host.CheckTargets(Session.Issues, states =>
                {
                    _targets = states;
                    foreach (var row in Rows) row.Target = states.TryGetValue(row.Id, out var s) ? s : TargetState.Ok;
                    if (_editor != null && states.TryGetValue(_editor.Id, out var es)) _editor.Target = es;
                });
            }
        }

        // ------------------------------------------------------------------ settings and session

        private string _context;

        private void OnSessionPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Another model, folder or state: start clean with that project's filter and sort.
            var context = Session.State + "|" + Session.Model?.DocumentKey + "|" + Session.FolderPath;
            if (context != _context)
            {
                _context = context;
                Editor = null;
                _selected = null;
                _targets = new Dictionary<string, TargetState>();
                Feedback = null;
                UpdatePeopleChoices(reset: true);
                LoadSettings();
                RebuildRows(checkTargets: false);
            }
            OnPropertyChanged(nameof(ShowNoIssues));
            OnPropertyChanged(nameof(ShowNoMatches));
        }

        private void LoadSettings()
        {
            var settings = Session.Settings;
            _loadingSettings = true;
            try
            {
                var f = settings?.Filter ?? new IssueFilter();
                SearchText = f.Text;
                Scope = ScopeOf(f.Statuses);
                PriorityHigh = f.Priorities.Contains(IssuePriority.High);
                PriorityNormal = f.Priorities.Contains(IssuePriority.Normal);
                PriorityLow = f.Priorities.Contains(IssuePriority.Low);
                Author = f.Author ?? Anyone;
                AssigneeFilter = f.Assignee ?? Anyone;
                UpdatePeopleChoices(reset: false);
                SortColumn = settings?.Sort?.Column ?? IssueQuery.SortModified;
                SortDescending = settings?.Sort?.Descending ?? true;
            }
            finally
            {
                _loadingSettings = false;
            }
            OnPropertyChanged(nameof(ActiveFilterCount));
            OnPropertyChanged(nameof(HasActiveFilters));
        }

        private void SaveSettings()
        {
            var settings = Session.Settings;
            if (settings == null) return;
            var filter = BuildFilter();
            settings.Filter = filter;
            settings.Sort = new IssueSort { Column = _sortColumn, Descending = _sortDescending };
            Session.SaveSettings();
        }

        private static string ScopeOf(List<IssueStatus> statuses)
        {
            if (statuses == null || statuses.Count == 0) return ScopeAll;
            if (statuses.Count == 1 && statuses[0] == IssueStatus.Resolved) return ScopeResolved;
            if (statuses.Count == 1 && statuses[0] == IssueStatus.Closed) return ScopeClosed;
            return ScopeActive;
        }

        private static string NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
