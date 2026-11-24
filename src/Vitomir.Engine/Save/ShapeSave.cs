// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Engine.Save;

/// <summary>
/// A shape change in force (<see cref="Rpg.ShapeShift"/>): what 1.1 writes of the record at
/// the actor's shape change at the end of the actor — the template's
/// name and the flags, nothing it kept. The load reads them back into a bare record
/// and the post-load step makes the shape again from them.
/// </summary>
public sealed class ShapeSave
{
    public string Template { get; set; } = "";
    public uint Flags { get; set; }
}
