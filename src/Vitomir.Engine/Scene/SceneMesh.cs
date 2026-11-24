// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.InteropServices;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Scene;

/// <summary>
/// A thing that is animating: the slice of the vertex list its model occupies, the transform that
/// slice was built with, and what it is playing. A renderer re-poses these each frame and uploads
/// just their slices; nothing else in the mesh ever moves once it is in.
/// </summary>
public sealed class AnimatedThing(int thing, uint firstVertex, Matrix4x4 world, ThingAnimation animation)
{
    public int Thing { get; } = thing;
    /// <summary>Where its vertices start in the mesh's vertex list.</summary>
    public uint FirstVertex { get; } = firstVertex;
    /// <summary>The transform its geometry was baked with (the scene's placement).</summary>
    public Matrix4x4 World { get; } = world;
    public ThingAnimation Animation { get; } = animation;
    /// <summary>Its vertices, ready to upload: the positions change, the colours and UVs do not.</summary>
    public SceneVertex[] Vertices { get; } = new SceneVertex[animation.Model.Vertices.Length];

    readonly Vector3[] posed = new Vector3[animation.Model.Vertices.Length];

    /// <summary>
    /// How far its last pose reached from the thing's own point, as the renderer measured it; without
    /// end until it has. **Ours**: what the renderer asks whether a view could show it by.
    /// </summary>
    public float Reach { get; set; } = float.PositiveInfinity;

    /// <summary>
    /// A model riding this character's skeleton — the weapon in its hand. Its own slice of the
    /// vertex list, posed from the same bones (<see cref="ThingAnimation.PoseCarried"/>).
    /// </summary>
    public sealed class Part(NodModel model, uint firstVertex, string name, int slot)
    {
        public NodModel Model { get; } = model;
        public string Name { get; } = name;
        /// <summary>Which of the model's four sub-model slots this sits in (<see cref="World.HeldModel"/>).</summary>
        public int Slot { get; } = slot;
        /// <summary>Where its vertices start in the mesh's vertex list.</summary>
        public uint FirstVertex { get; } = firstVertex;
        public SceneVertex[] Vertices { get; } = new SceneVertex[model.Vertices.Length];
        internal readonly Vector3[] Posed = new Vector3[model.Vertices.Length];

        /// <summary>
        /// Whether this is the model in the hand right now. The engine has one slot and replaces
        /// what is in it (the old model is freed first); this mesh is only ever appended to,
        /// so a weapon that has been put away keeps its vertices and stops being posed into them.
        /// Every one of its triangles then has three identical corners and rasterises nothing.
        /// </summary>
        public bool Shown { get; internal set; } = true;
    }

    /// <summary>
    /// What it is carrying, and everything it has carried: the one with <see cref="Part.Shown"/>
    /// is in its hand, and the rest are put away. Usually none or one.
    /// </summary>
    public List<Part> Carried { get; } = [];

    /// <summary>
    /// What each sub-model slot was last filled from — the weapon object, the off-hand item.
    /// Comparing against these is how a character that has changed nothing costs two reference
    /// comparisons a frame.
    /// </summary>
    public object?[] Filled { get; } = new object?[Vitomir.Engine.World.HeldModel.Slots];

    /// <summary>Poses the model at its current frame and writes the result into <see cref="Vertices"/>.</summary>
    public void Pose()
    {
        Animation.Pose(posed);
        for (int i = 0; i < posed.Length; i++) Vertices[i].Position = Vector3.Transform(posed[i], World);
        // After Pose, and never before it: the bones it leaves behind are what these ride on.
        foreach (var part in Carried)
        {
            if (!part.Shown)
            {
                var away = World.Translation;
                for (int i = 0; i < part.Vertices.Length; i++) part.Vertices[i].Position = away;
                continue;
            }
            Animation.PoseCarried(part.Model, part.Posed);
            for (int i = 0; i < part.Posed.Length; i++)
                part.Vertices[i].Position = Vector3.Transform(part.Posed[i], World);
        }
    }
}

/// <summary>
/// CPU-side geometry for a location: the NIL level plus the scene's things, in world space, in one
/// vertex/index list. Front faces are counter-clockwise seen from the normal's side (true of 99% of
/// level surfaces and model faces; the rest are degenerate).
///
/// **It grows.** The level goes in once and never changes, but a thing that enters the world after
/// the place has loaded — a dropped sword, a gib, a skeleton, a summoned wolf — appends its model
/// through <see cref="AddThing"/> and bumps <see cref="Revision"/> for a renderer to notice. Only
/// ever appended to: existing vertices keep their positions in the list, so every
/// <see cref="DrawBatch"/>, every <see cref="AnimatedThing.FirstVertex"/> and every
/// <see cref="DrawBatch.Thing"/> index stays pointing at what it pointed at.
/// </summary>
public sealed partial class SceneMesh
{
    readonly List<SceneVertex> vertices = [];
    readonly List<uint> indices = [];

    public List<DrawBatch> Batches { get; } = [];
    public SceneVertex[] Vertices => [.. vertices];

    readonly List<(int Thing, uint First, int Count)> slices = [];

    /// <summary>
    /// Every model's run of the vertex list and the thing it belongs to — a character's body and
    /// each thing it has ever held — in the order they went in.
    /// </summary>
    public IReadOnlyList<(int Thing, uint First, int Count)> Slices => slices;

    /// <summary>One entry of the index list, without copying the list (the shadows' silhouette, World.MeshSilhouette).</summary>
    public uint IndexAt(int i) => indices[i];

    /// <summary>Where one vertex of the list stands, without copying the list.</summary>
    public Vector3 PositionAt(uint v) => vertices[(int)v].Position;

    /// <summary>Copies a run of the vertex list out, without copying the rest of it.</summary>
    public void CopyVertices(uint first, SceneVertex[] into) =>
        vertices.CopyTo((int)first, into, 0, into.Length);
    public uint[] Indices => [.. indices];
    public Vector3 BoundsMin { get; private set; } = new(float.MaxValue);
    public Vector3 BoundsMax { get; private set; } = new(float.MinValue);
    public int ThingsDrawn { get; private set; }
    /// <summary>How many of them have a skeleton.</summary>
    public int Skinned { get; private set; }
    /// <summary>
    /// The ones with a mixer, in the order they were added — whether or not anything is playing on
    /// it (a set with no startable mode still gets one; see <see cref="ThingAnimation.For"/>).
    ///
    /// **Append-only, and it grows mid-frame.** A thing given geometry at run time is appended
    /// here on the spot, and that happens from inside the simulation's own animation walk: a
    /// strike tag rolls a blow, a blow kills, a vampire's death spawns a skeleton.
    /// **So anything that walks this list while a callback could run must take the count first and
    /// walk by index** — the simulation's index of it by thing does (`World/ThingTick.cs`), and
    /// enumerating it instead is what crashed the first Cappadocian kill after death-only sets got
    /// a mixer. Nothing is ever
    /// removed, so an index stays valid for the life of the location.
    /// </summary>
    public List<AnimatedThing> Animated { get; } = [];

    /// <summary>
    /// How many times an entry has been taken out of <see cref="Animated"/> (<see cref="Unanimate"/>).
    /// The list is otherwise append-only, and the simulation indexes it as it grows; a removal moves
    /// every later entry down one, so whoever keeps such an index starts it again when this changes.
    /// </summary>
    public int AnimatedRemovals { get; private set; }

    /// <summary>The animation of the thing drawn at <paramref name="thing"/> taken off the list — its geometry is being made again.</summary>
    public void Unanimate(int thing)
    {
        if (Animated.RemoveAll(a => a.Thing == thing) > 0) AnimatedRemovals++;
    }
    public List<string> Skipped { get; } = [];

    /// <summary>
    /// Bumped every time geometry is appended. A renderer holds GPU buffers built from
    /// <see cref="Vertices"/> and <see cref="Indices"/>, and this is how it learns that they are
    /// no longer the whole mesh.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>How many vertices and indices the mesh holds, without copying them out.</summary>
    public int VertexCount => vertices.Count;
    /// <inheritdoc cref="VertexCount"/>
    public int IndexCount => indices.Count;

    /// <summary>The models read so far, by name — the engine's own model cache.</summary>
    readonly Dictionary<string, NodModel?> models = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Level geometry. Sky and Nodraw groups are left out: the original never draws them (the sky model shows through Sky groups).</summary>
    public void AddLevel(NilLevel level, MaterialCache materials)
    {
        var matIndex = level.Materials.Select(m => materials.Get("materials/" + m)).ToArray();
        for (int si = 0; si < level.Sectors.Length; si++)
        {
            var s = level.Sectors[si];
            uint baseVertex = (uint)vertices.Count;
            foreach (var sv in s.SectorVertices)
            {
                var p = s.Vertices[sv.Vertex];
                Grow(p);
                vertices.Add(new SceneVertex { Position = p, Uv = sv.Uv, LightmapUv = sv.LightmapUv, Color = SceneVertex.FromBgra(sv.Rgba) });
            }
            for (int gi = 0; gi < s.Groups.Length; gi++)
            {
                var g = s.Groups[gi];
                if ((g.SurfaceFlags & (NilGroup.FlagSky | NilGroup.FlagNodraw)) != 0 || g.Material < 0) continue;
                uint first = (uint)indices.Count;
                var gmin = new Vector3(float.MaxValue); var gmax = new Vector3(float.MinValue);
                var runs = new SurfaceRun[g.NumSurfaces];   // each surface's run, for one drawn see-through (ours)
                for (int k = 0; k < g.NumSurfaces; k++)
                {
                    var sf = s.Surfaces[g.FirstSurface + k];
                    uint runFirst = (uint)indices.Count;
                    for (int i = 1; i + 1 < sf.NumVerts; i++)
                    {
                        indices.Add(baseVertex + (uint)sf.FirstVertex);
                        indices.Add(baseVertex + (uint)(sf.FirstVertex + i));
                        indices.Add(baseVertex + (uint)(sf.FirstVertex + i + 1));
                    }
                    for (int i = 0; i < sf.NumVerts; i++)
                    {
                        var p = s.Vertices[s.SectorVertices[sf.FirstVertex + i].Vertex];
                        gmin = Vector3.Min(gmin, p); gmax = Vector3.Max(gmax, p);
                    }
                    runs[k] = new SurfaceRun(g.FirstSurface + k, runFirst, (uint)indices.Count - runFirst);
                }
                int page = -1;
                foreach (var ly in g.Layers) if (ly.Style == 0) { page = (int)ly.Page; break; }
                if (page < 0 && g.Layers.Length > 0) page = (int)g.Layers[0].Page;
                float alpha = (g.SurfaceFlags & NilGroup.FlagTrans33) != 0 ? 0.33f : (g.SurfaceFlags & NilGroup.FlagTrans66) != 0 ? 0.66f : 1f;
                uint count = (uint)indices.Count - first;
                if (count > 0)
                    Batches.Add(new DrawBatch(matIndex[g.Material], page >= 0 ? Lighting.Lightmap : Lighting.Vertex, page,
                        (g.SurfaceFlags & NilGroup.FlagTwoSided) != 0, alpha, first, count, (gmin + gmax) * 0.5f, $"sector {si} group {gi}")
                    { Layers = g.Layers, Sector = si, Runs = runs });
            }
        }
    }

    /// <summary>
    /// The level and the light styles a thing's run-time lighting needs. Without them a thing with
    /// no baked colours falls back to the flat stand-in, which is what the tests that only care
    /// about geometry get.
    /// </summary>
    public NilLevel? Level { get; set; }
    /// <summary>
    /// Where a thing's light comes from, when it is not the level's own picker. The status bar's
    /// head viewport has no level and no sector — it carries **two fixed lights of
    /// its own** — so it sets this instead of going through <see cref="LightsFor"/>.
    /// </summary>
    public Func<WorldThing, (ChosenLight[] Lights, Vector3 Ambient)?>? LightSource { get; set; }

    /// <summary>
    /// A viewport's own light for each vertex (position and normal as built → the packed colour), or
    /// null. Set, it is used in place of <see cref="LightSource"/> for every model that is not prelit:
    /// the status bar's heads, lit the renderer's way by their own two lights (<see cref="Ui.HeadLighting"/>).
    /// </summary>
    public Func<Vector3, Vector3, uint>? VertexLight { get; set; }
    public LightStyles? Styles { get; set; }

    /// <summary>
    /// The scene's visible things: NOD models from 3d/models, or a thing's inline brush model.
    /// Pre-lit things use the scene's baked vertex colours; fully-lit ones are drawn at full light;
    /// the rest are lit from the level's lights (<see cref="ThingLight"/>).
    ///
    /// A thing whose template names a motion set and whose model has a skeleton is given that set's
    /// <c>stand</c> — what a character holds when it is doing nothing — and listed in
    /// <see cref="Animated"/>; its geometry goes in at the animation's first frame, and a renderer
    /// re-poses it from there. Without <paramref name="motions"/>, or without a playable
    /// <c>stand</c>, the model stays in its rest pose, which is what its vertices are authored in.
    /// </summary>
    public void AddThings(WorldThing[] things, GameFiles files, MaterialCache materials, MotionSets? motions = null)
    {
        for (int ti = 0; ti < things.Length; ti++) AddThing(things[ti], ti, files, materials, motions);
    }

    /// <summary>
    /// One thing's geometry, appended to the mesh. <paramref name="thingIndex"/> is the place it
    /// holds in the array its batches are resolved against (<see cref="World.Location.Drawn"/>).
    ///
    /// **This may be called after the first build, and that is the point of it.** The original has
    /// no combined mesh to invalidate: level geometry is baked per sector at load and
    /// every thing is submitted on its own, once per frame, by walking the live linked list at
    /// the sector's live list. A thing made at run time therefore draws on the very next frame,
    /// and the whole of its render-side registration is the one pointer store that links it into
    /// that list. One vertex buffer for the place is this tree's own bargain, and
    /// appending to it is what that bargain costs.
    ///
    /// The model is loaded on a miss and then kept, which is the engine's own model cache
    /// Hashtable lookup, `fopen` on a miss, a refcount bump on a hit) — so the first
    /// chunk of a gibbed body costs a file read and the other fifteen cost nothing.
    /// </summary>
    /// <returns>False for a thing that is never drawn, or whose model is missing.</returns>
    public bool AddThing(WorldThing worldThing, int thingIndex, GameFiles files, MaterialCache materials,
                         MotionSets? motions = null)
    {
        var thing = worldThing.Scene;
        // A region is a trigger volume, and the engine draws one only behind a debug bit
        // (raster options bit 0x800); a thing with no type at all is not a thing you can
        // see. **Nothing else is skipped by type**, because the draw loop has no type test in it:
        // a projectile and an explosion are ordinary template-built things with ordinary models
        // and go down the same path as a barrel.
        if (worldThing.Type is ThingType.Region or ThingType.None) return false;
        uint modelFlags = worldThing.ModelFlags;
        // The geometry is built at the thing's own base transform; a thing that later moves is
        // drawn by the difference between that and where it has got to.
        var world = worldThing.Base;
        string label = $"thing {thingIndex} {worldThing.Template}";
        // A thing with no baked colours is lit from the lights in its sector, as the engine
        // lights one (ThingLight). Chosen once at the thing's own position, which is where the
        // engine chooses them from too.
        var lit = LightSource is null ? LightsFor(worldThing) : LightSource(worldThing);
        if (thing.Model is { } brush)
        {
            var mats = brush.Materials.Select(m => materials.Get("materials/" + m)).ToArray();
            _ = AddModel(brush.Vertices, brush.Indices, brush.Groups, mats, world, thing, modelFlags, label, thingIndex, lit);
            Revision++;
            return true;
        }
        if (worldThing.Model.Length == 0) return false;
        string modelName = worldThing.Model;
        var model = Model(modelName, files);
        if (model is null) { Skipped.Add($"{label}: model {modelName} not found"); return false; }
        // A skinned model's vertices are authored in its rest pose, and the skin matrix at rest
        // is the identity, so drawing it untransformed is drawing it at
        // rest. That is what a thing with no animation playing shows.
        if (model.Bones.Length > 1) Skinned++;
        // Its own instance, whose named shapes can be hidden (World.NamedShapes).
        NamedShapes.Model(worldThing, model);
        var modelMats = model.Materials.Select(m => materials.Get("3d/materials/" + m)).ToArray();
        uint firstVertex = AddModel(model.Vertices, model.Indices, model.Groups, modelMats, world, thing, modelFlags, label, thingIndex, lit);
        Revision++;
        if (motions is null) return true;
        if (ThingAnimation.For(model, worldThing.MotionSet, motions) is not { } animation) return true;
        var animated = new AnimatedThing(thingIndex, firstVertex, world, animation);
        // Seed it from the vertices we have just built, so the colours and UVs are already right
        // and only the positions ever change; then pose it where its animation starts.
        vertices.CopyTo((int)firstVertex, animated.Vertices, 0, animated.Vertices.Length);
        // Anything the engine would already have played by now (WorldThing.Waiting).
        if (worldThing.Waiting is { } waiting)
        {
            animation.Play(waiting.Mode, motions, onMotion: false, rate: waiting.Rate);
            worldThing.Waiting = null;
        }
        animated.Pose();
        Settle(animated.FirstVertex, animated.Vertices);
        // And whatever it is holding, by the same path a change of weapon takes later.
        Rearm(animated, worldThing, files, materials);
        Animated.Add(animated);
        worldThing.Animation = animation;
        return true;
    }

    /// <summary>Writes a posed slice back into the mesh and grows the bounds around it.</summary>
    void Settle(uint firstVertex, SceneVertex[] slice)
    {
        for (int i = 0; i < slice.Length; i++)
        {
            vertices[(int)firstVertex + i] = slice[i];
            Grow(slice[i].Position);
        }
    }

    /// <summary>
    /// Gives a character the model it holds in its hand, if it holds one you can see.
    ///
    /// The engine's <c>SetWeapon</c> builds the name — see
    /// <see cref="World.HeldModel"/> — and loads it into sub-model slot 1 of the character's model,
    /// where it is skinned by the character's own bones. Here it is a second slice of
    /// the vertex list, posed from the same skeleton, drawn in its own batches and carried along by
    /// the same <c>thingIndex</c>, so it moves with the character exactly as the character's own
    /// geometry does.
    ///
    /// A weapon whose model is missing is skipped with a line in <see cref="Skipped"/> rather than
    /// failing the character: the engine does the same, leaving the slot empty (it returns 0
    /// on a model it cannot read) and drawing a character with nothing in its hand.
    /// </summary>
    /// <summary>Appends one carried model and returns it, or null if it cannot be carried.</summary>
    AnimatedThing.Part? Take(AnimatedThing animated, WorldThing worldThing, int slot, string held,
                             GameFiles files, MaterialCache materials)
    {
        string label = $"thing {animated.Thing} {worldThing.Template}";
        var model = Model(held, files);
        if (model is null) { Skipped.Add($"{label}: held model {held} not found"); return null; }
        if (!animated.Animation.CanCarry(model))
        {
            Skipped.Add($"{label}: held model {held} has {model.Bones.Length} bones, " +
                        $"too few for {animated.Animation.Model.Bones.Length}");
            return null;
        }
        var mats = model.Materials.Select(m => materials.Get("3d/materials/" + m)).ToArray();
        var lit = LightSource is null ? LightsFor(worldThing) : LightSource(worldThing);
        uint first = AddModel(model.Vertices, model.Indices, model.Groups, mats, animated.World,
                              worldThing.Scene, worldThing.ModelFlags, $"{label} holding {held}",
                              animated.Thing, lit, counts: false, slot: slot);
        var part = new AnimatedThing.Part(model, first, held, slot);
        animated.Carried.Add(part);
        Revision++;
        return part;
    }

    /// <summary>
    /// Puts the right models in a character's hands, and puts them there again whenever what it is
    /// holding changes — the AI swapping to a weapon it still has ammo for, the player equipping a
    /// sword, a hand going empty. This is the engine's `SetWeapon` / `SetOffhand` reached
    /// however many times they are reached: each clears its slot and fills it
    /// again, and so does this.
    ///
    /// Called once when the character's geometry goes in, and then every frame for every animating
    /// character, where it costs two reference comparisons for everyone who has changed nothing.
    /// </summary>
    /// <returns>Whether anything changed.</returns>
    public bool Rearm(AnimatedThing animated, WorldThing worldThing, GameFiles files, MaterialCache materials)
    {
        if (worldThing.Actor is not { } actor) return false;
        bool changed = false;
        // The slots' named shapes follow what is *held*, not what is drawn: `SetWeapon` makes a
        // fresh instance only when the weapon changes, and hiding its picture is a flag
        // on the instance it already has (World.NamedShapes).
        if (!NamedShapes.Holds(worldThing, World.HeldModel.WeaponSlot, actor.Weapon))
            NamedShapes.Fill(worldThing, World.HeldModel.WeaponSlot, actor.Weapon,
                             Carriable(animated, World.HeldModel.Weapon(actor.Weapon, actor.SkeletonId, worldThing.Model), files));
        if (!NamedShapes.Holds(worldThing, World.HeldModel.OffhandSlot, actor.Offhand))
            NamedShapes.Fill(worldThing, World.HeldModel.OffhandSlot, actor.Offhand,
                             Carriable(animated, World.HeldModel.Offhand(actor.Offhand, actor.SkeletonId), files));
        // A hidden weapon (`EnableActorWeapon(false)`) empties the slot's picture and nothing else.
        var shown = actor.WeaponHidden ? null : Rpg.ShapeShift.DrawnWeapon(actor, actor.Weapon);
        if (!ReferenceEquals(animated.Filled[World.HeldModel.WeaponSlot], shown))
        {
            animated.Filled[World.HeldModel.WeaponSlot] = shown;
            changed |= Swap(animated, worldThing, World.HeldModel.WeaponSlot,
                            World.HeldModel.Weapon(shown, actor.SkeletonId, worldThing.Model),
                            files, materials);
        }
        // And the off hand's, which only a conversation hides (`Rpg.Conversation.Weapons`).
        // A shape change's fresh model has nothing on its arm (Rpg.ShapeShift.DrawnOffhand).
        var offhand = actor.OffhandHidden ? null : Rpg.ShapeShift.DrawnOffhand(actor, actor.Offhand);
        if (!ReferenceEquals(animated.Filled[World.HeldModel.OffhandSlot], offhand))
        {
            animated.Filled[World.HeldModel.OffhandSlot] = offhand;
            changed |= Swap(animated, worldThing, World.HeldModel.OffhandSlot,
                            World.HeldModel.Offhand(offhand, actor.SkeletonId),
                            files, materials);
        }
        // The body model in slot 0 (World.BodyModels): armour, or the body with its head
        // off. While it holds one, the character's own geometry is not drawn (instance bit 8).
        var body = actor.BodyModel.Length > 0 ? actor.BodyModel : null;
        if (!ReferenceEquals(animated.Filled[BodySlot], body))
        {
            animated.Filled[BodySlot] = body;
            var bodyModel = body is null ? null : Carriable(animated, body, files);
            NamedShapes.Fill(worldThing, BodySlot, body, bodyModel);
            worldThing.Shapes!.BodyHidden = bodyModel is not null;
            changed |= Swap(animated, worldThing, BodySlot, bodyModel is null ? "" : body!, files, materials);
        }
        // And the talking hands in slot 3 (Rpg.TalkingHands), which hold their own name.
        var hands = actor.TalkingHands.Length > 0 ? actor.TalkingHands : null;
        if (!ReferenceEquals(animated.Filled[Rpg.TalkingHands.Slot], hands))
        {
            animated.Filled[Rpg.TalkingHands.Slot] = hands;
            changed |= Swap(animated, worldThing, Rpg.TalkingHands.Slot, hands ?? "", files, materials);
        }
        return changed;
    }

    /// <summary>The sub-model slot a body model goes in (slot 0).</summary>
    const int BodySlot = 0;

    /// <summary>
    /// The model a slot would hold under that name, or null when there is none or it cannot ride
    /// this skeleton — which is when the engine leaves the slot empty.
    /// </summary>
    NodModel? Carriable(AnimatedThing animated, string held, GameFiles files) =>
        held.Length > 0 && Model(held, files) is { } model && animated.Animation.CanCarry(model) ? model : null;

    /// <summary>One slot's worth of that: take out what is there and put <paramref name="held"/> in.</summary>
    bool Swap(AnimatedThing animated, WorldThing worldThing, int slot, string held,
              GameFiles files, MaterialCache materials)
    {
        var shown = animated.Carried.FirstOrDefault(p => p.Slot == slot && p.Shown);
        // A different weapon can still be the same model; file names are matched as the file system
        // matches them.
        if (shown is not null)
        {
            if (shown.Name.Equals(held, StringComparison.OrdinalIgnoreCase)) return false;
            shown.Shown = false;
        }
        if (held.Length == 0) return shown is not null;
        // Something this slot has held before is still in the vertex list; take it out again rather
        // than appending a second copy, which is what the engine's model cache amounts to here.
        if (animated.Carried.FirstOrDefault(p => p.Slot == slot
                                                 && p.Name.Equals(held, StringComparison.OrdinalIgnoreCase))
            is { } again) { again.Shown = true; return true; }
        var part = Take(animated, worldThing, slot, held, files, materials);
        if (part is null) return shown is not null;
        vertices.CopyTo((int)part.FirstVertex, part.Vertices, 0, part.Vertices.Length);
        animated.Pose();
        Settle(part.FirstVertex, part.Vertices);
        return true;
    }

    /// <summary>
    /// A model by name, read once and then kept — the engine's own model cache
    /// (hashtable lookup, <c>fopen</c> on a miss, a refcount bump on a hit). Null for one that is
    /// not there; the miss is cached too, so a missing file is looked for once.
    /// </summary>
    public NodModel? Model(string name, GameFiles files)
    {
        if (models.TryGetValue(name, out var cached)) return cached;
        string file = name.Contains('.') ? name : name + ".nod";
        return models[name] = files.TryRead("3d/models/" + file, out var data) ? NodModel.Read(data, file) : null;
    }

    /// <summary>
    /// The lights that reach a thing, when the caller gave us a level to read them from. Null means
    /// "no run-time lighting available", and an unlit thing keeps the flat stand-in.
    /// </summary>
    (ChosenLight[] Lights, Vector3 Ambient)? LightsFor(WorldThing thing)
    {
        if (Level is null || Styles is null) return null;
        var sector = thing.Sector >= 0 && thing.Sector < Level.Sectors.Length ? Level.Sectors[thing.Sector] : null;
        // The stand-in's rule, not the sector draw's (which takes the brighter of the two, World.ModelLight):
        // all but 88 sectors, in five levels, have no ambient of their own, so this is mostly the level's.
        var ambient = ThingLight.Ambient(sector is { } s && s.Ambient != Vector3.Zero ? s.Ambient : Level.Ambient);
        var lights = sector is null ? [] : ThingLight.Choose(sector, thing.Position, 0f, Styles);

        // The sun or moon sits in the level header rather than in any sector, and reaches
        // everything: outdoors by day it is most of the light there is.
        var global = Level.GlobalLight;
        if (global is { Intensity2: > 0 })
        {
            float strength = ThingLight.Strength(global, thing.Position, 0f, Styles.Value((int)global.Style));
            if (strength > 0) lights = [.. lights, new ChosenLight(global, strength)];
        }

        // The stand-in invents a light rather than leave a thing black. The thing draw makes this light up
        // too, but for the shadow alone; the model path does not use it.
        if (lights.Length == 0)
        {
            var fallback = ThingLight.Fallback(thing.Position);
            lights = [new ChosenLight(fallback, ThingLight.Strength(fallback, thing.Position, 0f, 1f))];
        }
        return (lights, ambient);
    }

    /// <summary>Adds a model's geometry and returns where its vertices start in the vertex list.</summary>
    uint AddModel(NodVertex[] verts, ushort[] faceIndices, NodMeshGroup[] groups, int[] mats, Matrix4x4 world,
                  NsdThing thing, uint modelFlags, string label, int thingIndex,
                  (ChosenLight[] Lights, Vector3 Ambient)? lit = null, bool counts = true,
                  int slot = NamedShapes.Body)
    {
        const uint PreLit = 1, FullyLit = 2;
        bool prelit = (modelFlags & PreLit) != 0 && thing.VertexColors.Length == verts.Length;
        bool full = (modelFlags & FullyLit) != 0;
        uint baseVertex = (uint)vertices.Count;
        var rotation = world with { M41 = 0, M42 = 0, M43 = 0 };
        for (int i = 0; i < verts.Length; i++)
        {
            var p = Vector3.Transform(verts[i].Position, world);
            Grow(p);
            uint color;
            if (prelit) color = SceneVertex.FromBgra(thing.VertexColors[i]);
            else if (VertexLight is { } own)
            {
                var n = Vector3.TransformNormal(verts[i].Normal, rotation);
                if (n != Vector3.Zero) n = Vector3.Normalize(n);
                color = own(p, n);
            }
            else if (lit is { } l)
            {
                var n = Vector3.TransformNormal(verts[i].Normal, rotation);
                if (n != Vector3.Zero) n = Vector3.Normalize(n);
                color = ThingLight.Pack(ThingLight.Shade(l.Lights, p, n, l.Ambient));
            }
            else color = 0xff808080u;                             // no level to light from
            vertices.Add(new SceneVertex { Position = p, Uv = verts[i].Uv, Color = color });
        }
        Remember(verts, world, thingIndex, baseVertex, prelit ? thing.VertexColors : null, modelFlags, slot);   // SceneMesh.Lighting.cs
        foreach (var g in groups)
        {
            if (g.Material < 0 || g.Material >= mats.Length || g.NumFaces == 0) continue;
            uint first = (uint)indices.Count;
            var gmin = new Vector3(float.MaxValue); var gmax = new Vector3(float.MinValue);
            for (int f = 0; f < g.NumFaces * 3; f++)
            {
                uint v = baseVertex + (uint)(g.FirstVertex + faceIndices[g.FirstFace * 3 + f]);
                indices.Add(v);
                gmin = Vector3.Min(gmin, vertices[(int)v].Position); gmax = Vector3.Max(gmax, vertices[(int)v].Position);
            }
            Batches.Add(new DrawBatch(mats[g.Material], full ? Lighting.Full : Lighting.Vertex, -1, false, 1f,
                                      first, (uint)indices.Count - first, (gmin + gmax) * 0.5f, label,
                                      BatchLayer.Thing, thingIndex) { Slot = slot, Mesh = g.Mesh });
        }
        slices.Add((thingIndex, baseVertex, verts.Length));
        if (counts) ThingsDrawn++;
        return baseVertex;
    }

    void Grow(Vector3 p)
    {
        BoundsMin = Vector3.Min(BoundsMin, p);
        BoundsMax = Vector3.Max(BoundsMax, p);
    }
}
