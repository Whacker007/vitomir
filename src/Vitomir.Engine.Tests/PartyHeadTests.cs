// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The party heads' arithmetic and state machine — everything but the GPU.
using Vitomir.Engine.Anim;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

public class PartyHeadTests
{
    static int next;
    static WorldThing Member(string model) => new()
    {
        Index = next, Guid = ++next, Scene = new Formats.NsdThing(), Template = "test",
        Type = ThingType.Player,
        Actor = new Actor { HeadModel = model, HeadMotionSet = model + ".nms" },
    };

    /// <summary>
    /// The row is centred on the **party size**, and each head steps one gap left of the last.
    /// A full party of four spans 3.3 to −3.3; a party of one sits dead centre.
    /// </summary>
    [Theory]
    [InlineData(1, 0f)]
    [InlineData(2, 1.1f)]
    [InlineData(3, 2.2f)]
    [InlineData(4, 3.3f)]
    public void The_row_is_centred_on_the_party_size(int size, float first)
    {
        var party = Enumerable.Range(0, size).Select(_ => Member("h.nod")).ToArray();
        var heads = PartyHeads.Of(party);

        Assert.Equal(size, heads.Count);
        Assert.Equal(first, heads[0].OffsetX, 4);
        Assert.Equal(-first, heads[^1].OffsetX, 4);
        for (int i = 1; i < heads.Count; i++)
            Assert.Equal(heads[i - 1].OffsetX - PartyHeads.Spacing, heads[i].OffsetX, 4);
    }

    /// <summary>
    /// **The gap lands at the end of the row, not in the missing member's slot.** The engine only
    /// advances the offset when a head actually drew (the head's draw answers 0 for an actor with no head
    /// model), so everybody after the gap shifts one place across and stops lining up with their own
    /// status row. It is reachable in shipped data — `HEADchristoph_da.nod` is in no archive.
    /// </summary>
    [Fact]
    public void A_member_with_no_head_shifts_everyone_after_it_and_leaves_the_gap_at_the_end()
    {
        WorldThing[] party = [Member("a.nod"), Member(""), Member("c.nod"), Member("d.nod")];

        var heads = PartyHeads.Of(party);

        Assert.Equal(3, heads.Count);
        // Four slots would be 3.3 / 1.1 / -1.1 / -3.3. The second member draws nothing, so the
        // third takes the second's place and the row ends one slot short of the right-hand end.
        Assert.Equal(3.3f, heads[0].OffsetX, 4);
        Assert.Equal(1.1f, heads[1].OffsetX, 4);
        Assert.Equal(-1.1f, heads[2].OffsetX, 4);
        Assert.DoesNotContain(heads, h => Math.Abs(h.OffsetX + 3.3f) < 0.001f);
    }

    /// <summary>Four slots, and a fifth member is not drawn at all (`MAX_PARTY_SIZE`).</summary>
    [Fact]
    public void Never_more_than_four()
    {
        var party = Enumerable.Range(0, 6).Select(_ => Member("h.nod")).ToArray();
        Assert.Equal(PartyHeads.MaxParty, PartyHeads.Of(party).Count);
    }

    /// <summary>
    /// The state precedence, which is 1.1's own order: **dead beats staked
    /// beats frenzied**, and everything else is the resting pair — hurt while
    /// `THING_AF2_LIMPING` is up, idle otherwise.
    /// </summary>
    [Fact]
    public void Dead_beats_staked_beats_frenzied_and_the_rest_is_idle_or_hurt()
    {
        var who = new Actor();
        Assert.Equal(HeadMode.Idle, PartyHeads.ModeFor(who));

        who.Flags2 |= Actor.Flag2.Limping;
        Assert.Equal(HeadMode.Hurt, PartyHeads.ModeFor(who));

        who.Flags2 |= Actor.Flag2.PermaFrenzy;
        Assert.Equal(HeadMode.Frenzy, PartyHeads.ModeFor(who));

        who.Flags |= Actor.Flag.Staked;
        Assert.Equal(HeadMode.Staked, PartyHeads.ModeFor(who));

        who.Flags |= Actor.Flag.Dead;
        Assert.Equal(HeadMode.Torpor, PartyHeads.ModeFor(who));

        Assert.Equal(HeadMode.None, PartyHeads.ModeFor(null));
    }

    /// <summary>Every mode this tree names is a mode the shipped motion sets actually declare.</summary>
    [Fact]
    public void Every_mode_name_is_one_the_shipped_head_motion_sets_declare()
    {
        using var files = Install.Open();
        var set = files.TryRead("3d/Motions/SerenaHead.nms", out var data)
            ? System.Text.Encoding.Latin1.GetString(data) : null;
        Assert.SkipWhen(set is null, "SerenaHead.nms is not in this install");

        foreach (HeadMode mode in Enum.GetValues<HeadMode>())
        {
            if (mode == HeadMode.None) continue;
            Assert.Contains(PartyHeads.NameOf(mode), set!, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The shipped cast really does name a head model and a motion set, and one of them really is
    /// missing — `HEADchristoph_da.nod` is named by the modern-day and multiplayer players and is
    /// in no archive, which is what makes the gap rule above reachable rather than theoretical.
    /// </summary>
    [Fact]
    public void Christof_has_a_head_and_the_modern_day_player_does_not()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);

        // The template names a bare file; models resolve under `3d/models/`, as everywhere else.
        var christof = Actor.From(templates.Resolve("Christof"));
        Assert.Equal("christofH.nod", christof.HeadModel, ignoreCase: true);
        Assert.Equal("ChristofDaHead.nms", christof.HeadMotionSet, ignoreCase: true);
        Assert.True(files.TryRead("3d/models/" + christof.HeadModel, out _), "christofH.nod should ship");

        var modern = Actor.From(templates.Resolve("_player_MD"));
        Assert.Equal("HEADchristoph_da.nod", modern.HeadModel, ignoreCase: true);
        Assert.False(files.TryRead("3d/models/" + modern.HeadModel, out _),
                     $"{modern.HeadModel} is expected to be missing from the install — it is what "
                     + "makes the gap rule reachable rather than theoretical");
    }

    /// <summary>
    /// The head camera's projection, against the numbers the projection computes for the status bar's own
    /// rectangle. This is the test that pins the framing: the fov is **horizontal**, so the scales
    /// come out 3.73 and 13.46, and reading the 30° as vertical instead would give 1.03 and 3.73 —
    /// a head three and a half times too small, which is what this tree drew before the framing
    /// was found.
    /// </summary>
    [Fact]
    public void The_head_camera_is_thirty_degrees_across_not_down()
    {
        var (x, y) = PartyHeads.Scales(PartyHeads.ViewW, PartyHeads.ViewH);

        // d = halfW / tan(15°) = 160.5 / 0.2679492 = 598.99, then d/halfW and d/halfH.
        Assert.Equal(3.7320509f, x, 4);
        Assert.Equal(13.460528f, y, 3);

        // The vertical field of view is only 8.5°, which is the whole surprise.
        var (fovY, aspect) = PartyHeads.Camera(PartyHeads.ViewW, PartyHeads.ViewH);
        Assert.Equal(8.4995f, fovY * 180f / MathF.PI, 2);

        // The aspect folded into the matrix is the rectangle's, counting both edges: 321/89.
        Assert.Equal(321f / 89f, aspect, 4);
        Assert.Equal(y / x, aspect, 5);
    }

    /// <summary>
    /// Pixels per unit is the same on both axes whatever the rectangle — which is why a head is
    /// never stretched, at any window size, and why the engine can use a 320-wide box and an
    /// 80-wide one without a second set of numbers.
    /// </summary>
    [Theory]
    [InlineData(320, 88)]      // the status bar
    [InlineData(80, 88)]       // the two multiplayer layouts
    [InlineData(640, 132)]     // the status bar at 1280x720
    [InlineData(1, 1)]
    public void A_head_is_never_stretched(int width, int height)
    {
        var (x, y) = PartyHeads.Scales(width, height);
        float perUnitX = x * (width + 1f) * 0.5f, perUnitY = y * (height + 1f) * 0.5f;
        Assert.Equal(perUnitX, perUnitY, 3);
    }

    /// <summary>
    /// The engine's two multiplayer layouts quarter the width and quarter the angle together
    /// 80 wide with a 7.5° fov against 320 wide with 30°). That only keeps the heads
    /// roughly the same size on screen if the fov is horizontal — so this is the framing's own
    /// corroboration, and it holds to within four per cent.
    /// </summary>
    [Fact]
    public void The_multiplayer_layouts_show_a_head_at_the_same_size()
    {
        static float PerUnit(float width, float fov) =>
            (width + 1f) * 0.5f / MathF.Tan(fov * MathF.PI / 360f);

        float bar = PerUnit(PartyHeads.ViewW, PartyHeads.Fov);   // 320 wide, 30°
        float multi = PerUnit(80f, 7.5f);
        Assert.Equal(598.99f, bar, 2);
        Assert.Equal(617.91f, multi, 2);
        Assert.True(MathF.Abs(multi - bar) / bar < 0.04f, $"{bar} vs {multi}");
    }

    /// <summary>
    /// Which channel each mode plays on. The resting pair goes through the play with `bMotion` 1;
    /// the six states and one-shots go through it with 0, which is why they can play over a
    /// hurt idle and hand the head straight back.
    /// </summary>
    [Theory]
    [InlineData(HeadMode.Idle, true)]
    [InlineData(HeadMode.Hurt, true)]
    [InlineData(HeadMode.Torpor, false)]
    [InlineData(HeadMode.Feed, false)]
    [InlineData(HeadMode.Staked, false)]
    [InlineData(HeadMode.Frenzy, false)]
    [InlineData(HeadMode.Impact, false)]
    [InlineData(HeadMode.Bite, false)]
    public void Only_the_resting_pair_plays_on_the_motion_channel(HeadMode mode, bool onMotion) =>
        Assert.Equal(onMotion, PartyHeads.OnMotionChannel(mode));

    /// <summary>
    /// **An impact is over when it has played** (a portrait that had been hit
    /// kept its impact as a state). Once the head's action channel is empty the one-shot comes off
    /// the actor, so the hurt face can show again and the next impact plays; a feed that has
    /// already moved on from its bite, and the states, are left alone.
    /// </summary>
    [Fact]
    public void A_one_shot_comes_off_the_head_once_it_has_played()
    {
        var a = new Actor { HeadAction = HeadMode.Impact, Flags2 = Actor.Flag2.Limping };
        Assert.Equal(HeadMode.Impact, PartyHeads.ModeFor(a));
        PartyHeads.Played(a, HeadMode.Impact);
        Assert.Equal(HeadMode.None, a.HeadAction);
        Assert.Equal(HeadMode.Hurt, PartyHeads.ModeFor(a));

        a.HeadAction = HeadMode.Feed;                                   // the bite has become the feed
        PartyHeads.Played(a, HeadMode.Bite);
        Assert.Equal(HeadMode.Feed, a.HeadAction);
        PartyHeads.Played(a, HeadMode.Feed);                            // a state is not a one-shot
        Assert.Equal(HeadMode.Feed, a.HeadAction);
    }

    /// <summary>
    /// Every head motion set the party can carry names all eight modes, and each one loops or stops
    /// the way the mode's own flags say — with exactly two gaps, both shipped, both harmless
    /// because the engine's lookup simply fails and nothing is played (the lookup answers −1, and
    /// the head carries on with whatever it had):
    /// <list type="bullet">
    /// <item>`ErikHead.nms` has its `head_hurt` line commented out, so a wounded Erik keeps
    /// playing his idle;</item>
    /// <item>`HEADwolf.nms` names `wolfHEAD_feed.nad`, which is **in no archive** — only
    /// `WolfHead_idle`, `WolfHead_bite` and `WolfHead_impact` ship — so a wolf's head does not
    /// change while it feeds.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void Every_party_head_set_names_its_modes_and_stops_where_it_should()
    {
        using var files = Install.Open();
        var motions = new MotionSets(files);
        string[] sets = ["ChristofDaHead.nms", "WilhemDaHead.nms", "SerenaHead.nms", "samuelHead.nms",
                         "ErikHead.nms", "pinkHead.nms", "HEADwolf.nms"];
        // `noloop` on the four that are events rather than states; `endpause` holds the last frame
        // on the two that are conditions you stay in.
        HeadMode[] stopping = [HeadMode.Torpor, HeadMode.Staked, HeadMode.Impact, HeadMode.Bite];
        HeadMode[] holding = [HeadMode.Torpor, HeadMode.Staked];

        foreach (var name in sets)
        {
            var set = motions.Set(name);
            Assert.NotNull(set);
            for (var mode = HeadMode.Idle; mode <= HeadMode.Bite; mode++)
            {
                var found = motions.Find(set!, PartyHeads.NameOf(mode));
                if (found is null)
                {
                    (string Set, HeadMode Mode)[] gaps =
                        [("ErikHead.nms", HeadMode.Hurt), ("HEADwolf.nms", HeadMode.Feed)];
                    Assert.True(gaps.Contains((name, mode)), $"{name} is missing {PartyHeads.NameOf(mode)}");
                    continue;
                }
                Assert.Equal(stopping.Contains(mode), found.Value.Mode.Has(NmsFlags.NoLoop));
                Assert.Equal(holding.Contains(mode), found.Value.Mode.Has(NmsFlags.EndPause));
            }
        }
    }

    /// <summary>
    /// The heads' three canvases: the bar's hole at 30°, and two 80 x 88 boxes at 7.5° — the
    /// second of which, 72,360 to 152,448, is the advancement screen's (it asks for mode 2).
    /// A head there is drawn at the bar's scale, give or take the engine's own inclusive edges.
    /// </summary>
    [Fact]
    public void The_advancement_screen_has_a_canvas_of_its_own_at_the_bars_scale()
    {
        Assert.Equal((PartyHeads.ViewX, PartyHeads.ViewY, PartyHeads.ViewW, PartyHeads.ViewH, PartyHeads.Fov),
                     PartyHeads.Canvas(PartyHeads.SinglePlayer));
        Assert.Equal((72, 360, 80, 88, 7.5f), PartyHeads.Canvas(PartyHeads.Advancement));
        Assert.Equal((9, 360, 80, 88, 7.5f), PartyHeads.Canvas(PartyHeads.Narrow));
        var (bar, _) = PartyHeads.Scales(PartyHeads.ViewW, PartyHeads.ViewH);
        var (adv, _) = PartyHeads.Scales(80f, 88f, 7.5f);
        // Pixels per unit is the scale times the half-width: 599 against 618.
        Assert.InRange(adv * 40.5f / (bar * 160.5f), 1.0f, 1.05f);
    }

    /// <summary>
    /// **The bite's lunge holds its last frame until the feed** (ours): the head's <c>head_bite</c> is shorter than the body's bite
    /// opening, and a finished one-shot hands the head back to its idle, so the portrait looked around for a moment between the lunge
    /// and the feed. Held from outside, the action channel stays on its last frame past the mode's end and is not handed back; the
    /// feed then takes it over.
    /// </summary>
    [Fact]
    public void A_held_bite_stays_on_its_last_frame_until_the_feed_takes_the_head_over()
    {
        using var files = Install.Open();
        var motions = new MotionSets(files);
        var model = NodModel.Read(files.Read("3d/models/ChrisHeadLeather.nod"), "ChrisHeadLeather.nod");
        var head = ThingAnimation.For(model, "ChristofDaHead.nms", motions, PartyHeads.NameOf(HeadMode.Idle))!;
        Assert.True(head.Play(PartyHeads.NameOf(HeadMode.Bite), motions, onMotion: false) >= 0);
        head.HoldActionAtEnd();
        float duration = head.Action!.Player.Animation.Duration;
        for (int i = 0; i < 300; i++) head.Advance(1f / 30f);                 // ten seconds: far past the lunge's end
        Assert.NotNull(head.Action);
        Assert.Equal(PartyHeads.NameOf(HeadMode.Bite), head.Action!.Mode.Name);
        Assert.InRange(head.Action.Player.Frame, duration - 1f, duration);
        Assert.True(head.Play(PartyHeads.NameOf(HeadMode.Feed), motions, onMotion: false) >= 0);
        Assert.Equal(PartyHeads.NameOf(HeadMode.Feed), head.Action!.Mode.Name);
    }
}
