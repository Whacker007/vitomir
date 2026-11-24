// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The models repository's motions as platform clips.
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Vitomir.Formats.Gltf;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Assets.People;

/// <summary>
/// One glTF animation of the models repository's motions file (<c>motions_&lt;sex&gt;.glb</c>) as a platform clip, its
/// keys converted to the platform's model space (<see cref="PeopleSpace"/>) at load.
/// <para><b>Bones by name.</b> Its tracks name their bones; the clip is mapped onto whatever skeleton it is sampled on by
/// name, once a skeleton (kept per <see cref="Skeleton"/> instance). A bone of that skeleton without a track keeps the
/// skeleton's own rest: each body has its own joint positions, and only <c>pelvis</c> carries translation, so every other
/// bone keeps its own length; the swing and strap bones an outfit adds keep their rest.</para>
/// <para><b>Sampling.</b> Rotations between keys go the short way (<see cref="DetMath.Slerp"/>), translations linearly
/// (<see cref="PoseMath.Lerp"/>), a STEP sampler holds its key, a CUBICSPLINE one is a Hermite curve (a rotation then
/// normalised). Scale tracks are ignored. A looping clip wraps by whole durations into [0, <see cref="Duration"/>) and its
/// last key is its first again (nothing is put between them); one that does not loop holds its first and last poses
/// outside. <see cref="Duration"/> is the notes' <c>duration_s</c>, else the last key. <see cref="Sample"/> allocates
/// nothing once a skeleton is mapped and is pure arithmetic in a fixed order (DetMath), so the simulation and the renderer
/// pose alike on every machine.</para>
/// <para><b>The face.</b> A motion that means something by the face carries a weights track on the file's <c>face</c> mesh:
/// it is kept (<see cref="FaceTargets"/>, <see cref="SampleFace"/>) and not applied.</para>
/// </summary>
public sealed class GltfClip : INotedClip
{
    readonly Track[] tracks;
    readonly FaceTrack? face;
    readonly ConditionalWeakTable<Skeleton, Binding> bindings = new();
    Binding? last;

    GltfClip(string name, Track[] tracks, FaceTrack? face, ClipNotes notes, float duration, bool loops)
    {
        Name = name;
        this.tracks = tracks;
        this.face = face;
        Notes = notes;
        Duration = duration;
        Loops = loops;
    }

    public string Name { get; }
    public float Duration { get; }
    public bool Loops { get; }
    public ClipNotes Notes { get; }

    /// <summary>The bones the clip has a rotation track for, in its channels' order.</summary>
    public IEnumerable<string> RotatedBones => tracks.Where(t => t.Rotation).Select(t => t.Bone);

    /// <summary>The bones the clip has a translation track for (the models repository's motions: <c>pelvis</c> alone).</summary>
    public IEnumerable<string> MovedBones => tracks.Where(t => !t.Rotation).Select(t => t.Bone);

    /// <summary>The face's shape names the clip's weights track drives, in order; empty when it has none.</summary>
    public IReadOnlyList<string> FaceTargets => face?.Targets ?? [];

    /// <summary>The face's shape weights at <paramref name="seconds"/> (wrapped or held as the pose is), one per <see cref="FaceTargets"/>.</summary>
    public void SampleFace(float seconds, Span<float> weights)
    {
        if (face is null) { weights.Clear(); return; }
        face.Sample(TimeAt(seconds), weights);
    }

    /// <summary>The clip's time at <paramref name="seconds"/>: wrapped into [0, duration) when it loops, else held within [0, duration].</summary>
    public float TimeAt(float seconds)
    {
        float d = Duration;
        if (d <= 0f) return 0f;
        if (!Loops) return seconds < 0f ? 0f : seconds > d ? d : seconds;
        if (seconds >= d || seconds < 0f)
        {
            seconds -= MathF.Floor(seconds / d) * d;
            if (seconds >= d) seconds -= d;
            if (seconds < 0f) seconds = 0f;
        }
        return seconds;
    }

    public void Sample(float seconds, Skeleton skeleton, Span<BoneTransform> local)
    {
        var binding = Bind(skeleton);
        float t = TimeAt(seconds);
        var bones = skeleton.Bones;
        int n = Math.Min(bones.Length, local.Length);
        var rot = binding.Rotation;
        var pos = binding.Translation;
        for (int b = 0; b < n; b++)
        {
            var rest = bones[b].Rest;
            int r = rot[b], p = pos[b];
            local[b] = new BoneTransform(r >= 0 ? tracks[r].SampleRotation(t) : rest.Rotation,
                                         p >= 0 ? tracks[p].SampleTranslation(t) : rest.Translation);
        }
    }

    /// <summary>The clip's tracks for each bone of <paramref name="skeleton"/>, made once a skeleton.</summary>
    Binding Bind(Skeleton skeleton)
    {
        var l = last;
        if (l is not null && ReferenceEquals(l.Skeleton, skeleton)) return l;
        if (!bindings.TryGetValue(skeleton, out var b))
        {
            var rot = new int[skeleton.Count];
            var pos = new int[skeleton.Count];
            Array.Fill(rot, -1);
            Array.Fill(pos, -1);
            for (int i = 0; i < tracks.Length; i++)
            {
                int bone = skeleton.Find(tracks[i].Bone);
                if (bone < 0) continue;
                var into = tracks[i].Rotation ? rot : pos;
                if (into[bone] < 0) into[bone] = i;
            }
            b = new Binding(skeleton, rot, pos);
            bindings.AddOrUpdate(skeleton, b);
        }
        last = b;
        return b;
    }

    sealed record Binding(Skeleton Skeleton, int[] Rotation, int[] Translation);

    /// <summary>One bone's rotation or translation keys, converted to the platform's space.</summary>
    sealed class Track
    {
        public required string Bone { get; init; }
        public required bool Rotation { get; init; }
        public required GltfInterpolation Interpolation { get; init; }
        public required float[] Times { get; init; }
        /// <summary>Rotation keys (three a key for CUBICSPLINE: in-tangent, value, out-tangent).</summary>
        public Quaternion[] Rotations { get; init; } = [];
        /// <summary>Translation keys (three a key for CUBICSPLINE).</summary>
        public Vector3[] Translations { get; init; } = [];

        int Values => Interpolation == GltfInterpolation.CubicSpline ? 3 : 1;
        int Middle => Interpolation == GltfInterpolation.CubicSpline ? 1 : 0;

        public Quaternion SampleRotation(float t)
        {
            int k = Key(Times, t, out float f, out float span);
            int v = Values, m = Middle;
            if (f <= 0f || k + 1 >= Times.Length) return Rotations[k * v + m];
            switch (Interpolation)
            {
                case GltfInterpolation.Step: return Rotations[k * v + m];
                case GltfInterpolation.CubicSpline:
                {
                    var p0 = Rotations[k * 3 + 1]; var b0 = Rotations[k * 3 + 2];
                    var a1 = Rotations[(k + 1) * 3]; var p1 = Rotations[(k + 1) * 3 + 1];
                    Hermite(f, span, out float h00, out float h10, out float h01, out float h11);
                    var q = new Quaternion(h00 * p0.X + h10 * b0.X + h01 * p1.X + h11 * a1.X,
                                           h00 * p0.Y + h10 * b0.Y + h01 * p1.Y + h11 * a1.Y,
                                           h00 * p0.Z + h10 * b0.Z + h01 * p1.Z + h11 * a1.Z,
                                           h00 * p0.W + h10 * b0.W + h01 * p1.W + h11 * a1.W);
                    float len = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
                    return len > 0f ? new Quaternion(q.X / len, q.Y / len, q.Z / len, q.W / len) : p0;
                }
                default: return DetMath.Slerp(Rotations[k], Rotations[k + 1], f);
            }
        }

        public Vector3 SampleTranslation(float t)
        {
            int k = Key(Times, t, out float f, out float span);
            int v = Values, m = Middle;
            if (f <= 0f || k + 1 >= Times.Length) return Translations[k * v + m];
            switch (Interpolation)
            {
                case GltfInterpolation.Step: return Translations[k * v + m];
                case GltfInterpolation.CubicSpline:
                {
                    var p0 = Translations[k * 3 + 1]; var b0 = Translations[k * 3 + 2];
                    var a1 = Translations[(k + 1) * 3]; var p1 = Translations[(k + 1) * 3 + 1];
                    Hermite(f, span, out float h00, out float h10, out float h01, out float h11);
                    return new Vector3(h00 * p0.X + h10 * b0.X + h01 * p1.X + h11 * a1.X,
                                       h00 * p0.Y + h10 * b0.Y + h01 * p1.Y + h11 * a1.Y,
                                       h00 * p0.Z + h10 * b0.Z + h01 * p1.Z + h11 * a1.Z);
                }
                default: return PoseMath.Lerp(Translations[k], Translations[k + 1], f);
            }
        }
    }

    /// <summary>The face's weights track: <see cref="Targets"/> weights a key.</summary>
    sealed class FaceTrack
    {
        public required string[] Targets { get; init; }
        public required GltfInterpolation Interpolation { get; init; }
        public required float[] Times { get; init; }
        public required float[] Weights { get; init; }

        public void Sample(float t, Span<float> into)
        {
            int n = Targets.Length;
            int k = Key(Times, t, out float f, out float span);
            int v = Interpolation == GltfInterpolation.CubicSpline ? 3 : 1, m = Interpolation == GltfInterpolation.CubicSpline ? 1 : 0;
            bool hold = f <= 0f || k + 1 >= Times.Length || Interpolation == GltfInterpolation.Step;
            float h00 = 0, h10 = 0, h01 = 0, h11 = 0;
            if (!hold && Interpolation == GltfInterpolation.CubicSpline) Hermite(f, span, out h00, out h10, out h01, out h11);
            for (int i = 0; i < n && i < into.Length; i++)
            {
                float w0 = Weights[(k * v + m) * n + i];
                if (hold) { into[i] = w0; continue; }
                if (Interpolation == GltfInterpolation.CubicSpline)
                    into[i] = h00 * w0 + h10 * Weights[(k * 3 + 2) * n + i] + h01 * Weights[((k + 1) * 3 + 1) * n + i] + h11 * Weights[(k + 1) * 3 * n + i];
                else
                {
                    float w1 = Weights[(k + 1) * n + i];
                    into[i] = w0 + (w1 - w0) * f;
                }
            }
            for (int i = n; i < into.Length; i++) into[i] = 0f;
        }
    }

    /// <summary>The last key at or before <paramref name="t"/> (0 before the first), how far towards the next (0 to 1) and the span between them.</summary>
    static int Key(float[] times, float t, out float f, out float span)
    {
        f = 0f; span = 0f;
        int last = times.Length - 1;
        if (last <= 0 || t <= times[0]) return 0;
        if (t >= times[last]) return last;
        int lo = 0, hi = last;   // times[lo] <= t < times[hi]
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (times[mid] <= t) lo = mid; else hi = mid;
        }
        span = times[lo + 1] - times[lo];
        f = span > 0f ? (t - times[lo]) / span : 0f;
        return lo;
    }

    /// <summary>The cubic Hermite basis at <paramref name="s"/>, the tangents' terms scaled by the keys' span (glTF 2.0, appendix C).</summary>
    static void Hermite(float s, float span, out float h00, out float h10, out float h01, out float h11)
    {
        float s2 = s * s, s3 = s2 * s;
        h00 = 2f * s3 - 3f * s2 + 1f;
        h10 = (s3 - 2f * s2 + s) * span;
        h01 = -2f * s3 + 3f * s2;
        h11 = (s3 - s2) * span;
    }

    // ----- loading -----

    /// <summary>Every animation of <paramref name="file"/> in its order, as clips.</summary>
    public static GltfClip[] FromFile(GltfFile file) => file.Animations.Select(a => From(file, a)).ToArray();

    /// <summary>One animation of <paramref name="file"/> as a clip: its tracks by bone name, converted to the platform's space; its notes from <c>extras.vitomir</c>.</summary>
    public static GltfClip From(GltfFile file, GltfAnimation animation)
    {
        var tracks = new List<Track>();
        var seen = new HashSet<(string, bool)>();
        FaceTrack? face = null;
        float lastKey = 0f;
        foreach (var channel in animation.Channels)
        {
            if (channel.Node < 0 || (uint)channel.Sampler >= (uint)animation.Samplers.Length) continue;
            var sampler = animation.Samplers[channel.Sampler];
            var node = file.Nodes[channel.Node];
            if (channel.Path is not (GltfPath.Rotation or GltfPath.Translation or GltfPath.Weights)) continue;   // scale: ignored
            var times = file.ReadFloats(sampler.Input);
            if (times.Length == 0) continue;
            lastKey = MathF.Max(lastKey, times[^1]);
            int perKey = sampler.Interpolation == GltfInterpolation.CubicSpline ? 3 : 1;
            if (channel.Path == GltfPath.Weights)
            {
                if (face is not null) continue;
                string[] targets = node.Mesh >= 0 ? file.Meshes[node.Mesh].TargetNames : [];
                var weights = file.ReadFloats(sampler.Output);
                int n = targets.Length > 0 ? targets.Length : weights.Length / (times.Length * perKey);
                if (n == 0 || weights.Length != times.Length * perKey * n) continue;
                if (targets.Length == 0) targets = Enumerable.Range(0, n).Select(i => $"target{i}").ToArray();
                face = new FaceTrack { Targets = targets, Interpolation = sampler.Interpolation, Times = times, Weights = weights };
                continue;
            }
            bool rotation = channel.Path == GltfPath.Rotation;
            if (!seen.Add((node.Name, rotation))) continue;
            if (rotation)
            {
                var q = file.ReadQuaternions(sampler.Output);
                if (q.Length != times.Length * perKey) throw new InvalidDataException($"{animation.Name}: {node.Name}'s rotation has {q.Length} values for {times.Length} keys");
                for (int i = 0; i < q.Length; i++) q[i] = PeopleSpace.Rotation(q[i]);
                tracks.Add(new Track { Bone = node.Name, Rotation = true, Interpolation = sampler.Interpolation, Times = times, Rotations = q });
            }
            else
            {
                var v = file.ReadVector3(sampler.Output);
                if (v.Length != times.Length * perKey) throw new InvalidDataException($"{animation.Name}: {node.Name}'s translation has {v.Length} values for {times.Length} keys");
                for (int i = 0; i < v.Length; i++) v[i] = PeopleSpace.Point(v[i]);
                tracks.Add(new Track { Bone = node.Name, Rotation = false, Interpolation = sampler.Interpolation, Times = times, Translations = v });
            }
        }
        var notes = ReadNotes(animation.Extras);
        float duration = notes.Duration > 0f ? notes.Duration : lastKey;
        return new GltfClip(animation.Name, tracks.ToArray(), face, notes with { Duration = duration }, duration, notes.Loops);
    }

    /// <summary>The models repository's notes (<c>extras.vitomir</c>) as <see cref="ClipNotes"/>; defaults when there are none.</summary>
    public static ClipNotes ReadNotes(string? extras)
    {
        if (string.IsNullOrEmpty(extras)) return new ClipNotes();
        using var doc = JsonDocument.Parse(extras);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("vitomir", out var v) || v.ValueKind != JsonValueKind.Object)
            return new ClipNotes();
        var footfalls = new List<ClipFootfall>();
        if (v.TryGetProperty("footfalls", out var ff) && ff.ValueKind == JsonValueKind.Array)
            foreach (var f in ff.EnumerateArray())
                if (f.ValueKind == JsonValueKind.Object)
                    footfalls.Add(new ClipFootfall(Text(f, "foot"), Number(f, "at_s", 0f)));
        // The ground a motion was made on: a slope's grade, or stairs; none for flat ground.
        ClipGround? ground = null;
        if (v.TryGetProperty("ground", out var g) && g.ValueKind == JsonValueKind.Object)
            ground = new ClipGround(Text(g, "kind"), Number(g, "grade_deg", 0f), Number(g, "climb_m_s", 0f), Flag(g, "up"), Number(g, "riser_m", 0f),
                                    Number(g, "tread_m", 0f), (int)Number(g, "treads_a_step", 0f), Number(g, "nosing_offset_m", 0f));
        // A start or a stop: its way, foot, curve, half-life, distance, the loop's phase it leaves at or enters from, its speed a frame.
        ClipStartStop? startStop = null;
        if (v.TryGetProperty("start_stop", out var ss) && ss.ValueKind == JsonValueKind.Object)
        {
            var speeds = new List<float>();
            if (ss.TryGetProperty("speed_m_s", out var sp) && sp.ValueKind == JsonValueKind.Array)
                foreach (var x in sp.EnumerateArray()) if (x.ValueKind == JsonValueKind.Number) speeds.Add((float)x.GetDouble());
            string way = Text(ss, "way");
            startStop = new ClipStartStop(way, Text(ss, "foot"), Text(ss, "curve"), Number(ss, "half_life_s", 0f), Number(ss, "distance_m", 0f),
                                          Number(ss, way == "stop" ? "enter_phase" : "exit_phase", 0f), [.. speeds], Text(v, "gait"), Number(ss, "prep_s", 0f))
            {
                // The stops' sliding feet and their moments.
                Slides = Slides(ss), BrakeLands = Number(ss, "braking_foot_lands_s", float.NaN), FeetClosed = Number(ss, "feet_closed_s", float.NaN),
            };
        }
        // A jump, the fall or a landing: the capsule's flight (airborne) and the landing's band and moments (landing).
        ClipAirborne? airborne = null;
        if (v.TryGetProperty("airborne", out var ab) && ab.ValueKind == JsonValueKind.Object)
            airborne = new ClipAirborne(Number(ab, "takeoff_s", float.NaN), Number(ab, "apex_s", float.NaN), Number(ab, "rise_m", 0f), Number(ab, "v_up_m_s", 0f),
                                        Text(ab, "then"), Number(ab, "pelvis_at_takeoff_m", 0f), Number(ab, "level_landing_s", float.NaN), Text(ab, "land"),
                                        Text(ab, "enter"), Text(ab, "enter_foot"), Text(ab, "after"))
            {
                // The stride carried on in the air, the pedalling loops, a walk-off's or run-off's phase, the landings' lists.
                AirFrom = Pair(ab, "air_s", 0), AirTo = Pair(ab, "air_s", 1), Loop = Text(ab, "loop"), HalfS = Number(ab, "half_s", float.NaN),
                LandsR = ab.TryGetProperty("lands", out var lands) && lands.ValueKind == JsonValueKind.Object ? Number(lands, "r", float.NaN) : float.NaN,
                LandsL = ab.TryGetProperty("lands", out var lands2) && lands2.ValueKind == JsonValueKind.Object ? Number(lands2, "l", float.NaN) : float.NaN,
                EnterPhase = Number(ab, "enter_phase", float.NaN), AfterAll = Texts(ab, "after"),
                // A prelanding's lead, the loop it comes from and the landing it goes into.
                Prelanding = ab.TryGetProperty("prelanding", out var pre) && pre.ValueKind == JsonValueKind.Object
                    ? new ClipPrelanding(Number(pre, "lead_s", float.NaN), Text(pre, "from"), Text(pre, "then"), Text(pre, "start")) : null,
            };
        ClipLanding? landing = null;
        if (v.TryGetProperty("landing", out var ld) && ld.ValueKind == JsonValueKind.Object)
        {
            float low = 0f, high = float.PositiveInfinity;
            if (ld.TryGetProperty("drop_m", out var band) && band.ValueKind == JsonValueKind.Array && band.GetArrayLength() == 2
                && band[0].ValueKind == JsonValueKind.Number && band[1].ValueKind == JsonValueKind.Number)
                (low, high) = ((float)band[0].GetDouble(), (float)band[1].GetDouble());
            landing = new ClipLanding(low, high, Number(ld, "contact_s", 0f), Number(ld, "heels_down_s", 0f), Number(ld, "deepest_s", float.NaN),
                                      Text(ld, "onto"), Text(ld, "exit_into"), Number(ld, "exit_phase", 0f))
            { Who = Text(ld, "who"), GroundSpeed = Number(ld, "ground_speed_m_s", 0f) };
        }
        // A move over or onto a thing : the capsule's path a frame, the obstacle it was made for, the hands, the warp.
        ClipTraverse? traverse = null;
        if (v.TryGetProperty("traverse", out var tv) && tv.ValueKind == JsonValueKind.Object && tv.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.Array)
        {
            var ts = new List<float>(); var fw = new List<float>(); var up = new List<float>(); var side = new List<float>();
            bool sideways = true;
            foreach (var row in path.EnumerateArray())
                if (row.ValueKind == JsonValueKind.Array && row.GetArrayLength() >= 3 && row[0].ValueKind == JsonValueKind.Number)
                {
                    ts.Add((float)row[0].GetDouble()); fw.Add((float)row[1].GetDouble()); up.Add((float)row[2].GetDouble());
                    // A shimmy's fourth number, the sideways move (positive to the left); every row has it, or the path has none.
                    if (row.GetArrayLength() >= 4 && row[3].ValueKind == JsonValueKind.Number) side.Add((float)row[3].GetDouble()); else sideways = false;
                }
            float face = 0f, top = 0f, depth = float.NaN, low = 0f, high = 0f;
            bool thin = false;
            JsonElement? ob0 = null;
            if (tv.TryGetProperty("obstacle", out var ob) && ob.ValueKind == JsonValueKind.Object)
            {
                ob0 = ob;
                face = Number(ob, "face_m", 0f); top = Number(ob, "top_m", 0f); depth = Number(ob, "depth_m", float.NaN); thin = Flag(ob, "thin");
                low = Pair(ob, "height_range_m", 0); high = Pair(ob, "height_range_m", 1);
            }
            var hands = new List<ClipHand>();
            if (tv.TryGetProperty("hands", out var hs) && hs.ValueKind == JsonValueKind.Array)
                foreach (var h in hs.EnumerateArray())
                    if (h.ValueKind == JsonValueKind.Object)
                        hands.Add(new ClipHand(Text(h, "hand"), Number(h, "contact_s", 0f), Number(h, "release_s", 0f), Pair(h, "at_m", 0), Pair(h, "at_m", 1), Number(h, "left_m", 0f)));
            float warpFrom = float.NaN, warpTo = float.NaN;
            if (tv.TryGetProperty("warp", out var wp) && wp.ValueKind == JsonValueKind.Object) (warpFrom, warpTo) = (Number(wp, "from_s", float.NaN), Number(wp, "to_s", float.NaN));
            var speeds = new List<float>();
            if (tv.TryGetProperty("speed_m_s", out var sp) && sp.ValueKind == JsonValueKind.Array)
                foreach (var x in sp.EnumerateArray()) if (x.ValueKind == JsonValueKind.Number) speeds.Add((float)x.GetDouble());
            traverse = new ClipTraverse([.. ts], [.. fw], [.. up], face, top, depth, thin, low, high, hands, warpFrom, warpTo)
            {
                Enter = Text(tv, "enter"), EnterFoot = Text(tv, "enter_foot"), EnterPhase = Number(tv, "enter_phase", float.NaN), Speeds = speeds,
                LeftLandsS = Number(tv, "left_lands_s", float.NaN), ExitInto = Text(tv, "exit_into"), ExitPhase = Number(tv, "exit_phase", float.NaN), Ends = Text(tv, "ends"),
                // The ledge's clips: the hang's place from the edge, a grab's, a shimmy's, the hang a cycle starts and ends in.
                Side = sideways && side.Count == ts.Count ? [.. side] : [], Hang = HangOf(tv), Grab = GrabOf(tv), Shimmy = ShimmyOf(tv), LoopsFrom = Text(tv, "loops_from"),
                // A low deep top's depths, the ladders' notes.
                DepthLow = ob0 is { } o0 ? Pair(o0, "depth_range_m", 0) : float.NaN, DepthHigh = ob0 is { } o1 ? Pair(o1, "depth_range_m", 1) : float.NaN,
                Ladder = LadderOf(tv), Top = LadderTopOf(tv), Landing = LadderLandingOf(tv), LadderHang = LadderHangOf(tv), Climb = LadderClimbOf(tv),
                Starts = Text(tv, "starts"),
                Leap = LeapOf(tv), Corner = CornerOf(tv),   // The hang's leaps and corners
                Wall = WallOf(tv),                          // The walls climbed by their holds
                SideStep = SideStepOf(tv),                  // Squeezing and cover
            };
        }
        return new ClipNotes
        {
            Kind = Text(v, "kind"),
            Style = Text(v, "style"),
            Carry = Text(v, "carry"),
            Loops = Flag(v, "loops"),
            Duration = Number(v, "duration_s", 0f),
            GroundSpeed = Number(v, "ground_speed_m_s", 0f),
            HeadingDeg = Number(v, "heading_deg", float.NaN),
            PlayedBackwards = Flag(v, "played_backwards"),
            Strides = (int)Number(v, "strides", 0f),
            StrideM = Number(v, "stride_m", 0f),
            StepsPerMinute = Number(v, "steps_per_minute", 0f),
            Footfalls = footfalls,
            TurnDeg = Number(v, "turn_deg", float.NaN),
            RootTurns = Flag(v, "root_turns"),
            Quickens = Flag(v, "quickens"),
            After = Text(v, "after"),
            Held = Text(v, "held"),
            Ground = ground,
            StartStop = startStop,
            Airborne = airborne,
            Landing = landing,
            Traverse = traverse,
            Json = v.GetRawText(),
        };
    }

    /// <summary>A ledge clip's <c>traverse.hang</c>, or null.</summary>
    /// <summary>A stop's <c>slides</c> (each <c>foot</c>, <c>from_s</c>, <c>to_s</c>, <c>m</c>), none when it has none.</summary>
    static ClipSlide[] Slides(JsonElement ss)
    {
        if (!ss.TryGetProperty("slides", out var sl) || sl.ValueKind != JsonValueKind.Array) return [];
        var all = new List<ClipSlide>();
        foreach (var x in sl.EnumerateArray())
            if (x.ValueKind == JsonValueKind.Object)
                all.Add(new ClipSlide(Text(x, "foot") == "l", Number(x, "from_s", 0f), Number(x, "to_s", 0f), Number(x, "m", 0f)));
        return [.. all];
    }

    static ClipHang? HangOf(JsonElement tv)
    {
        if (!tv.TryGetProperty("hang", out var h) || h.ValueKind != JsonValueKind.Object) return null;
        string at = ""; float fwd = float.NaN, up = float.NaN, apart = float.NaN;
        if (h.TryGetProperty("hands", out var hands) && hands.ValueKind == JsonValueKind.Object)
            (at, fwd, up, apart) = (Text(hands, "at"), Number(hands, "fwd_m", float.NaN), Number(hands, "up_m", float.NaN), Number(hands, "apart_m", float.NaN));
        return new ClipHang(Number(h, "edge_fwd_m", float.NaN), Number(h, "edge_up_m", float.NaN), Text(h, "kind"), at, fwd, up, apart);
    }

    /// <summary>A grab's <c>traverse.grab</c>, or null.</summary>
    static ClipGrab? GrabOf(JsonElement tv)
    {
        if (!tv.TryGetProperty("grab", out var g) || g.ValueKind != JsonValueKind.Object) return null;
        return new ClipGrab(Text(g, "start"), Number(g, "takeoff_s", float.NaN), Number(g, "v_up_m_s", float.NaN), Number(g, "contact_s", float.NaN),
                            Number(g, "speed_m_s", float.NaN), Pair(g, "height_range_m", 0), Pair(g, "height_range_m", 1))
        {
            Enter = Text(g, "enter"), EnterFoot = Text(g, "enter_foot"), EnterPhase = Number(g, "enter_phase", float.NaN), FallSpeedMs = Number(g, "fall_speed_m_s", float.NaN),
            StartsOverHang = Number(g, "starts_over_hang_m", float.NaN), StartsOffHang = Number(g, "starts_off_hang_m", float.NaN),
        };
    }

    /// <summary>A shimmy's <c>traverse.shimmy</c>, or null.</summary>
    static ClipShimmy? ShimmyOf(JsonElement tv) =>
        tv.TryGetProperty("shimmy", out var s) && s.ValueKind == JsonValueKind.Object
            ? new ClipShimmy(Text(s, "to"), Number(s, "speed_m_s", float.NaN), Number(s, "step_m", float.NaN)) : null;

    /// <summary>A ladder clip's <c>traverse.ladder</c>, or null.</summary>
    static ClipLadder? LadderOf(JsonElement tv) =>
        tv.TryGetProperty("ladder", out var l) && l.ValueKind == JsonValueKind.Object
            ? new ClipLadder(Number(l, "rung_m", float.NaN), Number(l, "tilt_deg", float.NaN), Number(l, "first_rung_m", float.NaN), Number(l, "rung_fwd_m", float.NaN),
                             Number(l, "lower_rung_up_m", float.NaN), Number(l, "hands_apart_m", float.NaN), Text(l, "kind"))
            : null;

    /// <summary>A ladder clip's <c>traverse.top</c>, or null.</summary>
    static ClipLadderTop? LadderTopOf(JsonElement tv) =>
        tv.TryGetProperty("top", out var t) && t.ValueKind == JsonValueKind.Object && t.TryGetProperty("rung", out _)
            ? new ClipLadderTop((int)Number(t, "rung", 0f), Number(t, "floor_up_m", float.NaN), Number(t, "lip_fwd_m", float.NaN), Number(t, "stand_fwd_m", float.NaN), Flag(t, "rails"))
            : null;

    /// <summary>A ladder clip's <c>traverse.landing</c>, or null.</summary>
    static ClipLadderLanding? LadderLandingOf(JsonElement tv) =>
        tv.TryGetProperty("landing", out var l) && l.ValueKind == JsonValueKind.Object
            ? new ClipLadderLanding(Text(l, "to"), (int)Number(l, "rung", 0f), Number(l, "floor_up_m", float.NaN), Number(l, "stand_left_m", float.NaN), Number(l, "stand_fwd_m", float.NaN))
            : null;

    /// <summary>A ladder clip's <c>traverse.hang</c> read as a ladder's (it has <c>edge_left_m</c>), or null.</summary>
    static ClipLadderHang? LadderHangOf(JsonElement tv)
    {
        if (!tv.TryGetProperty("hang", out var h) || h.ValueKind != JsonValueKind.Object || !h.TryGetProperty("edge_left_m", out _)) return null;
        var hands = new List<float>();
        if (h.TryGetProperty("hands_left_m", out var hl) && hl.ValueKind == JsonValueKind.Array)
            foreach (var x in hl.EnumerateArray()) if (x.ValueKind == JsonValueKind.Number) hands.Add((float)x.GetDouble());
        return new ClipLadderHang(Text(h, "to"), Text(h, "kind"), (int)Number(h, "rung", 0f), Number(h, "edge_left_m", float.NaN), Number(h, "edge_fwd_m", float.NaN),
                                  Number(h, "edge_up_m", float.NaN), [.. hands]);
    }

    /// <summary>A hang leap's <c>traverse.leap</c>, or null.</summary>
    static ClipLeap? LeapOf(JsonElement tv) =>
        tv.TryGetProperty("leap", out var l) && l.ValueKind == JsonValueKind.Object
            ? new ClipLeap(Number(l, "left_m", 0f), Number(l, "up_m", 0f), Number(l, "back_m", 0f), Number(l, "push_s", float.NaN), Number(l, "contact_s", float.NaN),
                           Text(l, "turn"), Text(l, "start"), Text(l, "then"))
            : null;

    /// <summary>A ledge corner's <c>traverse.corner</c>, or null.</summary>
    static ClipCorner? CornerOf(JsonElement tv) =>
        tv.TryGetProperty("corner", out var c) && c.ValueKind == JsonValueKind.Object
            ? new ClipCorner(Text(c, "kind"), Text(c, "way"), Number(c, "corner_along_m", float.NaN), Number(c, "ends_along_m", float.NaN), Text(c, "start"), Text(c, "then"))
            : null;

    /// <summary>A wall clip's <c>traverse.wall</c>, or null.</summary>
    static ClipWall? WallOf(JsonElement tv)
    {
        if (!tv.TryGetProperty("wall", out var w) || w.ValueKind != JsonValueKind.Object || !w.TryGetProperty("face_fwd_m", out _)) return null;
        float hf = float.NaN, hu = float.NaN;
        if (w.TryGetProperty("hands", out var hands) && hands.ValueKind == JsonValueKind.Object) (hf, hu) = (Number(hands, "fwd_m", float.NaN), Number(hands, "up_m", float.NaN));
        var holds = new List<ClipWallHold>();
        if (w.TryGetProperty("holds", out var hs) && hs.ValueKind == JsonValueKind.Array)
            foreach (var h in hs.EnumerateArray())
                if (h.ValueKind == JsonValueKind.Object)
                    holds.Add(new ClipWallHold(Text(h, "limb"), Number(h, "on_s", -1f), Number(h, "off_s", 99f), Number(h, "left_m", float.NaN), Number(h, "up_m", float.NaN),
                                               Number(h, "along_m", float.NaN), Text(h, "face")));
        float sl = 0f, su = 0f;
        if (w.TryGetProperty("step_m", out var st) && st.ValueKind == JsonValueKind.Object) (sl, su) = (Number(st, "left", 0f), Number(st, "up", 0f));
        var wall = new ClipWall(Number(w, "face_fwd_m", float.NaN), Number(w, "hold_depth_m", 0f), Number(w, "foothold_depth_m", 0f), Text(w, "kind"), hf, hu, holds,
                                sl, su, Number(w, "ground_up_m", 0f)) { Surface = Text(w, "surface") };
        if (w.TryGetProperty("jump", out var j) && j.ValueKind == JsonValueKind.Object)
            wall = wall with { JumpTakeoffS = Number(j, "takeoff_s", float.NaN), JumpContactS = Number(j, "contact_s", float.NaN), AboveGroundOnM = Number(j, "above_ground_on_m", float.NaN) };
        if (w.TryGetProperty("top", out var t) && t.ValueKind == JsonValueKind.Object)
            wall = wall with { TopEdgeFwdM = Number(t, "edge_fwd_m", float.NaN), TopEdgeUpM = Number(t, "edge_up_m", float.NaN), TopThen = Text(t, "then") };
        if (w.TryGetProperty("long", out var l) && l.ValueKind == JsonValueKind.Object)
            wall = wall with { LongFromS = Pair(l, "airborne_s", 0), LongToS = Pair(l, "airborne_s", 1) };
        return wall;
    }

    /// <summary>A squeeze's or a cover's <c>traverse.side_step</c>, or null.</summary>
    static ClipSideStep? SideStepOf(JsonElement tv) =>
        tv.TryGetProperty("side_step", out var s) && s.ValueKind == JsonValueKind.Object
            ? new ClipSideStep(Text(s, "kind"), Text(s, "wall"), Number(s, "step_m", float.NaN), Number(s, "cycle_s", float.NaN), Number(s, "body_front_m", float.NaN),
                               Number(s, "body_back_m", float.NaN), Number(s, "gap_min_m", float.NaN), Number(s, "wall_back_m", float.NaN),
                               Number(s, "stand_off_wall_m", float.NaN), Text(s, "then"))
            : null;

    /// <summary>A ladder climb's <c>climb</c>, or null.</summary>
    static ClipLadderClimb? LadderClimbOf(JsonElement tv) =>
        tv.TryGetProperty("climb", out var c) && c.ValueKind == JsonValueKind.Object
            ? new ClipLadderClimb((int)Number(c, "rungs", 0f), Number(c, "step_s", float.NaN), Text(c, "way"), Text(c, "loops_from"))
            : null;

    static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    /// <summary>The <paramref name="index"/>th number of a pair (<c>[a, b]</c>) named <paramref name="name"/>, NaN for none.</summary>
    static float Pair(JsonElement e, string name, int index) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > index && v[index].ValueKind == JsonValueKind.Number
            ? (float)v[index].GetDouble() : float.NaN;
    /// <summary>A text or a list of texts named <paramref name="name"/>, as a list (empty for none).</summary>
    static string[] Texts(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return [];
        if (v.ValueKind == JsonValueKind.String) return [v.GetString() ?? ""];
        if (v.ValueKind != JsonValueKind.Array) return [];
        var all = new List<string>();
        foreach (var x in v.EnumerateArray()) if (x.ValueKind == JsonValueKind.String) all.Add(x.GetString() ?? "");
        return [.. all];
    }
    static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    static float Number(JsonElement e, string name, float otherwise) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (float)v.GetDouble() : otherwise;
}
