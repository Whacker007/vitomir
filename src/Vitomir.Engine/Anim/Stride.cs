// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.Anim;

/// <summary>
/// How much ground one turn of a locomotion cycle is *drawn* as covering — measured from the
/// animation itself, because the game never wrote it down.
///
/// **This is ours.** The original has no such measurement and does not want one: it plays every
/// cycle at a flat 30 frames a second (<see cref="ThingAnimation.Rate"/>) whatever the character's
/// speed, which is why a `civ05YoungLady` walking at 70 units a second appears to skate. The flag
/// that was meant to tie the two together, `movespeed`, is parsed and never read.
/// So this exists to specify the Fixes-menu option that would put
/// it right, and nothing uses it unless the player asks.
///
/// The method uses the two things the shipped data does record:
/// <list type="bullet">
/// <item>the <b>footfall tags</b> in a `.nad` — `Lwalk`, `Rwalk`, `Lrun`, `Rrun` — which mark the
/// frame each foot plants;</item>
/// <item>the <b>`LFoot` and `RFoot` tag bones</b> in the motion set, which say which bone each foot
/// is.</item>
/// </list>
/// A planted foot does not move relative to the ground, so in an animation authored in place it
/// travels backwards through the character's own model space by exactly the distance the character
/// should have advanced. Summing that over every stance phase of the animation gives the ground the
/// whole loop covers. A character wanting to move at <c>speed</c> then needs
/// <c>rate = speed × duration ÷ stride</c> frames a second, which is 30 exactly when its speed
/// matches the pace the animator drew.
/// </summary>
public static class Stride
{
    /// <summary>Tag types 0 and 2 are the left foot planting, 1 and 3 the right.</summary>
    public static bool IsFootfall(uint type) => type <= 3;
    static bool IsLeft(uint type) => type is 0 or 2;

    /// <summary>
    /// The ground one loop of <paramref name="animation"/> is drawn as covering, in world units, or
    /// null when it cannot be measured: no footfall tags (139 of the game's locomotion modes, and
    /// they are creatures without an ordinary gait — an alligator, a bat), no `LFoot`/`RFoot` tags
    /// in the set, a skeleton the bones do not index, or a measurement that comes out at nothing.
    /// </summary>
    public static float? Measure(NodModel model, NmsMotionSet set, NadAnimation animation)
    {
        int left = BoneOf(set, "LFoot"), right = BoneOf(set, "RFoot");
        if (left < 0 || right < 0 || left >= model.Bones.Length || right >= model.Bones.Length) return null;
        if (animation.Duration <= 0f) return null;

        var plants = animation.Tags.Where(t => IsFootfall(t.Type)).OrderBy(t => t.Frame).ToArray();
        if (plants.Length < 2) return null;

        int n = model.Bones.Length;
        var rotation = new Vector3[n];
        var translation = new Vector3[n];
        var world = new Matrix4x4[n];

        float total = 0f;
        for (int i = 0; i < plants.Length; i++)
        {
            // The foot that plants here stays down until the next foot plants; the last one holds
            // until the first plant of the next loop, which is the same pose as the first plant.
            float from = plants[i].Frame;
            float to = i + 1 < plants.Length ? plants[i + 1].Frame : plants[0].Frame;
            int bone = IsLeft(plants[i].Type) ? left : right;
            var a = Foot(model, animation, from, bone, rotation, translation, world);
            var b = Foot(model, animation, to, bone, rotation, translation, world);
            total += Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
        }
        return total > 0.5f ? total : null;
    }

    /// <summary>
    /// The rate, in frames a second, at which <paramref name="animation"/> covers
    /// <paramref name="speed"/> units a second on the ground. <see cref="ThingAnimation.Rate"/>
    /// exactly when the character moves at the pace the animation was drawn for.
    /// </summary>
    public static float RateFor(float speed, float duration, float stride) =>
        stride > 0f ? speed * duration / stride : ThingAnimation.Rate;

    /// <summary>The pace the animator drew, in units a second: what plays at a natural 30 fps.</summary>
    public static float NaturalSpeed(float duration, float stride) =>
        duration > 0f ? stride * ThingAnimation.Rate / duration : 0f;

    static int BoneOf(NmsMotionSet set, string tag) =>
        set.Tags.FirstOrDefault(t => string.Equals(t.Name, tag, StringComparison.OrdinalIgnoreCase))?.Bone ?? -1;

    static Vector3 Foot(NodModel model, NadAnimation animation, float frame, int bone,
                        Vector3[] rotation, Vector3[] translation, Matrix4x4[] world)
    {
        Skeleton.Sample(animation, frame, rotation, translation);
        Skeleton.Pose(model, rotation, translation, world);
        return world[bone].Translation;
    }
}
