using System.Collections.Generic;
using System.Linq;
using Stickies.Core.Models;

namespace Stickies.Core.Issues
{
    /// <summary>
    /// Issue numbers shown on markers and in the list: one more than the highest number in the folder. Two people adding
    /// an issue in the same moment can rarely get the same number; the id stays unique, so nothing breaks.
    /// </summary>
    public static class IssueNumbers
    {
        public static int Next(IEnumerable<Issue> existing) =>
            (existing?.Where(i => i.Number.HasValue).Select(i => i.Number.Value).DefaultIfEmpty(0).Max() ?? 0) + 1;
    }
}
