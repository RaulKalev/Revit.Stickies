using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Stickies.Core.Models
{
    public enum IssueStatus { Open, InProgress, Resolved, Closed }

    public enum IssuePriority { Low, Normal, High }

    /// <summary>
    /// One review issue. While open it is stored as <c>_markup/active/&lt;id&gt;.json</c>; the file name is the id, so two
    /// users never write the same new file. Geometry is in metres, model coordinates; elements and views are referenced
    /// by UniqueId, never ElementId.
    /// </summary>
    public class Issue
    {
        public const int CurrentSchemaVersion = 1;

        [JsonProperty(Order = -10)] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        [JsonProperty(Order = -9)] public string Id { get; set; }

        /// <summary>The number on the marker and in the list (see IssueNumbers). Optional: older files have none.</summary>
        [JsonProperty(Order = -8)] public int? Number { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public IssueStatus Status { get; set; } = IssueStatus.Open;
        public IssuePriority Priority { get; set; } = IssuePriority.Normal;

        /// <summary>Windows user name of the person who created the issue (no login).</summary>
        public string Author { get; set; }

        /// <summary>Optional Windows user name.</summary>
        public string Assignee { get; set; }

        public DateTimeOffset Created { get; set; }
        public DateTimeOffset Modified { get; set; }
        public string ResolvedBy { get; set; }
        public DateTimeOffset? ResolvedAt { get; set; }

        public IssueLocation Location { get; set; }
        public IssueView View { get; set; }
        public List<IssueComment> Comments { get; set; } = new List<IssueComment>();

        /// <summary>Resolved and closed issues are the only ones the compactor moves to the archive.</summary>
        [JsonIgnore] public bool IsDone => Status == IssueStatus.Resolved || Status == IssueStatus.Closed;
    }

    public class IssueLocation
    {
        /// <summary>[x, y, z] in metres, model coordinates.</summary>
        public double[] Point { get; set; }

        /// <summary>Optional element the issue is about (UniqueId).</summary>
        public string ElementUniqueId { get; set; }

        public string ElementCategory { get; set; }
    }

    /// <summary>The view the issue was created in, enough to open it again as the reviewer saw it.</summary>
    public class IssueView
    {
        /// <summary>View.UniqueId (survives upgrades).</summary>
        public string UniqueId { get; set; }

        /// <summary>Fallback when the UniqueId is gone.</summary>
        public string Name { get; set; }

        /// <summary>Revit ViewType name: FloorPlan, Section, ThreeD, …</summary>
        public string Type { get; set; }

        /// <summary>3D views only, else null.</summary>
        public SectionBox SectionBox { get; set; }

        /// <summary>3D views only, else null.</summary>
        public ViewOrientation Orientation { get; set; }

        /// <summary>
        /// 2D and orthographic 3D views: the two corners of UIView.GetZoomCorners(), [[x, y, z], [x, y, z]] in metres.
        /// </summary>
        public double[][] ZoomCorners { get; set; }
    }

    public class SectionBox
    {
        /// <summary>[x, y, z] in metres, in the box's own coordinates.</summary>
        public double[] Min { get; set; }
        public double[] Max { get; set; }

        /// <summary>Box transform as 12 numbers: origin (metres), basisX, basisY, basisZ.</summary>
        public double[] Transform { get; set; }
    }

    public class ViewOrientation
    {
        /// <summary>Eye position in metres.</summary>
        public double[] Eye { get; set; }

        /// <summary>Unit vectors.</summary>
        public double[] Forward { get; set; }
        public double[] Up { get; set; }
    }

    public class IssueComment
    {
        public string Author { get; set; }
        public DateTimeOffset At { get; set; }
        public string Text { get; set; }
    }
}
