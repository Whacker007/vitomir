// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A load gives three minds back** (<see cref="LoadedMinds"/>),
/// through a real save and load. A save keeps no mind, so every character's comes back in its home
/// mode — and then a character still carrying `ef_frenzy` goes into frenzy again, one carrying a
/// fear is made afraid (of nobody) and one carrying Majesty is struck (by nobody). Mesmerised and
/// dazed are not given back, in 1.1 either. Found by a comparison with 1.1:
/// before it, a frenzy saved part-way through never ended after the load.
/// </summary>
public class LoadedMindsTests
{
    sealed record Game(Location Place, Session Session, Chronicle Chronicle, EffectDefinitions Effects, DisciplineDefinitions Powers);

    static Game Start(GameFiles files, bool awaken = true)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        if (awaken) place.Awaken(new AiClasses(files), 1, effects, powers);
        var chronicle = Chronicle.Load(files, "Prague");
        chronicle.Current = chronicle.Find("OldTownDay");
        var session = new Session { Here = place, Chronicle = chronicle };
        place.Session = session;
        return new Game(place, session, chronicle, effects, powers);
    }

    /// <summary>Saved, written, read and loaded into a freshly built place, as a load does it.</summary>
    static Location RoundTrip(GameFiles files, Game g)
    {
        string path = Path.Combine(Path.GetTempPath(), $"vitomir-loadedminds-{Guid.NewGuid():N}.vsave");
        SaveGame save;
        try
        {
            SaveArchive.Write(SaveCapture.Take(g.Place, g.Session), path);
            save = SaveArchive.Read(path);
        }
        finally { File.Delete(path); }
        var fresh = Start(files, awaken: false);
        SaveApply.Chronicle(fresh.Chronicle, save.Chronicle!);
        var world = save.World[save.Header.Location];
        var place = Location.Load(files, world.Level, things: true, sky: false,
                                  scene: world.Scene.Length > 0 ? world.Scene : null, player: "Christof", entrance: world.Entrance);
        var restore = SaveApply.Place(place, world, fresh.Effects, fresh.Powers);
        SaveApply.Party(place, save.Party, restore, fresh.Effects, fresh.Powers);
        place.Awaken(new AiClasses(files), 1, fresh.Effects, fresh.Powers, fromSave: true);
        return place;
    }

    static WorldThing Same(Location before, WorldThing thing, Location after)
    {
        string id = ThingIds.Of(before, thing)!;
        return after.Things.First(t => ThingIds.Of(after, t) == id);
    }

    /// <summary>
    /// **A frenzy saved part-way through is a frenzy after the load, and ends as one.** The Frenzy
    /// stat is down to 60 — under the 100 that starts one, over the 25 that ends one — so nothing in
    /// the stat's own check acts on the load; it is `ef_frenzy` that puts the mind back
    /// in mode 9, which takes the controls from the player. Soothed afterwards, it ends,
    /// taking its strength and dexterity with it, because the end asks for mode 9.
    /// </summary>
    [Fact]
    public void A_frenzy_saved_part_way_through_is_a_frenzy_after_the_load_and_ends_as_one()
    {
        using var files = Install.Open();
        var g = Start(files);
        var christof = g.Place.Player!.Thing;
        Frenzy.Begin(christof);
        Assert.True(Frenzy.Frenzied(christof));
        christof.Actor!.Stat[(int)ActorStat.Frenzy] = 60f;

        var back = RoundTrip(files, g);
        var him = back.Player!.Thing;
        Assert.NotNull(him.Effects.Find("ef_frenzy"));
        Assert.Equal(AiMode.Frenzied, him.Mind!.Mode);
        Assert.True(Frenzy.Frenzied(him));

        Frenzy.Soothe(him);
        Assert.False(Frenzy.Frenzied(him));
        Assert.Null(him.Effects.Find("ef_frenzy"));
        Assert.Null(him.Effects.Find("ef_frz_strength"));
    }

    /// <summary>
    /// **Afraid and struck come back with nobody to fear or admire; mesmerised does not come back.**
    /// A fear (kind 0x19) is given back afraid of nobody and not forced (the fear set with 0, 0
    /// by the load); Majesty (kind 0x20) struck by nobody. The
    /// mesmerised one comes up in its home mode, its effect still running: 1.1's pass has no test
    /// for kind 0x17.
    /// </summary>
    [Fact]
    public void Afraid_and_struck_come_back_with_nobody_and_mesmerised_does_not_come_back()
    {
        using var files = Install.Open();
        var g = Start(files);
        var christof = g.Place.Player!.Thing;
        var three = g.Place.Minds!.All
            .Where(m => !ReferenceEquals(m.Thing, christof) && m.Thing.Actor is { Dead: false }
                        && m.Class.MindFlags == 0 && (m.Flags & Mind.FlagUnaffected) == 0)
            .Take(3).ToArray();
        Assert.Equal(3, three.Length);
        Assert.NotNull(EffectRuntime.Apply(three[0].Thing, "ef_afraid", 0, durationMs: 600_000, creator: christof));
        Assert.NotNull(EffectRuntime.Apply(three[1].Thing, "ef_disc_majesty2", 4, durationMs: 600_000, creator: christof));
        Assert.NotNull(EffectRuntime.Apply(three[2].Thing, "ef_mesmerized", 0, durationMs: 600_000, creator: christof));
        Assert.Equal(AiMode.Afraid, three[0].Mode);
        Assert.Equal(AiMode.Majesty, three[1].Mode);
        Assert.Equal(AiMode.Mesmerized, three[2].Mode);

        var back = RoundTrip(files, g);
        var afraid = Same(g.Place, three[0].Thing, back).Mind!;
        var struck = Same(g.Place, three[1].Thing, back).Mind!;
        var mesmerised = Same(g.Place, three[2].Thing, back);

        Assert.True(afraid.Thing.Effects.Has(EffectKind.Afraid));
        Assert.Equal(AiMode.Afraid, afraid.Mode);
        Assert.Null(afraid.Target);
        Assert.True(struck.Thing.Effects.Has(EffectKind.Majesty));
        Assert.Equal(AiMode.Majesty, struck.Mode);
        Assert.Null(struck.Target);
        Assert.True(mesmerised.Effects.Has(EffectKind.Mesmerized));
        Assert.Equal(mesmerised.Mind!.BaseMode, mesmerised.Mind.Mode);
    }

    /// <summary>
    /// **The party's own reader does the same**: the party
    /// is read out of `!main.nsg` apart from the places (only outside a network session), and each
    /// member read is given its frenzy, fear and Majesty back as the place's things are. Here the
    /// party comes back through `SaveApply.Party` before the minds wake, so the same pass reaches it: Wilhem,
    /// a companion saved in frenzy, is in frenzy after the load. (A comparison with 1.1's gap 15 called the frenzy's reader the
    /// multiplayer join's copy; it is the single player's party — a joining player's character file is
    /// written with every effect taken off, and read without the pass.)
    /// </summary>
    [Fact]
    public void A_companion_saved_in_frenzy_is_in_frenzy_after_the_load()
    {
        using var files = Install.Open();
        var g = Start(files);
        var christof = g.Place.Player!.Thing;
        christof.Actor!.Flags |= Actor.Flag.Party;
        var wilhem = g.Place.Place("Wilhem", christof.Position + new System.Numerics.Vector3(96f, 0f, 0f), christof.Orientation, christof.Sector)!;
        wilhem.Actor!.Flags |= Actor.Flag.Party;
        Frenzy.Begin(wilhem);
        Assert.True(Frenzy.Frenzied(wilhem));

        var back = RoundTrip(files, g);
        var him = back.Things.Single(t => t.Template.Equals("Wilhem", StringComparison.OrdinalIgnoreCase) && t.InWorld);
        Assert.True(him.Actor!.Has(Actor.Flag.Party));
        Assert.NotNull(him.Effects.Find(LoadedMinds.FrenzyMarker));
        Assert.True(Frenzy.Frenzied(him));
    }
}
