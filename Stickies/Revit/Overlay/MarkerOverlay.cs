using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Stickies.Core.Overlay;
using Stickies.Infrastructure;
using Stickies.Overlay;
using Stickies.Session;

namespace Stickies.Revit.Overlay
{
    /// <summary>
    /// Puts the pane's markers on Revit views. On Revit's main thread (API context) it works out which markers each open
    /// view shows and where each view sits on screen, and hands an immutable snapshot to <see cref="MarkerServer"/>,
    /// which draws it. A marker shows in the view its issue was created in, and in other views that show its point:
    /// 3D views (inside the section box), plans (inside the view range and crop) and sections/elevations (inside the crop
    /// box). Clicking a marker in a plan, section, elevation or orthographic 3D view selects its issue in the pane.
    ///
    /// Printing and exporting never draw markers: DirectContext3D graphics are not part of the model, and drawing is
    /// also switched off from the start to the end of every print and export, in case a print path renders views.
    /// </summary>
    internal sealed class MarkerOverlay : IMarkerSink, IDisposable
    {
        private const double FeetPerMetre = 1 / 0.3048;

        private readonly UIControlledApplication _app;
        private readonly RevitRequests _requests;
        private readonly OverlayState _overlay;
        private readonly Dispatcher _dispatcher;
        private readonly MarkerServer _server = new MarkerServer();
        private readonly MouseHook _hook;
        private bool _registered, _dirty = true;

        private IReadOnlyList<MarkerInfo> _markers = new MarkerInfo[0];
        private volatile IReadOnlyList<ViewMapping> _mappings = new ViewMapping[0];

        public MarkerOverlay(UIControlledApplication app, RevitRequests requests, OverlayState overlay)
        {
            _app = app;
            _requests = requests;
            _overlay = overlay;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _server.Visible = overlay.IsVisible;

            Register();
            _overlay.Changed += OnOverlayChanged;
            _app.ViewActivated += OnViewActivated;
            _app.Idling += OnIdling;
            var c = _app.ControlledApplication;
            c.DocumentChanged += OnDocumentChanged;
            c.DocumentPrinting += OnOutputStarting;
            c.DocumentPrinted += OnOutputFinished;
            c.FileExporting += OnOutputStarting;
            c.FileExported += OnOutputFinished;
            _hook = new MouseHook(OnClick);
        }

        public event Action<string> MarkerClicked;

        // ------------------------------------------------------------------ registration

        private void Register()
        {
            try
            {
                var service = (MultiServerService)ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService);
                if (service.IsRegisteredServerId(MarkerServer.ServerId)) service.RemoveServer(MarkerServer.ServerId); // left by an earlier load (hot reload)
                service.AddServer(_server);
                var active = service.GetActiveServerIds();
                if (!active.Contains(MarkerServer.ServerId)) active.Add(MarkerServer.ServerId);
                service.SetActiveServers(active);
                _registered = true;
            }
            catch (Exception ex)
            {
                AppLog.Error("Registering the marker overlay failed", ex);
            }
        }

        private void Unregister()
        {
            if (!_registered) return;
            try
            {
                var service = (MultiServerService)ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService);
                var active = service.GetActiveServerIds().Where(id => id != MarkerServer.ServerId).ToList();
                service.SetActiveServers(active);
                if (service.IsRegisteredServerId(MarkerServer.ServerId)) service.RemoveServer(MarkerServer.ServerId);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Unregistering the marker overlay failed: " + ex.Message);
            }
            _registered = false;
        }

        // ------------------------------------------------------------------ inputs

        /// <summary>From the pane (UI thread, not API context): rebuild in API context, then redraw.</summary>
        public void Publish(IReadOnlyList<MarkerInfo> markers)
        {
            _markers = markers ?? new MarkerInfo[0];
            _dirty = true;
            _requests.Post(app =>
            {
                Rebuild(app);
                Redraw(app);
            });
        }

        private void OnOverlayChanged(object sender, EventArgs e)
        {
            _server.Visible = _overlay.IsVisible;
            _requests.Post(Redraw);
        }

        private void OnViewActivated(object sender, ViewActivatedEventArgs e)
        {
            _dirty = true;
            if (sender is UIApplication app) Rebuild(app);
        }

        // View range, crop or section box edits change which markers a view shows.
        private void OnDocumentChanged(object sender, DocumentChangedEventArgs e) => _dirty = true;

        private void OnIdling(object sender, IdlingEventArgs e)
        {
            if (!(sender is UIApplication app)) return;
            try
            {
                if (_dirty)
                {
                    Rebuild(app);
                    Redraw(app);
                }
                else
                {
                    UpdateMappings(app.ActiveUIDocument); // pan and zoom move markers on screen
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Marker overlay update failed: " + ex.Message);
            }
        }

        private void OnOutputStarting(object sender, EventArgs e) => _server.Suspend();
        private void OnOutputFinished(object sender, EventArgs e) => _server.Resume();

        // ------------------------------------------------------------------ scenes (API context)

        private void Rebuild(UIApplication app)
        {
            _dirty = false;
            var uidoc = app?.ActiveUIDocument;
            var scenes = new Dictionary<long, IReadOnlyList<PlacedMarker>>();
            if (uidoc != null && !uidoc.Document.IsFamilyDocument && _markers.Count > 0)
            {
                var doc = uidoc.Document;
                foreach (var uiView in uidoc.GetOpenUIViews())
                {
                    if (!(doc.GetElement(uiView.ViewId) is View view)) continue;
                    var placed = new List<PlacedMarker>();
                    foreach (var m in _markers)
                    {
                        var p = Feet(m.PointMetres);
                        if (!ShowsIn(doc, view, m, p)) continue;
                        placed.Add(new PlacedMarker(m.IssueId, m.Number, new V3(p.X, p.Y, p.Z), MarkerInfo.StatusColor(m.Status), m.IsSelected));
                    }
                    // Selected last, so it is drawn over its neighbours.
                    scenes[view.Id.Value] = placed.OrderBy(x => x.Selected).ToList();
                }
            }
            _server.Scenes = scenes;
            UpdateMappings(uidoc);
        }

        private static void Redraw(UIApplication app)
        {
            try { app?.ActiveUIDocument?.RefreshActiveView(); }
            catch (Exception ex) { AppLog.Warn("Redraw failed: " + ex.Message); }
        }

        /// <summary>Whether a view shows the marker's point (it always shows in the view the issue was created in).</summary>
        private static bool ShowsIn(Document doc, View view, MarkerInfo marker, XYZ p)
        {
            if (view.IsTemplate) return false;
            if (!string.IsNullOrEmpty(marker.ViewUniqueId) && marker.ViewUniqueId == view.UniqueId) return true;

            switch (view)
            {
                case View3D view3D:
                    return !view3D.IsSectionBoxActive || Inside(view3D.GetSectionBox(), p, checkDepth: true);
                case ViewPlan plan:
                    return InViewRange(doc, plan, p) && (!plan.CropBoxActive || Inside(plan.CropBox, p, checkDepth: false));
                case ViewSection section: // sections, elevations, detail views
                    return section.CropBoxActive && Inside(section.CropBox, p, checkDepth: true);
                default:
                    return false; // drafting views, sheets, schedules: only the issue's own view
            }
        }

        private static bool Inside(BoundingBoxXYZ box, XYZ p, bool checkDepth)
        {
            if (box == null) return true;
            const double tol = 0.5; // feet
            var local = box.Transform.Inverse.OfPoint(p);
            return local.X >= box.Min.X - tol && local.X <= box.Max.X + tol &&
                   local.Y >= box.Min.Y - tol && local.Y <= box.Max.Y + tol &&
                   (!checkDepth || (local.Z >= box.Min.Z - tol && local.Z <= box.Max.Z + tol));
        }

        /// <summary>Between the plan's view depth and top clip (either way round, for ceiling plans).</summary>
        private static bool InViewRange(Document doc, ViewPlan plan, XYZ p)
        {
            try
            {
                var range = plan.GetViewRange();
                var top = Elevation(doc, range, PlanViewPlane.TopClipPlane);
                var depth = Elevation(doc, range, PlanViewPlane.ViewDepthPlane);
                if (double.IsNaN(depth)) depth = Elevation(doc, range, PlanViewPlane.BottomClipPlane);
                const double tol = 0.5; // feet
                if (double.IsNaN(top) && double.IsNaN(depth)) return true;
                if (double.IsNaN(top)) return p.Z >= depth - tol;   // unlimited upwards
                if (double.IsNaN(depth)) return p.Z <= top + tol;   // unlimited downwards
                return p.Z >= Math.Min(top, depth) - tol && p.Z <= Math.Max(top, depth) + tol;
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>Elevation of a view range plane, or NaN when it is unlimited.</summary>
        private static double Elevation(Document doc, PlanViewRange range, PlanViewPlane plane)
        {
            var levelId = range.GetLevelId(plane);
            if (levelId == null || levelId == ElementId.InvalidElementId || levelId == PlanViewRange.Unlimited) return double.NaN;
            return doc.GetElement(levelId) is Level level ? level.ProjectElevation + range.GetOffset(plane) : double.NaN;
        }

        // ------------------------------------------------------------------ clicks

        /// <summary>Where each open parallel-projection view sits on screen, for matching clicks to markers.</summary>
        private void UpdateMappings(UIDocument uidoc)
        {
            var mappings = new List<ViewMapping>();
            if (uidoc != null)
            {
                foreach (var uiView in uidoc.GetOpenUIViews())
                {
                    try
                    {
                        if (!(uidoc.Document.GetElement(uiView.ViewId) is View view)) continue;
                        if (view is View3D v3 && v3.IsPerspective) continue;
                        var r = uiView.GetWindowRectangle();
                        var corners = uiView.GetZoomCorners();
                        if (corners == null || corners.Count < 2) continue;
                        mappings.Add(new ViewMapping(view.Id.Value, r.Left, r.Top, r.Right, r.Bottom,
                            V(corners[0]), V(corners[1]), V(view.RightDirection), V(view.UpDirection)));
                    }
                    catch (Exception)
                    {
                        // a view that is closing; skip it
                    }
                }
            }
            _mappings = mappings;
        }

        /// <summary>From the mouse hook (UI thread, not API context): uses only the cached snapshots.</summary>
        private void OnClick(int x, int y)
        {
            if (!_server.Visible || MarkerClicked == null) return;
            var scenes = _server.Scenes;
            foreach (var mapping in _mappings)
            {
                if (!mapping.Contains(x, y) || !scenes.TryGetValue(mapping.ViewId, out var markers)) continue;
                var hit = MarkerPicker.Hit(mapping,
                    markers.Select(m => new PickCandidate(m.IssueId, m.PointFeet, MarkerMesh.RadiusFor(m.Number, m.Selected))), x, y);
                if (hit != null) _dispatcher.BeginInvoke(new Action(() => MarkerClicked?.Invoke(hit)));
                return;
            }
        }

        // ------------------------------------------------------------------ helpers

        private static XYZ Feet(double[] m) => new XYZ(m[0], m[1], m[2]) * FeetPerMetre;
        private static V3 V(XYZ p) => new V3(p.X, p.Y, p.Z);

        public void Dispose()
        {
            _hook.Dispose();
            _overlay.Changed -= OnOverlayChanged;
            _app.ViewActivated -= OnViewActivated;
            _app.Idling -= OnIdling;
            var c = _app.ControlledApplication;
            c.DocumentChanged -= OnDocumentChanged;
            c.DocumentPrinting -= OnOutputStarting;
            c.DocumentPrinted -= OnOutputFinished;
            c.FileExporting -= OnOutputStarting;
            c.FileExported -= OnOutputFinished;
            _server.Scenes = null;
            Unregister();
        }
    }
}
