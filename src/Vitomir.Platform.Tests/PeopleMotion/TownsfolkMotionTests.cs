// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.People;
using Vitomir.Platform.Core;
using static Vitomir.Platform.Tests.PeopleMotion.PeopleWorlds;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// A townsperson of the models repository's crowd as a kind (<see cref="PersonStyle"/>): drawn at its own height, its walk a
/// style's clip at that clip's speed (scaled), its stand broken by the fidget without a jump at either seam, its feet keeping to the
/// ground walking the plain way and the styled way at its scale; and the player's kind as before. Skips without
/// <c>VITOMIR_MODELS_DIR</c>.
/// </summary>
public sealed class TownsfolkMotionTests(ITestOutputHelper output)
{
    static string F(float v, string format = "0.000") => v.ToString(format, CultureInfo.InvariantCulture);

    static readonly Lazy<(CharacterAssets Assets, int Plain, int Styled, PersonModel Person, float Scale)?> made = new(() =>
    {
        if (Environment.GetEnvironmentVariable(PeopleLibrary.FolderVariable) is not { Length: > 0 } dir || PeopleLibrary.TryOpen(dir, out _, decodeImages: false) is not { } lib)
            return null;   // a library of its own (PeopleLibraryTests' names its shared maps after the player)
        // The shortest of the crowd's men (person_04, 1.575 m), so the scale is well away from 1.
        var man = lib.Crowd().Where(p => p.Sex == "male").OrderBy(p => p.HeightMetres).First();
        var person = lib.Person(man.Recipe(["townsman_male"]));
        float scale = man.HeightMetres / person.BaseHeight;
        var assets = new CharacterAssets();
        var motions = lib.Motions("male");
        int plain = PeopleKinds.Register(assets, person, motions, "townsman", new PersonStyle { Scale = scale, FidgetAfter = 8f });
        int styled = PeopleKinds.Register(assets, person, motions, "old man", new PersonStyle { Walk = "walk_old", Scale = scale, FidgetAfter = 8f });
        return (assets, plain, styled, person, scale);
    });

    static (CharacterAssets Assets, int Plain, int Styled, PersonModel Person, float Scale) Made()
    {
        Assert.SkipUnless(made.Value is not null, "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        return made.Value!.Value;
    }

    [Fact]
    public void A_townsperson_is_drawn_at_its_height_walks_its_style_and_stands_with_the_fidget()
    {
        var (assets, plain, styled, _, scale) = Made();
        output.WriteLine($"scale {F(scale)}");
        Assert.InRange(scale, 0.85f, 0.98f);
        foreach (int k in new[] { plain, styled }) Assert.Equal(scale, assets.Kind(k).Scale);
        var old = assets.Kind(styled);
        var walk = old.Motion(MotionRole.Walk);
        var clip = (INotedClip)assets.Clip(walk.Clip);
        Assert.Equal("walk_old", clip.Name);
        Assert.Equal(clip.Notes.GroundSpeed * scale, walk.NaturalSpeed, 5);
        Assert.Null(old.Person!.Walk);                                         // one way: ahead
        Assert.NotNull(old.Person.Run);
        Assert.Equal(3.0f * scale, old.Motion(MotionRole.Run).NaturalSpeed, 3);
        var town = assets.Kind(plain);
        Assert.NotNull(town.Person!.Walk);                                     // the plain walk's eight ways
        Assert.Equal(1.25f * scale, town.Motion(MotionRole.Walk).NaturalSpeed, 3);

        var idle = Assert.IsType<FidgetClip>(assets.Clip(town.Motion(MotionRole.Idle).Clip));
        Assert.True(idle.Loops);
        Assert.Equal(8f, idle.StandSeconds, 3);
        // The seams: the stand's pose where the fidget starts, and where it ends and the stand begins again.
        var skeleton = assets.ModelOf(plain).Skeleton;
        float Jump(float before, float after)
        {
            var a = new BoneTransform[skeleton.Count];
            var b = new BoneTransform[skeleton.Count];
            idle.Sample(before, skeleton, a);
            idle.Sample(after, skeleton, b);
            float most = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                float dot = MathF.Min(1f, MathF.Abs(Quaternion.Dot(a[i].Rotation, b[i].Rotation)));
                most = MathF.Max(most, 2f * MathF.Acos(dot) * DetMath.RadToDeg);
            }
            return most;
        }
        float into = Jump(idle.StandSeconds - 1e-3f, idle.StandSeconds + 1e-3f), back = Jump(idle.Duration - 1e-3f, idle.Duration + 1e-3f);
        output.WriteLine($"the fidget's seams: into it {F(into, "0.00")}°, out of it {F(back, "0.00")}° (the most a bone turns)");
        Assert.InRange(into, 0f, 3f);
        Assert.InRange(back, 0f, 3f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_townsperson_at_its_scale_walks_with_its_planted_feet_nearly_still(bool styledWalk)
    {
        var (assets, plain, styled, _, scale) = Made();
        int kind = styledWalk ? styled : plain;
        float speed = assets.Kind(kind).Motion(MotionRole.Walk).NaturalSpeed;
        float yaw = DetMath.HalfPi;
        var v = new Vector3(DetMath.Cos(yaw), DetMath.Sin(yaw), 0f) * speed;
        var (world, e) = Make(assets, kind, Floor(), Vector3.Zero, yaw);
        var skeleton = assets.ModelOf(kind).Skeleton;
        int[] balls = [skeleton.Find("ball_l"), skeleton.Find("ball_r")];
        int settle = World.TicksPerSecond, ticks = 4 * World.TicksPerSecond;
        var path = new Vector3[2][];
        for (int f = 0; f < 2; f++) path[f] = new Vector3[ticks];
        var feet = Vector3.Zero;
        for (int t = 0; t < settle + ticks; t++)
        {
            feet += v * Tick;
            Step(world, e, feet, yaw, true, velocity: v);
            if (t >= settle) for (int f = 0; f < 2; f++) path[f][t - settle] = Bone(world, e, assets, balls[f]);
        }
        Assert.True(Finite(world, e));
        float sum = 0f;
        int n = 0;
        for (int f = 0; f < 2; f++)
        {
            float low = path[f].Min(p => p.Z);
            for (int t = 1; t < ticks; t++)
            {
                if (path[f][t].Z > low + 0.01f || path[f][t - 1].Z > low + 0.01f) continue;
                var d = path[f][t] - path[f][t - 1];
                sum += MathF.Sqrt(d.X * d.X + d.Y * d.Y) / Tick;
                n++;
            }
        }
        float mean = sum / n * 100f;
        string clip = assets.Clip(world.Get<CharacterAnimation>(e).Clip).Name;
        output.WriteLine($"{(styledWalk ? "walk_old" : "the plain walk")} at {F(speed, "0.00")} m/s, ×{F(scale)}: planted slide mean {F(mean, "0.0")} cm/s ({clip})");
        Assert.InRange(mean, 0f, 8f);   // as the player's (PeopleMotionTests); unscaled, the stride would be off by the scale (some 9 cm/s here)
    }

    [Fact]
    public void The_players_kind_is_as_the_people_made_it()
    {
        var (assets, k, _) = Player();
        var kind = assets.Kind(k);
        Assert.Equal(1f, kind.Scale);
        Assert.Equal("stand", assets.Clip(kind.Motion(MotionRole.Idle).Clip).Name);
        Assert.NotNull(kind.Person!.Walk);
        Assert.Equal(1.25f, kind.Motion(MotionRole.Walk).NaturalSpeed, 3);
    }
}
