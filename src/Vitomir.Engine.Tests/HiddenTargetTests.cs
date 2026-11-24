// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **An enemy and a hidden Christof**, from a play-test on the `SP3` save (Monastery 1)
/// with <c>sethide 3</c>: the Cappadocian kept walking to exactly where Christof was while he
/// was hidden and stood on the same spot, overlapping him.
///
/// Two halves, and only one of them was ours. **Walking at him is 1.1's**: a target the enemy mode
/// cannot make out (stealth above perception) sends it into the approach
/// (mode 16, `now + 10000`), and the approach orders a walk at the target's **live**
/// position every half second (the move order on the target's position, flag 0x100) — no last-seen spot is kept —
/// until the bodies are within ten units, and lets the target go when the ten seconds are up.
/// **Standing inside him was ours**: 1.1's walker is stopped by any character ahead of it,
/// whose flood starts from the sector the new feet are in;
/// Vitomir looked sixteen units up with its own sector test, which in the first monastery's hall
/// answers a sector whose flood never reaches the one both of them stand in, so the walker met
/// nobody at all.
/// </summary>
public class HiddenTargetTests
{
    /// <summary>
    /// The monastery scene of that save with Christof steered and one Cappadocian, everybody else
    /// out of the world; the Cappadocian 120 units from Christof, on the far side of him from the
    /// hall's middle, where the two of them stood in that save.
    /// </summary>
    internal static (Location Place, Minds Minds, Simulation Sim, WorldThing Christof, WorldThing Cappadocian) Stair()
    {
        var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var christof = place.Player!.Thing;
        var cappadocian = minds.All.First(m => m.Thing.Template.Equals("cappadocian", StringComparison.OrdinalIgnoreCase)).Thing;
        foreach (var m in minds.All)
            if (m.Thing != cappadocian && m.Thing != christof) m.Thing.Flags |= WorldThing.Flag.Remove;
        place.Move(christof, new Vector3(-826f, -590f, -252f));
        place.Player.Ground();
        place.Move(cappadocian, new Vector3(-715f, -533f, -252f));
        cappadocian.Mind!.Walker.Ground();
        var sim = new Simulation(place);
        sim.Step(1f / 60f);                                   // the removals go
        return (place, minds, sim, christof, cappadocian);
    }

    /// <summary>
    /// The walker on its own, the mind held off: sent to a spot on the far side of Christof, on a
    /// straight leg that runs through him, the Cappadocian is stopped at his body (the two radii,
    /// 32 and 31.3) and, while the two stand in the same sector, never gets inside it. Once it
    /// walked through him to within 14 units.
    ///
    /// **Why only in the same sector.** A thing is kept in one sector's list, the one its own
    /// position is in, and the walker's flood steps into a neighbour only across a portal
    /// the walker is within its own radius of. So a walker more than its radius past a
    /// portal does not meet somebody standing just the other side of it, and the two bodies can
    /// overlap by up to a radius there — the engine's, and it happens in this very walk once the
    /// detour takes the Cappadocian round into the next sector (44) south of Christof.
    /// </summary>
    [Fact]
    public void A_walker_is_stopped_by_a_character_ahead_of_it_in_the_same_hall()
    {
        var (_, _, sim, christof, cappadocian) = Stair();
        var mind = cappadocian.Mind!;
        mind.NextThink = int.MaxValue;                        // only the walker
        Assert.True(mind.Walker.WalkTo(new Vector3(-838f, -622f, -252f), mind.Walker.Node));
        float least = float.MaxValue;
        bool blockedByHim = false;
        for (int i = 0; i < 180; i++)
        {
            sim.Step(1f / 60f);
            if (cappadocian.Sector == christof.Sector)
                least = MathF.Min(least, Bodies.Distance2D(cappadocian.Position, christof.Position));
            blockedByHim |= ReferenceEquals(mind.Walker.BlockedBy, christof);
        }
        Assert.True(blockedByHim);
        Assert.True(least >= christof.Radius + cappadocian.Radius - 0.5f, $"came within {least:0.0} of Christof's middle");
    }

    /// <summary>
    /// The whole of it, as it was played: the Cappadocian is fighting Christof when he hides
    /// (stealth 3 against its perception of 0). It cannot make him out, so it goes into the approach
    /// and walks at where he stands — 1.1's, and pinned here — but ends up **against** him, not in
    /// him, and after the approach's ten seconds it lets him go and goes back to its search.
    /// </summary>
    [Fact]
    public void An_enemy_walks_up_to_a_hidden_target_and_not_into_him_and_lets_him_go_after_ten_seconds()
    {
        var (_, minds, sim, christof, cappadocian) = Stair();
        var mind = cappadocian.Mind!;
        christof.Actor!.Stealth = 3;
        Assert.Equal(0, cappadocian.Actor!.Perception);
        mind.Aim(christof, minds.Now);
        mind.Enter(AiMode.Enemy, andBase: false);
        mind.NextThink = minds.Now;

        float least = float.MaxValue;
        bool approached = false;
        int? droppedAt = null;
        int start = minds.Now;
        for (int i = 0; i < 60 * 13; i++)
        {
            sim.Step(1f / 60f);
            if (cappadocian.Sector == christof.Sector)
                least = MathF.Min(least, Bodies.Distance2D(cappadocian.Position, christof.Position));
            approached |= mind.Mode == AiMode.Approach;
            if (droppedAt is null && mind.Target is null) droppedAt = minds.Now - start;
        }
        Assert.True(approached);
        // Walked at him: it ends up within the approach's ten units of his body …
        Assert.True(least <= christof.Radius + cappadocian.Radius + Minds.ApproachStop, $"never nearer than {least:0.0}");
        // … and never inside it, while the two are in the same sector (see the test above).
        Assert.True(least >= christof.Radius + cappadocian.Radius - 0.5f, $"came within {least:0.0} of Christof's middle");
        // The approach's ten seconds, then back to the search, which cannot see him either.
        Assert.NotNull(droppedAt);
        Assert.InRange(droppedAt!.Value, Minds.ApproachFor, Minds.ApproachFor + 1500);
        Assert.Null(mind.Target);
    }

    /// <summary>
    /// **The shuffle, and it is 1.1's** — the same play-test and save: the Cappadocian kept
    /// coming up, stopping very briefly, moving away and stopping briefly, back and forth, until
    /// it turned on Wilhem. Nothing in the chain stops her at his body:
    /// <list type="bullet">
    /// <item>the approach orders a walk at his **own position** every half second while
    /// the two bodies are ten units or more apart — the centres' distance in three dimensions less
    /// both radii (`_DAT_004eb634`) — and when they are nearer it orders nothing, but it
    /// does not stop the walk it already gave;</item>
    /// <item>that walk runs until his body stops it, a step short of touching. The leg's
    /// end is his middle, 63 units on, so it is not "close enough" (the walker's own
    /// radius), there is no sliding round a body square ahead (0.95), and the ladder
    /// waits 300 ms for a person — <b>"stopping very briefly"</b> — then, still blocked,
    /// steps round the long way: a random ±91° to ±140°, 50 to 100 units out —
    /// <b>"moving away"</b>;</item>
    /// <item>the approach's next look finds her ten units off again and walks her back — <b>"then
    /// back and forth"</b> — for the approach's ten seconds, after which she lets him go,
    /// and her search, which cannot see him, finds Wilhem.</item>
    /// </list>
    /// Her Heightened Senses (the cast in the play-test's log) is the enemy mode reaching for a sense discipline
    /// the first time it cannot notice its target; at her level it gives perception 2
    /// (`ef_disc_hsenses`), under his 3 standing still — but a walking Christof is at 2 and
    /// is seen. What made the settle show a different fight was ours: it ran `sethide 3` before the
    /// first step, so she never saw him and took Wilhem at once; a windowed run types it on the tenth
    /// frame, after she has taken Christof, and a settle does the same (`SettleConsole`).
    /// </summary>
    [Fact]
    public void An_enemy_at_a_hidden_target_waits_steps_round_him_and_comes_back_for_ten_seconds()
    {
        var (_, minds, sim, christof, cappadocian) = Stair();
        var mind = cappadocian.Mind!;
        var walker = mind.Walker;
        christof.Actor!.Stealth = 3;
        mind.Aim(christof, minds.Now);
        mind.Enter(AiMode.Enemy, andBase: false);
        mind.NextThink = minds.Now;

        int waits = 0, detours = 0, returns = 0;
        bool waiting = false, detouring = false, wentAway = false;
        float touching = christof.Radius + cappadocian.Radius + Minds.ApproachStop;
        for (int i = 0; i < 60 * 10; i++)
        {
            sim.Step(1f / 60f);
            bool nowWaiting = walker.Waiting && ReferenceEquals(walker.BlockedBy, christof);
            if (nowWaiting && !waiting) waits++;
            waiting = nowWaiting;
            if (walker.Detouring && !detouring) detours++;
            detouring = walker.Detouring;
            float apart = Bodies.Distance2D(cappadocian.Position, christof.Position);
            if (apart > touching + 30f) wentAway = true;
            else if (apart <= touching && wentAway) { returns++; wentAway = false; }
        }
        Assert.Equal(AiMode.Approach, mind.Mode);                    // still at it: not ten seconds yet
        Assert.True(waits >= 3, $"waited for him {waits} times");
        Assert.True(detours >= 3, $"stepped round {detours} times");
        Assert.True(returns >= 2, $"came back {returns} times");
    }

    /// <summary>
    /// **A new order does not end a wait, nor forget it**: walked into
    /// Christof, the Cappadocian waits for him; ordered again mid-wait — as the approach and the
    /// manoeuvre do — she is still waiting, and when the wait is out and he is still there she steps
    /// round him rather than waiting a second time, because nothing cleared the "waited for a person"
    /// bit (0x40) but a step that went where it was aimed.
    /// </summary>
    [Fact]
    public void A_walker_waiting_for_somebody_keeps_waiting_under_a_new_order_and_then_steps_round()
    {
        var (_, _, sim, christof, cappadocian) = Stair();
        var mind = cappadocian.Mind!;
        var walker = mind.Walker;
        mind.NextThink = int.MaxValue;                        // only the walker
        Assert.True(walker.WalkTo(christof.Position, christof.Mind!.Walker.Node));
        int i = 0;
        for (; i < 180 && !(walker.Waiting && ReferenceEquals(walker.BlockedBy, christof)); i++) sim.Step(1f / 60f);
        Assert.True(i < 180, "never stopped for him");
        Assert.True(walker.WalkTo(christof.Position, christof.Mind!.Walker.Node));
        Assert.True(walker.Waiting);
        bool waitedAgain = false;
        for (int k = 0; k < 30 && !walker.Detouring; k++)
        {
            bool before = walker.Waiting;
            sim.Step(1f / 60f);
            waitedAgain |= !before && walker.Waiting && ReferenceEquals(walker.BlockedBy, christof);
        }
        Assert.True(walker.Detouring, "did not step round him");
        Assert.False(waitedAgain);
    }

    /// <summary>
    /// The second question from the same play-test: clicked a few times while hidden,
    /// the Cappadocian gave no feed and no animation. **A click is never a feed in 1.1.**
    /// The cursor's verb on a character that is alive, not talk-to, not neutral and not a
    /// friend is the attack (2), whatever the one clicking is doing or hiding behind — the verb looks
    /// at the target's flags and the team test and nothing else — and the pointer still offers her,
    /// since it is Christof who is hidden, not she (the sight test asks her stealth against his perception).
    /// </summary>
    [Fact]
    public void A_hidden_Christof_clicking_a_Cappadocian_attacks_her_and_does_not_feed()
    {
        var (_, _, _, christof, cappadocian) = Stair();
        christof.Actor!.Stealth = 3;
        Assert.Equal(CursorVerb.Attack, Cursor.Verb(christof, cappadocian));
        Assert.True(Vitomir.Engine.Ui.Highlight.Offers(christof, cappadocian));
    }

    /// <summary>
    /// **Feeding is the Feed discipline** (pick it — F — and right-click her; the shipped
    /// `DisciplineFeed` asks only `NOFEED` and the ghoul rule, then the `Feed` native gives the order
    /// the feed order), and hiding changes none of it: Christof walks up (ACTION 7) and bites.
    /// She is armed, awake, hostile and not mesmerised, so **she resists**: his chance is 20 a level of
    /// Feed plus his Wits less hers, held to 5..95 — with Feed at level 0, as in the save, that is his
    /// Wits less hers — and whether or not the bite lands she is told she was attacked and
    /// takes him as her target. A failed bite plays `feedfailed`, which is the animation
    /// there is to see; one that lands plays the embrace. And once he is feeding his stealth comes down
    /// by five, so a feeding Christof is in plain sight.
    /// </summary>
    [Fact]
    public void A_hidden_Christof_feeds_only_through_Feed_and_an_armed_Cappadocian_resists_the_bite()
    {
        var (_, minds, sim, christof, cappadocian) = Stair();
        var me = christof.Actor!;
        me.Stealth = 3;
        me.Type = ActorType.Vampire;
        Assert.Equal(4, Feeding.Order(christof, cappadocian));
        Assert.Equal(ActionMode.ApproachFeed, me.Action);
        for (int i = 0; i < 180 && me.Action == ActionMode.ApproachFeed; i++) sim.Step(1f / 60f);

        // Told either way: she has him now, hidden or not.
        Assert.Same(christof, cappadocian.Mind!.Target);
        string? act = christof.Animation?.Action?.Mode.Name;
        if (me.Action == ActionMode.Feeding)
        {
            Assert.Equal(0, Sight.StealthOf(christof, moving: false));
            Assert.True(Sight.Notices(cappadocian, christof, targetMoving: false));
        }
        else Assert.Equal("feedfailed", act, ignoreCase: true);
    }
}

