// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The party portraits on the status bar — the half of it that is not the GPU.
//
// **A head is a model, not a picture.** `vuiHeads::Draw` has two complete draw paths and
// the textured-quad one is **dead code in retail 1.1**: its branch tests bit 0x200 of a
// global whose image value is zero and whose 27 `.text` references are all single-bit ops on other
// bits. The live path renders each party member's own `.nod` head model, animated by its own `.nms`
// motion set, into a small perspective viewport.
//
// This file is the part that can be pinned by test without a GPU: which heads there are, where each
// one sits, and which motion mode its owner's state calls for. `Vitomir.Render` draws it.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>Which head animation a character's state calls for (the engine's own mode names).</summary>
public enum HeadMode
{
    /// <summary>No head: the character names no model, or the model does not ship.</summary>
    None = 0,
    /// <summary>`head_idle` — the resting state.</summary>
    Idle = 1,
    /// <summary>`head_hurt` — the resting state while `THING_AF2_LIMPING` is up.</summary>
    Hurt = 2,
    /// <summary>`head_torpor` — dead. `noloop endpause`, so it holds on the last frame.</summary>
    Torpor = 3,
    /// <summary>`head_feed` — fired by the feed action.</summary>
    Feed = 4,
    /// <summary>`head_staked` — `THING_AF_STAKED`. `noloop endpause`.</summary>
    Staked = 5,
    /// <summary>`head_frenzy` — frenzied.</summary>
    Frenzy = 6,
    /// <summary>`head_impact` — fired when hit. `noloop`.</summary>
    Impact = 7,
    /// <summary>`head_bite` — fired by the bite action. `noloop`.</summary>
    Bite = 8,
}

/// <summary>One head to draw: whose it is, which model and motion, where, and doing what.</summary>
/// <param name="Who">The party member.</param>
/// <param name="Model">
/// The head model to draw: its <c>ACTOR_HEADMODEL</c> (tag 57), or the armour's head standing in for
/// it (<see cref="Actor.PortraitModel"/>).
/// </param>
/// <param name="MotionSet">Its <c>ACTOR_HEADMOTION</c> (tag 58).</param>
/// <param name="OffsetX">Where it goes along the row, in the viewport's own world units.</param>
/// <param name="Mode">Which motion mode its state calls for.</param>
/// <param name="Own">Its own <c>ACTOR_HEADMODEL</c>, drawn when <paramref name="Model"/> is a stand-in that does not load.</param>
public readonly record struct PartyHead(WorldThing Who, string Model, string MotionSet,
                                        float OffsetX, HeadMode Mode, string Own = "");

/// <summary>The party heads, as the status bar decides them.</summary>
public static class PartyHeads
{
    /// <summary>`MAX_PARTY_SIZE`, from the bounds assert the draw loop carries (`rpg/rpgParty.h:0x33`).</summary>
    public const int MaxParty = 4;

    /// <summary>The gap between two heads along the row.</summary>
    public const float Spacing = 2.2f;

    /// <summary>
    /// The viewport, in the interface's own 640×480 space with **y down the screen**, from
    /// The canvas's single-player case. It is exactly `ui/descLargeSP.nui`'s rectangle, which is why
    /// the large description box covers the heads rather than replacing them.
    /// </summary>
    /// <remarks>
    /// The engine stores the rectangle as its four edges — left 160, top 390, right 480, bottom
    /// 478 — and treats both edges as inside it, so the spans it computes from are
    /// <see cref="ViewW"/> + 1 and <see cref="ViewH"/> + 1. <see cref="Scales"/> adds
    /// the one back.
    /// </remarks>
    public const int ViewX = 160, ViewY = 390, ViewW = 320, ViewH = 88;

    /// <summary>
    /// The head camera (given 30, 0, 5, 1000, 1 for the single-player bar).
    ///
    /// **<see cref="Fov"/> is the HORIZONTAL field of view, and the aspect argument is dead.**
    /// Both facts are read, not assumed, out of the function that actually builds the
    /// projection, reached from `SetCanvas`, which first copies the canvas rectangle
    /// into the camera's viewport rectangle unchanged. See <see cref="Scales"/> for the formula.
    /// The 1.0 written to the camera's aspect field is never read back by anything, so it is not
    /// kept here.
    /// </summary>
    public const float Fov = 30f, Near = 5f, Far = 1000f;

    /// <summary>
    /// The projection's x and y scales, exactly as the engine computes them from the viewport
    /// rectangle. <paramref name="width"/> and <paramref name="height"/> are the rectangle's
    /// *extents* — right − left and bottom − top — which for the status bar are
    /// <see cref="ViewW"/> and <see cref="ViewH"/>.
    ///
    /// <code>
    /// halfW = (width  + 1) × 0.5            ; the rectangle is inclusive of both edges
    /// halfH = (height + 1) × 0.5
    /// d     = halfW / tan(fov / 2)         ; **width only** — this is the whole trick
    /// xScale = d / halfW,  yScale = d / halfH
    /// </code>
    ///
    /// Reusing the width-derived <c>d</c> on both axes is what folds the rectangle's aspect into
    /// the matrix: <c>yScale / xScale</c> is exactly <c>halfW / halfH</c>. And because the screen
    /// mapping then multiplies by halfW and halfH again, the pixels-per-unit comes out as <c>d</c>
    /// on *both* axes for any rectangle — so a head is never stretched, whatever the shape of
    /// the box. The <c>+1</c>s are the engine's (constants 1.0 and 0.5), which is
    /// why the ratio is 321/89 and not 320/88.
    ///
    /// The consequence that matters to the eye: with a 30° *horizontal* fov in a box 3.6 times
    /// wider than it is tall, the **vertical** field of view is only 8.5°. Treating the 30° as
    /// vertical — which is what a stock perspective helper assumes — draws the head about
    /// three and a half times too small.
    ///
    /// Corroboration from the engine's own other layouts: the canvas's two multiplayer modes use a
    /// box 80 wide instead of 320 and a fov of 7.5° instead of 30° — a quarter of the width
    /// paired with a quarter of the angle, with the height left at 88. That only keeps the heads
    /// the same size on screen if the fov is horizontal, and by this formula it does: 618 pixels
    /// per unit against 599.
    /// </summary>
    public static (float X, float Y) Scales(float width, float height, float fov = Fov)
    {
        float halfW = (width + 1f) * 0.5f, halfH = (height + 1f) * 0.5f;
        float d = halfW / MathF.Tan(fov * MathF.PI / 360f);
        return (d / halfW, d / halfH);
    }

    /// <summary>
    /// The same projection expressed the way a stock right-handed perspective helper wants it:
    /// a *vertical* field of view in radians and an aspect. Identical x and y scales to
    /// <see cref="Scales"/>; only the depth convention differs, and one head in an empty viewport
    /// cannot tell.
    /// </summary>
    public static (float VerticalFov, float Aspect) Camera(float width, float height, float fov = Fov)
    {
        var (x, y) = Scales(width, height, fov);
        return (2f * MathF.Atan(1f / y), y / x);
    }

    /// <summary>The head canvas's three layouts (`newMode`): the single-player bar's, and two 80 wide.</summary>
    public const int SinglePlayer = 0, Narrow = 1, Advancement = 2;

    /// <summary>
    /// The canvas for each of the three modes, in the 640 x 480 space with y down, and its
    /// horizontal lens: mode 0 is the bar's hole (160,390 to 480,478, 30°); mode 1 is 9,360 to
    /// 89,448 and mode 2 is **72,360 to 152,448**, both at 7.5° — a quarter of the width at a
    /// quarter of the angle. **Mode 2 is the advancement screen's** (which sets mode 2),
    /// the black square at the left of `statusBarADV`, and while it is up the
    /// heads' draw puts one head there and returns: the character being advanced,
    /// at the canvas's middle (448).
    /// </summary>
    public static (int X, int Y, int W, int H, float Fov) Canvas(int mode) => mode switch
    {
        Narrow => (9, 360, 80, 88, 7.5f),                // `statusBarMP`'s one head
        Advancement => (72, 360, 80, 88, 7.5f),
        _ => (ViewX, ViewY, ViewW, ViewH, Fov),
    };

    /// <summary>Where the head camera stands, and the half-turn it looks back along.</summary>
    public static readonly System.Numerics.Vector3 Eye = new(0f, 17f, 1.35f);
    /// <summary>The camera's Euler angles in degrees — a half turn about Z and nothing else.</summary>
    public static readonly System.Numerics.Vector3 Facing = new(0f, 0f, 180f);

    /// <summary>
    /// The two lights the head viewport carries, cleared and re-added for every head (written
    /// with the canvas, 0x48 bytes a light): a warm key from the left and a cool fill
    /// from the right. Nothing else lights a head — the level's own lights are not in this viewport.
    /// </summary>
    public static readonly (System.Numerics.Vector3 At, uint Colour, float Range)[] Lights =
    [
        (new(-35f, 26f, 51f), 0xFFFFE2AD, 500f),   // 255, 226, 173
        (new(26f, 28f, -28f), 0xFFBFE2FF, 200f),   // 191, 226, 255
    ];

    /// <summary>
    /// Which motion mode a character's state calls for, in the engine's own precedence
    /// (then the resting state).
    ///
    /// **Dead beats staked beats frenzied**, and anything else is the resting pair — `head_hurt`
    /// while `THING_AF2_LIMPING` is up, `head_idle` otherwise. The one-shots (`head_bite`,
    /// `head_feed`, `head_impact`) are not in here because they are not states: the action that
    /// causes them plays them and the head falls back to this afterwards.
    /// </summary>
    public static HeadMode ModeFor(Actor? who)
    {
        if (who is null) return HeadMode.None;
        if (who.Has(Actor.Flag.Dead)) return HeadMode.Torpor;
        if (who.Has(Actor.Flag.Staked)) return HeadMode.Staked;
        if (who.HeadAction != HeadMode.None) return who.HeadAction;
        if (who.Has2(Actor.Flag2.PermaFrenzy)) return HeadMode.Frenzy;
        return who.Has2(Actor.Flag2.Limping) ? HeadMode.Hurt : HeadMode.Idle;
    }

    /// <summary>
    /// Whether a mode plays on the **motion** channel rather than over the top of it.
    ///
    /// The engine has two entry points and they do not use the same channel. The first is the
    /// resting pair — <c>head_idle</c> and <c>head_hurt</c> — and passes 1 for
    /// <c>bMotion</c>; everything else goes through the second, which passes 0 and lands on the
    /// action channel. That is what lets a bite or an impact play over a hurt idle and hand the
    /// head straight back afterwards without anyone restarting the idle.
    ///
    /// It also decides how often a mode may be asked for: the motion channel refuses to restart an
    /// animation it is already playing, so the resting pair can be asked for every
    /// frame, while the action channel always restarts and so must only be asked on a
    /// change of state.
    /// </summary>
    public static bool OnMotionChannel(HeadMode mode) => mode is HeadMode.Idle or HeadMode.Hurt;

    /// <summary>
    /// The head actions that play once and are over — `head_impact` and `head_bite`, `noloop` in
    /// every shipped head set and not held (`endpause` is only on `head_staked` and `head_torpor`).
    /// </summary>
    public static bool OneShot(HeadMode mode) => mode is HeadMode.Impact or HeadMode.Bite;

    /// <summary>
    /// **A one-shot has played out on the portrait**: the head goes back to its resting state. The
    /// engine keeps no memory of an impact — the action start begins `head_impact` on the head's action
    /// channel and the mixer drops the channel when a `noloop` mode runs out — so neither
    /// may Vitomir's <see cref="Actor.HeadAction"/>, or the next impact (the same mode again) would
    /// not play and the hurt face would never come back while the stale one outranked it. Only the
    /// mode that was playing is cleared: a bite that has moved on to `head_feed` is left alone.
    /// </summary>
    public static void Played(Actor? who, HeadMode mode)
    {
        if (who is not null && OneShot(mode) && who.HeadAction == mode) who.HeadAction = HeadMode.None;
    }

    /// <summary>The `.nms` mode name a <see cref="HeadMode"/> looks up.</summary>
    public static string NameOf(HeadMode mode) => mode switch
    {
        HeadMode.Idle => "head_idle",
        HeadMode.Hurt => "head_hurt",
        HeadMode.Torpor => "head_torpor",
        HeadMode.Feed => "head_feed",
        HeadMode.Staked => "head_staked",
        HeadMode.Frenzy => "head_frenzy",
        HeadMode.Impact => "head_impact",
        HeadMode.Bite => "head_bite",
        _ => "",
    };

    /// <summary>
    /// The heads to draw, in order, with the x offset each one sits at.
    ///
    /// **The offset only advances when a head actually draws**, which is the engine's own loop
    /// (the light pass answers 0 when the actor has no head model, and the caller only decrements the
    /// offset on a non-zero answer). So a party member with no head is skipped **and the gap lands
    /// at the end of the row, not in their slot** — everyone after them shifts one place across and
    /// stops lining up with their own status-bar row. That is reachable in shipped data:
    /// `HEADchristoph_da.nod` is named by `_player_MD` and `_player_MP` and is in no archive.
    ///
    /// The row is centred: the first offset is <c>(count − 1) × Spacing / 2</c> and each head after
    /// it steps <c>−Spacing</c>, where <c>count</c> is the **party size**, not the number of heads.
    /// </summary>
    public static List<PartyHead> Of(IReadOnlyList<WorldThing> party)
    {
        var heads = new List<PartyHead>();
        int count = Math.Min(party.Count, MaxParty);
        float offset = (count - 1) * 0.5f * Spacing;
        for (int i = 0; i < count; i++)
        {
            var thing = party[i];
            if (thing.Actor is not { } who) continue;
            if (who.HeadModel is not { Length: > 0 } model) continue;
            // The armour's head, when there is one, stands in for the head's own.
            string drawn = who.PortraitModel is { Length: > 0 } worn ? worn : model;
            heads.Add(new PartyHead(thing, drawn, who.HeadMotionSet, offset, ModeFor(who), model));
            offset -= Spacing;
        }
        return heads;
    }
}
