using System;
using System.Collections.Generic;
using Stickies.Core.Models;

namespace Stickies.Core.Issues
{
    /// <summary>
    /// The rules for creating and changing issues. Every change stamps <see cref="Issue.Modified"/>.
    /// Status rule: anyone may start, resolve or reopen an issue; only its author may close it.
    /// </summary>
    public static class IssueWorkflow
    {
        /// <summary>Who may close an issue. The one place to change if closing should be open to everyone.</summary>
        public static bool CanClose(Issue issue, string user) => IssueQuery.SameUser(issue.Author, user);

        public static Issue Create(string author, DateTimeOffset now, IssueLocation location, IssueView view)
        {
            now = Timestamp.ToSecond(now);
            return new Issue
            {
                Id = IssueId.New(now),
                Author = author,
                Created = now,
                Modified = now,
                Status = IssueStatus.Open,
                Priority = IssuePriority.Normal,
                Location = location,
                View = view
            };
        }

        /// <summary>The status changes offered for an issue to this user, in display order.</summary>
        public static IReadOnlyList<IssueStatus> NextStatuses(Issue issue, string user)
        {
            var next = new List<IssueStatus>();
            switch (issue.Status)
            {
                case IssueStatus.Open:
                    next.Add(IssueStatus.InProgress);
                    next.Add(IssueStatus.Resolved);
                    break;
                case IssueStatus.InProgress:
                    next.Add(IssueStatus.Resolved);
                    break;
                case IssueStatus.Resolved:
                    if (CanClose(issue, user)) next.Add(IssueStatus.Closed);
                    next.Add(IssueStatus.Open);
                    break;
                case IssueStatus.Closed:
                    next.Add(IssueStatus.Open);
                    break;
            }
            return next;
        }

        public static bool CanSetStatus(Issue issue, IssueStatus status, string user) =>
            status != issue.Status && (status != IssueStatus.Closed || CanClose(issue, user));

        /// <summary>Changes the status. Resolving or closing records who and when; reopening clears it.</summary>
        public static void SetStatus(Issue issue, IssueStatus status, string user, DateTimeOffset now)
        {
            now = Timestamp.ToSecond(now);
            if (!CanSetStatus(issue, status, user))
                throw new InvalidOperationException(status == IssueStatus.Closed
                    ? "Only the author (" + issue.Author + ") can close this issue."
                    : "The issue is already " + status + ".");

            if (status == IssueStatus.Resolved || (status == IssueStatus.Closed && issue.ResolvedAt == null))
            {
                issue.ResolvedBy = user;
                issue.ResolvedAt = now;
            }
            else if (status == IssueStatus.Open || status == IssueStatus.InProgress)
            {
                issue.ResolvedBy = null;
                issue.ResolvedAt = null;
            }
            issue.Status = status;
            issue.Modified = now;
        }

        public static void Edit(Issue issue, string title, string body, IssuePriority priority, string assignee, DateTimeOffset now)
        {
            now = Timestamp.ToSecond(now);
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("An issue needs a title.", nameof(title));
            issue.Title = title.Trim();
            issue.Body = string.IsNullOrWhiteSpace(body) ? null : body.Trim();
            issue.Priority = priority;
            issue.Assignee = string.IsNullOrWhiteSpace(assignee) ? null : assignee.Trim();
            issue.Modified = now;
        }

        public static IssueComment AddComment(Issue issue, string user, string text, DateTimeOffset now)
        {
            now = Timestamp.ToSecond(now);
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("A comment needs text.", nameof(text));
            var comment = new IssueComment { Author = user, At = now, Text = text.Trim() };
            if (issue.Comments == null) issue.Comments = new List<IssueComment>();
            issue.Comments.Add(comment);
            issue.Modified = now;
            return comment;
        }

        /// <summary>A deep copy (through the file format), so an editor can change it without touching the original.</summary>
        public static Issue Clone(Issue issue) =>
            Persistence.MarkupJson.Deserialize<Issue>(Persistence.MarkupJson.Serialize(issue));
    }
}
