// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The skinning rules are taken from src/Vitomir.Engine/Anim/Skeleton.cs, the material resolution from
// src/Vitomir.Engine/Scene/Materials.cs (Load, Resolve, Decode and MaterialCache.Get) at b4185ef,
// reworked for the platform.
using System.Numerics;
using Vitomir.Formats;
using Vitomir.Formats.L2;

namespace Vitomir.Platform.Assets.OneOne;

/// <summary>What a 1.1 model's material comes to on the platform: the texture it draws with and how.</summary>
/// <param name="Material">The material's name as the model gives it.</param>
/// <param name="Texture">The path of the texture found for it (1.1's path, for example <c>3d/materials/Christof_Cloth.tga</c>), or empty when there is none.</param>
/// <param name="AlphaTest">Cut out by the texture's alpha: the material is <c>masked</c>, or its texture carries alpha (which 1.1 blends; a cut-out is the nearest an opaque pass has).</param>
/// <param name="TwoSided">The material says <c>twosided</c> (capes and hems, the <c>_dbl</c> materials).</param>
public readonly record struct OneOneMaterial(string Material, string Texture, bool AlphaTest, bool TwoSided);

/// <summary>
/// 1.1's models (<c>.nod</c>) turned into the platform's <see cref="SkinnedModel"/>,
/// and their materials resolved to textures, exactly as the old engine (and 1.1) resolve them.
/// <para>
/// <b>Units.</b> 1.1 measures 72 units to the metre (<see cref="L2Manifest.EngineUnitsPerMetre"/>); every
/// position and translation is divided by it here, once, at load. Rotations are unchanged.
/// </para>
/// <para>
/// <b>The skeleton.</b> A bone's inverse bind matrix is the model's own <c>RestMatrixInverse</c> (its 3×3
/// rows, then its translation in metres): 1.1's skin matrix is <c>RestMatrixInverse × world</c>.
/// The rest pose the vertices are authored in — the bind pose — is that matrix's inverse, and a bone's
/// <see cref="BoneInfo.Rest"/> is the bind pose relative to its parent: the rotation the model was bound
/// with (which lives only in <c>RestMatrixInverse</c>) and the bone's <c>RestTranslate</c> (equal to
/// the bind pose's own offset within 3e-5 units in every model checked). So posing the rest pose gives
/// every skin matrix as the identity and the raw vertices back. Parents precede children in every
/// shipped model; a parent index that does not precede its bone is a root, as 1.1 treats it.
/// </para>
/// <para>
/// <b>The vertices.</b> A 1.1 vertex names one bone and a weight; its bone's parent takes the rest of the
/// weight. Weight 1 (or more) binds it to its bone alone; below 1 it is <c>w × (v × skin[bone]) +
/// (1 − w) × (v × skin[parent])</c>, and a vertex on a root bone follows its bone alone (the old engine's
/// <c>SkinPosition</c>). So each <see cref="SkinVertex"/> has bones (bone, parent) and weights (w, 1 − w).
/// <b>1.1 lights a vertex by its own bone alone</b> (the old engine's
/// <c>PoseNormals</c>): a renderer matching it turns the normal by the first bone's skin matrix only.
/// </para>
/// <para>
/// <b>Texture coordinates.</b> 1.1 keeps a texture's rows bottom first, so its <c>v = 0</c> is the image's
/// bottom edge; a <see cref="TextureImage"/> is top first, as the GPU and glTF take it, so <c>v</c> is
/// turned to <c>1 − v</c> here and the two agree.
/// </para>
/// <para>
/// <b>Parts.</b> One part per material that has triangles, in the model's material order; within it the
/// mesh groups in their order. Level-of-detail collapse tables are not used (the full model is drawn).
/// </para>
/// </summary>
public static class OneOneModels
{
    /// <summary>1.1's units to the metre.</summary>
    public const float UnitsPerMetre = L2Manifest.EngineUnitsPerMetre;

    /// <summary>The way a 1.1 character faces in its model's space, as a yaw: +Y (a walk carries the planted foot back along −Y; checked on the catalogue's five).</summary>
    public const float FacingYaw = Core.DetMath.HalfPi;

    /// <summary>The material 1.1 falls back on when neither a <c>.nam</c> nor a texture is found (<c>dflt</c>).</summary>
    public const string DefaultMaterial = "dflt";

    /// <summary>The folder a model's materials live in.</summary>
    public const string ModelMaterialFolder = "3d/materials/";

    /// <summary>Turns a read <c>.nod</c> into a skinned model, its parts drawn with <paramref name="materials"/> (one per <see cref="NodModel.Materials"/> entry).</summary>
    public static SkinnedModel ToSkinnedModel(NodModel nod, string name, IReadOnlyList<OneOneMaterial> materials)
    {
        var skeleton = SkeletonOf(nod);
        var bones = skeleton.Bones;

        var vertices = new SkinVertex[nod.Vertices.Length];
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int i = 0; i < vertices.Length; i++)
        {
            var v = nod.Vertices[i];
            int bone = v.Bone < bones.Length ? v.Bone : 0;
            int parent = bones[bone].Parent;
            bool alone = v.Weight >= 1f || parent < 0;
            var p = v.Position / UnitsPerMetre;
            vertices[i] = new SkinVertex
            {
                Position = p,
                Normal = v.Normal,
                Uv = new Vector2(v.Uv.X, 1f - v.Uv.Y),
                Bones = SkinVertex.PackBones(bone, alone ? 0 : parent),
                Weights = alone ? new Vector4(1, 0, 0, 0) : new Vector4(v.Weight, 1f - v.Weight, 0, 0),
            };
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        if (vertices.Length == 0) min = max = Vector3.Zero;

        var indices = new List<uint>(nod.Indices.Length);
        var parts = new List<ModelPart>();
        for (int m = 0; m < nod.Materials.Length; m++)
        {
            int first = indices.Count;
            foreach (var g in nod.Groups)
            {
                if (g.Material != m) continue;
                for (int k = g.FirstFace * 3, end = (g.FirstFace + g.NumFaces) * 3; k < end; k++)
                    indices.Add((uint)(g.FirstVertex + nod.Indices[k]));
            }
            if (indices.Count == first) continue;
            var mat = m < materials.Count ? materials[m] : new OneOneMaterial(nod.Materials[m], "", false, false);
            parts.Add(new ModelPart(first, indices.Count - first, mat.Texture, mat.AlphaTest) { TwoSided = mat.TwoSided });
        }

        return new SkinnedModel
        {
            Name = name, Vertices = vertices, Indices = [.. indices], Parts = [.. parts], Skeleton = skeleton,
            BoundsMin = min, BoundsMax = max,
        };
    }

    /// <summary>A 1.1 skeleton in metres: parents, inverse bind matrices, and the bind pose relative to each parent.</summary>
    public static Skeleton SkeletonOf(NodModel nod)
    {
        int n = Math.Max(1, nod.Bones.Length);
        var infos = new BoneInfo[n];
        var inverse = new Matrix4x4[n];
        var bind = new Matrix4x4[n];
        if (nod.Bones.Length == 0)
        {
            infos[0] = new BoneInfo("bone0", -1, BoneTransform.Identity);
            inverse[0] = Matrix4x4.Identity;
            return new Skeleton { Bones = infos, InverseBind = inverse };
        }
        for (int b = 0; b < n; b++)
        {
            var bone = nod.Bones[b];
            var m = bone.RestMatrixInverse;
            inverse[b] = new Matrix4x4(m[0], m[1], m[2], 0, m[3], m[4], m[5], 0, m[6], m[7], m[8], 0,
                                       m[9] / UnitsPerMetre, m[10] / UnitsPerMetre, m[11] / UnitsPerMetre, 1);
            if (!Matrix4x4.Invert(inverse[b], out bind[b])) bind[b] = Matrix4x4.Identity;
            int parent = bone.Parent >= 0 && bone.Parent < b ? bone.Parent : -1;
            var local = bind[b];
            if (parent >= 0 && Matrix4x4.Invert(bind[parent], out var parentInverse)) local = bind[b] * parentInverse;
            var rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(local));
            infos[b] = new BoneInfo($"bone{b}", parent, new BoneTransform(rotation, bone.RestTranslate / UnitsPerMetre));
        }
        return new Skeleton { Bones = infos, InverseBind = inverse };
    }

    /// <summary>
    /// A model material resolved as 1.1 resolves it (the old engine's <c>Materials.Load</c> and
    /// <c>MaterialCache.Get</c>): <c>3d/materials/&lt;name&gt;.nam</c> if it is there, else a material implied by
    /// <c>&lt;name&gt;.tga</c>; its first texture frame that loads (later frames of an animated material are
    /// not used on characters); and, when neither a <c>.nam</c> nor any texture is found, the default
    /// material <c>dflt</c>, found along the material search path. A texture is found along 1.1's search
    /// path (<see cref="GameInstall.MaterialSearchPath"/>) — never relative to the material that names it —
    /// then, as two fallbacks of the old engine's own that no shipped model needs, in the material's folder
    /// and by file name in any folder.
    /// </summary>
    public static OneOneMaterial ResolveMaterial(GameInstall install, string name, Func<string, TgaImage?>? decoded = null)
    {
        decoded ??= path => install.TryRead(path, out var d) ? TgaImage.Read(d, path) : null;
        var found = Load(install, ModelMaterialFolder + name, name, decoded);
        if (found is null && !name.Equals(DefaultMaterial, StringComparison.OrdinalIgnoreCase)
            && ResolveDefault(install) is { } dflt)
            found = Load(install, dflt, name, decoded);
        return found ?? new OneOneMaterial(name, "", false, false);
    }

    /// <summary>A material at <paramref name="basePath"/> (no extension), or null when it is missing (no <c>.nam</c> and no texture).</summary>
    static OneOneMaterial? Load(GameInstall install, string basePath, string name, Func<string, TgaImage?> decoded)
    {
        string path = basePath.Replace('\\', '/');
        string folder = path[..(path.LastIndexOf('/') + 1)];
        bool hasNam = install.TryRead(path + ".nam", out var namData);
        var nam = hasNam ? NamMaterial.Read(namData, path + ".nam") : NamMaterial.Implied(Path.GetFileName(path) + ".tga");

        string texture = "";
        TgaImage? image = null;
        foreach (var frame in nam.Textures.Take(Math.Max(1, nam.NumFrames)))
            if (Find(install, folder, frame, decoded) is { } f) { (texture, image) = f; break; }
        if (image is null && nam.Textures.Count == 0 && nam.Passes.Count == 0
            && Find(install, folder, Path.GetFileName(path) + ".tga", decoded) is { } own)
            (texture, image) = own;
        if (!hasNam && image is null) return null;
        bool alpha = nam.Masked || (!nam.AddBlend && image is { HasAlpha: true });
        return new OneOneMaterial(name, texture, alpha, nam.TwoSided);
    }

    static (string Path, TgaImage Image)? Find(GameInstall install, string folder, string texture, Func<string, TgaImage?> decoded)
    {
        if (!texture.Contains('.')) texture += ".tga";
        string? path = install.FindMaterialFile(texture)
                    ?? (install.Exists(folder + texture) ? folder + texture : install.FindByFileName(texture));
        return path is not null && decoded(path) is { } image ? (path, image) : null;
    }

    /// <summary>Where <c>dflt</c> is: its <c>.nam</c> or its <c>.tga</c> along the search path (the old engine's <c>Materials.Resolve</c>), without the extension.</summary>
    static string? ResolveDefault(GameInstall install)
    {
        foreach (var file in new[] { DefaultMaterial + ".nam", DefaultMaterial + ".tga" })
        {
            foreach (var dir in GameInstall.MaterialSearchPath)
                if (install.Exists(dir + "/" + file)) return dir + "/" + DefaultMaterial;
            if (install.Exists(file)) return DefaultMaterial;
        }
        return null;
    }

    /// <summary>A decoded 1.1 texture (TGA) as the platform's image: RGBA, rows top first (which the TGA reader already gives).</summary>
    public static TextureImage ToTextureImage(string name, TgaImage image) => new(name, image.Width, image.Height, image.Rgba);
}
