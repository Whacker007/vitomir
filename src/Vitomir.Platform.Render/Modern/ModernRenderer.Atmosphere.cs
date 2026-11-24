// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernRenderer.Atmosphere.cs at b4185ef, reworked for the platform.
using System.Numerics;
using SDL;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// **Ours: the physically based sky's tables on the GPU** (<see cref="ModernAtmosphere"/>; the sky and the
/// air). The sky's view and the aerial perspective's two atlases are worked out on the CPU
/// when the sun moves (by more than a twentieth of a degree) or the eye's height changes (by more than a tenth,
/// or 20 m), and copied up as half floats; the sky, the surfaces and the reflections read them. Off — a look
/// without <see cref="ModernSettings.Atmosphere"/> — the tables stay as they are and are not read.
/// </summary>
internal sealed unsafe partial class ModernRenderer
{
    SDL_GPUTexture* skyViewTex, apInTex, apTrTex;
    (ModernAtmosphere? Atm, Vector3 Sun, float AltitudeKm) atmosphereKey;

    /// <summary>How many times the tables were worked out (for tests and the log).</summary>
    public int AtmosphereUpdates { get; private set; }

    const SDL_GPUTextureFormat LutFormat = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT;

    void EnsureAtmosphereTextures()
    {
        if (skyViewTex != null) return;
        var use = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER;
        skyViewTex = gpu.CreateTarget(ModernAtmosphere.SkyW, ModernAtmosphere.SkyH, LutFormat, use);
        int aw = ModernAtmosphere.ApAz * ModernAtmosphere.ApColumns, ah = ModernAtmosphere.ApEl * (ModernAtmosphere.ApSlices / ModernAtmosphere.ApColumns);
        apInTex = gpu.CreateTarget(aw, ah, LutFormat, use);
        apTrTex = gpu.CreateTarget(aw, ah, LutFormat, use);
    }

    static float AltitudeKm(FreeCamera camera, float groundZ) =>
        MathF.Max((camera.Position.Z - groundZ) / ModernMath.UnitsPerMetre / 1000f, 0.002f);

    /// <summary>The frame's <c>Atmo</c> constants: on, the sun's illuminance above the air, the eye's altitude (km), the horizon's elevation there.</summary>
    Vector4 AtmosphereUniforms(ModernLight light, FreeCamera camera, float groundZ)
    {
        if (!light.Physical) return Vector4.Zero;
        float alt = AltitudeKm(camera, groundZ);
        return new Vector4(1f, light.SunIntensity, alt, ModernAtmosphere.HorizonElevation(alt));
    }

    /// <summary>Works the tables out again and queues their upload on <paramref name="cmd"/> when the sun or the eye's height has moved.</summary>
    void UpdateAtmosphere(SDL_GPUCommandBuffer* cmd, ModernLight light, FreeCamera camera, float groundZ)
    {
        EnsureAtmosphereTextures();
        if (!light.Physical) return;
        var atm = ModernAtmosphere.For(Settings.AtmosphereAerosols, Settings.GroundAlbedo);
        float alt = AltitudeKm(camera, groundZ);
        var key = atmosphereKey;
        if (key.Atm == atm && Vector3.Dot(key.Sun, light.ToSun) > MathF.Cos(0.05f * MathF.PI / 180f)
            && MathF.Abs(key.AltitudeKm - alt) <= MathF.Max(0.02f, 0.1f * key.AltitudeKm)) return;
        atmosphereKey = (atm, light.ToSun, alt);
        AtmosphereUpdates++;
        var sky = atm.SkyView(light.ToSun, alt);
        var (ins, tra) = atm.AerialPerspective(light.ToSun, alt);
        int aw = ModernAtmosphere.ApAz * ModernAtmosphere.ApColumns, ah = ModernAtmosphere.ApEl * (ModernAtmosphere.ApSlices / ModernAtmosphere.ApColumns);
        Upload(cmd, [((nint)skyViewTex, sky, ModernAtmosphere.SkyW, ModernAtmosphere.SkyH), ((nint)apInTex, ins, aw, ah), ((nint)apTrTex, tra, aw, ah)]);
    }

    void Upload(SDL_GPUCommandBuffer* cmd, (nint Texture, Vector3[] Data, int W, int H)[] items)
    {
        long total = items.Sum(i => (long)i.W * i.H * 8);
        var tinfo = new SDL_GPUTransferBufferCreateInfo { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = (uint)total };
        var transfer = SDL_CreateGPUTransferBuffer(gpu.Device, &tinfo);
        if (transfer == null) throw new SdlException("SDL_CreateGPUTransferBuffer (atmosphere)");
        var map = (Half*)SDL_MapGPUTransferBuffer(gpu.Device, transfer, false);
        long o = 0;
        foreach (var it in items)
            for (int k = 0; k < it.W * it.H; k++)
            {
                var v = it.Data[k];
                map[o++] = (Half)v.X; map[o++] = (Half)v.Y; map[o++] = (Half)v.Z; map[o++] = (Half)1f;
            }
        SDL_UnmapGPUTransferBuffer(gpu.Device, transfer);
        var copy = SDL_BeginGPUCopyPass(cmd);
        uint offset = 0;
        foreach (var it in items)
        {
            var src = new SDL_GPUTextureTransferInfo { transfer_buffer = transfer, offset = offset, pixels_per_row = (uint)it.W, rows_per_layer = (uint)it.H };
            var dst = new SDL_GPUTextureRegion { texture = (SDL_GPUTexture*)it.Texture, w = (uint)it.W, h = (uint)it.H, d = 1 };
            SDL_UploadToGPUTexture(copy, &src, &dst, false);
            offset += (uint)(it.W * it.H * 8);
        }
        SDL_EndGPUCopyPass(copy);
        SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
    }

    /// <summary>The three tables' bindings, for the surface, sky and reflection shaders.</summary>
    void AtmosphereBindings(SDL_GPUTextureSamplerBinding* at)
    {
        EnsureAtmosphereTextures();
        at[0] = new SDL_GPUTextureSamplerBinding { texture = skyViewTex, sampler = postSampler };
        at[1] = new SDL_GPUTextureSamplerBinding { texture = apInTex, sampler = postSampler };
        at[2] = new SDL_GPUTextureSamplerBinding { texture = apTrTex, sampler = postSampler };
    }

    void DisposeAtmosphere()
    {
        Release(ref skyViewTex); Release(ref apInTex); Release(ref apTrTex);
    }
}
