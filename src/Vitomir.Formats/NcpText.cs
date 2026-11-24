// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A camera path's recooked text (ours): its keys alone, the curves between them built again as the path editor built them.
using System.Globalization;
using System.Numerics;

namespace Vitomir.Formats;

/// <summary>
/// **A camera path as text** (ours): <c>paths/X.ncp</c> recooked is <c>paths/X.txt</c>, a <c>[key n]</c> section a keyframe with its
/// <c>position</c> and its <c>angles</c> (pitch, roll, yaw in degrees). The original file also holds each key's distance along the path
/// and the cubic to the next key, which the path editor worked out and the engine plays as stored; here they are worked out again from
/// the keys (<see cref="Build"/>), by the editor's own rule, so a key can be moved, added or taken out and the path stays smooth:
/// <list type="bullet">
/// <item>**Where the camera is**: a cubic between two keys (Hermite), at rest at the first and the last key; at a key between, it
/// passes along the sum of the unit directions from the key before and to the key after, as fast as half of each segment's
/// length (each side its own).</item>
/// <item>**Where it looks**: a cubic (Catmull-Rom) on each angle, turning the short way round; at rest at the first and last key.</item>
/// </list>
/// Checked against the 174 shipped paths: every segment the camera can play comes out the file's to within float rounding. (A segment
/// between two keys at one spot is never played — the player passes over it — and is not checked.)
/// </summary>
public static class NcpText
{
    public const string Extension = ".txt";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Write(NcpCameraPath path, string? from = null)
    {
        var w = new RecookText.Writer();
        if (from is not null) w.Comment($"recooked from {from}");
        for (int i = 0; i < path.Keyframes.Length; i++)
        {
            w.Section("key", (i + 1).ToString(Inv));
            w.Key("position", TagTable.Numbers(path.Keyframes[i].Position));
            w.Key("angles", TagTable.Numbers(path.Keyframes[i].Angles));
        }
        return w.ToString();
    }

    public static NcpCameraPath Read(RecookText text)
    {
        if (text.Top.Entries.Count > 0) throw text.Error(text.Top.Entries[0].Line, "a path's keys are in [key n] sections");
        var keys = new List<(Vector3 Position, Vector3 Angles)>();
        foreach (var s in text.Sections.Skip(1))
        {
            if (!s.Name.Equals("key", StringComparison.OrdinalIgnoreCase)) throw text.Error(s.Line, $"[{s.Name}]: a path has only [key n] sections");
            var p = TagTable.ReadVector(s["position"] ?? "") ?? throw text.Error(s.Line, "a key needs its position: three numbers");
            var a = TagTable.ReadVector(s["angles"] ?? "") ?? throw text.Error(s.Line, "a key needs its angles: pitch, roll, yaw");
            keys.Add((p, a));
        }
        if (keys.Count is < 1 or > 128) throw text.Error(1, $"a path has 1 to 128 keys, not {keys.Count}");
        return Build(keys);
    }

    static float Turn(float x)
    {
        while (x > 180f) x -= 360f;
        while (x < -180f) x += 360f;
        return x;
    }

    static Vector3 Turn(Vector3 v) => new(Turn(v.X), Turn(v.Y), Turn(v.Z));

    static Vector3 Unit(Vector3 v)
    {
        float l = v.Length();
        return l > 0f ? v / l : Vector3.Zero;
    }

    /// <summary>A path from its keys: the distances along it and the cubics, as the path editor makes them.</summary>
    public static NcpCameraPath Build(IReadOnlyList<(Vector3 Position, Vector3 Angles)> keys)
    {
        int n = keys.Count;
        var length = new float[Math.Max(n - 1, 0)];
        for (int i = 0; i + 1 < n; i++) length[i] = Vector3.Distance(keys[i].Position, keys[i + 1].Position);

        // The tangents: (into the key, out of it).
        (Vector3 In, Vector3 Out) Moving(int i)
        {
            if (i == 0 || i == n - 1) return (Vector3.Zero, Vector3.Zero);
            var before = keys[i].Position - keys[i - 1].Position;
            var after = keys[i + 1].Position - keys[i].Position;
            var along = Unit(before) + Unit(after);
            return (along * (before.Length() / 2f), along * (after.Length() / 2f));
        }
        Vector3 Looking(int i) =>
            i == 0 || i == n - 1 ? Vector3.Zero : (Turn(keys[i + 1].Angles - keys[i].Angles) + Turn(keys[i].Angles - keys[i - 1].Angles)) / 2f;

        var frames = new NcpKeyframe[n];
        float at = 0f;
        for (int i = 0; i < n; i++)
        {
            var ps = new float[9];
            var sp = new float[9];
            float inv = 0f;
            if (i + 1 < n)
            {
                inv = length[i] > 0f ? 1f / length[i] : 0f;
                var t0 = Moving(i).Out;
                var t1 = Moving(i + 1).In;
                var d = keys[i + 1].Position - keys[i].Position;
                Put(ps, t0, 3f * d - 2f * t0 - t1, -2f * d + t0 + t1);
                var a0 = Looking(i);
                var a1 = Looking(i + 1);
                var da = Turn(keys[i + 1].Angles - keys[i].Angles);
                Put(sp, a0, 3f * da - 2f * a0 - a1, -2f * da + a0 + a1);
            }
            frames[i] = new NcpKeyframe(at, inv, keys[i].Position, ps, keys[i].Angles, sp);
            if (i + 1 < n) at += length[i];
        }
        return new NcpCameraPath { TotalLength = at, Keyframes = frames };

        static void Put(float[] into, Vector3 linear, Vector3 square, Vector3 cube)
        {
            into[0] = linear.X; into[1] = linear.Y; into[2] = linear.Z;
            into[3] = square.X; into[4] = square.Y; into[5] = square.Z;
            into[6] = cube.X; into[7] = cube.Y; into[8] = cube.Z;
        }
    }

    /// <summary>A path written back as the engine reads it: the count, the length, then each keyframe's 104 bytes.</summary>
    public static byte[] WriteBinary(NcpCameraPath path)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((uint)path.Keyframes.Length);
        w.Write(path.TotalLength);
        foreach (var k in path.Keyframes)
        {
            w.Write(k.Distance); w.Write(k.InvSegmentLength);
            w.Write(k.Position.X); w.Write(k.Position.Y); w.Write(k.Position.Z);
            foreach (var f in k.PositionSpline) w.Write(f);
            w.Write(k.Angles.X); w.Write(k.Angles.Y); w.Write(k.Angles.Z);
            foreach (var f in k.AngleSpline) w.Write(f);
        }
        return ms.ToArray();
    }
}
