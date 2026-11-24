// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Reflection;
using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

namespace Vitomir.Platform.Render;

/// <summary>Thrown when an SDL call fails; carries SDL's error text.</summary>
public sealed class SdlException(string what) : Exception($"{what}: {SDL_GetError()}");

/// <summary>
/// The SDL_GPU device (Direct3D 12, Vulkan or Metal, chosen by SDL), plus upload helpers.
/// Works with a window (swapchain) or headless (offscreen targets only).
/// </summary>
public sealed unsafe class Gpu : IDisposable
{
    public SDL_GPUDevice* Device { get; }
    public SDL_Window* Window { get; }
    public string Driver { get; }
    /// <summary>The shader bytecode this device consumes: SPIR-V (Vulkan) or DXIL (Direct3D 12).</summary>
    public SDL_GPUShaderFormat ShaderFormat { get; }
    /// <summary>Format of the frame's colour target (the swapchain's, or RGBA8 headless).</summary>
    public SDL_GPUTextureFormat ColorFormat { get; }
    public SDL_GPUTextureFormat DepthFormat { get; }
    /// <summary>Whether the colour format's bytes are in blue, green, red, alpha order (a window's swapchain, on every driver Vitomir
    /// runs on): a download of it has red and blue to swap for RGBA (what the Classic controller asks, naming no SDL type).</summary>
    public bool ColourIsBgra => ColorFormat is SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM or SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM_SRGB;
    /// <summary>The shader format's name, as the log prints it.</summary>
    public string ShaderFormatName => ShaderFormat.ToString();

    public Gpu(SDL_Window* window, string? driver = null, bool debug = false)
    {
        Window = window;
        var formats = SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV | SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL;
        Device = SDL_CreateGPUDevice(formats, debug, driver);
        if (Device == null) throw new SdlException("SDL_CreateGPUDevice");
        Driver = SDL_GetGPUDeviceDriver(Device) ?? "?";
        var supported = SDL_GetGPUShaderFormats(Device);
        ShaderFormat = (supported & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL) != 0 && Driver == "direct3d12"
            ? SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL
            : SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV;
        if (window != null)
        {
            if (!SDL_ClaimWindowForGPUDevice(Device, window))
            {
                // The device let go before the failure is thrown, so another driver can be tried (ours; GpuDrivers.Open).
                var refused = new SdlException("SDL_ClaimWindowForGPUDevice");
                SDL_DestroyGPUDevice(Device);
                throw refused;
            }
            ColorFormat = SDL_GetGPUSwapchainTextureFormat(Device, window);
        }
        else ColorFormat = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM;
        DepthFormat = PickDepthFormat();
    }

    /// <summary>
    /// The swapchain presented without waiting for the screen (immediate, else mailbox), for a run whose
    /// time is not the wall clock's — a replay check, whose every step comes from its tape — so it runs
    /// as fast as the machine draws. Answers whether it could.
    /// </summary>
    public bool Unthrottle()
    {
        if (Window == null) return false;
        foreach (var mode in new[] { SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_IMMEDIATE, SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_MAILBOX })
            if (SDL_WindowSupportsGPUPresentMode(Device, Window, mode)
                && SDL_SetGPUSwapchainParameters(Device, Window, SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_SDR, mode))
                return true;
        return false;
    }

    SDL_GPUTextureFormat PickDepthFormat()
    {
        foreach (var f in new[] { SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT, SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D24_UNORM })
            if (SDL_GPUTextureSupportsFormat(Device, f, SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET))
                return f;
        return SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D16_UNORM;
    }

    /// <summary>
    /// **Every fragment shader here is given two sampler slots, whether it reads one or two**.
    /// SDL's Direct3D 12 backend (3.5.0 at a8591d9, and still upstream) checks for a full
    /// descriptor heap only <em>before</em> writing a draw's descriptors, never whether they fit: at
    /// 2047 of 2048 a draw with two samplers writes its second one past the heap's end
    /// (<c>D3D12_INTERNAL_WriteGPUDescriptors</c>). The world's draws take two and the presenter's and
    /// the interface's took one, all in the frame's one command buffer, so the count could be odd when
    /// the party heads — the first two-sampler draws after the present — came to draw; a write past the
    /// end is an access violation when nothing is mapped there (twice in play, both in
    /// <see cref="HeadsPass"/>). With every draw writing two, the count stays even and meets the end
    /// exactly, where SDL's check catches it. A shader that reads only slot 0 is legal against a
    /// layout with two, on Direct3D 12 and Vulkan alike. Ours; nothing of 1.1's.
    /// </summary>
    public const int FragmentSamplers = 2;

    /// <summary>
    /// Binds one texture to a fragment shader that reads one: the same binding in both slots
    /// (<see cref="FragmentSamplers"/>), so the draw writes two descriptors like every other.
    /// </summary>
    public static void BindFragmentSampler(SDL_GPURenderPass* pass, SDL_GPUTextureSamplerBinding binding)
    {
        var both = stackalloc SDL_GPUTextureSamplerBinding[FragmentSamplers];
        for (int i = 0; i < FragmentSamplers; i++) both[i] = binding;
        SDL_BindGPUFragmentSamplers(pass, 0, both, FragmentSamplers);
    }

    /// <summary>Creates a shader from the bytecode embedded at build time (Shaders/&lt;name&gt;.vert|frag.spv|dxil).</summary>
    public SDL_GPUShader* LoadShader(string name, SDL_GPUShaderStage stage, int samplers, int uniformBuffers)
    {
        if (stage == SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT && samplers != FragmentSamplers)
            throw new ArgumentException($"a fragment shader has {FragmentSamplers} sampler slots here, not {samplers} (Gpu.FragmentSamplers)", nameof(samplers));
        string ext = ShaderFormat == SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL ? "dxil" : "spv";
        string suffix = stage == SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX ? "vert" : "frag";
        string resource = $"Shaders/{name}.{suffix}.{ext}";
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                      ?? throw new FileNotFoundException($"embedded shader {resource} missing");
        var code = new byte[s.Length];
        s.ReadExactly(code);
        var entry = System.Text.Encoding.UTF8.GetBytes(stage == SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX ? "VSMain\0" : "PSMain\0");
        fixed (byte* pCode = code)
        fixed (byte* pEntry = entry)
        {
            var info = new SDL_GPUShaderCreateInfo
            {
                code_size = (nuint)code.Length, code = pCode, entrypoint = pEntry, format = ShaderFormat, stage = stage,
                num_samplers = (uint)samplers, num_uniform_buffers = (uint)uniformBuffers,
            };
            var shader = SDL_CreateGPUShader(Device, &info);
            if (shader == null) throw new SdlException($"SDL_CreateGPUShader({resource})");
            return shader;
        }
    }

    /// <summary>
    /// A vertex or index buffer holding <paramref name="data"/>, or <c>null</c> when there is
    /// nothing to hold.
    ///
    /// A zero-byte buffer is not a buffer. SDL passes the size straight through to the backend,
    /// and D3D12 rejects a zero-width resource outright (<c>0x80070057</c>, "The parameter is
    /// incorrect"); Vulkan's spec forbids it too, though SwiftShader happily hands one back, which
    /// is exactly how an empty mesh reached a machine with a real driver before anyone noticed.
    /// So an empty span is answered with <c>null</c>, and the callers below treat a null buffer as
    /// "nothing to draw" — see <see cref="WorldRenderer.Sync"/> and <see cref="SkyRenderer"/>.
    /// </summary>
    public SDL_GPUBuffer* CreateBuffer<T>(SDL_GPUBufferUsageFlags usage, ReadOnlySpan<T> data) where T : unmanaged
    {
        var bytes = MemoryMarshal.AsBytes(data);
        uint length = (uint)bytes.Length;
        if (length == 0) return null;
        var info = new SDL_GPUBufferCreateInfo { usage = usage, size = length };
        var buffer = SDL_CreateGPUBuffer(Device, &info);
        if (buffer == null) throw new SdlException("SDL_CreateGPUBuffer");
        Upload(bytes, (copy, transfer) =>
        {
            var src = new SDL_GPUTransferBufferLocation { transfer_buffer = transfer, offset = 0 };
            var dst = new SDL_GPUBufferRegion { buffer = buffer, offset = 0, size = length };
            SDL_UploadToGPUBuffer(copy, &src, &dst, false);
        });
        return buffer;
    }

    /// <summary>A sampled RGBA8 texture, with a full mip chain generated on the GPU when <paramref name="mipmaps"/>.</summary>
    public SDL_GPUTexture* CreateTexture(int width, int height, ReadOnlySpan<byte> rgba, bool mipmaps)
    {
        uint levels = mipmaps ? (uint)Math.Floor(Math.Log2(Math.Max(width, height))) + 1 : 1;
        var usage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER;
        if (levels > 1) usage |= SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET;   // needed to generate mips
        var info = new SDL_GPUTextureCreateInfo
        {
            type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM,
            usage = usage, width = (uint)width, height = (uint)height, layer_count_or_depth = 1, num_levels = levels,
            sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1,
        };
        var tex = SDL_CreateGPUTexture(Device, &info);
        if (tex == null) throw new SdlException("SDL_CreateGPUTexture");
        Upload(rgba, (copy, transfer) =>
        {
            var src = new SDL_GPUTextureTransferInfo { transfer_buffer = transfer, offset = 0, pixels_per_row = (uint)width, rows_per_layer = (uint)height };
            var dst = new SDL_GPUTextureRegion { texture = tex, w = (uint)width, h = (uint)height, d = 1 };
            SDL_UploadToGPUTexture(copy, &src, &dst, false);
        }, cmd => { if (levels > 1) SDL_GenerateMipmapsForGPUTexture(cmd, tex); });
        return tex;
    }

    /// <summary>
    /// Replaces the whole of a texture made by <see cref="CreateTexture"/>. For a film, where a new
    /// picture arrives 25 times a second and making and throwing away a texture each time would
    /// churn the device for no reason. <paramref name="mipmaps"/> makes the smaller levels again
    /// from the new picture, for a texture made with them (a material a film plays on).
    /// </summary>
    public void UpdateTexture(SDL_GPUTexture* texture, int width, int height, ReadOnlySpan<byte> rgba, bool mipmaps = false)
    {
        if (texture == null || rgba.Length < width * height * 4) return;
        Upload(rgba, (copy, transfer) =>
        {
            var src = new SDL_GPUTextureTransferInfo { transfer_buffer = transfer, offset = 0, pixels_per_row = (uint)width, rows_per_layer = (uint)height };
            var dst = new SDL_GPUTextureRegion { texture = texture, w = (uint)width, h = (uint)height, d = 1 };
            SDL_UploadToGPUTexture(copy, &src, &dst, false);
        }, cmd => { if (mipmaps && Math.Max(width, height) > 1) SDL_GenerateMipmapsForGPUTexture(cmd, texture); });
    }

    /// <summary>A render target (colour or depth) the size of the frame.</summary>
    public SDL_GPUTexture* CreateTarget(int width, int height, SDL_GPUTextureFormat format, SDL_GPUTextureUsageFlags usage)
    {
        var info = new SDL_GPUTextureCreateInfo
        {
            type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, format = format, usage = usage, width = (uint)width,
            height = (uint)height, layer_count_or_depth = 1, num_levels = 1, sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1,
        };
        var tex = SDL_CreateGPUTexture(Device, &info);
        if (tex == null) throw new SdlException("SDL_CreateGPUTexture (target)");
        return tex;
    }

    public SDL_GPUSampler* CreateSampler(bool repeat, bool mipmaps)
    {
        var mode = repeat ? SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_REPEAT : SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE;
        var info = new SDL_GPUSamplerCreateInfo
        {
            min_filter = SDL_GPUFilter.SDL_GPU_FILTER_LINEAR, mag_filter = SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
            mipmap_mode = SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_LINEAR,
            address_mode_u = mode, address_mode_v = mode, address_mode_w = mode,
            min_lod = 0, max_lod = mipmaps ? 1000 : 0,
        };
        var s = SDL_CreateGPUSampler(Device, &info);
        if (s == null) throw new SdlException("SDL_CreateGPUSampler");
        return s;
    }

    delegate void CopyAction(SDL_GPUCopyPass* copy, SDL_GPUTransferBuffer* transfer);
    delegate void CommandAction(SDL_GPUCommandBuffer* cmd);

    void Upload(ReadOnlySpan<byte> bytes, CopyAction copyAction, CommandAction? after = null)
    {
        var tinfo = new SDL_GPUTransferBufferCreateInfo { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = (uint)bytes.Length };
        var transfer = SDL_CreateGPUTransferBuffer(Device, &tinfo);
        if (transfer == null) throw new SdlException("SDL_CreateGPUTransferBuffer");
        var map = SDL_MapGPUTransferBuffer(Device, transfer, false);
        bytes.CopyTo(new Span<byte>((void*)map, bytes.Length));
        SDL_UnmapGPUTransferBuffer(Device, transfer);
        var cmd = SDL_AcquireGPUCommandBuffer(Device);
        var copy = SDL_BeginGPUCopyPass(cmd);
        copyAction(copy, transfer);
        SDL_EndGPUCopyPass(copy);
        after?.Invoke(cmd);
        SDL_SubmitGPUCommandBuffer(cmd);
        SDL_ReleaseGPUTransferBuffer(Device, transfer);
    }

    /// <summary>Reads back an RGBA8 target (headless screenshots). Blocks until the GPU is done.</summary>
    public byte[] Download(SDL_GPUTexture* texture, int width, int height)
    {
        int size = width * height * 4;
        var tinfo = new SDL_GPUTransferBufferCreateInfo { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_DOWNLOAD, size = (uint)size };
        var transfer = SDL_CreateGPUTransferBuffer(Device, &tinfo);
        var cmd = SDL_AcquireGPUCommandBuffer(Device);
        var copy = SDL_BeginGPUCopyPass(cmd);
        var src = new SDL_GPUTextureRegion { texture = texture, w = (uint)width, h = (uint)height, d = 1 };
        var dst = new SDL_GPUTextureTransferInfo { transfer_buffer = transfer, pixels_per_row = (uint)width, rows_per_layer = (uint)height };
        SDL_DownloadFromGPUTexture(copy, &src, &dst);
        SDL_EndGPUCopyPass(copy);
        var fence = SDL_SubmitGPUCommandBufferAndAcquireFence(cmd);
        SDL_WaitForGPUFences(Device, true, &fence, 1);
        SDL_ReleaseGPUFence(Device, fence);
        var result = new byte[size];
        var map = SDL_MapGPUTransferBuffer(Device, transfer, false);
        new ReadOnlySpan<byte>((void*)map, size).CopyTo(result);
        SDL_UnmapGPUTransferBuffer(Device, transfer);
        SDL_ReleaseGPUTransferBuffer(Device, transfer);
        return result;
    }

    public void Dispose()
    {
        if (Window != null) SDL_ReleaseWindowFromGPUDevice(Device, Window);
        SDL_DestroyGPUDevice(Device);
    }
}
