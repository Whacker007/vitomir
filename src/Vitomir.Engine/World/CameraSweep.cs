// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The two casts the camera makes into the world, read: the
// game camera's fat ray and the
// cutscene eye's thin one.
//
// They are the engine's general collision query (a 0x183c-byte record on the stack, filled by
// either cast and emptied nearest first) asked two different questions. Since
// Now the query is `WorldSweep`'s — the one the physics, the click, the sight line and the
// explosions ask — and this file is only the two questions and what the camera does with the
// answers. (An earlier build had a second copy of the query here; `WorldSweep`'s header says what the two
// differed in and which way each went.)
using System.Numerics;

namespace Vitomir.Engine.World;

/// <summary>
/// How far the camera can see before the world is in the way — the game camera's pull-in and the
/// cutscene eye's — as 1.1 casts them, on the engine's own sweep (<see cref="WorldSweep"/>).
///
/// <para><b>The game camera's cast</b> is the fat ray (<see cref="WorldSweep.Fat"/>)
/// asking for things, surfaces and camera regions (flags 0x23) with a radius of
/// 10 and a type mask that leaves out every thing but props (<c>0xfffffffb</c>), the
/// things walked sector by sector as the engine files them. Then it takes the nearest hit that is a
/// surface, a region, or a prop flagged <c>THING_FLAG_CAMERABLOCK</c> — any other prop it looks
/// past — and that distance becomes the camera's distance <em>and</em> the one it is heading
/// for.</para>
///
/// <para><b>The cutscene's cast</b> is the thin sweep asking for surfaces alone (flags
/// 2) with no radius, from the point the shot looks at out to the eye
/// (<see cref="WorldSweep.FirstSurface"/>), and the eye is put on the first surface it meets.</para>
/// </summary>
public static class CameraSweep
{
    /// <summary>The game camera's radius: <c>0x41200000</c>, 10.0.</summary>
    public const float Radius = 10f;

    /// <summary>
    /// The game camera's type mask: a set bit leaves that thing type out
    /// of the shape test, so <c>0xfffffffb</c> tests props and nothing else.
    /// </summary>
    public const uint GameCameraTypeMask = 0xfffffffb;

    /// <summary>
    /// The game camera's pull-in: how far from <paramref name="origin"/> along the unit
    /// <paramref name="direction"/> the eye can sit, <paramref name="length"/> being where it is, or
    /// null when nothing that counts is in the way. <paramref name="sector"/> is where the ray
    /// starts — the sector of the camera's target, found from the followed thing's own.
    /// </summary>
    public static float? GameCamera(Location place, int sector, Vector3 origin, Vector3 direction, float length)
    {
        var level = place.Level;
        if (Levels.Cells.CellLevel.Of(level) is { } cells) return Levels.Cells.CellSweep.Camera(cells, origin, direction, length, Radius);   // ours
        if (sector < 0 || sector >= level.Sectors.Length) return null;
        var asks = new WorldSweep.Asks(Pick.Tests.Both, TypeMask: GameCameraTypeMask, Regions: place.Regions,
                                       Filed: place.Props);
        // Nearest first, and the first that counts stops it.
        foreach (var met in WorldSweep.Fat(level, null, sector, origin, direction, length, Radius, asks))
            if (met.Kind == ContactKind.Surface || CountsForTheGameCamera(met.Thing)) return met.Distance;
        return null;
    }

    /// <summary>
    /// The game camera's filter over the thing hits: one counts when it is
    /// flagged <c>THING_FLAG_CAMERABLOCK</c> or is a region (type 3) — which only the query's
    /// camera-region list can bring, the type mask keeping regions out of the rest.
    /// </summary>
    static bool CountsForTheGameCamera(WorldThing? thing) =>
        thing is not null && (thing.Has(WorldThing.Flag.CameraBlock) || thing.Type == ThingType.Region);

    /// <summary>
    /// The cutscene eye's pull-in: a thin ray from the point the shot
    /// looks at to the eye, against the level's surfaces alone. Answers where it first meets one, or
    /// null — and the eye goes there, onto the surface itself, with nothing held back.
    /// </summary>
    public static Vector3? CutsceneEye(Location place, int sector, Vector3 lookAt, Vector3 eye)
    {
        var level = place.Level;
        if (sector < 0 || sector >= level.Sectors.Length) return null;
        var line = eye - lookAt;
        float length = line.Length();
        // The normalize: a zero line is left as it is and the query then meets nothing ahead.
        var direction = length > 0f ? line / length : line;
        return WorldSweep.FirstSurface(level, sector, lookAt, direction, length) is { } met
            ? lookAt + direction * met.Distance : null;
    }

    /// <summary>
    /// The sector a camera's ray starts in: the thing's own when the point is inside it,
    /// else whichever sector holds the point, else the thing's own anyway.
    /// </summary>
    public static int StartSector(Location place, Vector3 point, long hint)
    {
        int h = hint is >= 0 and <= int.MaxValue ? (int)hint : -1;
        int found = place.Solid?.SectorAt(point, h) ?? -1;
        return found >= 0 ? found : h;
    }
}
