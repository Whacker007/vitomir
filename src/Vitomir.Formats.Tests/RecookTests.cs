// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Formats.Tests;

/// <summary>
/// **The recooked text gives back what the engine reads, file for file** (ours): every shipped template, scene and camera path written
/// as text, read back, written as the engine's binary and read again comes out the original — every tag's bytes the engine reads, every
/// keyframe, parameter, brush model, sector and colour, every sound, start and spot; and every path's curve, worked out again, plays the
/// original's to within float rounding.
/// </summary>
public class RecookTests
{
    static IEnumerable<(string Name, byte[] Data)> Every(string extension)
    {
        GameData.RequireInstall();
        foreach (var archive in new[] { "Resource.nob", "Levels.nob", "Local_Eng.nob", "LMaterials.nob", "Sounds.nob" })
        {
            string path = Path.Combine(GameData.Dir!, archive);
            if (!File.Exists(path)) continue;
            using var nob = new NobArchive(path);
            foreach (var name in nob.NamesWithExtension(extension).ToList()) yield return (name, nob.Read(name));
        }
    }

    static void SameTags(NodTag[] want, NodTag[] got, string where)
    {
        Assert.True(want.Length == got.Length, $"{where}: {got.Length} tags, not {want.Length}");
        for (int i = 0; i < want.Length; i++)
        {
            Assert.True(want[i].Id == got[i].Id, $"{where}: tag {i} is {got[i].Id}, not {want[i].Id}");
            Assert.True(TagTable.Meaningful(want[i]).AsSpan().SequenceEqual(got[i].Raw), $"{where}: {TagTable.NameOf(want[i].Id)} differs");
        }
    }

    [Fact]
    public void Every_template_comes_back_tag_for_tag()
    {
        int files = 0, templates = 0;
        foreach (var (name, data) in Every(".not"))
        {
            files++;
            var original = NotFile.Read(data, name);
            var back = new List<NotTemplate>();
            foreach (var t in original.Templates)
            {
                string file = NotText.FileName(t.Name);
                Assert.Equal(t.Name, NotText.NameOf(file));
                var read = NotText.Read(RecookText.Parse(NotText.Write(t, name), file), NotText.NameOf(file));
                SameTags(t.Tags, read.Tags, $"{name} {t.Name}");
                back.Add(read);
                templates++;
            }
            var again = NotFile.Read(NotFileWrite.Write(back), name);
            Assert.Equal(original.Templates.Select(t => t.Name), again.Templates.Select(t => t.Name));
            for (int i = 0; i < again.Templates.Length; i++) SameTags(original.Templates[i].Tags, again.Templates[i].Tags, $"{name} {again.Templates[i].Name} (binary)");
        }
        Assert.Equal(4, files);
        Assert.Equal(2619, templates);
    }

    [Fact]
    public void Every_scene_comes_back_thing_for_thing()
    {
        int scenes = 0, things = 0;
        foreach (var (name, data) in Every(".nsd"))
        {
            scenes++;
            var a = NsdScene.Read(data, name);
            var (text, geometry) = NsdText.Write(a, name);
            var fromText = NsdText.Read(RecookText.Parse(text, name), geometry);
            var b = NsdScene.Read(NsdWrite.Write(fromText), name);
            Assert.Equal(a.Things.Length, b.Things.Length);
            for (int i = 0; i < a.Things.Length; i++)
            {
                things++;
                var (x, y) = (a.Things[i], b.Things[i]);
                string where = $"{name} thing {i}";
                SameTags(x.Tags, y.Tags, where);
                Assert.Equal(x.Keyframes, y.Keyframes);
                Assert.Equal(x.Params.Select(p => (p.Type, p.Bits, p.Text)), y.Params.Select(p => (p.Type, p.Bits, p.Text)));
                Assert.True((x.ModelBytes ?? []).AsSpan().SequenceEqual(y.ModelBytes ?? []), $"{where}: the brush model differs");
                Assert.Equal(x.Sectors, y.Sectors);
                Assert.Equal(x.VertexColors, y.VertexColors);
            }
            Assert.Equal(a.HasSounds, b.HasSounds);
            Assert.Equal(a.SectorSounds.Select(s => (s.Wav, s.Volume, s.Balance, s.Flags, string.Join(",", s.Sectors))),
                         b.SectorSounds.Select(s => (s.Wav, s.Volume, s.Balance, s.Flags, string.Join(",", s.Sectors))));
            Assert.Equal(a.PointSounds.Select(s => (s.Wav, s.Volume, s.Balance, s.Flags, s.Position, s.MinDist, s.MaxDist, string.Join(",", s.Sectors))),
                         b.PointSounds.Select(s => (s.Wav, s.Volume, s.Balance, s.Flags, s.Position, s.MinDist, s.MaxDist, string.Join(",", s.Sectors))));
            Assert.Equal(Starts(a.Starts), Starts(b.Starts));
            Assert.Equal(Starts(a.ConversationStarts), Starts(b.ConversationStarts));
            Assert.Equal(a.InterestSpots.Select(s => (s.Sector, string.Join(",", s.Rotation), s.Position, s.Group, s.Radius)),
                         b.InterestSpots.Select(s => (s.Sector, string.Join(",", s.Rotation), s.Position, s.Group, s.Radius)));
            Assert.Equal((a.SceneFlags, string.Join(",", a.TemplateFiles), a.Music, a.Ambient, a.SceneFlags2, a.Volume, a.AmbientVolume, a.Eax),
                         (b.SceneFlags, string.Join(",", b.TemplateFiles), b.Music, b.Ambient, b.SceneFlags2, b.Volume, b.AmbientVolume, b.Eax));
            Assert.Equal((a.FogType, a.FogColor, a.FogDist1, a.FogDist2, a.ZClip, a.AtmosphereEffects, a.SkyModel, a.SkyAnimation),
                         (b.FogType, b.FogColor, b.FogDist1, b.FogDist2, b.ZClip, b.AtmosphereEffects, b.SkyModel, b.SkyAnimation));
        }
        Assert.Equal(291, scenes);
        Assert.Equal(15942, things);

        static IEnumerable<(uint, string, Vector3, uint, uint)> Starts(NsdStart[] s) =>
            s.Select(x => (x.Sector, string.Join(",", x.Rotation), x.Position, x.Group, x.Number));
    }

    /// <summary>
    /// Every path, its curves worked out again from its keys, puts the camera where the original does: sampled along every segment the
    /// camera plays, the eye within a hundredth of a unit and the angles within a thousandth of a degree.
    /// </summary>
    [Fact]
    public void Every_camera_path_plays_as_the_original()
    {
        int paths = 0, segments = 0;
        double worstEye = 0, worstAngle = 0;
        foreach (var (name, data) in Every(".ncp"))
        {
            paths++;
            var a = NcpCameraPath.Read(data, name);
            var built = NcpText.Read(RecookText.Parse(NcpText.Write(a, name), name));
            var b = NcpCameraPath.Read(NcpText.WriteBinary(built), name);
            Assert.Equal(a.Keyframes.Length, b.Keyframes.Length);
            Assert.True(MathF.Abs(a.TotalLength - b.TotalLength) <= 1e-3f * MathF.Max(1f, a.TotalLength), $"{name}: length {b.TotalLength}, not {a.TotalLength}");
            for (int i = 0; i + 1 < a.Keyframes.Length; i++)
            {
                var (x, y) = (a.Keyframes[i], b.Keyframes[i]);
                Assert.Equal(x.Position, y.Position);
                Assert.Equal(x.Angles, y.Angles);
                if (a.Keyframes[i + 1].Distance <= x.Distance) continue;      // a segment of no length: never played
                segments++;
                for (float t = 0f; t <= 1.0001f; t += 0.125f)
                {
                    worstEye = Math.Max(worstEye, Vector3.Distance(Eval(x.Position, x.PositionSpline, t), Eval(y.Position, y.PositionSpline, t)));
                    worstAngle = Math.Max(worstAngle, (Eval(x.Angles, x.AngleSpline, t) - Eval(y.Angles, y.AngleSpline, t)).Length());
                }
                Assert.True(MathF.Abs(x.InvSegmentLength - y.InvSegmentLength) <= 1e-4f * x.InvSegmentLength, $"{name} key {i}: 1/length differs");
            }
        }
        Assert.Equal(174, paths);
        Assert.True(segments > 280, $"only {segments} segments played");
        Assert.True(worstEye < 0.01, $"the eye is off by {worstEye}");
        Assert.True(worstAngle < 0.001, $"the angles are off by {worstAngle}");

        static Vector3 Eval(Vector3 at, float[] s, float t) =>
            new(at.X + t * s[0] + t * t * s[3] + t * t * t * s[6], at.Y + t * s[1] + t * t * s[4] + t * t * t * s[7], at.Z + t * s[2] + t * t * s[5] + t * t * t * s[8]);
    }

    [Fact]
    public void A_value_reads_back_by_its_type_and_a_wrong_one_says_why()
    {
        var flags = TagTable.Of("THINGFLAGS")!;
        var tag = TagTable.Read(flags.Id, "NO HIGHLIGHT, STATIC SHADOW", out _)!.Value;
        Assert.Equal("NO HIGHLIGHT, STATIC SHADOW", TagTable.Write(tag));
        Assert.Equal("none", TagTable.Write(TagTable.Read(flags.Id, "none", out _)!.Value));
        Assert.Null(TagTable.Read(flags.Id, "NO SUCH FLAG", out var why));
        Assert.Contains("NO SUCH FLAG", why);
        var type = TagTable.Of("TYPE")!;
        Assert.Equal("PROP", TagTable.Write(TagTable.Read(type.Id, "prop", out _)!.Value));
        Assert.Null(TagTable.Read(TagTable.Of("MODEL")!.Id, new string('x', 32), out why));
        Assert.Contains("31", why);
        Assert.Equal("_Bottles%2FFlasks.txt", NotText.FileName("_Bottles/Flasks"));
        Assert.Equal("_Bottles/Flasks", NotText.NameOf("_Bottles%2FFlasks.txt"));
    }
}
