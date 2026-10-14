using System;
using System.IO;

namespace Stickies.Core.Store
{
    /// <summary>
    /// The shared folder layout:
    /// <code>
    /// &lt;project&gt;/_stickies/
    ///   project.json     schema version, model GUID, project name
    ///   active/          one file per open issue (&lt;id&gt;.json)
    ///   archive/         done issues by month (YYYY-MM.json)
    ///   compact.lock     held by whichever session is archiving
    /// </code>
    /// Early builds named the folder <c>_markup</c>; such a folder is renamed to <c>_stickies</c> the first time it is
    /// found (see <see cref="MigrateLegacy"/>).
    /// </summary>
    public sealed class MarkupFolder
    {
        public const string FolderName = "_stickies";
        public const string LegacyFolderName = "_markup";

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

        /// <summary>The archive file for a month: archive/YYYY-MM.json.</summary>
        public string ArchivePath(int year, int month) => Path.Combine(ArchiveDir, year.ToString("0000") + "-" + month.ToString("00") + ".json");

        /// <summary><c>_stickies</c> next to the model file, or null when the model has no folder on disk.</summary>
        public static string NextToModel(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath) || !Path.IsPathRooted(modelPath)) return null;
            var dir = Path.GetDirectoryName(modelPath);
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, FolderName);
        }

        /// <summary>
        /// The Stickies root for a folder the user picked: the folder itself when it is a <c>_stickies</c> folder (or an
        /// old <c>_markup</c> one, renamed), otherwise its <c>_stickies</c> subfolder.
        /// </summary>
        public static string FromPickedFolder(string picked)
        {
            var full = Path.GetFullPath(picked).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(full);
            if (string.Equals(name, FolderName, StringComparison.OrdinalIgnoreCase)) return full;
            if (string.Equals(name, LegacyFolderName, StringComparison.OrdinalIgnoreCase)) return MigrateLegacy(full);
            var root = Path.Combine(full, FolderName);
            if (!Directory.Exists(root)) MigrateLegacy(Path.Combine(full, LegacyFolderName));
            return root;
        }

        /// <summary>
        /// Where this model's notes live: the folder the user chose (when it still exists), else a <c>_stickies</c>
        /// folder next to the model, else null (ask the user). Old <c>_markup</c> folders are renamed on the way.
        /// </summary>
        public static string Resolve(string rememberedFolder, string modelPath)
        {
            if (!string.IsNullOrWhiteSpace(rememberedFolder))
            {
                var remembered = Path.GetFullPath(rememberedFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(Path.GetFileName(remembered), LegacyFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    var renamed = Path.Combine(Path.GetDirectoryName(remembered) ?? "", FolderName);
                    if (Directory.Exists(renamed)) return renamed; // renamed already (by this or another session)
                    if (Directory.Exists(remembered)) return MigrateLegacy(remembered);
                }
                else if (Directory.Exists(remembered)) return remembered;
            }

            var next = NextToModel(modelPath);
            if (next == null) return null;
            if (Directory.Exists(next)) return next;
            var legacy = Path.Combine(Path.GetDirectoryName(next) ?? "", LegacyFolderName);
            if (!IsStickiesFolder(legacy)) return null;
            var migrated = MigrateLegacy(legacy);
            return Directory.Exists(migrated) ? migrated : null;
        }

        /// <summary>
        /// Renames an old <c>_markup</c> Stickies folder (one with project.json) to <c>_stickies</c> next to it. Returns
        /// the new path, or the old one when it cannot be renamed (in use, or a <c>_stickies</c> folder is already there),
        /// so the notes stay reachable either way.
        /// </summary>
        public static string MigrateLegacy(string legacyPath)
        {
            if (!IsStickiesFolder(legacyPath)) return legacyPath;
            var target = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(legacyPath)) ?? "", FolderName);
            if (Directory.Exists(target)) return legacyPath;
            try
            {
                Directory.Move(legacyPath, target);
                return target;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return legacyPath;
            }
        }

        private static bool IsStickiesFolder(string path) =>
            !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "project.json"));
    }
}
