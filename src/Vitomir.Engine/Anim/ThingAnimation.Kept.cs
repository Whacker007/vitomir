// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A model's pose taken, and held again (ours): what a save needs to keep a frozen body's pose through a load.
// The repair `loadedeffects`; `Save/LoadedEffects.cs` is the only caller.
using System.Numerics;

namespace Vitomir.Engine.Anim;

public sealed partial class ThingAnimation
{
    /// <summary>
    /// **The pose as it would be drawn now** (ours; 1.1 has no such record — its save keeps one animation held at
    /// its end, <c>Save.Poses</c>, and nothing else of what a model is playing): every bone's angles and offset,
    /// three numbers a bone each, from the channel being shown at its frame with the fade under way laid in —
    /// what <see cref="Pose"/> works out, without the mouth. **Nothing of the mixer is touched**: it is worked
    /// out beside it. Read by a save for a model an effect's script froze (<c>FreezeAnimations</c>, the model's
    /// bit 0x20000): a frozen mixer is not advanced, so this is the pose it stays in. Null with nothing playing.
    /// </summary>
    public (float[] Rotation, float[] Translation)? KeepPose()
    {
        if (Current is not { } channel) return null;
        var r = new Vector3[rotation.Length];
        var t = new Vector3[translation.Length];
        Skeleton.Sample(channel.Player.Animation, channel.Player.Frame, r, t);
        if (Fade != 0f)
            for (int i = 0; i < r.Length; i++)                                   // Blend's own sum
            {
                if (i != 0 || !RootKept) r[i] = (mixRotation[i] - r[i]) * Fade + r[i];
                t[i] = (mixTranslation[i] - t[i]) * Fade + t[i];
            }
        static float[] Flat(Vector3[] of) => [.. of.SelectMany(v => new[] { v.X, v.Y, v.Z })];
        return (Flat(r), Flat(t));
    }

    /// <summary>
    /// **A pose held** (ours): the model is drawn in <paramref name="rotation"/> and <paramref name="translation"/>
    /// (<see cref="KeepPose"/>'s numbers) for as long as its mixer is not advanced — and eases out of it into
    /// whatever it is playing once it is, over a fifth of a second. It is done with the mixer's own fade and
    /// nothing else: the pose is made the pose a fade starts from with the weight at 1, which is
    /// exactly how a frozen body keeps its pose in 1.1 when it is told to play something new — the fade starts and
    /// never moves on. **No channel is touched**: what the model is playing, its frame and its tags are as they
    /// were, so nothing fires that would not have. False when the numbers are not this model's (another bone
    /// count: a save edited by hand) or nothing is playing to lay a pose over.
    /// </summary>
    public bool HoldPose(float[] rotation, float[] translation)
    {
        if (Current is null || rotation.Length != mixRotation.Length * 3 || translation.Length != mixTranslation.Length * 3) return false;
        for (int i = 0; i < mixRotation.Length; i++)
        {
            mixRotation[i] = new Vector3(rotation[i * 3], rotation[i * 3 + 1], rotation[i * 3 + 2]);
            mixTranslation[i] = new Vector3(translation[i * 3], translation[i * 3 + 1], translation[i * 3 + 2]);
        }
        var root = mixRotation[0];                                               // as a fade's start has them
        mixRotation[0] = new Vector3(World.WorldCamera.Signed(root.X), World.WorldCamera.Signed(root.Y), World.WorldCamera.Signed(root.Z));
        Fade = 1f;
        FadeRate = FadeSpeed;
        RootKept = false;
        PoseBones();                                                             // and there is a pose for a later fade to start from
        return true;
    }
}
