// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>One exit of a cell level (ours): the chronicle's exit number, and what the level's manifest says of it.</summary>
/// <param name="Number">The exit's number in the chronicle's place the level stands for.</param>
/// <param name="To">The place 1.1's chronicle sends it to, as the layout recorded it (the chronicle running decides).</param>
/// <param name="ToLevel">That place's 1.1 level.</param>
/// <param name="At">Where its sign stands, engine units, or null.</param>
/// <param name="Volume">A volume that takes the exit when the leader walks into it (a gate), or null for one that is clicked.</param>
/// <param name="Area">The area of this same level the exit leads into — a room in the map — or null for one that leaves the level.</param>
public sealed record CellExit(int Number, string To, string ToLevel, Vector3? At, L2AreaBox? Volume, L2Area? Area);

/// <summary>
/// **Ours: the exits of a cell level that stands for one of a chronicle's places**.
/// The exit numbers are the place's own, so everything 1.1 does with an exit
/// goes on working: <see cref="Take"/> asks the chronicle exactly as a <c>ClickExit</c> sign's script does
/// (<c>CodexSequence.TakeExit</c>, the exit request: the exit must exist, be open and lead
/// somewhere; the request is state 4, taken by the main loop), and the journey that follows is
/// the campaign's, unchanged — the place the chronicle names is built or found, the party carried over and
/// stood on the entrance's starts.
///
/// Two things are new. **A room in the map**: when the manifest has an <see cref="L2Area"/> for the exit
/// (the smithy, exit 9), taking it — once the chronicle has said the exit exists and is open — walks the
/// party in through the door instead of changing the level (<see cref="Cross"/>). **A gate's volume**: an
/// exit with a <see cref="CellExit.Volume"/> is taken by the leader walking into it (<see cref="Walked"/>),
/// which 1.1 does with a <c>RegionExit</c> region and the Old Town's gates do with a sign.
/// </summary>
public sealed class CellExits
{
    /// <summary>What <see cref="Take"/> did.</summary>
    public enum Taken
    {
        /// <summary>Nothing: no chronicle is running here, or it refused — no such exit, shut, or leading nowhere.</summary>
        Refused,
        /// <summary>The journey is asked for; the main loop takes it this frame.</summary>
        Leaving,
        /// <summary>The exit's place is a room of this map: the party walks in through its door.</summary>
        WalkingIn,
    }

    readonly float unitsPerMetre;
    readonly HashSet<int> armed = [];
    bool walkLooked;

    public CellExits(L2Manifest manifest)
    {
        unitsPerMetre = manifest.Units.EngineUnitsPerMetre;
        var rooms = (manifest.Areas ?? []).Where(a => a.Exit is not null).ToList();
        var table = manifest.Exits.Select(e => new CellExit(e.Number, e.To, e.ToLevel,
            e.At is { Length: 3 } p ? new Vector3(p[0], p[1], p[2]) * unitsPerMetre : null, e.Volume,
            rooms.FirstOrDefault(a => a.Exit == e.Number))).ToList();
        // A room whose exit the manifest does not list is still a room.
        foreach (var a in rooms.Where(a => table.All(e => e.Number != a.Exit)))
            table.Add(new CellExit(a.Exit!.Value, a.Place ?? "", "", null, null, a));
        Table = [.. table.OrderBy(e => e.Number)];
    }

    /// <summary>The level's exits, by number.</summary>
    public IReadOnlyList<CellExit> Table { get; }

    /// <summary>The exit numbered <paramref name="n"/>, or null.</summary>
    public CellExit? this[int n] => Table.FirstOrDefault(e => e.Number == n);

    /// <summary>The chronicle whose place this level stands for, once the game has bound it (<see cref="CellLevel.Bind"/>); null for a level played on its own.</summary>
    public Chronicle? Chronicle { get; set; }

    /// <summary>An exit was asked for: its number, what came of it, and the manifest's line for it when it has one.</summary>
    public event Action<int, Taken, CellExit?>? Took;

    /// <summary>
    /// **The seam for whatever takes an exit** — a clicked sign, a gate's volume, the harness
    /// (<c>--journey "exit N@t"</c>): takes exit <paramref name="n"/> of the place this level stands for.
    /// </summary>
    public Taken Take(int n)
    {
        Taken taken = Taken.Refused;
        if (Chronicle is { } chronicle && chronicle.TakeExit(n))
            taken = chronicle.Leaving is null ? Taken.WalkingIn : Taken.Leaving;
        Took?.Invoke(n, taken, this[n]);
        return taken;
    }

    /// <summary>
    /// The same from a place: exit <paramref name="n"/> of <paramref name="place"/> taken when it is a cell
    /// level, <see cref="Taken.Refused"/> and nothing done when it is a 1.1 level (whose signs ask the
    /// chronicle themselves).
    /// </summary>
    public static Taken Take(Location place, int n) => CellLevel.Of(place.Level)?.Exits.Take(n) ?? Taken.Refused;

    /// <summary>
    /// The chronicle's question once exit <paramref name="n"/> has passed its three gates
    /// (<see cref="Chronicle.Threshold"/>): true when the exit's place is a room of this map and the leader
    /// of <paramref name="place"/> has been sent in through its door, so no journey is asked for.
    /// </summary>
    public bool Cross(int n, Location place)
    {
        if (this[n] is not { Area: { } room } || place.Player is not { } leader || place.Walk is not { } walk) return false;
        if (Inside(room) is not { } spot) return false;
        var target = spot * unitsPerMetre;
        int node = walk.Find(target, 256f);
        return node >= 0 && leader.WalkTo(target, node);
    }

    /// <summary>The point just inside a room's first door, metres: the door's own <c>inside</c>, or a metre in from its threshold towards the room's middle.</summary>
    public static Vector3? Inside(L2Area room)
    {
        if (room.Doors is not { Count: > 0 } doors) return null;
        var door = doors[0];
        if (door.Inside is { Length: 3 } i) return new Vector3(i[0], i[1], i[2]);
        if (door.Position is not { Length: 3 } t) return null;
        var b = room.Box;
        float c = MathF.Cos(b.RotZ), s = MathF.Sin(b.RotZ);
        var middle = new Vector3(b.Origin[0] + (c * b.Size[0] - s * b.Size[1]) / 2, b.Origin[1] + (s * b.Size[0] + c * b.Size[1]) / 2, t[2]);
        var threshold = new Vector3(t[0], t[1], t[2]);
        var into = middle - threshold;
        return into.LengthSquared() < 1e-6f ? threshold : threshold + Vector3.Normalize(into) * 1.2f;
    }

    /// <summary>Forgets which volumes the leader has been outside of — for an arrival, which may stand the party inside one.</summary>
    public void Reset() { armed.Clear(); walkLooked = false; }

    /// <summary>
    /// The leader is at <paramref name="feet"/> (engine units): the number of an exit whose volume the leader
    /// has just walked into, or null. A volume takes its exit only when entered from outside — one the party
    /// arrives inside does nothing until the leader has stepped out of it.
    /// </summary>
    public int? Walked(Vector3 feet)
    {
        int? into = null;
        var point = feet / unitsPerMetre + new Vector3(0, 0, CellAreas.BodyHeight);
        foreach (var e in Table)
        {
            if (e.Volume is not { } box) continue;
            bool inside = Depth(box, point) >= 0f;
            if (!inside) armed.Add(e.Number);
            else if (walkLooked && armed.Remove(e.Number)) into ??= e.Number;
        }
        walkLooked = true;
        return into;
    }

    static float Depth(L2AreaBox box, Vector3 point) => new L2Area { Box = box }.Depth(point);
}
