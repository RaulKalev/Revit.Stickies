using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Stickies.Core.Models;
using Stickies.Core.Persistence;
using Xunit;

namespace Stickies.Core.Tests
{
    public class SerializationTests
    {
        private static readonly TimeSpan Tallinn = TimeSpan.FromHours(3);

        internal static Issue FullIssue() => new Issue
        {
            Id = "20261014-093012-a3f9c1",
            Title = "Camera too close to door",
            Body = "Move VVS-03 at least 500 mm from the frame",
            Status = IssueStatus.InProgress,
            Priority = IssuePriority.High,
            Author = "raul",
            Assignee = "joonas",
            Created = new DateTimeOffset(2026, 10, 14, 9, 30, 12, Tallinn),
            Modified = new DateTimeOffset(2026, 10, 14, 10, 2, 0, Tallinn),
            ResolvedBy = null,
            ResolvedAt = null,
            Location = new IssueLocation
            {
                Point = new[] { 12.345, 6.789, 3.0 },
                ElementUniqueId = "7a1c0e2b-1111-2222-3333-444455556666-00041f2a",
                ElementCategory = "Communication Devices"
            },
            View = new IssueView
            {
                UniqueId = "view-uid",
                Name = "EN_2_korrus_VVS",
                Type = "ThreeD",
                SectionBox = new SectionBox
                {
                    Min = new[] { -1.0, -2.0, -3.0 },
                    Max = new[] { 4.0, 5.0, 6.0 },
                    Transform = new[] { 0.0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 }
                },
                Orientation = new ViewOrientation { Eye = new[] { 10.0, 10, 10 }, Forward = new[] { -0.577, -0.577, -0.577 }, Up = new[] { -0.408, -0.408, 0.816 } },
                ZoomCorners = null
            },
            Comments = new List<IssueComment>
            {
                new IssueComment { Author = "joonas", At = new DateTimeOffset(2026, 10, 15, 8, 0, 0, Tallinn), Text = "Fixed" }
            }
        };

        [Fact]
        public void Issue_round_trips_every_field()
        {
            var original = FullIssue();
            var copy = MarkupJson.Deserialize<Issue>(MarkupJson.Serialize(original));

            Assert.Equal(MarkupJson.Serialize(original), MarkupJson.Serialize(copy));
            Assert.Equal(original.Created, copy.Created);
            Assert.Equal(Tallinn, copy.Created.Offset);
            Assert.Equal(IssueStatus.InProgress, copy.Status);
            Assert.Equal(new[] { 12.345, 6.789, 3.0 }, copy.Location.Point);
            Assert.Equal(12, copy.View.SectionBox.Transform.Length);
            Assert.Equal("Fixed", Assert.Single(copy.Comments).Text);
        }

        [Fact]
        public void Two_d_issue_with_zoom_corners_round_trips()
        {
            var issue = FullIssue();
            issue.View = new IssueView { UniqueId = "v", Name = "Plan", Type = "FloorPlan", ZoomCorners = new[] { new[] { 0.0, 0, 0 }, new[] { 20.5, 10.25, 0 } } };
            var copy = MarkupJson.Deserialize<Issue>(MarkupJson.Serialize(issue));

            Assert.Null(copy.View.SectionBox);
            Assert.Null(copy.View.Orientation);
            Assert.Equal(20.5, copy.View.ZoomCorners[1][0]);
        }

        [Fact]
        public void Format_matches_the_plan()
        {
            var json = JObject.Parse(MarkupJson.Serialize(FullIssue()));

            Assert.Equal("schemaVersion", ((JProperty)json.First).Name);
            Assert.Equal("id", ((JProperty)json.First.Next).Name);
            Assert.Equal("inProgress", (string)json["status"]);
            Assert.Equal("high", (string)json["priority"]);
            Assert.Equal(JTokenType.Null, json["resolvedBy"].Type);
            Assert.Equal("Communication Devices", (string)json["location"]["elementCategory"]);
            Assert.Contains("\"created\": \"2026-10-14T09:30:12+03:00\"", MarkupJson.Serialize(FullIssue()));
            Assert.Null(json["isDone"]);
        }

        [Fact]
        public void Unknown_properties_from_a_newer_schema_are_ignored()
        {
            var json = JObject.Parse(MarkupJson.Serialize(FullIssue()));
            json["schemaVersion"] = 2;
            json["somethingNew"] = new JObject { ["x"] = 1 };

            var issue = MarkupJson.Deserialize<Issue>(json.ToString());

            Assert.Equal(2, issue.SchemaVersion);
            Assert.Equal("Camera too close to door", issue.Title);
        }

        [Fact]
        public void Minimal_issue_gets_defaults()
        {
            var issue = MarkupJson.Deserialize<Issue>("{\"schemaVersion\":1,\"id\":\"20261014-093012-a3f9c1\",\"title\":\"x\"}");

            Assert.Equal(IssueStatus.Open, issue.Status);
            Assert.Equal(IssuePriority.Normal, issue.Priority);
            Assert.NotNull(issue.Comments);
            Assert.Null(issue.Location);
        }

        [Fact]
        public void Local_settings_round_trip()
        {
            var settings = new LocalSettings
            {
                MarkupFolder = @"D:\Dropbox\P123\_stickies",
                OverlayVisible = false,
                Filter = new IssueFilter
                {
                    Statuses = { IssueStatus.Open, IssueStatus.InProgress },
                    Priorities = { IssuePriority.High },
                    Author = "raul",
                    Assignee = "joonas",
                    Text = "camera"
                },
                Sort = new IssueSort { Column = "priority", Descending = false }
            };

            var copy = MarkupJson.Deserialize<LocalSettings>(MarkupJson.Serialize(settings));

            Assert.Equal(MarkupJson.Serialize(settings), MarkupJson.Serialize(copy));
            Assert.Equal(new[] { IssueStatus.Open, IssueStatus.InProgress }, copy.Filter.Statuses);
            Assert.False(copy.OverlayVisible);
        }

        [Fact]
        public void Project_file_round_trips()
        {
            var project = new MarkupProject { ModelGuid = "abc-123", ProjectName = "P123 School" };
            var copy = MarkupJson.Deserialize<MarkupProject>(MarkupJson.Serialize(project));

            Assert.Equal(1, copy.SchemaVersion);
            Assert.Equal("abc-123", copy.ModelGuid);
            Assert.Equal("P123 School", copy.ProjectName);
        }

        [Fact]
        public void Issue_ids_are_well_formed_and_unique()
        {
            var now = new DateTimeOffset(2026, 10, 14, 9, 30, 12, Tallinn);
            var ids = new HashSet<string>();
            for (var i = 0; i < 200; i++)
            {
                var id = IssueId.New(now);
                Assert.StartsWith("20261014-093012-", id);
                Assert.True(IssueId.IsValid(id), id);
                ids.Add(id);
            }
            Assert.True(ids.Count > 195);

            Assert.False(IssueId.IsValid("20261014-093012-a3f9c1 (Raul's conflicted copy 2026-10-14)"));
            Assert.False(IssueId.IsValid("20261014-093012-A3F9C1"));
            Assert.False(IssueId.IsValid(null));
        }
    }
}
