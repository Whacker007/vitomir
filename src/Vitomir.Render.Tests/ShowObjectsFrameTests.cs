// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The show-objects key on screen (ours). `ShowObjectsTests` proves what it lights; this proves it is
// drawn, and that it leaves the characters alone.
using System.Numerics;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Render.Tests;

[Collection("headless")]
public class ShowObjectsFrameTests(Headless h)
{
    void Ready() => Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");

    /// <summary>
    /// The camera `GlowFrameTests` hovers the lever's barrel from: with the key held, the barrels glow the
    /// attack's blue together, and the Cappadocians standing among them — whom the pointer would light
    /// just as blue — do not change at all.
    /// </summary>
    [Fact]
    public void With_the_key_held_the_barrels_glow_blue_and_the_cappadocians_beside_them_do_not()
    {
        Ready();
        const int W = 320, H = 180;
        var place = Location.Load(h.Files!, "P1_MON1", scene: "MON1_8_2", player: "Christof");
        var me = place.Player!.Thing;
        using var frame = new FrameRenderer(h.Gpu!, place);
        var glow = new HoverGlow();
        frame.Glow = glow;
        var camera = new FreeCamera { Position = new Vector3(1414f, -1000f, -60f), Yaw = 90f, Pitch = -18f };

        _ = Headless.Shoot(frame, W, H, camera);
        var plain = Headless.Shoot(frame, W, H, camera);
        var found = ShowObjects.Find(place.Things, me, frame.ViewProjection!.Value, place.Particles.View);
        Assert.Contains(found.Keys, t => t.Type == ThingType.Container);
        Assert.DoesNotContain(found.Keys, ShowObjects.IsCharacter);
        glow.Show(found);
        glow.Step(1f);
        var lit = Headless.Shoot(frame, W, H, camera);

        var (changed, red, blue) = Diff(plain, lit);
        Assert.True(changed > 100, $"only {changed} pixels changed");
        Assert.True(blue > red * 2, $"blue rose by {blue} and red by {red}");

        // Each Cappadocian in view on its own, hovered: that is what the pointer would light — and none of
        // those pixels moved with the key held.
        foreach (var enemy in place.Things.Where(t => t.InWorld && t.Template.StartsWith("cappadocian", StringComparison.OrdinalIgnoreCase)
                                                     && ShowObjects.OnScreen(t, frame.ViewProjection!.Value, place.Particles.View)))
        {
            var hover = new HoverGlow();
            frame.Glow = hover;
            hover.Point(enemy, me);
            hover.Step(1f);
            var hovered = Headless.Shoot(frame, W, H, camera);
            for (int i = 0; i < plain.Length; i += 4)
                if (hovered[i + 2] != plain[i + 2] && lit[i + 2] != plain[i + 2])
                    Assert.Fail($"{enemy.Template} (guid {enemy.Guid}) changed under the key at pixel {i / 4}");
        }
    }

    static (int Changed, long Red, long Blue) Diff(byte[] a, byte[] b)
    {
        int changed = 0;
        long red = 0, blue = 0;
        for (int i = 0; i < a.Length; i += 4)
        {
            if (a[i] == b[i] && a[i + 1] == b[i + 1] && a[i + 2] == b[i + 2]) continue;
            changed++;
            red += b[i] - a[i];
            blue += b[i + 2] - a[i + 2];
        }
        return (changed, red, blue);
    }
}
