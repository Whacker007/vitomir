// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The mouth moving with the voice.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Anim;

/// <summary>
/// Lip-sync, as the original does it. A spoken line's recording carries a **track** — a mouth
/// shape, 0 to 8, for every thirtieth of a second, read from the `.nlp` beside it
/// (<see cref="Nlp"/>) — and a character's **motion set** carries what each shape means: a `mouth`
/// line naming a bone (the jaw) and three angles. Every frame, a character whose line is
/// still running has its jaw turned by the two shapes either side of the moment, blended.
///
/// **Which line.** When a line starts, the speaker is told the channel it is on and when it ends
/// <see cref="WorldThing.MouthChannel"/>,
/// <see cref="WorldThing.MouthBusyUntil"/>). A conversation's lines and a
/// script's `PlayVoice` tell it the real channel; a foley `voice` bark tells it
/// the free marker 0xff before the play has given it one, which never resolves — so **a bark never
/// moves the mouth**, only recorded speech does.
///
/// **Each frame** (from the actor's frame, for anything with a model and a
/// motion set): while the game clock is short of the line's end and its channel is still playing a
/// sound that has a track, the moment is the seconds since the channel started
/// (`(clock − channel start) × 0.001`, never below 0) times 30; the frame is its **floor**
/// (the float-to-int under round-down) and the blend is what is left.
/// The two shapes are the track's at that frame and the next, the frame first held to the track's
/// length less two (an unsigned compare, so a track of fewer than two frames reads past its end in
/// 1.1 — ours reads shape 0 there). The pose is then added to the body model (with
/// its motion set), and — for a party member (type 7) with a portrait head — to the head
/// (with its own set), so **the portrait talks too**.
///
/// **The pose**: after the model's mixer has worked out this frame's pose (the
/// animation, then the blend between two modes), each shape's angles are **added** to its bone's
/// rotation, the first weighted <c>1 − lerp</c> and the second <c>lerp</c>. The `mouth` parser
/// stores the line's angles twice in the shape's record (two `sscanf`s of the same token into
/// both are added, so **each shape turns the jaw by twice the angles
/// its line writes** — reproduced. Nothing is added when a weight is exactly 0. The pose is not kept:
/// the next frame's pose is worked out afresh, so when the line ends the jaw is simply the
/// animation's again.
/// </summary>
public static class LipSync
{
    /// <summary>A track's frames a second.</summary>
    public const float FramesPerSecond = 30f;

    /// <summary>Milliseconds to seconds, as the channel's age is scaled.</summary>
    public const float MsToSeconds = 0.001f;

    /// <summary>The two shapes a mouth is between this frame, and how far from the first to the second.</summary>
    public readonly record struct Shapes(byte First, byte Second, float Lerp);

    /// <summary>
    /// The reading: the shapes <paramref name="thing"/>'s mouth is between now, or null when
    /// it is not speaking a line with a track.
    /// </summary>
    public static Shapes? For(WorldThing thing)
    {
        if (thing.Place is not { Audio: { } audio } place) return null;
        if ((uint)place.Milliseconds >= (uint)thing.MouthBusyUntil) return null;
        if (audio.Mixer.Get(thing.MouthChannel) is not { Sound: { } sound } channel) return null;
        return At(sound.Lips, Math.Max(0, audio.Mixer.Now - channel.StartedAt));
    }

    /// <summary>The shapes a track calls for <paramref name="ms"/> into its line.</summary>
    public static Shapes? At(byte[] track, int ms)
    {
        if (track.Length == 0) return null;
        float t = ms * MsToSeconds * FramesPerSecond;
        uint frame = (uint)MathF.Floor(t);
        uint at = Math.Min(frame, (uint)(track.Length - 2));
        byte first = at < track.Length ? track[at] : (byte)0;
        byte second = at + 1 < track.Length ? track[at + 1] : (byte)0;
        return new Shapes(first, second, t - frame);
    }

    /// <summary>
    /// The two shapes added to the pose's bone rotations (Euler degrees, as
    /// <see cref="Skeleton.Sample"/> leaves them), after the mode blend.
    /// </summary>
    public static void Apply(NmsMotionSet set, Shapes shapes, Span<Vector3> rotation)
    {
        float first = 1f - shapes.Lerp;
        if (first != 0f) Add(set, shapes.First, first, rotation);
        if (shapes.Lerp != 0f) Add(set, shapes.Second, shapes.Lerp, rotation);
    }

    static void Add(NmsMotionSet set, byte shape, float weight, Span<Vector3> rotation)
    {
        // The set's record for the shape: its last `mouth` line for that letter, since each line
        // overwrites the record; a letter with no line is a zeroed record — bone 0, no angles.
        NmsMouth? line = null;
        foreach (var m in set.Mouths) if (m.Letter - 'a' == shape) line = m;
        if (line is null || (uint)line.Bone >= (uint)rotation.Length) return;
        // Both copies of the angles into the same bone.
        rotation[line.Bone] += weight * line.Angles + weight * line.Angles;
    }
}
