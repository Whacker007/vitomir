// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;
using Gait = Vitomir.Platform.Bodies.Gait;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// (Confirmed in play: <i>"the hands clip into the walls and sometimes float over them"</i>): the hands of the vaults, climbs
/// and mantles put on the real top by the arms' IK while their clips have them down (<see cref="PersonHandsRule"/>). Measured: each hand's knuckles
/// (the middle finger's first joint) against the real face and top through its contact, at the low, middle and high end of each clip's band, against
/// the same clip at its own height (where the clip lays the hand on its own top), both bodies, with the IK and without; and the largest
/// change a tick of the hands' change a tick about the IK's ease in and out (no pop). Skips without the models repository's files, or.
/// </summary>
public sealed class HandsOnTopsTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.00") => v.ToString(format, CultureInfo.InvariantCulture);

    static OverPaths Paths(CharacterAssets assets, int kind)
    {
        var paths = assets.Kind(kind).Person!.Over?.Paths;
        Assert.SkipUnless(paths is not null && assets.Kind(kind).Person!.Air is not null, "the models repository's files are too old (no moves over and onto things)");
        return paths!;
    }

    static (World World, EntityId Body) Make(CharacterAssets assets, int kind, PersonHandsSettings hands, Vector3 at, (Vector3 Min, Vector3 Max) block)
    {
        var t = new TestLevel().Floor(-40, -40, 200, 40, cell: 4f);
        t.Box(block.Min, block.Max);
        var level = new LevelSpace("floor", t.Build("floor").Collision) { Places = LevelPlaces.None };
        var b = new WorldBuilder(5);
        MovementServices.Register(b, level);
        StartStopSprings.Register(b, new StartStopSprings { Default = 0.2f });
        StartStopSprings.RegisterPlayer(b);
        b.Animation(assets, starts: new PersonStartSettings(), air: new PersonAirSettings(), over: new PersonOverSettings());
        b.Service(hands);
        BodyMotionSystem.Register(b);
        var w = b.Build();
        var e = MovementServices.SpawnControlled(w, at + new Vector3(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        w.Remove<Controlled>(e);
        w.Set(e, CharacterAnimation.For(assets, kind));
        w.Set(e, MotionInput.Standing);
        if (w.TryStore<PersonStep>(out _)) w.Set(e, new PersonStep { Curve = 1 });
        if (w.TryStore<PersonAir>(out _)) w.Set(e, new PersonAir { Curve = 1 });
        if (w.TryStore<PersonOver>(out _)) w.Set(e, new PersonOver());
        return (w, e);
    }

    /// <summary>A tick of a move: its clip and time, and both hands' knuckles in the world as drawn.</summary>
    sealed record Row(string Clip, float Time, TraversalKind Move, Vector3 Left, Vector3 Right, Vector3 Pelvis);

    /// <summary>A move made going +X at a block: a vault from a run tapped 2 m short (a thin wall at x 8), else a tap standing 0.55 m short of a deep block at x 1.</summary>
    static List<Row> Made(CharacterAssets assets, int kind, OverClip clip, float h, PersonHandsSettings hands, out float face)
    {
        bool vault = clip.Kind == TraversalKind.Vault;
        face = vault ? 8f : 1f;
        var block = vault ? (new Vector3(8f, -3f, 0f), new Vector3(8.25f, 3f, h)) : (new Vector3(1f, -3f, 0f), new Vector3(4f, 3f, h));
        var (w, e) = Make(assets, kind, hands, vault ? Vector3.Zero : new Vector3(0.45f, 0f, 0f), block);
        var rig = assets.Kind(kind).Person!.Rig;
        for (int i = 0; i < 20; i++) { Ask(w, e, 0f, Gait.Walk, false); w.Step(); }
        var rows = new List<Row>();
        bool pressed = false;
        for (int i = 0; i < 600; i++)
        {
            bool press = !pressed && (!vault || w.Get<Body>(e).Feet.X >= 6f);
            pressed |= press;
            Ask(w, e, vault ? 1f : 0f, vault ? Gait.Run : Gait.Walk, press);
            w.Step();
            var a = w.Get<CharacterAnimation>(e);
            rows.Add(new Row(assets.Clip(a.Clip).Name, a.Time, w.Get<Traversal>(e).Kind, Bone(w, e, assets, rig.LeftArm.Tip), Bone(w, e, assets, rig.RightArm.Tip),
                             Bone(w, e, assets, rig.Pelvis)));
            if (rows.Count > 2 && rows[^2].Move != TraversalKind.None && rows[^1].Move == TraversalKind.None) break;
        }
        Assert.True(Finite(w, e));
        Assert.Contains(rows, r => r.Clip == clip.Name);
        return rows;
    }

    static void Ask(World w, EntityId e, float share, Gait gait, bool jump)
    {
        ref var intent = ref w.Get<MoveIntent>(e);
        intent.Direction = new Vector2(share, 0f);
        intent.Gait = gait;
        intent.Faces = true;
        intent.Facing = 0f;
        intent.Jump = jump;
        intent.JumpHeld = jump;
    }

    /// <summary>A hand's knuckles against the face and the top (forward past the face, up over the top, m) at each tick of its clip's contact.</summary>
    static List<(float Time, Vector2 At)> Down(List<Row> rows, OverClip clip, OverHand hand, float face, float top) =>
        [.. rows.Where(r => r.Clip == clip.Name && r.Move != TraversalKind.None && r.Time >= hand.ContactS && r.Time <= hand.ReleaseS)
                .Select(r => (r.Time, new Vector2((hand.Left ? r.Left : r.Right).X - face, (hand.Left ? r.Left : r.Right).Z - top)))];

    /// <summary>The largest change a tick of a hand's change a tick (m) through the move.</summary>
    static float Pop(List<Row> rows, Func<Row, Vector3> of)
    {
        float pop = 0f;
        for (int i = 2; i < rows.Count; i++)
        {
            if (rows[i].Move == TraversalKind.None || rows[i - 2].Move == TraversalKind.None) continue;
            Vector3 P(int k) => of(rows[k]);
            pop = MathF.Max(pop, ((P(i) - P(i - 1)) - (P(i - 1) - P(i - 2))).Length());
        }
        return pop;
    }

    [Fact]
    public void The_hands_lie_on_the_real_top_within_a_centimetre_at_every_height_of_each_band_and_come_onto_it_without_a_pop()
    {
        foreach (var body in new[] { "man", "woman" })
        {
            var (assets, kind, _) = body == "man" ? Player() : Woman();
            foreach (var clip in Paths(assets, kind).Clips.Where(c => c.Hands.Length > 0 && !c.RunOnly))   // the low vaults are LowVaultTests'
            {
                // The clip at its own height with the IK: its hands where the clip lays them on its own top (the reference; without the IK the capsule's
                // path there ends a little off its own top, the climbs' by what their own end stands over it).
                var own = Made(assets, kind, clip, clip.TopM, new PersonHandsSettings(), out float face);
                float worstOff = 0f, worstOn = 0f, popOff = 0f, popOn = 0f, pelvisOff = 0f, pelvisOn = 0f;
                foreach (float h in new[] { clip.Low + 0.02f, 0.5f * (clip.Low + clip.High), clip.High - 0.02f })
                {
                    var off = Made(assets, kind, clip, h, PersonHandsSettings.Off, out _);
                    var on = Made(assets, kind, clip, h, new PersonHandsSettings(), out _);
                    foreach (var hand in clip.Hands)
                    {
                        var reference = Down(own, clip, hand, face, clip.TopM);
                        var a = Down(off, clip, hand, face, h);
                        var b = Down(on, clip, hand, face, h);
                        Assert.NotEmpty(reference);
                        Vector2 Ref(float t) => reference.MinBy(r => MathF.Abs(r.Time - t)).At;
                        float errOff = a.Max(r => (r.At - Ref(r.Time)).Length()), errOn = b.Max(r => (r.At - Ref(r.Time)).Length());
                        var mid = b[b.Count / 2].At;
                        float lowest = b.Min(r => r.At.Y), lowestOwn = reference.Min(r => r.At.Y);
                        output.WriteLine($"{body} {clip.Name} at {F(h)} m, the {(hand.Left ? "left" : "right")} hand down {F(hand.ContactS, "0.000")}-{F(hand.ReleaseS, "0.000")} s: "
                                         + $"its knuckles {F(mid.X * 100f, "0.0")} cm past the face, {F(mid.Y * 100f, "0.0")} cm over the top (at its own height {F(Ref(b[b.Count / 2].Time).X * 100f, "0.0")}, "
                                         + $"{F(Ref(b[b.Count / 2].Time).Y * 100f, "0.0")}), at their lowest {F(lowest * 100f, "0.0")} cm over it (its own {F(lowestOwn * 100f, "0.0")}); off the clip's own by at most {F(errOff * 100f, "0.0")} cm as off, {F(errOn * 100f, "0.0")} cm with the IK");
                        worstOff = MathF.Max(worstOff, errOff);
                        worstOn = MathF.Max(worstOn, errOn);
                        Assert.InRange(errOn, 0f, 0.01f);
                    }
                    popOff = MathF.Max(popOff, MathF.Max(Pop(off, r => r.Left), Pop(off, r => r.Right)));
                    popOn = MathF.Max(popOn, MathF.Max(Pop(on, r => r.Left), Pop(on, r => r.Right)));
                    pelvisOff = MathF.Max(pelvisOff, Pop(off, r => r.Pelvis));
                    pelvisOn = MathF.Max(pelvisOn, Pop(on, r => r.Pelvis));
                }
                output.WriteLine($"{body} {clip.Name}: the hands off the clip's own at most {F(worstOff * 100f, "0.0")} cm as off, {F(worstOn * 100f, "0.0")} cm with the IK; "
                                 + $"the hands' largest pop {F(popOff * 1000f, "0.0")} mm as off, {F(popOn * 1000f, "0.0")} mm with the IK; the pelvis's {F(pelvisOff * 1000f, "0.0")} and {F(pelvisOn * 1000f, "0.0")} mm");
                Assert.InRange(popOn, 0f, MathF.Max(0.02f, 1.5f * popOff));
                Assert.InRange(pelvisOn, 0f, MathF.Max(0.01f, 1.5f * pelvisOff));
            }
        }
    }

    [Fact]
    public void The_weight_eases_in_before_the_contact_and_out_after_the_release_and_the_note_round_trips()
    {
        Assert.Equal(0f, PersonHandsRule.Weight(0.1f, 0.3f, 0.5f, 0.1f));
        Assert.Equal(1f, PersonHandsRule.Weight(0.3f, 0.3f, 0.5f, 0.1f));
        Assert.Equal(1f, PersonHandsRule.Weight(0.5f, 0.3f, 0.5f, 0.1f));
        Assert.InRange(PersonHandsRule.Weight(0.25f, 0.3f, 0.5f, 0.1f), 0.49f, 0.51f);
        Assert.InRange(PersonHandsRule.Weight(0.55f, 0.3f, 0.5f, 0.1f), 0.49f, 0.51f);
        Assert.Equal(0f, PersonHandsRule.Weight(0.6f, 0.3f, 0.5f, 0.1f));
        var tuned = new PersonHandsSettings { EaseSeconds = 0.08f };
        Assert.Equal(tuned, new PersonHandsSettings().WithNote(tuned.Note()));
        Assert.Equal(PersonHandsSettings.Off, new PersonHandsSettings().WithNote(null));
        Assert.Equal(PersonHandsSettings.Off, new PersonHandsSettings().WithNote("off"));
        Assert.False(PersonHandsSettings.Off.Any);
    }
}
