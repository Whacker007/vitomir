// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// What 1.1's characters do in the street, with their animations and effects —
/// Old Town at night, and the University: the knights, the horse and the party all break into an
/// idle flourish on one rhythm, and ease back out of it; everybody with a weapon stands, walks and
/// idles in that weapon's stance when the engine says so; a knight marks <c>ef_wanted</c> on
/// whoever he catches attacking — a fellow knight included — while the mark itself does nothing
/// at all; a character with nothing to fight with stays in the enemy mode with nobody; and one
/// you are meant to talk to never looks for a fight.
/// </summary>
public class StreetBehaviourTests
{
    static (Location Place, Minds Minds, Simulation Sim) OldTownAtNight(GameFiles files)
    {
        var place = Location.Load(files, "P1_OLDT", things: true, sky: false, scene: "OLDT_7_7", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        return (place, minds, new Simulation(place));
    }

    static string Motion(WorldThing t) => t.Animation?.Motion?.Mode.Name ?? "";

    /// <summary>
    /// **Every gap between one flourish and the next is thirty seconds and a whole number of
    /// four-second rolls**: as in 1.1, where every one falls on that grid
    /// to within a tenth of a second. Here the frames are a thirtieth of a second, so each roll can
    /// land up to a frame late; that is the only slack allowed. And a flourish ends back in the
    /// stand — the `fidget` flag's tag 20, answered by choosing the locomotion again.
    /// </summary>
    [Fact]
    public void Standing_characters_flourish_every_thirty_seconds_and_a_few_rolls()
    {
        using var files = Install.Open();
        var (place, minds, sim) = OldTownAtNight(files);
        var watched = minds.All.Where(m => m.Thing.Template is "knightofstjohn" or "horse").ToArray();
        Assert.Equal(5, watched.Length);

        var starts = watched.ToDictionary(m => m, _ => new List<int>());
        var endedIn = new List<string>();
        var was = watched.ToDictionary(m => m, m => Motion(m.Thing));
        const float Frame = 1f / 30f;
        for (int i = 0; i < 30 * 240; i++)
        {
            sim.Step(Frame);
            foreach (var m in watched)
            {
                string now = Motion(m.Thing);
                if (now.StartsWith("idle", StringComparison.Ordinal) && !was[m].StartsWith("idle", StringComparison.Ordinal))
                    starts[m].Add(place.Milliseconds);
                else if (was[m].StartsWith("idle", StringComparison.Ordinal) && !now.StartsWith("idle", StringComparison.Ordinal))
                    endedIn.Add(now);
                was[m] = now;
            }
        }

        foreach (var (m, times) in starts)
        {
            Assert.True(times.Count >= 3, $"{m.Thing.Template} #{m.Thing.Guid} flourished {times.Count} times in four minutes");
            foreach (var (a, b) in times.Zip(times.Skip(1)))
            {
                int past = b - a - Minds.IdleRest;
                Assert.True(past >= 0, $"{m.Thing.Template}: {b - a} ms between flourishes, under the thirty seconds");
                int rolls = past / Minds.IdleRetry;
                int late = past - rolls * Minds.IdleRetry;
                // Each wait ends on the first frame past it, so every roll — and the thirty seconds —
                // can each come a frame late, and the frame's clock rounds to the millisecond.
                Assert.True(late <= (rolls + 2) * 34, $"{m.Thing.Template}: {b - a} ms is not thirty seconds and whole rolls");
            }
        }
        Assert.NotEmpty(endedIn);
        Assert.All(endedIn, n => Assert.Equal("stand", n));
    }

    /// <summary>
    /// The knight's flourish is his set's <c>idle</c>, six seconds of it (179 frames at 30), as
    /// in 1.1; and the steered character has them too — the roll comes before the gate that
    /// stops a steered mind thinking (Christof flourished three times in 1.1 while he stood
    /// in front of Ecaterina).
    /// </summary>
    [Fact]
    public void The_steered_character_flourishes_and_a_knight_s_lasts_six_seconds()
    {
        using var files = Install.Open();
        var (place, minds, sim) = OldTownAtNight(files);
        var knight = minds.All.First(m => m.Thing.Template == "knightofstjohn");
        var christof = place.Steered!;
        int? knightStart = null, knightEnd = null;
        bool christofFlourished = false;
        for (int i = 0; i < 30 * 120 && (knightEnd is null || !christofFlourished); i++)
        {
            sim.Step(1f / 30f);
            string k = Motion(knight.Thing);
            if (k == "idle") { knightStart ??= place.Milliseconds; }
            else if (knightStart is not null) knightEnd ??= place.Milliseconds;
            if (Motion(christof).StartsWith("idle", StringComparison.Ordinal)) christofFlourished = true;
        }
        Assert.NotNull(knightEnd);
        Assert.Equal("knightstjohn_idle", Path.GetFileNameWithoutExtension(knight.Thing.Animation!.Set.Modes
            .First(m => m.Name == "idle").Animation).ToLowerInvariant());
        Assert.InRange(knightEnd!.Value - knightStart!.Value, 5_900, 6_100);
        Assert.True(christofFlourished, "the steered Christof never flourished in two minutes");
    }

    /// <summary>
    /// **Driving is moving**, so no flourish plays while a key is held (the think refuses the roll
    /// when the moving test says the character is moving — a walk, or any velocity). Driving is ours and
    /// has no route, and until then the roll only asked about a route: every thirty-odd seconds of
    /// driving, Christof broke into his look-round while sliding along at full run (in play,
    /// sometimes while running Christof stopped the running animation and switched to
    /// the standing animation, still moving).
    /// </summary>
    [Fact]
    public void Driving_is_moving_so_no_flourish_plays()
    {
        using var files = Install.Open();
        var (place, _, sim) = OldTownAtNight(files);
        var legs = place.Steered!.Locomotion!;
        legs.Running = true;
        const float Frame = 1f / 30f;
        for (int i = 0; i < 30 * 75; i++)
        {
            sim.Step(Frame);
            float a = i * Frame * 0.5f;                     // round in a slow circle
            legs.Move(new Vector2(MathF.Cos(a), MathF.Sin(a)), Frame);
            Assert.True(legs.Moving);
            Assert.False(Motion(place.Steered!).StartsWith("idle", StringComparison.Ordinal),
                         $"a flourish at {place.Milliseconds} ms while driving");
        }
        // Let go, and the rolls come back: the flourish is for standing characters, and he is one again.
        bool flourished = false;
        for (int i = 0; i < 30 * 75 && !flourished; i++)
        {
            sim.Step(Frame);
            legs.Move(Vector2.Zero, Frame);
            flourished = Motion(place.Steered!).StartsWith("idle", StringComparison.Ordinal);
        }
        Assert.True(flourished, "standing still for 75 s, he never flourished");
    }

    /// <summary>
    /// **The weapon's stance goes with the player's characters always, and with anybody else only
    /// in a fight**: 1.1's Christof stands in `stand_onehand` with his sword, and its
    /// knights stand plain on their rounds and take up `stand_polearm` in the enemy mode. The
    /// flourish follows the same rule — Christof's is `idle_onehand`.
    /// </summary>
    [Fact]
    public void A_weapon_s_stance_is_the_party_s_always_and_a_knight_s_in_a_fight()
    {
        using var files = Install.Open();
        var (place, minds, sim) = OldTownAtNight(files);
        var templates = new TemplateSet(files);
        var christof = place.Steered!;
        christof.Actor!.Weapon = Weapon.From("broadsword", templates.Resolve("broadsword"))!;
        Assert.Equal(1, christof.Actor.Weapon.Carry);
        var legs = christof.Locomotion!;
        legs.Reanimate();
        Assert.Equal("stand_onehand", Motion(christof));
        Assert.True(legs.Fidget() > 0);
        Assert.Equal("idle_onehand", Motion(christof));

        var knight = minds.All.First(m => m.Thing.Template == "knightofstjohn");
        knight.Walker.Reanimate();
        Assert.Equal(0, knight.Walker.Stance);
        Assert.Equal("stand", Motion(knight.Thing));
        knight.Enter(AiMode.Enemy, andBase: false);
        Assert.Equal(4, knight.Walker.Stance);
        // Nothing changes until the locomotion is chosen again — a walk starting or stopping.
        Assert.Equal("stand", Motion(knight.Thing));
        knight.Walker.Reanimate();
        Assert.Equal("stand_polearm", Motion(knight.Thing));
    }

    /// <summary>A place where a knight can plainly see: three hundred units in front of the one on the square's north side.</summary>
    static (Mind Knight, WorldThing Christof, WorldThing Victim) Witnessed(Location place, Minds minds)
    {
        var knight = minds.All.First(m => m.Thing.Template == "knightofstjohn"
                                          && Vector3.Distance(m.Thing.Position, new Vector3(-727, 3176, 0)) < 10f);
        var christof = place.Steered!;
        var victim = minds.All.First(m => m.Thing.Template == "civ02beggar").Thing;
        foreach (var (who, dx) in new[] { (christof, 0f), (victim, 60f) })
        {
            place.Move(who, knight.Thing.Position + new Vector3(dx, -300f, 0f));
            who.Locomotion?.Ground();
        }
        return (knight, christof, victim);
    }

    static int Marks(WorldThing t) => t.Effects.All.Count(e => !e.Spent && e.Name == "ef_wanted");

    /// <summary>
    /// **A knight marks whoever he catches attacking, for two minutes, and goes for them**:
    /// in 1.1, Christof in the attack stance closing on a knight is, half a second later, marked by that
    /// knight's search until exactly two minutes on and taken as its target.
    /// </summary>
    [Fact]
    public void A_knight_marks_whoever_he_catches_attacking_and_goes_for_them()
    {
        using var files = Install.Open();
        var (place, minds, sim) = OldTownAtNight(files);
        var (knight, christof, victim) = Witnessed(place, minds);

        int? caught = null;
        for (int i = 0; i < 30 * 3 && caught is null; i++)
        {
            christof.Actor!.Action = ActionMode.Attack;
            christof.Actor.Target = victim;
            sim.Step(1f / 30f);
            if (ReferenceEquals(knight.Target, christof)) caught = place.Milliseconds;
        }
        Assert.NotNull(caught);
        Assert.Equal(AiMode.Enemy, knight.Mode);
        var mark = christof.Effects.All.First(e => e.Name == "ef_wanted");
        Assert.InRange(mark.Until - caught!.Value, Minds.WantedFor - 100, Minds.WantedFor);
        Assert.True(Minds.IsCriminal(christof));
    }

    /// <summary>
    /// **The mark does nothing on its own.** The guard's "already wanted" is the fold of effect kind
    /// 22, and <c>ef_wanted</c> composes by multiplying from 0, so it is 0 however many marks there
    /// are. In 1.1 Christof, marked, standing near a knight who has just finished with Wilhem, is
    /// left alone; here, the same.
    /// </summary>
    [Fact]
    public void A_marked_character_who_is_not_attacking_is_left_alone()
    {
        using var files = Install.Open();
        var (place, minds, sim) = OldTownAtNight(files);
        var (knight, christof, _) = Witnessed(place, minds);
        EffectRuntime.Apply(christof, new EffectDefinitions(files).Wanted!, 0, place.Milliseconds,
                            Minds.WantedFor, value: Minds.WantedValue);
        Assert.Equal(1, Marks(christof));
        Assert.Equal(0f, christof.Effects.Value(EffectKind.Wanted));

        sim.Advance(3f, 1f / 30f);
        Assert.Null(knight.Target);
        Assert.Equal(1, Marks(christof));
        Assert.False(Minds.IsCriminal(christof));
    }

    /// <summary>
    /// **A knight catches a knight.** The guard search does not ask who is a friend, so a knight
    /// swinging at Christof is caught by the knight beside him, marked, and taken — and let go on
    /// the next think, because the enemy mode does ask. With no <c>wakeupDelay</c> and no
    /// <c>awaken</c> the next think is the next frame, so it happens again, and again: in 1.1
    /// one knight collects a fresh <c>ef_wanted</c> every frame of his swing.
    /// </summary>
    [Fact]
    public void A_knight_swinging_at_christof_is_caught_by_the_knight_beside_him_every_frame()
    {
        using var files = Install.Open();
        var (place, minds, sim) = OldTownAtNight(files);
        var (knight, christof, _) = Witnessed(place, minds);
        var other = minds.All.First(m => m.Thing.Template == "knightofstjohn" && !ReferenceEquals(m, knight));
        place.Move(other.Thing, christof.Position + new Vector3(0f, -60f, 0f));
        other.Walker.Ground();
        other.Thing.Actor!.Flags |= World.Actor.Flag.AiPaused;          // it swings and does nothing else

        var taken = 0;
        for (int i = 0; i < 30; i++)
        {
            other.Thing.Actor.Action = ActionMode.Swing;
            other.Thing.Actor.Target = christof;
            sim.Step(1f / 30f);
            if (ReferenceEquals(knight.Target, other.Thing)) taken++;
        }
        Assert.True(Marks(other.Thing) >= 10, $"only {Marks(other.Thing)} marks in a second of being caught");
        Assert.Equal(0, Marks(christof));
        Assert.True(taken < 30, "the knight kept his fellow knight as a target instead of letting him go");
    }

    /// <summary>
    /// **A flourish eases back into the stand over a fifth of a second** (rate 5): the
    /// pose last drawn is kept and each new one is drawn towards it by a weight falling from 1 at
    /// five a second. The knight's pose a tenth of a second after his flourish ends is half way
    /// between the two — play showed the snap, jerky and abrupt.
    /// </summary>
    [Fact]
    public void A_flourish_fades_back_into_the_stand()
    {
        using var files = Install.Open();
        var (place, minds, _) = OldTownAtNight(files);
        var knight = minds.All.First(m => m.Thing.Template == "knightofstjohn");
        var animation = knight.Thing.Animation!;
        Assert.True(knight.Walker.Fidget() > 0);
        Assert.Equal(1f, animation.Fade);                               // it eases into the flourish as well
        animation.Advance(3f);
        Assert.Equal(0f, animation.Fade);
        animation.PoseBones();
        var flourish = animation.BoneMatrix(1);

        knight.Walker.Reanimate();                                      // what the fidget tag does
        Assert.Equal("stand", Motion(knight.Thing));
        Assert.Equal(1f, animation.Fade);
        animation.Advance(0.1f);
        Assert.Equal(0.5f, animation.Fade, 3);
        var half = animation.BoneMatrix(1);
        animation.Advance(0.1f);
        Assert.Equal(0f, animation.Fade);
        var stand = animation.BoneMatrix(1);
        Assert.NotEqual(flourish, stand);
        // Half way, bone for bone — the fade is on the angles and offsets, so the matrices are near it.
        Assert.True(Vector3.Distance(half.Translation, (flourish.Translation + stand.Translation) / 2f) < 2f,
                    $"{half.Translation} is not between {flourish.Translation} and {stand.Translation}");
    }

    /// <summary>
    /// **While an action plays, the motion under it stands still** (the mixer advances one channel,
    /// the action if there is one): a swing does not carry the walk cycle on underneath it.
    /// </summary>
    [Fact]
    public void The_motion_waits_while_an_action_plays()
    {
        using var files = Install.Open();
        var (place, minds, _) = OldTownAtNight(files);
        var knight = minds.All.First(m => m.Thing.Template == "knightofstjohn");
        var animation = knight.Thing.Animation!;
        float frame = animation.Motion!.Player.Frame;
        Assert.True(animation.Play("swing", place.Motions!, onMotion: false) > 0 || animation.Play(1, place.Motions!, onMotion: false) > 0);
        animation.Advance(0.2f);
        Assert.Equal(frame, animation.Motion.Player.Frame);
        Assert.True(animation.Action!.Player.Frame > 0f);
    }

    /// <summary>
    /// **Nothing to fight with, and the enemy mode keeps it**: the target is dropped
    /// and the mode is not changed, so 1.1's horse, drawn into the knights' fight, stays in the
    /// enemy mode with nobody from then on.
    /// </summary>
    [Fact]
    public void A_character_with_nothing_to_fight_with_drops_the_target_and_stays_an_enemy()
    {
        using var files = Install.Open();
        var (place, minds, sim) = OldTownAtNight(files);
        var horse = minds.All.First(m => m.Thing.Template == "horse");
        Assert.Null(horse.Thing.Actor!.Weapon);
        horse.Aim(place.Steered, place.Milliseconds);
        horse.Enter(AiMode.Enemy, andBase: false);
        sim.Advance(2f, 1f / 30f);
        Assert.Null(horse.Target);
        Assert.Equal(AiMode.Enemy, horse.Mode);
        Assert.Equal(AiMode.DoNothing, horse.BaseMode);
    }

    /// <summary>
    /// **A character you are meant to talk to never searches**: Ecaterina, with
    /// THING_AF_TALKTO, stood in her search for two minutes in 1.1 with Christof and Wilhem in the
    /// room and took nobody. Vitomir used to have her take Christof on her first think, which is
    /// where the target flapping of Cosmas and Ecaterina came from.
    /// </summary>
    [Fact]
    public void Ecaterina_never_looks_for_a_fight()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_UNIV", things: true, sky: false, scene: "UNIV_8_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var sim = new Simulation(place);
        var ecaterina = minds.All.First(m => m.Thing.Template == "ecaterina");
        Assert.True(ecaterina.Thing.Actor!.Has(World.Actor.Flag.TalkTo));
        sim.Advance(60f, 1f / 30f);
        Assert.Equal(0, ecaterina.Acquisitions);
        Assert.Equal(AiMode.EnemySearch, ecaterina.Mode);
    }
}
