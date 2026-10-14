using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Stickies.Core.Models;
using Stickies.Core.Overlay;

namespace Stickies.UI
{
    /// <summary>
    /// The sticky-note card shown next to a marker: a pale yellow note with the number and status, the title, the first
    /// lines of the details and the assignee. Always yellow (it is a sticky note), whatever the Revit or pane theme.
    /// </summary>
    public static class StickyCard
    {
        public const double MaxCardWidth = 210;

        private static readonly FontFamily Font = new FontFamily("Segoe UI Variable Text, Segoe UI");
        private static readonly Brush Paper = Frozen(Color.FromRgb(0xFF, 0xF4, 0xB8));
        private static readonly Brush Edge = Frozen(Color.FromRgb(0xE3, 0xCF, 0x6E));
        private static readonly Brush Ink = Frozen(Color.FromRgb(0x1F, 0x1F, 0x1F));
        private static readonly Brush InkSoft = Frozen(Color.FromRgb(0x4A, 0x46, 0x3A));
        private static readonly Brush InkFaint = Frozen(Color.FromRgb(0x7A, 0x72, 0x58));
        private static readonly Brush Selected = Frozen(Color.FromRgb(0x0A, 0x84, 0xFF));
        private static readonly Brush HighPriority = Frozen(Color.FromRgb(0xC4, 0x2B, 0x1C));

        private const double OffsetX = 14, OffsetY = 10;

        /// <summary>
        /// Top-left corner for a card of <paramref name="size"/> next to a marker at (x, y) in a view of the given size:
        /// up and to the right of the marker, flipped left near the right edge and below near the top edge.
        /// </summary>
        public static Point Place(double x, double y, Size size, double viewWidth, double viewHeight)
        {
            var left = x + OffsetX;
            if (left + size.Width > viewWidth && x - OffsetX - size.Width >= 0) left = x - OffsetX - size.Width;
            var top = y - OffsetY - size.Height;
            if (top < 0 && y + OffsetY + size.Height <= viewHeight) top = y + OffsetY;
            return new Point(left, top);
        }

        public static FrameworkElement Build(MarkerInfo m)
        {
            var rgb = MarkerInfo.StatusColor(m.Status);

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            if (m.Priority == IssuePriority.High)
            {
                var high = Text("High", 10.5, HighPriority, FontWeights.SemiBold);
                DockPanel.SetDock(high, Dock.Right);
                header.Children.Add(high);
            }
            header.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new Ellipse { Width = 8, Height = 8, Fill = Frozen(Color.FromRgb(rgb[0], rgb[1], rgb[2])), Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center },
                    Text(m.Number.HasValue ? "#" + m.Number.Value : "Issue", 11, InkFaint, FontWeights.SemiBold)
                }
            });

            var content = new StackPanel();
            content.Children.Add(header);
            content.Children.Add(Lines(string.IsNullOrWhiteSpace(m.Title) ? "(untitled)" : m.Title, 12.5, Ink, FontWeights.SemiBold, 2));
            if (!string.IsNullOrWhiteSpace(m.Body))
                content.Children.Add(Lines(m.Body, 11.5, InkSoft, FontWeights.Normal, 3, new Thickness(0, 2, 0, 0)));
            if (!string.IsNullOrWhiteSpace(m.Assignee))
                content.Children.Add(Text("→ " + m.Assignee, 11, InkFaint, FontWeights.Normal, new Thickness(0, 4, 0, 0)));

            return new Border
            {
                Child = content,
                MaxWidth = MaxCardWidth,
                MinWidth = 120,
                Padding = new Thickness(9, 7, 9, 8),
                Background = Paper,
                BorderBrush = m.IsSelected ? Selected : Edge,
                BorderThickness = new Thickness(m.IsSelected ? 2 : 1),
                CornerRadius = new CornerRadius(3),
                SnapsToDevicePixels = true,
                Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Direction = 270, Opacity = 0.28, Color = Colors.Black },
                IsHitTestVisible = false
            };
        }

        private static TextBlock Text(string text, double size, Brush brush, FontWeight weight, Thickness margin = default(Thickness)) =>
            new TextBlock { Text = text, FontFamily = Font, FontSize = size, Foreground = brush, FontWeight = weight, Margin = margin, TextTrimming = TextTrimming.CharacterEllipsis };

        /// <summary>Wrapped text cut to a number of lines.</summary>
        private static TextBlock Lines(string text, double size, Brush brush, FontWeight weight, int lines, Thickness margin = default(Thickness))
        {
            var block = Text(text.Trim(), size, brush, weight, margin);
            block.TextWrapping = TextWrapping.Wrap;
            block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            block.LineHeight = size * 1.3;
            block.MaxHeight = block.LineHeight * lines;
            return block;
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
