using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using Stickies.Core.Overlay;
using Stickies.Infrastructure;

namespace Stickies.Revit.Overlay
{
    /// <summary>A marker as placed in one view: its point in feet and how it looks.</summary>
    internal sealed class PlacedMarker
    {
        public PlacedMarker(string issueId, int? number, V3 pointFeet, byte[] rgb, bool selected, MarkerInfo info = null)
        {
            Info = info;
            IssueId = issueId;
            Number = number;
            PointFeet = pointFeet;
            Rgb = rgb;
            Selected = selected;
        }

        public string IssueId { get; }
        public int? Number { get; }
        public V3 PointFeet { get; }
        public byte[] Rgb { get; }
        public bool Selected { get; }

        /// <summary>The issue behind the marker (card text, the view it was created in).</summary>
        public MarkerInfo Info { get; }
    }

    /// <summary>Where markers landed in a view's last drawing, in pixels of that view (y down).</summary>
    internal sealed class ViewProjection
    {
        public ViewProjection(double width, double height, IReadOnlyDictionary<string, Point2> points)
        {
            Width = width;
            Height = height;
            Points = points;
        }

        public double Width { get; }
        public double Height { get; }
        public IReadOnlyDictionary<string, Point2> Points { get; }
    }

    internal struct Point2
    {
        public Point2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }
    }

    /// <summary>
    /// Draws review markers with DirectContext3D: a screen-space overlay, never Revit elements, so nothing reaches the
    /// model. Revit may call <see cref="RenderScene"/> on another thread, so this class reads only the immutable
    /// per-view snapshot that <see cref="MarkerOverlay"/> prepares on Revit's main thread – never the Revit model.
    ///
    /// Markers keep a constant on-screen size (pixel size from the camera extent and the view rectangle) and are drawn
    /// just in front of the camera's near plane, so model geometry never hides them.
    /// </summary>
    internal sealed class MarkerServer : IDirectContext3DServer
    {
        public static readonly Guid ServerId = new Guid("C6E1F0B2-3D4A-4B8E-9F57-1A2B3C4D5E6F");

        /// <summary>Per view id: the markers to draw there. Replaced as a whole, never mutated.</summary>
        private volatile IReadOnlyDictionary<long, IReadOnlyList<PlacedMarker>> _scenes = new Dictionary<long, IReadOnlyList<PlacedMarker>>();
        private volatile bool _visible = true;
        private int _suspended;
        private bool _loggedRenderError;

        /// <summary>How far a marker sits in front of its point, toward the viewer (feet), and between its layers.</summary>
        private const double Nudge = 0.5, LayerStep = 0.02;
        private const int KeptFrames = 16;

        // Buffers handed to Revit stay alive for the last few frames.
        private readonly Queue<List<IDisposable>> _frames = new Queue<List<IDisposable>>();
        private readonly HashSet<string> _logged = new HashSet<string>();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, ViewProjection> _projections =
            new System.Collections.Concurrent.ConcurrentDictionary<long, ViewProjection>();

        /// <summary>Where the markers of a view landed when it was last drawn (null when it has not been drawn).</summary>
        public ViewProjection ProjectionOf(long viewId) => _projections.TryGetValue(viewId, out var p) ? p : null;
        private readonly object _frameLock = new object();

        public bool Visible { get => _visible; set => _visible = value; }

        /// <summary>Printing or exporting is in progress: draw nothing (belt and braces – see MarkerOverlay).</summary>
        public bool Suspended => System.Threading.Volatile.Read(ref _suspended) > 0;
        public void Suspend() => System.Threading.Interlocked.Increment(ref _suspended);
        public void Resume() { if (System.Threading.Interlocked.Decrement(ref _suspended) < 0) System.Threading.Interlocked.Exchange(ref _suspended, 0); }

        public IReadOnlyDictionary<long, IReadOnlyList<PlacedMarker>> Scenes
        {
            get => _scenes;
            set => _scenes = value ?? new Dictionary<long, IReadOnlyList<PlacedMarker>>();
        }

        // ------------------------------------------------------------------ IExternalServer

        public Guid GetServerId() => ServerId;
        public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;
        public string GetName() => "Stickies markers";
        public string GetVendorId() => "RKTools";
        public string GetDescription() => "Draws review issue markers on views (screen overlay, not printed or exported).";

        // ------------------------------------------------------------------ IDirectContext3DServer

        public string GetApplicationId() => "";
        public string GetSourceId() => "";
        public bool UsesHandles() => false;
        public bool UseInTransparentPass(View dBView) => false;

        public bool CanExecute(View dBView)
        {
            var result = dBView != null && _visible && !Suspended && _scenes.TryGetValue(dBView.Id.Value, out var markers) && markers.Count > 0;
            if (dBView != null && LogOnce("can:" + dBView.Id.Value + ":" + result))
                AppLog.Info("Markers: Revit asks about view " + dBView.Id.Value + " (" + dBView.Name + "): " + (result ? "drawing" : "nothing to draw") +
                            (_scenes.ContainsKey(dBView.Id.Value) ? "" : " (no scene for this view)") + (_visible ? "" : " (markers hidden)"));
            return result;
        }

        public Outline GetBoundingBox(View dBView)
        {
            var points = dBView == null
                ? _scenes.Values.SelectMany(m => m).Select(m => m.PointFeet).ToList()
                : (_scenes.TryGetValue(dBView.Id.Value, out var markers) ? markers.Select(m => m.PointFeet).ToList() : new List<V3>());
            if (points.Count == 0) return new Outline(XYZ.Zero, new XYZ(1, 1, 1));
            const double pad = 1; // feet
            return new Outline(
                new XYZ(points.Min(p => p.X) - pad, points.Min(p => p.Y) - pad, points.Min(p => p.Z) - pad),
                new XYZ(points.Max(p => p.X) + pad, points.Max(p => p.Y) + pad, points.Max(p => p.Z) + pad));
        }

        public void RenderScene(View dBView, DisplayStyle displayStyle)
        {
            try
            {
                if (!DrawContext.IsAvailable() || DrawContext.IsTransparentPass() || Suspended || !_visible) return;
                if (!_scenes.TryGetValue(dBView.Id.Value, out var markers) || markers.Count == 0) return;

                var camera = DrawContext.GetCamera();
                var rect = DrawContext.GetViewRectangle();
                var widthPx = (double)(rect.Right - rect.Left);
                if (widthPx < 1 || camera.HorizontalExtent <= 0) return;

                var dir = V(camera.ViewDirection).Normalize();
                var up = V(camera.UpDirection).Normalize();
                var right = dir.Cross(up).Normalize();
                var eye = V(camera.EyePosition);
                var perspective = camera.ProjectionMethod == ProjectionMethod.Perspective;
                var near = camera.NearDistance;
                var feetPerPixel = camera.HorizontalExtent / widthPx; // at the target distance
                var targetDistance = Math.Max(camera.TargetDistance, 1e-6);

                if (LogOnce("render:" + dBView.Id.Value))
                    AppLog.Info("Markers: first draw of view " + dBView.Id.Value + " (" + markers.Count + " markers) camera " +
                                (perspective ? "perspective" : "orthographic") + " eye " + eye + " dir " + dir + " up " + up +
                                " near " + near + " far " + camera.FarDistance + " target " + camera.TargetDistance +
                                " extent " + camera.HorizontalExtent + " x " + camera.VerticalExtent + " ft, view " + widthPx +
                                " px wide => " + feetPerPixel + " ft/px; first marker at " + markers[0].PointFeet +
                                " depth " + (markers[0].PointFeet - eye).Dot(dir));

                var heightPx = Math.Abs((double)(rect.Bottom - rect.Top));
                var points = new Dictionary<string, Point2>();
                var meshes = new List<MarkerMesh> { new MarkerMesh() };
                foreach (var m in markers)
                {
                    var rel = m.PointFeet - eye;
                    var d = rel.Dot(dir);
                    if (perspective && d <= near) continue; // behind the camera

                    // Screen position (pixels of this view, y down) for the sticky-note cards.
                    var k = perspective ? targetDistance / d : 1;
                    points[m.IssueId] = new Point2(
                        widthPx / 2 + (rel.Dot(right) * k - camera.HorizontalOffset) / feetPerPixel,
                        heightPx / 2 - (rel.Dot(up) * k - camera.VerticalOffset) / feetPerPixel);

                    // At the issue's own point, nudged toward the viewer so the surface it sits on does not cover it.
                    // (Revit clips overlay graphics to what the view shows, so the marker must stay near its point.)
                    Func<double, double, int, V3> toWorld = (x, y, layer) =>
                    {
                        var nudge = Nudge + layer * LayerStep; // higher layers nearer the viewer
                        if (perspective)
                        {
                            var depth = Math.Max(d - nudge, near + LayerStep * 4);
                            var fppAtDepth = feetPerPixel * depth / targetDistance;
                            return eye + rel * (depth / d) + right * (x * fppAtDepth) + up * (y * fppAtDepth);
                        }
                        return m.PointFeet - dir * nudge + right * (x * feetPerPixel) + up * (y * feetPerPixel);
                    };

                    if (meshes[meshes.Count - 1].Vertices.Count > 60000) meshes.Add(new MarkerMesh()); // 16-bit indices
                    meshes[meshes.Count - 1].AddMarker(m.Number, m.Rgb, m.Selected, toWorld);
                }

                _projections[dBView.Id.Value] = new ViewProjection(widthPx, heightPx, points);

                lock (_frameLock)
                {
                    var frame = new List<IDisposable>();
                    foreach (var mesh in meshes) Flush(mesh, frame);
                    // Revit may draw a view several times (passes, tiles, several open views) before it is done with
                    // the buffers, so keep the last few frames alive instead of disposing at the next call.
                    _frames.Enqueue(frame);
                    while (_frames.Count > KeptFrames)
                        foreach (var d in _frames.Dequeue()) d.Dispose();
                }
            }
            catch (Exception ex)
            {
                if (_loggedRenderError) return;
                _loggedRenderError = true; // once per session: RenderScene runs on every redraw
                AppLog.Error("Drawing markers failed", ex);
            }
        }

        private void Flush(MarkerMesh mesh, List<IDisposable> frame)
        {
            var vertexCount = mesh.Vertices.Count;
            var triangleCount = mesh.TriangleCount;
            if (vertexCount == 0 || triangleCount == 0) return;

            var vertexFloats = VertexPositionColored.GetSizeInFloats() * vertexCount;
            var vertices = new VertexBuffer(vertexFloats);
            frame.Add(vertices);
            vertices.Map(vertexFloats);
            var vs = vertices.GetVertexStreamPositionColored();
            foreach (var v in mesh.Vertices)
                vs.AddVertex(new VertexPositionColored(new XYZ(v.Position.X, v.Position.Y, v.Position.Z), new ColorWithTransparency(v.R, v.G, v.B, 0)));
            vertices.Unmap();

            var indexShorts = IndexTriangle.GetSizeInShortInts() * triangleCount;
            var indices = new IndexBuffer(indexShorts);
            frame.Add(indices);
            indices.Map(indexShorts);
            var ts = indices.GetIndexStreamTriangle();
            for (var i = 0; i < mesh.Indices.Count; i += 3)
                ts.AddTriangle(new IndexTriangle(mesh.Indices[i], mesh.Indices[i + 1], mesh.Indices[i + 2]));
            indices.Unmap();

            var format = new VertexFormat(VertexFormatBits.PositionColored);
            var effect = new EffectInstance(VertexFormatBits.PositionColored);
            frame.Add(format);
            frame.Add(effect);
            DrawContext.FlushBuffer(vertices, vertexCount, indices, triangleCount * 3, format, effect, PrimitiveType.TriangleList, 0, triangleCount);
        }

        /// <summary>True the first time a key is seen this session (diagnostics without flooding the log on every redraw).</summary>
        private bool LogOnce(string key)
        {
            lock (_logged) return _logged.Add(key);
        }

        private static V3 V(XYZ p) => new V3(p.X, p.Y, p.Z);
    }
}
