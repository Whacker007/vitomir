// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/D3D12SamplerBudget.cs at b4185ef, reworked for the platform.
// Ours: the count that keeps a frame of the modern renderer inside the one heap of
// sampler descriptors SDL's Direct3D 12 driver gives a command buffer. Arithmetic only, so it is tested
// without a device; ModernRenderer.Budget.cs feeds it the frame about to be drawn.
using System.Globalization;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// **How many sampler descriptors a frame's command buffer will write, and whether Direct3D 12 can take
/// them (ours).** SDL's Direct3D 12 driver (3.5.0 at a8591d9) gives a
/// command buffer one heap of <see cref="HeapSize"/> sampler descriptors (<c>SAMPLER_GPU_DESCRIPTOR_COUNT</c>).
/// At a draw whose pipeline is newly bound, or whose sampler bindings differ from the draw before
/// (<c>needFragmentSamplerBind</c>), it copies the pipeline's whole set of samplers to the heap's next
/// free descriptors (<c>D3D12_INTERNAL_WriteGPUDescriptors</c>, with its <c>// FIXME: need to error on
/// overflow</c>): it looks for a full heap only before it writes, never whether the set fits, so a set
/// that straddles the end is written past it — an access violation in <c>SDL_DrawGPUIndexedPrimitives</c>,
/// which is how the new town died on its first frame (a fast NVIDIA card). When it does
/// change heaps (<c>D3D12_INTERNAL_SetGPUDescriptorHeaps</c>) it does not write again the tables already set.
/// <para>This counts the same way — <see cref="Pipeline"/> at a bind, <see cref="Draw"/> at a draw with
/// the key its bindings follow from (a material) — and is an upper bound: two materials that happen to
/// bind the same textures count as a change. <see cref="Fits"/> refuses a frame only on Direct3D 12;
/// Vulkan has no such heap and is never refused.</para>
/// </summary>
internal sealed class D3D12SamplerBudget
{
    /// <summary>SDL's <c>SAMPLER_GPU_DESCRIPTOR_COUNT</c>: the sampler descriptors in a command buffer's one heap.</summary>
    public const int HeapSize = 2048;

    /// <summary>
    /// Kept back from <see cref="HeapSize"/> for what is not counted draw by draw: the frame's full-screen
    /// passes (clouds, ambient occlusion and the shafts with their blurs, reflections, the sky, temporal
    /// anti-aliasing, the flare, bloom's six levels down and five up, tone mapping, FXAA: 68 descriptors as
    /// built) and room to spare. What follows the modern world in the same command buffer — the game's
    /// own things, the interface — writes two at a time (<see cref="Gpu.FragmentSamplers"/>) and meets the
    /// heap's end exactly, where SDL's own check holds.
    /// </summary>
    public const int Margin = 128;

    /// <summary>The most a frame's counted draws may write on Direct3D 12.</summary>
    public const int Limit = HeapSize - Margin;

    /// <summary>The process's exit code when a run was stopped for this (ours).</summary>
    public const int ExitCode = 3;

    int samplers;
    bool need;
    long last;

    /// <summary>The sampler descriptors counted so far.</summary>
    public int Used { get; private set; }

    /// <summary>A pipeline of <paramref name="samplerSlots"/> fragment samplers bound: the next draw writes its whole set.</summary>
    public void Pipeline(int samplerSlots)
    {
        samplers = samplerSlots;
        need = true;
    }

    /// <summary>
    /// A draw whose sampler bindings follow from <paramref name="key"/> (a material's index, a texture's):
    /// it writes the bound pipeline's set when the pipeline is newly bound or the key is not the last draw's.
    /// </summary>
    public void Draw(long key)
    {
        if (!need && key == last) return;
        Used += samplers;
        need = false;
        last = key;
    }

    /// <summary>Descriptors written by something counted whole (a full-screen pass).</summary>
    public void Add(int descriptors) => Used += descriptors;

    /// <summary>Whether <paramref name="driver"/> (the device's name for itself) has the heap at all: only SDL's Direct3D 12.</summary>
    public static bool Limited(string driver) => driver == GpuDrivers.Direct3D12;

    /// <summary>Whether a frame that writes <paramref name="used"/> counted descriptors can be drawn on <paramref name="driver"/>.</summary>
    public static bool Fits(string driver, int used) => !Limited(driver) || used <= Limit;

    /// <summary>The one line a refused run says.</summary>
    public static string Refusal(int used) => string.Create(CultureInfo.InvariantCulture,
        $"gpu: this level needs more texture samplers in one frame than SDL's Direct3D 12 driver allows ({used:N0} of {HeapSize:N0}); run with --gpu {GpuDrivers.Vulkan}");

    // ----- the stop, once for the process -----

    /// <summary>Why the run was stopped (<see cref="Refusal"/>'s line), or null: the game's loop ends on it and the process answers <see cref="ExitCode"/>.</summary>
    public static string? Stopped { get; private set; }

    /// <summary>
    /// Stops the run: the first call keeps <paramref name="why"/> and says it on <paramref name="say"/>
    /// (the console's error stream when null); later calls change nothing, so it is said once.
    /// </summary>
    public static void Stop(string why, Action<string>? say = null)
    {
        if (Stopped is not null) return;
        Stopped = why;
        (say ?? Console.Error.WriteLine)(why);
    }

    /// <summary>The process's exit code: <see cref="ExitCode"/> when stopped, else <paramref name="code"/>.</summary>
    public static int Exit(int code) => Stopped is null ? code : ExitCode;

    /// <summary>Forgets a stop (the tests').</summary>
    public static void Forget() => Stopped = null;
}
