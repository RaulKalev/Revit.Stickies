using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Stickies.Core.Models;
using Stickies.Core.Overlay;
using Stickies.Session;

namespace Stickies.UiHarness
{
    /// <summary>Records what the pane would draw, and clicks markers like a user would.</summary>
    internal sealed class FakeMarkerSink : IMarkerSink
    {
        public IReadOnlyList<MarkerInfo> Last { get; private set; } = new MarkerInfo[0];
        public int PublishCount { get; private set; }

        public event Action<string> MarkerClicked;

        public void Publish(IReadOnlyList<MarkerInfo> markers)
        {
            Last = markers;
            PublishCount++;
        }

        public void Click(string issueId) => MarkerClicked?.Invoke(issueId);

        /// <summary>
        /// Draws markers with the real <see cref="MarkerMesh"/> the way Revit gets them (triangles, painter's order by
        /// layer) over a plan-like background, so their look can be checked without Revit.
        /// </summary>
        public static BitmapSource Preview(bool dark, double zoom = 1)
        {
            int w = (int)(520 * zoom), h = (int)(150 * zoom);
            var samples = new[]
            {
                Tuple.Create((int?)1, IssueStatus.Open, false),
                Tuple.Create((int?)7, IssueStatus.InProgress, false),
                Tuple.Create((int?)12, IssueStatus.Resolved, false),
                Tuple.Create((int?)128, IssueStatus.Closed, false),
                Tuple.Create((int?)45, IssueStatus.Open, true),
                Tuple.Create((int?)2048, IssueStatus.Open, false)
            };

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(0x21, 0x24, 0x28) : Colors.White), null, new Rect(0, 0, w, h));
                var line = new Pen(new SolidColorBrush(dark ? Color.FromRgb(0x9A, 0xA0, 0xA6) : Color.FromRgb(0x40, 0x40, 0x40)), 2);
                dc.DrawRectangle(null, line, new Rect(20 * zoom, 25 * zoom, 480 * zoom, 100 * zoom)); // a room
                dc.DrawLine(line, new Point(260 * zoom, 25 * zoom), new Point(260 * zoom, 125 * zoom));

                var mesh = new MarkerMesh();
                for (var i = 0; i < samples.Length; i++)
                {
                    double cx = (55 + i * 82) * zoom, cy = 75 * zoom;
                    mesh.AddMarker(samples[i].Item1, MarkerInfo.StatusColor(samples[i].Item2), samples[i].Item3,
                        (x, y, layer) => new V3(cx + x * zoom, cy - y * zoom, layer)); // screen y points down
                }
                var triangles = Enumerable.Range(0, mesh.TriangleCount)
                    .Select(t => new[] { mesh.Indices[3 * t], mesh.Indices[3 * t + 1], mesh.Indices[3 * t + 2] })
                    .OrderBy(t => mesh.Vertices[t[0]].Position.Z);
                foreach (var t in triangles)
                {
                    var a = mesh.Vertices[t[0]];
                    var brush = new SolidColorBrush(Color.FromRgb(a.R, a.G, a.B));
                    var geometry = new StreamGeometry();
                    using (var g = geometry.Open())
                    {
                        g.BeginFigure(new Point(a.Position.X, a.Position.Y), true, true);
                        g.LineTo(new Point(mesh.Vertices[t[1]].Position.X, mesh.Vertices[t[1]].Position.Y), false, false);
                        g.LineTo(new Point(mesh.Vertices[t[2]].Position.X, mesh.Vertices[t[2]].Position.Y), false, false);
                    }
                    dc.DrawGeometry(brush, new Pen(brush, 0.6), geometry); // thin edge hides seams, like GPU rasterisation
                }
            }
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            return bitmap;
        }
    }
}
