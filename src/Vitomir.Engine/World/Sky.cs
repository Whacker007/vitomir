// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Scene;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// A scene's sky: the SKYINFO model (3d/models) posed by the SKYINFO animation (3d/anims), which the
/// scene starts at 30 frames a second, looping. The original draws it centred on the camera with no
/// rotation, fully lit (vertex colours white), without fog or depth writes, and only through the
/// screen area of the level's Sky surfaces (see <see cref="SkyWindow"/>). The animation advances
/// only on frames where the sky is drawn.
/// </summary>
public sealed class Sky
{
    public string Name { get; }
    public NodModel Model { get; }
    public AnimationPlayer? Player { get; }
    /// <summary><see cref="MaterialCache"/> index for each of the model's materials.</summary>
    public int[] Materials { get; }

    readonly Vector3[] rotation, translation;
    readonly Matrix4x4[] world, skin;

    Sky(string name, NodModel model, NadAnimation? animation, int[] materials)
    {
        Name = name;
        Model = model;
        Player = animation is null ? null : new AnimationPlayer(animation, rate: 30f);
        Materials = materials;
        int n = model.Bones.Length;
        rotation = new Vector3[n]; translation = new Vector3[n];
        world = new Matrix4x4[n]; skin = new Matrix4x4[n];
    }

    /// <summary>The scene's sky, or null when the scene has none (or its model is missing).</summary>
    public static Sky? Load(NsdScene scene, GameFiles files, MaterialCache materials)
    {
        if (scene.SkyModel.Length == 0) return null;
        string modelFile = WithExtension(scene.SkyModel, ".nod");
        if (!files.TryRead("3d/models/" + modelFile, out var nodData)) return null;
        var model = NodModel.Read(nodData, modelFile);
        NadAnimation? animation = null;
        if (scene.SkyAnimation.Length > 0)
        {
            string animFile = WithExtension(scene.SkyAnimation, ".nad");
            if (files.TryRead("3d/anims/" + animFile, out var nadData)) animation = NadAnimation.Read(nadData, animFile);
        }
        var mats = model.Materials.Select(m => materials.Get("3d/materials/" + m)).ToArray();
        return new Sky(scene.SkyModel, model, animation, mats);
    }

    static string WithExtension(string name, string ext) => Path.HasExtension(name) ? name : name + ext;

    /// <summary>
    /// The model's vertex positions at the current frame, relative to the camera (the sky's origin
    /// sits on the eye). Without an animation the model keeps its rest pose.
    /// </summary>
    public void Pose(Span<Vector3> positions)
    {
        var verts = Model.Vertices;
        if (Player is null || Model.Bones.Length == 0)
        {
            for (int i = 0; i < verts.Length; i++) positions[i] = verts[i].Position;
            return;
        }
        Skeleton.Sample(Player.Animation, Player.Frame, rotation, translation);
        Skeleton.Pose(Model, rotation, translation, world);
        Skeleton.Skin(Model, world, skin);
        for (int i = 0; i < verts.Length; i++) positions[i] = Skeleton.SkinPosition(Model, verts[i], skin);
    }
}
