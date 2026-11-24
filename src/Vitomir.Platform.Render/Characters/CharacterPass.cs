// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Modelled on src/Vitomir.Render/CellThingPass.cs at b4185ef (its materials and its use of the modern
// renderer's extra geometry); the skinning is the platform's own.
using System.Numerics;
using System.Runtime.InteropServices;
using SDL;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render.Modern;

namespace Vitomir.Platform.Render.Characters;

/// <summary>
/// The frame's characters, skinned on the CPU into world space and handed to the modern renderer as its
/// extra geometry (<see cref="ModernExtra"/>), as the old program hands it 1.1's characters in a cell level:
/// so they are drawn by the same shader as the world, into the sun's shadow cascades (they cast their
/// shadows on the street and on themselves), the depth-and-normal prepass (ambient occlusion at their feet)
/// and the surfaces (the sun through the shadow maps, the sky's and the probes' ambient light, the torches,
/// the haze), then the same tone mapping.
/// <para>
/// Skinning: a vertex is <c>Σ wᵢ · (v × Skin[bᵢ])</c> in model space (row vectors; <see cref="DrawnCharacter.Skin"/>
/// is inverse bind × posed bone), then × <see cref="DrawnCharacter.World"/> (metres), then into the renderer's
/// units. Normals go through the same matrices' rotations and are normalised.
/// </para>
/// <para>
/// Materials (the old program's, chosen by eye): the part's texture is the base colour, read as sRGB; every
/// surface a dielectric of roughness 0.7 and the renderer's default reflectance; a part with
/// <see cref="ModelPart.AlphaTest"/> drops texels whose alpha is under 1/255, as 1.1's masked batches do. Parts
/// are drawn two-sided, so a model's winding does not matter. A UV of (0, 0) is the image's first row (its
/// top, as <see cref="TextureImage"/> stores it and as glTF has it).
/// </para>
/// <para>
/// The models repository's people (parts with a <see cref="PartMaterial"/>) are drawn by their materials
/// (<see cref="CharacterBatch"/>): glTF's metallic-roughness with the occlusion-roughness-metal map and the normal map, the
/// base colour × its factor × the vertex colour, cut-outs, each part one- or two-sided as its material says, the cornea not at
/// all. A texture is uploaded once in the colour space of the slot that names it (base colour sRGB, the maps linear), the same
/// pixels once whatever names them, and no larger than <see cref="TextureLimit"/> (halved by a box filter until it fits).
/// </para>
/// </summary>
internal sealed class CharacterPass : ICharacterTextures, IDisposable
{
    /// <summary>The roughness of a character's surfaces (the old program's <c>CellThingPass.Roughness</c>, chosen by eye).</summary>
    public const float Roughness = 0.7f;
    /// <summary>The reflectance factor (<c>KHR_materials_specular</c>'s, over the 4% of a dielectric; the old program's <c>CellThingPass.Specular</c>).</summary>
    public const float Specular = 0.5f;
    /// <summary>Below this alpha a cut-out part's texel is dropped (1.1's masked batches; the old program's <c>WorldRenderer.AlphaTestThreshold</c>).</summary>
    public const float AlphaCutoff = 1f / 255f;

    readonly Dictionary<string, int> colourTextures = new(StringComparer.OrdinalIgnoreCase), dataTextures = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<byte[], int> colourPixels = new(ReferenceEqualityComparer.Instance), dataPixels = new(ReferenceEqualityComparer.Instance);
    // Cut-out base colours by name and cut-off, and by pixels and cut-off (their mips depend on the cut-off).
    readonly Dictionary<(string, float), int> cutTextures = [];
    readonly Dictionary<(byte[], float), int> cutPixels = [];
    readonly CharacterBatch batch;

    public ModernExtra Extra { get; }

    /// <summary>A character part's texture by its name (the program's asset registry); null draws the part white.</summary>
    public Func<string, TextureImage?>? Textures { get; set; }

    /// <summary>
    /// The largest a character's texture is uploaded, in texels a side (<see cref="CharacterTextureSize.Limit"/>:
    /// 1024, 2048 or 4096, the player's <c>characterTextureLimit</c>); a larger one is halved by a box filter until it fits. Read
    /// when a texture is first uploaded.
    /// </summary>
    public int TextureLimit { get; set; } = CharacterTextureSize.DefaultLimit;

    /// <summary>Whether a person's cut-outs get coverage-keeping mips (else the GPU's own, as 1.1's); a comparison aid for the stills.</summary>
    internal bool KeepCoverage { get; set; } = true;

    /// <summary><see cref="CharacterBatch.FirstPersonCut"/> (a tuning aid for the stills).</summary>
    internal float FirstPersonCut { get => batch.FirstPersonCut; set => batch.FirstPersonCut = value; }

    /// <summary>The GPU memory the characters' textures take, bytes (RGBA8 and their mips).</summary>
    public long TextureBytes { get; private set; }

    public CharacterPass(Gpu gpu)
    {
        Extra = new ModernExtra(gpu);
        batch = new CharacterBatch(this, Extra.Draws);
    }

    /// <summary>Skins and uploads <paramref name="characters"/> on <paramref name="cmd"/>, before the frame's render passes.</summary>
    public unsafe void Prepare(SDL_GPUCommandBuffer* cmd, IReadOnlyList<DrawnCharacter> characters) => Prepare(cmd, characters, default);

    /// <summary>
    /// Skins and uploads <paramref name="characters"/> for <paramref name="view"/> (the people in it, the distant less often:
    /// <see cref="Skinning"/>), timing the work (<see cref="LastCost"/>).
    /// </summary>
    internal unsafe void Prepare(SDL_GPUCommandBuffer* cmd, IReadOnlyList<DrawnCharacter> characters, in SkinView view)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        batch.Build(characters, view);
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (batch.Draws.Count > 0) Extra.Upload(cmd, batch.Vertices, batch.Indices);
        long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
        var (skinned, reused, culled) = batch.LastPeople;
        LastCost = new CharacterCost(System.Diagnostics.Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds, System.Diagnostics.Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds,
                                     batch.Vertices.Length, skinned, reused, culled);
    }

    /// <summary>How the people are skinned when there are several (<see cref="PeopleSkinning"/>).</summary>
    public PeopleSkinning Skinning { get => batch.Skinning; set => batch.Skinning = value; }

    /// <summary>The last frame's characters' cost on the CPU (<see cref="CharacterCost"/>).</summary>
    public CharacterCost LastCost { get; private set; }

    /// <summary>
    /// Whether the triangle <paramref name="a"/>, <paramref name="b"/>, <paramref name="c"/> of <paramref name="v"/> is
    /// wound counter-clockwise seen from the side its vertices' normals point to (the renderer's front face): its winding's
    /// normal and their sum agree. A model's own winding is not relied on (1.1's parts are drawn two-sided).
    /// </summary>
    internal static bool Outward(ReadOnlySpan<ModernVertex> v, uint a, uint b, uint c)
    {
        if (a >= v.Length || b >= v.Length || c >= v.Length) return true;
        var face = Vector3.Cross(v[(int)b].Position - v[(int)a].Position, v[(int)c].Position - v[(int)a].Position);
        return Vector3.Dot(face, v[(int)a].Normal + v[(int)b].Normal + v[(int)c].Normal) >= 0f;
    }

    /// <summary>
    /// One character's vertices skinned into <paramref name="into"/> in the renderer's units, and the box
    /// round them. A bone index past the skin's matrices counts as the identity. (1.1's models; the models repository's people
    /// are skinned by <see cref="CharacterBatch.SkinPerson"/>.)
    /// </summary>
    internal static (Vector3 Min, Vector3 Max) Skin(SkinnedModel model, Matrix4x4[] skin, Matrix4x4 world, Span<ModernVertex> into)
    {
        var place = world * Matrix4x4.CreateScale(ModernMath.UnitsPerMetre);
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        var source = model.Vertices;
        for (int i = 0; i < source.Length; i++)
        {
            ref readonly var v = ref source[i];
            Vector3 p = default, n = default;
            float weights = 0f;
            for (int k = 0; k < 4; k++)
            {
                float w = v.Weights[k];
                if (w == 0f) continue;
                int b = v.Bone(k);
                if (b < skin.Length)
                {
                    p += Vector3.Transform(v.Position, skin[b]) * w;
                    n += Vector3.TransformNormal(v.Normal, skin[b]) * w;
                }
                else { p += v.Position * w; n += v.Normal * w; }
                weights += w;
            }
            if (weights == 0f) { p = v.Position; n = v.Normal; }
            var at = Vector3.Transform(p, place);
            n = Vector3.TransformNormal(n, world);
            float len = n.Length();
            into[i] = new ModernVertex
            {
                Position = at, Normal = len > 1e-6f ? n / len : Vector3.UnitZ, Tangent = new Vector4(1, 0, 0, 1),
                Uv0 = v.Uv, Uv1 = v.Uv, Color = 0xffffffff,
            };
            min = Vector3.Min(min, at);
            max = Vector3.Max(max, at);
        }
        return (min, max);
    }

    /// <summary>
    /// The texture named, uploaded the first time it is asked for in the colour space of the slot asking (base colour sRGB,
    /// a normal or occlusion-roughness-metal map linear), no larger than <see cref="TextureLimit"/>; the same pixels
    /// (one <see cref="TextureImage.Rgba"/>) under another name are not uploaded again. A texture the registry does not have
    /// yet is asked for again next frame (it may be loading).
    /// </summary>
    public int Texture(string? name, bool srgb, float cutout = 0f)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        if (cutout > 0f && KeepCoverage) return CutTexture(name, srgb, cutout);
        var byName = srgb ? colourTextures : dataTextures;
        if (byName.TryGetValue(name, out int have)) return have;
        if (Textures?.Invoke(name) is not { Width: > 0, Height: > 0 } image || image.Rgba.Length < image.Width * image.Height * 4) return -1;
        var byPixels = srgb ? colourPixels : dataPixels;
        if (!byPixels.TryGetValue(image.Rgba, out int made))
        {
            var fitted = CharacterTextureSize.Fit(image, TextureLimit, srgb);
            made = Extra.AddTexture(fitted.Width, fitted.Height, fitted.Rgba, srgb);
            TextureBytes += CharacterTextureSize.GpuBytes(fitted.Width, fitted.Height);
            byPixels[image.Rgba] = made;
        }
        byName[name] = made;
        return made;
    }

    /// <summary>
    /// A person's cut-out base colour (hair, beard, lashes), uploaded once for its cut-off with mips that keep its
    /// coverage (<see cref="CharacterTextureSize.CoverageMips"/>), so it does not thin as it is seen smaller.
    /// </summary>
    int CutTexture(string name, bool srgb, float cutout)
    {
        if (cutTextures.TryGetValue((name, cutout), out int have)) return have;
        if (Textures?.Invoke(name) is not { Width: > 0, Height: > 0 } image || image.Rgba.Length < image.Width * image.Height * 4) return -1;
        if (!cutPixels.TryGetValue((image.Rgba, cutout), out int made))
        {
            var fitted = CharacterTextureSize.Fit(image, TextureLimit, srgb);
            made = Extra.AddTextureLevels(fitted.Width, fitted.Height, CharacterTextureSize.CoverageMips(fitted, cutout, srgb), srgb);
            TextureBytes += CharacterTextureSize.GpuBytes(fitted.Width, fitted.Height);
            cutPixels[(image.Rgba, cutout)] = made;
        }
        cutTextures[(name, cutout)] = made;
        return made;
    }

    public void Dispose()
    {
        batch.Dispose();
        Extra.Dispose();
    }
}
