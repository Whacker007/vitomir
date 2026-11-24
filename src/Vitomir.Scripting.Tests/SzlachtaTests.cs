// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// Old Town's night Szlachta, through the game's own scripts: the heal seen twice in play,
/// and the trigger that decides whether one fights at all. The discipline is the
/// shipped `DisciplineBloodHealing`, the effect `ef_disc_bloodhealing`, the AI file
/// `enemySzlachta.nai` — nothing stands in.
/// </summary>
[Collection("scripts")]
public class SzlachtaTests(Scripts s)
{
    sealed class Scene : IDisposable
    {
        public required GameFiles Files { get; init; }
        public required Location Place { get; init; }
        public required CodexScenes Scenes { get; init; }
        public required Simulation Sim { get; init; }
        public required Minds Minds { get; init; }
        public required WorldThing Christof { get; init; }

        public void Step() => Sim.Step(1f / 30f);

        public void Dispose()
        {
            Scenes.Leave();
            Files.Dispose();
        }
    }

    /// <summary>`P1_OLDT` in its first night scene, `OLDT_7_1`, with the scripts in and the minds awake.</summary>
    Scene Enter()
    {
        s.Ready();
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var place = Location.Load(files, "P1_OLDT", sky: false, scene: "OLDT_7_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var christof = place.Player!.Thing;
        var camera = new WorldCamera();
        camera.Start(christof);
        var talk = new Conversations();
        var scenes = new CodexScenes(s.Runtime!)
        {
            Chronicles = set, Camera = camera, Talk = talk, Nls = new Vitomir.Engine.Ui.Strings(files).Find,
        };
        scenes.Enter(place);
        return new Scene
        {
            Files = files, Place = place, Scenes = scenes, Minds = minds, Christof = christof,
            Sim = new Simulation(place) { Scripts = scenes, Camera = camera, Talk = talk },
        };
    }

    /// <summary>
    /// **One Blood Healing every ten seconds, for as long as it is hurt, and half a second standing
    /// after each.** The heal rung's cooldown is the AI file's `discHealRate 10`; the cast itself is
    /// instant, because `SzlachtaEating.nms` has no spell animation (mode 145); and the think that
    /// cast it waits exactly `attackRate` (0.5 s) before the next, with no roll on top — the roll
    /// belongs to an attack being ordered. The order stopped it where it stood.
    /// In the run the Szlachta died 9,988 ms after its first heal, twelve milliseconds
    /// before it could have had a second.
    /// </summary>
    [Fact]
    public void A_hurt_Szlachta_heals_every_ten_seconds_and_stands_half_a_second_after_each()
    {
        using var m = Enter();
        var szlachta = m.Place.Things.First(t => t.Template.Equals("szlachtaEating", StringComparison.OrdinalIgnoreCase));
        var mind = m.Minds.All.First(x => ReferenceEquals(x.Thing, szlachta));
        var it = szlachta.Actor!;
        Assert.Contains(it.Disciplines.All, d => d.Definition.Name == "BloodHealing");

        // Christof beside it, and past harm, so the fight goes on as long as the test wants.
        m.Christof.Position = szlachta.Position + new Vector3(40f, 0f, 0f);
        m.Christof.Sector = szlachta.Sector;
        m.Christof.Actor!.Flags |= Actor.Flag.Invulnerable;
        it.Flags &= ~Actor.Flag.AiPaused;
        m.Minds.Aggravate(mind, m.Christof);

        float Hurt() => it.Health = it.MaxHealth * 0.2f;      // under `lowhealth 0.30`
        var heals = new List<(int At, float Gained, int NextThink)>();
        int freeAt = 0;
        float low = Hurt();
        it.Flags2 |= Actor.Flag2.ScrollCast;                    // `Discipline.Fizzled` never fizzles a scroll's cast
        for (int frame = 0; frame < 30 * 14 && heals.Count < 2; frame++)
        {
            m.Step();
            // The ladder is shut while it is busy: the second heal waits for the swing
            // the cooldown's end finds it in, if any — which swing that is depends on the rolls,
            // whose order is the world's list's.
            if (heals.Count == 1 && freeAt == 0 && m.Minds.Now >= heals[0].At + 10_000 && it.BusyUntil <= m.Minds.Now)
                freeAt = m.Minds.Now;
            if (it.Health > low + 1f)
            {
                heals.Add((m.Minds.Now, it.Health - low, mind.NextThink));
                Assert.Equal(ActionMode.Cast, it.Action);
                Assert.False(mind.Moving, "the cast order stops the caster where it stands");
                low = Hurt();
                it.Flags2 |= Actor.Flag2.ScrollCast;
            }
        }

        Assert.Equal(2, heals.Count);
        // Level 1 of `ef_disc_bloodhealing` is 30 health (effects.ned, v1).
        Assert.All(heals, h => Assert.Equal(30f, h.Gained, 0.01f));
        // Half a second, exactly — not half a second and a roll.
        Assert.All(heals, h => Assert.Equal(h.At + 500, h.NextThink));
        // The second waited out the ten-second cooldown, and came at the first think after it
        // that found the Szlachta free.
        Assert.True(heals[1].At - heals[0].At >= 10_000);
        Assert.InRange(heals[1].At - freeAt, 0, 700);
    }

    /// <summary>
    /// The trigger strip in front of the first Szlachta (`OLDT_7_1` [26], `ActorAIOff_TriggerOn`,
    /// 648 x 128 at 1404, 1728) lies across the route from the Haven's door to the smithy — the way
    /// Christof comes on the first night — so on the normal route both are woken, as in
    /// 1.1. Until it is crossed the first one is paused, and a blow does not wake it.
    /// </summary>
    [Fact]
    public void The_first_Szlachta_sleeps_until_its_strip_is_crossed_and_a_blow_does_not_wake_it()
    {
        using var m = Enter();
        var first = m.Place.Things.First(t => t.Template.Equals("szlachta", StringComparison.OrdinalIgnoreCase));
        Assert.True(first.Actor!.Has(Actor.Flag.AiPaused), "beginscene pauses it");

        // Hit it: the damage event takes a target, and still nothing thinks.
        var mind = m.Minds.All.First(x => ReferenceEquals(x.Thing, first));
        m.Christof.Position = first.Position + new Vector3(40f, 0f, 0f);
        m.Christof.Sector = first.Sector;
        m.Minds.AttackedBy(mind, m.Christof);
        var at = first.Position;
        for (int frame = 0; frame < 30 * 3; frame++) m.Step();
        Assert.True(first.Actor.Has(Actor.Flag.AiPaused));
        Assert.Equal(at, first.Position);
    }
}
