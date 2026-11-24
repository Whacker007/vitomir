// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A journey clears the marks** (a comparison with 1.1): every journey ends the scene it leaves
/// — the exit, the jump, the teleport — and the scene's end empties the decal
/// pool along with the sounds, the conversation and the rest. 1.1 keeps one pool for the
/// whole game, so a place left and come back to has lost its bullet marks and scorches, even while
/// its world was kept loaded. Vitomir keeps a pool a place and had left them there.
/// </summary>
public class JourneyDecalsTests
{
    [Fact]
    public void A_place_left_and_come_back_to_has_lost_its_marks()
    {
        using var files = Install.Open();
        var chronicle = ChronicleSet.Load(files)["prague"]!;
        var session = new Session { Chronicle = chronicle };
        session.Messages.Text = new Strings(files);
        Location Build(string name, string? scene, int entrance)
        {
            var place = Location.Load(files, name, things: false, sky: false, scene: scene, player: "Christof", entrance: entrance);
            place.Session = session;
            return place;
        }
        int start = chronicle.Find("Smithy");
        chronicle.Current = start;
        var at = chronicle.At(start)!;
        var smithy = Build(at.LevelName, at.SceneName.Length > 0 ? at.SceneName : null, -1);
        session.Here = smithy;
        var campaign = new Campaign { Chronicle = chronicle, Build = Build, Here = smithy };

        var feet = smithy.Player!.Thing.Position;
        int sector = smithy.Solid!.SectorAt(feet, -1);
        Assert.True(smithy.Decals.Stamp(sector, Vector3.UnitZ, feet + new Vector3(0, 0, 40), 0f, 120f, 15000, "BulletMark1", 0));
        Assert.True(smithy.Decals.Count > 0);

        Assert.True(chronicle.Jump("Unorna", 0));
        Assert.NotNull(campaign.Travel());
        Assert.Equal(0, smithy.Decals.Count);                     // gone as the party left, not when it came back

        Assert.True(chronicle.Jump("Smithy", 0));
        var back = campaign.Travel();
        Assert.NotNull(back);
        Assert.Same(smithy, back.Value.To);                       // the same place, still loaded
        Assert.Equal(0, smithy.Decals.Count);
    }
}
