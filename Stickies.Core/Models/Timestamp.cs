using System;

namespace Stickies.Core.Models
{
    /// <summary>
    /// Issue files store times to the second ("2026-10-14T09:30:12+03:00"). Times are rounded down to the second where
    /// they are made, so an issue in memory equals the one read back from its file.
    /// </summary>
    public static class Timestamp
    {
        public static DateTimeOffset ToSecond(DateTimeOffset t) =>
            new DateTimeOffset(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, t.Offset);

        /// <summary>Same instant at file precision.</summary>
        public static bool SameSecond(DateTimeOffset a, DateTimeOffset b) => ToSecond(a) == ToSecond(b);
    }
}
