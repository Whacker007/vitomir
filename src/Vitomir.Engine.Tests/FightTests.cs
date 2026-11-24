// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The AI actually fighting: the AI's attack choice decides, the attack's approach closes in,
/// and the blow lands on the animation's own keyframe tag. All of it runs on the simulation clock,
/// so a fight can be watched with nothing drawing it.
/// </summary>
public class FightTests
{
    static (Location Place, Minds Minds, Simulation Sim) Monastery(GameFiles files)
    {
        var place = Location.Load(files, "P1_MON2", things: true, sky: false,
                                  scene: ChronicleSet.Load(files).ForLevel("P1_MON2")?.Entry?.SceneName,
                                  player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        return (place, minds, new Simulation(place));
    }

    [Fact]
    public void Something_finds_Christof_and_swings_at_him()
    {
        using var files = Install.Open();
        var (place, _, sim) = Monastery(files);
        var christof = place.Player!.Thing;
        float health = christof.Actor!.Health;

        sim.Advance(60f, 1f / 30f);

        Assert.NotEmpty(sim.Blows);
        // Everything that swung did it at him, and it closed the distance to do it: the Monastery
        // puts nobody within reach at the start.
        Assert.All(sim.Blows, b => Assert.Same(christof, b.Target));
        Assert.Contains(sim.Blows, b => b.Blow.Hit);
        Assert.True(christof.Actor.Health < health,
                    $"he took nothing in a minute of being attacked ({sim.Blows.Count} blows)");

        // A swing is paced by the AI file's attackRate plus the animation, not by the frame: the
        // gaps between one character's blows are all of a size.
        var rat = sim.Blows[0].Attacker;
        var mine = sim.Blows.Where(b => ReferenceEquals(b.Attacker, rat)).Select(b => b.AtMs).ToArray();
        Assert.True(mine.Length >= 4, $"only {mine.Length} blows from the first attacker");
        var gaps = mine.Zip(mine.Skip(1), (a, b) => b - a).ToArray();
        Assert.All(gaps, g => Assert.InRange(g, 200, 5000));
    }

    [Fact]
    public void A_fight_ends_in_a_death_and_the_survivor_goes_back_to_looking()
    {
        using var files = Install.Open();
        var (_, minds, sim) = Monastery(files);

        sim.Advance(240f, 1f / 30f);

        Assert.Contains(sim.Blows, b => b.Harm.Killed);
        // Nobody is left holding a corpse: the engine drops a dead target on the next think, and so
        // does this (Minds.Enemy's first test).
        Assert.DoesNotContain(minds.All, m => m.Target is { Actor.Health: <= 0f });
        Assert.DoesNotContain(minds.All, m => m.Mode == AiMode.Enemy && m.Target is null);
    }

    [Fact]
    public void An_enemy_runs_at_you_rather_than_walking()
    {
        using var files = Install.Open();
        var (_, minds, sim) = Monastery(files);

        // The fastest each character moves while it has somebody to fight.
        var fastest = new Dictionary<int, float>();
        for (int i = 0; i < 30 * 60; i++)
        {
            sim.Step(1f / 30f);
            foreach (var m in minds.All)
                if (m.Target is not null && m.Walker.Speed > fastest.GetValueOrDefault(m.Thing.Guid))
                    fastest[m.Thing.Guid] = m.Walker.Speed;
        }

        Assert.NotEmpty(fastest);
        // Every one of them broke into a run: a walk would cap it at the template's walk speed.
        foreach (var (guid, speed) in fastest)
        {
            var thing = minds.All.First(m => m.Thing.Guid == guid).Thing;
            var actor = thing.Actor!;
            Assert.True(speed > actor.WalkSpeed,
                $"{thing.Template} closed at {speed:0}, no faster than its walk speed {actor.WalkSpeed:0}");
            Assert.Equal(actor.RunSpeed, speed, 1);
        }
    }

    [Fact]
    public void A_skeleton_chases_at_a_run_while_playing_its_walk_cycle()
    {
        using var files = Install.Open();
        var (place, minds, sim) = Monastery(files);

        // Bring the skeletons to him: from the scene's own start only the rats reach him, and his
        // spawn is certainly open ground.
        var skeletons = minds.All
            .Where(m => m.Thing.Template.StartsWith("skel", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.NotEmpty(skeletons);
        for (int i = 0; i < skeletons.Length; i++)
        {
            skeletons[i].Thing.Position = place.Player!.Thing.Position + new Vector3(200f, 60f * i, 0f);
            skeletons[i].Walker.Ground();
        }

        var played = new Dictionary<int, HashSet<string>>();
        var fastest = new Dictionary<int, float>();
        for (int i = 0; i < 30 * 60; i++)
        {
            sim.Step(1f / 30f);
            foreach (var m in skeletons)
            {
                if (m.Target is null) continue;
                if (m.Walker.Speed > fastest.GetValueOrDefault(m.Thing.Guid)) fastest[m.Thing.Guid] = m.Walker.Speed;
                // The **motion** channel, not whatever is current. A skeleton carries a bow, so at
                // this range it is already shooting while it circles, and the action channel wins
                // `Current` — which says nothing about the cycle its legs were given. The question
                // here is only ever about the legs.
                if (m.Walker.Speed > 0f && m.Thing.Animation?.Motion?.Mode?.Name is { } name)
                    (played.TryGetValue(m.Thing.Guid, out var set) ? set : played[m.Thing.Guid] = []).Add(name);
            }
        }

        Assert.NotEmpty(fastest);
        foreach (var (guid, speed) in fastest)
        {
            var thing = skeletons.First(m => m.Thing.Guid == guid).Thing;
            // It really is running: 150, its full run speed, not its walk speed of 100.
            Assert.Equal(thing.Actor!.RunSpeed, speed, 1);
            Assert.True(speed > thing.Actor.WalkSpeed);
            // And it plays its **walk** cycle the whole time, because which cycle the legs get is
            // decided against an absolute 200 units a second and 150 never reaches it. That is the
            // original's own rule, and it is why a skeleton chasing you
            // does not look like it is running. Its bow's own walk, `walk_onehand`: in a
            // fight a character walks in its weapon's stance.
            Assert.True(thing.Actor.RunSpeed <= Player.RunAnimationBeyond);
            Assert.Contains(played[guid], n => n.StartsWith("walk", StringComparison.Ordinal));
            Assert.DoesNotContain(played[guid], n => n.StartsWith("run", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Every_shipped_AI_file_closes_at_a_run()
    {
        using var files = Install.Open();
        var classes = new AiClasses(files);
        int seen = 0;
        foreach (var name in files.List(AiClasses.Folder))
        {
            if (!name.EndsWith(".nai", StringComparison.OrdinalIgnoreCase)) continue;
            if (classes.Get(Path.GetFileName(name)) is not { } ai) continue;
            seen++;
            // The move order's flags come out of this bit, and 0x10 is "run".
            Assert.True((ai.AttackFlags & Minds.AttackFlagRun) != 0,
                        $"{name} would walk into a fight (attackFlags 0x{ai.AttackFlags:x})");
        }
        Assert.True(seen > 50, $"only {seen} AI files read");
    }

    [Fact]
    public void An_attack_rallies_other_guards_and_nobody_else()
    {
        using var files = Install.Open();
        var classes = new AiClasses(files);

        // The shout only moves guards, and the game has two of them. Everything else that hears an
        // attack is either the player's party (aiParty — `Minds.Rally`, written) or nothing at all.
        int guards = 0, total = 0;
        foreach (var name in files.List(AiClasses.Folder))
        {
            if (!name.EndsWith(".nai", StringComparison.OrdinalIgnoreCase)) continue;
            if (classes.Get(Path.GetFileName(name)) is not { } ai) continue;
            total++;
            if (ai.SearchStyle == Minds.GuardSearchStyle) guards++;
        }
        Assert.Equal(136, total);
        Assert.Equal(2, guards);        // guard.nai and passivePed.nai, and nothing else

        // Old Town's two convent knights share guard.nai and stand 182 units apart, inside the
        // 500-unit earshot, so one of them attacking really does bring the other.
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var minds = place.Awaken(new AiClasses(files));
        var knights = minds.All.Where(m => m.Thing.Template.StartsWith("convknight", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, knights.Length);
        Assert.All(knights, k => Assert.Equal(Minds.GuardSearchStyle, k.Class.SearchStyle));
        Assert.True(Vector3.Distance(knights[0].Thing.Position, knights[1].Thing.Position)
                    < Minds.AttackShoutRange,
                    "the two guards are out of earshot, so the rally never fires");

        // The third character in the level running the guard search is the **horse**, on
        // passivePed.nai — but its behaviourClass puts it in `donothing`, so it never searches, and
        // it stands nearly 1,800 units off in any case. Worth pinning: "searchStyle 1" is not the
        // same set as "the guards".
        var horse = minds.All.Single(m => m.Thing.Template == "horse");
        Assert.Equal(Minds.GuardSearchStyle, horse.Class.SearchStyle);
        Assert.Equal(AiMode.DoNothing, horse.Mode);
        Assert.True(Vector3.Distance(horse.Thing.Position, knights[0].Thing.Position)
                    > Minds.AttackShoutRange);
    }

    [Fact]
    public void One_enemy_fighting_does_not_wake_the_others()
    {
        using var files = Install.Open();
        var (place, minds, sim) = Monastery(files);

        // Searching off, so the only thing that could hand anybody a target is the shout itself.
        minds.SearchingOff = true;
        var christof = place.Player!.Thing;
        var start = christof.Position;

        // The most crowded Cappadocian that Christof can actually be stood next to and seen by —
        // picked rather than assumed, because the Monastery's floor does not extend everywhere.
        var hostiles = minds.All
            .Where(m => m.Thing.Template.StartsWith("cappadocian", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => minds.All.Count(o => !ReferenceEquals(o, m)
                && Vector3.Distance(o.Thing.Position, m.Thing.Position) < Minds.AttackShoutRange))
            .ToArray();
        Mind? lead = null;
        foreach (var candidate in hostiles)
        {
            christof.Position = candidate.Thing.Position + new Vector3(120f, 0f, 0f);
            if (!place.Player.Ground()) { christof.Position = start; continue; }
            if (!Sight.Sees(place.Level, candidate.Thing, christof)) { christof.Position = start; continue; }
            lead = candidate;
            break;
        }
        Assert.NotNull(lead);

        // Everyone else with a mind inside the 500-unit earshot. If the shout woke a room, these
        // are who would wake.
        var listeners = minds.All
            .Where(m => !ReferenceEquals(m, lead) && m.Thing.Type != ThingType.Player
                        && Vector3.Distance(m.Thing.Position, lead!.Thing.Position) < Minds.AttackShoutRange)
            .ToArray();
        Assert.NotEmpty(listeners);
        Assert.All(listeners, m => Assert.Null(m.Target));

        lead!.Aim(christof, 0);
        lead.Enter(AiMode.Enemy, andBase: false);
        sim.Advance(30f, 1f / 30f);

        // It fought, and it was the only one that did.
        Assert.NotEmpty(sim.Blows);
        Assert.All(sim.Blows, b => Assert.Same(lead.Thing, b.Attacker));

        // And nobody within earshot so much as looked up. This is what 1.1 does,
        // in the Monastery: it does not set any nearby enemies on the player, and
        // one enemy at a time can be pulled from a distance, provided the player
        // only rouses that one enemy by getting into its line of sight. Two independent
        // readings — the mind's event handler, and playing the original — and the same answer: event
        // 9 rallies guards and the player's party, and neither is present here.
        Assert.All(listeners, m => Assert.Null(m.Target));
        Assert.All(listeners, m => Assert.NotEqual(AiMode.Enemy, m.Mode));
    }

    [Fact]
    public void Reach_is_measured_between_bodies_not_between_centres()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var knights = place.Things.Where(t => t.Template.StartsWith("convknight", StringComparison.Ordinal)).ToArray();
        var (a, b) = (knights[0], knights[1]);
        Assert.True(a.Radius > 0f && b.Radius > 0f, "the knights have no bodies to measure");

        // Stand them a whole radius further apart than the attack reaches, and the gap is what
        // decides — the engine's own test takes both collide shapes off the distance.
        float reach = a.Actor!.Weapon!.Current.MaxDistance;
        b.Position = a.Position + new Vector3(reach + a.Radius + b.Radius - 1f, 0f, 0f);
        Assert.True(Bodies.Gap(a, b) < reach, "the bodies should be within reach of each other");
        Assert.True(Vector3.Distance(a.Position, b.Position) > reach,
                    "and their centres should not be, or the test proves nothing");
    }

    /// <summary>
    /// **The monastery's Cappadocians leave the monks alone, and it took seven readings to get
    /// here.** Play showed it twice, rightly both times: the Cappadocians never turn on
    /// those monks in the base game. Six explanations were tried against the
    /// game and eliminated (neutrality, team, the enemy qualifier, range,
    /// `AISetMainFlags`, the AI class); the seventh was a divergence this tree introduced.
    ///
    /// `Mind.Settle` used to look up a walk node near <see cref="Mind.Home"/> while the home was
    /// the world origin — so every Cappadocian that failed a search set off across the monastery
    /// toward 0,0,0 and took against the monks on the way. The home is where the character came
    /// into the world (the finalize's spawn transform — found after a
    /// session in which this tree believed nothing wrote it and gave nobody a home at all), and a
    /// Cappadocian's is in the crypt, nowhere near a monk.
    ///
    /// Twenty seconds of simulation, which was ten times what it took to reproduce.
    ///
    /// **Scope, and it matters.** This asserts the monks are safe **while the party lives**, which
    /// is what the shipped game does. In 1.1, then, with **both party members
    /// dead** and a monk in line of sight, the Cappadocians really do hunt them — no special rule,
    /// just a searcher with no nearer target, on its way home from wherever the chase left it. That is
    /// faithful; the Fix that would change it is
    /// named `monasterymonks` and is not built. So this test must not be
    /// widened into "a monk is never taken against" — that would be asserting a falsehood.
    /// </summary>
    [Fact]
    public void The_monastery_cappadocians_never_take_against_the_monks()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", things: true, sky: false,
                                  scene: "MON1_8_2", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1);
        var monks = place.Things.Where(t => t.Template.Contains("monk", StringComparison.OrdinalIgnoreCase))
                                .ToHashSet();
        Assert.True(monks.Count >= 3, $"expected the monastery's monks, found {monks.Count}");

        var sim = new Simulation(place);
        for (int step = 0; step < 600; step++) sim.Step(1f / 30f);

        var taken = minds.All.Where(m => m.Target is { } t && monks.Contains(t))
                             .Select(m => $"{m.Thing.Template} -> {m.Target!.Template}").ToArray();
        Assert.True(taken.Length == 0, "a monk was taken against: " + string.Join(", ", taken));
    }
}