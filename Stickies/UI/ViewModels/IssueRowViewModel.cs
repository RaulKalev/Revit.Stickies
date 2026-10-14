using System;
using System.Collections.Generic;
using Stickies.Core.Models;
using Stickies.Session;
using Stickies.UI.Mvvm;

namespace Stickies.UI.ViewModels
{
    /// <summary>One row of the issue list.</summary>
    public sealed class IssueRowViewModel : ObservableObject
    {
        private TargetState _target;

        public IssueRowViewModel(Issue issue, DateTimeOffset now)
        {
            Issue = issue;
            Now = now;
        }

        public Issue Issue { get; }
        private DateTimeOffset Now { get; }

        public string Id => Issue.Id;
        public string NumberText => Issue.Number.HasValue ? "#" + Issue.Number.Value : null;
        public string Title => string.IsNullOrWhiteSpace(Issue.Title) ? "(untitled)" : Issue.Title;
        public IssueStatus Status => Issue.Status;
        public string StatusText => Display.Status(Issue.Status);
        public IssuePriority Priority => Issue.Priority;
        public bool IsHighPriority => Issue.Priority == IssuePriority.High;
        public bool IsLowPriority => Issue.Priority == IssuePriority.Low;
        public bool IsDone => Issue.IsDone;
        public int CommentCount => Issue.Comments?.Count ?? 0;
        public bool HasComments => CommentCount > 0;

        /// <summary>"joonas · 2 h ago" (assignee, else author).</summary>
        public string Meta
        {
            get
            {
                var parts = new List<string>();
                parts.Add(string.IsNullOrWhiteSpace(Issue.Assignee) ? "by " + Issue.Author : "→ " + Issue.Assignee);
                parts.Add(Display.When(Issue.Modified, Now));
                return string.Join(" · ", parts);
            }
        }

        public string ToolTip =>
            StatusText + " · " + Display.Priority(Issue.Priority) + " priority" + Environment.NewLine +
            "Created by " + Issue.Author + ", " + Display.Exact(Issue.Created) + Environment.NewLine +
            "Changed " + Display.Exact(Issue.Modified) + Environment.NewLine +
            (Issue.IsArchived ? "Archived (done and unchanged for over a week)" + Environment.NewLine : "") +
            (Issue.View != null ? "In " + Issue.View.Name + Environment.NewLine : "") +
            "Double-click to open the view";

        public TargetState Target
        {
            get => _target;
            set { if (Set(ref _target, value)) OnPropertiesChanged(); }
        }

        public bool HasTargetProblem => _target != TargetState.Ok;

        public string TargetText =>
            _target.HasFlag(TargetState.ViewMissing) ? "View not found" :
            _target.HasFlag(TargetState.ElementMissing) ? "Element not found" : null;

        private void OnPropertiesChanged()
        {
            OnPropertyChanged(nameof(HasTargetProblem));
            OnPropertyChanged(nameof(TargetText));
        }
    }
}
