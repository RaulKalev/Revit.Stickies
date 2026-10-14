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

        /// <summary>Markers with their sticky-note cards, laid out as the card overlay does (card up and right of the marker).</summary>
        public static BitmapSource PreviewWithCards()
        {
            const int w = 720, h = 330;
            var notes = new[]
            {
                new MarkerInfo("a", 3, null, "v", IssueStatus.Open, false, "Camera too close to door", "Move VVS-03 at least 500 mm from the frame", "joonas", IssuePriority.High),
                new MarkerInfo("b", 7, null, "v", IssueStatus.InProgress, true, "Missing card reader", "Staff entrance, outside the door", null),
                new MarkerInfo("c", 12, null, "v", IssueStatus.Resolved, false, "Cable tray height clashes with the duct along the whole corridor on level 2", null, "raul", IssuePriority.Low)
            };
            var anchors = new[] { new Point(90, 230), new Point(330, 270), new Point(530, 180) };

            var canvas = new System.Windows.Controls.Canvas { Width = w, Height = h, Background = new SolidColorBrush(Color.FromRgb(0x21, 0x24, 0x28)) };
            var line = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6));
            canvas.Children.Add(new System.Windows.Shapes.Rectangle { Width = 680, Height = 290, Stroke = line, StrokeThickness = 2 });
            System.Windows.Controls.Canvas.SetLeft(canvas.Children[0], 20);
            System.Windows.Controls.Canvas.SetTop(canvas.Children[0], 20);

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var mesh = new MarkerMesh();
                for (var i = 0; i < notes.Length; i++)
                {
                    var a = anchors[i];
                    mesh.AddMarker(notes[i].Number, MarkerInfo.StatusColor(notes[i].Status), notes[i].IsSelected, (x, y, layer) => new V3(a.X + x, a.Y - y, layer));
                }
                foreach (var t in Enumerable.Range(0, mesh.TriangleCount).Select(t => new[] { mesh.Indices[3 * t], mesh.Indices[3 * t + 1], mesh.Indices[3 * t + 2] }).OrderBy(t => mesh.Vertices[t[0]].Position.Z))
                {
                    var v = mesh.Vertices[t[0]];
                    var brush = new SolidColorBrush(Color.FromRgb(v.R, v.G, v.B));
                    var g = new StreamGeometry();
                    using (var s = g.Open())
                    {
                        s.BeginFigure(new Point(v.Position.X, v.Position.Y), true, true);
                        s.LineTo(new Point(mesh.Vertices[t[1]].Position.X, mesh.Vertices[t[1]].Position.Y), false, false);
                        s.LineTo(new Point(mesh.Vertices[t[2]].Position.X, mesh.Vertices[t[2]].Position.Y), false, false);
                    }
                    dc.DrawGeometry(brush, new Pen(brush, 0.6), g);
                }
            }
            canvas.Children.Add(new System.Windows.Controls.Image { Source = new DrawingImage(visual.Drawing), Stretch = Stretch.None });
            System.Windows.Controls.Canvas.SetLeft(canvas.Children[1], visual.Drawing.Bounds.Left);
            System.Windows.Controls.Canvas.SetTop(canvas.Children[1], visual.Drawing.Bounds.Top);

            for (var i = 0; i < notes.Length; i++)
            {
                var card = Stickies.UI.StickyCard.Build(notes[i]);
                card.Measure(new Size(Stickies.UI.StickyCard.MaxCardWidth, double.PositiveInfinity));
                var at = Stickies.UI.StickyCard.Place(anchors[i].X, anchors[i].Y, card.DesiredSize, w, h);
                System.Windows.Controls.Canvas.SetLeft(card, at.X);
                System.Windows.Controls.Canvas.SetTop(card, at.Y);
                canvas.Children.Add(card);
            }

            canvas.Measure(new Size(w, h));
            canvas.Arrange(new Rect(0, 0, w, h));
            canvas.UpdateLayout();
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            return bitmap;
        }

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
