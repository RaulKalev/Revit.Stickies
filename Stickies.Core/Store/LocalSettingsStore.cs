using System;
using System.IO;
using Stickies.Core.Models;
using Stickies.Core.Persistence;

namespace Stickies.Core.Store
{
    /// <summary>
    /// Per-user, per-project settings on this machine: <c>%AppData%\Stickies\projects\&lt;modelGuid&gt;.json</c>.
    /// Never touches the shared folder.
    /// </summary>
    public sealed class LocalSettingsStore
    {
        public LocalSettingsStore(string directory = null)
        {
            Directory = directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stickies", "projects");
        }

        public string Directory { get; }

        public string PathFor(string modelGuid)
        {
            if (string.IsNullOrWhiteSpace(modelGuid)) throw new ArgumentException("A model GUID is required.", nameof(modelGuid));
            foreach (var c in Path.GetInvalidFileNameChars()) modelGuid = modelGuid.Replace(c, '_');
            return Path.Combine(Directory, modelGuid + ".json");
        }

        /// <summary>The saved settings, or defaults when there are none (or they cannot be read).</summary>
        public LocalSettings Load(string modelGuid)
        {
            try
            {
                var path = PathFor(modelGuid);
                return File.Exists(path) ? MarkupJson.Deserialize<LocalSettings>(File.ReadAllText(path)) ?? new LocalSettings() : new LocalSettings();
            }
            catch (Exception)
            {
                return new LocalSettings();
            }
        }

        public void Save(string modelGuid, LocalSettings settings) =>
            AtomicFile.WriteAllText(PathFor(modelGuid), MarkupJson.Serialize(settings));
    }
}
