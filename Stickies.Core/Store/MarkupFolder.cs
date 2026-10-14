using System;
using System.IO;

namespace Stickies.Core.Store
{
    /// <summary>
    /// The shared folder layout:
    /// <code>
    /// &lt;project&gt;/_markup/
    ///   project.json     schema version, model GUID, project name
    ///   active/          one file per open issue (&lt;id&gt;.json)
    ///   archive/         closed issues compacted by month (YYYY-MM.json)
    ///   compact.lock     held by whichever session is compacting
    /// </code>
    /// </summary>
    public sealed class MarkupFolder
    {
        public const string FolderName = "_markup";

        public MarkupFolder(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A folder is required.", nameof(root));
            Root = Path.GetFullPath(root);
        }

        public string Root { get; }
        public string ActiveDir => Path.Combine(Root, "active");
        public string ArchiveDir => Path.Combine(Root, "archive");
        public string ProjectFile => Path.Combine(Root, "project.json");
        public string CompactLock => Path.Combine(Root, "compact.lock");

        public bool Exists => Directory.Exists(Root);

        public string IssuePath(string id) => Path.Combine(ActiveDir, id + ".json");

        /// <summary><c>_markup</c> next to the model file, or null when the model has no folder on disk.</summary>
        public static string NextToModel(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath) || !Path.IsPathRooted(modelPath)) return null;
            var dir = Path.GetDirectoryName(modelPath);
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, FolderName);
        }

        /// <summary>
        /// The markup root for a folder the user picked: the folder itself when it is a <c>_markup</c> folder,
        /// otherwise its <c>_markup</c> subfolder.
        /// </summary>
        public static string FromPickedFolder(string picked)
        {
            var full = Path.GetFullPath(picked).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(Path.GetFileName(full), FolderName, StringComparison.OrdinalIgnoreCase)
                ? full
                : Path.Combine(full, FolderName);
        }

        /// <summary>
        /// Where this model's markup lives: the folder the user chose (when it still exists), else an existing
        /// <c>_markup</c> next to the model, else null (ask the user).
        /// </summary>
        public static string Resolve(string rememberedFolder, string modelPath)
        {
            if (!string.IsNullOrWhiteSpace(rememberedFolder) && Directory.Exists(rememberedFolder)) return Path.GetFullPath(rememberedFolder);
            var next = NextToModel(modelPath);
            return next != null && Directory.Exists(next) ? next : null;
        }
    }
}
