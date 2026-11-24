// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A body lies where it fell after a load** (`Save/Poses.cs`). 1.1's model block keeps the
/// animation a thing is holding at its end (flags `0x1010`) and plays it again from its last
/// frame on the way back in (`0x800`); earlier, Vitomir's save kept no animation at all,
/// and every corpse a load brought back stood up. Pinned on a Cappadocian killed in the monastery,
/// through a save file and a load the way the game loads one — and on 1.1's own record of a body,
/// when the install has a monastery save of 1.1's.
/// </summary>
public class PoseTests
{
    [Fact]
    public void A_body_killed_before_a_save_lies_in_its_fall_after_the_load()
    {
        using var files = Install.Open();
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        place.Awaken(new AiClasses(files), 1, effects, powers);
        var session = new Session { Here = place };
        place.Session = session;
        var sim = new Simulation(place);
        var victim = place.Things.First(t => t.Template == "cappadocian" && t.Actor is not null);
        string id = ThingIds.Of(place, victim)!;
        Assert.True(Damage.Kill(victim, place.Player!.Thing, default, default, place.Milliseconds, 50f, place.Motions));
        for (int i = 0; i < 90; i++) sim.Step(1f / 30f);
        var fell = victim.Animation!.Action!;
        Assert.True(fell.Held || (fell.Flags & NmsFlags.EndPause) != 0, "the fall is not held");

        var saved = SaveCapture.Take(place, session);
        Assert.Equal(fell.Mode.File, saved.World.Values.Single().Things[id].Pose?.Animation);
        string path = Path.Combine(Path.GetTempPath(), $"vitomir-pose-{Guid.NewGuid():N}.vsave");
        SaveGame game;
        try { SaveArchive.Write(saved, path); game = SaveArchive.Read(path); }
        finally { File.Delete(path); }

        var loaded = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var restore = SaveApply.Place(loaded, game.World.Values.Single(), effects, powers);
        SaveApply.Party(loaded, game.Party, restore, effects, powers);
        loaded.Awaken(new AiClasses(files), 1, effects, powers, fromSave: true);
        loaded.Milliseconds = (int)game.Header.ClockMs;
        var again = new Simulation(loaded);
        again.Resume(game.Header.ClockMs / 1000f);
        for (int i = 0; i < 30; i++) again.Step(1f / 30f);

        var body = loaded.Things.First(t => ThingIds.Of(loaded, t) == id);
        Assert.True(body.Actor!.Dead);
        Assert.Equal(fell.Mode.File, body.Animation!.Action?.Mode.File);
        Assert.True(body.Animation.Action!.Held, "the fall is not held at its end");
        Assert.Equal(body.Animation.Action.Player.Animation.Duration, body.Animation.Action.Player.Frame);
    }

    /// <summary>1.1's own record of a body — `halfCorpse_death.nad` in a monastery save — comes across as the pose.</summary>
    [Fact]
    public void A_body_in_a_1_1_place_file_comes_across_holding_its_fall()
    {
        using var files = Install.Open();
        string slot = Path.Combine(files.Root, "SaveGames", "SP4");
        Assert.SkipUnless(File.Exists(Path.Combine(slot, "location_32.nsw")), "no 1.1 save with the monastery's file");
        var file = NswWorld.ReadFile(Path.Combine(slot, "location_32.nsw"));
        var held = file.Things.Where(r => r.Model.Animation is { Length: > 0 }).ToArray();
        Assert.NotEmpty(held);
        var nsg = NsgSave.ReadFile(Path.Combine(slot, "!main.nsg"));
        var game = SaveImport.FromOriginal(files, ChronicleSet.Load(files), nsg, "SP4", [], folder: slot)!;
        var monastery = game.World[game.Header.Location];
        foreach (var r in held)
        {
            string id = r.Scene == NswWorld.OwnScene ? $"{monastery.Level}#{(int)r.Field28}" : $"{monastery.Scene}#{(int)r.Field28}";
            Assert.Equal(r.Model.Animation, monastery.Things[id].Pose?.Animation);
        }
    }
}
