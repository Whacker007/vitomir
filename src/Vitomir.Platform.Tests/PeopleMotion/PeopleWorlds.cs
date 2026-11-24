// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets.People;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests.PeopleMotion;

/// <summary>
/// Worlds for the people in motion: a body, its move, the animation and its rig on a level, one person in it, the
/// body moved by the test (standing in for the body's own motor), as <see cref="TraversalPoseTests"/> does for 1.1's.
/// </summary>
internal static class PeopleWorlds
{
    public const float Tick = World.TickSeconds;

    /// <summary>The player (the base man dressed) registered as a kind from the models repository's files, once (null without <c>VITOMIR_MODELS_DIR</c>).</summary>
    static readonly Lazy<(CharacterAssets Assets, int Kind, PersonModel Person)?> player = new(() => Load(PersonRecipe.Player, "player"));
    static readonly Lazy<(CharacterAssets Assets, int Kind, PersonModel Person)?> woman = new(() => Load(PersonRecipe.Woman, "woman"));

    static (CharacterAssets, int, PersonModel)? Load(PersonRecipe recipe, string name)
    {
        if (People.PeopleLibraryTests.Library.Value is not { } lib) return null;
        var person = lib.Person(recipe);
        var assets = new CharacterAssets();
        int kind = PeopleKinds.Register(assets, person, lib.Motions(recipe.Sex), name);
        return (assets, kind, person);
    }

    /// <summary>The player as a kind (skips the test without the files).</summary>
    public static (CharacterAssets Assets, int Kind, PersonModel Person) Player()
    {
        Assert.SkipUnless(player.Value is not null, "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        return player.Value!.Value;
    }

    /// <summary>The woman as a kind (skips the test without the files).</summary>
    public static (CharacterAssets Assets, int Kind, PersonModel Person) Woman()
    {
        Assert.SkipUnless(woman.Value is not null, "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        return woman.Value!.Value;
    }

    /// <summary>A flat floor 40 m square round the origin.</summary>
    public static LevelSpace Floor() => new TestLevel().Floor(-20, -20, 20, 20).Build("floor");

    /// <summary>A world with a body, its move, the animation and its rig on <paramref name="level"/>, one person at <paramref name="feet"/> facing <paramref name="yaw"/>.</summary>
    public static (World World, EntityId Person) Make(CharacterAssets assets, int kind, LevelSpace level, Vector3 feet, float yaw = DetMath.HalfPi, ulong seed = 7,
                                                     LegTurnSettings? legs = null, AnimationSettings? animation = null, PersonFeetSettings? feetRules = null,
                                                     PersonGroundSettings? groundRules = null)
    {
        // The session's speed by direction (DirectionPace): the walk or the run chosen by the pace ahead, as in the game.
        // The people's feet as the session's by default (planted, the heel lift, the bend rule), or the earlier build's (PersonFeetSettings.Off).
        // The people on stairs and slopes likewise (PersonGroundSettings.Off: the earlier build's).
        var b = new WorldBuilder(seed).Component<Body>().Component<Traversal>().Component<MoveIntent>().Service(level).Animation(assets, animation, legs: legs, feet: feetRules, ground: groundRules)
                .Service(new DirectionPace());
        BodyMotionSystem.Register(b);
        var world = b.Build();
        var e = world.Create();
        world.Set(e, Body.At(feet, yaw) with { State = BodyState.Ground });
        world.Set(e, new Traversal());
        world.Set(e, CharacterAnimation.For(assets, kind));
        world.Set(e, MotionInput.Standing);
        return (world, e);
    }

    /// <summary>One tick with the body at <paramref name="feet"/> facing <paramref name="yaw"/>, moving at <paramref name="velocity"/>, on ground or not, making <paramref name="move"/>.</summary>
    public static void Step(World world, EntityId e, Vector3 feet, float yaw, bool grounded, in Traversal move = default, Vector3 velocity = default, int crouched = 0)
    {
        ref var body = ref world.Get<Body>(e);
        body.Feet = feet;
        body.Yaw = yaw;
        body.Velocity = velocity;
        body.State = grounded ? BodyState.Ground : BodyState.Air;
        body.Crouched = crouched;
        world.Get<Traversal>(e) = move;
        world.Step();
    }

    /// <summary>A bone's joint in the world, as drawn.</summary>
    public static Vector3 Bone(World world, EntityId e, CharacterAssets assets, int bone)
    {
        var body = world.Get<Body>(e);
        var place = assets.Kind(world.Get<CharacterAnimation>(e).Kind).Placement(body.Feet, body.Yaw);
        return Vector3.Transform(world.Service<CharacterPoses>().Bones(world, e)[bone].Translation, place);
    }

    /// <summary>A bone's joint in the world as the renderer draws it (with the player's lean, look, lead and joins).</summary>
    public static Vector3 DrawnBone(World world, EntityId e, CharacterAssets assets, int bone)
    {
        var body = world.Get<Body>(e);
        var place = assets.Kind(world.Get<CharacterAnimation>(e).Kind).Placement(body.Feet, body.Yaw);
        return Vector3.Transform(world.Service<CharacterPoses>().DrawnBones(world, e)[bone].Translation, place);
    }

    /// <summary>Whether every bone and skin matrix of the pose is a number.</summary>
    public static bool Finite(World world, EntityId e)
    {
        var poses = world.Service<CharacterPoses>();
        foreach (var m in poses.Skin(world, e)) if (!Finite(m)) return false;
        foreach (var m in poses.Bones(world, e)) if (!Finite(m)) return false;
        return true;

        static bool Finite(in Matrix4x4 m) =>
            float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M13) && float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M23)
            && float.IsFinite(m.M31) && float.IsFinite(m.M32) && float.IsFinite(m.M33) && float.IsFinite(m.M41) && float.IsFinite(m.M42) && float.IsFinite(m.M43);
    }
}
