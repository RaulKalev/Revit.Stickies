using System;
using System.Collections.Generic;
using System.Linq;
using Stickies.Core.Issues;
using Stickies.Core.Models;
using Xunit;

namespace Stickies.Core.Tests
{
    public class IssueRulesTests
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.FromHours(3));

        private static Issue Make(string title, IssueStatus status = IssueStatus.Open, IssuePriority priority = IssuePriority.Normal,
            string author = "raul", string assignee = null, int minutes = 0)
        {
            var issue = IssueWorkflow.Create(author, T0.AddMinutes(minutes), null, new IssueView { Name = "EN_2_korrus" });
            issue.Title = title;
            issue.Status = status;
            issue.Priority = priority;
            issue.Assignee = assignee;
            return issue;
        }

        private static List<Issue> Sample() => new List<Issue>
        {
            Make("Camera too close to door", IssueStatus.Open, IssuePriority.High, "raul", "joonas", 1),
            Make("Missing card reader", IssueStatus.InProgress, IssuePriority.Normal, "raul", null, 2),
            Make("Wrong cable tray height", IssueStatus.Resolved, IssuePriority.Low, "joonas", "raul", 3),
            Make("Duplicate smoke detector", IssueStatus.Closed, IssuePriority.High, "mari", "joonas", 4)
        };

        [Fact]
        public void Empty_filter_returns_everything_newest_change_first()
        {
            var result = IssueQuery.Apply(Sample(), new IssueFilter(), new IssueSort());
            Assert.Equal(new[] { "Duplicate smoke detector", "Wrong cable tray height", "Missing card reader", "Camera too close to door" },
                result.Select(i => i.Title));
        }

        [Fact]
        public void Filters_combine()
        {
            var filter = new IssueFilter { Statuses = { IssueStatus.Open, IssueStatus.Closed }, Priorities = { IssuePriority.High } };
            Assert.Equal(2, IssueQuery.Apply(Sample(), filter, null).Count);

            filter.Assignee = "JOONAS";
            Assert.Equal(2, IssueQuery.Apply(Sample(), filter, null).Count);

            filter.Author = "mari";
            Assert.Equal("Duplicate smoke detector", Assert.Single(IssueQuery.Apply(Sample(), filter, null)).Title);
        }

        [Fact]
        public void Text_search_covers_title_body_comments_view_and_people()
        {
            var issues = Sample();
            issues[1].Body = "Reader at the staff entrance";
            IssueWorkflow.AddComment(issues[2], "raul", "Tray moved to +2850", T0);

            Assert.Equal("Missing card reader", Assert.Single(IssueQuery.Apply(issues, new IssueFilter { Text = "staff ENTRANCE" }, null)).Title);
            Assert.Equal("Wrong cable tray height", Assert.Single(IssueQuery.Apply(issues, new IssueFilter { Text = "2850" }, null)).Title);
            Assert.Equal(4, IssueQuery.Apply(issues, new IssueFilter { Text = "korrus" }, null).Count);
            Assert.Single(IssueQuery.Apply(issues, new IssueFilter { Text = "mari" }, null));
        }

        [Theory]
        [InlineData(IssueQuery.SortPriority, true, "Duplicate smoke detector")] // tie on High: most recently changed first
        [InlineData(IssueQuery.SortPriority, false, "Wrong cable tray height")]
        [InlineData(IssueQuery.SortTitle, false, "Camera too close to door")]
        [InlineData(IssueQuery.SortStatus, true, "Duplicate smoke detector")]
        [InlineData(IssueQuery.SortCreated, false, "Camera too close to door")]
        [InlineData(IssueQuery.SortAssignee, false, "Duplicate smoke detector")] // tie on joonas
        public void Sorts_by_column(string column, bool descending, string first)
        {
            var result = IssueQuery.Apply(Sample(), null, new IssueSort { Column = column, Descending = descending });
            Assert.Equal(first, result[0].Title);
        }

        [Fact]
        public void Unassigned_sorts_last_by_assignee_in_both_directions()
        {
            Assert.Equal("Missing card reader", IssueQuery.Apply(Sample(), null, new IssueSort { Column = "assignee", Descending = false }).Last().Title);
            Assert.Equal("Missing card reader", IssueQuery.Apply(Sample(), null, new IssueSort { Column = "assignee", Descending = true }).Last().Title);
        }

        [Fact]
        public void Resolving_records_who_and_when_and_reopening_clears_it()
        {
            var issue = Make("x", author: "raul");
            IssueWorkflow.SetStatus(issue, IssueStatus.Resolved, "joonas", T0.AddHours(1));
            Assert.Equal("joonas", issue.ResolvedBy);
            Assert.Equal(T0.AddHours(1), issue.ResolvedAt);
            Assert.Equal(T0.AddHours(1), issue.Modified);

            IssueWorkflow.SetStatus(issue, IssueStatus.Open, "joonas", T0.AddHours(2));
            Assert.Null(issue.ResolvedBy);
            Assert.Null(issue.ResolvedAt);
        }

        [Fact]
        public void Only_the_author_can_close()
        {
            var issue = Make("x", IssueStatus.Resolved, author: "raul");
            Assert.DoesNotContain(IssueStatus.Closed, IssueWorkflow.NextStatuses(issue, "joonas"));
            Assert.Throws<InvalidOperationException>(() => IssueWorkflow.SetStatus(issue, IssueStatus.Closed, "joonas", T0));

            Assert.Contains(IssueStatus.Closed, IssueWorkflow.NextStatuses(issue, "RAUL"));
            IssueWorkflow.SetStatus(issue, IssueStatus.Closed, "raul", T0);
            Assert.Equal(IssueStatus.Closed, issue.Status);
        }

        [Fact]
        public void Closing_an_unresolved_issue_records_who_closed_it()
        {
            var issue = Make("x", IssueStatus.Open, author: "raul");
            IssueWorkflow.SetStatus(issue, IssueStatus.Closed, "raul", T0);
            Assert.Equal("raul", issue.ResolvedBy);
        }

        [Fact]
        public void Edit_trims_and_clears_empty_fields_and_requires_a_title()
        {
            var issue = Make("x");
            IssueWorkflow.Edit(issue, "  Camera  ", "  ", IssuePriority.High, " ", T0.AddDays(1));
            Assert.Equal("Camera", issue.Title);
            Assert.Null(issue.Body);
            Assert.Null(issue.Assignee);
            Assert.Equal(IssuePriority.High, issue.Priority);
            Assert.Equal(T0.AddDays(1), issue.Modified);
            Assert.Throws<ArgumentException>(() => IssueWorkflow.Edit(issue, " ", null, IssuePriority.Low, null, T0));
        }

        [Fact]
        public void Comments_append_and_stamp_modified()
        {
            var issue = Make("x");
            IssueWorkflow.AddComment(issue, "joonas", " Fixed ", T0.AddHours(3));
            Assert.Equal("Fixed", Assert.Single(issue.Comments).Text);
            Assert.Equal(T0.AddHours(3), issue.Modified);
            Assert.Throws<ArgumentException>(() => IssueWorkflow.AddComment(issue, "joonas", "  ", T0));
        }

        [Fact]
        public void Workflow_stamps_whole_seconds_like_the_file()
        {
            var at = T0.AddMilliseconds(789);
            var issue = IssueWorkflow.Create("raul", at, null, null);
            Assert.Equal(T0, issue.Created);
            IssueWorkflow.SetStatus(issue, IssueStatus.Resolved, "raul", at);
            Assert.Equal(T0, issue.ResolvedAt);
            Assert.Equal(T0, IssueWorkflow.AddComment(issue, "raul", "x", at).At);
            IssueWorkflow.Edit(issue, "t", null, IssuePriority.Low, null, at);
            Assert.Equal(T0, issue.Modified);
        }

        [Fact]
        public void Clone_is_deep()
        {
            var issue = Make("x");
            IssueWorkflow.AddComment(issue, "a", "b", T0);
            var copy = IssueWorkflow.Clone(issue);
            copy.Comments[0].Text = "changed";
            copy.View.Name = "other";
            Assert.Equal("b", issue.Comments[0].Text);
            Assert.Equal("EN_2_korrus", issue.View.Name);
        }
    }
}
