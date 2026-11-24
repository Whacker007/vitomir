// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **An effect's glow, pad and trail through a save and a load** (the repair `loadedeffects`, ours;
/// `Save/LoadedEffects.cs`). In play, after reloading a game save, Christof had
/// heightened senses still active, but the visual effect at his feet was gone. It is 1.1's own fault,
/// as a look at 1.1 showed (the visual effects are not preserved on save
/// and load; a plain bug, so it is fixed with no option to turn it off):
/// what an effect's script makes as it starts is flagged SAVE NEVER, a model's shell is not in the file,
/// and a load never tells a restored effect's script `effectstarted`.
///
/// Pinned here with the game's own scripts: every save records what each effect's script made; a save with no
/// such record — every older one — loads as it did, and the record's whole consequence for a later save is the
/// record; with it the pad is there after a load under the number it had, riding the character, and the shell
/// is on as it stood; the effect ending after a load takes both away, by the script's own `EndEffect`; nothing
/// comes back for an effect the load itself takes off (Mist Form); and a burst that ends by itself is
/// not kept.
/// </summary>
public partial class SaveScriptTests
{
    const string Potence = "ef_disc_potence", Senses = "ef_disc_hsenses";

    /// <summary>An effect put on Christof as its discipline puts it on, its script told `effectstarted`.</summary>
    static Effect PutOn(Game g, string name, int ms = 60_000)
    {
        var christof = g.Place.Player!.Thing;
        var effect = EffectRuntime.Apply(christof, name, 0, ms, creator: christof)!;
        Assert.NotNull(effect);
        g.Step(2);
        Assert.NotNull(effect.Script);
        return effect;
    }

    static List<WorldThing> MadeBy(Game g, Effect effect) => [.. g.Place.Things.Where(t => t.MadeByEffect == effect.Guid)];

    static SaveGame Stripped(SaveGame save)
    {
        var again = SaveArchive.Read(Write(save));
        foreach (var e in again.Party.Members.SelectMany(m => m.Effects ?? []).Concat(again.World.Values.SelectMany(w => w.Things.Values).SelectMany(t => t.Effects ?? [])))
        { e.Made = null; e.Shells = null; e.Frozen = null; }
        return again;
    }

    [Fact]
    public void Every_save_records_what_an_effect_s_script_made()
    {
        using var g = Begin();
        var christof = g.Place.Player!.Thing;
        var potence = PutOn(g, Potence);

        // What the script made, marked as it ran: the pad that stays, and the opening ring that ends by itself.
        var made = MadeBy(g, potence);
        Assert.Equal(["ringTower", "sawToothBlue"], made.Select(t => t.Template).Order());
        var pad = made.Single(t => t.Template == "sawToothBlue");
        Assert.Same(christof, pad.AttachedTo);
        Assert.NotEqual(0, made.Single(t => t.Template == "ringTower").ExpireAt);      // its template's TIMER
        Assert.Equal(0, pad.ExpireAt);
        Assert.True(LoadedEffects.Looks(pad));
        Assert.False(LoadedEffects.Looks(made.Single(t => t.Template == "ringTower")));
        // The shell is the effect's, and a shell anything else sets is not.
        Assert.Equal("redCloudShell_b", christof.Fx!.Shell);
        Assert.Equal(potence.Guid, christof.Fx.ShellEffect);
        Assert.Equal(0, EffectMade.Current);                                            // no stretch is left open

        g.Step(45);                                                                     // the shell's ramp done: 0.3 and holding
        var save = SaveCapture.Take(g.Place, g.Session);
        var record = save.Party.Members[0].Effects!.Single(e => e.Guid == potence.Guid);
        var thing = Assert.Single(record.Made!);
        Assert.Equal(("sawToothBlue", pad.Guid, (int?)christof.Guid, -1, Attachments.AutoRemove), (thing.Template, thing.Guid, thing.Rides, thing.Bone, thing.AttachFlags));
        Assert.Equal(12, thing.Relative!.Length);
        Assert.Equal(pad.Position, thing.Position);
        var shell = Assert.Single(record.Shells!);
        Assert.Equal((christof.Guid, "redCloudShell_b", 0.3f, false), (shell.On, shell.Material, shell.Alpha, shell.Ramping));
        // The thing itself is in no part of the save, as before: the record is the only word of it.
        Assert.DoesNotContain(save.World.Values.SelectMany(w => w.Things.Values), t => t.Guid == pad.Guid);
        Assert.Null(save.Party.Members[0].Attachments?.FirstOrDefault(a => a.Child.Contains("sawTooth", StringComparison.OrdinalIgnoreCase)));

        // It goes through the file.
        var read = SaveArchive.Read(Write(save)).Party.Members[0].Effects!.Single(e => e.Guid == potence.Guid);
        Assert.Equal((thing.Template, thing.Guid, thing.Rides, thing.Bone, thing.AttachFlags), (read.Made![0].Template, read.Made[0].Guid, read.Made[0].Rides, read.Made[0].Bone, read.Made[0].AttachFlags));
        Assert.Equal(thing.Relative, read.Made[0].Relative);
        Assert.Equal((shell.On, shell.Material, shell.Flags, shell.Alpha, shell.Ramping, shell.From, shell.To, shell.Rate, shell.Progress),
                     (read.Shells![0].On, read.Shells[0].Material, read.Shells[0].Flags, read.Shells[0].Alpha, read.Shells[0].Ramping, read.Shells[0].From, read.Shells[0].To, read.Shells[0].Rate, read.Shells[0].Progress));

        // A shell something else sets over it is no longer the effect's (the steered character's flash, 1.1's own).
        christof.Model3D.SetShell(Location.SteerShell, 0x5000, 0f, 1f, Location.SteerShellRamp);
        Assert.Equal(0, christof.Fx.ShellEffect);
        Assert.Null(SaveCapture.Take(g.Place, g.Session).Party.Members[0].Effects!.Single(e => e.Guid == potence.Guid).Shells);
    }

    /// <summary>
    /// **An older save loads as it did.** A save from before the record was written has none, and a load of it
    /// makes nothing: the effect is on and its looks are gone, as in 1.1 (the effect ends, and the next cast
    /// has them). And the record changes nothing else: the game loaded from the save **with** the record,
    /// saved again and the record taken out, is to the letter the game loaded from the save without it, saved
    /// again — the pad made again is in no part of a save but the record.
    /// </summary>
    [Fact]
    public void A_save_with_no_record_loads_as_it_did_and_the_record_s_whole_consequence_is_the_record()
    {
        SaveGame first;
        int pad, effectGuid;
        using (var g = Begin())
        {
            var potence = PutOn(g, Potence);
            g.Step(45);
            pad = MadeBy(g, potence).Single(t => t.Template == "sawToothBlue").Guid;
            effectGuid = potence.Guid;
            first = SaveCapture.Take(g.Place, g.Session);
        }
        var older = Stripped(first);                                                    // as a build before the record wrote it
        Assert.All(older.Party.Members[0].Effects!, e => Assert.True(e.Made is null && e.Shells is null));
        Assert.DoesNotContain("\"made\"", SaveArchive.Parts(older).Single(p => p.Name.StartsWith("party")).Text);
        Assert.Contains("\"made\"", SaveArchive.Parts(first).Single(p => p.Name.StartsWith("party")).Text);

        static string Again(Game g) =>
            string.Join("\n", Texts(Stripped(SaveCapture.Take(g.Place, g.Session))).OrderBy(p => p.Key).Select(p => p.Value));

        string without;
        int things;
        using (var old = Begin(SaveArchive.Read(Write(older))))
        {
            var him = old.Place.Player!.Thing;
            Assert.NotNull(him.Effects.Find(effectGuid));                               // the effect is on,
            Assert.Null(old.Place.Thing(pad));                                          // and nothing its script made is there
            Assert.Null(him.Fx?.Shell);
            Assert.DoesNotContain(old.Place.Things, t => t.MadeByEffect != 0);
            things = old.Place.Things.Count;
            old.Step(30);
            without = Again(old);
            // Nothing was made, so there is nothing for the engine to take when the effect ends.
            EffectRuntime.Remove(him, him.Effects.Find(effectGuid)!, notify: true);
            Assert.Equal(0, LoadedEffects.Swept(old.Place));
        }
        using var with = Begin(SaveArchive.Read(Write(first)));
        Assert.NotNull(with.Place.Thing(pad));
        Assert.Equal(things + 1, with.Place.Things.Count);                              // the pad, and no other thing
        with.Step(30);
        Same("with the record, against a save with none", without, Again(with));
    }

    [Fact]
    public void After_a_load_the_pad_is_back_under_its_number_riding_him_and_the_shell_is_on_as_it_stood()
    {
        SaveGame first;
        int pad, effectGuid, things;
        Matrix4x4 relative;
        using (var g = Begin())
        {
            var potence = PutOn(g, Potence);
            g.Step(45);
            var rider = MadeBy(g, potence).Single(t => t.Template == "sawToothBlue");
            pad = rider.Guid;
            relative = g.Place.Player!.Thing.Attachments.Single(a => ReferenceEquals(a.Child, rider)).Relative;
            effectGuid = potence.Guid;
            g.Step(90);                                                                 // the opening ring gone by itself
            Assert.DoesNotContain(g.Place.Things, t => t.Template == "ringTower");
            first = SaveCapture.Take(g.Place, g.Session);
        }
        using (var bare = Begin(Stripped(first))) things = bare.Place.Things.Count;       // the same save with no record: an older one
        using var back = Begin(SaveArchive.Read(Write(first)));
        var him = back.Place.Player!.Thing;
        var effect = him.Effects.Find(effectGuid)!;
        var again = back.Place.Thing(pad)!;
        Assert.NotNull(again);
        Assert.Equal("sawToothBlue", again.Template);
        Assert.True(again.InWorld);
        Assert.Same(him, again.AttachedTo);
        var link = him.Attachments.Single(a => ReferenceEquals(a.Child, again));
        Assert.Equal((-1, Attachments.AutoRemove, relative), (link.Bone, link.Flags, link.Relative));
        Assert.Equal(effectGuid, again.MadeByEffect);
        Assert.Equal(things + 1, back.Place.Things.Count);                              // one thing more than the load of the save without the record
        // The script was not run again: its own word of the pad is the one it saved, and there is one pad.
        Assert.Equal(pad, back.Scenes.SaveObject(effect.Script!)!.Values[0].Int);
        Assert.Single(back.Place.Things, t => t.Template == "sawToothBlue");
        Assert.DoesNotContain(back.Place.Things, t => t.Template == "ringTower");       // nor its opening ring
        // The shell as it stood.
        Assert.Equal(("redCloudShell_b", 0.3f, false, effectGuid), (him.Fx!.Shell, him.Fx.ShellAlpha, him.Fx.ShellRamping, him.Fx.ShellEffect));
        // Kept back from a network game's players as the script's own pad was (each makes its own).
        Assert.True(MadeHere.Scripted(again) && MadeHere.Own(again));

        // It rides him: where he goes it goes.
        back.Place.Move(him, him.Position + new Vector3(40f, 0f, 0f));
        back.Step(2);
        Assert.Equal(him.Position, again.Position);

        // And a save taken now says the same of it as the first did: nothing is lost by a second round.
        var second = SaveCapture.Take(back.Place, back.Session).Party.Members[0].Effects!.Single(e => e.Guid == effectGuid);
        Assert.Equal(pad, Assert.Single(second.Made!).Guid);
        Assert.Equal("redCloudShell_b", Assert.Single(second.Shells!).Material);
    }

    /// <summary>
    /// The check the repair stands on: the script kept the pad's number, so with the pad back under it the
    /// script's own `EndEffect` takes the pad and the shell away when the effect ends — run out here, not
    /// taken off. Without the number it would stay for good.
    /// </summary>
    [Fact]
    public void The_effect_ending_after_a_load_takes_the_pad_and_the_shell_away()
    {
        SaveGame first;
        int pad, effectGuid;
        using (var g = Begin())
        {
            var potence = PutOn(g, Potence, ms: 8_000);
            g.Step(60);
            pad = MadeBy(g, potence).Single(t => t.Template == "sawToothBlue").Guid;
            effectGuid = potence.Guid;
            first = SaveCapture.Take(g.Place, g.Session);
        }
        using var back = Begin(SaveArchive.Read(Write(first)));
        var him = back.Place.Player!.Thing;
        Assert.NotNull(back.Place.Thing(pad));
        Assert.NotNull(him.Fx!.Shell);
        for (int i = 0; i < 30 * 10 && him.Effects.Find(effectGuid) is not null; i++) back.Step();
        Assert.Null(him.Effects.Find(effectGuid));                                      // it ran out
        back.Step(2);
        Assert.Null(back.Place.Thing(pad));
        Assert.DoesNotContain(back.Place.Things, t => t.Template == "sawToothBlue");
        Assert.Null(him.Fx.Shell);
        Assert.Equal(0, him.Fx.ShellEffect);
        Assert.Equal(0, LoadedEffects.Swept(back.Place));                               // the script took both itself: nothing was left for the engine
    }

    /// <summary>
    /// **Not every script keeps its number.** `EffectDiscTheftOfVitae` holds its beam's GUID in a field and
    /// declares no `save`, so after a load the field is 0 and its `EndEffect` removes nothing — in 1.1 there
    /// is nothing to remove. With the beam made again, the engine takes it when the effect ends
    /// (<see cref="LoadedEffects.Ended"/>): a look a load made again never outlives its effect.
    /// </summary>
    [Fact]
    public void What_a_load_made_again_goes_with_its_effect_even_when_the_script_lost_its_number()
    {
        SaveGame first;
        int beam, effectGuid, victimGuid, bone;
        using (var g = Begin())
        {
            var christof = g.Place.Player!.Thing;
            var victim = g.Place.Things.Where(t => t.IsActor && !ReferenceEquals(t, christof) && t.Animation is not null && t.InWorld)
                                       .OrderBy(t => Vector3.Distance(t.Position, christof.Position)).First();
            var theft = EffectRuntime.Apply(victim, "ef_disc_theftofvitae2", 0, 60_000, creator: christof)!;
            g.Step(2);
            Assert.NotNull(theft.Script);
            Assert.Null(g.Scenes.SaveObject(theft.Script!));                            // the class declares no `save`: nothing is kept
            var stream = MadeBy(g, theft).Single(t => t.Template == "bloodStream");
            Assert.Same(victim, stream.AttachedTo);
            Assert.Equal(2, stream.Keyframes.Length);                                   // the beam's two ends
            (beam, effectGuid, victimGuid) = (stream.Guid, theft.Guid, victim.Guid);
            bone = victim.Attachments.Single(a => ReferenceEquals(a.Child, stream)).Bone;       // the chest's, where the motion set has one
            first = SaveCapture.Take(g.Place, g.Session);
            var record = first.World.Values.SelectMany(w => w.Things.Values).Single(t => t.Guid == victimGuid).Effects!.Single(e => e.Guid == effectGuid);
            Assert.Equal(2, Assert.Single(record.Made!).Keyframes!.Count);
        }
        using var back = Begin(SaveArchive.Read(Write(first)));
        var again = back.Place.Thing(beam)!;
        var bitten = back.Place.Thing(victimGuid)!;
        Assert.Equal("bloodStream", again.Template);
        Assert.Same(bitten, again.AttachedTo);
        Assert.Equal(2, again.Keyframes.Length);
        Assert.Equal(bone, bitten.Attachments.Single(a => ReferenceEquals(a.Child, again)).Bone);    // on the bone it rode
        EffectRuntime.Remove(bitten, bitten.Effects.Find(effectGuid)!, notify: true);
        Assert.Null(back.Place.Thing(beam));
        Assert.Equal(1, LoadedEffects.Swept(back.Place));
        // The drinker's flash, put back part-way, is left to fade as it would have been: the engine ends only a shell that would stay.
        var him = back.Place.Player!.Thing;
        Assert.Equal(("redCloudShell", true), (him.Fx?.Shell, him.Fx?.ShellEndsByItself));
        back.Step(70);
        Assert.Null(him.Fx!.Shell);
    }

    /// <summary>
    /// Heightened Senses, the report's own: its shell is a flash (0x5000: in over a second and out over the
    /// next), so a save taken inside the flash has it part-way and a load puts it back part-way, to end when
    /// it would have; the rosette stays until the effect ends.
    /// </summary>
    [Fact]
    public void Heightened_senses_keeps_its_rosette_and_a_flash_saved_half_over_ends_when_it_would_have()
    {
        SaveGame first;
        int pad, effectGuid;
        float alpha;
        using (var g = Begin())
        {
            var senses = PutOn(g, Senses);
            g.Step(40);                                                                 // 1.4 s: fading out
            var him0 = g.Place.Player!.Thing;
            Assert.True(him0.Fx!.ShellRamping);
            alpha = him0.Fx.ShellAlpha;
            Assert.InRange(alpha, 0.05f, 0.85f);
            pad = MadeBy(g, senses).Single().Guid;
            Assert.Equal("rosettePad", g.Place.Thing(pad)!.Template);
            effectGuid = senses.Guid;
            first = SaveCapture.Take(g.Place, g.Session);
        }
        using var back = Begin(SaveArchive.Read(Write(first)));
        var him = back.Place.Player!.Thing;
        Assert.Equal("rosettePad", back.Place.Thing(pad)?.Template);
        Assert.Equal(("redCloudShell_b", true), (him.Fx!.Shell, him.Fx.ShellRamping));
        Assert.Equal(alpha, him.Fx.ShellAlpha, 3);
        back.Step(30);                                                                  // the rest of the flash, and it is gone
        Assert.Null(him.Fx.Shell);
        Assert.NotNull(back.Place.Thing(pad));                                          // the rosette stays
        EffectRuntime.Remove(him, him.Effects.Find(effectGuid)!, notify: true);
        back.Step(2);
        Assert.Null(back.Place.Thing(pad));
    }

    /// <summary>
    /// Nothing for an effect no longer there. 1.1's post-load pass takes Mist Form off,
    /// and its mist with it: the load makes nothing for it. And a record whose number another thing now holds is
    /// passed over, not made under another number.
    /// </summary>
    [Fact]
    public void Nothing_is_made_for_an_effect_the_load_takes_off_or_for_a_number_that_is_taken()
    {
        SaveGame first;
        int mist, pad, potenceGuid;
        using (var g = Begin())
        {
            var form = PutOn(g, "ef_disc_mistform");
            var potence = PutOn(g, Potence);
            g.Step(30);
            mist = MadeBy(g, form).Single(t => t.Template == "greenMist").Guid;
            pad = MadeBy(g, potence).Single(t => t.Template == "sawToothBlue").Guid;
            potenceGuid = potence.Guid;
            first = SaveCapture.Take(g.Place, g.Session);
            Assert.Contains(first.Party.Members[0].Effects!, e => e.Name == "ef_disc_mistform" && e.Made is { Count: 1 });
        }
        using (var back = Begin(SaveArchive.Read(Write(first))))
        {
            var him = back.Place.Player!.Thing;
            Assert.Null(him.Effects.Find("ef_disc_mistform"));
            Assert.Null(back.Place.Thing(mist));
            Assert.DoesNotContain(back.Place.Things, t => t.Template == "greenMist");
            Assert.NotNull(back.Place.Thing(pad));                                      // the other effect's is back
        }
        // The pad's record given Christof's own number: taken, so no pad at all.
        var edited = SaveArchive.Read(Write(first));
        edited.Party.Members[0].Effects!.Single(e => e.Guid == potenceGuid).Made![0].Guid = edited.Party.Members[0].Guid;
        using var taken = Begin(SaveArchive.Read(Write(edited)));
        Assert.DoesNotContain(taken.Place.Things, t => t.Template == "sawToothBlue");
        Assert.Equal("redCloudShell_b", taken.Place.Player!.Thing.Fx?.Shell);           // its shell is still put back
    }

    /// <summary>
    /// **Prison of Ice's frozen pose, through a save and a load** (asked whether the repair
    /// should bring it back too: <i>"Yes."</i>). 1.1's script freezes its target as the effect starts
    /// (`EffectDiscPrisonOfIce.effectstarted`: `FreezeAnimations`, the model's bit 0x20000, which stops the mixer,
    /// the model's flag). The save writes the model's flag word with the bit in it and the reader throws the word away
    /// (it puts 0 in its place), and nothing of what a model is playing is kept but an animation
    /// held at its end — so in 1.1 a load leaves the target held by the rules (`ef_disc_prisonofice2` is
    /// IMMOBILIZE, which a save keeps) and standing at ease in the picture. With the repair the freeze is marked
    /// the effect's as the script sets it, every save records the pose of a model its effect froze, and a load
    /// freezes the model in that pose: the same to the last bone, held until the effect ends, when the script's
    /// own `EndEffect` lets it go and it eases into what it is doing.
    ///
    /// **The picture and nothing else**: the pose is laid in with the mixer's own fade and no channel is put back,
    /// so what the character is playing after the load is what the load gave it, as in 1.1 — no swing is carried
    /// across, and no tag fires that would not have.
    /// </summary>
    [Fact]
    public void Prison_of_ice_holds_its_target_in_the_pose_it_was_frozen_in_after_a_load()
    {
        string[] Ice = ["ef_disc_prisonofice1", "ef_disc_prisonofice2"];               // the discipline puts both on; one carries the script
        static Vector3[] Pose(WorldThing t)
        {
            t.Animation!.PoseBones();
            return [.. Enumerable.Range(0, t.Animation.BoneCount).Select(t.Animation.BoneRotation)];
        }
        static void Close(Vector3[] a, Vector3[] b)
        {
            Assert.Equal(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++) Assert.True(Vector3.Distance(a[i], b[i]) < 1e-3f, $"bone {i}: {a[i]} against {b[i]}");
        }
        static float Apart(Vector3[] a, Vector3[] b) => a.Zip(b, Vector3.Distance).Max();
        SaveGame first;
        int effectGuid, victimGuid;
        Vector3[] frozen;
        using (var g = Begin())
        {
            var christof = g.Place.Player!.Thing;
            var victim = g.Place.Things.Where(t => t.IsActor && !ReferenceEquals(t, christof) && t.Animation is not null && t.InWorld)
                                       .OrderBy(t => Vector3.Distance(t.Position, christof.Position)).First();
            // Caught in the middle of something: an action a few frames in, still fading in over its stand.
            Pose(victim);
            var doing = victim.Animation!.Set.Modes.First(m => !m.Has(Formats.NmsFlags.NoFadeIn) && m.Name != victim.Animation.Mode?.Name
                                                                 && g.Place.Motions!.Animation(m.File) is { Duration: > 10f });
            Assert.True(victim.Animation.Play(doing, g.Place.Motions!.Animation(doing.File)!, g.Place.Motions, onMotion: false) > 0);
            victim.Animation.Advance(0.1f);
            Assert.InRange(victim.Animation.Fade, 0.1f, 0.9f);
            var both = Ice.Select(name => EffectRuntime.Apply(victim, name, 0, 60_000, creator: christof)!).ToList();
            Assert.All(both, Assert.NotNull);
            g.Step(2);
            var ice = Assert.Single(both, e => e.Script is not null);
            Assert.True(victim.Actor!.Has2(Actor.Flag2.Restrained));                    // held by the rules: the other effect's, which a save keeps
            Assert.True(victim.Fx!.Frozen);
            Assert.Equal(ice.Guid, victim.Fx.FrozenEffect);                             // marked as the script froze it
            Assert.Equal(("iceShell", ice.Guid), (victim.Fx.Shell, victim.Fx.ShellEffect));
            frozen = Pose(victim);
            var at = (victim.Animation.Mode?.Name, victim.Animation.Player!.Frame, victim.Animation.Fade);
            Assert.True(at.Frame > 0f);
            g.Step(45);
            Assert.Equal(frozen, Pose(victim));                                         // held: the mixer is not advanced

            (effectGuid, victimGuid) = (ice.Guid, victim.Guid);
            first = SaveCapture.Take(g.Place, g.Session);
            // Taking the save moved nothing of the mixer.
            Assert.Equal(at, (victim.Animation.Mode?.Name, victim.Animation.Player!.Frame, victim.Animation.Fade));
            var record = first.World.Values.SelectMany(w => w.Things.Values).Single(t => t.Guid == victimGuid).Effects!.Single(e => e.Guid == effectGuid);
            var held = Assert.Single(record.Frozen!);
            Assert.Equal(victimGuid, held.On);
            Assert.Equal((victim.Animation.BoneCount * 3, victim.Animation.BoneCount * 3), (held.Rotation.Length, held.Translation.Length));
            Assert.Equal(frozen, Enumerable.Range(0, frozen.Length).Select(i => new Vector3(held.Rotation[i * 3], held.Rotation[i * 3 + 1], held.Rotation[i * 3 + 2])));
            Assert.Equal("iceShell", Assert.Single(record.Shells!).Material);
            // It goes through the file.
            var read = SaveArchive.Read(Write(first)).World.Values.SelectMany(w => w.Things.Values).Single(t => t.Guid == victimGuid).Effects!.Single(e => e.Guid == effectGuid);
            Assert.Equal(held.On, read.Frozen![0].On);
            Assert.Equal(held.Rotation, read.Frozen[0].Rotation);
            Assert.Equal(held.Translation, read.Frozen[0].Translation);
            // A freeze that is no effect's is no save's business (the console's `freeze`).
            christof.Model3D.Frozen = true;
            Assert.Equal(0, christof.Fx!.FrozenEffect);
            Assert.DoesNotContain(SaveCapture.Take(g.Place, g.Session).Party.Members[0].Effects ?? [], e => e.Frozen is not null);
            christof.Model3D.Frozen = false;
        }

        // A save without the record — any older one — loads as it did, and as 1.1 does: held, and at ease.
        (string? Mode, float Frame) loaded;
        using (var bare = Begin(Stripped(first)))
        {
            var loose = bare.Place.Thing(victimGuid)!;
            Assert.NotNull(loose.Effects.Find(effectGuid));
            Assert.True(loose.Actor!.Has2(Actor.Flag2.Restrained));
            Assert.False(loose.Fx?.Frozen ?? false);
            Assert.True(Apart(frozen, Pose(loose)) > 1f);                               // not the pose it was frozen in
            loaded = (loose.Animation!.Mode?.Name, loose.Animation.Player!.Frame);
        }

        using var back = Begin(SaveArchive.Read(Write(first)));
        var iced = back.Place.Thing(victimGuid)!;
        Assert.NotNull(iced.Effects.Find(effectGuid));
        Assert.True(iced.Fx!.Frozen);
        Assert.Equal(effectGuid, iced.Fx.FrozenEffect);
        Assert.Equal("iceShell", iced.Fx.Shell);
        Close(frozen, Pose(iced));                                                      // the pose, to the last bone
        // And nothing else of the mixer: what it plays is what the load gave it, as the load without the record did.
        Assert.Equal(loaded, (iced.Animation!.Mode?.Name, iced.Animation.Player!.Frame));
        back.Step(90);
        Close(frozen, Pose(iced));                                                      // held there
        Assert.Equal(loaded, (iced.Animation.Mode?.Name, iced.Animation.Player!.Frame));
        // A save taken now says the same of it: nothing is lost by a second round.
        var second = SaveCapture.Take(back.Place, back.Session).World.Values.SelectMany(w => w.Things.Values).Single(t => t.Guid == victimGuid).Effects!.Single(e => e.Guid == effectGuid);
        Close(frozen, [.. Enumerable.Range(0, frozen.Length).Select(i => new Vector3(Assert.Single(second.Frozen!).Rotation[i * 3], second.Frozen![0].Rotation[i * 3 + 1], second.Frozen[0].Rotation[i * 3 + 2]))]);

        // The effect ends: the script's own `EndEffect` lets it go and takes the ice off; nothing is left for the engine.
        EffectRuntime.Remove(iced, iced.Effects.Find(effectGuid)!, notify: true);
        Assert.False(iced.Fx.Frozen);
        Assert.Equal(0, iced.Fx.FrozenEffect);
        Assert.Equal(0, LoadedEffects.Swept(back.Place));
        back.Step(30);
        Assert.Null(iced.Fx.Shell);
        Assert.Equal(0f, iced.Animation.Fade);                                          // eased out of the held pose, a fifth of a second
        Assert.True(Apart(frozen, Pose(iced)) > 1f);                                    // and moving again
    }
}
