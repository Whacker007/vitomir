// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.Anim;

/// <summary>
/// One character's animation: its model, the motion set its template names, and what it is playing
/// on each of its two channels. Posing is the same chain the sky uses — sample the tracks, build
/// the bones' world matrices, turn those into skin matrices, and push every vertex through its own
/// bone.
///
/// This is the engine's <c>renderKey</c> mixer (made on the
/// render model). A channel is <c>{ flags, mode, track, curFrame, rate, curBase[] }</c>; the maths that
/// moves it is one line — <c>curFrame += rate × seconds</c> — so **the rate is frames a
/// second**, and every gameplay caller passes 30 (see <see cref="Rate"/>).
///
/// **Two channels, as the engine has** (<c>PlayMotionSetMode(mode, bMotion, speed)</c>): the
/// *motion* channel is what the character is doing with itself — standing, walking, running — and
/// the *action* channel is what it is doing to the world, which plays over the top. An action that
/// has run out gives the character back to its motion. **Only one of the two moves at a time**:
/// while an action plays, the motion under it is not advanced at all, so a walk picks up
/// where it left off once a swing is over, and its footsteps are not heard during it.
///
/// **And a change of animation is faded, over a fifth of a second**: the pose the
/// character was last drawn in is kept, and each frame's new pose is drawn that much towards it,
/// the weight falling from 1 to 0 at five a second. It happens when the motion changes to another
/// track with no action over it, when an action starts, and when an action runs out — unless the
/// mode says <c>nofadein</c>. Without it an idle flourish snapped back into the stand:
/// the animation, when it ended, was jerky and abrupt.
///
/// The two channels are played by *different* functions and they do not behave the same way:
/// <list type="bullet">
/// <item>the motion channel **does not restart an animation that is already playing** —
/// it only refreshes the flags and returns. That is what lets the engine ask for <c>walk</c> on
/// every single frame of a walk, which is exactly what it does;</item>
/// <item>the action channel always restarts, so asking twice for a swing swings twice;
/// and it leaves the motion channel alone, so the character keeps walking underneath.</item>
/// </list>
/// </summary>
public sealed partial class ThingAnimation
{
    /// <summary>One channel: what it is playing there, how fast, and how far through.</summary>
    public sealed class Channel(NmsMode mode, AnimationPlayer player, NmsFlags flags)
    {
        public NmsMode Mode { get; internal set; } = mode;
        public AnimationPlayer Player { get; } = player;

        /// <summary>
        /// The mode's flags as the engine copies them onto the channel (the play writes the word
        /// straight through). A motion play of the same animation refreshes only this.
        /// </summary>
        public NmsFlags Flags { get; internal set; } = flags;

        /// <summary>
        /// The engine's internal "stopped at the end" bit (0x1000), set when an
        /// <see cref="NmsFlags.EndPause"/> animation reaches its duration. A channel holding this
        /// is not advanced again.
        /// </summary>
        public bool Held { get; internal set; }

        /// <summary>
        /// Ours: the channel plays to its end and then holds its last frame there, as a mode with <c>endpause</c> does, though its
        /// own mode does not ask to (<see cref="HoldActionAtEnd"/>).
        /// </summary>
        public bool HoldsAtEnd { get; internal set; }

        /// <summary>True once a non-looping animation has run off the end.</summary>
        public bool Finished => Held || (!Player.Loop && Player.Frame >= Player.Animation.Duration);

        /// <summary>Whether this channel's `endtag` has been fired, so it fires once.</summary>
        public bool EndFired { get; internal set; }
    }

    /// <summary>
    /// Frames a second a motion set's animation is played at, and the engine's own number.
    /// 30.0f is the rate passed at *every* gameplay play (the
    /// actor's own walk/run/stand, an attack with no time of its own, a head), and
    /// <c>_DAT_004eb84c</c> = 30.0 is the constant in both of the formulas that stretch an animation
    /// to fit a given time. A <c>.nad</c> stores no rate of its own.
    /// </summary>
    public const float Rate = 30f;

    /// <summary>
    /// The rate the mixer will not exceed (<c>_DAT_004ebaa0</c>): a negative rate becomes
    /// 0 and anything over 300 frames a second becomes 300.
    /// </summary>
    public const float RateMax = 300f;

    /// <summary>
    /// How far <see cref="NmsFlags.Variable"/> moves a rate: the engine rolls
    /// <c>rate += rate × (rand() × (1/32767) × 0.5 − 0.25)</c> once, when the animation starts,
    /// so a <c>variable</c> mode plays anywhere from 0.75× to 1.25× speed and stays
    /// there. 66 of the 5,487 shipped modes carry it.
    /// </summary>
    public const float VariableSpread = 0.5f, VariableOffset = 0.25f;

    /// <summary>The engine's clamp, applied to every rate before it reaches a channel.</summary>
    public static float ClampRate(float rate) => rate < 0f ? 0f : rate > RateMax ? RateMax : rate;

    public NodModel Model { get; }
    public NmsMotionSet Set { get; }

    /// <summary>What the character is doing with itself: the idle, or a walk or run cycle.</summary>
    public Channel? Motion { get; private set; }
    /// <summary>What it is doing to the world — a swing, a gesture, a death — over the top.</summary>
    public Channel? Action { get; private set; }

    /// <summary>The channel the character is actually posed from: the action while it lasts.</summary>
    public Channel? Current => Action is not null ? Action : Motion;
    /// <summary>What it is playing now. Null only while it has nothing at all.</summary>
    public NmsMode? Mode => Current?.Mode;
    public AnimationPlayer? Player => Current?.Player;

    readonly Vector3[] rotation, translation;
    /// <summary>The pose a fade starts from (the mixer's <c>aMixRotation</c> and <c>aMixTranslation</c>).</summary>
    readonly Vector3[] mixRotation, mixTranslation;
    /// <summary>Whether a pose has been worked out yet, so there is one for a fade to start from.</summary>
    bool posed;

    /// <summary>
    /// How much of <see cref="mixRotation"/> is still in the pose: 1 as a fade starts, falling by
    /// <see cref="FadeRate"/> a second to 0 (the mixer's fade fields).
    /// </summary>
    public float Fade { get; private set; }
    public float FadeRate { get; private set; }

    /// <summary>The rate every fade is started at (<c>0x40a00000</c>): five a second, a fifth of a second long.</summary>
    public const float FadeSpeed = 5f;

    /// <summary>
    /// Whether the fade under way leaves the root bone's rotation alone (a mixer flag):
    /// set when an action whose flags carry <see cref="KeepRootFlag"/> starts a fade,
    /// read by the fade — bone 0's angles are not drawn towards the kept pose — and
    /// cleared only when the fade runs out, not by the next play. Only the paired motion
    /// sets it (<see cref="PlayPaired"/>).
    /// </summary>
    public bool RootKept { get; private set; }

    /// <summary>The flag bit that keeps the root out of a fade (<c>0x200</c>).</summary>
    public const uint KeepRootFlag = 0x200;

    /// <summary>
    /// What the paired motion adds to its mode's flags (<c>0x2a0</c>):
    /// <c>endtag</c>, <c>nofadeout</c> and <see cref="KeepRootFlag"/>. Of the three only the last
    /// shows — the end tag reaches the feed's end, which does nothing for one who is not feeding, and
    /// <c>nofadeout</c> is never read — so it is the one <see cref="PlayPaired"/> carries.
    /// </summary>
    public const uint PairedFlags = 0x2a0;
    readonly Matrix4x4[] world, skin;

    ThingAnimation(NodModel model, NmsMotionSet set, Channel? motion)
    {
        Model = model; Set = set; Motion = motion;
        int n = model.Bones.Length;
        rotation = new Vector3[n]; translation = new Vector3[n];
        mixRotation = new Vector3[n]; mixTranslation = new Vector3[n];
        world = new Matrix4x4[n]; skin = new Matrix4x4[n];
    }

    /// <summary>
    /// What a thing plays when nothing is happening to it, in the order we try them. A character
    /// stands; the ten sets that have only <c>create</c> are the things that are always moving —
    /// flags, banners, a buoy, a traffic signal, a tattered curtain — and a head model idles.
    /// Everything else (a chest's <c>deathslow</c>, a set-piece's <c>special4</c>) waits for a
    /// script to ask for it, and stays in its rest pose until then.
    ///
    /// Which mode the original starts a newly created thing on is read: nothing names one when the
    /// motion set is attached (tag 9), and the finalize then plays mode 127,
    /// <c>create</c>, on the action channel at 30 fps (built as
    /// <c>Location.PlayCreate</c>). This order also puts <c>create</c> on the motion channel for the
    /// ten sets that have nothing else (ours), which the action channel covers while it plays.
    /// </summary>
    public static readonly string[] Idle = [NmsMotionSet.Idle, "create", "head_idle"];

    /// <summary>
    /// The animation state for a thing, or null when it has no skeleton or no motion set that
    /// loads.
    ///
    /// **A set with none of <paramref name="modes"/> in it still gets one**, with nothing playing:
    /// the thing stands in its rest pose, which is what its vertices are authored in, until
    /// something asks for a mode. That is the engine's own shape — the mixer is allocated when tag
    /// 9 is read and no mode is named there — and it is load-bearing rather than tidy.
    /// The five `skeleton_&lt;n&gt;` templates a vampire's death leaves behind carry sets that
    /// contain **the four death modes and nothing else**; with no mixer there is nothing for
    /// `Damage.Dust` to play the death on, and the skeleton stands up straight for ever.
    /// </summary>
    public static ThingAnimation? For(NodModel model, string motionSet, MotionSets motions,
                                      params string[] modes)
    {
        if (model.Bones.Length < 2 || motionSet.Length == 0) return null;
        var set = motions.Set(motionSet);
        if (set is null) return null;
        foreach (var mode in modes.Length > 0 ? modes : Idle)
            if (motions.Find(set, mode) is { } found)
                return new ThingAnimation(model, set, Start(found.Mode, found.Animation, Rate, motions));
        return new ThingAnimation(model, set, motion: null);
    }

    /// <summary>
    /// A channel at frame 0 playing an animation at a rate, with the engine's own two adjustments
    /// to that rate: the <c>variable</c> roll, then the clamp. <c>0x800</c> — start at
    /// the last frame — is the engine's own, is never set by a motion set, and is not offered here.
    /// </summary>
    static Channel Start(NmsMode mode, NadAnimation animation, float rate, MotionSets motions)
    {
        if (mode.Has(NmsFlags.Variable))
            rate += rate * (motions.Roll() * VariableSpread - VariableOffset);
        return new Channel(mode, new AnimationPlayer(animation, ClampRate(rate)) { Loop = !Stops(mode) },
                           mode.Flags);
    }

    /// <summary>
    /// Whether a mode stops at its last frame rather than looping. The engine tests
    /// <c>endpause</c> *before* <c>noloop</c> when the frame reaches the duration, so
    /// either flag stops it — though no shipped mode sets <c>endpause</c> without <c>noloop</c>.
    /// </summary>
    static bool Stops(NmsMode mode) => mode.Has(NmsFlags.NoLoop) || mode.Has(NmsFlags.EndPause);

    /// <summary>
    /// Plays a mode, by name. <paramref name="onMotion"/> false puts it on the action channel, over
    /// whatever the character is doing with itself. Returns how long it runs in milliseconds, or
    /// −1 when the set cannot play it — which is what the native answers.
    /// </summary>
    public int Play(string mode, MotionSets motions, bool onMotion = true, float rate = Rate)
    {
        if (motions.Find(Set, mode) is not { } found) return -1;
        return Begin(found.Mode, found.Animation, onMotion, rate, motions);
    }

    /// <summary>
    /// Plays a mode by its number — the MOTION_* constants the scripts use are the engine's own
    /// mode table, so the number is an index into it.
    /// </summary>
    public int Play(int mode, MotionSets motions, bool onMotion = true, float rate = Rate)
    {
        if (motions.Find(Set, mode) is not { } found) return -1;
        return Begin(found.Mode, found.Animation, onMotion, rate, motions);
    }

    /// <summary>
    /// The paired motion (only the unstake's): a mode on the action channel with
    /// <see cref="PairedFlags"/> added, so the fade it starts leaves the root's rotation where the
    /// new animation puts it — the body faces its new way at once while the limbs blend.
    /// The partner it records is only ever cleared again, so it is not kept.
    /// </summary>
    public int PlayPaired(int mode, MotionSets motions, float rate = Rate)
    {
        if (motions.Find(Set, mode) is not { } found) return -1;
        return Begin(found.Mode, found.Animation, onMotion: false, rate, motions, PairedFlags);
    }

    /// <summary>
    /// Plays a mode stretched to last <paramref name="milliseconds"/>, which is what the engine does
    /// for an attack or a scripted gesture that has to fit a window:
    /// <c>rate = 30 × nominalMilliseconds ÷ wanted</c>, with a rate of 0 or less becoming 0 — and a
    /// rate of 0 means the animation holds its first frame for ever. Returns the same as
    /// <see cref="Play(string, MotionSets, bool, float)"/>.
    /// </summary>
    public int PlayFor(int mode, int milliseconds, MotionSets motions, bool onMotion = true)
    {
        if (motions.Find(Set, mode) is not { } found) return -1;
        if (milliseconds <= 0) return -1;
        float r = Milliseconds(found.Animation, Rate) / (float)milliseconds * Rate;
        return Begin(found.Mode, found.Animation, onMotion, r < 0f ? 0f : r, motions);
    }

    /// <inheritdoc cref="PlayFor(int, int, MotionSets, bool)"/>
    public int PlayFor(string mode, int milliseconds, MotionSets motions, bool onMotion = true)
    {
        if (motions.Find(Set, mode) is not { } found) return -1;
        if (milliseconds <= 0) return -1;
        float rate = Milliseconds(found.Animation, Rate) / (float)milliseconds * Rate;
        return Begin(found.Mode, found.Animation, onMotion, rate < 0f ? 0f : rate, motions);
    }

    /// <summary>
    /// Plays a mode that has already been chosen — which matters when the caller looked it up
    /// itself, because a set that lists the same mode twice rolls between the two on every lookup
    /// and asking again could land on the other one.
    /// </summary>
    public int Play(NmsMode mode, NadAnimation animation, MotionSets motions, bool onMotion = true,
                    float rate = Rate) =>
        Begin(mode, animation, onMotion, rate, motions);

    int Begin(NmsMode mode, NadAnimation animation, bool onMotion, float rate, MotionSets motions, uint extra = 0)
    {
        if (onMotion)
        {
            // The same animation again is not a restart. The engine copies the new flags
            // onto the channel and returns, which is the whole reason the actor's own update can
            // ask for `walk` on every frame without freezing the character on frame 0. The action
            // channel is left alone — a character swinging while it walks keeps both.
            if (Motion is { } playing && ReferenceEquals(playing.Player.Animation, animation))
            {
                playing.Mode = mode;
                playing.Flags = mode.Flags;
                // Ours, and only ever a no-op on a faithful run: the engine cannot change a rate
                // once an animation is playing, but a fix that ties the rate to the character's
                // speed has to be able to, or it goes stale the moment the speed changes under it.
                if (rate != playing.Player.Rate && !mode.Has(NmsFlags.Variable))
                    playing.Player.Rate = ClampRate(rate);
                return Milliseconds(animation, playing.Player.Rate);
            }
            // A new track fades in over the old one — unless an action is over it, which is what
            // is being seen, or the mode says not to.
            bool fade = Motion is not null && Action is null && !mode.Has(NmsFlags.NoFadeIn);
            Motion = Start(mode, animation, rate, motions);
            if (fade) StartFade();
        }
        else
        {
            // An action always restarts, and it replaces whatever was on the action
            // channel; it fades in over whatever was showing, action or motion.
            bool fade = (Action is not null || Motion is not null) && !mode.Has(NmsFlags.NoFadeIn);
            Action = Start(mode, animation, rate, motions);
            if (fade) StartFade();
            if (fade && (extra & KeepRootFlag) != 0) RootKept = true;
        }
        return Milliseconds(animation, (onMotion ? Motion : Action)!.Player.Rate);
    }

    /// <summary>
    /// How long an animation runs at a rate, in milliseconds:
    /// <c>(int)(duration × 1000 ÷ rate)</c>, truncated, which is what the engine returns
    /// (whose only assertion is that the rate is not zero; its plain-seconds twin, for a motion set's
    /// mode by index, ). Both lazily load the mode's track first if it is not already;
    /// Vitomir has none left to load by then, since a set's tracks are all read together
    /// when the set is (<see cref="MotionSets"/>).
    /// </summary>
    public static int Milliseconds(NadAnimation animation, float rate = Rate) =>
        rate <= 0f ? 0 : (int)(animation.Duration * 1000f / rate);

    /// <summary>
    /// Drops whatever is on the action channel, giving the character back to its motion — **and
    /// fades there**: the channel is cleared when it holds something, and then, whether
    /// it did or not, the fade starts from the pose last drawn at <see cref="FadeSpeed"/> (the
    /// engine's fade at rate 5), so the character eases back over a fifth of a second as it does
    /// when an action runs out. Every stop in 1.1 is this one — a script's <c>StopAction</c> and
    /// <c>StopActorAction</c>, the feeding's, the throw's, the revive's, the conversation's; until then
    /// Vitomir's snapped straight to the motion.
    /// </summary>
    public void StopAction()
    {
        Action = null;
        StartFade();
    }

    /// <summary>
    /// Stops the motion channel too, which leaves the character in its rest pose. `StopMotion` and
    /// `StopActorMotion` both reach the engine's own channel-clear.
    /// </summary>
    public void StopMotion() => Motion = null;

    /// <summary>
    /// Stops the action channel **where it is** and keeps it there — the mixer's "held" bit
    /// (<c>channel |= 0x1000</c>) set from outside, which only the `Throwdeath` tag does:
    /// a thrown body that is dead lies as it landed. A held channel is not advanced (the advance tests
    /// the bit first) and is not handed back to the motion either.
    /// </summary>
    public void HoldAction() { if (Action is { } a) a.Held = true; }

    /// <summary>
    /// Ours: the action channel plays on to its end and then holds its last frame and is not handed back, as an <c>endpause</c>
    /// mode is held — for a one-shot that must not give way before what follows it starts (the portrait's bite before its feed,
    /// <see cref="Ui.HeadModels"/>). Playing another mode on the channel ends it.
    /// </summary>
    public void HoldActionAtEnd() { if (Action is { } a) a.HoldsAtEnd = true; }

    /// <summary>
    /// Puts the action channel's play head at <paramref name="frame"/> — the jump held automatic fire
    /// makes back to the `RepeatTo` tag (the held fire writes the channel's frame and clears
    /// its per-track key cursors, which Vitomir's sampler does not keep). Tags from there on fire
    /// again as the head crosses them. Nothing on the action channel: nothing happens.
    /// </summary>
    public void Rewind(float frame) { if (Action is { } a) a.Player.Frame = frame; }

    /// <summary>
    /// Advances both channels. Any keyframe tag the play head crosses is added to
    /// <paramref name="crossed"/> — that is how the engine fires a footfall, a strike, a shot or a
    /// script callback (the tag dispatcher switches on them).
    /// </summary>
    public void Advance(float seconds, List<NadTag>? crossed = null)
    {
        // The action if there is one, otherwise the motion — never both.
        if (Action is not null)
        {
            Step(Action, seconds, crossed);
            // An action that has run out hands the character back rather than holding its last
            // frame, unless the mode asked to be held there (the advance tests `endpause` first and
            // leaves the channel where it is; anything else clears it) — and the hand-back fades.
            // A channel held from outside (<see cref="HoldAction"/>) is not handed back either.
            if (Action is { Finished: true, Held: false, HoldsAtEnd: false } a && !a.Mode.Has(NmsFlags.EndPause))
            {
                Action = null;
                StartFade();
            }
        }
        else Step(Motion, seconds, crossed, motion: true);
        if (Fade != 0f)
        {
            Fade -= seconds * FadeRate;
            if (Fade < 0f) { Fade = 0f; RootKept = false; }                  // the root flag goes with the weight
        }
    }

    /// <summary>
    /// Starts a fade from the pose the character was last drawn in: that pose is kept,
    /// its root's three angles brought into −180..180, and the weight set to 1. A thing
    /// that has never been posed has nothing to fade from and does not.
    /// </summary>
    void StartFade(float rate = FadeSpeed)
    {
        if (!posed) return;
        rotation.AsSpan().CopyTo(mixRotation);
        translation.AsSpan().CopyTo(mixTranslation);
        var root = mixRotation[0];
        mixRotation[0] = new Vector3(World.WorldCamera.Signed(root.X), World.WorldCamera.Signed(root.Y),
                                     World.WorldCamera.Signed(root.Z));
        Fade = 1f;
        FadeRate = rate;
    }

    /// <summary>
    /// The fade applied to a freshly sampled pose: each bone's angles and offset drawn
    /// towards the kept pose by the weight, <c>(kept − now) × weight + now</c>.
    /// </summary>
    void Blend()
    {
        posed = true;
        if (Fade != 0f)
            for (int i = 0; i < rotation.Length; i++)
            {
                // With the root flag set (a paired motion's fade) bone 0's angles are skipped.
                if (i != 0 || !RootKept) rotation[i] = (mixRotation[i] - rotation[i]) * Fade + rotation[i];
                translation[i] = (mixTranslation[i] - translation[i]) * Fade + translation[i];
            }
        // The mouth goes on top of whatever the modes made of the pose (after the blend).
        if (Mouth is { } mouth) LipSync.Apply(Set, mouth, rotation);
    }

    /// <summary>
    /// The two mouth shapes this frame's pose has added to it, or null — set once a frame by the
    /// lip-sync (<see cref="LipSync"/>) while the character speaks a line with a track.
    /// </summary>
    public LipSync.Shapes? Mouth { get; set; }

    static void Step(Channel? channel, float seconds, List<NadTag>? crossed = null, bool motion = false)
    {
        if (channel is null || channel.Held) return;
        float from = channel.Player.Frame;
        channel.Player.Advance(seconds);
        if ((channel.Mode.Has(NmsFlags.EndPause) || channel.HoldsAtEnd) && channel.Player.Frame >= channel.Player.Animation.Duration)
        {
            channel.Player.Frame = channel.Player.Animation.Duration;
            channel.Held = true;
        }
        if (crossed is not null) Crossings(channel, from, crossed);
        // **A `noloop endtag` mode that runs out fires keyframe 19**, after its own tags —
        // the mixer's `(*callback)(channel, thing, 0x13)`, which is how the feed's opening bite hands
        // over to its loop. It fires once: the channel is spent with it.
        if (crossed is not null && !channel.EndFired && channel.Mode.Has(NmsFlags.NoLoop) && channel.Mode.Has(NmsFlags.EndTag)
            && !channel.Mode.Has(NmsFlags.EndPause) && from < channel.Player.Animation.Duration
            && channel.Player.Frame >= channel.Player.Animation.Duration)
        {
            channel.EndFired = true;
            crossed.Add(new NadTag(channel.Player.Animation.Duration, EndTagType));
        }
        // **And a `noloop fidget` one fires 20** — the idle flourish running out, which
        // the animation callback answers by choosing the locomotion again,
        // so the character goes back to its stand from the first frame. Only the motion channel's
        // is followed: that is where every flourish plays. The engine fires it from the
        // action channel too, and there leaves the track where it stopped rather than clearing it;
        // the five sets whose `gesture7`–`gesture10` carry the flag are the only ones that could
        // meet that, and nothing here plays them on the action channel with the flag yet.
        if (motion && crossed is not null && !channel.EndFired && channel.Mode.Has(NmsFlags.NoLoop)
            && channel.Mode.Has(NmsFlags.Fidget) && !channel.Mode.Has(NmsFlags.EndTag)
            && !channel.Mode.Has(NmsFlags.EndPause) && from < channel.Player.Animation.Duration
            && channel.Player.Frame >= channel.Player.Animation.Duration)
        {
            channel.EndFired = true;
            crossed.Add(new NadTag(channel.Player.Animation.Duration, FidgetTagType));
        }
    }

    /// <summary>The synthetic tag a `noloop endtag` mode fires as it ends (0x13).</summary>
    public const uint EndTagType = 19;

    /// <summary>The synthetic tag a `noloop fidget` mode fires as it ends (0x14).</summary>
    public const uint FidgetTagType = 20;

    /// <summary>
    /// The tags between where the play head was and where it is now, half-open at the start so a
    /// tag fires once. A looping animation that wrapped is two spans: the tail and the head.
    /// </summary>
    static void Crossings(Channel channel, float from, List<NadTag> into)
    {
        var tags = channel.Player.Animation.Tags;
        if (tags.Length == 0) return;
        float to = channel.Player.Frame;
        if (to >= from) { Between(tags, from, to, into); return; }
        Between(tags, from, channel.Player.Animation.Duration, into);
        Between(tags, -1f, to, into);
    }

    static void Between(NadTag[] tags, float from, float to, List<NadTag> into)
    {
        foreach (var t in tags) if (t.Frame > from && t.Frame <= to) into.Add(t);
    }

    /// <summary>Whether whatever is playing has run off the end of a mode that does not loop.</summary>
    public bool Finished => Current is { Finished: true };

    /// <summary>
    /// The model's vertex positions at the current frame, in model space. Leaves the bone matrices
    /// current, so <see cref="PoseCarried"/> can put a held weapon on them without sampling again.
    /// </summary>
    public void Pose(Span<Vector3> positions)
    {
        var verts = Model.Vertices;
        if (Current is not { } channel)
        {
            // Nothing playing: the rest pose. The vertices are already in it,
            // but the bones are still wanted, because something carried on them is not.
            PoseBones();
            skinned = false;
            for (int i = 0; i < verts.Length; i++) positions[i] = verts[i].Position;
            return;
        }
        Skeleton.Sample(channel.Player.Animation, channel.Player.Frame, rotation, translation);
        Blend();
        Skeleton.Pose(Model, rotation, translation, world);
        Skeleton.Skin(Model, world, skin);
        skinned = true;
        for (int i = 0; i < verts.Length; i++) positions[i] = Skeleton.SkinPosition(Model, verts[i], skin);
    }

    /// <summary>
    /// Whether <paramref name="carried"/> can ride this skeleton — the engine's own test before it
    /// puts a model in a sub-model slot (the slot model's bone count must be at least the
    /// carrier's). Every shipped pair has exactly the same count.
    /// </summary>
    public bool CanCarry(NodModel carried) => carried.Bones.Length >= Model.Bones.Length;

    /// <summary>
    /// The current frame's positions for a model **carried on this skeleton** — a held weapon, a
    /// shield — in the carrier's model space.
    ///
    /// The carried model has a skeleton of its own, the same one, and its own inverse rest
    /// matrices; what it does not have is an animation. So the skin matrix is the carried model's
    /// <c>RestMatrixInverse</c> against the *carrier's* animated bone world matrix, and the
    /// difference between the two rigs' rest poses falls out of the product. In `scythe_0.nod`
    /// against `cappadocian2.nod` that difference is a translation of 7.68 along z on every one of
    /// the 40 bones — the weapon was exported with its root at a different height — and the scythe
    /// still lands in the hand, because the same 7.68 is inside the inverse the product undoes.
    ///
    /// Call this after <see cref="Pose"/> or <see cref="PoseBones"/>: it reads the bone matrices
    /// they leave behind rather than sampling the animation a second time.
    /// </summary>
    public void PoseCarried(NodModel carried, Span<Vector3> positions)
    {
        var verts = carried.Vertices;
        if (!carriedSkin.TryGetValue(carried, out var bones))
            carriedSkin[carried] = bones = new Matrix4x4[carried.Bones.Length];
        // A carried model with more bones than the carrier has nothing to ride on past the
        // carrier's last one; the engine would read off the end of the posed array there.
        int n = Math.Min(bones.Length, world.Length);
        for (int b = 0; b < n; b++) bones[b] = Skeleton.RestInverse(carried.Bones[b]) * world[b];
        for (int b = n; b < bones.Length; b++) bones[b] = Matrix4x4.Identity;
        for (int i = 0; i < verts.Length; i++)
            positions[i] = verts[i].Bone < n
                ? Skeleton.SkinPosition(carried, verts[i], bones)
                : verts[i].Position;
    }

    /// <summary>Skin matrices for the models this skeleton carries, one array each, kept between frames.</summary>
    readonly Dictionary<NodModel, Matrix4x4[]> carriedSkin = [];

    /// <summary>
    /// How many bones there are — the count the engine reads as a <c>u16</c> out of the skeleton
    /// header  and picks a random one out of.
    /// </summary>
    public int BoneCount => Model.Bones.Length;

    /// <summary>
    /// Which bone a motion tag names, or −1. As the engine finds it: a linear walk of the
    /// motion set's tag table — five dwords a record, the tag first and the bone second — answering
    /// −1 when the tag is not in it. That −1 is what sends the gib kill to a random bone.
    /// </summary>
    public int TagBone(int tag)
    {
        foreach (var t in Set.Tags) if (t.Index == tag) return t.Bone;
        return -1;
    }

    /// <summary>
    /// One bone's matrix at the current frame, in model space — the engine's posed-bone array
    /// (the bone's posed matrix under the thing), which is the transform the gib spray
    /// copies to spawn a chunk at. An index outside the skeleton gives the identity.
    /// </summary>
    public Matrix4x4 BoneMatrix(int index)
    {
        if (index < 0 || index >= world.Length) return Matrix4x4.Identity;
        PoseBones();
        return world[index];
    }

    /// <summary>
    /// The bones alone, without skinning a single vertex. <see cref="Pose"/> does this on its way to
    /// the vertices; anything that only wants to know where a joint has got to comes here instead.
    /// A thing with nothing playing is posed from its rest offsets, which is where its bones are.
    /// </summary>
    public void PoseBones()
    {
        if (Current is { } channel)
        {
            Skeleton.Sample(channel.Player.Animation, channel.Player.Frame, rotation, translation);
            Blend();
        }
        else { rotation.AsSpan().Clear(); translation.AsSpan().Clear(); }
        Skeleton.Pose(Model, rotation, translation, world);
    }

    /// <summary>A bone's angles as the last pose left them, fade and all (ours: for the tests).</summary>
    public Vector3 BoneRotation(int bone) => rotation[bone];

    /// <summary>Where a tag's bone has got to, so an attachment can ride along with it.</summary>
    public Matrix4x4 TagMatrix(NmsTag tag)
    {
        if (tag.Bone < 0 || tag.Bone >= world.Length) return Matrix4x4.Identity;
        var m = world[tag.Bone];
        m.Translation += Vector3.TransformNormal(tag.Offset, m);
        return m;
    }
}
