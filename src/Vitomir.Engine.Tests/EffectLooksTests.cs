// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.RegularExpressions;
using Vitomir.Engine.Features;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The guard under the repair `loadedeffects`**, as
/// <see cref="LocomotionTagTests"/> guards the gait Fixes and <see cref="SoakedHandlerTests"/> the white flash.
/// The repair is declared presentation: on a load it makes again the things an effect's script had made that no
/// save keeps. That is no rule's business only if **nothing can touch such a thing** — which is a fact about the
/// install's scripts and templates, and this reads both: every template an effect's script spawns
/// (`Effect*.java` under the install's `Codex` sources, every `SpawnThing`) is one no save keeps
/// (<c>SAVE NEVER</c>), and each either ends by itself (a template with a <c>TIMER</c>: a burst, which a load
/// does not make again) or is a prop with no collision shape and no script of its own
/// (<see cref="LoadedEffects.Makes"/>) — a pad, a ring, a trail, a beam, a mist, a dome. The two lists are
/// pinned whole, so the doc's account of what comes back is the install's.
///
/// And the record itself, without the scripts (<see cref="EffectMade"/>): a stretch marks what was made in it
/// that no save keeps, an inner stretch's things are its own, and nothing is left open.
/// </summary>
public partial class EffectLooksTests
{
    [GeneratedRegex(@"/\*.*?\*/|//[^\n]*", RegexOptions.Singleline)]
    private static partial Regex Comments();

    /// <summary>A spawn, with its first argument as written.</summary>
    [GeneratedRegex(@"\b(?:SpawnThing|SpawnThingNear|CreateThing)\s*\(\s*(?<what>[^,)]*)")]
    private static partial Regex Spawns();

    /// <summary>The lasting looks: what a load makes again. Every one a prop nothing can touch.</summary>
    static readonly string[] Lasting =
    [
        "ankhs2Pad", "ankhs2PadYellow", "ankhsPad", "ankhsPadRed", "ankhsPadYellow", "bloodStream", "blueLightningOne",
        "celerityTrail", "disciplineTorch", "flamePad", "fogStream", "greenMist", "holyHalfdome", "ringOfEyesBlue",
        "ringOfEyesRed", "ringOfEyesYellow", "ringOfSerpentsGreen", "ringOfSkullsBlue", "ringOfSkullsYellow",
        "ringOfWolvesRed", "ringOfWolvesYellow", "rosettePad", "rosettePadYellow", "sawToothBlue", "swirlingSkullsYellow",
    ];

    /// <summary>The bursts: each ends by itself (its template's `TIMER`), and is not made again.</summary>
    static readonly string[] Passing =
    [
        "blackMagic", "blueMagic", "flamewave", "greenMagic", "invshockwave", "invshockwave_y", "lightningCloud",
        "redMagic", "ringTower", "ringTowerFlame", "yellowMagic",
    ];

    [Fact]
    public void Everything_an_effect_s_script_spawns_is_never_saved_and_is_a_burst_or_a_prop_nothing_can_touch()
    {
        using var files = Install.Open();
        Assert.SkipUnless(Directory.Exists(files.CodexFolder), "the install has no script sources");
        var sources = Directory.EnumerateFiles(files.CodexFolder, "Effect*.java", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(sources.Count > 60, $"only {sources.Count} effect scripts were read");

        var spawned = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var unread = new List<string>();
        foreach (var path in sources)
        {
            string text = Comments().Replace(File.ReadAllText(path), " ");
            foreach (Match m in Spawns().Matches(text))
            {
                string what = m.Groups["what"].Value.Trim();
                // Every spawn in an effect's script names its template in so many words; one that did not could not be read here.
                if (what.Length < 3 || what[0] != '"' || what[^1] != '"') { unread.Add($"{Path.GetFileName(path)}: {m.Value}"); continue; }
                string name = what[1..^1];
                if (!spawned.TryGetValue(name, out var by)) spawned[name] = by = [];
                by.Add(Path.GetFileNameWithoutExtension(path));
            }
        }
        Assert.True(unread.Count == 0, "an effect's script spawns something this cannot read:\n  " + string.Join("\n  ", unread));

        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var (lasting, passing, touchable) = (new List<string>(), new List<string>(), new List<string>());
        foreach (var (name, by) in spawned)
        {
            var thing = place.Make(name);
            Assert.True(thing is not null, $"{by[0]} spawns {name}, which there is no template for");
            if ((thing!.Flags & WorldThing.Flag.SaveNever) == 0) touchable.Add($"{name} ({by[0]}) is kept by a save: flags 0x{thing.Flags:x}");
            else if (thing.ExpireAt != 0) passing.Add(name);
            else if (LoadedEffects.Makes(thing)) lasting.Add(name);
            else touchable.Add($"{name} ({by[0]}) is a {thing.Type} with collision {thing.Collide}, script \"{thing.Script}\"");
        }
        Assert.True(touchable.Count == 0,
            "loadedeffects cannot be declared presentation: an effect's script makes something a load would make again that the rules can touch:\n  "
            + string.Join("\n  ", touchable));
        Assert.Equal(Lasting.Order(StringComparer.Ordinal), lasting);
        Assert.Equal(Passing.Order(StringComparer.Ordinal), passing);

        // What the declaration then says.
        var repair = GameFeatures.Registry.Find("repairs", Repairs.LoadedEffects)!;
        Assert.Null(GameFeatures.Registry.Find("fixes", "loadedeffects"));      // no switch: always done
        Assert.False(GameFeatures.Data.EffectLooks.RulesRead);
        Assert.False(GameFeatures.Points.EffectsRemake.Rules);
        Assert.Equal(FeatureAxis.Presentation, repair.Axis);
        Assert.Equal(FeatureWhose.Machine, repair.Whose);
        Assert.True(repair.Default && !repair.Switchable);
        TestContext.Current.SendDiagnosticMessage($"effect looks: {sources.Count} scripts spawn {spawned.Count} templates: {lasting.Count} lasting, {passing.Count} passing");
    }

    static Effect An(Location place, string name, int guid) =>
        new() { Definition = new EffectDefinitions(place.Files!)[name]!, Guid = guid };

    [Fact]
    public void A_stretch_marks_what_was_made_in_it_that_no_save_keeps_and_an_inner_stretch_s_things_are_its_own()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var christof = place.Player!.Thing;
        var (outer, inner) = (An(place, "ef_disc_potence", 41), An(place, "ef_disc_hsenses", 42));

        var before = place.Spawn("rosettePad", christof)!;                    // made by nobody's stretch
        WorldThing pad, kept, ring, mine;
        Assert.Equal(0, EffectMade.Current);
        using (EffectMade.By(christof, outer))
        {
            Assert.Equal(41, EffectMade.Current);
            pad = place.Spawn("sawToothBlue", christof)!;
            kept = place.Spawn("torchU", christof)!;                          // an item: a save keeps it, so it is not the effect's to make again
            using (EffectMade.By(christof, inner))
            {
                Assert.Equal(42, EffectMade.Current);
                mine = place.Spawn("rosettePad", christof)!;
            }
            Assert.Equal(41, EffectMade.Current);
            Assert.Equal(42, mine.MadeByEffect);                              // marked as its own stretch ended
            Assert.Equal(0, pad.MadeByEffect);                                // the outer's are marked when the outer ends
            ring = place.Spawn("ringTower", christof)!;
        }
        Assert.Equal(0, EffectMade.Current);
        Assert.Equal((41, 41, 42, 0, 0), (pad.MadeByEffect, ring.MadeByEffect, mine.MadeByEffect, kept.MadeByEffect, before.MadeByEffect));
        using (default(EffectMade.Stretch)) { }                              // no stretch (a timer that is no effect's) changes nothing
        Assert.Equal(0, EffectMade.Current);

        // What a save then records of each: the pad, not the burst; and nothing of an effect with no script.
        var record = new EffectSave();
        LoadedEffects.Capture(place, outer, record);
        Assert.Equal(pad.Guid, Assert.Single(record.Made!).Guid);
        Assert.Null(record.Made![0].Rides);                                   // it rides nothing here: where it stood
        Assert.Equal(pad.Position, record.Made[0].Position);
        Assert.Null(record.Shells);
        Assert.True(LoadedEffects.Looks(pad) && !LoadedEffects.Looks(ring) && !LoadedEffects.Looks(kept));
        place.Remove(pad);
        record = new EffectSave();
        LoadedEffects.Capture(place, outer, record);
        Assert.Null(record.Made);                                             // gone: nothing to make again
    }

    /// <summary>
    /// The shell's own state, out and in (<see cref="ModelFx.ShellNow"/>, <see cref="ModelFx.PutShell"/>): a
    /// flash caught half-way goes on from there on another model, tick for tick; and whose a shell is follows
    /// who set it last.
    /// </summary>
    [Fact]
    public void A_shell_put_back_goes_on_from_where_it_stood()
    {
        var first = new ModelFx();
        Assert.Null(first.ShellNow);
        first.SetShell("redCloudShell_b", 0x5000, 0f, 0.9f, 1f);              // in over a second, out over the next
        first.ShellBy(7);
        Assert.Equal(7, first.ShellEffect);
        Assert.True(first.ShellEndsByItself);                                 // a flash, on the way in too
        for (int i = 0; i < 40; i++) first.Tick(1f / 30f);                    // 1.33 s: on the way out
        var state = first.ShellNow!.Value;
        Assert.Equal(("redCloudShell_b", true, 0.9f, 0f), (state.Material, state.Ramping, state.From, state.To));
        Assert.Equal(0u, state.Flags & ModelFx.ShellInOut);                   // the turn is behind it
        Assert.True(first.ShellEndsByItself);

        var second = new ModelFx();
        second.PutShell(state, 7);
        Assert.Equal((first.Shell, first.ShellAlpha, first.ShellFlags, first.ShellRamping, 7), (second.Shell, second.ShellAlpha, second.ShellFlags, second.ShellRamping, second.ShellEffect));
        for (int i = 0; i < 40; i++)
        {
            first.Tick(1f / 30f);
            second.Tick(1f / 30f);
            Assert.Equal((first.Shell, first.ShellAlpha, first.ShellRamping), (second.Shell, second.ShellAlpha, second.ShellRamping));
        }
        Assert.Null(second.Shell);                                            // faded out, and gone
        Assert.Equal(0, second.ShellEffect);

        // A lasting shell is the effect's until something else sets one over it, or it is ended.
        second.SetShell("iceShell", 0x1000, 0f, 0.3f, 1f);
        Assert.Equal(0, second.ShellEffect);                                  // not yet said to be anyone's
        Assert.False(second.ShellEndsByItself);                               // ramping up to a level it will hold
        second.ShellBy(9);
        second.SetShell("white", 0x5000, 0f, 1f, 1f);                         // the soak's flash: nobody's
        Assert.Equal(0, second.ShellEffect);
        second.ShellBy(9);
        second.EndShell();
        Assert.Equal(0, second.ShellEffect);
        second.ShellBy(9);                                                    // no shell on: nothing to mark
        Assert.Equal(0, second.ShellEffect);
    }
}
