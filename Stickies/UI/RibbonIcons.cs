using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Stickies.UI
{
    /// <summary>
    /// Ribbon images drawn in code, so they follow Revit's light/dark theme and match the weight of the Stickies icon:
    /// thin rounded strokes on a 32-unit grid (the Material Design glyphs were too heavy next to it).
    /// </summary>
    public static class RibbonIcons
    {
        /// <summary>The Markers button: an open eye (markers shown) or a struck-through eye (hidden).</summary>
        public static ImageSource Eye(bool open, int size, Color color)
        {
            var scale = size / 32.0;
            // Thin at 32 px; a little heavier than a straight scale-down at 16 px so it stays crisp.
            var pen = new Pen(new SolidColorBrush(color), size >= 32 ? 2.0 : 1.4 / scale)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();

            var outline = Geometry.Parse("M 3,16 C 7,9.5 11.5,7 16,7 C 20.5,7 25,9.5 29,16 C 25,22.5 20.5,25 16,25 C 11.5,25 7,22.5 3,16 Z");
            var pupil = new EllipseGeometry(new Point(16, 16), 4.5, 4.5);

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawGeometry(null, pen, outline);
                dc.DrawGeometry(null, pen, pupil);
                if (!open) dc.DrawLine(pen, new Point(6, 27), new Point(26, 5));
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
