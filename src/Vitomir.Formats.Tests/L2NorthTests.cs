// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json.Nodes;
using Vitomir.Formats.L2;

namespace Vitomir.Formats.Tests;

/// <summary>
/// A cell level's compass (ours; <see cref="L2North"/>): the
/// manifest's <c>north</c> written and read back with what this build does not know kept; a manifest without
/// one having north along +X, as every cell level had before; the generator's layout giving it to the cook;
/// and why the new Old Town's is 68°: with it 1.1's four gates face their names, and with north along +X
/// they do not. The same sums as the levels generator's compass test, so the two sides
/// cannot drift apart.
/// </summary>
public class L2NorthTests
{
    [Fact]
    public void A_manifests_north_comes_back_as_written()
    {
        var m = new L2Manifest { Version = L2Manifest.Version2, Name = "oldtown", North = new L2North { Yaw = 68f } };
        var text = m.Write();
        Assert.Contains("\"north\"", text);
        var back = L2Manifest.Read(text);
        Assert.Equal(68f, back.North!.Yaw);
        Assert.Equal(68f, L2North.Of(back));
        Assert.Equal("north at yaw 68°", back.North.ToString());

        // What the generator says beside the yaw (its north and east vectors, its note) is kept through a read and a write.
        var later = L2Manifest.Read(text.Replace("\"yaw\": 68", "\"yaw\": 68, \"east\": [0.9272, -0.3746]"));
        Assert.Contains("\"east\"", later.Write());
        Assert.Equal(68f, L2Manifest.Read(later.Write()).North!.Yaw);
    }

    [Fact]
    public void A_manifest_without_a_north_has_north_along_x_as_before()
    {
        var old = new L2Manifest { Version = L2Manifest.Version2, Name = "round13" };
        var text = old.Write();
        Assert.DoesNotContain("\"north\"", text);                 // nothing written when there is none
        var back = L2Manifest.Read(text);
        Assert.Null(back.North);
        Assert.Equal(0f, L2North.DefaultYaw);
        Assert.Equal(0f, L2North.Of(back));
    }

    [Fact]
    public void The_generators_layout_gives_the_compass_and_a_layout_without_a_sensible_one_gives_none()
    {
        var layout = JsonNode.Parse("""{ "level": "P1_OTDY", "frame": { "units": "metre", "north": { "yaw": 68.0, "north": [0.3746, 0.9272], "note": "ours" } } }""");
        var north = L2North.FromLayout(layout)!;
        Assert.Equal(68f, north.Yaw);
        var manifest = new L2Manifest { Version = L2Manifest.Version2, Name = "oldtown", North = north }.Write();
        Assert.Contains("\"note\": \"ours\"", manifest);            // the layout's words go on into the manifest
        Assert.Null(L2North.FromLayout(null));
        Assert.Null(L2North.FromLayout(JsonNode.Parse("""{ "level": "P1_OTDY" }""")));
        Assert.Null(L2North.FromLayout(JsonNode.Parse("""{ "frame": { "units": "metre" } }""")));   // the layouts
        Assert.Null(L2North.FromLayout(JsonNode.Parse("""{ "frame": { "north": "+y" } }""")));
        Assert.Null(L2North.FromLayout(JsonNode.Parse("""{ "frame": { "north": { "note": "no yaw" } } }""")));
        Assert.Null(L2North.FromLayout(JsonNode.Parse("""{ "frame": { "north": { "yaw": "sixty-eight" } } }""")));
        Assert.Null(L2North.FromLayout(JsonNode.Parse("""{ "frame": "metres" }""")));
    }

    [Fact]
    public void A_bearing_is_counted_clockwise_from_north_and_plus_x_bears_the_yaw()
    {
        Assert.Equal(0f, L2North.Bearing(1, 0, 0), 3);          // north along +X: +X is north,
        Assert.Equal(270f, L2North.Bearing(0, 1, 0), 3);        // +Y west,
        Assert.Equal(90f, L2North.Bearing(0, -1, 0), 3);        // −Y east
        Assert.Equal(68f, L2North.Bearing(1, 0, 68), 3);        // turned: +X bears the yaw itself
        Assert.Equal(0f, L2North.Bearing(MathF.Cos(68 * MathF.PI / 180), MathF.Sin(68 * MathF.PI / 180), 68), 2);
    }

    /// <summary>
    /// 1.1's four Old Town gates as the survey measured them with north along +X (from each exit's start points
    /// to its door; good to some 15°), and the bearing each one's name asks for.
    /// </summary>
    static readonly (string Name, float MeasuredAtYaw0, float Named)[] Gates =
    [
        ("the East Gate (exit 3)", 16.3f, 90f), ("the North Quarter's gate (exit 11)", 290.6f, 0f),
        ("Vysehrad's gate, south (exit 7)", 108.8f, 180f), ("the bridge's gate (exit 8)", 212.3f, 270f),
    ];

    static float Off(float a, float b) => MathF.Abs((a - b + 540f) % 360f - 180f);

    [Fact]
    public void Turned_68_degrees_the_four_gates_face_their_names_and_with_north_along_x_none_does()
    {
        foreach (var (name, measured, named) in Gates)
        {
            // The gate's facing as a direction in the level (its bearing with north along +X is the yaw −bearing).
            float yaw = -measured * MathF.PI / 180f, x = MathF.Cos(yaw), y = MathF.Sin(yaw);
            Assert.True(Off(L2North.Bearing(x, y, 0f), named) > 55f, $"{name} with north along +X: {L2North.Bearing(x, y, 0f)}");
            float turned = L2North.Bearing(x, y, 68f);
            Assert.True(Off(turned, named) < 15f, $"{name} turned: {turned}, its name asks {named}");
            // The other way round every gate would be further off than before: the sign is pinned.
            Assert.True(Off(L2North.Bearing(x, y, -68f), named) > 120f, $"{name} turned the wrong way: {L2North.Bearing(x, y, -68f)}");
        }
    }
}
