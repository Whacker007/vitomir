// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The noises an animation asks for by keyframe tag, and the floor a footstep is on.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Audio;

/// <summary>
/// The keyframe tag dispatcher for the tags that only make a noise. The others —
/// the strike, the cast, the codex callbacks — are somebody else's.
///
/// <list type="table">
/// <item><term>0–3</term><description><c>Lwalk</c>, <c>Rwalk</c>, <c>Lrun</c>, <c>Rrun</c>: a
/// footstep, <c>walk_&lt;floor&gt;</c> — <b>all four</b>, the run tags included. Nothing ever plays
/// a <c>run_…</c> event, though every character's foley file fills them.</description></item>
/// <item><term>7</term><description><c>Fall</c>: <c>death_bodyfall</c>.</description></item>
/// <item><term>9</term><description><c>Flap</c>: <c>flap</c>.</description></item>
/// <item><term>11, 12</term><description><c>Idle</c>, <c>Idle2</c>: <c>anim_idle</c>,
/// <c>anim_idle2</c>.</description></item>
/// </list>
/// </summary>
public static class KeyframeSounds
{
    public const uint Lwalk = 0, Rwalk = 1, Lrun = 2, Rrun = 3, Fall = 7, Flap = 9, Idle = 11, Idle2 = 12;

    /// <summary>Plays the noise a tag asks for, when it asks for one. Answers the channel or 0.</summary>
    public static int Sound(WorldThing thing, NadTag tag)
    {
        // Every machine plays the noises of the animations it shows, so these are not relayed (ours).
        using var _ = Net.HostSeen.Local();
        return tag.Type switch
        {
            Lwalk or Rwalk or Lrun or Rrun => Noise.Foley(thing, FoleyEvent.Walk + Floor(thing)),
            Fall => Noise.Foley(thing, FoleyEvent.DeathBodyFall),
            Flap => Noise.Foley(thing, FoleyEvent.Flap),
            Idle => Noise.Foley(thing, FoleyEvent.AnimIdle),
            Idle2 => Noise.Foley(thing, FoleyEvent.AnimIdle2),
            _ => 0,
        };
    }

    /// <summary>The ten words a material's <c>sound</c> keyword may say, in the engine's order.</summary>
    public static readonly string[] SurfaceWords = ["carpet", "dirt", "metal", "wet", "snow", "stone", "street", "wood", "creaky", "silent"];

    /// <summary>A material with no <c>sound</c> keyword is stone (the constructor's 5).</summary>
    public const int Stone = 5;
    /// <summary>What a walk surface with no material behind it counts as: wood.</summary>
    public const int NoMaterial = 7;

    /// <summary>
    /// The floor under a thing as a surface number, 0..9 — the thing's cached walk surface,
    /// that surface's material, and the material's <c>sound</c>. Standing on
    /// nothing is <b>0, carpet</b>: the dispatcher starts from <c>walk_carpet</c> and only moves off
    /// it when there is a surface to ask. And a <c>silent</c> floor is <b>9</b>, which is one past
    /// the nine walks — <c>walk_</c> + 9 lands on <c>run_carpet</c>. That is what the engine
    /// plays on a silent floor.
    /// </summary>
    public static int Floor(WorldThing thing)
    {
        if (thing.Place is not { } place || place.Walk is not { } walk) return 0;
        int node = walk.Find(thing.Position);
        if (node < 0) return 0;
        var n = walk.Nodes[node];
        return place.Audio?.SurfaceSound(place, n.Sector, n.Surface) ?? SurfaceSound(place, n.Sector, n.Surface);
    }

    /// <summary>
    /// The floor for a scuff: the same lookup, but a thing standing on nothing, or on a
    /// surface with no material, makes <b>no</b> scuff at all (−1) rather than falling back.
    /// </summary>
    public static int FloorOrNone(WorldThing thing)
    {
        if (thing.Place is not { } place || place.Walk is not { } walk) return -1;
        int node = walk.Find(thing.Position);
        if (node < 0) return -1;
        var n = walk.Nodes[node];
        int sound = place.Audio?.SurfaceSound(place, n.Sector, n.Surface) ?? SurfaceSound(place, n.Sector, n.Surface);
        return sound == NoMaterial && !HasMaterial(place, n.Sector, n.Surface) ? -1 : sound;
    }

    static bool HasMaterial(Location place, int sector, int surface)
    {
        var level = place.Level;
        if (sector < 0 || sector >= level.Sectors.Length) return false;
        var sec = level.Sectors[sector];
        if (surface < 0 || surface >= sec.Surfaces.Length) return false;
        int m = sec.Groups[sec.Surfaces[surface].Group].Material;
        return m >= 0 && m < level.Materials.Length;
    }

    /// <summary>The sound number of a level surface's material, read from its `.nam`.</summary>
    public static int SurfaceSound(Location place, int sector, int surface)
    {
        var level = place.Level;
        if (sector < 0 || sector >= level.Sectors.Length) return NoMaterial;
        var sec = level.Sectors[sector];
        if (surface < 0 || surface >= sec.Surfaces.Length) return NoMaterial;
        var group = sec.Groups[sec.Surfaces[surface].Group];
        if (group.Material < 0 || group.Material >= level.Materials.Length) return NoMaterial;
        return MaterialSound(place.Files, level.Materials[group.Material]);
    }

    /// <summary>
    /// The <c>sound</c> keyword of a level material, as its number; stone when it has none. Each
    /// material's answer is remembered for the files it was read from (ours): the `.nam` does not change,
    /// and a floor walked on in a new sector otherwise opened its file again.
    /// </summary>
    public static int MaterialSound(Vfs.GameFiles? files, string material)
    {
        if (files is null) return Stone;
        var known = materialSounds.GetValue(files, _ => new(StringComparer.OrdinalIgnoreCase));
        if (known.TryGetValue(material, out int sound)) return sound;
        if (!files.TryRead("materials/" + material.Replace('\\', '/') + ".nam", out var data)) return known[material] = Stone;
        int i = Array.IndexOf(SurfaceWords, NamMaterial.Read(data).Sound);
        return known[material] = i < 0 ? Stone : i;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Vfs.GameFiles, System.Collections.Concurrent.ConcurrentDictionary<string, int>> materialSounds = new();
}
