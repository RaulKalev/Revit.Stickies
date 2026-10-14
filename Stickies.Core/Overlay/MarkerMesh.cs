using System;
using System.Collections.Generic;
using System.Globalization;

namespace Stickies.Core.Overlay
{
    /// <summary>A coloured vertex of the marker mesh.</summary>
    public struct MarkerVertex
    {
        public MarkerVertex(V3 position, byte r, byte g, byte b)
        {
            Position = position;
            R = r;
            G = g;
            B = b;
        }

        public V3 Position { get; }
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }
    }

    /// <summary>
    /// Builds marker geometry as plain coloured triangles: a ring, a disc in the status colour and the issue number in
    /// seven-segment digits (DirectContext3D cannot draw text). Shapes are laid out in screen pixels around the marker
    /// centre; the caller's <c>toWorld(xPx, yPx, layer)</c> places them in the view, so the marker keeps its on-screen
    /// size at any zoom. Layer 0 is the ring (furthest from the viewer), 1 the disc, 2 the digits (nearest).
    /// </summary>
    public sealed class MarkerMesh
    {
        public const double Radius = 11;
        public const double SelectedRadius = 14;
        public const double RingWidth = 2;
        private const int DiscSegments = 28;
        private const double DigitHeight = 10, DigitWidth = 5.6, DigitStroke = 1.6, DigitGap = 2.4;

        // Seven segments a..g in a [0,1]x[0,1] cell (y up): a top, b top-right, c bottom-right, d bottom, e bottom-left, f top-left, g middle.
        private static readonly double[][] Segments =
        {
            new[] { 0.0, 1, 1, 1 }, new[] { 1.0, 1, 1, 0.5 }, new[] { 1.0, 0.5, 1, 0 }, new[] { 0.0, 0, 1, 0 },
            new[] { 0.0, 0, 0, 0.5 }, new[] { 0.0, 0.5, 0, 1 }, new[] { 0.0, 0.5, 1, 0.5 }
        };

        private static readonly string[] Digits = { "abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgedc", "abc", "abcdefg", "abcdfg" };

        public List<MarkerVertex> Vertices { get; } = new List<MarkerVertex>();

        /// <summary>Vertex indices, three per triangle.</summary>
        public List<int> Indices { get; } = new List<int>();

        public int TriangleCount => Indices.Count / 3;

        /// <summary>The radius in pixels a marker with this number is drawn with (wide numbers grow the disc).</summary>
        public static double RadiusFor(int? number, bool selected)
        {
            var textHalf = TextWidth(number?.ToString(CultureInfo.InvariantCulture) ?? "", 1) / 2 + 4;
            return Math.Max(selected ? SelectedRadius : Radius, textHalf);
        }

        public void AddMarker(int? number, byte[] rgb, bool selected, Func<double, double, int, V3> toWorld)
        {
            var radius = RadiusFor(number, selected);
            var ring = selected ? new byte[] { 0xFF, 0xFF, 0xFF } : new byte[] { 0x1E, 0x1E, 0x20 };
            AddDisc(radius + (selected ? RingWidth + 1 : RingWidth), ring, 0, toWorld);
            AddDisc(radius, rgb, 1, toWorld);
            if (number.HasValue) AddNumber(number.Value.ToString(CultureInfo.InvariantCulture), selected ? 1.2 : 1.0, toWorld);
        }

        /// <summary>Width of the number in pixels: digits are <see cref="DigitWidth"/> wide, a one is one stroke wide.</summary>
        private static double TextWidth(string text, double scale)
        {
            if (text.Length == 0) return 0;
            var width = 0.0;
            foreach (var ch in text) width += ch == '1' ? DigitStroke : DigitWidth;
            return (width + (text.Length - 1) * DigitGap) * scale;
        }

        private void AddDisc(double radius, byte[] rgb, int layer, Func<double, double, int, V3> toWorld)
        {
            var centre = Add(toWorld(0, 0, layer), rgb);
            var first = Vertices.Count;
            for (var i = 0; i < DiscSegments; i++)
            {
                var a = 2 * Math.PI * i / DiscSegments;
                Add(toWorld(radius * Math.Cos(a), radius * Math.Sin(a), layer), rgb);
            }
            for (var i = 0; i < DiscSegments; i++)
                Triangle(centre, first + i, first + (i + 1) % DiscSegments);
        }

        private void AddNumber(string text, double scale, Func<double, double, int, V3> toWorld)
        {
            var white = new byte[] { 0xFF, 0xFF, 0xFF };
            double w = DigitWidth * scale, h = DigitHeight * scale, gap = DigitGap * scale, stroke = DigitStroke * scale;
            var x0 = -TextWidth(text, scale) / 2;
            var y0 = -h / 2;
            foreach (var ch in text)
            {
                if (ch == '1')
                {
                    // A narrow one, as on real seven-segment displays: a full-width cell would look like a gap.
                    AddStroke(x0 + stroke / 2, y0, x0 + stroke / 2, y0 + h, stroke, white, toWorld);
                    x0 += stroke + gap;
                    continue;
                }
                foreach (var s in Digits[ch - '0'])
                {
                    var seg = Segments[s - 'a'];
                    AddStroke(x0 + seg[0] * w, y0 + seg[1] * h, x0 + seg[2] * w, y0 + seg[3] * h, stroke, white, toWorld);
                }
                x0 += w + gap;
            }
        }

        /// <summary>A straight stroke with square ends (half the stroke past each end, so joints close).</summary>
        private void AddStroke(double x1, double y1, double x2, double y2, double stroke, byte[] rgb, Func<double, double, int, V3> toWorld)
        {
            var dx = x2 - x1;
            var dy = y2 - y1;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) return;
            dx /= len;
            dy /= len;
            var half = stroke / 2;
            double ex = dx * half, ey = dy * half; // along
            double nx = -dy * half, ny = dx * half; // across
            var a = Add(toWorld(x1 - ex + nx, y1 - ey + ny, 2), rgb);
            var b = Add(toWorld(x1 - ex - nx, y1 - ey - ny, 2), rgb);
            var c = Add(toWorld(x2 + ex - nx, y2 + ey - ny, 2), rgb);
            var d = Add(toWorld(x2 + ex + nx, y2 + ey + ny, 2), rgb);
            Triangle(a, b, c);
            Triangle(a, c, d);
        }

        private int Add(V3 p, byte[] rgb)
        {
            Vertices.Add(new MarkerVertex(p, rgb[0], rgb[1], rgb[2]));
            return Vertices.Count - 1;
        }

        private void Triangle(int a, int b, int c)
        {
            Indices.Add(a);
            Indices.Add(b);
            Indices.Add(c);
        }
    }
}
