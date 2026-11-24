// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **What the host's mover does to a player's character's mind**
/// (`Net.MoverMinds`; a comparison with 1.1's gap 13): a possession it is part of ends — the body it possesses let
/// go, or its own possessor's hold — and its mind starts over as a revive's does
/// **unless it is frenzied**. Driven on the Monastery with Christof and Wilhem and
/// the scene's Cappadocians, as `DisciplineModeTests`; the network half (the frenzy kept across a real
/// move) is `NetPlacesTests`, the carried body's `StoryCarryTests`.
/// </summary>
public class MoverMindTests
{
    static (Location Place, Minds Minds, Mind Christof, Mind Wilhem) Monastery(Vfs.GameFiles files)
    {
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var lead = place.Player!.Thing;
        var joined = place.Place("Wilhem", lead.Position + new Vector3(96f, 0f, 0f), lead.Orientation, lead.Sector)!;
        joined.Actor!.Flags |= Actor.Flag.Party;
        lead.Actor!.Flags |= Actor.Flag.Party;
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        return (place, minds, lead.Mind!, joined.Mind!);
    }

    static WorldThing Body(Location place) =>
        place.Things.First(t => string.Equals(t.Template, "cappadocian_female_conv1", StringComparison.OrdinalIgnoreCase) && t.Mind is not null);

    static bool Possessing(WorldThing body) => body.Effects.All.Any(e => e.Kind == EffectKind.Possessing && !e.Spent);

    /// <summary>
    /// **A possessor moved lets the body go and starts over.** Christof possessing the Cappadocian (mode 13,
    /// the body flagged possessed): the mover takes the possessing effect off her, which ends the
    /// possession (the controls Christof's again, her mind free); then his mind is reset:
    /// his home mode, and **no target** — the possession's own end keeps the body as it.
    /// </summary>
    [Fact]
    public void A_possessor_moved_lets_the_body_go_and_his_mind_starts_over()
    {
        using var files = Install.Open();
        var (place, _, christof, _) = Monastery(files);
        var body = Body(place);
        Assert.NotNull(EffectRuntime.Apply(body, "ef_disc_possession", 1, durationMs: 90000, creator: christof.Thing));
        Assert.Equal(AiMode.Possessing, christof.Mode);
        Assert.Same(body, place.Steered);

        Assert.False(MoverMinds.Leaving(christof.Thing));
        Assert.False(Possessing(body));
        Assert.Equal(0u, body.Mind!.Flags & Mind.FlagPossessed);
        Assert.Same(christof.Thing, place.Steered);
        Assert.Equal(christof.BaseMode, christof.Mode);
        Assert.Null(christof.Target);
    }

    /// <summary>
    /// **A possessed body moved is let go** (its mind's bit 0x10000000; the possession effect ended on the mover at
    /// Its possessing effect comes off, the possessor has the controls back, and its own mind
    /// starts over.
    /// </summary>
    [Fact]
    public void A_possessed_body_moved_is_let_go()
    {
        using var files = Install.Open();
        var (place, _, christof, _) = Monastery(files);
        var body = Body(place);
        Assert.NotNull(EffectRuntime.Apply(body, "ef_disc_possession", 1, durationMs: 90000, creator: christof.Thing));

        Assert.False(MoverMinds.Leaving(body));
        Assert.False(Possessing(body));
        Assert.Equal(0u, body.Mind!.Flags & Mind.FlagPossessed);
        Assert.Equal(body.Mind.BaseMode, body.Mind.Mode);
        Assert.Same(christof.Thing, place.Steered);
        Assert.NotEqual(AiMode.Possessing, christof.Mode);
    }

    /// <summary>
    /// **A frenzied mover keeps its frenzy; an afraid one starts over** (the mind reset skips the
    /// reset only for mode 9). Wilhem in frenzy is left in mode 9 — the mover answers it, for the other side
    /// to put back — and Wilhem afraid comes out of his fear into his home mode, his fear's effect left to run.
    /// </summary>
    [Fact]
    public void A_frenzied_mover_keeps_his_frenzy_and_an_afraid_one_starts_over()
    {
        using var files = Install.Open();
        var (_, minds, _, wilhem) = Monastery(files);
        Frenzy.Begin(wilhem.Thing);
        Assert.True(Frenzy.Frenzied(wilhem.Thing));
        Assert.True(MoverMinds.Leaving(wilhem.Thing));
        Assert.Equal(AiMode.Frenzied, wilhem.Mode);

        Frenzy.End(wilhem.Thing);
        minds.Frighten(wilhem, null, force: true);
        Assert.Equal(AiMode.Afraid, wilhem.Mode);
        Assert.False(MoverMinds.Leaving(wilhem.Thing));
        Assert.Equal(wilhem.BaseMode, wilhem.Mode);
    }

    /// <summary>
    /// **The host's own journey alone** (slot 0's own journey; `MoverMinds.Journeyed`): the character it arrives
    /// as is put in frenzy when the one that left was frenzied (ours, as the player's arrival), and otherwise
    /// its mind starts over. Here Wilhem stands for the one that left and Christof for the arrival.
    /// </summary>
    [Fact]
    public void The_host_s_journey_keeps_a_frenzy_and_otherwise_starts_the_mind_over()
    {
        using var files = Install.Open();
        var (_, minds, christof, wilhem) = Monastery(files);
        Frenzy.Begin(wilhem.Thing);
        MoverMinds.Journeyed(wilhem.Thing, christof.Thing);
        Assert.Equal(AiMode.Frenzied, christof.Mode);

        Frenzy.End(wilhem.Thing);
        Frenzy.End(christof.Thing);
        minds.Frighten(christof, null, force: true);
        MoverMinds.Journeyed(wilhem.Thing, christof.Thing);
        Assert.Equal(christof.BaseMode, christof.Mode);
    }
}
