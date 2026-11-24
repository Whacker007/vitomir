// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.People;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// A made-up person on the models repository's skeleton (no files needed): its 78 bones by their names and in their
/// order (the 56 and the 22 twist bones), unrotated at rest, facing +Y with its left at −X; rings of skin round its bones (the
/// limbs' shared with their twist bones, so no one bone has half a vertex); and made-up clips with the models repository's notes —
/// the stand, the walk and run each way (their hips turned as the set's, a phase written into the jaw's turn so a blend's two
/// halves can be seen in step), the four turns on the spot (the root turning; the chest and the head leading it), and a kneel.
/// </summary>
internal static class PeopleDoubles
{
    /// <summary>A clip made of a function of its time, with notes.</summary>
    internal sealed class Clip(string name, ClipNotes notes, Action<float, Skeleton, Span<BoneTransform>> pose) : INotedClip
    {
        public string Name => name;
        public float Duration => notes.Duration;
        public bool Loops => notes.Loops;
        public ClipNotes Notes => notes;

        public void Sample(float seconds, Skeleton skeleton, Span<BoneTransform> local)
        {
            float d = Duration, t = seconds;
            if (Loops && d > 0f) { t -= MathF.Floor(t / d) * d; }
            else t = Math.Clamp(t, 0f, d);
            PoseEvaluator.Rest(skeleton, local);
            pose(t, skeleton, local);
        }
    }

    /// <summary>The bones: name, parent's name, offset from the parent (m).</summary>
    static IEnumerable<(string Name, string? Parent, Vector3 Offset)> Bones()
    {
        yield return ("root", null, Vector3.Zero);
        yield return ("pelvis", "root", new(0, 0, 0.95f));
        yield return ("spine_01", "pelvis", new(0, 0, 0.1f));
        yield return ("spine_02", "spine_01", new(0, 0, 0.13f));
        yield return ("spine_03", "spine_02", new(0, 0, 0.13f));
        yield return ("neck_01", "spine_03", new(0, 0, 0.2f));
        yield return ("head", "neck_01", new(0, 0, 0.1f));
        yield return ("jaw", "head", new(0, 0.03f, -0.03f));
        yield return ("eye_l", "head", new(-0.03f, 0.08f, 0.04f));
        yield return ("eye_r", "head", new(0.03f, 0.08f, 0.04f));
        foreach (var (s, x) in new[] { ("l", -1f), ("r", 1f) })
        {
            yield return ($"clavicle_{s}", "spine_03", new(0.03f * x, 0.02f, 0.15f));
            yield return ($"upperarm_{s}", $"clavicle_{s}", new(0.15f * x, 0, 0));
            yield return ($"lowerarm_{s}", $"upperarm_{s}", new(0, 0, -0.28f));
            yield return ($"hand_{s}", $"lowerarm_{s}", new(0, 0, -0.26f));
            foreach (var f in new[] { "thumb", "index", "middle", "ring", "pinky" })
            {
                yield return ($"{f}_01_{s}", $"hand_{s}", new(0, 0.01f, -0.08f));
                yield return ($"{f}_02_{s}", $"{f}_01_{s}", new(0, 0, -0.03f));
                yield return ($"{f}_03_{s}", $"{f}_02_{s}", new(0, 0, -0.025f));
            }
        }
        foreach (var (s, x) in new[] { ("l", -1f), ("r", 1f) })
        {
            yield return ($"thigh_{s}", "pelvis", new(0.1f * x, 0, -0.05f));
            yield return ($"calf_{s}", $"thigh_{s}", new(0, 0, -0.42f));
            yield return ($"foot_{s}", $"calf_{s}", new(0, 0, -0.42f));
            yield return ($"ball_{s}", $"foot_{s}", new(0, 0.12f, -0.06f));
        }
        foreach (var s in new[] { "l", "r" })
            for (int i = 0; i < 4; i++) yield return ($"upperarm_twist_{i + 1:00}_{s}", $"upperarm_{s}", new(0, 0, -0.28f * i / 4f));
        foreach (var s in new[] { "l", "r" })
            for (int i = 0; i < 5; i++) yield return ($"lowerarm_twist_{i + 1:00}_{s}", $"lowerarm_{s}", new(0, 0, -0.26f * i / 4f));
        foreach (var s in new[] { "l", "r" })
            for (int i = 0; i < 2; i++) yield return ($"thigh_twist_{i + 1:00}_{s}", $"thigh_{s}", new(0, 0, -0.42f * i / 2f));
    }

    /// <summary>The made-up person: its model (the skin rings, see the class) and rig.</summary>
    public static PersonModel Person()
    {
        var bones = new List<BoneInfo>();
        var at = new List<Vector3>();
        foreach (var (name, parent, offset) in Bones())
        {
            int p = parent is null ? -1 : bones.FindIndex(b => b.Name == parent);
            bones.Add(new BoneInfo(name, p, new BoneTransform(Quaternion.Identity, offset)));
            at.Add((p >= 0 ? at[p] : Vector3.Zero) + offset);
        }
        var skeleton = new Skeleton { Bones = [.. bones], InverseBind = [.. at.Select(p => Matrix4x4.CreateTranslation(-p))] };
        int B(string n) => skeleton.Find(n);
        var vertices = new List<SkinVertex>();
        // A ring of eight vertices round the line a→b at each of five places, its weights by place.
        void Rings(Vector3 a, Vector3 b, float radius, Func<float, (int Bone, float Weight)[]> weights)
        {
            var axis = Vector3.Normalize(b - a);
            var e1 = MathF.Abs(axis.Z) < 0.9f ? Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitZ)) : Vector3.UnitX;
            var e2 = Vector3.Cross(axis, e1);
            for (int i = 0; i <= 4; i++)
            {
                float u = i / 4f;
                var w = weights(u);
                for (int k = 0; k < 8; k++)
                {
                    float angle = k * MathF.PI / 4f;
                    var p = a + (b - a) * u + (e1 * MathF.Cos(angle) + e2 * MathF.Sin(angle)) * radius;
                    vertices.Add(new SkinVertex
                    {
                        Position = p, Normal = Vector3.UnitZ,
                        Bones = SkinVertex.PackBones(w[0].Bone, w.Length > 1 ? w[1].Bone : 0, w.Length > 2 ? w[2].Bone : 0, w.Length > 3 ? w[3].Bone : 0),
                        Weights = new Vector4(w[0].Weight, w.Length > 1 ? w[1].Weight : 0, w.Length > 2 ? w[2].Weight : 0, w.Length > 3 ? w[3].Weight : 0),
                    });
                }
            }
        }
        Vector3 J(string n) => at[B(n)];
        (int, float)[] One(string n) => [(B(n), 1f)];
        Rings(J("head") - new Vector3(0, 0, 0.02f), J("head") + new Vector3(0, 0, 0.2f), 0.09f, u => u < 0.3f ? [(B("jaw"), 1f)] : One("head"));
        Rings(J("neck_01"), J("head"), 0.06f, _ => One("neck_01"));
        Rings(J("spine_03") + new Vector3(-0.18f, 0, 0.1f), J("spine_03") + new Vector3(0.18f, 0, 0.1f), 0.12f,
              u => u < 0.2f ? One("clavicle_l") : u > 0.8f ? One("clavicle_r") : One("spine_03"));
        Rings(J("spine_01") + new Vector3(-0.12f, 0, 0.06f), J("spine_01") + new Vector3(0.12f, 0, 0.06f), 0.13f, u => u < 0.5f ? One("spine_01") : One("spine_02"));
        Rings(J("pelvis") + new Vector3(-0.14f, 0, -0.05f), J("pelvis") + new Vector3(0.14f, 0, -0.05f), 0.12f, _ => One("pelvis"));
        foreach (var s in new[] { "l", "r" })
        {
            // Upper arm: its skin shared three ways, never half on one bone (merged it is all the upper arm's).
            Rings(J($"upperarm_{s}"), J($"lowerarm_{s}"), 0.045f, u => [(B($"upperarm_twist_02_{s}"), 0.4f), (B($"upperarm_twist_03_{s}"), 0.3f), (B($"upperarm_{s}"), 0.3f)]);
            Rings(J($"lowerarm_{s}"), J($"hand_{s}"), 0.035f, u => [(B($"lowerarm_twist_0{1 + (int)(u * 3.99f)}_{s}"), 0.45f), (B($"lowerarm_twist_0{2 + (int)(u * 3.99f)}_{s}"), 0.45f), (B($"upperarm_{s}"), 0.1f)]);
            Rings(J($"hand_{s}"), J($"middle_03_{s}"), 0.03f, u => u < 0.5f ? One($"hand_{s}") : One($"middle_02_{s}"));
            Rings(J($"thigh_{s}"), J($"calf_{s}"), 0.07f, u => [(B($"thigh_twist_01_{s}"), 0.35f), (B($"thigh_twist_02_{s}"), 0.35f), (B($"thigh_{s}"), 0.3f)]);
            Rings(J($"calf_{s}"), J($"foot_{s}"), 0.05f, _ => One($"calf_{s}"));
            Rings(J($"foot_{s}") + new Vector3(0, -0.05f, -0.03f), J($"ball_{s}") + new Vector3(0, 0.05f, 0f), 0.035f, u => u < 0.6f ? One($"foot_{s}") : One($"ball_{s}"));
        }
        var model = new SkinnedModel
        {
            Name = "person double", Vertices = [.. vertices], Indices = [], Parts = [], Skeleton = skeleton,
            BoundsMin = new Vector3(-0.6f, -0.2f, 0f), BoundsMax = new Vector3(0.6f, 0.3f, 1.9f),
        };
        return new PersonModel(new PersonRecipe("double", "male", "base_double", []), model, PersonRig.From(skeleton), []);
    }

    /// <summary>A gait's ways: (suffix, heading, its hips' turn, ground speed, cycle, backpedals) — the set's own numbers for the walk.</summary>
    public static readonly (string Way, float Heading, float Hips, float Speed, float Cycle, bool Back)[] WalkWays =
    [
        ("", 0f, 0f, 1.25f, 1.1f, false), ("_fwd_left", 45f, 30f, 1.19f, 1.1f, false), ("_left", 90f, 60f, 1.125f, 1.1333f, false),
        ("_back_left", 135f, -30f, 1.0f, 1.2f, true), ("_back", 180f, 0f, 0.875f, 1.3f, true), ("_back_right", -135f, 30f, 1.0f, 1.2f, true),
        ("_right", -90f, -60f, 1.125f, 1.1333f, false), ("_fwd_right", -45f, -30f, 1.19f, 1.1f, false),
    ];

    /// <summary>
    /// The made-up set: the stand (8 s), the walk and run each way (the hips turned, the thighs swinging in a cycle, the jaw turned
    /// by the share of the cycle, so the phase shows), the turns on the spot (the root turning from none to its turn, eased), a kneel.
    /// </summary>
    public static IReadOnlyList<INotedClip> Motions(Skeleton skeleton, bool crouch = false)
    {
        int root = skeleton.Find("root"), pelvis = skeleton.Find("pelvis"), jaw = skeleton.Find("jaw");
        int thighL = skeleton.Find("thigh_l"), thighR = skeleton.Find("thigh_r");
        int chest = skeleton.Find("spine_03"), head = skeleton.Find("head");
        var clips = new List<INotedClip>
        {
            new Clip("stand", new ClipNotes { Kind = "stand", Loops = true, Duration = 8f }, (_, _, _) => { }),
        };
        foreach (var (gait, scale) in new[] { ("walk", 1f), ("run", 2.4f) })
            foreach (var w in WalkWays)
            {
                float cycle = gait == "run" ? w.Cycle * 0.66f : w.Cycle;
                var notes = new ClipNotes
                {
                    Kind = gait, Loops = true, Duration = cycle, GroundSpeed = w.Speed * scale, HeadingDeg = w.Heading, PlayedBackwards = w.Back,
                    Footfalls = [new ClipFootfall($"L{gait}", 0f), new ClipFootfall($"R{gait}", cycle / 2f)],
                };
                float hips = w.Hips * DetMath.DegToRad;
                clips.Add(new Clip(gait + w.Way, notes, (t, _, local) =>
                {
                    float phase = t / cycle;
                    local[pelvis] = local[pelvis] with { Rotation = DetMath.Yaw(hips) };
                    float swing = 0.4f * DetMath.Sin(phase * DetMath.TwoPi) * (w.Back ? -1f : 1f);
                    local[thighL] = local[thighL] with { Rotation = DetMath.AxisAngle(Vector3.UnitX, swing) };
                    local[thighR] = local[thighR] with { Rotation = DetMath.AxisAngle(Vector3.UnitX, -swing) };
                    local[jaw] = local[jaw] with { Rotation = DetMath.AxisAngle(Vector3.UnitX, phase) };
                }));
            }
        foreach (var (side, degrees) in new[] { ("left", 45f), ("left", 90f), ("right", -45f), ("right", -90f) })
        {
            float duration = MathF.Abs(degrees) > 60f ? 1.05f : 0.825f;
            var notes = new ClipNotes { Kind = "turn", Loops = false, Duration = duration, TurnDeg = degrees, RootTurns = true };
            clips.Add(new Clip($"turn_{side}_{(int)MathF.Abs(degrees)}", notes, (t, _, local) =>
            {
                float s = Math.Clamp(t / duration, 0f, 1f);
                float lead = DetMath.Sin(DetMath.Pi * s) * MathF.Sign(degrees) * DetMath.DegToRad;
                s = s * s * (3f - 2f * s);
                local[root] = local[root] with { Rotation = DetMath.Yaw(degrees * DetMath.DegToRad * s) };
                // The upper body's own lead, as the set's turns have (the chest up to 12° ahead of the root, the head 8° more).
                local[chest] = local[chest] with { Rotation = DetMath.Yaw(12f * lead) };
                local[head] = local[head] with { Rotation = DetMath.Yaw(8f * lead) };
            }));
        }
        clips.Add(new Clip("kneel_one_down", new ClipNotes { Kind = "act", Loops = false, Duration = 2.4f }, (t, _, local) =>
            local[pelvis] = local[pelvis] with { Translation = local[pelvis].Translation - new Vector3(0, 0, 0.4f * t / 2.4f) }));
        if (crouch) clips.AddRange(Crouch(skeleton));
        return clips;
    }

    /// <summary>
    /// The models repository's crouch, made up: the crouch held (the hips 0.35 m down) and the kneel (0.55 m down),
    /// the clips between them and the stand moving the hips from one height to the other, the crouched walk each way (the walk's
    /// numbers at the round's speeds) and the crouched turns.
    /// </summary>
    static IEnumerable<INotedClip> Crouch(Skeleton skeleton)
    {
        int root = skeleton.Find("root"), pelvis = skeleton.Find("pelvis"), jaw = skeleton.Find("jaw");
        const float Crouched = 0.35f, Kneeling = 0.55f;
        Clip Between(string name, float duration, float from, float to, bool loops = false) =>
            new(name, new ClipNotes { Kind = "crouch", Loops = loops, Duration = duration }, (t, _, local) =>
                local[pelvis] = local[pelvis] with { Translation = local[pelvis].Translation - new Vector3(0, 0, from + (to - from) * Math.Clamp(t / duration, 0f, 1f)) });
        yield return Between("crouch", 8f, Crouched, Crouched, loops: true);
        yield return Between("crouch_down", 0.5f, 0f, Crouched);
        yield return Between("crouch_up", 0.6f, Crouched, 0f);
        yield return Between("crouch_kneel", 6f, Kneeling, Kneeling, loops: true);
        yield return Between("crouch_settle", 0.6f, Crouched, Kneeling);
        yield return Between("crouch_rise", 0.5f, Kneeling, Crouched);
        yield return Between("crouch_kneel_down", 0.7f, 0f, Kneeling);
        yield return Between("crouch_kneel_up", 0.8f, Kneeling, 0f);
        float[] speeds = [1.0f, 0.95f, 0.9f, 0.8f, 0.7f, 0.8f, 0.9f, 0.95f];
        for (int i = 0; i < WalkWays.Length; i++)
        {
            var w = WalkWays[i];
            float cycle = w.Cycle * 1.2f;
            var notes = new ClipNotes { Kind = "walk", Style = "crouch", Loops = true, Duration = cycle, GroundSpeed = speeds[i], HeadingDeg = w.Heading, PlayedBackwards = w.Back };
            yield return new Clip("crouch_walk" + w.Way, notes, (t, _, local) =>
            {
                local[pelvis] = local[pelvis] with { Translation = local[pelvis].Translation - new Vector3(0, 0, Crouched), Rotation = DetMath.Yaw(w.Hips * DetMath.DegToRad) };
                local[jaw] = local[jaw] with { Rotation = DetMath.AxisAngle(Vector3.UnitX, t / cycle) };
            });
        }
        foreach (var (side, degrees) in new[] { ("left", 45f), ("left", 90f), ("right", -45f), ("right", -90f) })
        {
            float duration = MathF.Abs(degrees) > 60f ? 1.1f : 0.9f;
            var notes = new ClipNotes { Kind = "turn", Style = "crouch", Loops = false, Duration = duration, TurnDeg = degrees, RootTurns = true };
            yield return new Clip($"crouch_turn_{side}_{(int)MathF.Abs(degrees)}", notes, (t, _, local) =>
            {
                float s = Math.Clamp(t / duration, 0f, 1f);
                s = s * s * (3f - 2f * s);
                local[root] = local[root] with { Rotation = DetMath.Yaw(degrees * DetMath.DegToRad * s) };
                local[pelvis] = local[pelvis] with { Translation = local[pelvis].Translation - new Vector3(0, 0, Crouched) };
            });
        }
    }

    /// <summary>The made-up person registered as a kind with its made-up set.</summary>
    public static (CharacterAssets Assets, int Kind, PersonModel Person) Kind(bool crouch = false)
    {
        var person = Person();
        var assets = new CharacterAssets();
        int kind = PeopleKinds.Register(assets, person, Motions(person.Model.Skeleton, crouch), "double");
        return (assets, kind, person);
    }
}
