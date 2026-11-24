// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Render.Characters;
using Vitomir.Platform.Render.Modern;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// The viewer's own body in first person is drawn one-sided, each triangle wound to face the way its
/// vertices' normals point (<see cref="CharacterPass.Outward"/>), whatever the model's own winding, so its inside is never drawn.
/// </summary>
public sealed class FirstPersonBodyTests
{
    static ModernVertex V(Vector3 p, Vector3 n) => new() { Position = p, Normal = n };

    [Fact]
    public void A_triangle_is_outward_when_its_winding_agrees_with_its_normals_and_turned_when_not()
    {
        var up = Vector3.UnitZ;
        // Counter-clockwise seen from +Z: its winding's normal is +Z.
        ModernVertex[] v = [V(new(0, 0, 0), up), V(new(1, 0, 0), up), V(new(0, 1, 0), up)];
        Assert.True(CharacterPass.Outward(v, 0, 1, 2));
        Assert.False(CharacterPass.Outward(v, 0, 2, 1));
        // The same triangle whose normals point down is outward the other way round.
        ModernVertex[] down = [V(new(0, 0, 0), -up), V(new(1, 0, 0), -up), V(new(0, 1, 0), -up)];
        Assert.False(CharacterPass.Outward(down, 0, 1, 2));
        Assert.True(CharacterPass.Outward(down, 0, 2, 1));
    }
}
