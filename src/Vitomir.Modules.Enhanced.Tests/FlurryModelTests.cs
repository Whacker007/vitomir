// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// What a Celerity swing's extra blows are (each extra blow its own strike that must
/// still find its target at its own moment; the rules package's three models side by side). The rules' side:
/// by default the own blow is resolved at the contact and the extra blows wait for the platform's check at their moments (a test
/// stands in for the platform: it publishes the <see cref="FlurryStrike"/>s); one that met the body is its own staged harm naming
/// its logical strike, one met at a guard is judged by the earlier build's contest, any other resolves nothing and its line says why;
/// <c>flurry-compressed</c> is the earlier build's (its blows named as logical strikes); <c>flurry-off</c> carries none; the two switches
/// together are refused; the data's numbers reach the platform's settings.
/// </summary>
[Collection(AllocationCollection.Name)]   // it measures allocations (Allocations.cs)
public sealed class FlurryModelTests
{
    sealed class Rig
    {
        public required World W;
        public required ActorSheets Sheets;
        public required EntityId Attacker, Target;
        public required List<ContactMade> Contacts;
        public required List<FlurryStrike> Strikes;
        public required StringWriter Trace;
        public readonly List<HarmResolved> Harm = [];
        public readonly List<FlurryStrike> Seen = [];
        long next = 1;

        public void Run(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                W.Step();
                Harm.AddRange(W.Events<HarmResolved>().ThisTick);
                Seen.AddRange(W.Events<FlurryStrike>().ThisTick);
            }
        }

        /// <summary>A contact of the attacker's on the target's chest next tick (its own swing), then that tick run; answers it.</summary>
        public ContactMade Hit()
        {
            long id = next++;
            var c = new ContactMade(id, Attacker, (int)id, Target, ContactMet.Body, "chest", ContactMaterial.Cloth, W.Tick,
                new Vector3(1, 2, 1.3f), new Vector3(-1, 0, 0), 9.5f, 13f, false, false, true, false);
            Contacts.Add(c);
            Run();
            return c with { Tick = W.Tick - 1 };
        }

        /// <summary>The platform's stand-in: extra blow <paramref name="k"/> of <paramref name="c"/>'s swing checked next tick as <paramref name="check"/>.</summary>
        public void Strike(in ContactMade c, int k, int count, FlurryCheck check, ContactMet met = ContactMet.Body, GuardMeet guard = GuardMeet.None, string site = "chest")
        {
            var region = FlurryBlows.RegionOf(site);
            var ev = c with { Tick = W.Tick, Met = met, Site = met == ContactMet.Body ? site : null, HitCoverOrShield = met == ContactMet.Shield, Guard = guard };
            Strikes.Add(new FlurryStrike(FlurryBlows.Id(c.ContactId, k), c.ContactId, c.Attacker, c.Swing, c.Target, k, count, W.Tick, check, met, guard,
                check is FlurryCheck.Met or FlurryCheck.Guarded ? region : BodyRegion.None, ev.Point, 0.05f, ev, AngleBand.Normal, 1f, false, 0f));
            Run();
        }

        public List<JsonElement> Lines() => [.. Trace.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement)
            .Where(l => l.GetProperty("schema").GetString() == HarmTrace.Schema)];
    }

    /// <summary>The player (the combat rules' newly Embraced or the raw-1000 stress sheet) with Celerity 3 live (K = 2: one extra blow a swing) and a broadsword against a townsman of 1000 Health (or the trained guard).</summary>
    static Rig Make(int rank = 3, bool strong = false, string target = "", params string[] switches)
    {
        var m = TestData.Sandbox(null, switches);
        var b = new WorldBuilder(11);
        var trace = new StringWriter();
        b.Service(new CombatTrace(trace));
        DeathServices.Register(b);
        b.Event<ContactMade>();
        var contacts = new List<ContactMade>();
        var strikes = new List<FlurryStrike>();
        b.System(Phase.Contact, 0, "test.contacts", new DelegateSystem(w =>
        {
            foreach (var c in contacts) if (c.Tick == w.Tick) w.Events<ContactMade>().Publish(c);
        }));
        m.Register(b);
        // The platform's stand-in, after the module registered the event (with the harm): strikes at their ticks, in Contact.
        b.System(Phase.Contact, 80, "test.strikes", new DelegateSystem(w =>
        {
            foreach (var s in strikes) if (s.Tick == w.Tick) w.Events<FlurryStrike>().Publish(s);
        }));
        var world = b.Build();
        world.Service<HarmWeapons>().Of = (_, _) => "BroadSword";
        var people = world.Service<PeopleHooks>();
        var (a, t) = (world.Create(), world.Create());
        people.Made(a, PersonRole.Player, "Christof");
        people.Made(t, PersonRole.Townsperson, "Civ12male");
        var route = world.Service<IPersonSheets>();
        string error;
        Assert.True(route.TryTemplate(a, strong ? "sandbox.raw1000_stress" : "sandbox.newly_embraced", out error), error);
        if (target.Length > 0) Assert.True(route.TryTemplate(t, target, out error), error);
        Assert.True(route.TrySet(t, "health_pool", 1000, out error), error);
        Assert.True(route.TryLearn(a, "celerity", rank, ["celerity.active"], out error), error);
        world.Set(a, new PowerIntent { Slot = 1 });
        var r = new Rig { W = world, Sheets = world.Service<ActorSheets>(), Attacker = a, Target = t, Contacts = contacts, Strikes = strikes, Trace = trace };
        r.Run();
        return r;
    }

    [Fact]
    public void By_default_the_own_blow_lands_at_the_contact_and_an_extra_blow_only_when_its_check_met_the_body()
    {
        var r = Make();
        Assert.Equal(FlurryModel.Geometric, r.W.Service<FlurrySettings>().Model);
        var c = r.Hit();
        var own = Assert.Single(r.Harm);                                                // the own blow alone, at the contact
        Assert.Equal((0, 1, FlurryBlows.Id(c.ContactId, 0)), (own.Flurry, own.FlurryBlows, own.Strike));
        Assert.Equal(1, r.W.Service<HarmLedger>().Resolved);
        // Its extra blow, checked at its moment (five ticks on) and met: its own staged harm, its own logical strike, that moment's tick.
        r.Run(4);
        r.Strike(c, 1, 1, FlurryCheck.Met);
        Assert.Equal(2, r.Harm.Count);
        var extra = r.Harm[1];
        Assert.Equal((c.ContactId, 1, 1, FlurryBlows.Id(c.ContactId, 1), r.W.Tick - 1), (extra.ContactId, extra.Flurry, extra.FlurryBlows, extra.Strike, extra.Tick));
        Assert.NotEqual(extra.ContactId, extra.Strike);                                 // the logical strike apart from the physical contact
        Assert.Equal(own.HealthAfter, extra.HealthBefore);
        Assert.Equal(extra.HealthAfter, r.Sheets.Resource(r.Target, "health")!.Value.Current);
        Assert.Equal(1, r.W.Service<HarmLedger>().FlurryBlows);
        var lines = r.Lines();
        Assert.Equal(("contact:1/flurry:1", "1/1", "met"), (lines[1].GetProperty("request_id").GetString(), lines[1].GetProperty("strike").GetString(),
            lines[1].GetProperty("flurry_check").GetString()));
        Assert.Equal("1/0", lines[0].GetProperty("strike").GetString());
    }

    [Theory]
    [InlineData(FlurryCheck.OutOfReach, "Missed", "out of the blade's reach")]
    [InlineData(FlurryCheck.Dodged, "Missed", "dodge")]
    [InlineData(FlurryCheck.AttackerStopped, "Denied", "no longer swings")]
    [InlineData(FlurryCheck.TargetDown, "Denied", "down")]
    public void An_extra_blow_whose_check_failed_resolves_nothing_and_its_line_says_why(FlurryCheck check, string outcome, string why)
    {
        var r = Make();
        var c = r.Hit();
        long health = r.Sheets.Resource(r.Target, "health")!.Value.Current;
        r.Strike(c, 1, 1, check);
        Assert.Single(r.Harm);
        Assert.Equal(health, r.Sheets.Resource(r.Target, "health")!.Value.Current);
        Assert.Equal(0, r.W.Service<HarmLedger>().FlurryBlows);
        var line = r.Lines()[^1];
        Assert.Equal((outcome, 1, FlurryBlows.Name(check)), (line.GetProperty("outcome").GetString(), line.GetProperty("flurry").GetInt32(), line.GetProperty("flurry_check").GetString()));
        Assert.Contains(why, line.GetProperty("reason").GetString());
    }

    [Fact]
    public void An_extra_blow_met_at_a_guard_is_judged_by_the_guard_s_contest_held_by_a_strong_guard_and_overpowered_by_a_strong_blow()
    {
        // The newly Embraced (Strength 15) on the trained guard's raised shield: held (no harm); the raw-1000 stress sheet on it: overpowered,
        // the blow going on to the part behind with what the guard left of its budget.
        var weak = Make(target: "sandbox.guard_trained");
        var c = weak.Hit();
        weak.Strike(c, 1, 1, FlurryCheck.Guarded, ContactMet.Shield, GuardMeet.Shield);
        Assert.Single(weak.Harm);
        var held = weak.Lines()[^1];
        Assert.Equal(("Blocked", "guarded", "Held"), (held.GetProperty("outcome").GetString(), held.GetProperty("flurry_check").GetString(), held.GetProperty("flurry_guard_verdict").GetString()));

        var strong = Make(strong: true, target: "sandbox.guard_trained");
        var d = strong.Hit();
        strong.Strike(d, 1, 1, FlurryCheck.Guarded, ContactMet.Shield, GuardMeet.Shield);
        Assert.Equal(2, strong.Harm.Count);
        var through = strong.Lines()[^1];
        Assert.Equal(("guarded", "Overpowered"), (through.GetProperty("flurry_check").GetString(), through.GetProperty("flurry_guard_verdict").GetString()));
        Assert.Equal(FlurryBlows.Id(d.ContactId, 1), strong.Harm[1].Strike);
        Assert.Equal("chest", through.GetProperty("evidence").GetProperty("site").GetString());
        // With the guard off (the earlier build's guard), a guard met holds: no contest, no harm.
        var off = Make(strong: true, target: "sandbox.guard_trained", switches: EnhancedModule.GuardOffSwitch);
        var e = off.Hit();
        off.Strike(e, 1, 1, FlurryCheck.Guarded, ContactMet.Shield, GuardMeet.Shield);
        Assert.Single(off.Harm);
        Assert.False(off.Lines()[^1].TryGetProperty("flurry_guard_verdict", out _));
    }

    [Fact]
    public void Compressed_resolves_at_the_contact_its_blows_named_as_logical_strikes_and_off_carries_none()
    {
        var r = Make(switches: EnhancedModule.FlurryCompressedSwitch);
        Assert.Equal(FlurryModel.Compressed, r.W.Service<FlurrySettings>().Model);
        var c = r.Hit();
        Assert.Equal(2, r.Harm.Count);                                                   // both at once, at the contact
        Assert.Equal([FlurryBlows.Id(c.ContactId, 0), FlurryBlows.Id(c.ContactId, 1)], r.Harm.Select(h => h.Strike));
        Assert.All(r.Harm, h => Assert.Equal(c.Tick, h.Tick));
        var s = Assert.Single(r.Seen);                                                   // each extra blow resolved, told as a strike
        Assert.Equal((FlurryCheck.Compressed, 1, 1, c.ContactId, FlurryBlows.Id(c.ContactId, 1), c.Tick), (s.Check, s.Index, s.Count, s.ContactId, s.Strike, s.Tick));

        var off = Make(switches: EnhancedModule.FlurryOffSwitch);
        Assert.Equal(FlurryModel.Off, off.W.Service<FlurrySettings>().Model);
        for (int i = 0; i < 4; i++) off.Hit();
        Assert.Equal(4, off.Harm.Count);
        Assert.All(off.Harm, h => Assert.Equal((0, 0), (h.Flurry, h.FlurryBlows)));
        Assert.Empty(off.Seen);
        Assert.False(off.W.Has<FlurryCarry>(off.Attacker));                              // pure tempo: no carry moved

        // The same contacts give the same own blows under the three models (the draws of the own blow come first).
        var geo = Make();
        var g = geo.Hit();
        Assert.Equal(r.Harm[0] with { }, geo.Harm[0]);
        Assert.Equal(off.Harm[0] with { FlurryBlows = 1 }, geo.Harm[0]);
        _ = g;
    }

    [Fact]
    public void The_two_switches_together_are_refused_and_the_data_s_numbers_reach_the_platform()
    {
        var both = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(null, EnhancedModule.FlurryCompressedSwitch, EnhancedModule.FlurryOffSwitch));
        Assert.Contains(both.Reasons, x => x.Contains("flurry-compressed") && x.Contains("flurry-off"));
        var m = TestData.Sandbox();
        Assert.Equal((FlurrySpacing.Fixed, 0.04f, 0.5f, 0.15f), (m.Flurry.Spacing, m.Flurry.SpacingSeconds, m.Flurry.ApproachMetres, m.Flurry.ReachMetres));
        Assert.EndsWith("rules/flurry", m.Flurry.File);
        var r = Make();
        var s = r.W.Service<FlurrySettings>();
        Assert.Equal((FlurrySpacing.Fixed, 0.04f, 0.5f, 0.15f), (s.Spacing, s.SpacingSeconds, s.ApproachMetres, s.ReachMetres));
        // The re-strike motion off in the data (0.07 s of clip, three times the swing's rate at most); its switch turns it
        // on, a spacing switch sets the fixed spacing's seconds (its name's milliseconds); two spacings together are refused.
        Assert.Equal((false, 0.07f, 3f), (s.Motion, s.PullBackClipSeconds, s.RestrikeRateMost));
        var moving = TestData.Sandbox(null, EnhancedModule.FlurryMotionSwitch, "flurry-spacing-120ms");
        Assert.Equal((true, FlurrySpacing.Fixed, 0.12f, 0.07f), (moving.Flurry.Motion, moving.Flurry.Spacing, moving.Flurry.SpacingSeconds, moving.Flurry.PullBackClipSeconds));
        Assert.Equal((false, 0.08f), (TestData.Sandbox(null, "flurry-spacing-80ms").Flurry.Motion, TestData.Sandbox(null, "flurry-spacing-80ms").Flurry.SpacingSeconds));
        Assert.Equal(0.16f, TestData.Sandbox(null, "flurry-spacing-160ms").Flurry.SpacingSeconds);
        var two = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(null, "flurry-spacing-80ms", EnhancedModule.FlurrySwingSpacingSwitch));
        Assert.Contains(two.Reasons, x => x.Contains("spacing"));
        using var copy = new TestData.Copy();
        copy.Edit("rules/flurry.json", t => t.Replace("\"kind\": \"fixed\"", "\"kind\": \"often\""));
        var bad = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
        Assert.Contains(bad.Reasons, x => x.Contains("rules/flurry") && x.Contains("often"));
    }

    [Fact]
    public void Resolving_extra_blows_at_their_moments_allocates_nothing_per_tick()
    {
        // No trace (the trace's lines are its only allocation); fists, so the townsman stands the many blows.
        var m = TestData.Sandbox();
        var b = new WorldBuilder(9);
        b.Service(new CombatTrace(null));
        DeathServices.Register(b);
        b.Event<ContactMade>();
        EntityId attacker = EntityId.None, target = EntityId.None;
        long id = 1;
        b.System(Phase.Contact, 0, "test.contacts", new DelegateSystem(x =>
        {
            if (x.Tick % 8 == 0 && attacker != EntityId.None)
                x.Events<ContactMade>().Publish(new ContactMade(id++, attacker, (int)id, target, ContactMet.Body, "chest", ContactMaterial.Cloth, x.Tick,
                    Vector3.Zero, Vector3.UnitX, 9f, 12f, false, false, true, false));
        }));
        m.Register(b);
        b.System(Phase.Contact, 80, "test.strikes", new DelegateSystem(x =>
        {
            // The platform's stand-in: four ticks after each contact, its extra blow checked — met, a guard, out of reach, in turn.
            if (x.Tick % 8 != 4 || attacker == EntityId.None || id < 2) return;
            long c = id - 1;
            var check = (c % 3) switch { 0 => FlurryCheck.Met, 1 => FlurryCheck.Guarded, _ => FlurryCheck.OutOfReach };
            var met = check == FlurryCheck.Guarded ? ContactMet.Shield : ContactMet.Body;
            var ev = new ContactMade(c, attacker, (int)c, target, met, met == ContactMet.Body ? "chest" : null, ContactMaterial.Cloth, x.Tick,
                Vector3.Zero, Vector3.UnitX, 9f, 12f, false, met == ContactMet.Shield, true, false, Guard: met == ContactMet.Shield ? GuardMeet.Shield : GuardMeet.None);
            x.Events<FlurryStrike>().Publish(new FlurryStrike(FlurryBlows.Id(c, 1), c, attacker, (int)c, target, 1, 1, x.Tick, check, met, ev.Guard,
                BodyRegion.Chest, Vector3.Zero, 0f, ev));
        }));
        var world = b.Build();
        world.Service<HarmWeapons>().Of = (_, _) => "Punch";
        var people = world.Service<PeopleHooks>();
        (attacker, target) = (world.Create(), world.Create());
        people.Made(attacker, PersonRole.Player, "Christof");
        people.Made(target, PersonRole.Townsperson, "Civ12male");
        var route = world.Service<IPersonSheets>();
        Assert.True(route.TrySet(target, "health_pool", 1000, out string error), error);
        Assert.True(route.TryTemplate(attacker, "sandbox.newly_embraced", out error), error);
        Assert.True(route.TryLearn(attacker, "celerity", 5, ["celerity.active"], out error), error);
        world.Set(attacker, new PowerIntent { Slot = 1 });
        for (int i = 0; i < 200; i++) world.Step();
        Allocations.None("400 ticks of extra blows resolved at their moments", () => { for (int i = 0; i < 400; i++) world.Step(); });
        Assert.True(world.Service<HarmLedger>().FlurryBlows > 20);
        Assert.True(world.Get<Vitals>(target).Health > 0);
    }
}
