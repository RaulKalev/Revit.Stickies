using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Stickies.Core.Persistence
{
    /// <summary>
    /// Writes a file so readers (and Dropbox) never see it half written: the text goes to a uniquely named
    /// <c>.tmp</c> file next to the target, which then replaces the target in one rename. Sync clients briefly lock
    /// files, so the rename is retried a few times before giving up.
    /// </summary>
    public static class AtomicFile
    {
        public const string TempExtension = ".tmp";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void WriteAllText(string path, string text)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(dir);
            var tmp = Path.Combine(dir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + TempExtension);

            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Utf8NoBom.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            try
            {
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        if (File.Exists(path)) File.Replace(tmp, path, null, true);
                        else File.Move(tmp, path);
                        return;
                    }
                    catch (IOException) when (attempt < 5)
                    {
                        Thread.Sleep(100 * attempt);
                    }
                    catch (UnauthorizedAccessException) when (attempt < 5)
                    {
                        Thread.Sleep(100 * attempt);
                    }
                }
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* leftover tmp files are ignored by readers */ }
            }
        }
    }
}
