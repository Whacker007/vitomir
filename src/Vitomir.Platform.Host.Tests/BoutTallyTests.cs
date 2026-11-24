// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The bout's tally against the contract of <c>CombatEvents.cs</c>, before contact and harm are joined: a stand-in
/// system publishes, on fixed ticks, the records contact and harm will publish — contacts on the body, a shield and the
/// level, the harm of two of them, a death — and the tally counts them by fighter and by what was met, joins each harm to its
/// contact by id, ends the bout at the death with the killer the winner and the time of the kill, and leaves the harmless
/// contact without harm. a stand-in for the module's powers (the contract of <c>Powers/PowerContract.cs</c>) — a use, a
/// renewal, refusals, an expiry, Blood in the vitals, the tempo — and the tally's Blood, uses, refusals by reason, the time
/// live, the swings while live and unpaid, each contact's live powers, its swing's tempo and why a hit dealt no wound; the
/// columns at the end, the columns where they were. Needs no game files.
/// </summary>
public sealed class BoutTallyTests
{
    [Fact]
    public void The_tally_counts_the_contract_s_records_and_ends_at_the_first_death()
    {
        var b = new WorldBuilder(1);
        b.Event<AttackBegun>().Event<StrikeMoment>().Event<BlockRaised>().Event<AttackRefused>();
        b.Event<ContactMade>().Event<HarmResolved>().Event<ActorDied>();
        EntityId a = EntityId.None, t = EntityId.None;
        b.System(Phase.Contact, 1, "test.stand-in", w =>
        {
            ContactMade Contact(long id, EntityId by, EntityId on, ContactMet met) =>
                new(id, by, 1, on, met, met == ContactMet.Body ? "chest" : null, ContactMaterial.Flesh, w.Tick, new Vector3(1, 2, 1.3f), Vector3.UnitX, 8f, 9.6f, false, met == ContactMet.Shield, true, false);
            switch (w.Tick)
            {
                case 10:
                    w.Events<AttackBegun>().Publish(new AttackBegun(a, w.Tick, 1, AttackStrength.Quick, Animation.ActionRole.AttackQuick, 0, 0f));
                    w.Events<BlockRaised>().Publish(new BlockRaised(t, w.Tick));
                    break;
                case 20:
                    w.Events<StrikeMoment>().Publish(new StrikeMoment(a, w.Tick, 1, Animation.ActionRole.AttackQuick, Vector3.Zero, 30f));
                    w.Events<ContactMade>().Publish(Contact(1, a, t, ContactMet.Body));
                    break;
                case 21:
                    w.Events<HarmResolved>().Publish(new HarmResolved(1, a, t, w.Tick, HarmKind.Lethal, 6, 3, 1, 2, 1, 3, 7, 4));
                    break;
                case 30:
                    w.Events<ContactMade>().Publish(Contact(2, t, a, ContactMet.Shield));
                    w.Events<ContactMade>().Publish(Contact(3, t, EntityId.None, ContactMet.Level));
                    w.Events<AttackRefused>().Publish(new AttackRefused(t, w.Tick, AttackRefusal.MidSwing));
                    break;
                case 40:
                    w.Events<ContactMade>().Publish(Contact(4, a, t, ContactMet.Body));
                    w.Events<HarmResolved>().Publish(new HarmResolved(4, a, t, w.Tick, HarmKind.Lethal, 8, 5, 1, 2, 0, 6, 4, 0));
                    w.Events<ActorDied>().Publish(new ActorDied(t, w.Tick, 4, a));
                    break;
                case 41:
                    w.Events<ContactMade>().Publish(Contact(5, a, t, ContactMet.Body));     // after the death: not collected
                    break;
            }
        });
        var world = b.Build();
        a = world.Create();
        t = world.Create();
        var result = new BoutResult { Point = 0, Values = [7], Bout = 3, Seed = 11, Fighters = [new FighterTally(), new FighterTally()] };
        var rows = new List<ContactRow>();
        var tally = new BoutTally(world, [a, t], result, rows);
        int steps = 0;
        while (!tally.Over && steps < 200) { world.Step(); tally.Collect(); steps++; }

        Assert.Equal(41, steps);
        Assert.Equal(0, result.Winner);
        Assert.Equal(40 / 120.0, result.KillSeconds!.Value, 12);
        var (ta, tt) = (result.Fighters[0], result.Fighters[1]);
        Assert.Equal((1, 1, 2, 0, 0, 0), (ta.Swings, ta.Strikes, ta.ContactsBody, ta.ContactsShield, ta.ContactsWeapon, ta.ContactsLevel));
        Assert.Equal((0, 1, 1), (tt.ContactsBody, tt.ContactsShield, tt.ContactsLevel));
        Assert.Equal((2, 14L, 8L, 9L), (ta.Hits, ta.DamageDice, ta.DamageSuccesses, ta.WoundsDealt));
        Assert.Equal((9L, 0, true, "ZERO_HEALTH_STOP"), (tt.WoundsTaken, tt.HealthLeft, tt.Stopped, tt.Stop));   // stopped, not died
        Assert.Equal((1, 1), (tt.BlocksRaised, tt.Refused));
        Assert.Null(ta.HealthLeft);
        Assert.Equal((false, ""), (ta.Stopped, ta.Stop));
        Assert.Equal(7.0, ta.MeanDice!.Value, 12);
        Assert.Equal(4.0, ta.MeanSuccesses!.Value, 12);
        // The rows: four contacts, the harm beside the two it was resolved for.
        Assert.Equal([1L, 2, 3, 4], rows.Select(r => r.Contact.ContactId));
        Assert.Equal([3, (int?)null, null, 6], rows.Select(r => r.Harm?.Wounds));
        Assert.Equal((0, 1), (rows[0].AttackerIndex, rows[0].TargetIndex));
        Assert.Equal(-1, rows[2].TargetIndex);
        Assert.All(rows, r => Assert.Equal((3, 11UL), (r.Bout, r.Seed)));
    }

    [Fact]
    public void The_tally_counts_each_command_s_uses_Blood_and_amount_and_a_heal_opens_no_live_window()
    {
        // Blood Strength for 0.5 s (renewed once) and two instant heals; powers.csv by command.
        var b = new WorldBuilder(1);
        PowerContract.Register(b);
        b.Event<AttackBegun>();
        EntityId a = EntityId.None, t = EntityId.None;
        b.System(Phase.Act, 1, "test.powers-stand-in", w =>
        {
            switch (w.Tick)
            {
                case 10: w.Events<PowerUsed>().Publish(new PowerUsed(a, "common.blood_strength", 1, 2, w.Tick, 1, 5, 100, 95, 15f, false, w.Tick + 60, 5)); break;
                case 20: w.Events<PowerUsed>().Publish(new PowerUsed(a, "common.blood_healing", 1, 5, w.Tick, 2, 5, 95, 90, 0f, false, w.Tick, 20)); break;
                case 40:
                    w.Events<PowerEnded>().Publish(new PowerEnded(a, "common.blood_strength", w.Tick, PowerEndCause.Renewed));
                    w.Events<PowerUsed>().Publish(new PowerUsed(a, "common.blood_strength", 1, 2, w.Tick, 3, 5, 90, 85, 15f, true, w.Tick + 30, 5));
                    break;
                case 60: w.Events<PowerUsed>().Publish(new PowerUsed(a, "common.blood_healing", 1, 5, w.Tick, 4, 5, 85, 80, 0f, false, w.Tick, 7)); break;
                case 70: w.Events<PowerEnded>().Publish(new PowerEnded(a, "common.blood_strength", w.Tick, PowerEndCause.Expired)); break;
            }
        });
        var world = b.Build();
        a = world.Create();
        t = world.Create();
        var result = new BoutResult { Point = 0, Values = [], Bout = 0, Seed = 1, Fighters = [new FighterTally(), new FighterTally()] };
        var tally = new BoutTally(world, [a, t], result, null);
        for (int i = 0; i < 100; i++) { world.Step(); tally.Collect(); }
        tally.Finish(world.Tick);

        var f = result.Fighters[0];
        Assert.Equal(("blood_healing", "blood_strength"), (FighterTally.PowerOf("common.blood_healing"), FighterTally.PowerOf("common.blood_strength")));
        Assert.Equal("celerity", FighterTally.PowerOf("celerity.active"));         // The earlier build's ids as they were
        Assert.Equal(["blood_healing", "blood_strength"], f.ByPower.Keys);
        var (heal, rise) = (f.ByPower["blood_healing"], f.ByPower["blood_strength"]);
        Assert.Equal((2, 0, 10L, 27L), (heal.Uses, heal.Renewals, heal.Blood, heal.Amount));      // Health healed 20 + 7
        Assert.Equal((2, 1, 10L, 10L), (rise.Uses, rise.Renewals, rise.Blood, rise.Amount));
        Assert.Equal((4, 20L), (f.Uses, f.BloodSpent));
        Assert.Equal(["blood_strength"], f.LiveTicks.Keys);                       // the heals opened no window
        Assert.Equal((60L, 60L), (f.LiveTicks["blood_strength"], f.LiveAnyTicks)); // ticks 10 to 70, the renewal no break

        var path = Path.Combine(Path.GetTempPath(), $"powers-tally-{Environment.ProcessId}.csv");
        try
        {
            var spec = ArenaSpec.Read("""{ "fighters": [ { "name": "A" }, { "name": "B" } ] }""", Path.GetTempPath(), ArenaSpecTests.FakeLevel.Value);
            ArenaPowers.Write(path, spec, [result]);
            var lines = File.ReadAllLines(path);
            Assert.Equal("point,bout,seed,fighter,power,uses,renewals,blood_spent,amount,live_seconds", lines[0]);
            Assert.Equal(["0,0,1,A,blood_healing,2,0,10,27,0", "0,0,1,A,blood_strength,2,1,10,10,0.5"], lines[1..]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_tally_measures_what_a_power_did_from_the_module_s_events_whatever_its_effect()
    {
        var b = new WorldBuilder(1);
        PowerContract.Register(b);
        CombatTempos.Register(b);
        b.Event<AttackBegun>().Event<ContactMade>().Event<HarmResolved>();
        EntityId a = EntityId.None, t = EntityId.None;
        b.System(Phase.Act, 1, "test.powers-stand-in", w =>
        {
            ContactMade Contact(long id, int swing) => new(id, a, swing, t, ContactMet.Body, "chest", ContactMaterial.Flesh, w.Tick, Vector3.Zero, Vector3.UnitX, 8f, 9f, false, false, true, false);
            void Blood(float v) { w.Set(a, new Vitals { Health = 100, HealthMax = 100, Blood = v, BloodMax = 100, HasBlood = true }); }
            switch (w.Tick)
            {
                case 0:
                    Blood(100);
                    w.Set(t, new Vitals { Health = 100, HealthMax = 100 });             // a mortal: no Blood to show
                    break;
                case 5:
                    w.Events<PowerUsed>().Publish(new PowerUsed(a, "celerity.active", 2, 1, w.Tick, 1, 30, 100, 70, 30f, false));
                    Blood(70);
                    w.Set(a, new CombatTempo(3f, 3f, 3f));
                    break;
                case 6:
                    w.Events<PowerRefused>().Publish(new PowerRefused(a, "celerity.active", 1, PowerDenial.Recast, w.Tick));
                    break;
                case 7:
                    w.Events<PowerRefused>().Publish(new PowerRefused(a, null, 2, PowerDenial.EmptySlot, w.Tick));
                    w.Events<PowerRefused>().Publish(new PowerRefused(a, "celerity.active", 1, PowerDenial.Recast, w.Tick));
                    break;
                case 10:
                    w.Events<AttackBegun>().Publish(new AttackBegun(a, w.Tick, 1, AttackStrength.Quick, Animation.ActionRole.AttackQuick, 0, 0f));
                    break;
                case 20:
                    w.Events<ContactMade>().Publish(Contact(1, 1));
                    w.Events<HarmResolved>().Publish(new HarmResolved(1, a, t, w.Tick, HarmKind.Lethal, 4, 2, 0, 5, 3, 0, 100, 100, ResistedBy: ResistedBy.Soak, FlurryBlows: 2));
                    // The two extra blows its swing carried (Celerity's flurry), each its own harm of the same contact.
                    w.Events<HarmResolved>().Publish(new HarmResolved(1, a, t, w.Tick, HarmKind.Lethal, 4, 3, 0, 0, 0, 1, 100, 85, Flurry: 1, FlurryBlows: 2));
                    w.Events<HarmResolved>().Publish(new HarmResolved(1, a, t, w.Tick, HarmKind.Lethal, 5, 4, 0, 0, 0, 2, 85, 55, Flurry: 2, FlurryBlows: 2, Band: HealthBand.None));
                    break;
                case 21:
                    w.Set(t, new Vitals { Health = 55, HealthMax = 100 });              // the module's view the tick after
                    break;
                case 30:     // a renewal: the old record ends as renewed, the new one goes on (paid again)
                    w.Events<PowerEnded>().Publish(new PowerEnded(a, "celerity.active", w.Tick, PowerEndCause.Renewed));
                    w.Events<PowerUsed>().Publish(new PowerUsed(a, "celerity.active", 2, 1, w.Tick, 2, 30, 70, 40, 30f, true));
                    Blood(40);
                    break;
                case 60:
                    w.Events<PowerEnded>().Publish(new PowerEnded(a, "celerity.active", w.Tick, PowerEndCause.Expired));
                    w.Set(a, CombatTempo.Normal);
                    break;
                case 70:
                    w.Events<AttackBegun>().Publish(new AttackBegun(a, w.Tick, 2, AttackStrength.Quick, Animation.ActionRole.AttackQuick, 0, 0f));
                    break;
                case 80:
                    w.Events<ContactMade>().Publish(Contact(2, 2));
                    w.Events<HarmResolved>().Publish(new HarmResolved(2, a, t, w.Tick, HarmKind.Lethal, 3, 0, 0, 5, 1, 0, 55, 55, ResistedBy: ResistedBy.NoDamage));
                    break;
            }
        });
        var world = b.Build();
        a = world.Create();
        t = world.Create();
        var result = new BoutResult { Point = 0, Values = [], Bout = 0, Seed = 1, Fighters = [new FighterTally(), new FighterTally()] };
        var rows = new List<ContactRow>();
        var tally = new BoutTally(world, [a, t], result, rows);
        for (int i = 0; i < 120; i++) { world.Step(); tally.Collect(); }
        tally.Finish(world.Tick);
        result.Ticks = world.Tick;

        var f = result.Fighters[0];
        Assert.Equal((2, 1, 60L), (f.Uses, f.Renewals, f.BloodSpent));
        Assert.Equal((100, 40), (f.BloodStart, f.BloodEnd));
        Assert.Equal(f.BloodStart - f.BloodSpent, f.BloodEnd);                    // Blood conserved (no regeneration)
        Assert.Equal(3, f.PowersRefused);
        Assert.Equal("EmptySlot:1;Recast:2", f.RefusedReasons());
        Assert.Equal(55L, f.LiveTicks["celerity"]);                               // ticks 5 to 60, the renewal no break
        Assert.Equal(ArenaTables.Num(55 / 120.0), f.LiveSeconds());
        Assert.Equal((2, 1, 55L), (f.Swings, f.SwingsLive, f.LiveAnyTicks));
        Assert.Equal(1 / (65 / 120.0), f.SwingsUnpaidPerSecond(result.Ticks)!.Value, 9);
        var g = result.Fighters[1];
        Assert.Null(g.BloodStart);
        Assert.Null(g.BloodEnd);
        Assert.Equal((0, 0), (g.Uses, g.PowersRefused));
        Assert.Equal("0", g.LiveSeconds());
        // Each contact: the attacker's powers live at it and its swing's tempo; why the hit dealt no wound.
        Assert.Equal(["celerity:2", ""], rows.Select(r => r.AttackerLive));
        Assert.Equal([3f, 1f], rows.Select(r => r.SwingTempo!.Value));
        Assert.Equal(["soak", "no_damage"], rows.Select(r => ArenaTables.ResistedName(r.Harm!.Value)));   // the contact's own blow
        // The flurry — its blows apart from the contact's own (hits, dice), their wounds with the rest; Health at start and end.
        Assert.Equal((2, 3L), (rows[0].FlurryBlows, rows[0].FlurryWounds));
        Assert.Equal(45L, rows[0].FlurryHealth);
        Assert.Equal((0, 0L, 0L), (rows[1].FlurryBlows, rows[1].FlurryWounds, rows[1].FlurryHealth));
        Assert.Equal((2, 3L), (f.FlurryBlows, f.FlurryWounds));
        Assert.Equal(1, rows.Count(r => r.AttackerLive.Length > 0 && r.Harm is { Flurry: 0 }));
        Assert.Equal((100, 100, 0L), (f.HealthStart, f.HealthEnd, f.HealthLost));
        Assert.Equal((100, 55, 45L, 55), (g.HealthStart, g.HealthEnd, g.HealthLost, g.HealthLeft));

        // The columns: at the end, in order, with the stand-in's numbers; the earlier ones where they were.
        var spec = ArenaSpec.Read("""{ "fighters": [ { "name": "A" }, { "name": "B", "template": "rudolfGuard" } ] }""", Path.GetTempPath(), ArenaSpecTests.FakeLevel.Value);
        var header = ArenaTables.BoutHeader(spec)[..^((ArenaTables.QualityColumns.Length + ArenaTables.ReboundColumns.Length + ArenaTables.SolverColumns.Length + ArenaTables.DirectionColumns.Length + ArenaTables.FightColumns.Length + ArenaTables.StaggerRunColumns.Length + ArenaTables.PaceColumns.Length + ArenaTables.TempoColumns.Length + ArenaTables.SideColumns.Length + ArenaTables.ShieldReadColumns.Length) * 2 + ArenaTables.GroupColumns.Length)];   // its block at the very end, taken out
        // Each fighter's own stop, just before the flurry block's block at the very end (taken out here, so the checks below are
        // The earlier build's and the flurry block's).
        int fsn = ArenaTables.FlurryStrikeColumns.Length * 2;
        Assert.Equal(["A.stop", "B.stop"], header[^(fsn + 2)..^fsn]);
        header = [.. header[..^(fsn + 2)], .. header[^fsn..]];
        int an = ArenaTables.AngleColumns.Length * 2, arm = (ArenaTables.ArmorColumns.Length + ArenaTables.GuardColumns.Length + ArenaTables.FlurryStrikeColumns.Length) * 2;   // The severance block's block, then the armour block's and the guard block's at the very end; the checks below are on all but the severance block's
        Assert.Equal([.. ArenaTables.AngleColumns.Select(c => "A." + c), .. ArenaTables.AngleColumns.Select(c => "B." + c)], header[^(an + arm)..^arm]);
        header = [.. header[..^(an + arm)], .. header[^arm..]];
        string[] powers = ["blood_start", "blood_end", "blood_spent", "uses", "renewals", "refused", "refused_reasons", "live_seconds", "swings_live", "swings_unpaid_per_s"];
        string[] flurry = ["flurry_blows", "health_start", "health_end"];                // at the end
        string[] sever = ["severances", "severed_regions", "continued", "chain_contacts", "chained_swings", "parts_lost", "structural_loss"];
        string[] dodge = ["dodges_begun", "dodges_refused", "swings_dodged", "dodged_clean"];
        string[] armor = ["armor", "armor_blows", "bare_blows", "armor_dice", "armor_kept", "cuts_covered", "cuts_bare"];   // after the severance block's (taken out above)
        string[] guard = ["parries_tried", "guard_held", "guard_parried", "guard_overpowered", "overpowered_foe", "clashes", "clashes_won", "feints", "dodge_cancels"];
        string[] strikes = ["flurry_tried", "flurry_landed", "flurry_guarded", "flurry_out_of_reach", "flurry_dodged", "flurry_attacker_stopped", "flurry_target_down"];   // at the very end
        int fs = strikes.Length * 2;
        int gd = guard.Length * 2 + fs;
        int ar = armor.Length * 2 + gd;
        int d = dodge.Length * 2 + ar;    // The dodge work's and everything after
        int sv = sever.Length * 2 + d;    // everything after the flurry's block
        Assert.Equal(1 + 9 + ArenaTables.FighterColumns.Length * 2 + 1 + 4 + powers.Length * 2 + flurry.Length * 2 + sv, header.Count);
        Assert.Equal([.. dodge.Select(c => "A." + c), .. dodge.Select(c => "B." + c)], header[^d..^ar]);
        Assert.Equal([.. armor.Select(c => "A." + c), .. armor.Select(c => "B." + c)], header[^ar..^gd]);
        Assert.Equal([.. guard.Select(c => "A." + c), .. guard.Select(c => "B." + c)], header[^gd..^fs]);
        Assert.Equal([.. strikes.Select(c => "A." + c), .. strikes.Select(c => "B." + c)], header[^fs..]);
        Assert.Equal([.. sever.Select(c => "A." + c), .. sever.Select(c => "B." + c)], header[^sv..^d]);
        Assert.Equal([.. flurry.Select(c => "A." + c), .. flurry.Select(c => "B." + c)], header[^(6 + sv)..^sv]);
        Assert.Equal([.. powers.Select(c => "A." + c), .. powers.Select(c => "B." + c)], header[^(powers.Length * 2 + 6 + sv)..^(6 + sv)]);
        Assert.Equal(["stop_state", "A.contested_hits", "A.mean_contest_change", "B.contested_hits", "B.mean_contest_change"], header[^(powers.Length * 2 + 11 + sv)..^(powers.Length * 2 + 6 + sv)]);
        Assert.Equal(("digest", "A.swings", "A.stopped"), (header[9], header[10], header[24]));   // "died" renamed in its place
        var row = ArenaTables.BoutRow(spec, result)[..^((ArenaTables.QualityColumns.Length + ArenaTables.ReboundColumns.Length + ArenaTables.SolverColumns.Length + ArenaTables.DirectionColumns.Length + ArenaTables.FightColumns.Length + ArenaTables.StaggerRunColumns.Length + ArenaTables.PaceColumns.Length + ArenaTables.TempoColumns.Length + ArenaTables.SideColumns.Length + ArenaTables.ShieldReadColumns.Length) * 2 + ArenaTables.GroupColumns.Length)];   // its block at the very end, taken out
        Assert.Equal(["", ""], row[^(fs + 2)..^fs]);                                            // no stop in this bout (the flurry block's block after)
        Assert.Equal(("0", "0"), (row[24], row[24 + ArenaTables.FighterColumns.Length]));        // A.stopped, B.stopped
        row = [.. row[..^(fs + 2)], .. row[^fs..]];
        Assert.Equal(["0", "0", "0", "2", "0", "0", "0", "1", "0", "0", "0", "0", "0", "0", "0", ""], row[^(an + arm)..^arm]);   // the stand-in's two blows carry no angle (unmeasured), cut share 1
        row = [.. row[..^(an + arm)], .. row[^arm..]];
        Assert.Equal(header.Count, row.Count);
        Assert.Equal(["100", "40", "60", "2", "1", "3", "EmptySlot:1;Recast:2", ArenaTables.Num(55 / 120.0), "1", ArenaTables.Num(120 / 65.0)], row[^(26 + sv)..^(16 + sv)]);
        Assert.Equal(["", "", "0", "0", "0", "0", "", "0", "0", ArenaTables.Num(0.0)], row[^(16 + sv)..^(6 + sv)]);
        Assert.Equal(["2", "100", "100", "0", "100", "55"], row[^(6 + sv)..^sv]);
        Assert.Equal(["0", "", "0", "0", "0", "", "0", "0", "", "0", "0", "0", "", "0"], row[^sv..^d]);   // no severance here
        Assert.Equal(["0", "0", "0", "0", "0", "0", "0", "0"], row[^d..^ar]);              // no dodges here
        // Nobody wears any — no blow on armour, every blow taken on a bare part (bare_blows, the third of each seven).
        Assert.Equal(["", "0", result.Fighters[0].BareBlows.ToString(CultureInfo.InvariantCulture), "0", "0", "0", "0",
                      "", "0", result.Fighters[1].BareBlows.ToString(CultureInfo.InvariantCulture), "0", "0", "0", "0"], row[^ar..^gd]);
        Assert.All(row[^gd..^fs], x => Assert.Equal("0", x));                             // no guard here
        // No FlurryStrike from the stand-in (nothing tried); its extra blows' harm counted landed.
        string Landed(int i) => result.Fighters[i].FlurryLanded.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(["0", Landed(0), "0", "0", "0", "0", "0", "0", Landed(1), "0", "0", "0", "0", "0"], row[^fs..]);
        Assert.Equal(result.Fighters[0].FlurryBlows, result.Fighters[0].FlurryLanded);
        var contacts = ArenaTables.ContactHeader(spec);
        Assert.Equal(["shot", "strike", "flurry_strike_ids", "fixed"], contacts[^4..]);   // at the very end, taken out here
        contacts = [.. contacts[..^4]];
        Assert.Equal("swing_direction", contacts[^1]);   // at the very end, taken out here
        contacts = [.. contacts[..^1]];
        Assert.Equal(["q", "q_ref", "quality", "quality_factor", "potential_before", "potential_after", "hilt", "cut_share_from", "distance", "attacker_reach"], contacts[^10..]);   // at the very end, taken out here
        contacts = [.. contacts[..^10]];
        Assert.Equal(["swing_kind", "swing_clip", "swing_aim"], contacts[^3..]);   // at the very end, taken out here
        contacts = [.. contacts[..^3]];
        Assert.Equal(["flurry_tried", "flurry_strikes"], contacts[^2..]);   // before the rules block's three: taken out for the checks below
        contacts = [.. contacts[..^2]];
        Assert.Equal(["sine", "angle_band", "cut_share"], contacts[^8..^5]);   // before the armour block's three and the guard block's two at the very end
        contacts = [.. contacts[..^8], .. contacts[^5..]];
        Assert.Equal(["contest_change", "attacker_live_powers", "attacker_swing_tempo", "resisted_by", "flurry_blows", "flurry_wounds", "flurry_health",
            "point_speed", "along_blade", "tip_speed", "peak_speed"], contacts[^20..^9]);
        Assert.Equal(["chain", "severed", "budget_left", "structural_loss"], contacts[^9..^5]);
        Assert.Equal(["armor", "armor_dice", "armor_kept"], contacts[^5..^2]);
        Assert.Equal(["guard", "guard_verdict"], contacts[^2..]);
        Assert.Equal(39 + 3 + 3 + 4 + 4 + 3 + 2, contacts.Count);  // The first 39 with nothing swept, then three, three, four and four, three and two at the end (the severance block's three taken out above)
        string file = Path.Combine(Path.GetTempPath(), $"vitomir-tally-contacts-{Environment.ProcessId}.csv");
        ArenaTables.WriteContacts(file, spec, rows);
        var read = ArenaTables.Read(file);
        Assert.Equal(["", "16", "", "0"], read[1][^4..]);   // no shot, contact 1's own strike (1 × 16), no extra blows, swept
        Assert.Equal(["celerity:2", "3", "soak", "2", "3", "45"], read[1][..^28][^14..^8]);   // its one after // an earlier build: the flurry block's two and the rules block's three at the very end; the quality block's eight after
        Assert.Equal(["", "1", "no_damage", "0", "0", "0"], read[2][..^28][^14..^8]);
    }

    /// <summary>
    /// A stand-in for the rules' severance and the platform's follow-through — a swing that cuts the guard's
    /// right forearm off and goes on into his chest (its chain's second contact), cutting through the trunk and stopping him
    /// (<see cref="ActorStop.SeveredStop"/>) — and the tally's severances by region, the blows that went on, the chain's
    /// contacts and swings, the parts lost and the Health the effective maximum lost; the columns at the end.
    /// </summary>
    [Fact]
    public void The_tally_counts_the_severances_the_chain_and_the_structural_loss_and_a_severed_stop_ends_the_bout()
    {
        var b = new WorldBuilder(1);
        b.Event<ContactMade>().Event<HarmResolved>().Event<ActorDied>();
        EntityId a = EntityId.None, t = EntityId.None;
        b.System(Phase.Contact, 1, "test.stand-in", w =>
        {
            ContactMade Contact(long id, string site, int chain) =>
                new(id, a, 1, t, ContactMet.Body, site, ContactMaterial.Flesh, w.Tick, Vector3.Zero, Vector3.UnitX, 8f, 9.6f, false, false, true, false, Chain: chain);
            switch (w.Tick)
            {
                case 10:
                    w.Events<ContactMade>().Publish(Contact(1, "right forearm", 0));
                    w.Events<HarmResolved>().Publish(new HarmResolved(1, a, t, w.Tick, HarmKind.Lethal, 52, 20, 0, 0, 0, 20, 1000, 700,
                        Severed: BodyRegion.RightForearm, Continues: true, BudgetLeft: 0.85f, StructuralLoss: 150));
                    break;
                case 12:
                    w.Events<ContactMade>().Publish(Contact(2, "chest", 1));
                    w.Events<HarmResolved>().Publish(new HarmResolved(2, a, t, w.Tick, HarmKind.Lethal, 44, 16, 0, 0, 0, 16, 700, 460,
                        Severed: BodyRegion.Chest, Continues: true, BudgetLeft: 0.1f, StructuralLoss: 0));
                    w.Events<ActorDied>().Publish(new ActorDied(t, w.Tick, 2, a, HarmKind.Lethal, ActorStop.SeveredStop));
                    break;
            }
        });
        var world = b.Build();
        a = world.Create();
        t = world.Create();
        var result = new BoutResult { Point = 0, Values = [], Bout = 0, Seed = 1, Fighters = [new FighterTally(), new FighterTally()] };
        var rows = new List<ContactRow>();
        var tally = new BoutTally(world, [a, t], result, rows);
        while (!tally.Over && world.Tick < 100) { world.Step(); tally.Collect(); }
        Assert.Equal((ActorStop.SeveredStop, 0), (result.Stop!.Value, result.Winner));
        var (f, g) = (result.Fighters[0], result.Fighters[1]);
        Assert.Equal((2, 2, 1, 1), (f.Severances, f.Continued, f.ChainContacts, f.ChainedSwings));
        Assert.Equal("chest:1;right forearm:1", FighterTally.Regions(f.SeveredByRegion));
        Assert.Equal(("chest:1;right forearm:1", 150L), (FighterTally.Regions(g.LostByRegion), g.StructuralLost));
        var spec = ArenaSpec.Read("""{ "fighters": [ { "name": "A" }, { "name": "B", "template": "rudolfGuard" } ] }""", Path.GetTempPath(), ArenaSpecTests.FakeLevel.Value);
        var whole = ArenaTables.BoutRow(spec, result)[..^((ArenaTables.QualityColumns.Length + ArenaTables.ReboundColumns.Length + ArenaTables.SolverColumns.Length + ArenaTables.DirectionColumns.Length + ArenaTables.FightColumns.Length + ArenaTables.StaggerRunColumns.Length + ArenaTables.PaceColumns.Length + ArenaTables.TempoColumns.Length + ArenaTables.SideColumns.Length + ArenaTables.ShieldReadColumns.Length) * 2 + ArenaTables.GroupColumns.Length)];   // its block at the very end, taken out
        int fsn = ArenaTables.FlurryStrikeColumns.Length * 2;
        Assert.Equal(["", "SEVERED_STOP"], whole[^(fsn + 2)..^fsn]);                         // B's own stop, a severed stop, before the flurry block's block
        var row = whole[..^(fsn + 2 + (ArenaTables.AngleColumns.Length + ArenaTables.ArmorColumns.Length + ArenaTables.GuardColumns.Length) * 2)];   // The severance block's, the armour block's and the guard block's blocks; the rules block's and the flurry block's at the very end
        Assert.Equal(["2", "chest:1;right forearm:1", "2", "1", "1", "", "0", "0", "", "0", "0", "0", "chest:1;right forearm:1", "150"], row[^(14 + ArenaTables.DodgeColumns.Length * 2)..^(ArenaTables.DodgeColumns.Length * 2)]);   // The dodge work's columns after
        Assert.Equal("SEVERED_STOP", row[1 + 9 + ArenaTables.FighterColumns.Length * 2]);
        string file = Path.Combine(Path.GetTempPath(), $"vitomir-sever-contacts-{Environment.ProcessId}.csv");
        ArenaTables.WriteContacts(file, spec, rows);
        var read = ArenaTables.Read(file);
        Assert.Equal(["0", "right forearm", "0.85", "150"], read[1][..^28][^4..]);   // its one after // an earlier build: the flurry block's two and the rules block's three at the very end; the quality block's eight after
        Assert.Equal(["1", "chest", "0.1", "0"], read[2][..^28][^4..]);
        Assert.Equal(["", "0", "0"], read[1][^25..^22]);   // its one after // the quality block: its eight after // an earlier build: no armour met (the flurry block's two and the rules block's three after)
        var summary = ArenaSummary.Compute(spec, [result]);
        Assert.Equal((2.0, 1.0, 150.0, 1), (summary[0].SeverancesPerBout[0], summary[0].ChainedSwingsPerBout[0], summary[0].StructuralLostPerBout[1], summary[0].SeveredStops));
        Assert.Equal("chest 1.00, right forearm 1.00", ArenaSummary.SeveredText(summary[0]));
        string page = ArenaSummary.Page(spec, summary, new ArenaSummary.Head("test", "", "", 1, ""));
        Assert.Contains("<h2>Severance</h2>", page);
        Assert.Contains("severances per bout", page);
    }
}
