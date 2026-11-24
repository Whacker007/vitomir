// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// How far the thighs go through the tunic ("sometimes when at full run, the left thigh would clip through the bottom of
/// the tunic just a hair"), measured on the meshes as drawn: each tunic vertex skinned to the drawn pose, tested against the nearest skinned
/// vertex of the body's leg (its normal: inside by how much). A measurement, run with <c>VITOMIR_CLIP_TAPE</c> (a tape) and
/// <c>VITOMIR_CLIP_OUT</c> (a folder for its table).
/// </summary>
public sealed class SwingClipMeasure(ITestOutputHelper output)
{
    static string F(double v, string f = "0.0") => v.ToString(f, CultureInfo.InvariantCulture);

    /// <summary>The player's mesh, split into the tunic's vertices and each leg's skin vertices.</summary>
    public sealed class Meshes
    {
        public static string Names = "";
        public required SkinnedModel Model;
        public required int[] Tunic, LegL, LegR;
        public required int[] TunicLevel;
        public required int ThighL, CalfL, FootL, ThighR, CalfR, FootR;

        public static Meshes Of(SkinnedModel model)
        {
            var sk = model.Skeleton;
            var v = model.Vertices;
            // The tunic's parts (its wool, its patches) and the legs' surfaces (the hose, the braies, the skin), by material.
            var tunicVerts = new HashSet<int>();
            var legVerts = new HashSet<int>();
            foreach (var p in model.Parts)
            {
                string name = (p.Material?.Name ?? p.Texture).ToLowerInvariant();
                var into = name.Contains("tunic") || name.Contains("patch") ? tunicVerts
                         : name.Contains("hose") || name.Contains("linen") || name.Contains("skin") ? legVerts : null;
                if (into is null) continue;
                for (int i = p.FirstIndex; i < p.FirstIndex + p.IndexCount; i++) into.Add((int)model.Indices[i]);
            }
            bool Leg(int b, string side) { var n = sk.Bones[b].Name.ToLowerInvariant(); return n.EndsWith(side) && (n.Contains("thigh") || n.Contains("knee_bend_front")); }   // the thigh's own and the knee's front (not the calf's, which a bent knee brings round it)
            int Dominant(in SkinVertex x) { int best = x.Bone(0); float w = x.Weights.X; if (x.Weights.Y > w) { w = x.Weights.Y; best = x.Bone(1); } if (x.Weights.Z > w) { w = x.Weights.Z; best = x.Bone(2); } if (x.Weights.W > w) best = x.Bone(3); return best; }
            var tunic = new List<int>(); var level = new List<int>(); var l = new List<int>(); var r = new List<int>();
            for (int i = 0; i < v.Length; i++)
            {
                int d = Dominant(v[i]);
                string dn = sk.Bones[d].Name;
                if (tunicVerts.Contains(i)) { if (dn.StartsWith("swing_tunic", StringComparison.Ordinal)) { tunic.Add(i); level.Add(dn[^1] - '0'); } }
                else if (legVerts.Contains(i) && Leg(d, "_l")) l.Add(i);
                else if (legVerts.Contains(i) && Leg(d, "_r")) r.Add(i);
            }
            Names = string.Join(", ", model.Parts.Select(p => p.Material?.Name ?? p.Texture).Distinct());
            return new Meshes { Model = model, Tunic = [.. tunic], TunicLevel = [.. level], LegL = [.. l], LegR = [.. r],
                                ThighL = sk.Find("thigh_l"), CalfL = sk.Find("calf_l"), FootL = sk.Find("foot_l"),
                                ThighR = sk.Find("thigh_r"), CalfR = sk.Find("calf_r"), FootR = sk.Find("foot_r") };
        }

        static (Vector3 P, Vector3 N) Skinned(in SkinVertex x, ReadOnlySpan<Matrix4x4> skin)
        {
            Vector3 p = default, n = default;
            for (int k = 0; k < 4; k++)
            {
                float w = k switch { 0 => x.Weights.X, 1 => x.Weights.Y, 2 => x.Weights.Z, _ => x.Weights.W };
                if (w <= 0f) continue;
                p += Vector3.Transform(x.Position, skin[x.Bone(k)]) * w;
                n += Vector3.TransformNormal(x.Normal, skin[x.Bone(k)]) * w;
            }
            return (p, Vector3.Normalize(n));
        }

        /// <summary>
        /// The deepest a tunic vertex is inside each thigh by the chains' level (m; 0 none, [side, level]), and how many are more than 1 mm inside.
        /// The thigh as drawn is taken as a tube about its bone's line (thigh to knee): its surface's distance from the line in each of 20 places along
        /// it and 16 ways round it (the farthest of the leg's vertices there: its hose, braies, skin), a tunic vertex inside by how much nearer the line
        /// it is than that surface in its place and way.
        /// </summary>
        public (float[,] Deepest, int[] Count) Inside(ReadOnlySpan<Matrix4x4> skin, ReadOnlySpan<Matrix4x4> bones)
        {
            const int Along = 20, Round = 16;   // and a 21st place, the knee's front just past the joint
            var v = Model.Vertices;
            var deepest = new float[2, 5];
            var count = new int[2];
            var surface = new float[Along + 1, Round];
            var tunic = new Vector3[Tunic.Length];
            for (int t = 0; t < Tunic.Length; t++) tunic[t] = Skinned(v[Tunic[t]], skin).P;
            for (int side = 0; side < 2; side++)
            {
                var ids = side == 0 ? LegL : LegR;
                var frame = bones[side == 0 ? ThighL : ThighR];
                Vector3 a = frame.Translation, b = bones[side == 0 ? CalfL : CalfR].Translation;
                var ab = b - a;
                var axis = Vector3.Normalize(ab);
                var e1 = Vector3.Normalize(new Vector3(frame.M11, frame.M12, frame.M13) - axis * Vector3.Dot(new Vector3(frame.M11, frame.M12, frame.M13), axis));
                var e2 = Vector3.Cross(axis, e1);
                bool Place(Vector3 p, out int i, out int k, out float r)
                {
                    float s = Vector3.Dot(p - a, ab) / ab.LengthSquared();
                    var radial = p - (a + ab * s);
                    r = radial.Length();
                    i = (int)MathF.Floor(s * Along);
                    float ang = MathF.Atan2(Vector3.Dot(radial, e2), Vector3.Dot(radial, e1));
                    k = ((int)MathF.Floor((ang + MathF.PI) / (2f * MathF.PI) * Round)) % Round;
                    return i >= 2 && i < Along + 1;   // from a tenth of the way down (the hip's join with the body left out) to the knee and its front
                }
                Array.Clear(surface);
                foreach (int id in ids)
                    if (Place(Skinned(v[id], skin).P, out int i, out int k, out float r) && r < 0.2f) surface[i, k] = MathF.Max(surface[i, k], r);
                for (int t = 0; t < tunic.Length; t++)
                {
                    if (!Place(tunic[t], out int i, out int k, out float r) || surface[i, k] <= 0f || r > 0.2f) continue;
                    float inside = surface[i, k] - r;
                    if (inside <= 0f) continue;
                    if (inside > 0.001f) count[side]++;
                    int level = Math.Clamp(TunicLevel[t], 0, 4);
                    if (inside > deepest[side, level]) deepest[side, level] = inside;
                }
            }
            return (deepest, count);
        }

        static (int, int, int) Cell(Vector3 p) => ((int)MathF.Floor(p.X / 0.03f), (int)MathF.Floor(p.Y / 0.03f), (int)MathF.Floor(p.Z / 0.03f));

        /// <summary>How far the leg's surface (its hose, braies, skin) lies from the thigh's bone line at rest, by tenths of the way to the knee: (the largest, the 95th percentile) m.</summary>
        public (float Max, float P95)[] Extent(ReadOnlySpan<Matrix4x4> skin, ReadOnlySpan<Matrix4x4> bones, bool left, bool front = false)
        {
            var v = Model.Vertices;
            var ids = left ? LegL : LegR;
            Vector3 a = bones[left ? ThighL : ThighR].Translation, b = bones[left ? CalfL : CalfR].Translation;
            var ab = b - a;
            var bins = Enumerable.Range(0, 10).Select(_ => new List<float>()).ToArray();
            foreach (int i in ids)
            {
                var (p, _) = Skinned(v[i], skin);
                float s = Vector3.Dot(p - a, ab) / ab.LengthSquared();
                if (s < 0f || s >= 1f) continue;
                var radial = p - (a + ab * s);
                if (front && Vector3.Dot(Vector3.Normalize(radial), Vector3.UnitY) < 0.85f) continue;   // the front of the thigh (the person faces +Y)
                bins[(int)(s * 10)].Add(radial.Length());
            }
            return [.. bins.Select(l => l.Count == 0 ? (0f, 0f) : (l.Max(), l.OrderBy(x => x).ElementAt((int)(0.95 * (l.Count - 1)))))];
        }
    }

    [Fact]
    public void The_thighs_through_the_tunic_on_a_tape()
    {
        var tapePath = Environment.GetEnvironmentVariable("VITOMIR_CLIP_TAPE");
        Assert.SkipUnless(tapePath is { Length: > 0 } && File.Exists(tapePath), "VITOMIR_CLIP_TAPE is not set");
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL")!;
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable)!;
        string outDir = Environment.GetEnvironmentVariable("VITOMIR_CLIP_OUT") ?? Path.GetTempPath();
        int every = int.TryParse(Environment.GetEnvironmentVariable("VITOMIR_CLIP_RATE"), out int rr) ? rr : 60;
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-swing-clip-measure-clip"));
        var tape = Tape.Read(tapePath!);
        var options = new SessionOptions { Level = course, Game = GameInstall.Locate(), ModelsFolder = models, Say = _ => { } }.From(tape.Header)
                      with { Level = course, PersonSwing = every == 0 ? PersonSwingSettings.Off
                          : PersonSwingSettings.Default with { StepsPerSecond = every, KeepOutBetweenSteps = Environment.GetEnvironmentVariable("VITOMIR_CLIP_DRAW14CR") != "1" } };
        using var session = Session.Create(options);
        var world = session.World;
        var assets = world.Service<CharacterAssets>();
        var poses = world.Service<CharacterPoses>();
        var p = session.Player;
        var mesh = Meshes.Of(assets.ModelOf(world.Get<CharacterAnimation>(p).Kind));
        output.WriteLine(Meshes.Names);
        output.WriteLine($"tunic vertices {mesh.Tunic.Length}, left leg {mesh.LegL.Length}, right leg {mesh.LegR.Length}");
        string variant = Environment.GetEnvironmentVariable("VITOMIR_CLIP_NAME") ?? $"rate{every}";
        using var w = new StreamWriter(Path.Combine(outDir, $"clip-{variant}.tsv"));
        w.WriteLine("tick\tclip\tspeed\tstepped\tL0\tL1\tL2\tL3\tL4\tR0\tR1\tR2\tR3\tR4\tcountL\tcountR");
        var swings = world.TryService<PersonSwings>(out var sw) ? sw : null;
        float worstRun = 0f; long worstTick = 0;
        bool extent = false;
        for (int i = 0; i < tape.Frames.Count; i++)
        {
            int steps = swings?.Live(p)?.Steps ?? 0;
            session.Step(tape.Frames[i]);
            var anim = world.Get<CharacterAnimation>(p);
            string clip = anim.Clip >= 0 ? assets.Clip(anim.Clip).Name.Replace("people:player:", "") : "-";
            float speed = world.Get<Body>(p).Velocity is var vel ? MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y) : 0f;
            if (!extent && clip == "stand" && speed == 0f)
            {
                extent = true;
                var bonesAtRest = poses.Bones(world, p).ToArray();
                foreach (bool left in new[] { true, false })
                    foreach (bool front in new[] { false, true })
                        output.WriteLine($"{(left ? "left" : "right")} leg's surface{(front ? " in front" : "")} from the thigh's line (tick {session.Tick}, standing), by tenths to the knee, max/p95 mm: "
                                         + string.Join("  ", mesh.Extent(poses.Skin(world, p), bonesAtRest, left, front).Select(e => $"{F(e.Max * 1000, "0")}/{F(e.P95 * 1000, "0")}")));
            }
            if (speed < 2.5f && i % 30 != 0) continue;   // the runs and sprints, and now and then the rest
            var skin = poses.Skin(world, p).ToArray();
            var bones = poses.Bones(world, p).ToArray();
            var (deep, count) = mesh.Inside(skin, bones);
            bool stepped = (swings?.Live(p)?.Steps ?? 0) != steps;
            var cells = new List<string> { session.Tick.ToString(CultureInfo.InvariantCulture), clip, F(speed, "0.00"), stepped ? "1" : "0" };
            for (int side = 0; side < 2; side++) for (int l = 0; l < 5; l++) cells.Add(F(deep[side, l] * 1000));
            cells.Add(count[0].ToString(CultureInfo.InvariantCulture)); cells.Add(count[1].ToString(CultureInfo.InvariantCulture));
            w.WriteLine(string.Join('\t', cells));
            float most = 0f; for (int side = 0; side < 2; side++) for (int l = 0; l < 5; l++) most = MathF.Max(most, deep[side, l]);
            if (speed >= 2.5f && most > worstRun) { worstRun = most; worstTick = session.Tick; }
        }
        output.WriteLine($"the deepest at a run or sprint: {F(worstRun * 1000)} mm at tick {worstTick}");
    }
}
