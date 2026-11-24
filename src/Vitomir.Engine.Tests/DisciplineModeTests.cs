// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The three modes a discipline puts a mind into that were the last not written — possession (13),
/// majesty (14) and the pet (2) — as 1.1 has them
/// with the disciplines in the Monastery: Christof
/// possessing a Cappadocian, Majesty, Awe, Drawing Out the Beast, and Summon Elemental.
/// </summary>
public class DisciplineModeTests
{
    /// <summary>The Monastery with Christof and Wilhem in the party and the scene's own people about.</summary>
    static (Location Place, Minds Minds, Mind Christof, Mind Wilhem) Monastery(Vfs.GameFiles files)
    {
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var lead = place.Player!.Thing;
        var joined = place.Place("Wilhem", lead.Position + new Vector3(96f, 0f, 0f), lead.Orientation, lead.Sector)!;
        joined.Actor!.Flags |= Actor.Flag.Party;
        joined.Actor.Flags2 |= Actor.Flag2.InFormation;
        lead.Actor!.Flags |= Actor.Flag.Party;
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var christof = minds.All.Single(m => ReferenceEquals(m.Thing, lead));
        var wilhem = minds.All.Single(m => ReferenceEquals(m.Thing, joined));
        wilhem.Enter(AiMode.Party);
        return (place, minds, christof, wilhem);
    }

    static WorldThing Named(Location place, string template) =>
        place.Things.First(t => string.Equals(t.Template, template, StringComparison.OrdinalIgnoreCase) && t.Mind is not null);

    static void Run(Location place, Minds minds, int ms)
    {
        for (int t = 0; t < ms; t += 33) minds.Tick(0.033f);
    }

    // ---- possession --------------------------------------------------------------------------

    /// <summary>
    /// **The controls go to the body, and come back when it dies**. In 1.1
    /// Christof possessed `cappadocian_female_conv1`: he went to mode 13 with her as his target; her
    /// mind was marked possessed and stayed in its home mode; the player steered her for 33 seconds
    /// until she was cut down, and the controls were Christof's again in the same tenth of a second,
    /// his mode back to `party`. The party's selected member stays Christof all the while — so the
    /// mouse may order her (the refusal tests the selected member) and Wilhem keeps following him.
    /// </summary>
    [Fact]
    public void A_possession_hands_the_controls_to_the_body_and_back_when_it_dies()
    {
        using var files = Install.Open();
        var (place, _, christof, _) = Monastery(files);
        var body = Named(place, "cappadocian_female_conv1");
        var steered = new List<WorldThing>();
        place.SteeredChanged += steered.Add;

        Assert.NotNull(EffectRuntime.Apply(body, "ef_disc_possession", 1, durationMs: 90000, creator: christof.Thing));

        Assert.Same(body, place.Steered);
        Assert.Same(christof.Thing, place.Selected);
        Assert.Equal(AiMode.Possessing, christof.Mode);
        Assert.Same(body, christof.Target);
        Assert.NotEqual(0u, body.Mind!.Flags & Mind.FlagPossessed);
        Assert.Equal(AiMode.EnemySearch, body.Mind.Mode);
        Assert.Null(body.Mind.Target);
        Assert.False(CastInput.Refused(body));             // a possessed body takes mouse orders

        Damage.Kill(body, null, HitKind.Melee, DamageType.Lethal, nowMs: place.Milliseconds);

        Assert.Same(christof.Thing, place.Steered);
        Assert.Equal(AiMode.Party, christof.Mode);
        Assert.Same(body, christof.Target);                // the possession leaves the caster's target alone
        Assert.Equal(0u, body.Mind.Flags & Mind.FlagPossessed);
        Assert.Equal([body, christof.Thing], steered);
    }

    /// <summary>
    /// **A possessed body does not think**, steered or not — and the caster, badly hurt,
    /// lets it go (`verylowhealth`): every possessing effect on the body comes
    /// off and the controls come back.
    /// </summary>
    [Fact]
    public void A_badly_hurt_caster_lets_the_body_go()
    {
        using var files = Install.Open();
        var (place, minds, christof, _) = Monastery(files);
        var body = Named(place, "cappadocian_female_conv1");
        EffectRuntime.Apply(body, "ef_disc_possession", 1, durationMs: 90000, creator: christof.Thing);
        int thinks = body.Mind!.NextThink;
        Run(place, minds, 600);
        Assert.Equal(thinks, body.Mind.NextThink);          // no think taken
        Assert.Same(body, place.Steered);

        var me = christof.Thing.Actor!;
        me.Health = me.MaxHealth * 0.25f;                   // under the player's 0.30
        Run(place, minds, 300);

        Assert.Same(christof.Thing, place.Steered);
        Assert.Equal(AiMode.Party, christof.Mode);
        Assert.Equal(0u, body.Mind.Flags & Mind.FlagPossessed);
        Assert.DoesNotContain(body.Effects.All, e => e.Kind == EffectKind.Possessing && !e.Spent);
    }

    /// <summary>
    /// Refused: a skeleton, whose class says `NOPOSSESSED` (every skeleton's `mindflags` is 0x1f),
    /// and a body that is possessed already (the apply itself refuses).
    /// </summary>
    [Fact]
    public void A_skeleton_or_a_body_already_taken_cannot_be_possessed()
    {
        using var files = Install.Open();
        var (place, _, christof, wilhem) = Monastery(files);
        var skeleton = Named(place, "skel_sword");
        EffectRuntime.Apply(skeleton, "ef_disc_possession", 1, durationMs: 90000, creator: christof.Thing);
        Assert.Same(christof.Thing, place.Steered);
        Assert.Equal(AiMode.Party, christof.Mode);

        var body = Named(place, "cappadocian_female_conv1");
        EffectRuntime.Apply(body, "ef_disc_possession", 1, durationMs: 90000, creator: christof.Thing);
        Assert.Null(EffectRuntime.Apply(body, "ef_disc_possession", 1, durationMs: 90000, creator: wilhem.Thing));
        Assert.Equal(AiMode.Party, wilhem.Mode);
    }

    /// <summary>
    /// While a possession lasts **the party follows the caster**, who is still its selected member
    /// — not the body the player is walking about in — and taking the caster's own slot
    /// (F6) takes the controls straight back (selected, but not steered).
    /// </summary>
    [Fact]
    public void The_party_stays_with_the_caster_and_F6_takes_the_controls_back()
    {
        using var files = Install.Open();
        var (place, _, christof, _) = Monastery(files);
        var body = Named(place, "cappadocian_female_conv1");
        EffectRuntime.Apply(body, "ef_disc_possession", 1, durationMs: 90000, creator: christof.Thing);
        Assert.Same(christof.Thing, place.Minds!.Selected);

        Assert.True(place.SteerSlot(0));
        Assert.Same(christof.Thing, place.Steered);
    }

    /// <summary>
    /// **Tab ends a possession; F6 does not**. Steering a possessed
    /// body, Tab takes every possessing effect off it instead of moving on: the
    /// controls come back to Christof and the body is its own again — Tab does not also go on to
    /// Wilhem. F6 takes the controls back and leaves the possession running.
    /// </summary>
    [Fact]
    public void Tab_ends_a_possession_and_F6_does_not()
    {
        using var files = Install.Open();
        var (place, _, christof, wilhem) = Monastery(files);
        var body = Named(place, "cappadocian_female_conv1");

        EffectRuntime.Apply(body, "ef_disc_possession", 1, durationMs: 90000, creator: christof.Thing);
        Assert.True(place.SteerSlot(0));                     // F6
        Assert.Same(christof.Thing, place.Steered);
        Assert.NotEqual(0u, body.Mind!.Flags & Mind.FlagPossessed);
        Assert.False(place.EndSteeredPossession());          // nobody possessed is being steered
        EffectRuntime.RemoveKind(body, EffectKind.Possessing, 0);

        EffectRuntime.Apply(body, "ef_disc_possession", 1, durationMs: 90000, creator: christof.Thing);
        Assert.Same(body, place.Steered);
        Assert.Same(christof.Thing, place.SteerTab(() => 1f));
        Assert.Equal(0u, body.Mind.Flags & Mind.FlagPossessed);
        Assert.DoesNotContain(body.Effects.All, e => e.Kind == EffectKind.Possessing && !e.Spent);
        Assert.Equal(AiMode.Party, christof.Mode);
        Assert.Same(wilhem.Thing, place.SteerTab(() => 1f));  // and the next Tab is an ordinary one
    }

    /// <summary>
    /// **A possessed body picks up into its own pack.** The take order has no test of who is taking
    /// (all let a possessed body take), but the straight-to-the-cursor
    /// branch asks for the party's *selected* member — Christof, all the while — so with
    /// the inventory open and alt held the body still does not put it on the cursor.
    /// </summary>
    [Fact]
    public void A_possessed_body_picks_up_into_its_own_pack()
    {
        using var files = Install.Open();
        var (place, _, christof, _) = Monastery(files);
        var body = Named(place, "cappadocian_female_conv1");
        EffectRuntime.Apply(body, "ef_disc_possession", 1, durationMs: 90000, creator: christof.Thing);
        Assert.Same(body, place.Steered);

        var potion = place.Place("VitaeRed", body.Position + new Vector3(20f, 0f, 0f), body.Orientation, body.Sector)!;
        var took = Ground.Take(place, body, potion, InputModifier.Alt, steering: true, nowMs: place.Milliseconds,
                               paneOpen: true, selected: ReferenceEquals(place.Selected, body));
        Assert.NotEqual(GroundResult.ToHand, took);
        Assert.NotEqual(GroundResult.Refused, took);
        Assert.Same(body, potion.Owner);
        Assert.Null(body.Actor!.Held);
    }

    /// <summary>
    /// **The bar shows the body's own belt and disciplines while it is steered** (the take-over refills
    /// the panes from the steered character), and the lit row and the stance radios stay the
    /// caster's (the party's selected member). In 1.1 the possessed
    /// Cappadocian's quick slots hold the gems and the blood potion she has picked up.
    /// </summary>
    [Fact]
    public void The_bar_shows_a_possessed_bodys_own_belt()
    {
        using var files = Install.Open();
        var (place, _, christof, _) = Monastery(files);
        var bar = new Vitomir.Engine.Ui.StatusBar(new Vitomir.Engine.Ui.UiAssets(files).Pane(Vitomir.Engine.Ui.StatusBar.PaneName), place);
        var body = Named(place, "cappadocian_female_conv1");
        EffectRuntime.Apply(body, "ef_disc_possession", 1, durationMs: 90000, creator: christof.Thing);
        var potion = place.Place("VitaeRed", body.Position + new Vector3(20f, 0f, 0f), body.Orientation, body.Sector)!;
        Ground.Take(place, body, potion, InputModifier.None, steering: true, nowMs: place.Milliseconds, selected: false);
        Assert.Equal(Slot.QuickUse, potion.Item!.Location);

        bar.Update();
        Assert.Same(christof.Thing.Actor, bar.Selected);
        Assert.Same(body.Actor, bar.Steered);
        Assert.Contains(bar.QuickUse, c => ReferenceEquals(c.Item, potion.Item));
        Assert.All(bar.Powers, p => Assert.Contains(p.Discipline, body.Actor!.Disciplines.All));

        Assert.True(place.SteerSlot(0));                     // F6: Christof's own belt again
        bar.Update();
        Assert.DoesNotContain(bar.QuickUse, c => ReferenceEquals(c.Item, potion.Item));
    }

    // ---- majesty ------------------------------------------------------------------------------

    /// <summary>
    /// **Majesty**. In 1.1 `cappadocian_female_conv2`, casting when
    /// Christof's Majesty reached her, went from `enemy` to `majesty` with Christof as her target,
    /// finished the cast she had started, and then stood — Wilhem fed on her while she was struck —
    /// until `ef_disc_majesty2` ran out, when she went back to `enemysearch` with no target. Here:
    /// struck, she stands and faces Christof; the effect off, she is herself again.
    /// </summary>
    [Fact]
    public void Majesty_holds_an_enemy_facing_the_caster_until_it_runs_out()
    {
        using var files = Install.Open();
        var (place, minds, christof, _) = Monastery(files);
        var her = Named(place, "cappadocian_female_conv2");
        place.Move(christof.Thing, her.Position + new Vector3(120f, 0f, 0f));

        var effect = EffectRuntime.Apply(her, "ef_disc_majesty2", 4, durationMs: 5000, creator: christof.Thing);
        Assert.NotNull(effect);
        Assert.Equal(AiMode.Majesty, her.Mind!.Mode);
        Assert.Same(christof.Thing, her.Mind.Target);

        var at = her.Position;
        Run(place, minds, 1000);
        Assert.Equal(AiMode.Majesty, her.Mind.Mode);
        Assert.True(Vector3.Distance(at, her.Position) < 1f, $"she moved to {her.Position}");
        Assert.Equal(Rpg.ActionMode.None, her.Actor!.Action);
        // A heading of 0 looks along +y, and a positive one turns toward −x (WorldThing.Face).
        var toward = Vector3.Normalize((christof.Thing.Position - her.Position) with { Z = 0f });
        float yaw = her.Orientation.Z * MathF.PI / 180f;
        Assert.True(Vector3.Dot(toward, new Vector3(-MathF.Sin(yaw), MathF.Cos(yaw), 0f)) > 0.9f,
                    $"she faces {her.Orientation.Z:0}°");

        EffectRuntime.Remove(her, effect!);
        Assert.Equal(AiMode.EnemySearch, her.Mind.Mode);
        Assert.Null(her.Mind.Target);
    }

    /// <summary>Majesty is refused to a skeleton (`NOMAJESTY`) and to a frenzied mind.</summary>
    [Fact]
    public void Majesty_does_not_take_on_a_skeleton_or_the_frenzied()
    {
        using var files = Install.Open();
        var (place, minds, christof, _) = Monastery(files);
        var skeleton = Named(place, "skel_sword");
        EffectRuntime.Apply(skeleton, "ef_disc_majesty2", 4, durationMs: 5000, creator: christof.Thing);
        Assert.Equal(AiMode.EnemySearch, skeleton.Mind!.Mode);

        var her = Named(place, "cappadocian_female_conv2");
        minds.EnterFrenzy(her.Mind!);
        EffectRuntime.Apply(her, "ef_disc_majesty2", 4, durationMs: 5000, creator: christof.Thing);
        Assert.Equal(AiMode.Frenzied, her.Mind!.Mode);
    }

    // ---- pets ---------------------------------------------------------------------------------

    /// <summary>
    /// **Summon Elemental** (`DisciplineSummonElemental`). In 1.1 the
    /// elemental came into the world beside Christof in mode `pet` — its home mode `enemysearch`,
    /// its team Christof's — and stood by him with nothing to fight. Here: it joins Christof's list,
    /// is marked a pet with Christof its master, the natives count it, and it walks after him.
    /// </summary>
    [Fact]
    public void A_summoned_elemental_is_Christofs_pet_and_follows_him()
    {
        using var files = Install.Open();
        var (place, minds, christof, wilhem) = Monastery(files);
        var lead = christof.Thing;
        // On Christof's far side from Wilhem. Earlier, it stood 60 to the east, which is inside
        // Wilhem (36 from him, 49 + 32 of body); a walker inside somebody whose middle is ahead of it
        // cannot step out that way in 1.1 (the step test measures from where it stands), and Christof's
        // way north had Wilhem's middle a fraction ahead. The old test measured from the step's end.
        var away = lead.Position - wilhem.Thing.Position;
        away = Vector3.Normalize(away with { Z = 0f });
        var elemental = place.Place("elemental", lead.Position + away * 60f, lead.Orientation, lead.Sector)!;
        elemental.Actor!.Flags |= Actor.Flag.Summoned;
        Assert.NotNull(EffectRuntime.Apply(elemental, "ef_disc_summonelemental", 1, durationMs: 15000, creator: lead));

        var pet = elemental.Mind!;
        Assert.Equal(AiMode.Pet, pet.Mode);
        Assert.Equal(AiMode.EnemySearch, pet.BaseMode);
        Assert.NotEqual(0u, pet.Flags & Mind.FlagPet);
        Assert.Equal(lead.Guid, pet.MasterGuid);
        Assert.Equal(1, Minds.PetCount(christof));

        // Christof goes off 500 along the first way that has floor: the pet comes after him.
        Vector3[] ways = [new(0f, -500f, 0f), new(500f, 0f, 0f), new(-500f, 0f, 0f), new(0f, 500f, 0f)];
        var (there, node) = ways.Select(w => lead.Position + w).Select(p => (p, place.Walk!.Find(p))).First(c => c.Item2 >= 0);
        place.Move(lead, there with { Z = place.Walk!.Nodes[node].Height(there.X, there.Y) });
        var from = elemental.Position;
        for (int t = 0; t < 6000; t += 33) { minds.Tick(0.033f); minds.Follow(0.033f); }
        Assert.True(Vector3.Distance(elemental.Position, lead.Position) < Vector3.Distance(from, lead.Position) - 200f,
                    $"the elemental stayed at {elemental.Position}");
    }

    /// <summary>
    /// **A summoned pet's time is up**: it leaves its master's list, runs from
    /// him — afraid, forced — and fades out over five seconds, gone when the fade is.
    /// </summary>
    [Fact]
    public void A_summoned_pet_whose_time_is_up_runs_from_its_master_and_fades()
    {
        using var files = Install.Open();
        var (place, _, christof, _) = Monastery(files);
        var lead = christof.Thing;
        var elemental = place.Place("elemental", lead.Position + new Vector3(60f, 0f, 0f), lead.Orientation, lead.Sector)!;
        elemental.Actor!.Flags |= Actor.Flag.Summoned;
        var effect = EffectRuntime.Apply(elemental, "ef_disc_summonelemental", 1, durationMs: 15000, creator: lead)!;

        EffectRuntime.Remove(elemental, effect);

        Assert.Equal(0, Minds.PetCount(christof));
        Assert.Equal(AiMode.Afraid, elemental.Mind!.Mode);
        Assert.Same(lead, elemental.Mind.Target);
        Assert.Equal(0, elemental.Mind.MasterGuid);
        Assert.Equal(place.Milliseconds + Minds.SummonedLinger, elemental.ExpireAt);
        Assert.True(elemental.Model3D.AlphaRamping);
    }
}
