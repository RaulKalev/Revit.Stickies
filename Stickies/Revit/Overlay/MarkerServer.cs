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
        public PlacedMarker(string issueId, int? number, V3 pointFeet, byte[] rgb, bool selected)
        {
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

        // Buffers handed to Revit stay alive until the next frame.
        private readonly List<IDisposable> _lastFrame = new List<IDisposable>();
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

        public bool CanExecute(View dBView) =>
            dBView != null && _visible && !Suspended && _scenes.TryGetValue(dBView.Id.Value, out var markers) && markers.Count > 0;

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
                var span = Math.Max(camera.FarDistance - near, 1);
                var layerStep = Math.Max(span * 1e-4, 0.002);
                var baseDepth = near + Math.Max(span * 1e-3, 0.05) + 3 * layerStep;
                var feetPerPixel = camera.HorizontalExtent / widthPx; // at the target distance
                var targetDistance = Math.Max(camera.TargetDistance, 1e-6);

                var meshes = new List<MarkerMesh> { new MarkerMesh() };
                foreach (var m in markers)
                {
                    var rel = m.PointFeet - eye;
                    var d = rel.Dot(dir);
                    if (perspective && d <= near) continue; // behind the camera

                    Func<double, double, int, V3> toWorld = (x, y, layer) =>
                    {
                        var depth = baseDepth - layer * layerStep; // higher layers nearer the viewer
                        V3 centre;
                        double fpp;
                        if (perspective)
                        {
                            centre = eye + rel * (depth / d);
                            fpp = feetPerPixel * depth / targetDistance;
                        }
                        else
                        {
                            centre = m.PointFeet + dir * (depth - d);
                            fpp = feetPerPixel;
                        }
                        return centre + right * (x * fpp) + up * (y * fpp);
                    };

                    if (meshes[meshes.Count - 1].Vertices.Count > 60000) meshes.Add(new MarkerMesh()); // 16-bit indices
                    meshes[meshes.Count - 1].AddMarker(m.Number, m.Rgb, m.Selected, toWorld);
                }

                lock (_frameLock)
                {
                    foreach (var d in _lastFrame) d.Dispose();
                    _lastFrame.Clear();
                    foreach (var mesh in meshes) Flush(mesh);
                }
            }
            catch (Exception ex)
            {
                if (_loggedRenderError) return;
                _loggedRenderError = true; // once per session: RenderScene runs on every redraw
                AppLog.Error("Drawing markers failed", ex);
            }
        }

        private void Flush(MarkerMesh mesh)
        {
            var vertexCount = mesh.Vertices.Count;
            var triangleCount = mesh.TriangleCount;
            if (vertexCount == 0 || triangleCount == 0) return;

            var vertexFloats = VertexPositionColored.GetSizeInFloats() * vertexCount;
            var vertices = new VertexBuffer(vertexFloats);
            _lastFrame.Add(vertices);
            vertices.Map(vertexFloats);
            var vs = vertices.GetVertexStreamPositionColored();
            foreach (var v in mesh.Vertices)
                vs.AddVertex(new VertexPositionColored(new XYZ(v.Position.X, v.Position.Y, v.Position.Z), new ColorWithTransparency(v.R, v.G, v.B, 0)));
            vertices.Unmap();

            var indexShorts = IndexTriangle.GetSizeInShortInts() * triangleCount;
            var indices = new IndexBuffer(indexShorts);
            _lastFrame.Add(indices);
            indices.Map(indexShorts);
            var ts = indices.GetIndexStreamTriangle();
            for (var i = 0; i < mesh.Indices.Count; i += 3)
                ts.AddTriangle(new IndexTriangle(mesh.Indices[i], mesh.Indices[i + 1], mesh.Indices[i + 2]));
            indices.Unmap();

            var format = new VertexFormat(VertexFormatBits.PositionColored);
            var effect = new EffectInstance(VertexFormatBits.PositionColored);
            _lastFrame.Add(format);
            _lastFrame.Add(effect);
            DrawContext.FlushBuffer(vertices, vertexCount, indices, triangleCount * 3, format, effect, PrimitiveType.TriangleList, 0, triangleCount);
        }

        private static V3 V(XYZ p) => new V3(p.X, p.Y, p.Z);
    }
}
