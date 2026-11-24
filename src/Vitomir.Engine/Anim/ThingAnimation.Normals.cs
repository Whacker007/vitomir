// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.Anim;

public sealed partial class ThingAnimation
{
    /// <summary>Whether the last <see cref="Pose"/> skinned the model (something was playing), so its skin matrices are this frame's.</summary>
    bool skinned;

    /// <summary>
    /// The normals of <paramref name="model"/> — this skeleton's own model or one it carries — as the
    /// last <see cref="Pose"/> left the bones, each turned by its **own** bone alone: the renderer
    /// lights a vertex in the space of the bone it names (the renderer turns each light
    /// into every bone's space and reads the bone byte of each vertex), so a vertex blended
    /// towards its bone's parent is still lit as its own bone sees it. At rest, or for a model this
    /// skeleton has not posed, the model's own normals.
    /// </summary>
    public void PoseNormals(NodModel model, Span<Vector3> normals)
    {
        var verts = model.Vertices;
        Matrix4x4[]? bones = ReferenceEquals(model, Model) ? (skinned ? skin : null)
                           : skinned && carriedSkin.TryGetValue(model, out var carried) ? carried : null;
        for (int i = 0; i < verts.Length && i < normals.Length; i++)
        {
            var v = verts[i];
            normals[i] = bones is not null && v.Bone < bones.Length ? Vector3.TransformNormal(v.Normal, bones[v.Bone]) : v.Normal;
        }
    }
}
