using System;
using System.Collections.Generic;
using System.Windows.Threading;
using Stickies.Core.Models;
using Stickies.Session;

namespace Stickies.UiHarness
{
    /// <summary>Stands in for Revit: answers asynchronously on the UI thread, as the ExternalEvent does.</summary>
    internal sealed class FakeRevitHost : IRevitHost
    {
        private readonly Dispatcher _dispatcher;

        public FakeRevitHost(Dispatcher dispatcher) => _dispatcher = dispatcher;

        /// <summary>The next capture's answer (default: a spot on a camera in a floor plan).</summary>
        public Func<CaptureResult> NextCapture { get; set; } = () => new CaptureResult
        {
            Location = new IssueLocation { Point = new[] { 12.345, 6.789, 3.0 }, ElementUniqueId = "elem-camera", ElementCategory = "Security Devices" },
            View = new IssueView { UniqueId = "view-plan-2", Name = "EN_2_korrus_VVS", Type = "FloorPlan", ZoomCorners = new[] { new[] { 0.0, 0, 0 }, new[] { 30.0, 20, 0 } } }
        };

        public Dictionary<string, TargetState> Targets { get; } = new Dictionary<string, TargetState>();
        public List<string> Opened { get; } = new List<string>();
        public int TargetChecks { get; private set; }

        public void CaptureNewIssue(Action<CaptureResult> done)
        {
            var result = NextCapture();
            _dispatcher.BeginInvoke(new Action(() => done(result)));
        }

        public void OpenView(Issue issue, Action<OpenViewResult> done)
        {
            Opened.Add(issue.Id);
            Targets.TryGetValue(issue.Id, out var target);
            var result = target.HasFlag(TargetState.ViewMissing)
                ? new OpenViewResult { Target = target, Message = "The view \"" + issue.View?.Name + "\" no longer exists in this model." }
                : new OpenViewResult { Opened = true, Target = target };
            _dispatcher.BeginInvoke(new Action(() => done(result)));
        }

        public void CheckTargets(IReadOnlyList<Issue> issues, Action<IReadOnlyDictionary<string, TargetState>> done)
        {
            TargetChecks++;
            var copy = new Dictionary<string, TargetState>(Targets);
            _dispatcher.BeginInvoke(new Action(() => done(copy)));
        }
    }
}
