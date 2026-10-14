using System;
using System.IO;

namespace Stickies.Core.Tests
{
    /// <summary>A throwaway folder for one test.</summary>
    internal sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StickiesTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch { /* best effort */ }
        }
    }
}
