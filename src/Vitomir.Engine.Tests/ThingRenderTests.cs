// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>What a scene's things turn into, short of the GPU.</summary>
public class ThingRenderTests
{
    static Location Load(string level)
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        return Location.Load(files, level, scene: set.ForLevel(level)?.Entry?.SceneName);
    }

    [Fact]
    public void A_street_with_people_on_it_draws_the_people()
    {
        // East London in chapter 28 has Otto, his van and half a dozen Londoners. They are things
        // like any other; what kept them off the screen was that every one of them is skinned.
        var east = Load("l1_east");
        Assert.True(east.Mesh.Skinned > 0, "no skinned things drawn in East London");
        Assert.Contains(east.Things, t => t.Type == ThingType.Actor);
        Assert.True(east.Mesh.ThingsDrawn >= east.Things.Count(t => t.Type == ThingType.Actor),
                    "fewer things drawn than there are actors");
    }

    [Fact]
    public void A_skinned_model_stands_where_its_thing_stands()
    {
        // A skinned model's vertices are authored in its rest pose and the rest-pose skin matrix is
        // the identity, so a thing with no animation playing is already in the
        // right place. If that were wrong the mesh would pile up at the model's origin instead, and
        // the level's bounds would not contain it.
        var east = Load("l1_east");
        var actor = east.Things.First(t => t.Type == ThingType.Actor && t.Model.Length > 0);
        var min = east.Mesh.BoundsMin;
        var max = east.Mesh.BoundsMax;
        Assert.InRange(actor.Position.X, min.X, max.X);
        Assert.InRange(actor.Position.Y, min.Y, max.Y);
        Assert.InRange(actor.Position.Z, min.Z, max.Z);
    }

    [Fact]
    public void Trigger_volumes_are_still_not_drawn()
    {
        var east = Load("l1_east");
        Assert.Contains(east.Things, t => t.Type == ThingType.Region);
        Assert.DoesNotContain(east.Mesh.Batches, b => b.Source.Contains("trigger_region", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Nothing_is_skipped_for_being_skinned_any_more()
    {
        var east = Load("l1_east");
        Assert.DoesNotContain(east.Mesh.Skipped, s => s.Contains("skinned", StringComparison.OrdinalIgnoreCase));
    }
}
