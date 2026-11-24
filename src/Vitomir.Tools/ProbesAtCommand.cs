// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
using System.Globalization;
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Formats.L2;
using Vitomir.Render;
using Vitomir.Render.Modern;

namespace Vitomir;

/// <summary>
/// **Ours: what a cooked level's light probes hold at a point**
/// (<c>vitomir --probes-at &lt;package.l2&gt; --points "x,y,z;x,y,z" [--normal x,y,z]</c>, metres).
/// 1.1 has nothing like it. Written to answer with numbers why the door wall under the
/// University's porch is nearly black by day: for each point, the eight probes round it (which were baked
/// where they stand, which moved, which filled from their neighbours), how much of the sky the point sees,
/// and the irradiance the renderer's shader reads there on a surface facing the normal and on one facing up
/// — in all, and split into the sky it sees, the sun's bounce and the sky's bounce — beside the two things to
/// measure it against: the sun's own irradiance on open level ground, and the sky's there. No GPU is opened:
/// the sums are <see cref="ModernProbes.Compute"/>'s, the ones the renderer uploads, under the level's own
/// morning light (<see cref="CellWorldPass.SettingsFor"/>).
/// </summary>
public static class ProbesAtCommand
{
    /// <summary>Runs <c>--probes-at</c> when asked, answering the exit code; null when it was not.</summary>
    public static int? Handle(string[] args)
    {
        int at = Array.IndexOf(args, "--probes-at");
        if (at < 0) return null;
        string? Opt(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        static Vector3 V(string s) { var p = s.Split(','); return new Vector3(F(p[0]), F(p[1]), F(p[2])); }
        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        if (at + 1 >= args.Length || Opt("--points") is not { } pointList)
        {
            Console.Error.WriteLine("usage: vitomir --probes-at <package.l2> --points \"x,y,z;x,y,z\" [--normal x,y,z]   (metres)");
            return 2;
        }
        var package = Path.GetFullPath(args[at + 1].TrimEnd('/', '\\'));
        var level = CellLevel.Load(package);
        if (L2ProbeGrid.ReadPackage(package, level.Manifest) is not { } grid) { Console.Error.WriteLine($"probes-at: {package} has no light probes"); return 2; }
        var normal = Opt("--normal") is { } ns ? Vector3.Normalize(V(ns)) : -Vector3.UnitX;
        var settings = CellWorldPass.SettingsFor(level);
        var light = ModernLight.From(settings);
        var sky = ModernAmbient.Sky(light, settings);
        var bounce = ModernAmbient.Bounce(light, settings, sky);
        var skyAverage = ModernAmbient.Evaluate(sky, Vector3.UnitZ) / MathF.PI;
        var sun = light.SunColor * light.SunIntensity;
        var probes = new ModernProbes(grid);
        var all = probes.Compute(sky, bounce, sun, light.ToSun, skyAverage, settings.ProbeBounce);
        var seen = probes.Compute(sky, bounce, sun, light.ToSun, skyAverage, 0f);                          // no bounce at all: the sky each probe sees
        var withSun = probes.Compute(sky, bounce, sun, light.ToSun, Vector3.Zero, settings.ProbeBounce);   // the sky seen and the sun's bounce
        static float Lum(Vector3 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
        string S(Vector3 c) => string.Create(CultureInfo.InvariantCulture, $"{Lum(c),7:0.000}");
        var sunGround = sun * MathF.Max(light.ToSun.Z, 0f);
        var skyGround = ModernAmbient.Evaluate(sky, Vector3.UnitZ);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"probes-at: {grid.Nx} × {grid.Ny} × {grid.Nz} probes {grid.Spacing} m apart from ({grid.Origin.X:0.##}, {grid.Origin.Y:0.##}, {grid.Origin.Z:0.##}); baked for a sun {MathF.Asin(grid.ToSun.Z) * 180 / MathF.PI:0.#}° up; bounce × {settings.ProbeBounce}, share of the baked sun {probes.SunShare(light.ToSun):0.##}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"probes-at: irradiance (luminance of RGB) on open level ground: the sun {Lum(sunGround):0.000}, the sky {Lum(skyGround):0.000}; exposure {settings.Exposure:0.###}; normal ({normal.X:0.##}, {normal.Y:0.##}, {normal.Z:0.##})"));
        Console.WriteLine("probes-at:        point (m)         states of the 8 probes   sky seen |  facing the normal: all = sky + sun's bounce + sky's bounce |  facing up: all = sky + sun's + sky's");
        foreach (var text in pointList.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = V(text);
            var c = Vector3.Clamp((p - grid.Origin) / grid.Spacing, Vector3.Zero, new Vector3(grid.Nx - 1, grid.Ny - 1, grid.Nz - 1));
            int x0 = Math.Min((int)c.X, grid.Nx - 2), y0 = Math.Min((int)c.Y, grid.Ny - 2), z0 = Math.Min((int)c.Z, grid.Nz - 2);
            var f = c - new Vector3(x0, y0, z0);
            var states = new char[8];
            float visible = 0;
            for (int corner = 0; corner < 8; corner++)
            {
                int dx = corner & 1, dy = (corner >> 1) & 1, dz = corner >> 2;
                int i = grid.Index(x0 + dx, y0 + dy, z0 + dz);
                states[corner] = grid.State[i] switch { 1 => 'b', 2 => 'm', 0 => 'f', var s => (char)('0' + s) };
                float w = (dx == 1 ? f.X : 1 - f.X) * (dy == 1 ? f.Y : 1 - f.Y) * (dz == 1 ? f.Z : 1 - f.Z);
                visible += w * grid.Visibility[i * 9] / (2f * MathF.Sqrt(MathF.PI));
            }
            string Parts(Vector3 n)
            {
                var a = probes.IrradianceAt(all, p, n);
                var s0 = probes.IrradianceAt(seen, p, n);
                var s1 = probes.IrradianceAt(withSun, p, n);
                return $"{S(a)} = {S(s0)} + {S(s1 - s0)} + {S(a - s1)}";
            }
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"probes-at: ({p.X,6:0.00}, {p.Y,6:0.00}, {p.Z,5:0.00})  {new string(states)} (b baked, m moved, f filled)  {visible,6:0.000}  |  {Parts(normal)}  |  {Parts(Vector3.UnitZ)}"));
        }
        return 0;
    }
}
