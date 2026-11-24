// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// One thing riding another: the attachment code, all three of its functions.
using System.Numerics;

namespace Vitomir.Engine.World;

/// <summary>
/// The 64-byte record the attach pushes on the parent's list: the child, the flags
/// it was attached with, the bone (or −1 for the parent's own transform), and the child's
/// transform relative to the attach point.
/// </summary>
public sealed class Attachment(WorldThing child, uint flags, int bone, Matrix4x4 relative)
{
    public WorldThing Child { get; } = child;
    public uint Flags { get; } = flags;
    public int Bone { get; } = bone;
    public Matrix4x4 Relative { get; } = relative;
}

/// <summary>
/// Attaching, keeping up, and letting go. The flags are the engine's own: bit 0 is
/// read only when the child is let go, and bits 12 to 15 each *skip* a step of the attach.
/// </summary>
public static class Attachments
{
    /// <summary>`ATTACH_FLAG_AUTOREMOVE`: when the child is let go, it is destroyed.</summary>
    public const uint AutoRemove = 0x1;
    /// <summary>Leave the child where it is instead of snapping it onto the attach point.</summary>
    public const uint KeepPlace = 0x1000;
    /// <summary>Keep the relative rotation instead of resetting it to none.</summary>
    public const uint KeepRotation = 0x2000;
    /// <summary>Ignore the offset.</summary>
    public const uint NoOffset = 0x4000;
    /// <summary>Ignore the Euler angles.</summary>
    public const uint NoEuler = 0x8000;

    /// <summary>
    /// The attach. Refused for a child that already rides something and for a bone
    /// the parent's skeleton has not got. With nothing skipped — the scripts pass 1, which skips
    /// nothing — the child is snapped onto the attach point, the offset is turned into the attach
    /// point's frame and added, the relative rotation is reset and then rebuilt from
    /// <paramref name="euler"/>, which the script native fills from the child's own orientation.
    /// The child's matrix is then set to the parent's own, and it keeps up from the
    /// next frame on (<see cref="Update"/>).
    /// </summary>
    public static bool Attach(WorldThing parent, WorldThing child, int bone, Vector3 offset, Matrix4x4 euler, uint flags)
    {
        if (child.AttachedTo is not null || ReferenceEquals(parent, child)) return false;
        if (bone != -1 && (parent.Animation is not { } animation || (uint)bone >= (uint)animation.BoneCount)) return false;

        // The attach point at creation: the parent's matrix, or the bone's posed matrix as the
        // skeleton holds it (model space, and not multiplied by the parent's; with the
        // snap below that makes no difference to the relative transform, which is all it feeds).
        var at = bone == -1 ? parent.World : parent.Animation!.BoneMatrix(bone);
        var childWorld = (flags & KeepPlace) == 0 ? at : child.World;
        if ((flags & NoOffset) == 0) childWorld.Translation += Vector3.TransformNormal(offset, at);
        Matrix4x4.Invert(at, out var inverse);
        var relative = childWorld * inverse;
        var keep = relative.Translation;
        if ((flags & KeepRotation) == 0) relative = Matrix4x4.CreateTranslation(keep);
        if ((flags & NoEuler) == 0) { relative = euler; relative.Translation = keep; }

        parent.Attachments.Insert(0, new Attachment(child, flags, bone, relative));   // pushed on the front
        child.AttachedTo = parent;
        Place(child, parent.World, parent);
        return true;
    }

    /// <summary>
    /// The per-frame update: a thing riding nothing walks its children, each
    /// child is put at `attach point × relative`, and then its own children follow it — so a chain
    /// follows a bone as one. The attach point is the parent's matrix, or the bone's current posed
    /// matrix carried into the world by the parent's.
    /// </summary>
    public static void Update(WorldThing root)
    {
        if (root.AttachedTo is not null || root.Attachments.Count == 0) return;
        Follow(root);
    }

    static void Follow(WorldThing parent)
    {
        foreach (var a in parent.Attachments.ToArray())
        {
            var at = a.Bone == -1 || parent.Animation is not { } animation
                ? parent.World
                : animation.BoneMatrix(a.Bone) * Matrix4x4.CreateScale(parent.Scale) * parent.World;
            Place(a.Child, a.Relative * at, parent);
            if (a.Child.Attachments.Count > 0) Follow(a.Child);
        }
    }

    /// <summary>
    /// Puts a child at a world matrix. The engine keeps the child in the right sector as it goes,
    /// when the parent is in a world (the sector placing then the world's add, whose carry-over can say
    /// `exited`) — and does **no region test**: a rider does not move itself,
    /// so nothing else tests it either, and a torch carried into a region is not heard entering it.
    /// Earlier, Vitomir put the child through <see cref="Location.Move"/>, `SetPosition`'s whole
    /// work, region test included.
    /// </summary>
    static void Place(WorldThing child, Matrix4x4 world, WorldThing parent)
    {
        child.Orientation = WorldThing.OrientationOf(world);
        child.Position = world.Translation;
        if (parent.Place is { } place && child.InWorld) TrackMoves.Refile(place, child, pivot: false);
    }

    /// <summary>
    /// The child is let go and is a free thing again where it stands — or, attached with
    /// <see cref="AutoRemove"/>, it is destroyed. False when it was not riding this parent.
    /// </summary>
    public static bool Detach(WorldThing parent, WorldThing child)
    {
        if (!ReferenceEquals(child.AttachedTo, parent)) return false;
        int i = parent.Attachments.FindIndex(a => ReferenceEquals(a.Child, child));
        if (i < 0) return false;
        var record = parent.Attachments[i];
        parent.Attachments.RemoveAt(i);
        child.AttachedTo = null;
        if ((record.Flags & AutoRemove) != 0) child.Place?.Remove(child);
        return true;
    }

    /// <summary>
    /// The detach-all, from the destroy: every child is let go, and those attached with
    /// <see cref="AutoRemove"/> go with the parent.
    /// </summary>
    public static void DetachAll(WorldThing parent)
    {
        if (parent.Attachments.Count == 0) return;
        var all = parent.Attachments.ToArray();
        parent.Attachments.Clear();
        foreach (var a in all)
        {
            a.Child.AttachedTo = null;
            if ((a.Flags & AutoRemove) != 0) a.Child.Place?.Remove(a.Child);
        }
    }

    /// <summary>The <paramref name="index"/>-th thing riding a parent, or null.</summary>
    public static WorldThing? Nth(WorldThing parent, int index) =>
        index >= 0 && index < parent.Attachments.Count ? parent.Attachments[index].Child : null;
}
