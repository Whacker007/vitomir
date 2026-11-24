// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernRenderer.Probes.cs at b4185ef, reworked for the platform.
using System.Numerics;
using SDL;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// **Ours: the light probes in the frame** (<see cref="ModernProbes"/>).
/// Each frame the scene's probes are given this frame's sky (the same spherical harmonics as the
/// flat ambient light), the old model's surroundings, the sun and the sky's average radiance, and work their
/// light out again only when those changed; the surfaces' shader reads them through slot 14. Without probes
/// (or with <see cref="ModernSettings.Probes"/> off) a one-texel 3D texture stands in and the frame's
/// <c>ProbeOrigin.w</c> is 0, so the shader keeps the flat ambient light.
/// </summary>
internal sealed unsafe partial class ModernRenderer
{
    SDL_GPUTexture* probeStandIn;
    Vector3[] frameSky = new Vector3[9], frameBounce = new Vector3[9];

    bool ProbesOn(ModernScene scene) => Settings.Probes && scene.Probes is not null;

    void UpdateProbes(SDL_GPUCommandBuffer* cmd, ModernScene scene, ModernLight light, ref FrameUniforms frame)
    {
        frame.ProbeOrigin = Vector4.Zero;
        if (!ProbesOn(scene)) return;
        var probes = scene.Probes!;
        var skyAverage = ModernAmbient.Evaluate(frameSky, Vector3.UnitZ) / MathF.PI;
        var sun = light.SunColor * light.SunIntensity;
        probes.Update(gpu, cmd, frameSky, frameBounce, sun, light.ToSun, skyAverage, Settings.ProbeBounce);
        var g = probes.Grid;
        float units = ModernMath.UnitsPerMetre;
        frame.ProbeOrigin = new Vector4(g.Origin * units, 1);
        frame.ProbeGrid = new Vector4(1f / (g.Spacing * units), 0.5f * g.Spacing * units, ModernProbeRoom.Rise * units, g.Spacing * units);
        // The level's rooms (ours; ModernProbeRoom): inside one the probes are read within its own air.
        var rooms = probes.Rooms;
        int count = Math.Min(rooms.Count, ModernProbeRoom.Most);
        fixed (float* packed = frame.ProbeRooms)
            for (int i = 0; i < count; i++)
            {
                var (o, size) = (rooms[i].Origin * units, rooms[i].Size * units);
                float* a = packed + 4 * i, b = packed + 4 * (ModernProbeRoom.Most + i);
                (a[0], a[1], a[2], a[3]) = (o.X, o.Y, o.Z, MathF.Cos(rooms[i].RotZ));
                (b[0], b[1], b[2], b[3]) = (size.X, size.Y, size.Z, MathF.Sin(rooms[i].RotZ));
            }
        frame.ProbeDims = new Vector4(g.Nx, g.Ny, g.Nz, count);
    }

    /// <summary>The probes' texture for the surfaces' slot 14, or the stand-in.</summary>
    SDL_GPUTextureSamplerBinding ProbeBinding(ModernScene scene)
    {
        if (ProbesOn(scene) && scene.Probes!.Texture != null)
            return new SDL_GPUTextureSamplerBinding { texture = scene.Probes!.Texture, sampler = postSampler };
        if (probeStandIn == null)
        {
            var info = new SDL_GPUTextureCreateInfo
            {
                type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_3D, format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT,
                usage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER, width = 1, height = 1, layer_count_or_depth = 1,
                num_levels = 1, sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1,
            };
            probeStandIn = SDL_CreateGPUTexture(gpu.Device, &info);
            if (probeStandIn == null) throw new SdlException("SDL_CreateGPUTexture (the probes' stand-in)");
            var cmd = SDL_AcquireGPUCommandBuffer(gpu.Device);
            ModernProbes.Upload3D(gpu, cmd, probeStandIn, 1, 1, 1, (at, n) => { var h = (Half*)at; for (int i = 0; i < n; i++) h[i] = (Half)0f; });
            SDL_SubmitGPUCommandBuffer(cmd);
        }
        return new SDL_GPUTextureSamplerBinding { texture = probeStandIn, sampler = postSampler };
    }

    void DisposeProbes() => Release(ref probeStandIn);
}
