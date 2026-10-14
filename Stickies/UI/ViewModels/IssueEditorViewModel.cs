using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Stickies.Core.Issues;
using Stickies.Core.Models;
using Stickies.Core.Store;
using Stickies.Infrastructure;
using Stickies.Session;
using Stickies.UI.Mvvm;

namespace Stickies.UI.ViewModels
{
    public sealed class StatusActionViewModel
    {
        public StatusActionViewModel(string text, IssueStatus status, ICommand command, bool isPrimary)
        {
            Text = text;
            Status = status;
            Command = command;
            IsPrimary = isPrimary;
        }

        public string Text { get; }
        public IssueStatus Status { get; }
        public ICommand Command { get; }
        public bool IsPrimary { get; }
    }

    public sealed class CommentViewModel
    {
        public CommentViewModel(IssueComment comment, DateTimeOffset now)
        {
            Author = comment.Author;
            Text = comment.Text;
            When = Display.When(comment.At, now);
            Exact = Display.Exact(comment.At);
        }

        public string Author { get; }
        public string Text { get; }
        public string When { get; }
        public string Exact { get; }
    }

    /// <summary>
    /// The selected (or new) issue: fields to edit, status actions and the comment thread. Every change is applied to a
    /// fresh copy of the stored issue and saved with an overwrite guard, so a colleague's edit is never lost.
    /// </summary>
    public sealed class IssueEditorViewModel : ObservableObject
    {
        private readonly MarkupSession _session;
        private readonly IRevitHost _host;
        private readonly string _me;
        private readonly Func<DateTimeOffset> _clock;
        private readonly Action<IssueEditorViewModel> _saved;

        private Issue _original;
        private bool _isNew;
        private string _title, _body, _assignee, _newComment;
        private IssuePriority _priority;
        private bool _isBusy;
        private string _banner;
        private bool _bannerIsError, _hasConflict;
        private TargetState _target;

        public IssueEditorViewModel(Issue issue, bool isNew, MarkupSession session, IRevitHost host, string me,
            Func<DateTimeOffset> clock, Action<IssueEditorViewModel> saved)
        {
            _session = session;
            _host = host;
            _me = me;
            _clock = clock;
            _saved = saved;
            _original = issue;
            _isNew = isNew;

            SaveCommand = new RelayCommand(() => _ = SaveAsync(), () => CanSave);
            RevertCommand = new RelayCommand(Revert, () => IsDirty && !_isBusy);
            AddCommentCommand = new RelayCommand(() => _ = AddCommentAsync(), () => !_isNew && !_isBusy && !string.IsNullOrWhiteSpace(_newComment));
            OpenViewCommand = new RelayCommand(OpenView, () => _original.View != null && !_isBusy);
            ReloadCommand = new RelayCommand(() => _ = ReloadAsync(), () => !_isNew && !_isBusy);
            SetStatusCommand = new RelayCommand(p => _ = SetStatusAsync((IssueStatus)p), p => p is IssueStatus && !_isNew && !_isBusy);
            LoadFields();
        }

        public ICommand SaveCommand { get; }
        public ICommand RevertCommand { get; }
        public ICommand AddCommentCommand { get; }
        public ICommand OpenViewCommand { get; }
        public ICommand ReloadCommand { get; }
        public ICommand SetStatusCommand { get; }

        public Issue Original => _original;
        public string Id => _original.Id;
        public bool IsNew => _isNew;
        public string Heading => (_original.Number.HasValue ? "#" + _original.Number.Value + " · " : "") + (_isNew ? "New issue" : Display.Status(_original.Status));
        public IssueStatus Status => _original.Status;

        // ------------------------------------------------------------------ fields

        public string Title { get => _title; set { if (Set(ref _title, value)) FieldsChanged(); } }
        public string Body { get => _body; set { if (Set(ref _body, value)) FieldsChanged(); } }
        public string Assignee { get => _assignee; set { if (Set(ref _assignee, value)) FieldsChanged(); } }

        public IssuePriority Priority
        {
            get => _priority;
            set
            {
                if (!Set(ref _priority, value)) return;
                OnPropertyChanged(nameof(IsLow));
                OnPropertyChanged(nameof(IsNormal));
                OnPropertyChanged(nameof(IsHigh));
                FieldsChanged();
            }
        }

        public bool IsLow { get => _priority == IssuePriority.Low; set { if (value) Priority = IssuePriority.Low; } }
        public bool IsNormal { get => _priority == IssuePriority.Normal; set { if (value) Priority = IssuePriority.Normal; } }
        public bool IsHigh { get => _priority == IssuePriority.High; set { if (value) Priority = IssuePriority.High; } }

        public bool IsDirty =>
            _isNew ||
            Norm(_title) != Norm(_original.Title) || Norm(_body) != Norm(_original.Body) ||
            Norm(_assignee) != Norm(_original.Assignee) || _priority != _original.Priority;

        public bool CanSave => !_isBusy && IsDirty && !string.IsNullOrWhiteSpace(_title);
        public string SaveText => _isNew ? "Add issue" : "Save";
        public string RevertText => _isNew ? "Discard" : "Undo changes";
        public bool ShowSaveBar => IsDirty;

        public void AssignTo(string person) => Assignee = person;

        // ------------------------------------------------------------------ read-only details

        public string CreatedText => "Created by " + _original.Author + " · " + Display.When(_original.Created, _clock());
        public string CreatedExact => Display.Exact(_original.Created);
        public string ResolvedText => _original.ResolvedAt.HasValue
            ? (_original.Status == IssueStatus.Closed ? "Closed by " : "Resolved by ") + _original.ResolvedBy + " · " + Display.When(_original.ResolvedAt.Value, _clock())
            : null;

        public string ViewText => _original.View == null ? null : _original.View.Name + " · " + Display.ViewType(_original.View.Type);
        public string ElementText => _original.Location?.ElementCategory;

        public IReadOnlyList<StatusActionViewModel> StatusActions =>
            _isNew ? new List<StatusActionViewModel>() :
            IssueWorkflow.NextStatuses(_original, _me)
                .Select((s, i) => new StatusActionViewModel(Display.StatusAction(_original.Status, s), s, SetStatusCommand, i == 0))
                .ToList();

        public string CloseHint => !_isNew && _original.Status == IssueStatus.Resolved && !IssueWorkflow.CanClose(_original, _me)
            ? _original.Author + " closes this issue once the fix is checked."
            : null;

        public ObservableCollection<CommentViewModel> Comments { get; } = new ObservableCollection<CommentViewModel>();
        public bool HasComments => Comments.Count > 0;
        public string NewComment { get => _newComment; set => Set(ref _newComment, value); }

        public TargetState Target
        {
            get => _target;
            set
            {
                if (!Set(ref _target, value)) return;
                OnPropertyChanged(nameof(TargetText));
            }
        }

        public string TargetText =>
            _target.HasFlag(TargetState.ViewMissing) ? "The saved view no longer exists." :
            _target.HasFlag(TargetState.ElementMissing) ? "The element this issue is about no longer exists." : null;

        public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

        /// <summary>Message above the form (conflict, error or notice).</summary>
        public string Banner { get => _banner; private set => Set(ref _banner, value); }
        public bool BannerIsError { get => _bannerIsError; private set => Set(ref _bannerIsError, value); }
        public bool HasConflict { get => _hasConflict; private set => Set(ref _hasConflict, value); }

        // ------------------------------------------------------------------ actions

        private async Task SaveAsync()
        {
            var wasNew = _isNew;
            await CommitAsync(i => IssueWorkflow.Edit(i, _title, _body, _priority, _assignee, _clock()));
            if (wasNew && !_isNew) _saved?.Invoke(this);
        }

        private Task SetStatusAsync(IssueStatus status) => CommitAsync(i => IssueWorkflow.SetStatus(i, status, _me, _clock()));

        private async Task AddCommentAsync()
        {
            var text = _newComment;
            if (await CommitAsync(i => IssueWorkflow.AddComment(i, _me, text, _clock()))) NewComment = null;
        }

        /// <summary>Applies one change to a copy of the stored issue and saves it. Returns true when saved.</summary>
        private async Task<bool> CommitAsync(Action<Issue> change)
        {
            var working = IssueWorkflow.Clone(_original);
            try { change(working); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                ShowBanner(ex.Message, true);
                return false;
            }

            IsBusy = true;
            try
            {
                var outcome = await _session.SaveAsync(working, _isNew ? (DateTimeOffset?)null : _original.Modified);
                if (outcome == SaveOutcome.Conflict)
                {
                    ShowBanner("Someone else changed this issue after you opened it, so this change was not saved. Reload to see their version.", true);
                    HasConflict = true;
                    return false;
                }

                var keepTyped = !_isNew && IsDirty;
                _original = working;
                _isNew = false;
                ClearBanner();
                if (keepTyped) RefreshDetails(); // unsaved field edits stay in the form
                else LoadFields();
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("Saving issue " + working.Id + " failed", ex);
                ShowBanner("Could not save: " + ex.Message, true);
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void Revert()
        {
            if (_isNew) { _saved?.Invoke(null); return; } // discard the new issue
            LoadFields();
        }

        private async Task ReloadAsync()
        {
            IsBusy = true;
            try
            {
                var latest = await _session.LoadLatestAsync(_original.Id);
                if (latest == null) { ShowBanner("This issue is no longer in the active folder.", true); return; }
                _original = latest;
                ClearBanner();
                LoadFields();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OpenView()
        {
            _host.OpenView(_original, result =>
            {
                Target = result.Target;
                if (result.Message != null) ShowBanner(result.Message, !result.Opened);
                else if (Banner != null && !HasConflict) ClearBanner();
            });
        }

        /// <summary>The list was reloaded: follow the stored version unless the user is editing.</summary>
        public void OnStoredChanged(Issue latest)
        {
            if (_isNew || _isBusy) return;
            if (latest == null)
            {
                ShowBanner("This issue is no longer in the active folder.", false);
                return;
            }
            if (Timestamp.SameSecond(latest.Modified, _original.Modified)) return;
            if (IsDirty)
            {
                ShowBanner("Someone else changed this issue while you were editing. Reload to see their version.", false);
                HasConflict = true;
                return;
            }
            _original = latest;
            LoadFields();
        }

        // ------------------------------------------------------------------ helpers

        private void LoadFields()
        {
            _title = _original.Title;
            _body = _original.Body;
            _assignee = _original.Assignee;
            _priority = _original.Priority;
            RefreshDetails();
        }

        private void RefreshDetails()
        {
            Comments.Clear();
            var now = _clock();
            foreach (var c in _original.Comments ?? new List<IssueComment>()) Comments.Add(new CommentViewModel(c, now));
            RefreshAll();
        }

        private void FieldsChanged()
        {
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(ShowSaveBar));
        }

        private void ShowBanner(string message, bool isError)
        {
            Banner = message;
            BannerIsError = isError;
        }

        private void ClearBanner()
        {
            Banner = null;
            BannerIsError = false;
            HasConflict = false;
        }

        private static string Norm(string s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim();
    }
}
