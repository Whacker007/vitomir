// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render.Characters;
using Vitomir.Platform.Render.Modern;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// The renderer's arithmetic that needs no GPU: where the picture lands in the window and the
/// characters' skinning.
/// </summary>
public sealed class RendererArithmeticTests
{
    [Fact]
    public void A_picture_of_another_shape_fits_the_window_centred_with_black_bars()
    {
        // Three screens (7680 × 1440) and a 2560 × 1440 picture: full height, centred, bars either side.
        Assert.Equal((2560, 0, 2560, 1440), Presenter.Fit(2560, 1440, 7680, 1440));
        // A picture wider than the window's shape: full width, bars above and below.
        Assert.Equal((0, 60, 640, 360), Presenter.Fit(1280, 720, 640, 480));
        // The window's own size: the whole of it.
        Assert.Equal((0, 0, 800, 600), Presenter.Fit(800, 600, 800, 600));
    }

    [Fact]
    public void A_first_person_view_draws_from_a_few_centimetres_and_other_views_from_the_renderers_own_near_plane()
    {
        // A hand 5 cm before the eye is drawn in first person and cut by the renderer's own 14 cm plane.
        var first = FreeCamera.From(new ViewPoint(Vector3.Zero, 0f, 0f, 80f, FirstPersonView.NearPlane));
        var third = FreeCamera.From(new ViewPoint(Vector3.Zero, 0f, 0f, 75f));
        Assert.Equal(FirstPersonView.NearPlane * ModernMath.UnitsPerMetre, first.Near, 0.001f);
        Assert.Equal(FreeCamera.DefaultNear, third.Near);
        static float Depth(FreeCamera camera, float metres)
        {
            var clip = Vector4.Transform(new Vector4(metres * ModernMath.UnitsPerMetre, 0f, 0f, 1f), ModernRenderer.ViewProjection(camera, 16f / 9f));
            return clip.Z / clip.W;      // reversed: 1 at the near plane, towards 0 far away; above 1 is cut
        }
        Assert.InRange(Depth(first, 0.05f), 0f, 1f);
        Assert.True(Depth(third, 0.05f) > 1f);
        Assert.InRange(Depth(third, 1f), 0f, 1f);
    }

    [Fact]
    public void A_draw_is_in_the_shadows_the_picture_or_both_as_its_passes_say()
    {
        // The first-person body is drawn twice, its hidden head and neck in the shadow only.
        var both = new ModernExtra.Draw(0, 3, Vector3.Zero, Vector3.One, 0, -1f, true, 0.7f, 0f, 0.5f);
        var seen = both with { Passes = ModernExtra.DrawPasses.View };
        var shadow = both with { Passes = ModernExtra.DrawPasses.Shadow };
        Assert.True(both.InShadows && both.InView);
        Assert.True(seen.InView && !seen.InShadows);
        Assert.True(shadow.InShadows && !shadow.InView);
    }

    [Fact]
    public void A_column_bent_at_its_middle_bone_keeps_its_foot_and_turns_its_top()
    {
        var model = TestColumn.Make();
        var into = new ModernVertex[model.Vertices.Length];
        var world = Matrix4x4.CreateTranslation(10, 20, 0);
        var (min, max) = CharacterPass.Skin(model, TestColumn.Bent(MathF.PI / 2), world, into);
        float u = ModernMath.UnitsPerMetre;
        for (int i = 0; i < model.Vertices.Length; i++)
        {
            var v = model.Vertices[i];
            var at = into[i].Position / u - new Vector3(10, 20, 0);
            if (v.Position.Z < TestColumn.Joint - 0.01f)
                Assert.True(Vector3.Distance(at, v.Position) < 1e-4f, $"a lower vertex moved: {v.Position} → {at}");
            else if (v.Position.Z > TestColumn.Joint + 0.01f)
            {
                // Turned a right angle about Y round the joint (row vectors: z goes to x, x to −z).
                var expected = new Vector3(v.Position.Z - TestColumn.Joint, v.Position.Y, TestColumn.Joint - v.Position.X);
                Assert.True(Vector3.Distance(at, expected) < 1e-4f, $"an upper vertex {v.Position} went to {at}, not {expected}");
            }
            Assert.True(MathF.Abs(into[i].Normal.Length() - 1f) < 1e-4f);
        }
        Assert.True(max.X / u - 10f >= TestColumn.Height - TestColumn.Joint - 1e-3f, "the box holds the bent top");
        Assert.True(min.Z >= -1e-3f);
    }
}

/// <summary>
/// The renderer on a GPU, headless (skipped where no device can be made): the overlay with no level, the East
/// Gate from its first start and from above, and a skinned character standing in it. Pictures are written to
/// <c>VITOMIR_RENDER_OUT</c> when it is set.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class RendererTests
{
    [Fact]
    public void Before_a_level_the_picture_is_black_but_for_the_reticle_the_banner_and_the_status_line()
    {
        var (w, h) = Stills.Size(320, 180);
        using var r = Stills.Headless(w, h);
        Assert.Equal((w, h), r.PixelSize);
        var frame = new FrameView
        {
            View = new ViewPoint(Vector3.Zero, 0, 0, 60),
            Reticle = new ReticleAnswer(ReticleTarget.Person, 7, Vector3.One, 2f, "someone", null, false),
            Banner = "RULES SANDBOX: not the shipping profile",
            StatusLine = "vitomir-platform 1.0  16.7 ms",
        };
        r.Draw(frame);
        Stills.Save(r, "overlay");
        var px = r.Pixels(out _, out _);
        var cross = Stills.Mean(px, w, w / 2, h / 2, w / 2 + 1, h / 2 + 1);
        Assert.True(cross.X > 180, $"the reticle's cross at the centre ({cross})");
        var top = Stills.Mean(px, w, 0, 0, w, 20);
        Assert.True(top.X > 20, $"the banner across the top ({top})");
        var status = Stills.Mean(px, w, 0, h - 16, w / 2, h);
        Assert.True(status.X > 8, $"the status line in the bottom-left corner ({status})");
        Assert.True(Stills.Mean(px, w, w / 2 + 20, h / 2 - 30, w - 20, h / 2 + 30).Length() < 1f, "black elsewhere");

        // Blocked: the same cross, tinted red.
        frame.Reticle = frame.Reticle with { Blocked = true };
        r.Draw(frame);
        var blocked = r.Pixels(out _, out _);
        var centre = Stills.Mean(blocked, w, w / 2, h / 2, w / 2 + 1, h / 2 + 1);
        Assert.True(centre.X > centre.Y + 60, $"a blocked reticle is red ({centre})");
    }

    [Fact]
    public void The_East_Gate_is_drawn_from_its_first_start_with_sky_above_and_walls_ahead()
    {
        var level = Stills.Level();
        var (w, h) = Stills.Size(320, 180);
        using var r = Stills.Headless(w, h, Environment.GetEnvironmentVariable("VITOMIR_RENDER_QUALITY") ?? "low");
        r.LoadLevel(level);
        var frame = new FrameView { View = Stills.StartView(level), ShowReticle = false };
        Stills.Still(r, frame);
        Stills.Save(r, "eastgate-start");
        var px = r.Pixels(out _, out _);
        // The gate's tower and the houses reach the top of this view, so the top's mean is the sky and the stone
        // together (a textured package's stone is warm and darker): what is asked is that a fair share of the
        // top is plainly sky-blue, which it is not when the sky is missing.
        float skyShare = Stills.Share(px, w, 0, 0, w, h / 12, c => c.Z > c.X + 8 && c.Z > 100);
        Assert.True(skyShare > 0.10f, $"sky at the top ({skyShare:P0} sky-blue; mean {Stills.Mean(px, w, 0, 0, w, h / 12)})");
        var ground = Stills.Mean(px, w, w / 3, h * 7 / 8, w * 2 / 3, h);
        Assert.True(ground.Length() > 60 && MathF.Abs(ground.Z - ground.X) < 60, $"the street at the bottom is lit, not sky ({ground})");

        // From higher up, looking down the street: more of the ground, less sky. A fresh renderer, as the old
        // program's stills are (the clouds and the reflections keep a history from frame to frame).
        using var r2 = Stills.Headless(w, h, Environment.GetEnvironmentVariable("VITOMIR_RENDER_QUALITY") ?? "low");
        r2.LoadLevel(level);
        frame.View = Stills.StreetView(level);
        Stills.Still(r2, frame);
        Stills.Save(r2, "eastgate-street");
        var above = r2.Pixels(out _, out _);
        Assert.True(Stills.Differing(px, above, 8) > 0.5f, "a different view");
    }

    [Fact]
    public void A_skinned_column_stands_bent_on_the_street_in_front_of_the_eye()
    {
        var level = Stills.Level();
        var (w, h) = Stills.Size(320, 180);
        using var r = Stills.Headless(w, h, Environment.GetEnvironmentVariable("VITOMIR_RENDER_QUALITY") ?? "low");
        r.LoadLevel(level);
        var checker = TestColumn.Checker();
        int asked = 0;
        r.CharacterTextures = name => { asked++; return name == TestColumn.TextureName ? checker : null; };
        var view = Stills.StartView(level);
        var frame = new FrameView { View = view, ShowReticle = false };
        Stills.Still(r, frame);
        var bare = r.Pixels(out _, out _);

        // Four metres ahead of the eye, on the anchor's ground, turned so the bend is seen from the side.
        var (start, yaw) = Stills.FirstStart(level);
        var ahead = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0);
        var place = Matrix4x4.CreateRotationZ(yaw + MathF.PI / 2) * Matrix4x4.CreateTranslation(start + ahead * 4f);
        frame.Characters.Add(new DrawnCharacter { Model = TestColumn.Make(), World = place, Skin = TestColumn.Bent(35f * MathF.PI / 180f), Entity = 1 });
        Stills.Still(r, frame);
        Stills.Save(r, "eastgate-column");
        var with = r.Pixels(out _, out _);
        Assert.True(asked >= 1, "the part's texture was asked for by name");
        // The column covers a patch round the middle of the picture, and nothing far from it changes much.
        float centre = Stills.Differing(Crop(bare, w, w * 3 / 8, h / 4, w * 5 / 8, h * 3 / 4), Crop(with, w, w * 3 / 8, h / 4, w * 5 / 8, h * 3 / 4), 12);
        Assert.True(centre > 0.04f, $"the column is drawn ahead ({centre:P0} of the middle changed)");
        float sky = Stills.Differing(Crop(bare, w, 0, 0, w, h / 10), Crop(with, w, 0, 0, w, h / 10), 12);
        Assert.True(sky < 0.05f, $"the sky is untouched ({sky:P0} changed)");
    }

    [Fact]
    public void An_evening_hour_moves_the_sun_and_no_hour_brings_the_levels_own_back()
    {
        var level = Stills.Level();
        var (w, h) = (160, 90);
        using var r = Stills.Headless(w, h);
        r.LoadLevel(level);
        var frame = new FrameView { View = Stills.StartView(level), ShowReticle = false };
        Stills.Still(r, frame);
        var own = r.Pixels(out _, out _);
        frame.TimeOfDay = 18f;
        Stills.Still(r, frame);
        Stills.Save(r, "eastgate-evening");
        var evening = r.Pixels(out _, out _);
        Assert.True(Stills.Mean(evening, w, 0, 0, w, h).Length() < Stills.Mean(own, w, 0, 0, w, h).Length() * 0.9f, "the evening is darker");
        frame.TimeOfDay = null;
        Stills.Still(r, frame);
        var again = r.Pixels(out _, out _);
        Assert.True(Stills.Differing(own, again, 6) < 0.05f, "the level's own sun is back");
    }

    [Fact]
    public unsafe void A_window_shows_a_picture_of_its_own_size_scaled_to_fit_with_bars()
    {
        Assert.SkipUnless(SDL.SDL3.SDL_Init(SDL.SDL_InitFlags.SDL_INIT_VIDEO), "no video: " + SDL.SDL3.SDL_GetError());
        var window = SDL.SDL3.SDL_CreateWindow("render test", 400, 300, 0);
        Assert.SkipWhen(window == null, "no window with this video driver");
        try
        {
            PlatformRenderer r;
            try { r = PlatformRenderer.Create((nint)window, new RendererOptions(Quality: "low", VSync: false, PictureWidth: 320, PictureHeight: 120)); }
            catch (SdlException e) { Assert.Skip("no swapchain here: " + e.Message); throw; }
            using (r)
            {
                Assert.Equal((320, 120), r.PixelSize);
                var (x, y, pw, ph) = r.PictureRect;
                Assert.True(pw <= 400 && ph < 300 && y > 0 && x == (400 - pw) / 2, $"a wide picture has bars above and below ({x}, {y}, {pw}, {ph})");
                r.Draw(new FrameView { View = new ViewPoint(Vector3.Zero, 0, 0, 60), StatusLine = "window" });
                r.Draw(new FrameView { View = new ViewPoint(Vector3.Zero, 0, 0, 60), StatusLine = "window" });
                var px = r.Pixels(out int w, out int h);
                Assert.Equal((320, 120), (w, h));
                Assert.True(Stills.Mean(px, w, w / 2, h / 2, w / 2 + 1, h / 2 + 1).X > 180, "the reticle in the picture");
            }
        }
        finally
        {
            SDL.SDL3.SDL_DestroyWindow(window);
            SDL.SDL3.SDL_Quit();
        }
    }

    static byte[] Crop(byte[] rgba, int width, int x0, int y0, int x1, int y1)
    {
        var o = new byte[(x1 - x0) * (y1 - y0) * 4];
        for (int y = y0; y < y1; y++) Array.Copy(rgba, (y * width + x0) * 4, o, (y - y0) * (x1 - x0) * 4, (x1 - x0) * 4);
        return o;
    }
}
