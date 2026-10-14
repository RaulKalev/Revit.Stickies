using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Stickies.Core.Models
{
    /// <summary>Issue ids: <c>yyyyMMdd-HHmmss-xxxxxx</c> (local creation time + 6 random hex digits), also the file name.</summary>
    public static class IssueId
    {
        private static readonly Regex Pattern = new Regex(@"^\d{8}-\d{6}-[0-9a-f]{6}$", RegexOptions.CultureInvariant);

        public static string New(DateTimeOffset now)
        {
            var bytes = new byte[3];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
                   bytes[0].ToString("x2") + bytes[1].ToString("x2") + bytes[2].ToString("x2");
        }

        public static bool IsValid(string id) => id != null && Pattern.IsMatch(id);
    }
}
