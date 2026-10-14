using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Stickies.Core.Models;
using Stickies.Infrastructure;
using Stickies.Session;
using RevitOperationCanceled = Autodesk.Revit.Exceptions.OperationCanceledException;

namespace Stickies.Revit
{
    /// <summary>
    /// The Revit side of the pane: everything runs through <see cref="RevitRequests"/> in API context. Geometry leaves
    /// and enters here in metres (Revit works in feet). The model is changed only when an issue's 3D view is opened
    /// (section box and camera), in one undoable transaction.
    /// </summary>
    internal sealed class RevitHost : IRevitHost
    {
        private const double MetresPerFoot = 0.3048;
        private readonly RevitRequests _requests;

        public RevitHost(RevitRequests requests)
        {
            _requests = requests;
        }

        // ------------------------------------------------------------------ capture

        public void CaptureNewIssue(Action<CaptureResult> done)
        {
            _requests.Post(app =>
            {
                CaptureResult result;
                try { result = Capture(app.ActiveUIDocument); }
                catch (Exception ex)
                {
                    AppLog.Error("Capturing a new issue failed", ex);
                    result = new CaptureResult { Error = "Could not read the view: " + ex.Message };
                }
                done(result);
            });
        }

        private static CaptureResult Capture(UIDocument uidoc)
        {
            if (uidoc == null || uidoc.Document.IsFamilyDocument) return new CaptureResult { Error = "Open a project first." };
            var doc = uidoc.Document;
            var view = uidoc.ActiveView;
            if (!IsReviewable(view)) return new CaptureResult { Error = "Open a plan, section, elevation, drafting or 3D view to add an issue there." };

            // 1. Something selected: the issue is about that element.  2. Plans: click any spot.  3. Else: click a spot on an element.
            XYZ point = null;
            Element element = null;
            var selected = uidoc.Selection.GetElementIds().Select(doc.GetElement).FirstOrDefault(e => e != null);
            if (selected != null)
            {
                element = selected;
                var box = selected.get_BoundingBox(view) ?? selected.get_BoundingBox(null);
                point = box != null ? (box.Min + box.Max) / 2 : (selected.Location as LocationPoint)?.Point;
            }

            if (point == null)
            {
                const string prompt = "Stickies: click the spot for the new issue (Esc cancels).";
                try
                {
                    if (view.SketchPlane != null && !(view is View3D))
                    {
                        point = uidoc.Selection.PickPoint(prompt);
                    }
                    else
                    {
                        var picked = uidoc.Selection.PickObject(ObjectType.PointOnElement, prompt);
                        point = picked.GlobalPoint;
                        if (picked.LinkedElementId == ElementId.InvalidElementId) element = doc.GetElement(picked.ElementId);
                    }
                }
                catch (RevitOperationCanceled)
                {
                    return new CaptureResult(); // cancelled by the user: no error to show
                }
            }

            if (point == null) return new CaptureResult { Error = "Could not find a point for the issue." };

            return new CaptureResult
            {
                Location = new IssueLocation
                {
                    Point = M(point),
                    ElementUniqueId = element?.UniqueId,
                    ElementCategory = element?.Category?.Name
                },
                View = CaptureView(uidoc, view)
            };
        }

        private static bool IsReviewable(View view)
        {
            if (view == null || view.IsTemplate) return false;
            switch (view.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.EngineeringPlan:
                case ViewType.AreaPlan:
                case ViewType.Section:
                case ViewType.Elevation:
                case ViewType.Detail:
                case ViewType.DraftingView:
                case ViewType.ThreeD:
                    return true;
                default:
                    return false;
            }
        }

        private static IssueView CaptureView(UIDocument uidoc, View view)
        {
            var captured = new IssueView { UniqueId = view.UniqueId, Name = view.Name, Type = view.ViewType.ToString() };
            if (view is View3D view3D)
            {
                if (view3D.IsSectionBoxActive)
                {
                    var box = view3D.GetSectionBox();
                    var t = box.Transform;
                    captured.SectionBox = new SectionBox
                    {
                        Min = M(box.Min),
                        Max = M(box.Max),
                        Transform = M(t.Origin).Concat(D(t.BasisX)).Concat(D(t.BasisY)).Concat(D(t.BasisZ)).ToArray()
                    };
                }
                var o = view3D.GetOrientation();
                captured.Orientation = new ViewOrientation { Eye = M(o.EyePosition), Forward = D(o.ForwardDirection), Up = D(o.UpDirection) };
            }
            else
            {
                var corners = UiViewOf(uidoc, view)?.GetZoomCorners();
                if (corners != null && corners.Count == 2) captured.ZoomCorners = new[] { M(corners[0]), M(corners[1]) };
            }
            return captured;
        }

        // ------------------------------------------------------------------ open

        public void OpenView(Issue issue, Action<OpenViewResult> done)
        {
            _requests.Post(app =>
            {
                OpenViewResult result;
                try { result = Open(app.ActiveUIDocument, issue); }
                catch (Exception ex)
                {
                    AppLog.Error("Opening the issue's view failed", ex);
                    result = new OpenViewResult { Message = "Could not open the view: " + ex.Message };
                }
                done(result);
            });
        }

        private static OpenViewResult Open(UIDocument uidoc, Issue issue)
        {
            if (uidoc == null) return new OpenViewResult { Message = "Open the project first." };
            var doc = uidoc.Document;
            var saved = issue.View;
            var target = ElementMissing(doc, issue) ? TargetState.ElementMissing : TargetState.Ok;
            var notes = new List<string>();

            var view = FindView(doc, saved, out var byName);
            if (view == null)
                return new OpenViewResult { Target = target | TargetState.ViewMissing, Message = "The view \"" + saved?.Name + "\" no longer exists in this model." };
            if (byName) notes.Add("The original view was replaced; opened \"" + view.Name + "\" by its name.");
            if (target.HasFlag(TargetState.ElementMissing)) notes.Add("The element this issue is about no longer exists.");

            if (uidoc.ActiveView.Id != view.Id) uidoc.ActiveView = view;

            if (view is View3D view3D && (saved.SectionBox != null || saved.Orientation != null))
            {
                using (var t = new Transaction(doc, "Stickies: restore issue view"))
                {
                    try
                    {
                        t.Start();
                        if (saved.SectionBox != null)
                        {
                            view3D.IsSectionBoxActive = true;
                            view3D.SetSectionBox(ToBox(saved.SectionBox));
                        }
                        if (saved.Orientation != null)
                        {
                            if (view3D.IsLocked) notes.Add("The 3D view is locked, so its camera was not restored.");
                            else view3D.SetOrientation(new ViewOrientation3D(Ft(saved.Orientation.Eye), Dir(saved.Orientation.Up), Dir(saved.Orientation.Forward)));
                        }
                        t.Commit();
                    }
                    catch (Exception ex)
                    {
                        if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                        AppLog.Warn("Restoring 3D view failed: " + ex.Message);
                        notes.Add("The view opened, but its section box and camera could not be restored (" + ex.Message + ").");
                    }
                }
            }
            else if (saved.ZoomCorners != null && saved.ZoomCorners.Length == 2)
            {
                try { UiViewOf(uidoc, view)?.ZoomAndCenterRectangle(Ft(saved.ZoomCorners[0]), Ft(saved.ZoomCorners[1])); }
                catch (Exception ex) { AppLog.Warn("Zoom failed: " + ex.Message); }
            }

            return new OpenViewResult { Opened = true, Target = target, Message = notes.Count == 0 ? null : string.Join(" ", notes) };
        }

        /// <summary>By UniqueId; when that is gone, by name (preferring the same view type).</summary>
        private static View FindView(Document doc, IssueView saved, out bool byName)
        {
            byName = false;
            if (saved == null) return null;
            if (!string.IsNullOrEmpty(saved.UniqueId) && doc.GetElement(saved.UniqueId) is View v && !v.IsTemplate) return v;
            if (string.IsNullOrEmpty(saved.Name)) return null;

            var match = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(x => !x.IsTemplate && x.Name == saved.Name)
                .OrderBy(x => x.ViewType.ToString() == saved.Type ? 0 : 1)
                .FirstOrDefault();
            byName = match != null;
            return match;
        }

        // ------------------------------------------------------------------ targets

        public void CheckTargets(IReadOnlyList<Issue> issues, Action<IReadOnlyDictionary<string, TargetState>> done)
        {
            var snapshot = issues.ToList();
            _requests.Post(app =>
            {
                var states = new Dictionary<string, TargetState>();
                var doc = app.ActiveUIDocument?.Document;
                if (doc != null && !doc.IsFamilyDocument)
                {
                    foreach (var issue in snapshot)
                    {
                        try
                        {
                            var state = TargetState.Ok;
                            if (issue.View != null && FindView(doc, issue.View, out _) == null) state |= TargetState.ViewMissing;
                            if (ElementMissing(doc, issue)) state |= TargetState.ElementMissing;
                            if (state != TargetState.Ok) states[issue.Id] = state;
                        }
                        catch (Exception ex)
                        {
                            AppLog.Warn("Checking issue " + issue.Id + " failed: " + ex.Message);
                        }
                    }
                }
                done(states);
            });
        }

        private static bool ElementMissing(Document doc, Issue issue)
        {
            var uid = issue.Location?.ElementUniqueId;
            return !string.IsNullOrEmpty(uid) && doc.GetElement(uid) == null;
        }

        // ------------------------------------------------------------------ helpers

        private static UIView UiViewOf(UIDocument uidoc, View view) =>
            uidoc.GetOpenUIViews().FirstOrDefault(u => u.ViewId == view.Id);

        private static BoundingBoxXYZ ToBox(SectionBox box)
        {
            var t = Transform.Identity;
            if (box.Transform != null && box.Transform.Length == 12)
            {
                var n = box.Transform;
                t.Origin = new XYZ(n[0], n[1], n[2]) / MetresPerFoot;
                t.BasisX = new XYZ(n[3], n[4], n[5]);
                t.BasisY = new XYZ(n[6], n[7], n[8]);
                t.BasisZ = new XYZ(n[9], n[10], n[11]);
            }
            return new BoundingBoxXYZ { Transform = t, Min = Ft(box.Min), Max = Ft(box.Max) };
        }

        /// <summary>Feet → metres.</summary>
        private static double[] M(XYZ p) => new[] { p.X * MetresPerFoot, p.Y * MetresPerFoot, p.Z * MetresPerFoot };

        /// <summary>Direction (unitless).</summary>
        private static double[] D(XYZ v) => new[] { v.X, v.Y, v.Z };

        /// <summary>Metres → feet.</summary>
        private static XYZ Ft(double[] m) => new XYZ(m[0], m[1], m[2]) / MetresPerFoot;

        private static XYZ Dir(double[] v) => new XYZ(v[0], v[1], v[2]).Normalize();
    }
}
