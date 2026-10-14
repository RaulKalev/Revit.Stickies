using System;
using System.Collections.Generic;
using Stickies.Core.Models;

namespace Stickies.Session
{
    /// <summary>What an issue's view or element looks like in the open model.</summary>
    [Flags]
    public enum TargetState
    {
        Ok = 0,
        ViewMissing = 1,
        ElementMissing = 2
    }

    public sealed class CaptureResult
    {
        public IssueLocation Location { get; set; }
        public IssueView View { get; set; }

        /// <summary>Why nothing was captured; null when it worked or the user cancelled.</summary>
        public string Error { get; set; }

        public bool Cancelled => Location == null && Error == null;
    }

    public sealed class OpenViewResult
    {
        public bool Opened { get; set; }
        public TargetState Target { get; set; }

        /// <summary>What the user should know (a fallback was used, something could not be restored); null when all went well.</summary>
        public string Message { get; set; }
    }

    /// <summary>
    /// Everything the pane asks of Revit. Calls return at once; the work runs in Revit's API context and the callback
    /// arrives on the UI thread. The harness supplies a fake.
    /// </summary>
    public interface IRevitHost
    {
        /// <summary>Asks the user for the issue's spot in the active view and captures the view to reopen later.</summary>
        void CaptureNewIssue(Action<CaptureResult> done);

        /// <summary>Opens the issue's saved view as it was captured (section box and camera for 3D, zoom for 2D).</summary>
        void OpenView(Issue issue, Action<OpenViewResult> done);

        /// <summary>Checks whose view or element no longer exists, by issue id.</summary>
        void CheckTargets(IReadOnlyList<Issue> issues, Action<IReadOnlyDictionary<string, TargetState>> done);
    }
}
