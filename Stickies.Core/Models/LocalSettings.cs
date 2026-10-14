using System.Collections.Generic;
using Newtonsoft.Json;

namespace Stickies.Core.Models
{
    /// <summary>
    /// One user's settings for one project, stored on this machine only
    /// (<c>%AppData%\Stickies\projects\&lt;modelGuid&gt;.json</c>). Never written to the shared folder.
    /// </summary>
    public class LocalSettings
    {
        public const int CurrentSchemaVersion = 1;

        [JsonProperty(Order = -10)] public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>The project's <c>_stickies</c> folder chosen by the user; null = look next to the model.</summary>
        public string MarkupFolder { get; set; }

        public bool OverlayVisible { get; set; } = true;

        /// <summary>Show each issue as a sticky-note card next to its marker (active view only).</summary>
        public bool ShowCards { get; set; }

        public IssueFilter Filter { get; set; } = new IssueFilter();

        public IssueSort Sort { get; set; } = new IssueSort();
    }

    public class IssueFilter
    {
        /// <summary>Empty = all statuses.</summary>
        public List<IssueStatus> Statuses { get; set; } = new List<IssueStatus>();

        /// <summary>Empty = all priorities.</summary>
        public List<IssuePriority> Priorities { get; set; } = new List<IssuePriority>();

        public string Author { get; set; }
        public string Assignee { get; set; }
        public string Text { get; set; }
    }

    public class IssueSort
    {
        public string Column { get; set; } = "modified";
        public bool Descending { get; set; } = true;
    }
}
