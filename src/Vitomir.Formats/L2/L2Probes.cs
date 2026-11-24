// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A cell level's baked light probes. All of this is ours: 1.1 lights its
// levels with lightmaps and has nothing like it. Lengths are metres, Z up, as the manifest's.
using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace Vitomir.Formats.L2;

/// <summary>
/// <c>PROB</c> version 1: a regular grid of irradiance probes over a level's open air, baked by the cook
/// (ours). Probe (x, y, z) stands at
/// <see cref="Origin"/> + (x, y, z) · <see cref="Spacing"/> and is number (z · <see cref="Ny"/> + y) ·
/// <see cref="Nx"/> + x. Each holds, as spherical harmonics in the order of
/// <c>ModernAmbient.Basis</c> (Y00; Y1−1, y, z, x —; then band 2):
/// <list type="bullet">
/// <item><see cref="Visibility"/>: nine coefficients of where the probe sees open sky (1) or something (0),
/// so the renderer can light it by whatever sky it has, at any time;</item>
/// <item><see cref="SunBounce"/>: four RGB coefficients (band 0 and 1) of the radiance the probe sees on the
/// surfaces round it from one bounce of a sun of irradiance 1 shining from <see cref="ToSun"/> (white light;
/// the renderer multiplies by its sun's colour and strength), shadowed by ray;</item>
/// <item><see cref="SkyBounce"/>: four RGB coefficients of the radiance seen on those surfaces from the open
/// sky above them, per unit of the sky's average radiance (the renderer multiplies by its sky's).</item>
/// </list>
/// <see cref="State"/> says which probes were baked where they stand (1), baked a little way off after being
/// found inside something (2), or filled from their neighbours because they are inside a wall or the ground (0).
/// The body after the header is deflated.
/// </summary>
public sealed class L2ProbeGrid
{
    public const string Tag = "PROB";
    public const uint Version1 = 1;
    /// <summary>The package file the grid is kept in (an <see cref="L2Cell"/> holding one <c>PROB</c> chunk), and the manifest's <c>extras</c> key naming it.</summary>
    public const string FileName = "probes.cell", ExtrasKey = "probes";

    public int Nx { get; init; }
    public int Ny { get; init; }
    public int Nz { get; init; }
    /// <summary>The first probe's place, metres.</summary>
    public Vector3 Origin { get; init; }
    /// <summary>The distance between neighbouring probes, metres, the same along every axis.</summary>
    public float Spacing { get; init; }
    /// <summary>The direction towards the sun the bounce was baked for (unit, Z up).</summary>
    public Vector3 ToSun { get; init; }
    /// <summary>Rays cast from each probe.</summary>
    public int Rays { get; init; }
    public required byte[] State { get; init; }
    public required float[] Visibility { get; init; }
    public required float[] SunBounce { get; init; }
    public required float[] SkyBounce { get; init; }

    public int Count => Nx * Ny * Nz;
    public int Index(int x, int y, int z) => (z * Ny + y) * Nx + x;
    public Vector3 Position(int x, int y, int z) => Origin + new Vector3(x, y, z) * Spacing;

    public L2Chunk Encode()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Nx); w.Write(Ny); w.Write(Nz);
            w.Write(Origin.X); w.Write(Origin.Y); w.Write(Origin.Z);
            w.Write(Spacing);
            w.Write(ToSun.X); w.Write(ToSun.Y); w.Write(ToSun.Z);
            w.Write(Rays);
            w.Write(1u);   // flags: bit 0, the body is deflated
        }
        using (var z = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var w = new BinaryWriter(z, Encoding.UTF8))
        {
            w.Write(State);
            foreach (var a in new[] { Visibility, SunBounce, SkyBounce })
                foreach (var f in a) w.Write((Half)f);
        }
        return new L2Chunk(Tag, Version1, ms.ToArray());
    }

    public static L2ProbeGrid Decode(L2Chunk chunk)
    {
        if (chunk.Version != Version1) throw new NodFormatException($"{Tag}: version {chunk.Version} unknown");
        var r = new BinReader(chunk.Payload, Tag);
        int nx = r.I32(), ny = r.I32(), nz = r.I32();
        if (nx <= 0 || ny <= 0 || nz <= 0 || (long)nx * ny * nz > 16_000_000) throw r.Error($"a grid of {nx} × {ny} × {nz}");
        var origin = r.Vec3();
        float spacing = r.F32();
        var toSun = r.Vec3();
        int rays = r.I32();
        uint flags = r.U32();
        int n = nx * ny * nz;
        var body = r.Bytes(chunk.Payload.Length - r.Pos);
        Stream s = new MemoryStream(body);
        if ((flags & 1) != 0) s = new DeflateStream(s, CompressionMode.Decompress);
        using var br = new BinaryReader(s);
        var state = br.ReadBytes(n);
        float[] Halves(int count)
        {
            var bytes = br.ReadBytes(count * 2);
            if (bytes.Length != count * 2) throw new NodFormatException($"{Tag}: the body ends early");
            var f = new float[count];
            for (int i = 0; i < count; i++) f[i] = (float)BitConverter.ToHalf(bytes, i * 2);
            return f;
        }
        if (state.Length != n) throw new NodFormatException($"{Tag}: the body ends early");
        return new L2ProbeGrid
        {
            Nx = nx, Ny = ny, Nz = nz, Origin = origin, Spacing = spacing, ToSun = toSun, Rays = rays,
            State = state, Visibility = Halves(n * 9), SunBounce = Halves(n * 12), SkyBounce = Halves(n * 12),
        };
    }

    /// <summary>Writes the grid into <paramref name="packageFolder"/>'s <see cref="FileName"/>; the caller names it in the manifest.</summary>
    public long WriteFile(string packageFolder)
    {
        var bytes = new L2Cell { Chunks = [Encode()] }.Write();
        File.WriteAllBytes(Path.Combine(packageFolder, FileName), bytes);
        return bytes.Length;
    }

    /// <summary>The grid a package's manifest names in its extras, or null when it has none (a level cooked before probes, or without them).</summary>
    public static L2ProbeGrid? ReadPackage(string packageFolder, L2Manifest manifest)
    {
        if (manifest.Extras is null || !manifest.Extras.TryGetPropertyValue(ExtrasKey, out var node) || node is null) return null;
        var path = Path.Combine(packageFolder, node.GetValue<string>());
        if (!File.Exists(path)) return null;
        var cell = L2Cell.ReadFile(path, t => t == Tag);
        return cell.Find(Tag) is { } chunk ? Decode(chunk) : null;
    }
}
