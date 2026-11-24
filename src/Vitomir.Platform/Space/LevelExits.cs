// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats.L2;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Space;

/// <summary>
/// A box turned about Z (ours): an exit's walk-in volume as an L2 manifest writes it
/// (<see cref="L2AreaBox"/>) — the corner its sides run from, the
/// turn of its own x side from the level's x (radians, anticlockwise seen from above) and its sizes along its
/// own x, y and the vertical (metres). Its sine and cosine are worked out once, with
/// <see cref="DetMath"/>, when it is made.
/// </summary>
public readonly struct TurnedBox
{
    public Vector3 Origin { get; }
    public float RotZ { get; }
    public Vector3 Size { get; }
    readonly float sin, cos;

    public TurnedBox(Vector3 origin, float rotZ, Vector3 size)
    {
        Origin = origin; RotZ = rotZ; Size = size;
        (sin, cos) = DetMath.SinCos(rotZ);
    }

    /// <summary>The box's own x side as a direction in the level's plane.</summary>
    public Vector2 AxisX => new(cos, sin);
    /// <summary>The box's own y side as a direction in the level's plane.</summary>
    public Vector2 AxisY => new(-sin, cos);

    /// <summary>A point of the level in the box's own frame: along its x and y sides from the corner, and up from it.</summary>
    public Vector3 ToLocal(Vector3 p)
    {
        float dx = p.X - Origin.X, dy = p.Y - Origin.Y;
        return new Vector3(dx * cos + dy * sin, -dx * sin + dy * cos, p.Z - Origin.Z);
    }

    /// <summary>A point of the box's own frame in the level's.</summary>
    public Vector3 ToLevel(Vector3 local) =>
        new(Origin.X + local.X * cos - local.Y * sin, Origin.Y + local.X * sin + local.Y * cos, Origin.Z + local.Z);

    /// <summary>The middle of the box, in the level's frame.</summary>
    public Vector3 Centre => ToLevel(Size * 0.5f);

    /// <summary>Whether <paramref name="p"/> is inside the box.</summary>
    public bool Contains(Vector3 p)
    {
        var l = ToLocal(p);
        return l.X >= 0 && l.X <= Size.X && l.Y >= 0 && l.Y <= Size.Y && l.Z >= 0 && l.Z <= Size.Z;
    }

    /// <summary>A manifest's volume, or null when it is not sound (<see cref="L2AreaBox.Sound"/>).</summary>
    public static TurnedBox? From(L2AreaBox? box) =>
        box is { Sound: true }
            ? new TurnedBox(new Vector3(box.Origin[0], box.Origin[1], box.Origin[2]), box.RotZ, new Vector3(box.Size[0], box.Size[1], box.Size[2]))
            : null;

    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{Size.X:0.##}x{Size.Y:0.##}x{Size.Z:0.##} m from {Origin.X:0.##},{Origin.Y:0.##},{Origin.Z:0.##}{(RotZ != 0 ? $" turned {RotZ * DetMath.RadToDeg:0.#}°" : "")}");
}

/// <summary>
/// One of a level's exits as the platform knows it (ours): the number the
/// chronicle and the scripts know it by, the place it leads to and that place's level, whether the package
/// says it is shut, the door or gate it is at, where its sign stands (1.1's <c>ClickExit</c> "tracker",
/// metres) and the volume the player's body walks into to take it — null for an exit that is only clicked.
/// </summary>
public sealed record LevelExit(int Number, string To, string ToLevel, bool Closed, string? Door, Vector3? At, TurnedBox? Volume)
{
    /// <summary>The exit as the log and the status line name it: <c>exit 9: to SmithyDay (P1_SMYD)</c>.</summary>
    public string Name => $"exit {Number}{(Closed ? " (shut)" : "")}: to {(To.Length > 0 ? To : "?")}{(ToLevel.Length > 0 ? $" ({ToLevel})" : "")}";
}

/// <summary>
/// A level's exits (ours), read from its package's manifest (<see cref="L2Manifest.Exits"/>) with the
/// level's space (<see cref="LevelSpace.Exits"/>), in the manifest's order, and read-only while the world
/// steps. Today the player's body is stopped at a walk-in volume's edge and told which exit it reached
/// (<see cref="Bodies.ExitThresholdSystem"/>); taking an exit — the next level loaded, the party standing at
/// the entrance it arrives by (the movement design's) — is a later batch's, and will look the
/// exit up here by its number.
/// </summary>
public sealed class LevelExits
{
    /// <summary>A level without exits.</summary>
    public static LevelExits None { get; } = new([]);

    readonly LevelExit[] all;

    public LevelExits(IEnumerable<LevelExit> exits)
    {
        all = [.. exits];
        WalkIn = all.Count(e => e.Volume is not null);
    }

    /// <summary>Every exit, in the manifest's order (an array, so a system walks it without allocating).</summary>
    public ReadOnlySpan<LevelExit> All => all;

    public int Count => all.Length;

    /// <summary>The exits a body walks into (those with a <see cref="LevelExit.Volume"/>).</summary>
    public int WalkIn { get; }

    /// <summary>The exit at <paramref name="index"/> in <see cref="All"/>.</summary>
    public LevelExit this[int index] => all[index];

    /// <summary>The exit of a number, or null when the level has none of it.</summary>
    public LevelExit? Number(int number) => all.FirstOrDefault(e => e.Number == number);

    /// <summary>A manifest's exits: number, to, toLevel, closed, door, <c>at</c> and <c>volume</c> as written (a volume that is not sound is left out).</summary>
    public static LevelExits From(IEnumerable<L2Exit> exits) =>
        new(exits.Select(e => new LevelExit(e.Number, e.To ?? "", e.ToLevel ?? "", e.Closed, e.Door,
                                            e.At is { Length: >= 3 } at ? new Vector3(at[0], at[1], at[2]) : null,
                                            TurnedBox.From(e.Volume))));
}
