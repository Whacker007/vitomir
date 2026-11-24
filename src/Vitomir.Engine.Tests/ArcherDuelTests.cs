// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The skeleton archer's duel, as in 1.1**.
/// The Monastery's
/// <c>skel_bow</c> at (1957, −82, −271) shooting at Christof while he walks back and forth
/// 340–530 units away. What 1.1 does, and these pin: a draw of the bow's 1.333 s
/// (<c>WEAPON_STRIKETIME</c>) with the arrow loosed 0.54–0.67 s into it, a new draw every
/// 1.53–1.76 s (about 1.67 s on average), and every arrow aimed at where Christof **is** as it
/// leaves — the aim reads the target's position and nothing of its motion (it takes the target's
/// position and half its height) — so the arrows at a walking Christof fly
/// 18–104 units behind him, and only those at a Christof standing still reach him. The same walk
/// in Vitomir (the same clicks at the same moments) draws first at the same 2.03 s.
/// </summary>
public class ArcherDuelTests
{
    /// <summary>Where Christof stands still under the arrows (a spot the archer reaches in 1.1).</summary>
    static readonly Vector3 StoodAt = new(1919.5f, -422f, -270f);

    static (Location Place, Minds Minds, WorldThing Archer, WorldThing Christof, Vitomir.Engine.Vfs.GameFiles Files) Duel(Vector3 at)
    {
        var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var minds = place.Awaken(new AiClasses(files));
        var archer = place.Things.First(t => t.Template.Equals("skel_bow", StringComparison.OrdinalIgnoreCase)
                                             && Bodies.Distance2D(t.Position, new Vector3(1957f, -82f, 0f)) < 5f);
        var christof = place.Player!.Thing;
        int node = place.Walk!.Find(at);
        christof.Position = at with { Z = place.Walk.Nodes[node].Height(at.X, at.Y) };
        christof.Sector = place.Walk.Nodes[node].Sector;
        place.Player.Ground();
        christof.Actor!.Health = christof.Actor.MaxHealth = 100_000f;       // he has to outlast it
        // Only the two of them: the others the Monastery has about would join in.
        foreach (var m in minds.All)
            if (!ReferenceEquals(m.Thing, archer) && !ReferenceEquals(m.Thing, christof)
                && Vector3.Distance(m.Thing.Position, archer.Position) < 1500f)
                m.Thing.Flags |= WorldThing.Flag.Remove;
        minds.Aggravate(minds.All.First(m => ReferenceEquals(m.Thing, archer)), christof);
        return (place, minds, archer, christof, files);
    }

    /// <summary>
    /// **The rhythm.** Each draw is the bow's 1.333 s and looses its arrow about 0.6 s in (1.1,
    /// sampled every 110 ms: 0.54–0.67 s); the next draw starts when the archer's mind next thinks,
    /// 1.53–1.76 s after the last began. Stepped at 60 a second, as the original runs.
    /// </summary>
    [Fact]
    public void The_archer_draws_every_one_and_two_thirds_seconds_and_looses_six_tenths_in()
    {
        var (place, _, archer, _, files) = Duel(StoodAt);
        using var _f = files;
        var sim = new Simulation(place);
        var draws = new List<int>();
        bool was = false;
        for (int frame = 0; frame < 60 * 22; frame++)
        {
            sim.Step(1f / 60f);
            bool drawing = archer.Actor!.Action == ActionMode.Swing;
            if (drawing && !was) draws.Add(sim.Milliseconds);
            was = drawing;
        }
        var shots = sim.Shots.Where(s => ReferenceEquals(s.Attacker, archer)).ToList();
        Assert.True(draws.Count >= 10, $"{draws.Count} draws in 22 s");
        for (int i = 1; i < draws.Count; i++)
            Assert.InRange(draws[i] - draws[i - 1], 1450, 1950);
        double mean = (draws[^1] - draws[0]) / (double)(draws.Count - 1);
        Assert.InRange(mean, 1580, 1780);
        foreach (var s in shots)
        {
            int began = draws.Last(d => d <= s.AtMs);
            Assert.InRange(s.AtMs - began, 500, 700);
        }
    }

    /// <summary>
    /// **No lead.** With a steady hand (no scatter), an arrow at a Christof walking across the line
    /// at 115 units a second leaves along the line to where he stood as it was loosed, and so passes
    /// behind him: in 1.1 every arrow at the walking Christof missed, by 18 to 54 units at a walk.
    /// </summary>
    [Fact]
    public void An_arrow_is_aimed_where_its_target_is_and_passes_behind_a_walking_one()
    {
        var (place, _, archer, christof, files) = Duel(new Vector3(1886f, -495f, -270f));
        using var _f = files;
        archer.Actor!.Stat[(int)ActorStat.Dexterity] = archer.Actor.BaseStat[(int)ActorStat.Dexterity] = 100f;   // a steady hand
        var sim = new Simulation(place);
        var goal = new Vector3(2251f, -495f, -270f);
        Assert.True(place.Player!.WalkTo(goal, place.Walk!.Find(goal), run: false));
        var where = new Dictionary<int, Vector3>();
        for (int frame = 0; frame < 60 * 3 && sim.Shots.Count == 0; frame++)
        {
            sim.Step(1f / 60f);
            where[sim.Milliseconds] = Projectiles.Aim(christof);
        }
        var shot = Assert.Single(sim.Shots, s => ReferenceEquals(s.Attacker, archer));
        Assert.True(place.Player.Walking, "Christof should still be walking when the arrow leaves");
        var line = Vector3.Normalize(where[shot.AtMs] - shot.From);
        float off = MathF.Acos(Math.Clamp(Vector3.Dot(line, Vector3.Normalize(shot.Velocity)), -1f, 1f)) * 180f / MathF.PI;
        Assert.True(off < 0.5f, $"the arrow left {off:0.00} degrees off the line to where Christof was");
        for (int frame = 0; frame < 60; frame++) sim.Step(1f / 60f);
        Assert.DoesNotContain(sim.Blows, b => ReferenceEquals(b.Attacker, archer));
    }
}
