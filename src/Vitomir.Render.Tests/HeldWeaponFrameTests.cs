// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Render.Tests;

/// <summary>
/// The weapon in the hand, on screen. The engine-side tests say the scythe's vertices land beside
/// the hand; these say the pixels arrive, which is a different question — a carried model has its
/// own slice of the vertex buffer and its own upload, and a slice written to the wrong offset would
/// pass every test that only looks at the geometry.
/// </summary>
[Collection("headless")]
public class HeldWeaponFrameTests(Headless h)
{
    void Ready() => Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");

    /// <summary>A Cappadocian in the monastery, seen from in front at about two metres.</summary>
    const string Level = "P1_MON2", Scene = "MON2_8_2";
    static FreeCamera Close() => new() { Position = new Vector3(110, 4, -112), Yaw = 0, Pitch = -6 };
    const int Wide = 640, Tall = 360;

    [Fact]
    public void A_character_s_weapon_reaches_the_screen()
    {
        Ready();
        _ = h.Frame(Level, scene: Scene, camera: Close(), width: Wide, height: Tall);   // see below
        var armed = h.Frame(Level, scene: Scene, camera: Close(), width: Wide, height: Tall);
        var empty = h.Frame(Level, scene: Scene, camera: Close(), prepare: Disarm, width: Wide, height: Tall);
        Assert.NotEqual(armed, empty);
        // Measured at 595 of the 230,400 pixels on software Vulkan — a quarter of one percent,
        // which is what a hand-sickle two metres away is worth. A handful would mean something
        // else moved instead of the weapon; a tenth of the frame would mean the character went.
        int changed = Differing(armed, empty);
        Assert.InRange(changed, 200, armed.Length / 16);
    }

    [Fact]
    public void Disarming_everyone_leaves_the_rest_of_the_picture_alone()
    {
        Ready();
        // Taking the weapons away twice gives the same picture, so the difference above is the
        // weapons and not the disarming.
        //
        // **Three frames, and the first is thrown away**, because of the first-frame difference
        // `FrameTests.A_level_draws_the_same_frame_twice` documents: on the cloud's software Vulkan
        // the very first frame a device draws differs from every later one in about 0.2% of its
        // pixels, and 0.2% is the same size as the thing this file is measuring. Whichever test
        // runs first in this collection pays that cost, so this one does not assume it is not the
        // one.
        _ = h.Frame(Level, scene: Scene, camera: Close(), prepare: Disarm, width: Wide, height: Tall);
        var a = h.Frame(Level, scene: Scene, camera: Close(), prepare: Disarm, width: Wide, height: Tall);
        var b = h.Frame(Level, scene: Scene, camera: Close(), prepare: Disarm, width: Wide, height: Tall);
        Assert.Equal(a, b);
    }

    /// <summary>Empties every hand in the place, the way the engine's SetWeapon(null) would.</summary>
    static void Disarm(Location place)
    {
        foreach (var animated in place.Mesh.Animated)
        {
            var thing = place.Drawn[animated.Thing];
            if (thing.Actor is not { } actor) continue;
            actor.Weapon = null;
            place.Mesh.Rearm(animated, thing, place.Files!, place.Materials);
        }
    }

    static int Differing(byte[] a, byte[] b)
    {
        int n = 0;
        for (int i = 0; i + 3 < a.Length; i += 4)
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2]) n++;
        return n;
    }
}
