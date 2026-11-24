// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Formats.L2;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// Every test that draws is in this collection, one at a time: a GPU device is made per renderer and
/// lavapipe, the cloud's software Vulkan, shares two CPUs with everything else.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GpuCollection
{
    public const string Name = "gpu";
}

/// <summary>
/// The headless renderer for tests, the East Gate's package and its views, and the test character. Pictures
/// are small (lavapipe is slow) unless <c>VITOMIR_RENDER_SIZE</c> asks for another (<c>WxH</c>), and are
/// written to <c>VITOMIR_RENDER_OUT</c> when it names a folder, to be looked at.
/// </summary>
internal static class Stills
{
    /// <summary>A headless renderer, or a skip with the reason when no GPU device can be made here.</summary>
    public static PlatformRenderer Headless(int width, int height, string quality = "low")
    {
        try { return PlatformRenderer.CreateHeadless(width, height, new RendererOptions(Quality: quality)); }
        catch (Exception e) when (e is SdlException or DllNotFoundException or InvalidOperationException)
        {
            Assert.Skip("no GPU device for headless rendering: " + e.Message);
            throw;
        }
    }

    /// <summary>The East Gate's package (<c>VITOMIR_TEST_LEVEL</c>), or a skip when it is not on this machine.</summary>
    public static string Level()
    {
        var path = Environment.GetEnvironmentVariable("VITOMIR_TEST_LEVEL");
        Assert.SkipUnless(path is { Length: > 0 } && File.Exists(Path.Combine(path, L2Package.ManifestFile)),
                          "no level package (set VITOMIR_TEST_LEVEL to the East Gate's eastgate.l2)");
        return path!;
    }

    /// <summary>The picture's size: <c>VITOMIR_RENDER_SIZE</c>, else the test's own small one.</summary>
    public static (int Width, int Height) Size(int width, int height)
    {
        var asked = Environment.GetEnvironmentVariable("VITOMIR_RENDER_SIZE")?.Split('x');
        return asked is [var w, var h] && int.TryParse(w, out int aw) && int.TryParse(h, out int ah) ? (aw, ah) : (width, height);
    }

    /// <summary>Saves the renderer's last picture as <c>&lt;VITOMIR_RENDER_OUT&gt;/&lt;name&gt;.png</c> when that folder is given.</summary>
    public static void Save(PlatformRenderer renderer, string name)
    {
        if (Environment.GetEnvironmentVariable("VITOMIR_RENDER_OUT") is { Length: > 0 } folder)
            renderer.SaveScreenshot(Path.Combine(folder, name + ".png"));
    }

    /// <summary>A still of one view: drawn as many times as the renderer's stills need (the reflections read the frame before).</summary>
    public static void Still(PlatformRenderer renderer, FrameView frame)
    {
        // VITOMIR_RENDER_SECONDS: the clock the still is drawn at (the clouds drift by it), a harness aid.
        if (double.TryParse(Environment.GetEnvironmentVariable("VITOMIR_RENDER_SECONDS"), CultureInfo.InvariantCulture, out double seconds)) frame.Seconds = seconds;
        for (int i = 0; i < renderer.StillFrames; i++) renderer.Draw(frame);
    }

    /// <summary>The old program's free camera's vertical view: 80° across at 4:3, kept vertical (Hor+).</summary>
    public static readonly float OldProgramFov = 2f * MathF.Atan(MathF.Tan(80f * MathF.PI / 360f) * 3f / 4f) * 180f / MathF.PI;

    /// <summary>The level's first start anchor: where it stands (metres) and its yaw (radians, from the anchor's rotation about +Z).</summary>
    public static (Vector3 Position, float Yaw) FirstStart(string package)
    {
        var start = L2Package.Open(package).Manifest.Anchors.First(a => a.Kind == "start");
        var p = new Vector3(start.Position[0], start.Position[1], start.Position[2]);
        return (p, start.Rotation.Length > 2 ? start.Rotation[2] * MathF.PI / 180f : 0f);
    }

    /// <summary>The first start's view: the eye 1.65 m above the anchor, looking along its yaw, level.</summary>
    public static ViewPoint StartView(string package)
    {
        var (p, yaw) = FirstStart(package);
        return new ViewPoint(p + new Vector3(0, 0, 1.65f), yaw, 0f, OldProgramFov);
    }

    /// <summary>The street from higher up: 12 m above the first start, 4 m back, looking along its yaw 20° down.</summary>
    public static ViewPoint StreetView(string package)
    {
        var (p, yaw) = FirstStart(package);
        var back = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0) * -4f;
        return new ViewPoint(p + back + new Vector3(0, 0, 12f), yaw, -20f * MathF.PI / 180f, OldProgramFov);
    }

    /// <summary>A view as the old program's flags: <c>--pos</c> in its units (72 to the metre), <c>--yaw</c> and <c>--pitch</c> in degrees.</summary>
    public static string OldFlags(ViewPoint v) => string.Create(CultureInfo.InvariantCulture,
        $"--pos {v.Eye.X * 72f},{v.Eye.Y * 72f},{v.Eye.Z * 72f} --yaw {v.Yaw * 180f / MathF.PI} --pitch {v.Pitch * 180f / MathF.PI}");

    /// <summary>The share of pixels where two pictures differ by more than <paramref name="tolerance"/> in any channel.</summary>
    public static float Differing(byte[] a, byte[] b, int tolerance = 0)
    {
        int differ = 0;
        for (int i = 0; i < a.Length; i += 4)
            if (Math.Abs(a[i] - b[i]) > tolerance || Math.Abs(a[i + 1] - b[i + 1]) > tolerance || Math.Abs(a[i + 2] - b[i + 2]) > tolerance) differ++;
        return (float)differ / (a.Length / 4);
    }

    /// <summary>The share of a rectangle's pixels whose red, green and blue (0 to 255) pass <paramref name="test"/>.</summary>
    public static float Share(byte[] rgba, int width, int x0, int y0, int x1, int y1, Func<Vector3, bool> test)
    {
        int yes = 0, n = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int i = (y * width + x) * 4;
                if (test(new Vector3(rgba[i], rgba[i + 1], rgba[i + 2]))) yes++;
                n++;
            }
        return n == 0 ? 0f : (float)yes / n;
    }

    /// <summary>The mean of a picture's red, green and blue over a rectangle, 0 to 255.</summary>
    public static Vector3 Mean(byte[] rgba, int width, int x0, int y0, int x1, int y1)
    {
        var sum = Vector3.Zero;
        int n = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int i = (y * width + x) * 4;
                sum += new Vector3(rgba[i], rgba[i + 1], rgba[i + 2]);
                n++;
            }
        return n == 0 ? Vector3.Zero : sum / n;
    }
}

/// <summary>
/// The test character: a column 1.8 m tall and 0.3 m square, its lower half on a root bone and its upper half on a
/// second bone at 0.9 m (a ring at the joint shared half and half), textured with a generated checker.
/// </summary>
internal static class TestColumn
{
    public const float Height = 1.8f, Joint = 0.9f, Half = 0.15f;
    public const string TextureName = "test/checker";

    public static SkinnedModel Make()
    {
        float[] rings = [0f, 0.3f, 0.6f, 0.8f, 0.9f, 1.0f, 1.2f, 1.5f, 1.8f];
        var corners = new Vector2[] { new(-Half, -Half), new(Half, -Half), new(Half, Half), new(-Half, Half) };
        var vertices = new List<SkinVertex>();
        var indices = new List<uint>();
        for (int side = 0; side < 4; side++)
        {
            var a = corners[side];
            var b = corners[(side + 1) % 4];
            var mid = (a + b) * 0.5f;
            var normal = Vector3.Normalize(new Vector3(mid.X, mid.Y, 0));
            uint first = (uint)vertices.Count;
            foreach (var z in rings)
            {
                var (bones, weights) = Weights(z);
                vertices.Add(new SkinVertex { Position = new Vector3(a.X, a.Y, z), Normal = normal, Uv = new Vector2(0, z / Height * 3f), Bones = bones, Weights = weights });
                vertices.Add(new SkinVertex { Position = new Vector3(b.X, b.Y, z), Normal = normal, Uv = new Vector2(1, z / Height * 3f), Bones = bones, Weights = weights });
            }
            for (uint r = 0; r + 1 < rings.Length; r++)
            {
                uint i0 = first + r * 2, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3;
                indices.AddRange([i0, i1, i3, i0, i3, i2]);
            }
        }
        uint top = (uint)vertices.Count;
        foreach (var c in corners)
            vertices.Add(new SkinVertex { Position = new Vector3(c.X, c.Y, Height), Normal = Vector3.UnitZ, Uv = (c / Half + Vector2.One) * 0.5f, Bones = SkinVertex.PackBones(1), Weights = new Vector4(1, 0, 0, 0) });
        indices.AddRange([top, top + 1, top + 2, top, top + 2, top + 3]);
        return new SkinnedModel
        {
            Name = "test column",
            Vertices = [.. vertices],
            Indices = [.. indices],
            Parts = [new ModelPart(0, indices.Count, TextureName, AlphaTest: false)],
            Skeleton = new Skeleton
            {
                Bones = [new BoneInfo("root", -1, BoneTransform.Identity), new BoneInfo("middle", 0, new BoneTransform(Quaternion.Identity, new Vector3(0, 0, Joint)))],
                InverseBind = [Matrix4x4.Identity, Matrix4x4.CreateTranslation(0, 0, -Joint)],
            },
            BoundsMin = new Vector3(-Half, -Half, 0),
            BoundsMax = new Vector3(Half, Half, Height),
        };
    }

    static (uint Bones, Vector4 Weights) Weights(float z) =>
        z < Joint - 0.01f ? (SkinVertex.PackBones(0), new Vector4(1, 0, 0, 0))
        : z > Joint + 0.01f ? (SkinVertex.PackBones(1), new Vector4(1, 0, 0, 0))
        : (SkinVertex.PackBones(0, 1), new Vector4(0.5f, 0.5f, 0, 0));

    /// <summary>The skin bent by <paramref name="radians"/> at the middle bone, about the model's Y axis.</summary>
    public static Matrix4x4[] Bent(float radians) =>
        [Matrix4x4.Identity, Matrix4x4.CreateTranslation(0, 0, -Joint) * Matrix4x4.CreateRotationY(radians) * Matrix4x4.CreateTranslation(0, 0, Joint)];

    /// <summary>A 64 × 64 checker of eight squares a side, brick red and cream.</summary>
    public static TextureImage Checker()
    {
        var rgba = new byte[64 * 64 * 4];
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                bool dark = ((x / 8) + (y / 8)) % 2 == 0;
                int i = (y * 64 + x) * 4;
                (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = dark ? ((byte)150, (byte)40, (byte)30, (byte)255) : ((byte)235, (byte)225, (byte)200, (byte)255);
            }
        return new TextureImage(TextureName, 64, 64, rgba);
    }
}
