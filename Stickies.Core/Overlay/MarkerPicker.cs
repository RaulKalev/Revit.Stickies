using System;
using System.Collections.Generic;

namespace Stickies.Core.Overlay
{
    /// <summary>
    /// Where a view sits on screen and what it shows, so a mouse position can be matched to a marker. Built from
    /// UIView.GetWindowRectangle (screen pixels, y down) and UIView.GetZoomCorners (model points at the window's
    /// lower-left and upper-right), projected on the view's right and up directions. Valid for parallel projections
    /// (plans, sections, elevations, orthographic 3D); perspective views are not mapped.
    /// </summary>
    public sealed class ViewMapping
    {
        private readonly double _u0, _v0, _u1, _v1;

        public ViewMapping(long viewId, int left, int top, int right, int bottom, V3 lowerLeft, V3 upperRight, V3 rightDir, V3 upDir)
        {
            ViewId = viewId;
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
            RightDir = rightDir.Normalize();
            UpDir = upDir.Normalize();
            _u0 = lowerLeft.Dot(RightDir);
            _v0 = lowerLeft.Dot(UpDir);
            _u1 = upperRight.Dot(RightDir);
            _v1 = upperRight.Dot(UpDir);
        }

        public long ViewId { get; }
        public int Left { get; }
        public int Top { get; }
        public int Right { get; }
        public int Bottom { get; }
        public V3 RightDir { get; }
        public V3 UpDir { get; }

        public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

        /// <summary>Screen pixel of a model point (same units as the zoom corners).</summary>
        public bool TryToScreen(V3 p, out double x, out double y)
        {
            x = y = 0;
            var du = _u1 - _u0;
            var dv = _v1 - _v0;
            if (Math.Abs(du) < 1e-9 || Math.Abs(dv) < 1e-9) return false;
            x = Left + (p.Dot(RightDir) - _u0) / du * (Right - Left);
            y = Bottom - (p.Dot(UpDir) - _v0) / dv * (Bottom - Top);
            return true;
        }
    }

    public sealed class PickCandidate
    {
        public PickCandidate(string issueId, V3 point, double radiusPx)
        {
            IssueId = issueId;
            Point = point;
            RadiusPx = radiusPx;
        }

        public string IssueId { get; }
        public V3 Point { get; }
        public double RadiusPx { get; }
    }

    public static class MarkerPicker
    {
        /// <summary>Extra pixels around a marker that still count as a hit.</summary>
        public const double Slop = 3;

        /// <summary>The issue whose marker is under the screen point (the nearest one when markers overlap), or null.</summary>
        public static string Hit(ViewMapping mapping, IEnumerable<PickCandidate> candidates, int x, int y)
        {
            if (mapping == null || !mapping.Contains(x, y)) return null;
            string best = null;
            var bestDistance = double.MaxValue;
            foreach (var c in candidates)
            {
                if (!mapping.TryToScreen(c.Point, out var mx, out var my)) continue;
                var d = Math.Sqrt((mx - x) * (mx - x) + (my - y) * (my - y));
                if (d <= c.RadiusPx + Slop && d < bestDistance)
                {
                    best = c.IssueId;
                    bestDistance = d;
                }
            }
            return best;
        }
    }
}
