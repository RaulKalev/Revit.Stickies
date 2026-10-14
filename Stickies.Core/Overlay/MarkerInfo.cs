using Stickies.Core.Models;

namespace Stickies.Core.Overlay
{
    /// <summary>One marker to draw: an issue that passes the user's current filter and has a location. Immutable.</summary>
    public sealed class MarkerInfo
    {
        public MarkerInfo(string issueId, int? number, double[] pointMetres, string viewUniqueId, IssueStatus status, bool isSelected,
            string title = null, string body = null, string assignee = null, IssuePriority priority = IssuePriority.Normal)
        {
            Title = title;
            Body = body;
            Assignee = assignee;
            Priority = priority;
            IssueId = issueId;
            Number = number;
            PointMetres = pointMetres;
            ViewUniqueId = viewUniqueId;
            Status = status;
            IsSelected = isSelected;
        }

        public string IssueId { get; }
        public int? Number { get; }

        /// <summary>[x, y, z] in metres, model coordinates.</summary>
        public double[] PointMetres { get; }

        /// <summary>The view the issue was created in: its marker always shows there.</summary>
        public string ViewUniqueId { get; }

        public IssueStatus Status { get; }
        public bool IsSelected { get; }

        // Card text (sticky-note cards).
        public string Title { get; }
        public string Body { get; }
        public string Assignee { get; }
        public IssuePriority Priority { get; }

        /// <summary>Marker colour by status (RGB): open blue, in progress orange, resolved green, closed grey.</summary>
        public static byte[] StatusColor(IssueStatus status)
        {
            switch (status)
            {
                case IssueStatus.InProgress: return new byte[] { 0xFF, 0x9F, 0x0A };
                case IssueStatus.Resolved: return new byte[] { 0x28, 0xB4, 0x4E };
                case IssueStatus.Closed: return new byte[] { 0x8E, 0x8E, 0x93 };
                default: return new byte[] { 0x0A, 0x84, 0xFF };
            }
        }
    }
}
