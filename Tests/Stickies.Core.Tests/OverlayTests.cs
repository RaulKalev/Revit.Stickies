using System;
using System.Linq;
using Stickies.Core.Issues;
using Stickies.Core.Models;
using Stickies.Core.Overlay;
using Stickies.Core.Persistence;
using Xunit;

namespace Stickies.Core.Tests
{
    public class OverlayTests
    {
        private static V3 Flat(double x, double y, int layer) => new V3(x, y, layer);

        [Fact]
        public void Numbers_continue_from_the_highest_and_ignore_unnumbered()
        {
            Assert.Equal(1, IssueNumbers.Next(new Issue[0]));
            Assert.Equal(1, IssueNumbers.Next(new[] { new Issue() }));
            Assert.Equal(13, IssueNumbers.Next(new[] { new Issue { Number = 3 }, new Issue { Number = 12 }, new Issue() }));
        }

        [Fact]
        public void Number_is_optional_in_the_file()
        {
            var issue = MarkupJson.Deserialize<Issue>("{\"schemaVersion\":1,\"id\":\"20261014-093012-a3f9c1\"}");
            Assert.Null(issue.Number);
            issue.Number = 7;
            Assert.Equal(7, MarkupJson.Deserialize<Issue>(MarkupJson.Serialize(issue)).Number);
        }

        [Theory]
        [InlineData(1, 1)]          // a narrow one: a single stroke
        [InlineData(8, 7)]
        [InlineData(10, 1 + 6)]
        [InlineData(1234, 1 + 5 + 5 + 4)]
        public void Digits_use_seven_segments(int number, int strokes)
        {
            var mesh = new MarkerMesh();
            mesh.AddMarker(number, new byte[] { 1, 2, 3 }, false, Flat);
            const int discTriangles = 2 * 28;
            Assert.Equal(discTriangles + strokes * 2, mesh.TriangleCount);
            Assert.All(mesh.Indices, i => Assert.InRange(i, 0, mesh.Vertices.Count - 1));
        }

        [Fact]
        public void Layers_and_colours_ring_then_disc_then_white_digits()
        {
            var mesh = new MarkerMesh();
            mesh.AddMarker(4, new byte[] { 0x0A, 0x84, 0xFF }, false, Flat);
            Assert.Contains(mesh.Vertices, v => v.Position.Z == 0 && v.R == 0x1E);
            Assert.All(mesh.Vertices.Where(v => v.Position.Z == 1), v => Assert.Equal(0x84, v.G));
            Assert.All(mesh.Vertices.Where(v => v.Position.Z == 2), v => Assert.Equal(0xFF, v.R & v.G & v.B));
        }

        [Fact]
        public void Digits_stay_inside_the_disc_and_wide_numbers_grow_it()
        {
            foreach (var n in new[] { 1, 88, 888, 8888 })
            {
                var mesh = new MarkerMesh();
                mesh.AddMarker(n, new byte[] { 0, 0, 0 }, false, Flat);
                var r = MarkerMesh.RadiusFor(n, false);
                Assert.All(mesh.Vertices.Where(v => v.Position.Z == 2),
                    v => Assert.True(Math.Sqrt(v.Position.X * v.Position.X + v.Position.Y * v.Position.Y) < r, n + " overflows"));
            }
            Assert.Equal(MarkerMesh.Radius, MarkerMesh.RadiusFor(7, false));
            Assert.True(MarkerMesh.RadiusFor(8888, false) > MarkerMesh.Radius);
            Assert.Equal(MarkerMesh.SelectedRadius, MarkerMesh.RadiusFor(7, true));
        }

        [Fact]
        public void Only_issues_created_in_3d_show_beyond_their_own_view()
        {
            Assert.True(new MarkerInfo("a", 1, null, "v", IssueStatus.Open, false, viewType: "ThreeD").ShowsBeyondOwnView);
            Assert.False(new MarkerInfo("b", 2, null, "v", IssueStatus.Open, false, viewType: "FloorPlan").ShowsBeyondOwnView);
            Assert.False(new MarkerInfo("c", 3, null, "v", IssueStatus.Open, false, viewType: "Section").ShowsBeyondOwnView);
            Assert.False(new MarkerInfo("d", 4, null, "v", IssueStatus.Open, false).ShowsBeyondOwnView);
        }

        [Fact]
        public void Selected_marker_has_a_white_ring()
        {
            var mesh = new MarkerMesh();
            mesh.AddMarker(null, new byte[] { 1, 1, 1 }, true, Flat);
            Assert.All(mesh.Vertices.Where(v => v.Position.Z == 0), v => Assert.Equal(0xFF, v.R));
            Assert.DoesNotContain(mesh.Vertices, v => v.Position.Z == 2); // no number, no digits
        }

        // A plan view 100 x 50 ft shown in an 1000 x 500 px window at screen (200, 100).
        private static ViewMapping Plan() => new ViewMapping(42, 200, 100, 1200, 600,
            new V3(0, 0, 0), new V3(100, 50, 0), new V3(1, 0, 0), new V3(0, 1, 0));

        [Fact]
        public void Mapping_projects_model_points_to_screen_with_y_down()
        {
            var m = Plan();
            Assert.True(m.TryToScreen(new V3(0, 0, 7), out var x, out var y));
            Assert.Equal(200, x, 6);
            Assert.Equal(600, y, 6);
            m.TryToScreen(new V3(50, 25, 0), out x, out y);
            Assert.Equal(700, x, 6);
            Assert.Equal(350, y, 6);
            m.TryToScreen(new V3(100, 50, 0), out x, out y);
            Assert.Equal(1200, x, 6);
            Assert.Equal(100, y, 6);
        }

        [Fact]
        public void Pick_finds_the_nearest_marker_within_its_radius()
        {
            var m = Plan();
            var candidates = new[]
            {
                new PickCandidate("a", new V3(50, 25, 0), 11),  // at (700, 350)
                new PickCandidate("b", new V3(51.5, 25, 0), 11) // at (715, 350)
            };
            Assert.Equal("a", MarkerPicker.Hit(m, candidates, 702, 352));
            Assert.Equal("b", MarkerPicker.Hit(m, candidates, 712, 350));
            Assert.Equal("b", MarkerPicker.Hit(m, candidates, 715 + 13, 350)); // radius + slop
            Assert.Null(MarkerPicker.Hit(m, candidates, 715 + 15, 350));
            Assert.Null(MarkerPicker.Hit(m, candidates, 150, 350)); // outside the view
        }

        [Fact]
        public void Section_mapping_uses_the_view_directions()
        {
            // A section looking north: right = +X, up = +Z. The point's Y (depth) does not matter.
            var m = new ViewMapping(1, 0, 0, 1000, 1000, new V3(0, 5, 0), new V3(100, 5, 100), new V3(1, 0, 0), new V3(0, 0, 1));
            m.TryToScreen(new V3(25, 999, 75), out var x, out var y);
            Assert.Equal(250, x, 6);
            Assert.Equal(250, y, 6);
        }
    }
}
