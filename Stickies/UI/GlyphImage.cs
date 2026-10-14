using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;

namespace Stickies.UI
{
    /// <summary>Renders a Material Design icon to a bitmap (ribbon button images), so no image files are needed.</summary>
    public static class GlyphImage
    {
        public static ImageSource Render(PackIconKind kind, int size, Color color)
        {
            var geometry = Geometry.Parse(new PackIcon { Kind = kind }.Data);
            var brush = new SolidColorBrush(color);
            brush.Freeze();

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // Material Design icons are drawn on a 24×24 grid.
                dc.PushTransform(new ScaleTransform(size / 24.0, size / 24.0));
                dc.DrawGeometry(brush, null, geometry);
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
