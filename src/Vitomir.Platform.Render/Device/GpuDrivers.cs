// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Which of SDL's GPU drivers the game opens (ours; nothing of 1.1's, which is Direct3D 7
// through rlDirect.dll). The rules are here, so they can be tested without a device; the part that asks
// SDL is `Open`, below them.
using SDL;

namespace Vitomir.Platform.Render;

/// <summary>
/// **The order of GPU drivers to try (ours).** On Windows the levels build opens Vulkan first and SDL's
/// own choice (Direct3D 12) only when no Vulkan device can be made; elsewhere SDL chooses. <c>--gpu
/// &lt;name&gt;</c> names one driver and nothing else is tried.
/// <para>Why Vulkan: SDL's Direct3D 12 driver (3.5.0 at a8591d9)
/// gives a command buffer one heap of 2,048 sampler descriptors and writes past its end when a frame
/// needs more (<see cref="Modern.D3D12SamplerBudget"/>); a frame of the new town does. Vulkan has no such
/// heap. So Vulkan now, and a Direct3D 12 run that says why instead of crashing.</para>
/// </summary>
public static unsafe class GpuDrivers
{
    /// <summary>SDL's name for its Vulkan driver.</summary>
    public const string Vulkan = "vulkan";

    /// <summary>SDL's name for its Direct3D 12 driver.</summary>
    public const string Direct3D12 = "direct3d12";

    /// <summary>
    /// The drivers to try, in order; null is SDL's own choice. <paramref name="asked"/> (<c>--gpu</c>) is
    /// tried alone; with nothing asked, Windows tries <see cref="Vulkan"/> and then SDL's choice, and
    /// every other system SDL's choice only.
    /// </summary>
    public static string?[] Order(string? asked, bool windows) =>
        !string.IsNullOrWhiteSpace(asked) ? [asked.Trim()]
        : windows ? [Vulkan, null]
        : [null];

    /// <summary>
    /// The console's line for the driver taken: <paramref name="driver"/> is the device's own name for
    /// itself, <paramref name="asked"/> what <c>--gpu</c> said, <paramref name="refused"/> why an earlier
    /// driver in the order was passed over (null when the first one was taken).
    /// </summary>
    public static string Line(string driver, string? asked, string? refused, bool windows)
    {
        if (!string.IsNullOrWhiteSpace(asked)) return $"gpu: {driver} (--gpu {asked.Trim()})";
        if (refused is not null) return $"gpu: {driver} ({refused})";
        if (windows && driver == Vulkan)
            return $"gpu: {driver} (the default for Vitomir's levels build; --gpu {Direct3D12} for Direct3D 12)";
        return $"gpu: {driver} (SDL's choice; --gpu <name> for another)";
    }

    /// <summary>Why a driver was passed over, for <see cref="Line"/>: <c>no Vulkan device: &lt;SDL's error&gt;</c>.</summary>
    public static string Refused(string? driver, string error) =>
        $"no {(driver == Vulkan ? "Vulkan" : driver ?? "default")} device: {(string.IsNullOrWhiteSpace(error) ? "SDL gave no reason" : error.Trim())}";

    /// <summary>
    /// The device: each driver of <see cref="Order"/> tried in turn, one whose device cannot be made (or
    /// cannot take <paramref name="window"/>) falling through to the next; the last one's failure is
    /// thrown as it always was. <paramref name="say"/> is told the one line of <see cref="Line"/>.
    /// </summary>
    public static Gpu Open(SDL_Window* window, string? asked, bool debug, Action<string> say)
    {
        bool windows = OperatingSystem.IsWindows();
        var order = Order(asked, windows);
        string? refused = null;
        for (int i = 0; ; i++)
        {
            try
            {
                var gpu = new Gpu(window, order[i], debug);
                say(Line(gpu.Driver, asked, refused, windows));
                return gpu;
            }
            catch (SdlException e) when (i + 1 < order.Length)
            {
                // "SDL_CreateGPUDevice: <SDL's error>": the error alone is what the line carries.
                int colon = e.Message.IndexOf(": ", StringComparison.Ordinal);
                refused = Refused(order[i], colon >= 0 ? e.Message[(colon + 2)..] : e.Message);
            }
        }
    }
}
