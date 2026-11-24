// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// A master's pets as a list: they take his new target — and drop it with
/// him — unless afraid, dazed, mesmerised, frenzied or possessed (from <c>SetTarget</c>);
/// they are all let go when the party leaves a place and when their master's
/// body is taken away — a summoned one runs and fades, a beckoned one goes
/// home. Earlier, a pet took its master's target only as it joined, stayed behind in pet mode through
/// an exit with its slot still taken, and stood about with no master once an enemy summoner's body
/// was gone.
/// </summary>
public class PetListTests
{
    /// <summary>The Monastery with Christof in the party and the scene's own people about.</summary>
    static (Location Place, Minds Minds, Mind Christof) Monastery(Vfs.GameFiles files)
    {
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var lead = place.Player!.Thing;
        lead.Actor!.Flags |= Actor.Flag.Party;
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        return (place, minds, minds.All.Single(m => ReferenceEquals(m.Thing, lead)));
    }

    static WorldThing Named(Location place, string template) =>
        place.Things.First(t => string.Equals(t.Template, template, StringComparison.OrdinalIgnoreCase) && t.Mind is not null);

    /// <summary>An elemental summoned beside <paramref name="master"/>, as Summon Elemental makes one.</summary>
    static WorldThing Summon(Location place, WorldThing master)
    {
        var elemental = place.Place("elemental", master.Position + new Vector3(60f, 0f, 0f), master.Orientation, master.Sector)!;
        elemental.Actor!.Flags |= Actor.Flag.Summoned;
        Assert.NotNull(EffectRuntime.Apply(elemental, "ef_disc_summonelemental", 1, durationMs: 15000, creator: master));
        Assert.Equal(master.Guid, elemental.Mind!.MasterGuid);
        return elemental;
    }

    /// <summary>
    /// **The master's target is the pets' target**: Christof takes a Cappadocian and his
    /// elemental takes her too; he lets her go and so does it — <c>SetTarget(0)</c> is handed on
    /// like any other.
    /// </summary>
    [Fact]
    public void A_pet_takes_its_masters_new_target_and_lets_it_go_with_him()
    {
        using var files = Install.Open();
        var (place, minds, christof) = Monastery(files);
        var elemental = Summon(place, christof.Thing);
        var her = Named(place, "cappadocian_female_conv1");
        Assert.Null(elemental.Mind!.Target);

        minds.SetTarget(christof, her);
        Assert.Same(her, elemental.Mind.Target);

        minds.SetTarget(christof, null);
        Assert.Null(elemental.Mind.Target);
    }

    /// <summary>
    /// **A pet in a mode of its own is passed over**: afraid, dazed,
    /// mesmerised, frenzied, or possessed — each keeps what it had.
    /// </summary>
    [Theory]
    [InlineData(AiMode.Afraid)]
    [InlineData(AiMode.Dazed)]
    [InlineData(AiMode.Mesmerized)]
    [InlineData(AiMode.Frenzied)]
    [InlineData(AiMode.Possessing)]
    public void A_pet_in_a_mode_of_its_own_does_not_follow(AiMode mode)
    {
        using var files = Install.Open();
        var (place, minds, christof) = Monastery(files);
        var elemental = Summon(place, christof.Thing);
        var pet = elemental.Mind!;
        // The fifth test is the possessed mark, not a mode.
        if (mode == AiMode.Possessing) pet.Flags |= Mind.FlagPossessed;
        else pet.Enter(mode, andBase: false);

        minds.SetTarget(christof, Named(place, "cappadocian_female_conv1"));
        Assert.Null(pet.Target);
    }

    /// <summary>
    /// **The master's body taken away lets his pets go**. A summon's own
    /// effect dies with its creator (at the end of the kill), which lets a disciplined
    /// elemental go the moment its summoner dies; this is for the pet whose joining does not — one a
    /// script or an effect without that flag made. It stays hers while she lies dead, and when her
    /// body is removed it runs, afraid, and fades over five seconds; her list goes with her.
    /// </summary>
    [Fact]
    public void A_masters_body_taken_away_lets_his_pet_go()
    {
        using var files = Install.Open();
        var (place, minds, _) = Monastery(files);
        var her = Named(place, "cappadocian_female_conv1");
        var elemental = place.Place("elemental", her.Position + new Vector3(60f, 0f, 0f), her.Orientation, her.Sector)!;
        elemental.Actor!.Flags |= Actor.Flag.Summoned;
        Assert.True(minds.AddPet(her.Mind!, elemental));
        Assert.True(Damage.Kill(her, null, HitKind.Melee, DamageType.Lethal, place.Milliseconds));
        Assert.Equal(her.Guid, elemental.Mind!.MasterGuid);   // dead is not gone

        Assert.True(place.Remove(her, drop: false));

        Assert.Equal(0, elemental.Mind.MasterGuid);
        Assert.Equal(AiMode.Afraid, elemental.Mind.Mode);
        Assert.Equal(minds.Now + Minds.SummonedLinger, elemental.ExpireAt);
        Assert.True(elemental.Model3D.AlphaRamping);
        Assert.Null(her.Mind!.Pets);
    }

    /// <summary>
    /// **The party leaving a place lets every pet go**, through the campaign's
    /// own journey: Christof's summoned elemental runs and fades in the place left behind, and a
    /// beckoned one — a pet without <c>THING_AF_SUMMONED</c> — loses the mark and goes back to its home
    /// mode. His list is empty.
    /// </summary>
    [Fact]
    public void The_party_leaving_a_place_lets_every_pet_go()
    {
        using var files = Install.Open();
        var chronicle = ChronicleSet.Load(files)["prague"]!;
        var session = new Session { Chronicle = chronicle };
        Location Build(string name, string? scene, int entrance)
        {
            var built = Location.Load(files, name, things: false, sky: false, scene: scene, player: "Christof", entrance: entrance);
            built.Session = session;
            return built;
        }
        chronicle.Current = chronicle.Find("Smithy");
        var at = chronicle.At(chronicle.Current)!;
        var here = Build(at.LevelName, at.SceneName.Length > 0 ? at.SceneName : null, -1);
        session.Here = here;
        var lead = here.Player!.Thing;
        lead.Actor!.Flags |= Actor.Flag.Party;
        var minds = here.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var christof = minds.All.Single(m => ReferenceEquals(m.Thing, lead));
        var summoned = Summon(here, lead);
        var beckoned = here.Place("elemental", lead.Position + new Vector3(-60f, 0f, 0f), lead.Orientation, lead.Sector)!;
        Assert.True(minds.AddPet(christof, beckoned));
        Assert.Equal(2, Minds.PetCount(christof));

        var campaign = new Campaign { Chronicle = chronicle, Build = Build, Here = here };
        Assert.True(chronicle.Jump("Unorna", 0));
        Assert.NotNull(campaign.Travel());

        Assert.Equal(0, Minds.PetCount(christof));
        Assert.Equal(AiMode.Afraid, summoned.Mind!.Mode);
        Assert.Equal(0, summoned.Mind.MasterGuid);
        Assert.Equal(minds.Now + Minds.SummonedLinger, summoned.ExpireAt);
        var home = beckoned.Mind!;
        Assert.Equal(home.BaseMode, home.Mode);
        Assert.Equal(0u, home.Flags & Mind.FlagPet);
        Assert.Equal(0, home.MasterGuid);
        Assert.Null(home.Target);
    }
}
