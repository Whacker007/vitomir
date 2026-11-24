// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>What became of one placed thing of a 1.1 scene in a cell level (<see cref="CellScene"/>).</summary>
/// <param name="Scene">The scene file's name (<c>P1_OTDY</c>, <c>OTDY_4_1</c>).</param>
/// <param name="Index">Its number in that scene's own thing list.</param>
/// <param name="Type">Its template's <c>TYPE</c>.</param>
/// <param name="From">Where 1.1 has it (engine units).</param>
/// <param name="At">Where it is in the cell level.</param>
/// <param name="How">
/// <c>ground</c>: stood on the walk ground under it; <c>floor</c>: stood on solid ground that is not walk
/// ground (a yard, a bank); <c>kept</c>: left at 1.1's height, which is off the ground there too (a lantern on
/// a wall, a sign, a bird). A dropped thing says how it would have stood.
/// </param>
/// <param name="Dropped">Whether the level's drop rule left it out.</param>
/// <param name="Anchored">Whether an anchor of the level's moved it from 1.1's place.</param>
/// <param name="OverWalk">Whether there is walk ground anywhere under or over it.</param>
/// <param name="Inside">Whether its body (its template's radius and height) meets the new level's solid triangles.</param>
/// <param name="Protected">Why the drop rule may never take it, or empty: an actor, a container, a trigger, a script.</param>
public sealed record CellScenePlacement(string Scene, int Index, string Template, ThingType Type, string Script,
                                        Vector3 From, Vector3 At, string How, bool OverWalk, bool Inside,
                                        float Radius, float Height, string Protected, bool Dropped, bool Anchored)
{
    /// <summary>
    /// How much the level's ground lifts (<see cref="L2Lift"/>) added to its 1.1 height before the
    /// ground was looked for (engine units); 0 where the level has none, and for a thing an anchor placed.
    /// </summary>
    public float Lift { get; init; }

    /// <summary>Its anchor id, as the converter and the drop rules name it: the scene, <c>things</c>, its number.</summary>
    public string Id => $"{Scene}/things/{Index}";
}

/// <summary>
/// **Ours: a 1.1 scene in a cell level**. A cell level whose manifest names
/// the 1.1 scene it stands for (<see cref="L2Scene"/>) is loaded by the game's own scene loader
/// (<c>Location.Load</c>): the level's scene and the chapter's are read from the install as for a 1.1 level,
/// and pass through here on the way, where everything that says <em>where</em> is turned into the cell
/// level's terms. Nothing else about a thing changes, so its template, its script and its script's
/// parameters, its number in the thing list and its GUID are 1.1's.
///
/// <b>Heights.</b> The pilot keeps 1.1's plan and datum, so a thing's x and y are 1.1's. A thing within
/// <see cref="StandTolerance"/> of the cell level's walk ground stands on it (the ground under its 1.1
/// position, looked for from its own height, <see cref="CellCollision.Floor"/>); failing walk ground, on
/// solid ground met by a short sweep down that faces up. Anything else is off the ground in 1.1 too — a
/// lantern on a wall, a sign, a crow — and keeps its height. Starts, conversation starts and interest spots
/// stand on the ground within <see cref="StartWindow"/>. A thing's keyframes move with it.
///
/// <b>Lifts.</b> Where the level's ground is deliberately not 1.1's, the manifest says by how much
/// (<see cref="L2Lift"/>: the new walkable ground minus 1.1's, in patches of cells), and the lift of the cell
/// that holds a thing's 1.1 x and y is added to its 1.1 height <em>before</em> any of the above
/// (<see cref="Raised"/>): a thing that stood on 1.1's ground is then within the tolerance of the new ground
/// and stands on it, and one that hung over 1.1's ground hangs as far over the new. Everything that carries a
/// 1.1 height goes the same way: things (a region's hull and a thing's own geometry are in the thing's frame
/// and move with it; keyframes move by as much as the thing), player starts, conversation starts, interest
/// spots and point sounds. What the level itself places is not lifted: a thing moved by an anchor stands
/// where the anchor says, and the level's own start anchors are the level's. A level without lifts is placed
/// exactly as before.
///
/// <b>Sectors.</b> 1.1 files every thing under the sector it stands in (tag 3; a region lists the sectors
/// it touches, laid into them by the thing loader), and reads that sector for the thing's light (the
/// picker takes the sector's lights, and the sun only where the sector's flags carry 0x10000),
/// for the sounds heard there (each sector sound is hung on its sectors at load), for
/// whether it is drawn (a sector's things are drawn when the portal walk from the eye reaches the sector,
/// the portal walk and the sector draw) and for where its sweeps start. A cell level's shell is one sector (<see cref="CellShell"/>), sunlit, holding the
/// level's lights, so every sector number becomes 0 and each of those questions is answered for the whole
/// level: lit by the shell's sun and lights, drawn by the modern renderer's own culling, swept against the
/// cooked triangles. A sector sound is heard in the sectors it lists; with one sector only the scene's widest
/// (the level's ambience: the birds of the daytime Old Town) is kept, and the others — the river, the smithy's
/// hammer — are left out until areas carry sound.
///
/// <b>Anchors.</b> An anchor in the manifest whose id is a thing's (<c>OTDY_4_1/things/0</c>: the scene and
/// the thing's number in it; <see cref="L2Scene.Places"/>) moves that thing: it stands where the anchor says,
/// on the ground there, facing the anchor's way when it gives one. By design, moving a
/// person out of a new wall is an edit to the level, not to the scene or its scripts.
///
/// <b>Dropped things.</b> A thing the level's rule drops (<see cref="L2Scene.Drops"/>) keeps its place in the
/// thing list as an empty thing — no template, so no model, body, light, sound or script — because a script's
/// thing parameter is a number in that list and GUIDs are dealt in its order. An actor, a container,
/// a region, a thing with a script or script parameters, and a thing a script is handed are never dropped.
///
/// <b>Hidden things.</b> 1.1's click exits are its doors: each <c>tracker_thing</c> carries the door's own
/// geometry. The new town draws its own doors, so a click exit is kept whole — its <c>ClickExit</c> script,
/// its body, its name under the pointer — and only not drawn (<see cref="L2Scene.Hides"/>).
///
/// <b>What the scene keeps and what the cell level overrides.</b> Music, ambience and EAX are the scene's.
/// Fog, the far clip and the sky are the cell level's (the modern renderer's haze and sky; <see cref="CellShell.FarClip"/>).
/// </summary>
public static class CellScene
{
    /// <summary>How far from the ground under it a thing still counts as standing on it (engine units: half a metre).</summary>
    public const float StandTolerance = 36f;
    /// <summary>The render flag that keeps a thing's model from being drawn (tag 8's bit 0x2, <c>WorldThing.ModelHidden</c>).</summary>
    public const uint HiddenFlag = 0x2;
    /// <summary>How far above or below a start, a conversation start or an interest spot its ground is looked for (engine units).</summary>
    public const float StartWindow = 4 * CellLevel.FloorWindow;
    /// <summary>The body a thing with no radius of its own is tested with for <see cref="CellScenePlacement.Inside"/> (engine units).</summary>
    public const float DefaultRadius = 8f, DefaultHeight = 16f;
    /// <summary>The part of a body above its feet left out of the inside test, as the walker's step is (engine units).</summary>
    public const float Step = 18f;

    /// <summary>
    /// The level's own scene as <c>Location.Load</c> should have it: for a 1.1 level, what was read; for a
    /// cell level without a scene, what was read or else its starts (<see cref="CellLevel.Scene"/>), as before;
    /// for one that carries a scene, 1.1's scene placed in it (<see cref="Adapt"/>).
    /// </summary>
    public static NsdScene? World(NilLevel level, GameFiles files, string name, NsdScene? read)
    {
        if (CellLevel.Of(level) is not { } cells) return read;
        if (cells.Manifest.Scene is null || read is null) return read ?? cells.Scene;
        var placed = Adapt(cells, files, name, read);
        // 1.1's starts when it has any; the level's own (its start anchors) otherwise.
        return placed.Starts.Length > 0 ? placed : With(placed, cells.Scene.Starts);
    }

    /// <summary>The chapter scene as the loader should have it: placed in the cell level when it carries a scene, else as read.</summary>
    public static NsdScene? Chapter(NilLevel level, GameFiles files, string? name, NsdScene? read)
    {
        if (read is null || name is null || CellLevel.Of(level) is not { } cells || cells.Manifest.Scene is null) return read;
        return Adapt(cells, files, name, read);
    }

    /// <summary>
    /// <paramref name="scene"/> (the file called <paramref name="name"/>) turned into the cell level's terms,
    /// as the summary says; what became of each thing is added to <see cref="CellLevel.Placements"/> (a
    /// scene placed again replaces its own records).
    /// </summary>
    public static NsdScene Adapt(CellLevel cells, GameFiles files, string name, NsdScene scene)
    {
        var binding = cells.Manifest.Scene ?? new L2Scene();
        var collision = cells.Collision;
        var templates = new TemplateSet(files);
        foreach (var extra in scene.TemplateFiles)
            if (files.TryRead("misc/templates/" + (extra.Contains('.') ? extra : extra + ".not"), out var notData))
                templates.AddFile(NotFile.Read(notData, extra));

        // The things a script is handed: a parameter of a thing kind is a number in the scene's list.
        var handed = new HashSet<int>();
        foreach (var t in scene.Things)
            foreach (var p in t.Params)
                if (p.Type is NsdParam.Thing or NsdParam.Region or NsdParam.Actor or NsdParam.Player or NsdParam.Item or NsdParam.Prop)
                    handed.Add(p.Bits);

        // The things the level's anchors place: an anchor with a thing's id moves it.
        float scale = cells.Manifest.Units.EngineUnitsPerMetre;
        var anchored = new Dictionary<int, L2Anchor>();
        foreach (var a in cells.Manifest.Anchors)
            if (L2Scene.ThingOf(a.Id) is { } of && of.Scene.Equals(name, StringComparison.OrdinalIgnoreCase)) anchored[of.Index] = a;

        // Where the new ground is deliberately not 1.1's (ours): the lift under each 1.1 position.
        var lifts = cells.Manifest.Lifts;

        cells.Placements.RemoveAll(p => p.Scene.Equals(name, StringComparison.OrdinalIgnoreCase));
        var things = new NsdThing[scene.Things.Length];
        int dropped = 0, stood = 0, kept = 0, inside = 0, hidden = 0, lifted = 0;
        for (int i = 0; i < things.Length; i++)
        {
            var t = scene.Things[i];
            var tags = templates.Resolve(t);
            var type = tags.TryGetValue(Tags.Type, out var ty) ? (ThingType)ty.AsUInt() : ThingType.None;
            string script = tags.TryGetValue(Tags.Script, out var sc) ? sc.AsString() : "";
            float radius = tags.TryGetValue(Tags.Radius, out var ra) && ra.AsFloat() > 0 ? ra.AsFloat() : 0f;
            float height = tags.TryGetValue(Tags.Height, out var he) && he.AsFloat() > 0 ? he.AsFloat() : 0f;
            string guard = Guard(t, type, script, tags, handed.Contains(i));
            var from = t.Position;
            bool moved = anchored.TryGetValue(i, out var anchor) && anchor.Position.Length == 3;
            float lift = moved ? 0f : LiftAt(lifts, scale, from);
            var wanted = moved ? new Vector3(anchor!.Position[0], anchor.Position[1], anchor.Position[2]) * scale : new Vector3(from.X, from.Y, from.Z + lift);
            var facing = moved && anchor!.Rotation.Length == 3 && anchor.Kind != L2Scene.ThingAtAnchor ? new Vector3(anchor.Rotation[0], anchor.Rotation[1], anchor.Rotation[2]) : t.Orientation;
            if (lift != 0f) lifted++;
            var (at, how) = Stand(collision, wanted);
            bool overWalk = collision.Floor(wanted.X, wanted.Y, wanted.Z, 100_000f) is not null;
            bool blocked = t.Model is null && type != ThingType.Region
                           && !collision.Open(at, radius > 0 ? radius : DefaultRadius, Step, MathF.Max(height > 0 ? height : DefaultHeight, Step + 1f));
            bool drop = binding.Drops(name, i, t.Template, type == ThingType.Prop) && guard.Length == 0;
            if (drop)
            {
                dropped++;
                things[i] = NsdThing.Placed("", at, t.Orientation, 0);
            }
            else
            {
                if (how == "kept") kept++; else stood++;
                if (blocked) inside++;
                bool hide = binding.Hides(name, i, t.Template);
                if (hide) hidden++;
                things[i] = Moved(t, at, facing, hide ? (tags.TryGetValue(Tags.RenderFlags, out var rf) ? rf.AsUInt() : 0u) | HiddenFlag : null);
            }
            cells.Placements.Add(new CellScenePlacement(name, i, t.Template, type, script, from, at, how, overWalk, blocked, radius, height, guard, drop, moved) { Lift = lift });
        }

        // One sector: the widest sector sound is the level's ambience; the others have nowhere to be heard from.
        var widest = scene.SectorSounds.OrderByDescending(s => s.Sectors.Length).FirstOrDefault();
        NsdSectorSound[] sounds = widest is null ? [] : [widest with { Sectors = [0] }];

        var log = Diag.For(typeof(CellScene).FullName!);
        string summary = $"cell scene: {name} in {cells.Name}: {things.Length} things — {stood} on the ground, {kept} at 1.1's height, " +
                         $"{dropped} dropped, {hidden} not drawn, {inside} meeting new geometry{(lifts is null ? "" : $", {lifted} lifted with the ground")}; {scene.Starts.Length} starts, " +
                         $"{scene.ConversationStarts.Length} conversation starts, {scene.InterestSpots.Length} interest spots" +
                         (scene.SectorSounds.Length > sounds.Length
                            ? $"; sector sounds left out: {string.Join(", ", scene.SectorSounds.Where(s => !ReferenceEquals(s, widest)).Select(s => s.Wav))}" : "");
        log.LogInformation("{Summary}", summary);
        Console.WriteLine(summary);

        return new NsdScene
        {
            Version = scene.Version, ScratchA = scene.ScratchA, ScratchB = scene.ScratchB, SceneFlags = scene.SceneFlags,
            TemplateFiles = scene.TemplateFiles, Things = things, SectorSounds = sounds,
            PointSounds = lifts is null ? scene.PointSounds : [.. scene.PointSounds.Select(s => s with { Position = Raised(lifts, scale, s.Position) })],
            Starts = [.. scene.Starts.Select(s => s with { Sector = 0, Position = OnGround(collision, Raised(lifts, scale, s.Position)) })],
            InterestSpots = [.. scene.InterestSpots.Select(s => s with { Sector = 0, Position = OnGround(collision, Raised(lifts, scale, s.Position)) })],
            ConversationStarts = [.. scene.ConversationStarts.Select(s => s with { Sector = 0, Position = OnGround(collision, Raised(lifts, scale, s.Position)) })],
            Music = scene.Music, Ambient = scene.Ambient, SceneFlags2 = scene.SceneFlags2, Volume = scene.Volume,
            AmbientVolume = scene.AmbientVolume, Eax = scene.Eax,
            // The cell level's own: no 1.1 fog, sky or far clip (the modern renderer's haze and sky).
            FogType = 0, FogColor = scene.FogColor, FogDist1 = scene.FogDist1, FogDist2 = scene.FogDist2,
            ZClip = CellShell.FarClip, AtmosphereEffects = scene.AtmosphereEffects, SkyModel = "", SkyAnimation = "",
        };
    }

    /// <summary>
    /// Why a thing may never be dropped, or empty: it is a character, a container, a region, it has a script
    /// (its own or its template's) or script parameters, a script is handed it, or the scene carries its
    /// geometry (a tracker, a trigger, a door cut out of the level).
    /// </summary>
    static string Guard(NsdThing t, ThingType type, string script, Dictionary<uint, NodTag> tags, bool handed)
    {
        if (type is ThingType.Actor or ThingType.Player) return "an actor";
        if (type == ThingType.Container) return "a container";
        if (type == ThingType.Region || t.Sectors.Length > 0) return "a region";
        if (script.Length > 0) return $"has the script {script}";
        if (t.Params.Length > 0) return "has script parameters";
        if (handed) return "handed to a script";
        if (t.Model is not null) return "carries its own geometry";
        if (tags.TryGetValue(Tags.DropItem, out var di) && di.AsString().Length > 0) return "drops an item";
        return "";
    }

    /// <summary>Where a thing at <paramref name="p"/> in 1.1 goes, and how (<see cref="CellScenePlacement.How"/>).</summary>
    public static (Vector3 At, string How) Stand(CellCollision collision, Vector3 p)
    {
        if (collision.Floor(p.X, p.Y, p.Z, StandTolerance) is { } ground) return (p with { Z = ground }, "ground");
        // Solid ground that is not walk ground (a yard, a bank): the first surface a short sweep down meets, if it faces up.
        if (collision.Ray(p + new Vector3(0, 0, StandTolerance), -Vector3.UnitZ, 2 * StandTolerance) is { } hit
            && MathF.Abs(hit.Normal.Z) > 0.7f)
            return (hit.Point, "floor");
        return (p, "kept");
    }

    /// <summary>
    /// The lift under a 1.1 position (ours; <see cref="L2Lift"/>), engine units: the value of the
    /// cell of each of the level's patches that holds its x and y, added up; 0 for a level without lifts and
    /// outside every patch.
    /// </summary>
    public static float LiftAt(IReadOnlyList<L2Lift>? lifts, float scale, Vector3 p) =>
        lifts is null ? 0f : L2Lift.At(lifts, p.X / scale, p.Y / scale) * scale;

    /// <summary>A 1.1 position with the lift under it added to its height: where the ground is then looked for.</summary>
    public static Vector3 Raised(IReadOnlyList<L2Lift>? lifts, float scale, Vector3 p) =>
        lifts is null ? p : p with { Z = p.Z + LiftAt(lifts, scale, p) };

    /// <summary>A start's or a spot's position on the ground under it, when there is ground within <see cref="StartWindow"/>.</summary>
    static Vector3 OnGround(CellCollision collision, Vector3 p) =>
        p with { Z = collision.Floor(p.X, p.Y, p.Z, StartWindow) ?? p.Z };

    /// <summary>
    /// The placed thing at a new position in sector 0: its position and sector tags rewritten, its keyframes
    /// moved by as much, a region's sector list made the one sector; everything else as it was.
    /// </summary>
    static NsdThing Moved(NsdThing t, Vector3 at, Vector3 facing, uint? renderFlags = null)
    {
        var by = at - t.Position;
        var tags = new NodTag[t.Tags.Length];
        for (int i = 0; i < tags.Length; i++)
            tags[i] = t.Tags[i].Id switch
            {
                NsdThing.TagPosition => NodTag.Of(NsdThing.TagPosition, at),
                NsdThing.TagOrientation when facing != t.Orientation => NodTag.Of(NsdThing.TagOrientation, facing),
                NsdThing.TagSector => NodTag.Of(NsdThing.TagSector, 0u),
                _ => t.Tags[i],
            };
        if (renderFlags is { } flags)
            tags = [.. tags.Where(g => g.Id != Tags.RenderFlags), NodTag.Of(Tags.RenderFlags, flags)];
        return new NsdThing
        {
            Tags = tags,
            Keyframes = by == Vector3.Zero ? t.Keyframes : [.. t.Keyframes.Select(k => k with { Position = k.Position + by })],
            Params = t.Params, Model = t.Model, Sectors = t.Sectors.Length > 0 ? [0u] : t.Sectors,
            VertexColors = t.VertexColors, Offset = t.Offset,
        };
    }

    static NsdScene With(NsdScene s, NsdStart[] starts) => new()
    {
        Version = s.Version, ScratchA = s.ScratchA, ScratchB = s.ScratchB, SceneFlags = s.SceneFlags,
        TemplateFiles = s.TemplateFiles, Things = s.Things, SectorSounds = s.SectorSounds, PointSounds = s.PointSounds,
        Starts = starts, InterestSpots = s.InterestSpots, ConversationStarts = s.ConversationStarts,
        Music = s.Music, Ambient = s.Ambient, SceneFlags2 = s.SceneFlags2, Volume = s.Volume,
        AmbientVolume = s.AmbientVolume, Eax = s.Eax, FogType = s.FogType, FogColor = s.FogColor,
        FogDist1 = s.FogDist1, FogDist2 = s.FogDist2, ZClip = s.ZClip, AtmosphereEffects = s.AtmosphereEffects,
        SkyModel = s.SkyModel, SkyAnimation = s.SkyAnimation,
    };
}
