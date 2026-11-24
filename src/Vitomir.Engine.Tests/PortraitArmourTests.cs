// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The portrait head in armour (the body model's second half): a player character's body stand-in is
/// matched by a head stand-in named from the **body's** model with `"_%sH"` —
/// `christof` in `plate` is `christof_plateH` — only for type 7, emptied with the armour, and the
/// status bar draws it in the head's place with the head's own name kept for when it will not load.
/// The shipped `H` heads ride the head's own fifteen-bone skeleton.
/// </summary>
public class PortraitArmourTests
{
    static WorldThing Player(ThingType type = ThingType.Player) => new()
    {
        Index = 0, Guid = 1, Scene = new NsdThing(), Template = "christof", Model = "christof.nod", Type = type,
        Actor = new Actor { Name = "Christof", HeadModel = "christofH.nod", HeadMotionSet = "ChristofDaHead.nms" },
    };

    [Fact]
    public void Plate_on_a_player_names_the_plate_head_and_taking_it_off_empties_it()
    {
        var christof = Player();
        BodyModels.Set(christof, "plate");
        Assert.Equal("christof_plate", christof.Actor!.BodyModel);
        Assert.Equal("christof_plateH", christof.Actor.PortraitModel);

        var head = Assert.Single(PartyHeads.Of([christof]));
        Assert.Equal("christof_plateH", head.Model);
        Assert.Equal("christofH.nod", head.Own);

        BodyModels.Set(christof, null);
        Assert.Equal("", christof.Actor.PortraitModel);
        Assert.Equal("christofH.nod", Assert.Single(PartyHeads.Of([christof])).Model);
    }

    [Fact]
    public void Only_a_player_type_has_its_portrait_done()
    {
        var guard = Player(ThingType.Actor);
        BodyModels.Set(guard, "plate");
        Assert.Equal("christof_plate", guard.Actor!.BodyModel);
        Assert.Equal("", guard.Actor.PortraitModel);
    }

    [Fact]
    public void The_shipped_plate_heads_ride_the_head_skeleton()
    {
        using var files = Install.Open();
        Assert.True(files.TryRead("3d/Models/christofH.nod", out var own));
        Assert.True(files.TryRead("3d/Models/" + BodyModels.Portrait("christof", "plate") + ".nod", out var plate));
        Assert.Equal(NodModel.Read(own, "christofH.nod").Bones.Length, NodModel.Read(plate, "christof_plateH.nod").Bones.Length);
        // A beheading's `nohead` has no head of its own: the portrait keeps its face.
        Assert.False(files.TryRead("3d/Models/" + BodyModels.Portrait("christof", BodyModels.NoHead) + ".nod", out _));
    }
}
