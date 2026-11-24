// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Auto Camera Slewing — the Controls screen's *auto camera slewing* box, bit 8 of the options word
// (`AutoSlew`) — with what asks it, the game camera's tick.
using System.Numerics;

namespace Vitomir.Engine.World;

public sealed partial class WorldCamera
{
    /// <summary>
    /// Whether the camera swings round behind the character by itself: the options word's bit 8
    /// (options bit 8), which the program keeps in step with the Controls screen.
    /// </summary>
    public bool AutoSlew { get; set; }

    /// <summary>How long the character has to keep its facing before the swing starts, in seconds (1.0 from the constructor).</summary>
    public const float SlewDelay = 1f;
    /// <summary>How long the swing takes, in seconds (2.0).</summary>
    public const float SlewDuration = 2f;
    /// <summary>How far the character may turn and still be facing "the same way": the cosine of 10° (0.9848).</summary>
    public const float SlewSameFacing = 0.9848f;

    bool slewStarted, slewDone;
    float slewStartAt;
    Vector3 slewFacing, slewFrom, slewDelta;

    /// <summary>
    /// **The slew**, once a game-camera frame while <see cref="AutoSlew"/> is on. It watches the
    /// character's facing (the thing's forward row): whenever that has turned more than 10°
    /// from the one it last kept, the slew starts over — not started, not done — and waits
    /// <see cref="SlewDelay"/> from now. Once the wait is over and it is not done, it takes the
    /// camera's own forward, flattened and made unit, as where it swings from, and the character's
    /// facing less that as how far; then each frame it points the camera along
    /// <c>from + delta × (cos(180° × left / duration) + 1) / 2</c> — an ease from where the camera was
    /// to straight behind the character over <see cref="SlewDuration"/> — and is done when the time
    /// is up. Turning the camera by hand (a key, or the pointer at the edge) marks it done
    /// a flag set), so it stays where the player put it until the character turns again.
    /// </summary>
    void SlewStep(WorldThing subject, float now)
    {
        var facing = SlewFacingOf(subject);
        if (Vector3.Dot(slewFacing, facing) < SlewSameFacing)
        {
            slewStarted = slewDone = false;
            slewStartAt = now + SlewDelay;
            slewFacing = facing;
            return;
        }
        if (slewStartAt > now || slewDone) return;
        if (!slewStarted)
        {
            var forward = FromAngles(Angles, Target).Forward();
            forward.Z = 0f;
            slewFrom = forward.LengthSquared() > 0f ? Vector3.Normalize(forward) : facing;
            slewStarted = true;
            slewDelta = facing - slewFrom;
        }
        float left = (SlewDuration - (now - slewStartAt)) / SlewDuration * MathF.PI;
        float share = (MathF.Cos(left) + 1f) * 0.5f;
        var d = slewFrom + slewDelta * share;
        if (d.LengthSquared() > 0f)
        {
            d = Vector3.Normalize(d);
            Angles.Z = Wrap(MathF.Atan2(-d.X, d.Y) * (180f / MathF.PI));
        }
        if (slewStartAt + SlewDuration <= now) slewDone = true;
    }

    /// <summary>A turn by hand ends the slew until the character turns again.</summary>
    void SlewTurnedByHand() => slewDone = true;

    /// <summary>The character's facing the slew watches: its transform's forward row, level.</summary>
    static Vector3 SlewFacingOf(WorldThing subject)
    {
        float z = subject.Orientation.Z * (MathF.PI / 180f);
        return new Vector3(-MathF.Sin(z), MathF.Cos(z), 0f);
    }

    /// <summary>
    /// **Following somebody new**: the slew's started and done
    /// marks cleared and its facing taken from the new subject — **its
    /// start time left alone**. Whether or not the option is on, and
    /// whoever the subject was before. So the next frame finds the facing unchanged, and, the old wait
    /// being over, swings the camera at once by the share the time since that wait began works out
    /// to (<see cref="SlewStep"/>) — anything from none of the way to all of it when the wait began
    /// more than <see cref="SlewDuration"/> ago — and marks it done. A new subject selected during
    /// the old wait waits out what is left of it and then eases round.
    /// </summary>
    void SlewFollow(WorldThing subject)
    {
        slewStarted = slewDone = false;
        slewFacing = SlewFacingOf(subject);
    }

    /// <summary>A new camera's slew: not started, not done, and its start time 0.</summary>
    void SlewReset()
    {
        slewStarted = slewDone = false;
        slewStartAt = 0f;
    }
}
