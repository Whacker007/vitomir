// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;
using Vitomir.Platform.Space;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// Celerity on the player's body, end to end: the module dresses the player (<c>sandbox.player</c>,
/// Celerity 1, the human's movement), a press of slot 1 pays and makes it live; <b>in the masquerade's mode</b> the sprint stays
/// a human's (5.5 m/s) while the blow's tempo is Celerity's (×2), and no risk is raised; <b>unbound</b> (the key) the sprint is
/// the human's × 2 (11 m/s) — in a public place a <see cref="MasqueradeRisk"/> of speed, in a free place none. these
/// run on the earlier build's curve (all tempo, the switch <c>celerity-curve-tempo</c>); on the default <c>split</c> rank 1's pace is
/// (1 + 1)^0.5 = ×1.41.
/// </summary>
public sealed class CelerityBodyTests
{
    static LevelSpace Floor(LevelPlaces places)
    {
        var v = new List<Vector3>();
        var ix = new List<int>();
        for (int x = -40; x < 40; x += 4)
            for (int y = -20; y < 20; y += 4)
            {
                int at = v.Count;
                v.Add(new(x, y, 0)); v.Add(new(x + 4, y, 0)); v.Add(new(x + 4, y + 4, 0)); v.Add(new(x, y + 4, 0));
                ix.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
            }
        return new LevelSpace("floor", CollisionWorld.FromTriangles([.. v], [.. ix])) { Places = places };
    }

    sealed record Rig(World W, EntityId Player, List<MasqueradeRisk> Risks);

    static Rig Make(LevelPlaces places, params string[] switches)
    {
        var m = TestData.Sandbox(null, [EnhancedModule.CombatTempoOffSwitch, .. switches]);   // Celerity's factors at the earlier build's pace (the global tempo: PaceTests)
        var b = new WorldBuilder(4);
        MovementServices.Register(b, Floor(places));
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        DeathServices.Register(b);
        b.Component<Combatant>().Component<CombatIntent>();
        m.Register(b);
        var w = b.Build();
        var p = MovementServices.SpawnControlled(w, new(-36, 0, 0.01f), 0f, Locomotion.Sandbox);
        w.Service<PeopleHooks>().Made(p, PersonRole.Player, "Christof");
        CombatServices.Arm(w, p);
        return new Rig(w, p, []);
    }

    static void Run(Rig r, int ticks, ActionFrame frame)
    {
        for (int i = 0; i < ticks; i++)
        {
            MovementServices.Step(r.W, frame);
            r.Risks.AddRange(r.W.Events<MasqueradeRisk>().ThisTick);
        }
    }

    static readonly ActionFrame Sprint = new(new Vector2(0, 1), default, Buttons.Sprint, default);

    static float Speed(Rig r) { var v = r.W.Get<Body>(r.Player).Velocity; return new Vector2(v.X, v.Y).Length(); }

    static void Celerity(Rig r, float pace = 2f)
    {
        Run(r, 1, ActionFrame.Idle with { Held = Buttons.Slot1 });                     // the slot's key, through the player's control
        Assert.Equal(pace, r.W.Get<PaceScale>(r.Player).Factor);
    }

    [Fact]
    public void In_the_masquerade_the_sprint_is_a_human_s_while_the_tempo_is_Celerity_s_and_unbound_it_is_doubled_and_a_risk_in_public()
    {
        var r = Make(LevelPlaces.None, EnhancedModule.CelerityTempoSwitch);            // all public; the earlier build's curve
        Run(r, 10, ActionFrame.Idle);
        Assert.Equal(MasqueradeMode.Masquerade, r.W.Get<MasqueradeState>(r.Player).Mode);
        Celerity(r);
        Run(r, World.TicksPerSecond, Sprint);
        Assert.Equal(5.5f, Speed(r), 2);                                               // A human's, whatever Celerity says
        Assert.Equal(new CombatTempo(2f, 2f, 2f), r.W.Get<CombatTempo>(r.Player));     // the blow is Celerity's (Melee 0: skill 1)
        Assert.Empty(r.Risks);

        Run(r, 1, Sprint with { Held = Buttons.Sprint | Buttons.Masquerade });         // the key: unbound
        Run(r, World.TicksPerSecond, Sprint);
        Assert.Equal(MasqueradeMode.Unbound, r.W.Get<MasqueradeState>(r.Player).Mode);
        Assert.Equal(11f, Speed(r), 2);                                                // the human's 5.5 × (1 + 1)
        Run(r, World.TicksPerSecond, ActionFrame.Idle);
        var risk = Assert.Single(r.Risks);                                             // reported at its top, once stopped
        Assert.Equal(MasqueradeAct.Speed, risk.Act);
        Assert.InRange(risk.Measured, 10.9f, 11.1f);
    }

    [Fact]
    public void Unbound_in_a_free_place_a_Celerity_sprint_is_no_risk()
    {
        var r = Make(LevelPlaces.Of([], PlaceKind.Free), EnhancedModule.CelerityTempoSwitch);
        Run(r, 10, ActionFrame.Idle);
        Run(r, 1, ActionFrame.Idle with { Held = Buttons.Masquerade });
        Run(r, 1, ActionFrame.Idle);
        Assert.Equal(MasqueradeMode.Unbound, r.W.Get<MasqueradeState>(r.Player).Mode);
        Celerity(r);
        Run(r, World.TicksPerSecond, Sprint);
        Assert.Equal(11f, Speed(r), 2);
        Run(r, World.TicksPerSecond, ActionFrame.Idle);
        Assert.Empty(r.Risks);
    }

    [Fact]
    public void On_the_split_curve_rank_1_quickens_the_body_by_the_square_root_of_two_and_the_masquerade_still_holds_it()
    {
        var r = Make(LevelPlaces.Of([], PlaceKind.Free));                              // the default curve, split
        Run(r, 10, ActionFrame.Idle);
        float t = CelerityEffect.Split(2, 0.5).Tempo;
        Celerity(r, t);
        Run(r, World.TicksPerSecond, Sprint);
        Assert.Equal(5.5f, Speed(r), 2);                                               // in the masquerade: a human's
        Assert.Equal(new CombatTempo(t, t, t), r.W.Get<CombatTempo>(r.Player));        // the blow's tempo ×1.41 (Melee 0: skill 1)
        Run(r, 1, Sprint with { Held = Buttons.Sprint | Buttons.Masquerade });
        Run(r, World.TicksPerSecond, Sprint);
        Assert.Equal(MasqueradeMode.Unbound, r.W.Get<MasqueradeState>(r.Player).Mode);
        Assert.Equal(5.5f * t, Speed(r), 2);                                           // the human's 5.5 × 1.41
    }
}
