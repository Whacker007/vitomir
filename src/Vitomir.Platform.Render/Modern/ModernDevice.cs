// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Render/Modern/ModernDevice.cs at b4185ef, reworked for the platform.
using System.Reflection;
using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Modern;

/// <summary>
/// The modern path's own GPU helpers over <see cref="Gpu"/>'s device (ours): shaders with more than two
/// sampler slots, textures in any format with a mip chain, anisotropic and comparison samplers, and a
/// batch upload. <see cref="Gpu"/> keeps the 1999 path's rules; these are the same calls with the
/// modern path's needs. **Sampler slots stay even** for every fragment shader, for the reason
/// <see cref="Gpu.FragmentSamplers"/> gives (Direct3D 12's descriptor heap).
/// </summary>
internal static unsafe class ModernDevice
{
    /// <summary>Creates a shader from the embedded bytecode (Shaders/&lt;name&gt;.vert|frag.spv|dxil).</summary>
    public static SDL_GPUShader* LoadShader(Gpu gpu, string name, SDL_GPUShaderStage stage, int samplers, int uniformBuffers) =>
        LoadShader(gpu, name, stage, samplers, uniformBuffers, 0);

    /// <summary>As <see cref="LoadShader(Gpu, string, SDL_GPUShaderStage, int, int)"/>, with read-only storage buffers after the samplers.</summary>
    public static SDL_GPUShader* LoadShader(Gpu gpu, string name, SDL_GPUShaderStage stage, int samplers, int uniformBuffers, int storageBuffers)
    {
        if (stage == SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT && samplers % 2 != 0)
            throw new ArgumentException("a fragment shader's sampler slots stay even (Gpu.FragmentSamplers)", nameof(samplers));
        string ext = gpu.ShaderFormat == SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL ? "dxil" : "spv";
        string suffix = stage == SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX ? "vert" : "frag";
        string resource = $"Shaders/{name}.{suffix}.{ext}";
        using var s = typeof(Gpu).Assembly.GetManifestResourceStream(resource)
                      ?? throw new FileNotFoundException($"embedded shader {resource} missing");
        var code = new byte[s.Length];
        s.ReadExactly(code);
        var entry = System.Text.Encoding.UTF8.GetBytes(stage == SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX ? "VSMain\0" : "PSMain\0");
        fixed (byte* pCode = code)
        fixed (byte* pEntry = entry)
        {
            var info = new SDL_GPUShaderCreateInfo
            {
                code_size = (nuint)code.Length, code = pCode, entrypoint = pEntry, format = gpu.ShaderFormat, stage = stage,
                num_samplers = (uint)samplers, num_uniform_buffers = (uint)uniformBuffers, num_storage_buffers = (uint)storageBuffers,
            };
            var shader = SDL_CreateGPUShader(gpu.Device, &info);
            if (shader == null) throw new SdlException($"SDL_CreateGPUShader({resource})");
            return shader;
        }
    }

    /// <summary>The number of mip levels a full chain for this size has.</summary>
    public static int MipLevels(int width, int height) => (int)Math.Floor(Math.Log2(Math.Max(1, Math.Max(width, height)))) + 1;

    /// <summary>
    /// A sampled RGBA8 texture (sRGB-encoded when <paramref name="srgb"/>, so the hardware decodes colour
    /// to linear light before filtering) with its full mip chain made on the GPU, uploaded on
    /// <paramref name="copy"/>; the mips are generated after the copy pass by the caller
    /// (<see cref="Uploader"/>).
    /// </summary>
    public static SDL_GPUTexture* CreateTexture(Gpu gpu, int width, int height, bool srgb, int levels)
    {
        var info = new SDL_GPUTextureCreateInfo
        {
            type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D,
            format = srgb ? SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM_SRGB : SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM,
            usage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER | (levels > 1 ? SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET : 0),
            width = (uint)width, height = (uint)height, layer_count_or_depth = 1, num_levels = (uint)levels,
            sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1,
        };
        var tex = SDL_CreateGPUTexture(gpu.Device, &info);
        if (tex == null) throw new SdlException("SDL_CreateGPUTexture (modern)");
        return tex;
    }

    /// <summary>A trilinear sampler with anisotropic filtering at <paramref name="anisotropy"/> (off at 1 or less).</summary>
    public static SDL_GPUSampler* CreateSampler(Gpu gpu, bool repeatU, bool repeatV, bool mirrorU, bool mirrorV, float anisotropy)
    {
        static SDL_GPUSamplerAddressMode Mode(bool repeat, bool mirror) =>
            mirror ? SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_MIRRORED_REPEAT
            : repeat ? SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_REPEAT : SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE;
        var info = new SDL_GPUSamplerCreateInfo
        {
            min_filter = SDL_GPUFilter.SDL_GPU_FILTER_LINEAR, mag_filter = SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
            mipmap_mode = SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_LINEAR,
            address_mode_u = Mode(repeatU, mirrorU), address_mode_v = Mode(repeatV, mirrorV),
            address_mode_w = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_REPEAT,
            min_lod = 0, max_lod = 1000,
            enable_anisotropy = anisotropy > 1f, max_anisotropy = Math.Max(1f, anisotropy),
        };
        var s = SDL_CreateGPUSampler(gpu.Device, &info);
        if (s == null) throw new SdlException("SDL_CreateGPUSampler (modern)");
        return s;
    }

    /// <summary>A bilinear clamped sampler that compares against a depth reference (the shadow maps' PCF).</summary>
    public static SDL_GPUSampler* CreateShadowSampler(Gpu gpu)
    {
        var info = new SDL_GPUSamplerCreateInfo
        {
            min_filter = SDL_GPUFilter.SDL_GPU_FILTER_LINEAR, mag_filter = SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
            mipmap_mode = SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_NEAREST,
            address_mode_u = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
            address_mode_v = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
            address_mode_w = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
            enable_compare = true, compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS_OR_EQUAL,
            min_lod = 0, max_lod = 0,
        };
        var s = SDL_CreateGPUSampler(gpu.Device, &info);
        if (s == null) throw new SdlException("SDL_CreateGPUSampler (shadow)");
        return s;
    }

    /// <summary>A nearest-texel clamped sampler (depth and normals read back by the full-screen passes).</summary>
    public static SDL_GPUSampler* CreatePointSampler(Gpu gpu)
    {
        var info = new SDL_GPUSamplerCreateInfo
        {
            min_filter = SDL_GPUFilter.SDL_GPU_FILTER_NEAREST, mag_filter = SDL_GPUFilter.SDL_GPU_FILTER_NEAREST,
            mipmap_mode = SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_NEAREST,
            address_mode_u = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
            address_mode_v = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
            address_mode_w = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
            min_lod = 0, max_lod = 0,
        };
        var s = SDL_CreateGPUSampler(gpu.Device, &info);
        if (s == null) throw new SdlException("SDL_CreateGPUSampler (point)");
        return s;
    }

    /// <summary>A buffer the shaders read as a structured buffer, of <paramref name="bytes"/> bytes.</summary>
    public static SDL_GPUBuffer* CreateStorageBuffer(Gpu gpu, int bytes)
    {
        var info = new SDL_GPUBufferCreateInfo { usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_GRAPHICS_STORAGE_READ, size = (uint)Math.Max(bytes, 16) };
        var b = SDL_CreateGPUBuffer(gpu.Device, &info);
        if (b == null) throw new SdlException("SDL_CreateGPUBuffer (storage)");
        return b;
    }

    /// <summary>Whether the device can sample a depth target of <paramref name="format"/> (ambient occlusion and sun shafts read the depth buffer).</summary>
    public static bool CanSampleDepth(Gpu gpu, SDL_GPUTextureFormat format) =>
        SDL_GPUTextureSupportsFormat(gpu.Device, format, SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D,
            SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);

    /// <summary>The depth format for a sampled shadow map: 32-bit float when the device can sample it, else 16-bit.</summary>
    public static SDL_GPUTextureFormat ShadowDepthFormat(Gpu gpu)
    {
        var usage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER;
        return SDL_GPUTextureSupportsFormat(gpu.Device, SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT, SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, usage)
            ? SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT : SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D16_UNORM;
    }

    /// <summary>
    /// Many uploads in one command buffer: buffers and textures are queued with their bytes, and
    /// <see cref="Submit"/> copies them all in one copy pass, makes the textures' mips, and waits.
    /// </summary>
    public sealed class Uploader(Gpu gpu)
    {
        readonly List<(byte[] Data, nint Buffer, nint Texture, int W, int H, bool Mips, int Level)> items = [];

        public SDL_GPUBuffer* Buffer<T>(SDL_GPUBufferUsageFlags usage, ReadOnlySpan<T> data) where T : unmanaged
        {
            var bytes = MemoryMarshal.AsBytes(data).ToArray();
            if (bytes.Length == 0) return null;
            var info = new SDL_GPUBufferCreateInfo { usage = usage, size = (uint)bytes.Length };
            var buffer = SDL_CreateGPUBuffer(gpu.Device, &info);
            if (buffer == null) throw new SdlException("SDL_CreateGPUBuffer (modern)");
            items.Add((bytes, (nint)buffer, 0, 0, 0, false, 0));
            return buffer;
        }

        public SDL_GPUTexture* Texture(int width, int height, byte[] rgba, bool srgb, bool mips)
        {
            int levels = mips ? MipLevels(width, height) : 1;
            var tex = CreateTexture(gpu, width, height, srgb, levels);
            items.Add((rgba, 0, (nint)tex, width, height, levels > 1, 0));
            return tex;
        }

        /// <summary>
        /// A texture whose mip levels are given (<paramref name="levels"/>, the first <paramref name="width"/> ×
        /// <paramref name="height"/>, each next one half as large, at least 1), not made by the GPU: a cut-out's coverage-keeping
        /// mips (<c>CharacterTextureSize.CoverageMips</c>).
        /// </summary>
        public SDL_GPUTexture* TextureLevels(int width, int height, byte[][] levels, bool srgb)
        {
            var tex = CreateTexture(gpu, width, height, srgb, levels.Length);
            for (int l = 0; l < levels.Length; l++)
                items.Add((levels[l], 0, (nint)tex, Math.Max(1, width >> l), Math.Max(1, height >> l), false, l));
            return tex;
        }

        static long Align(long n) => (n + 511) & ~511L;

        public void Submit()
        {
            // In slices, so no one transfer buffer grows past about 64 MB.
            int at = 0;
            while (at < items.Count)
            {
                long total = 0; int end = at;
                // Each item starts on a 512-byte boundary (Direct3D 12's placement rule for texture copies).
                while (end < items.Count && (end == at || total + items[end].Data.Length < 64L << 20)) total += Align(items[end++].Data.Length);
                var tinfo = new SDL_GPUTransferBufferCreateInfo { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = (uint)total };
                var transfer = SDL_CreateGPUTransferBuffer(gpu.Device, &tinfo);
                if (transfer == null) throw new SdlException("SDL_CreateGPUTransferBuffer (modern)");
                var map = (byte*)SDL_MapGPUTransferBuffer(gpu.Device, transfer, false);
                long o = 0;
                for (int i = at; i < end; i++) { items[i].Data.CopyTo(new Span<byte>(map + o, items[i].Data.Length)); o += Align(items[i].Data.Length); }
                SDL_UnmapGPUTransferBuffer(gpu.Device, transfer);
                var cmd = SDL_AcquireGPUCommandBuffer(gpu.Device);
                var copy = SDL_BeginGPUCopyPass(cmd);
                o = 0;
                for (int i = at; i < end; i++)
                {
                    var it = items[i];
                    if (it.Buffer != 0)
                    {
                        var src = new SDL_GPUTransferBufferLocation { transfer_buffer = transfer, offset = (uint)o };
                        var dst = new SDL_GPUBufferRegion { buffer = (SDL_GPUBuffer*)it.Buffer, offset = 0, size = (uint)it.Data.Length };
                        SDL_UploadToGPUBuffer(copy, &src, &dst, false);
                    }
                    else
                    {
                        var src = new SDL_GPUTextureTransferInfo { transfer_buffer = transfer, offset = (uint)o, pixels_per_row = (uint)it.W, rows_per_layer = (uint)it.H };
                        var dst = new SDL_GPUTextureRegion { texture = (SDL_GPUTexture*)it.Texture, mip_level = (uint)it.Level, w = (uint)it.W, h = (uint)it.H, d = 1 };
                        SDL_UploadToGPUTexture(copy, &src, &dst, false);
                    }
                    o += Align(it.Data.Length);
                }
                SDL_EndGPUCopyPass(copy);
                for (int i = at; i < end; i++) if (items[i].Mips) SDL_GenerateMipmapsForGPUTexture(cmd, (SDL_GPUTexture*)items[i].Texture);
                var fence = SDL_SubmitGPUCommandBufferAndAcquireFence(cmd);
                SDL_WaitForGPUFences(gpu.Device, true, &fence, 1);
                SDL_ReleaseGPUFence(gpu.Device, fence);
                SDL_ReleaseGPUTransferBuffer(gpu.Device, transfer);
                at = end;
            }
            items.Clear();
        }
    }
}
