// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Formats;

/// <summary>A camera keyframe (104 B, the engine's in-memory record).</summary>
/// <param name="Distance">Cumulative path length at this keyframe (0 for the first).</param>
/// <param name="InvSegmentLength">1 / distance to the next keyframe (junk on the last).</param>
/// <param name="PositionSpline">
/// The cubic to the next keyframe, grouped by power: x, y and z of the linear term, then of the
/// square, then of the cube — derived by the path editor when the file was made, and played as
/// stored.
/// </param>
/// <param name="Angles">Pitch, roll, yaw in degrees.</param>
/// <param name="AngleSpline">Derived, as <paramref name="PositionSpline"/>.</param>
public sealed record NcpKeyframe(float Distance, float InvSegmentLength, Vector3 Position, float[] PositionSpline,
                                 Vector3 Angles, float[] AngleSpline);

/// <summary>
/// NCP camera path, played by <c>CodexCamera.PlayPath</c>. Position and angles
/// are the authored data; the spline fields are the editor's derivation of them, and they are what
/// the engine plays — nothing recomputes them on load.
/// </summary>
public sealed class NcpCameraPath
{
    public float TotalLength { get; init; }
    public NcpKeyframe[] Keyframes { get; init; } = [];

    public static NcpCameraPath Read(byte[] data, string name = "<ncp>")
    {
        var r = new BinReader(data, name);
        uint n = r.U32();
        if (n is < 1 or > 128) throw r.Error($"keyframe count {n} outside 1..128");
        float total = r.F32();
        var kfs = new NcpKeyframe[n];
        for (int i = 0; i < n; i++)
            kfs[i] = new NcpKeyframe(r.F32(), r.F32(), r.Vec3(), r.Floats(9), r.Vec3(), r.Floats(9));
        r.ExpectEof();
        if (n > 1)
        {
            // Cross-check the stored distances against the positions (float32 vs double: 1% tolerance).
            double acc = 0;
            for (int i = 1; i < n; i++)
            {
                acc += Vector3.Distance(kfs[i - 1].Position, kfs[i].Position);
                if (Math.Abs(kfs[i].Distance - acc) > 0.01 * Math.Max(1.0, acc))
                    throw r.Error($"cumulative distance {kfs[i].Distance} != {acc}");
            }
            if (Math.Abs(total - acc) > 0.01 * Math.Max(1.0, acc)) throw r.Error($"total length {total} != {acc}");
        }
        return new NcpCameraPath { TotalLength = total, Keyframes = kfs };
    }
}
