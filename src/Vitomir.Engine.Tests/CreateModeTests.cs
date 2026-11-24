// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The finalize's `create` (mode 127 at 30 frames a second): every thing, as it is placed or
/// made, is sent mode 127 on its **action** channel, which only the ten sets that have the mode take;
/// and a thing whose render flags carry `RECREATE` is sent it again when a game is loaded (the
/// load's own test).
/// </summary>
public class CreateModeTests
{
    [Fact]
    public void A_thing_whose_set_has_create_plays_it_on_the_action_channel_when_placed_and_nothing_else_does()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        place.Awaken(new AiClasses(files));
        var creators = place.Things.Where(t => t.Animation is { } a && a.Set.Modes.Any(m => m.Index == Location.CreateMode)).ToList();
        Assert.SkipWhen(creators.Count == 0, "nothing in Old Town has a `create` mode");
        foreach (var t in creators)
            Assert.Equal("create", t.Animation!.Action?.Mode.Name);
        // A character's set has no `create`, and its action channel is left empty.
        Assert.Null(place.Player!.Thing.Animation!.Action);
    }

    [Fact]
    public void A_recreate_thing_starts_its_create_over_on_a_load_and_another_does_not()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        place.Awaken(new AiClasses(files));
        var creator = place.Things.FirstOrDefault(t => t.Animation is { } a && a.Set.Modes.Any(m => m.Index == Location.CreateMode));
        Assert.SkipWhen(creator is null, "nothing in Old Town has a `create` mode");
        var anim = creator!.Animation!;
        anim.Advance(0.5f);
        float along = anim.Action!.Player.Frame;
        Assert.True(along > 0f);

        uint flags = creator.RenderFlags;               // the shipped flags and banners carry it
        creator.RenderFlags = flags & ~Location.RecreateFlag;
        place.Recreate(creator);                        // no RECREATE: left where it was
        Assert.Equal(along, anim.Action!.Player.Frame);
        creator.RenderFlags = flags | Location.RecreateFlag;
        place.Recreate(creator);                        // RECREATE: from the first frame again
        Assert.Equal(0f, anim.Action!.Player.Frame);
    }
}
