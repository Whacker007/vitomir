// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours: the sight lines a scene asks of the world, answered by a cell level's triangles.**
/// With 1.1's people and scripts in a cell level, the questions 1.1 puts to its sector trees have to be put to
/// the cooked collision instead — the shell that stands for the <c>.nil</c> has one sector with no surfaces,
/// so asked there they meet nothing and everything is seen through. A later change moved the walker, the pointer,
/// the game camera's pull-in and the general sweep (<see cref="CellSweep"/>, <see cref="CellLines"/>; the
/// cutscene camera's eye and the ground ray go through that sweep and so meet the
/// triangles already). The scene's work found two still asking 1.1's trees:
/// <list type="bullet">
/// <item>the clear-line test of a flier choosing its perch (the flier into the line test) and of a light's halo,
/// which walks 1.1's sectors and their surfaces itself: <see cref="Clear"/>;</item>
/// <item>one character seeing another, which went through the general sweep and then read the
/// surface it met out of 1.1's sector to ask whether it is translucent — in a cell level there is none to
/// read, and the first character to look at another through a house stopped the game (<c>Rpg.Sight.Sees</c>:
/// a cooked triangle stops the line).</item>
/// </list>
/// A cooked triangle is solid from both sides; 1.1's surfaces stop these lines only from the front.
/// </summary>
public static class CellSceneSweeps
{
    /// <summary>
    /// The line test's question on a cell level: whether nothing solid stops a ray from <paramref name="from"/>
    /// along <paramref name="direction"/> for <paramref name="reach"/> units of the direction's own length.
    /// </summary>
    public static bool Clear(CellLevel cells, Vector3 from, Vector3 direction, float reach)
    {
        float unit = direction.Length();
        if (unit <= 0f || reach <= 0f) return true;
        return cells.Collision.Ray(from, direction / unit, unit * reach) is null;
    }
}
