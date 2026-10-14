using System;
using System.Globalization;
using Stickies.Core.Models;

namespace Stickies.UI.ViewModels
{
    /// <summary>Words and times as the pane shows them.</summary>
    public static class Display
    {
        // The UI is English, so day and month names are too, whatever the Windows region (e.g. Estonian) says.
        private static readonly CultureInfo English = CultureInfo.InvariantCulture;

        public static string Status(IssueStatus status)
        {
            switch (status)
            {
                case IssueStatus.InProgress: return "In progress";
                case IssueStatus.Resolved: return "Resolved";
                case IssueStatus.Closed: return "Closed";
                default: return "Open";
            }
        }

        /// <summary>The button text for moving an issue to <paramref name="status"/>.</summary>
        public static string StatusAction(IssueStatus from, IssueStatus status)
        {
            switch (status)
            {
                case IssueStatus.InProgress: return "Start";
                case IssueStatus.Resolved: return "Resolve";
                case IssueStatus.Closed: return "Close";
                default: return "Reopen";
            }
        }

        public static string Priority(IssuePriority priority) =>
            priority == IssuePriority.High ? "High" : priority == IssuePriority.Low ? "Low" : "Normal";

        /// <summary>"just now", "5 min ago", "3 h ago", "yesterday", "Mon 09:30", "14 Oct", "14 Oct 2025".</summary>
        public static string When(DateTimeOffset at, DateTimeOffset now)
        {
            var local = at.ToLocalTime();
            var span = now - at;
            if (span < TimeSpan.FromMinutes(1)) return "just now";
            if (span < TimeSpan.FromHours(1)) return (int)span.TotalMinutes + " min ago";
            if (span < TimeSpan.FromHours(12)) return (int)span.TotalHours + " h ago";
            var days = (now.ToLocalTime().Date - local.Date).Days;
            if (days == 0) return local.ToString("HH:mm", English);
            if (days == 1) return "yesterday";
            if (days < 7) return local.ToString("ddd HH:mm", English);
            return local.Year == now.ToLocalTime().Year
                ? local.ToString("d MMM", English)
                : local.ToString("d MMM yyyy", English);
        }

        /// <summary>Full date and time for tooltips.</summary>
        public static string Exact(DateTimeOffset at) => at.ToLocalTime().ToString("d MMM yyyy HH:mm", English);

        public static string ViewType(string type)
        {
            switch (type)
            {
                case "FloorPlan": return "floor plan";
                case "CeilingPlan": return "ceiling plan";
                case "EngineeringPlan": return "structural plan";
                case "AreaPlan": return "area plan";
                case "ThreeD": return "3D view";
                case "DraftingView": return "drafting view";
                case null: return "view";
                default: return type.ToLowerInvariant();
            }
        }
    }
}
