using System;
using System.Collections.Generic;
using System.Linq;
using Stickies.Core.Models;

namespace Stickies.Core.Issues
{
    /// <summary>Filters and sorts issues for the list (the user's filter and sort live in their local settings).</summary>
    public static class IssueQuery
    {
        public const string SortModified = "modified";
        public const string SortCreated = "created";
        public const string SortPriority = "priority";
        public const string SortStatus = "status";
        public const string SortTitle = "title";
        public const string SortAssignee = "assignee";

        public static IReadOnlyList<string> SortColumns { get; } =
            new[] { SortModified, SortCreated, SortPriority, SortStatus, SortTitle, SortAssignee };

        public static List<Issue> Apply(IEnumerable<Issue> issues, IssueFilter filter, IssueSort sort)
        {
            filter = filter ?? new IssueFilter();
            var matching = issues.Where(i => Matches(i, filter));
            return Sort(matching, sort ?? new IssueSort()).ToList();
        }

        public static bool Matches(Issue issue, IssueFilter filter)
        {
            if (filter.Statuses != null && filter.Statuses.Count > 0 && !filter.Statuses.Contains(issue.Status)) return false;
            if (filter.Priorities != null && filter.Priorities.Count > 0 && !filter.Priorities.Contains(issue.Priority)) return false;
            if (!string.IsNullOrWhiteSpace(filter.Author) && !SameUser(issue.Author, filter.Author)) return false;
            if (!string.IsNullOrWhiteSpace(filter.Assignee) && !SameUser(issue.Assignee, filter.Assignee)) return false;
            return string.IsNullOrWhiteSpace(filter.Text) || ContainsText(issue, filter.Text.Trim());
        }

        public static bool SameUser(string a, string b) =>
            string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        private static bool ContainsText(Issue issue, string text)
        {
            bool Has(string s) => s != null && s.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
            return Has(issue.Title) || Has(issue.Body) || Has(issue.Author) || Has(issue.Assignee) ||
                   Has(issue.View?.Name) || Has(issue.Location?.ElementCategory) ||
                   (issue.Comments != null && issue.Comments.Any(c => Has(c.Text) || Has(c.Author)));
        }

        private static IEnumerable<Issue> Sort(IEnumerable<Issue> issues, IssueSort sort)
        {
            IOrderedEnumerable<Issue> ordered;
            var desc = sort.Descending;
            switch ((sort.Column ?? SortModified).ToLowerInvariant())
            {
                case SortCreated: ordered = By(issues, i => i.Created, desc); break;
                case SortPriority: ordered = By(issues, i => (int)i.Priority, desc); break;
                case SortStatus: ordered = By(issues, i => (int)i.Status, desc); break;
                case SortTitle: ordered = By(issues, i => i.Title ?? "", desc, StringComparer.CurrentCultureIgnoreCase); break;
                case SortAssignee:
                    // Unassigned issues go last either way.
                    ordered = issues.OrderBy(i => string.IsNullOrWhiteSpace(i.Assignee) ? 1 : 0);
                    ordered = desc ? ordered.ThenByDescending(i => i.Assignee ?? "", StringComparer.CurrentCultureIgnoreCase)
                                   : ordered.ThenBy(i => i.Assignee ?? "", StringComparer.CurrentCultureIgnoreCase);
                    break;
                default: ordered = By(issues, i => i.Modified, desc); break;
            }
            // Stable tie-break: most recently changed first, then id.
            return ordered.ThenByDescending(i => i.Modified).ThenBy(i => i.Id, StringComparer.Ordinal);
        }

        private static IOrderedEnumerable<Issue> By<T>(IEnumerable<Issue> issues, Func<Issue, T> key, bool desc, IComparer<T> comparer = null) =>
            desc ? issues.OrderByDescending(key, comparer ?? Comparer<T>.Default) : issues.OrderBy(key, comparer ?? Comparer<T>.Default);
    }
}
