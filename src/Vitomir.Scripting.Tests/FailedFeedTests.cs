// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **The failed bite, down the player's own path**. In play the
/// attempt fired, but the lunge animation did not play. An earlier build had seen `feedfailed` from the
/// harness's `--cast`, which calls the cast order itself; these go the way a right-click on her goes —
/// Feed selected, the right button on a thing (<see cref="CastInput.OnThing"/>), the cast
/// order, the shipped `DisciplineFeed`'s `cancast` and `cast`, its `Feed` native (the feed
/// order), the bite — with the victim's mind running, so that her answer
/// to being bitten (she takes him as her target and swings) happens while the lunge plays.
/// </summary>
[Collection("scripts")]
public class FailedFeedTests(Scripts s)
{
    sealed class Scene : IDisposable
    {
        public required GameFiles Files { get; init; }
        public required Location Place { get; init; }
        public required CodexScenes Scenes { get; init; }
        public required Simulation Sim { get; init; }
        public required WorldThing Christof { get; init; }
        public required WorldThing Cappadocian { get; init; }
        public required DisciplineDefinitions Powers { get; init; }

        public void Dispose()
        {
            Scenes.Leave();
            Files.Dispose();
        }
    }

    /// <summary>
    /// The monastery's stair with Christof, a vampire with Feed at level 0 and Feed selected, and one
    /// Cappadocian beside him, armed and hostile — the rest of the scene's characters taken away.
    /// His Wits are 0 and hers 100, so his chance is the floor, 5 in 100 (the chance's clamp).
    /// </summary>
    Scene Enter()
    {
        s.Ready();
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var powers = new DisciplineDefinitions(files);
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), powers);
        var christof = place.Player!.Thing;
        var cappadocian = minds.All.First(m => m.Thing.Template.Equals("cappadocian", StringComparison.OrdinalIgnoreCase)).Thing;
        foreach (var m in minds.All)
            if (m.Thing != cappadocian && m.Thing != christof) m.Thing.Flags |= WorldThing.Flag.Remove;
        var me = christof.Actor!;
        me.Type = ActorType.Vampire;
        me.Stat[(int)ActorStat.BloodPool] = 110f;
        me.Stat[(int)ActorStat.Blood] = 20f;
        me.Stat[(int)ActorStat.Wits] = 0f;
        cappadocian.Actor!.Stat[(int)ActorStat.Wits] = 100f;
        me.Disciplines.Current = DisciplineBook.SetLevel(christof, powers, "Feed", 0);
        var camera = new WorldCamera();
        camera.Start(christof);
        var talk = new Conversations();
        var scenes = new CodexScenes(s.Runtime!)
        {
            Chronicles = set, Camera = camera, Talk = talk, Nls = new Vitomir.Engine.Ui.Strings(files).Find,
        };
        scenes.Enter(place);
        var sim = new Simulation(place) { Scripts = scenes, Camera = camera, Talk = talk };
        sim.Step(1f / 60f);                                   // the removals go
        // Placed once the scene's own entry has run, which puts the party at its entrance.
        place.Move(christof, new Vector3(-826f, -590f, -252f));
        place.Player!.Ground();
        place.Move(cappadocian, new Vector3(-826f + 50f, -590f, -252f));
        cappadocian.Mind!.Walker.Ground();
        christof.Face(cappadocian);
        return new Scene
        {
            Files = files, Place = place, Scenes = scenes, Sim = sim, Christof = christof, Cappadocian = cappadocian, Powers = powers,
        };
    }

    /// <summary>
    /// **A right-click with Feed on an armed Cappadocian who resists plays the lunge, whole.** The
    /// bite fails: Christof stops, she is told, and `feedfailed` (mode 236) plays on his
    /// action channel at thirty frames a second and keeps him busy for its length — about 1.4 s, as
    /// in 1.1 (1.43 s). Stepped at a player's sixty frames a second, nothing
    /// takes the channel from it before its end: not her swings (a busy character does not flinch,
    /// the flinch), not a second right-click (the cast order's busy test), not the walk.
    /// A bite that lands instead plays the embrace, so the test tries again until one fails
    /// (at 5 in 100, the first almost always does).
    /// </summary>
    [Fact]
    public void A_right_click_feed_that_the_Cappadocian_resists_plays_the_whole_lunge()
    {
        using var m = Enter();
        var me = m.Christof.Actor!;
        Assert.NotNull(m.Cappadocian.Actor!.Weapon);                    // armed: she resists
        bool failed = false;
        for (int attempt = 0; attempt < 5 && !failed; attempt++)
        {
            for (int i = 0; i < 180 && m.Place.Milliseconds < me.BusyUntil; i++) m.Sim.Step(1f / 60f);
            if (me.Action is ActionMode.Feeding or ActionMode.FedOn) break;
            int answer = CastInput.OnThing(m.Christof, m.Cappadocian, InputModifier.None);
            Assert.Equal((int)OrderResult.Started, answer);
            // The cast fires, the script feeds, the order walks the few units and bites.
            for (int i = 0; i < 60 && m.Christof.Animation?.Action?.Mode.Name is not ("feedfailed" or "feed"); i++)
                m.Sim.Step(1f / 60f);
            failed = m.Christof.Animation?.Action?.Mode.Name == "feedfailed";
        }
        Assert.True(failed, "no bite failed in five tries at 5 in 100");

        int from = m.Place.Milliseconds, until = me.BusyUntil;
        Assert.InRange(until - from, 1300, 1500);                      // the lunge and the recoil, 1.4 s
        Assert.Same(m.Christof, m.Cappadocian.Mind!.Target);           // she was told
        bool clickedAgain = false;
        while (m.Place.Milliseconds < until - 20)
        {
            Assert.Equal("feedfailed", m.Christof.Animation?.Action?.Mode.Name);
            // A second right-click during it is only refused.
            if (!clickedAgain && m.Place.Milliseconds - from > 500)
            {
                Assert.Equal((int)OrderResult.Busy, CastInput.OnThing(m.Christof, m.Cappadocian, InputModifier.None));
                clickedAgain = true;
            }
            m.Sim.Step(1f / 60f);
        }
        Assert.True(clickedAgain);
    }

    /// <summary>
    /// **Hidden, the same lunge — and he stays half seen through it** (<i>"It
    /// was when Christof was partially cloaked that the animation wouldn't fire"</i>, and again from
    /// `SP3` with `sethide 3`). Nothing on the path asks about stealth — the right button,
    /// the cast order, `DisciplineFeed`, the feed order, the bite —
    /// so the bite fails as often and `feedfailed` plays as long. And the failed bite
    /// cancels his action: ACTION 0, so the stealth test takes nothing off his stealth (only
    /// feeding, a swing or a cast still playing do) and the stealth fade keeps him at
    /// half, the viewer seeing himself, from the first frame of the lunge to the last. 1.1 does the
    /// same: a run of 1.1 (Monastery 1, stealth 10 under Cloak of Shadows
    /// and Cloak the Gathering) has three failed bites in a row, `chrisfeedfail` for 1.43 s each.
    /// </summary>
    [Fact]
    public void Hidden_a_failed_bite_plays_the_whole_lunge_and_he_stays_half_seen()
    {
        using var m = Enter();
        var me = m.Christof.Actor!;
        me.Stealth = 3;
        bool failed = false;
        for (int attempt = 0; attempt < 5 && !failed; attempt++)
        {
            for (int i = 0; i < 180 && m.Place.Milliseconds < me.BusyUntil; i++) m.Sim.Step(1f / 60f);
            if (me.Action is ActionMode.Feeding or ActionMode.FedOn) break;
            Assert.Equal((int)OrderResult.Started, CastInput.OnThing(m.Christof, m.Cappadocian, InputModifier.None));
            for (int i = 0; i < 60 && m.Christof.Animation?.Action?.Mode.Name is not ("feedfailed" or "feed"); i++)
                m.Sim.Step(1f / 60f);
            failed = m.Christof.Animation?.Action?.Mode.Name == "feedfailed";
        }
        Assert.True(failed, "no bite failed in five tries at 5 in 100");

        int from = m.Place.Milliseconds, until = me.BusyUntil;
        Assert.InRange(until - from, 1300, 1500);                      // as long as when he is seen
        var channel = m.Christof.Animation!.Action!;
        while (m.Place.Milliseconds < until - 20)
        {
            Assert.Same(channel, m.Christof.Animation!.Action);          // one lunge, never cut or restarted
            Assert.Equal(ActionMode.None, me.Action);
            Assert.Equal(3, Sight.StealthOf(m.Christof, moving: false));
            Assert.Equal(StealthFade.Half, StealthFade.Of(m.Christof, m.Christof));
            m.Sim.Step(1f / 60f);
        }
        Assert.True(channel.Player.Frame > channel.Player.Animation.Duration * 0.9f, "the lunge did not play to its end");
    }

    /// <summary>
    /// **A failed bite writes nothing in the message box — in 1.1 either.** The feed order,
    /// the bite and the failed bite add nothing to the box,
    /// and the shipped `DisciplineFeed` prints nothing in `cancast` or `cast` — it
    /// has no fizzle roll and no `DisplayCast`. What 1.1 gives a refused feed is a voice
    /// (`vox_cannotfeed`, `vox_sated`), and a cast with too little blood `vox_notenoughblood`.
    /// </summary>
    [Fact]
    public void A_failed_bite_says_nothing_in_the_message_box()
    {
        using var m = Enter();
        var session = new Vitomir.Engine.Session { Here = m.Place };
        m.Place.Session = session;
        var me = m.Christof.Actor!;
        Assert.Equal((int)OrderResult.Started, CastInput.OnThing(m.Christof, m.Cappadocian, InputModifier.None));
        bool bit = false;
        for (int i = 0; i < 90 && !bit; i++)
        {
            m.Sim.Step(1f / 60f);
            bit = me.Action is ActionMode.Feeding || m.Christof.Animation?.Action?.Mode.Name == "feedfailed";
        }
        Assert.True(bit);
        for (int i = 0; i < 90; i++) m.Sim.Step(1f / 60f);
        Assert.DoesNotContain(session.Messages.Lines, l => l.Text.Contains("Feed", StringComparison.OrdinalIgnoreCase));
    }
}
