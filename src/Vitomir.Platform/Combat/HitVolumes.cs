// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Combat;

/// <summary>One of a character's hit volumes where its pose has it now (in the world): the capsule, its region and its bone.</summary>
public readonly record struct HitVolume(OrientedCapsule Capsule, BodyRegion Region, int Bone);

/// <summary>What a ray met of a character's volumes: which volume (an index into <see cref="HitVolumes.Posed(World, EntityId)"/>'s), its region and bone, and the contact.</summary>
public readonly record struct VolumeHit(int Index, BodyRegion Region, int Bone, ContactHit Hit);

/// <summary>
/// The characters' hit volumes on their drawn poses (a service, ours: "the blade you
/// see swing is the blade that hits"). Each model's volumes are fitted once (<see cref="HitVolumeFit"/>, the first time
/// they are asked for, or all at once by <see cref="Prepare"/>); a character's volumes in the world are its fitted ones
/// carried by its posed bones (<see cref="CharacterPoses.Bones"/>) and its placement
/// (<see cref="CharacterKind.Placement"/>) — <b>worked out on demand</b>, for whoever needs them, into a buffer kept
/// for the character's slot, so asking allocates nothing after the first time. They are asked for after the cheap test
/// of <see cref="Broad"/> says they could matter.
/// <para>
/// <b>When.</b> The pose is <see cref="CharacterPoses"/>' for the character's <see cref="CharacterAnimation"/> as it
/// stands when asked, and the placement the body's feet and yaw then. The animation moves at the end of
/// <see cref="Phase.Move"/>, so from <see cref="Phase.Contact"/> and the reticle (Post 110) the pose and the
/// place are both this tick's.
/// </para>
/// <para>
/// <b>Last tick's.</b> A sweep of a moving body needs its volumes at the last tick as well as this one:
/// <see cref="Keep"/> keeps a character's volumes and broad capsule as they are now, and <see cref="Last"/> gives
/// them back on the next tick (contact keeps those of everyone near a swinging blade, each tick it swings).
/// </para>
/// Not digested: derived from digested state, and nothing it gives is written back into it.
/// </summary>
public sealed class HitVolumes
{
    FittedBody?[] byModel = [];
    bool[] fitted = [];
    Slot?[] slots = [];

    sealed class Slot
    {
        public HitVolume[] Volumes = [];
        // The volumes and broad capsule kept on LastTick, for the next tick's sweep.
        public HitVolume[] Last = [];
        public int LastCount;
        public long LastTick = long.MinValue;
        public EntityId LastOf = EntityId.None;
        public OrientedCapsule LastBroad;
    }

    /// <summary>Fits every model <paramref name="assets"/> holds now (at load, so no tick fits one).</summary>
    public void Prepare(CharacterAssets assets)
    {
        for (int m = 0; m < assets.ModelCount; m++) Model(assets, m);
    }

    /// <summary>The model's fitted volumes (fitted now if they were not), or null when its skeleton has no anatomy table.</summary>
    public FittedBody? Model(CharacterAssets assets, int model)
    {
        if (model >= fitted.Length)
        {
            int size = Math.Max(model + 1, fitted.Length * 2);
            Array.Resize(ref fitted, size);
            Array.Resize(ref byModel, size);
        }
        if (!fitted[model])
        {
            byModel[model] = HitVolumeFit.Fit(assets.Model(model));
            fitted[model] = true;
        }
        return byModel[model];
    }

    /// <summary>The entity's fitted volumes, or null when it is no character or its skeleton has no table.</summary>
    public FittedBody? Fitted(World world, EntityId entity)
    {
        ref var anim = ref world.Store<CharacterAnimation>().Find(entity);
        if (Unsafe.IsNullRef(ref anim)) return null;
        var assets = world.Service<CharacterAssets>();
        return Model(assets, assets.Kind(anim.Kind).Model);
    }

    /// <summary>
    /// The broad test: the body's own capsule grown by the fitted body's <see cref="FittedBody.Reach"/> (scaled with
    /// the model), which holds every volume in any pose that keeps the hips inside the body's capsule and turns the
    /// bones without stretching them. False when the entity has no body, no locomotion or no volumes.
    /// </summary>
    public bool Broad(World world, EntityId entity, out OrientedCapsule broad)
    {
        broad = default;
        if (Fitted(world, entity) is not { } body) return false;
        ref var b = ref world.Store<Body>().Find(entity);
        ref var l = ref world.Store<Locomotion>().Find(entity);
        if (Unsafe.IsNullRef(ref b) || Unsafe.IsNullRef(ref l)) return false;
        var kind = world.Service<CharacterAssets>().Kind(world.Get<CharacterAnimation>(entity).Kind);
        var cap = Capsule.OfBody(b.Feet, l.Radius, b.CapsuleHeight(l));
        float grow = body.Reach * kind.Scale;
        // A dying or dead body's hips leave its capsule (the death lays it down away from its feet): grown by its height.
        if (ActionRoles.IsDeath(world.Get<CharacterAnimation>(entity).Action)) grow += b.CapsuleHeight(l);
        broad = new OrientedCapsule(cap.Lower, cap.Upper, cap.Radius + grow);
        return true;
    }

    /// <summary>
    /// Keeps the entity's volumes and broad capsule as they are now, so that the next tick's sweep of a blade
    /// against it starts from them (<see cref="Last"/>): a moving body is swept, not sampled. Allocates only the first
    /// time an entity's buffer is made.
    /// </summary>
    public void Keep(World world, EntityId entity)
    {
        if (!Broad(world, entity, out var broad)) return;
        var now = Posed(world, entity);
        var slot = slots[entity.Index]!;
        if (slot.Last.Length < now.Length) slot.Last = new HitVolume[now.Length];
        now.CopyTo(slot.Last);
        slot.LastCount = now.Length;
        slot.LastTick = world.Tick;
        slot.LastOf = entity;
        slot.LastBroad = broad;
    }

    /// <summary>
    /// The entity's volumes and broad capsule as <see cref="Keep"/> kept them on the tick before this one, or
    /// false when they were not kept then (the entity was not near a swinging blade; the sweep then starts from now).
    /// </summary>
    public bool Last(World world, EntityId entity, out ReadOnlySpan<HitVolume> last, out OrientedCapsule broad)
    {
        last = default;
        broad = default;
        if ((uint)entity.Index >= (uint)slots.Length || slots[entity.Index] is not { } slot) return false;
        if (slot.LastOf != entity || slot.LastTick != world.Tick - 1) return false;
        last = slot.Last.AsSpan(0, slot.LastCount);
        broad = slot.LastBroad;
        return true;
    }

    /// <summary>The entity's volumes where its pose and its body have them now (empty when it has none). The span is the entity's own buffer, good until the next ask for it.</summary>
    public ReadOnlySpan<HitVolume> Posed(World world, EntityId entity)
    {
        ref var b = ref world.Store<Body>().Find(entity);
        ref var anim = ref world.Store<CharacterAnimation>().Find(entity);
        if (Unsafe.IsNullRef(ref b) || Unsafe.IsNullRef(ref anim)) return [];
        var kind = world.Service<CharacterAssets>().Kind(anim.Kind);
        return Posed(world, entity, kind.Placement(b.Feet, b.Yaw));
    }

    /// <summary>The entity's volumes on its pose now, stood in the world by <paramref name="place"/> (the picture's interpolated placement, for the overlay).</summary>
    public ReadOnlySpan<HitVolume> Posed(World world, EntityId entity, Matrix4x4 place)
    {
        if (Fitted(world, entity) is not { } body) return [];
        if (entity.Index >= slots.Length) Array.Resize(ref slots, Math.Max(entity.Index + 1, slots.Length * 2));
        var slot = slots[entity.Index] ??= new Slot();
        if (slot.Volumes.Length < body.Volumes.Length) slot.Volumes = new HitVolume[body.Volumes.Length];
        var kind = world.Service<CharacterAssets>().Kind(world.Get<CharacterAnimation>(entity).Kind);
        var bones = world.Service<CharacterPoses>().Bones(world, entity);
        Pose(body, bones, place, kind.Scale, slot.Volumes);
        return slot.Volumes.AsSpan(0, body.Volumes.Length);
    }

    /// <summary>
    /// A fitted body's volumes on a pose (its bones in model space, <see cref="CharacterPoses.Bones"/>) stood in the
    /// world by <paramref name="place"/> (which carries the model's <paramref name="scale"/>; the radii are scaled by
    /// it), into <paramref name="into"/>: a bone's volume is its ends through the posed bone and the placement. Pure
    /// scalar arithmetic (<see cref="PoseMath"/>).
    /// </summary>
    public static void Pose(FittedBody body, ReadOnlySpan<Matrix4x4> bones, in Matrix4x4 place, float scale, Span<HitVolume> into)
    {
        var volumes = body.Volumes;
        for (int i = 0; i < volumes.Length; i++)
        {
            ref readonly var v = ref volumes[i];
            var m = PoseMath.Multiply(bones[v.Bone], place);
            into[i] = new HitVolume(new OrientedCapsule(PoseMath.Transform(v.A, m), PoseMath.Transform(v.B, m), v.Radius * scale), v.Region, v.Bone);
        }
    }

    /// <summary>
    /// What the entity lacks (<see cref="MissingParts.Bits"/>, 0 whole) and its model's collapse
    /// (<see cref="PartCollapse"/>): a volume for which <see cref="PartCollapse.VolumeGone"/> answers true is <b>skipped</b> —
    /// its part was cut off (or a part above it), so contact and the ray meet it no more and a blade's path through where it
    /// was is clear (the rules package's attack budget). The posed volumes themselves are all still given (the overlay draws them).
    /// </summary>
    public uint Missing(World world, EntityId entity, out PartCollapse? collapse)
    {
        collapse = null;
        if (!world.TryService<CharacterPoses>(out var poses)) return 0;
        uint missing = poses.Missing(world, entity);
        if (missing == 0) return 0;
        ref var anim = ref world.Store<CharacterAnimation>().Find(entity);
        if (Unsafe.IsNullRef(ref anim)) return 0;
        collapse = poses.Collapse(world.Service<CharacterAssets>().Kind(anim.Kind).Model);
        return collapse is null ? 0 : missing;
    }

    /// <summary>
    /// The ray from <paramref name="o"/> along the unit <paramref name="d"/> for <paramref name="length"/> against the
    /// entity's volumes: the nearest volume it meets (a ray starting inside one meets it at once), its lost parts skipped.
    /// The broad test first, so a character the ray passes far from is not posed.
    /// </summary>
    public bool Ray(World world, EntityId entity, Vector3 o, Vector3 d, float length, out VolumeHit hit)
    {
        hit = default;
        if (!Broad(world, entity, out var broad) || !ContactMath.Ray(o, d, length, broad, out _)) return false;
        var volumes = Posed(world, entity);
        uint missing = Missing(world, entity, out var collapse);
        bool found = false;
        float best = length;
        for (int i = 0; i < volumes.Length; i++)
        {
            if (missing != 0 && collapse is not null && collapse.VolumeGone(missing, volumes[i].Region, volumes[i].Bone)) continue;
            if (!ContactMath.Ray(o, d, best, volumes[i].Capsule, out var h)) continue;
            if (found && h.Time >= best) continue;
            best = h.Time;
            hit = new VolumeHit(i, volumes[i].Region, volumes[i].Bone, h);
            found = true;
        }
        return found;
    }
}

/// <summary>Registering the hit volumes in a world.</summary>
public static class HitVolumeRegistration
{
    /// <summary>Adds the <see cref="Combat.HitVolumes"/> service (after the animation: it reads <see cref="CharacterAssets"/> and <see cref="CharacterPoses"/>).</summary>
    public static WorldBuilder HitVolumes(this WorldBuilder builder) => builder.Service(new HitVolumes());
}
