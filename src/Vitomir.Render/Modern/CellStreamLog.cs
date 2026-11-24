// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Globalization;

namespace Vitomir.Render.Modern;

/// <summary>
/// **Ours, a harness aid.** With <c>VITOMIR_STREAM_LOG=&lt;seconds&gt;</c> in the environment, a played cell
/// level prints a line at most that often while it is drawn: the picture's size, the streamer's cells and
/// far versions in, its loads and unloads so far, the geometry and textures on the GPU, the camera and the
/// process's memory — for watching a windowed run, where <c>--stream-fly</c>'s table is not written.
/// </summary>
public static class CellStreamLog
{
    static readonly double Every = double.TryParse(Environment.GetEnvironmentVariable("VITOMIR_STREAM_LOG"),
        NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0;
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static double last = double.NegativeInfinity;
    static int frames;

    /// <summary>Called once a frame, after the stream's update.</summary>
    public static void Frame(CellStreamScene? stream, int width, int height, System.Numerics.Vector3 camera)
    {
        frames++;
        if (Every <= 0 || stream is null || Clock.Elapsed.TotalSeconds - last < Every) return;
        last = Clock.Elapsed.TotalSeconds;
        using var proc = Process.GetCurrentProcess();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"stream: {last:0}s frame {frames} {width}x{height} at {camera.X / ModernMath.UnitsPerMetre:0},{camera.Y / ModernMath.UnitsPerMetre:0},{camera.Z / ModernMath.UnitsPerMetre:0} m; " +
            $"cells {stream.ResidentCells} (drawn {stream.DrawnCellsLod0}+{stream.DrawnCellsLod1}), far {stream.ResidentFar} (drawn {stream.DrawnFar}), " +
            $"loads {stream.Loads}, unloads {stream.Unloads}, geometry {stream.ResidentBytes / 1048576.0:0} MB, " +
            $"textures {(stream.Textures?.ResidentBytes ?? 0) / 1048576.0:0} MB; process {proc.WorkingSet64 / 1048576} MB, managed {GC.GetTotalMemory(false) / 1048576} MB"));
    }
}
