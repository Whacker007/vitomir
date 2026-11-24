// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Engine.World;

/// <summary>
/// The modifier mask the engine builds once a frame and hands to every click — the world's
/// (for the ground and for a thing) and the interface's alike. The
/// bit order is the engine's own, from the mouse's input, which folds four pairs of key flags into one
/// number in exactly this order.
///
/// Which key each pair is follows 1.1's ground click and is confirmed against the shipped manual's
/// control list: bit 2 fires a ranged weapon at the floor, which the manual calls
/// <b>Alt + Left click</b>; bit 1 stops everyone who is keeping formation before the walk order,
/// which is <b>Ctrl + Left click</b>, "Party Regroup to Location". That leaves bit 0 as
/// <b>Shift</b>, and the manual agrees twice over — Shift on a target is the secondary attack and
/// Shift on the ground is the force run, which is one modifier doing two things because the engine
/// picks by what is under the cursor rather than by the key.
/// </summary>
[Flags]
public enum InputModifier
{
    None = 0,
    /// <summary>Secondary attack on a target, force run on the ground.</summary>
    Shift = 1,
    /// <summary>The coterie modifier: party attack on a target, party regroup on the ground.</summary>
    Ctrl = 2,
    /// <summary>Fire at the ground; pick an item up to the cursor.</summary>
    Alt = 4,
    /// <summary>Hold attack on a target; turn in place on the ground; cast without moving.</summary>
    Space = 8,
}
