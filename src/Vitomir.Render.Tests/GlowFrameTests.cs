// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The hover glow on screen. `GlowTests` proves what the glow is; this proves it is drawn, and in
// the colour the reference build shows rather than the one 1.1 asks for.
using System.Numerics;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Render.Tests;

[Collection("headless")]
public class GlowFrameTests(Headless h)
{
    void Ready() => Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");

    [Fact]
    public void A_hovered_barrel_glows_blue_and_nothing_else_changes()
    {
        Ready();
        const int W = 320, H = 180;
        var place = Location.Load(h.Files!, "P1_MON1", scene: "MON1_8_2", player: "Christof");
        // The one tried in play, by the lever: the nearest `container_barrel1` to the start point, as
        // `--hover` picks it (scene thing 405). Found by place, not by its GUID, which moves whenever the
        // numbering does (436 under the game's one counter).
        var start = new Vector3(1414f, -495f, -270f);
        var barrel = place.Things.Where(t => t.InWorld && t.Template.Equals("container_barrel1", StringComparison.OrdinalIgnoreCase))
                                 .OrderBy(t => Vector3.Distance(t.Position, start)).First();
        using var frame = new FrameRenderer(h.Gpu!, place);
        var glow = new HoverGlow();
        frame.Glow = glow;
        // The shot `--start 1414,-495,-270 --hover container_barrel1` takes.
        var camera = new FreeCamera { Position = new Vector3(1414f, -902.5f, -104.4f), Yaw = 90f, Pitch = -14f };

        _ = Headless.Shoot(frame, W, H, camera);
        var plain = Headless.Shoot(frame, W, H, camera);
        glow.Point(barrel, place.Player!.Thing);
        glow.Step(1f);
        var lit = Headless.Shoot(frame, W, H, camera);

        int changed = 0;
        long red = 0, blue = 0;
        for (int i = 0; i < plain.Length; i += 4)
        {
            if (plain[i] == lit[i] && plain[i + 1] == lit[i + 1] && plain[i + 2] == lit[i + 2]) continue;
            changed++;
            red += lit[i] - plain[i];
            blue += lit[i + 2] - plain[i + 2];
        }
        Assert.True(changed > 50, $"only {changed} pixels changed");
        Assert.True(changed < W * H / 20, $"{changed} pixels changed: more than the barrel");
        // An attack's 0xFF600000 reaches the screen as blue on the reference build.
        Assert.True(blue > red * 2, $"blue rose by {blue} and red by {red}");

        // And the tint is gone the frame the pointer leaves, though the light is still fading.
        glow.Point(null, place.Player!.Thing);
        glow.Step(0.01f);
        var left = Headless.Shoot(frame, W, H, camera);
        long blueAfter = 0, redAfter = 0;
        for (int i = 0; i < plain.Length; i += 4)
        {
            blueAfter += left[i + 2] - plain[i + 2];
            redAfter += left[i] - plain[i];
        }
        Assert.True(blueAfter < blue / 3, $"the tint outlived the hover ({blueAfter} of {blue})");
        Assert.True(redAfter > 0, "the light went out with the tint");
    }
}
