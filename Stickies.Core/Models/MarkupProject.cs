using Newtonsoft.Json;

namespace Stickies.Core.Models
{
    /// <summary><c>_markup/project.json</c>: which model the folder belongs to.</summary>
    public class MarkupProject
    {
        public const int CurrentSchemaVersion = 1;

        [JsonProperty(Order = -10)] public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>The model's ProjectInformation.UniqueId (shared by a central model and its local copies).</summary>
        public string ModelGuid { get; set; }

        public string ProjectName { get; set; }
    }
}
