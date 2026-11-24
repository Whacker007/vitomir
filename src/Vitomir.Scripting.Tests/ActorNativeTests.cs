// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// The CodexActor natives, driven the way the game drives them: the real classes, compiled from
/// the player's own install, against a real location's cast.
/// </summary>
[Collection("scripts")]
public class ActorNativeTests(Scripts s)
{
    /// <summary>
    /// A location with its scripts running. The install stays open for the life of the test: a
    /// character's animations are read when a script asks for one, not when the level loads, which
    /// is the same reason the game holds its archives open for the whole session.
    /// </summary>
    static (CodexScenes Scenes, Location Where, GameFiles Files) Enter(Scripts s, string level, bool player = false,
                                                                       bool minds = false, string? scene = null)
    {
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel(level);
        // See WalkTests.Load: only Prague.nmf names a PLAYER, and the rest carry an empty one.
        var named = where?.Chronicle.Meta?.Player;
        var location = Location.Load(files, level, sky: false, scene: scene ?? where?.Entry?.SceneName,
                                     player: player ? (string.IsNullOrEmpty(named) ? "Christof" : named) : null);
        if (minds) location.Awaken(new AiClasses(files));
        var scenes = new CodexScenes(s.Runtime!) { Chronicles = set };
        scenes.Enter(location);
        return (scenes, location, files);
    }

    /// <summary>Calls a native the way the bridge does, with an optional <c>self</c>.</summary>
    static object? Call(Scripts s, string key, object? self, params object?[] args) =>
        s.Engine.get(new object[] { key, self!, args.Select(Wrap).ToArray() });

    static object? Wrap(object? v) => v switch
    {
        int i => java.lang.Integer.valueOf(i),
        float f => java.lang.Float.valueOf(f),
        bool b => java.lang.Boolean.valueOf(b),
        _ => v,
    };

    static int Int(object? o) => ((java.lang.Integer)o!).intValue();
    static float Flt(object? o) => ((java.lang.Float)o!).floatValue();
    static bool Bln(object? o) => ((java.lang.Boolean)o!).booleanValue();
    static string Str(object? o) => o?.ToString() ?? "";

    [Fact]
    public void A_named_character_is_found_by_the_name_the_scripts_know()
    {
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_OTDY");
        var named = where.Things.First(t => t.Actor is { CastId.Length: > 0 });
        Assert.Equal(named.Guid, Int(Call(s, "CodexThing.GuidFromCastID", null, named.Actor!.CastId)));
        // Case does not matter, and a name nobody carries is 0 — which is "no thing" to a script.
        Assert.Equal(named.Guid, Int(Call(s, "CodexThing.GuidFromCastID", null, named.Actor.CastId.ToUpperInvariant())));
        Assert.Equal(0, Int(Call(s, "CodexThing.GuidFromCastID", null, "NobodyOfThatName")));
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void Only_the_cast_answers_to_IsActorGuid()
    {
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_OTDY");
        var actor = where.Things.First(t => t.IsActor);
        var prop = where.Things.First(t => t.Type == ThingType.Prop);
        Assert.True(Bln(Call(s, "Codex.IsActorGuid", null, actor.Guid)));
        Assert.False(Bln(Call(s, "Codex.IsActorGuid", null, prop.Guid)));
        Assert.False(Bln(Call(s, "Codex.IsActorGuid", null, 0)));            // 0 is "no thing"
        Assert.False(Bln(Call(s, "Codex.IsActorGuid", null, 999_999)));
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void A_weapon_override_puts_claws_in_the_hand_and_the_cancel_gives_it_back()
    {
        // `OverrideActorWeapon(template, removeShield, removeHands)` and
        // `CancelOverrideActorWeapon()` — what Feral Claws calls with
        // `true, true`. A template that is not a weapon is refused.
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_OTDY", player: true);
        var christof = where.Player!.Thing;
        var self = s.Runtime!.NewThingRef("CodexActor", christof.Guid);
        var own = christof.Actor!.Weapon;

        Assert.False(Bln(Call(s, "CodexActor.OverrideActorWeapon", self, "christof", true, true)));
        Assert.True(Bln(Call(s, "CodexActor.OverrideActorWeapon", self, "Claw", true, true)));
        Assert.Equal("Claw", christof.Actor.Weapon!.Name, ignoreCase: true);
        Assert.True(christof.Actor.Has2(Actor.Flag2.WeaponOverride));
        Assert.True(Bln(Call(s, "CodexActor.CancelOverrideActorWeapon", self)));
        Assert.Same(own, christof.Actor.Weapon);
        Assert.False(christof.Actor.Has2(Actor.Flag2.WeaponOverride));
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void The_flag_verbs_answer_with_the_flags_as_they_were()
    {
        s.Ready();
        const int AiPaused = 0x4, Neutral = 0x40000;                          // THING_AF_*
        var (scenes, where, files) = Enter(s, "P1_OTDY");
        var thing = where.Things.First(t => t.IsActor);
        var actor = s.Runtime!.NewThingRef("CodexActor", thing.Guid);
        uint before = thing.Actor!.Flags;

        Assert.Equal((int)before, Int(Call(s, "CodexActor.SetActorFlags", actor, AiPaused)));
        Assert.True(thing.Actor.Has(Actor.Flag.AiPaused));
        // Setting adds rather than replaces, which is what lets two scripts flag the same character.
        Assert.Equal((int)(before | AiPaused), Int(Call(s, "CodexActor.SetActorFlags", actor, Neutral)));
        Assert.True(thing.Actor.Has(Actor.Flag.Neutral));
        Assert.Equal((int)(before | AiPaused | Neutral), Int(Call(s, "CodexActor.GetActorFlags", actor)));
        Assert.Equal((int)(before | AiPaused | Neutral), Int(Call(s, "CodexActor.ClearActorFlags", actor, AiPaused)));
        Assert.False(thing.Actor.Has(Actor.Flag.AiPaused));
        Assert.True(thing.Actor.Has(Actor.Flag.Neutral));
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void Setting_something_on_a_thing_that_is_not_a_character_answers_minus_one()
    {
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_OTDY");
        var prop = s.Runtime!.NewThingRef("CodexActor", where.Things.First(t => t.Type == ThingType.Prop).Guid);
        Assert.Equal(-1, Int(Call(s, "CodexActor.SetActorFlags", prop, 0x4)));
        Assert.Equal(-1f, Flt(Call(s, "CodexActor.SetActorHealth", prop, 10f)));
        Assert.Equal(-1, Int(Call(s, "CodexActor.GetActorTeam", prop)));
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void A_character_s_own_numbers_read_and_write()
    {
        s.Ready();
        const int Strength = 0, Fire = 4;                          // ACTOR_STAT_STRENGTH, DAMAGE_TYPE_FIRE
        var (scenes, where, files) = Enter(s, "P1_OTDY");
        var thing = where.Things.First(t => t.Actor is { Health: > 0 });
        var actor = s.Runtime!.NewThingRef("CodexActor", thing.Guid);
        float health = thing.Actor!.Health;

        Assert.Equal(health, Flt(Call(s, "CodexActor.GetActorHealth", actor)));
        Assert.Equal(health, Flt(Call(s, "CodexActor.SetActorHealth", actor, 1f)));
        Assert.Equal(1f, thing.Actor.Health);
        // Healing stops at the maximum however much is poured in.
        Assert.True(Bln(Call(s, "CodexActor.HealActor", actor, 1e6f)));
        Assert.Equal(thing.Actor.MaxHealth, thing.Actor.Health);

        float strength = thing.Actor.Stat[Strength];
        Assert.Equal(strength, Flt(Call(s, "CodexActor.GetActorStat", actor, Strength)));
        Assert.Equal(strength, Flt(Call(s, "CodexActor.SetActorStat", actor, Strength, 99f)));
        Assert.Equal(99f, Flt(Call(s, "CodexActor.GetActorStat", actor, Strength)));
        // The base is what the template gave it, and a discipline moving the stat leaves it alone.
        Assert.Equal(strength, Flt(Call(s, "CodexActor.GetActorBaseStat", actor, Strength)));
        Assert.Equal(thing.Actor.Soak[Fire], Flt(Call(s, "CodexActor.GetActorSoak", actor, Fire)));
        // An index nobody has reads as zero rather than throwing.
        Assert.Equal(0f, Flt(Call(s, "CodexActor.GetActorStat", actor, 999)));
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void A_script_can_make_a_character_do_something()
    {
        s.Ready();
        const int Stand = 33, Talk = 117;                          // MOTION_STAND, MOTION_TALK
        var (scenes, where, files) = Enter(s, "P1_OTDY");
        // One of the townsfolk whose motion set can talk, so the action channel has something to play.
        var thing = where.Things.First(t => t.IsActor && t.Animation?.Set.Mode(Talk) is not null);
        var actor = s.Runtime!.NewThingRef("CodexActor", thing.Guid);
        Assert.Equal("stand", thing.Animation!.Mode!.Name);

        // The action channel plays over whatever the character is doing with itself, and the
        // native answers with how long it runs in milliseconds.
        int ms = Int(Call(s, "CodexActor.PlayActorMotionSetMode", actor, Talk, false, 1f));
        Assert.True(ms > 0, $"a talk animation of no length ({ms} ms)");
        Assert.Equal("talk", thing.Animation.Mode!.Name);
        Assert.Equal("stand", thing.Animation.Motion!.Mode.Name);  // still standing underneath
        Call(s, "CodexActor.StopActorAction", actor);
        Assert.Equal("stand", thing.Animation.Mode!.Name);

        // On the motion channel it replaces the idle outright.
        Assert.True(Int(Call(s, "CodexActor.PlayActorMotionSetMode", actor, Stand, true, 1f)) > 0);
        Assert.Equal("stand", thing.Animation.Motion!.Mode.Name);
        // A mode the character's set has not got is -1, and changes nothing.
        Assert.Equal(-1, Int(Call(s, "CodexActor.PlayActorMotionSetMode", actor, 250, true, 1f)));
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void A_shipped_script_pauses_its_actor_s_AI_on_the_way_in()
    {
        s.Ready();
        // ActorAIOff_TriggerOn is the fifth most used class in the game: it takes a CodexActor as
        // its first CODEXPARAM and sets THING_AF_AIPAUSED on it in beginscene. Nothing about this
        // test is ours — the class, the parameter and the flag are all the game's.
        var (scenes, where, files) = Enter(s, "P1_MON1");
        var paused = where.Things.Where(t => t.Actor is { } a && a.Has(Actor.Flag.AiPaused)).ToList();
        Assert.NotEmpty(paused);
        Assert.Contains(scenes.Running, n => n == "ActorAIOff_TriggerOn");
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void A_thing_can_be_given_keyframes_it_was_not_authored_with()
    {
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_OTDY");
        var thing = where.Things.First(t => t.Keyframes.Length == 0 && t.Type == ThingType.Prop);
        var self = s.Runtime!.NewThingRef("CodexThing", thing.Guid);
        Assert.Equal(0, Int(Call(s, "CodexThing.GetNumFrames", self)));

        Assert.True(Bln(Call(s, "CodexThing.AllocateFrames", self, 3)));
        Assert.Equal(3, Int(Call(s, "CodexThing.GetNumFrames", self)));
        // Every new frame sits where the thing is, which is what the SDK says AllocateFrames does.
        var here = (float[])Call(s, "CodexThing.GetFramePosition", self, 0)!;
        Assert.Equal(thing.Position.X, here[0]);

        float[] there = [thing.Position.X, thing.Position.Y, thing.Position.Z + 128f];
        Assert.True(Bln(Call(s, "CodexThing.SetFramePosition", self, 1, there)));
        Assert.Equal(there[2], ((float[])Call(s, "CodexThing.GetFramePosition", self, 1)!)[2]);
        Assert.False(Bln(Call(s, "CodexThing.SetFramePosition", self, 9, there)));   // no such frame

        // **Frames are not permission to move.** AllocateFrames sets an internal bit on
        // the thing's flags and touches nothing else, so this prop is still THING_MOVE_NONE
        // and the track move refuses it outright. The script has to say so itself.
        Assert.False(Bln(Call(s, "CodexThing.MoveToFrame", self, 1, 100f)));
        Assert.Equal((int)WorldThing.MoveNone, Int(Call(s, "CodexThing.GetMoveType", self)));
        Assert.Equal((int)WorldThing.MoveNone,
                     Int(Call(s, "CodexThing.SetMoveType", self, (int)WorldThing.MoveTrack)));
        Assert.True(Bln(Call(s, "CodexThing.MoveToFrame", self, 1, 100f)));

        Assert.True(Bln(Call(s, "CodexThing.ClearFrames", self)));
        Assert.Equal(0, Int(Call(s, "CodexThing.GetNumFrames", self)));
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void AllocateFrames_takes_two_to_five_hundred_and_twelve_and_only_once()
    {
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_OTDY");
        var spare = where.Things.Where(t => t.Keyframes.Length == 0 && t.Type == ThingType.Prop).ToArray();
        var self = s.Runtime!.NewThingRef("CodexThing", spare[0].Guid);

        // Fewer than 2 or more than 512 fail. One frame is not a track.
        Assert.False(Bln(Call(s, "CodexThing.AllocateFrames", self, 0)));
        Assert.False(Bln(Call(s, "CodexThing.AllocateFrames", self, 1)));
        Assert.False(Bln(Call(s, "CodexThing.AllocateFrames", self, 513)));
        Assert.Empty(spare[0].Keyframes);

        Assert.True(Bln(Call(s, "CodexThing.AllocateFrames", self, 512)));
        // And it refuses a thing that already has frames, so
        // there is no resizing — ClearFrames first or not at all.
        Assert.False(Bln(Call(s, "CodexThing.AllocateFrames", self, 4)));
        Assert.Equal(512, spare[0].Keyframes.Length);
        Assert.True(Bln(Call(s, "CodexThing.ClearFrames", self)));
        Assert.True(Bln(Call(s, "CodexThing.AllocateFrames", self, 4)));

        // A door the level builder authored has frames of its own, and is refused for that reason.
        if (where.Things.FirstOrDefault(t => t.Keyframes.Length > 0) is { } authored)
            Assert.False(Bln(Call(s, "CodexThing.AllocateFrames",
                                  s.Runtime.NewThingRef("CodexThing", authored.Guid), 2)));
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void SetMoveType_answers_with_the_kind_it_replaced_and_refuses_a_fifth()
    {
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_OTDY");
        var thing = where.Things.First(t => t.Type == ThingType.Prop);
        var self = s.Runtime!.NewThingRef("CodexThing", thing.Guid);
        int was = Int(Call(s, "CodexThing.GetMoveType", self));

        Assert.Equal(was, Int(Call(s, "CodexThing.SetMoveType", self, (int)WorldThing.MovePhysics)));
        Assert.Equal((int)WorldThing.MovePhysics, Int(Call(s, "CodexThing.GetMoveType", self)));
        // Outside 0..3 the write is not made **and** the answer is −1 rather than the old kind:
        // the one Codex setter whose refusal is distinguishable from its success.
        Assert.Equal(-1, Int(Call(s, "CodexThing.SetMoveType", self, 4)));
        Assert.Equal(-1, Int(Call(s, "CodexThing.SetMoveType", self, -1)));
        Assert.Equal((int)WorldThing.MovePhysics, Int(Call(s, "CodexThing.GetMoveType", self)));
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void Stop_means_something_different_to_each_kind_of_mover()
    {
        // The monastery rather than Old Town: Old Town's ten track things are all trackers with no
        // authored frames, and this test needs something with a route between two of them.
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_MON1", player: true);

        // A track thing halts where it is, and never reaches the frame it was going to.
        var door = where.Things.First(t => t.MoveType == WorldThing.MoveTrack && t.Keyframes.Length > 1);
        var doorSelf = s.Runtime!.NewThingRef("CodexThing", door.Guid);
        Assert.True(Bln(Call(s, "CodexThing.MoveToFrame", doorSelf, 1, 20f)));
        door.Advance(0.05f);
        var caught = door.Position;
        Assert.True(Bln(Call(s, "CodexThing.Stop", doorSelf)));
        Assert.Null(door.Motion);
        Assert.Equal(-1, door.Advance(10f));                    // no `arrived`, ever
        Assert.Equal(caught, door.Position);                    // and it stayed where it stopped

        // A physics thing loses its velocity, and nothing else: the linear velocity is zeroed
        // alone, so the rotational velocity beside it is left turning. **No placed thing in any
        // shipped level is MOVETYPE 3** — the physics things in this game are all spawned, gore and
        // projectiles — so a script that wants one makes it, which is what SetMoveType is for.
        var rock = where.Things.First(t => t.Type == ThingType.Prop && t.Keyframes.Length == 0);
        var rockSelf = s.Runtime.NewThingRef("CodexThing", rock.Guid);
        Call(s, "CodexThing.SetMoveType", rockSelf, (int)WorldThing.MovePhysics);
        rock.Velocity = new Vector3(10f, 20f, 30f);
        rock.RotVelocity = new Vector3(1f, 2f, 3f);
        Assert.True(Bln(Call(s, "CodexThing.Stop", rockSelf)));
        Assert.Equal(Vector3.Zero, rock.Velocity);
        Assert.Equal(new Vector3(1f, 2f, 3f), rock.RotVelocity);

        // And a thing that moves in no way at all is answered `true` having been left alone, which
        // is the arm of the switch that falls through.
        var still = where.Things.First(t => t.MoveType == WorldThing.MoveNone);
        var pose = (still.Position, still.Orientation);
        Assert.True(Bln(Call(s, "CodexThing.Stop", s.Runtime.NewThingRef("CodexThing", still.Guid))));
        Assert.Equal(pose, (still.Position, still.Orientation));
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void Stop_calls_off_a_character_s_walk()
    {
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_OTDY", player: true);
        var walker = where.Player!;
        var thing = walker.Thing;
        Assert.Equal(WorldThing.MoveWalker, thing.MoveType);

        var far = Somewhere(where, walker, 600f);
        Assert.True(walker.WalkTo(far.Point, far.Node), "nowhere to walk to");
        Assert.True(walker.Walking);

        Assert.True(Bln(Call(s, "CodexThing.Stop", s.Runtime!.NewThingRef("CodexThing", thing.Guid))));
        Assert.False(walker.Walking);
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void SendActorToPos_is_a_walk_order_and_refuses_what_cannot_take_one()
    {
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_OTDY", player: true, minds: true);
        var walker = where.Player!;
        var thing = walker.Thing;
        var self = s.Runtime!.NewThingRef("CodexActor", thing.Guid);
        var far = Somewhere(where, walker, 600f);
        float[] to = [far.Point.X, far.Point.Y, far.Point.Z];

        Assert.True(Bln(Call(s, "CodexActor.SendActorToPos", self, to, 200f)));
        Assert.True(walker.Walking);
        Assert.Equal(200f, Paced(walker));

        // **Zero means 150**, the engine's own default walk speed written in as a literal
        // — not "don't move".
        Assert.True(Bln(Call(s, "CodexActor.SendActorToPos", self, to, 0f)));
        Assert.Equal(Player.DefaultWalkSpeed, Paced(walker));
        // And the speed is taken as an absolute value, so a negative one is an order too.
        Assert.True(Bln(Call(s, "CodexActor.SendActorToPos", self, to, -300f)));
        Assert.Equal(300f, Paced(walker));

        // Nowhere to stand: the point has to be over walkable ground.
        float[] sky = [far.Point.X, far.Point.Y, far.Point.Z + 100_000f];
        Assert.False(Bln(Call(s, "CodexActor.SendActorToPos", self, sky, 200f)));

        // A prop is not a character, and the type test is `0 || 7` with nothing else allowed.
        var prop = where.Things.First(t => t.Type == ThingType.Prop);
        Assert.False(Bln(Call(s, "CodexActor.SendActorToPos",
                              s.Runtime.NewThingRef("CodexActor", prop.Guid), to, 200f)));

        // And a corpse does not walk: its health has to be above zero.
        var other = where.Things.First(t => t.Actor is { } a && !ReferenceEquals(t, thing) && a.Health > 0f
                                            && t.Mind is not null);
        var dead = s.Runtime.NewThingRef("CodexActor", other.Guid);
        other.Actor!.Health = 0f;
        Assert.False(Bln(Call(s, "CodexActor.SendActorToPos", dead, to, 200f)));
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void The_monastery_s_own_scripts_start_people_walking_the_moment_the_scene_opens()
    {
        // Where this came from: a play-test's log of this level had eight
        // `script called CodexActor.SendActorToPos, which the engine does not implement yet` lines
        // at scene start, and nobody in the monastery wandered anywhere. The callers are the
        // generic behaviour classes — `WalkCycle` is the second most placed class in the game, 90
        // things — so this one native is most of what makes a level look inhabited.
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_MON2", player: true, minds: true);

        var walking = where.Things
            .Where(t => t.Mind is { } m && m.Walker.Walking && !ReferenceEquals(t, where.Player?.Thing))
            .ToArray();
        Assert.NotEmpty(walking);
        // And they were told where to go, not merely left with a stale route: each has somewhere
        // to be that is not where it is standing.
        Assert.All(walking, t => Assert.NotEqual(t.Position, t.Mind!.Walker.Destination));
        scenes.Leave();
        files.Dispose();
    }

    /// <summary>
    /// `arrived` for a walker was missing once, so every patrol in
    /// the game walked to its keyframe 1 and stood there for good. The script's own `arrived` is
    /// what sends it on to the next frame — and a round whose last keyframe is a real leg away from
    /// its first goes round for ever. 113 of the game's 143 patrollers are built that way, the
    /// Knights of St John in an Old Town save among them.
    /// </summary>
    [Theory]
    [InlineData("P1_SIM1", null, "WalkCycle")]                 // Prague, beneath the city
    [InlineData("P1_OLDT", "OLDT_7_7", "WalkCycleLawmen")]     // the knights on their rounds in Old Town
    public void A_walk_that_arrives_tells_the_script_so_a_round_goes_round(string level, string? scene, string script)
    {
        s.Ready();
        var (scenes, where, files) = Enter(s, level, player: true, minds: true, scene: scene);
        var sim = new Simulation(where) { Scripts = scenes };
        var cyclers = where.Things.Where(t => t.Script == script && t.Mind is not null).ToArray();
        Assert.NotEmpty(cyclers);
        var arrivals = cyclers.ToDictionary(t => t.Guid, _ => 0);
        where.Arrived += (t, _) => { if (arrivals.ContainsKey(t.Guid)) arrivals[t.Guid]++; };
        for (int i = 0; i < 240 * 30; i++) sim.Step(1f / 30f);
        // More arrivals than keyframes: round the whole loop and on again.
        Assert.All(cyclers, t => Assert.True(arrivals[t.Guid] > t.Keyframes.Length,
            $"{t.Template} {t.Guid} arrived {arrivals[t.Guid]} times on a round of {t.Keyframes.Length}"));
        scenes.Leave();
        files.Dispose();
    }

    /// <summary>
    /// **London's police go round — a repair** (`Repairs.ClosedRound`). Their rounds end on the
    /// keyframe they start on, so after one lap the script's next order is a walk of no length,
    /// which 1.1's walker refuses without an `arrived` (under a tenth of a unit) — and
    /// nothing asks again, so 1.1's bobbies stop for good on their first keyframe.
    /// In play the police in London do not keep patrolling; and on the question of
    /// keeping it, guards and police should patrol instead of just standing there.
    ///
    /// The third bobbie is a different defect and is **not** repaired: his third keyframe is 240
    /// units under the street, where `SendActorToPos` finds no ground (the ground lookup looks down, never
    /// up), so he walks one leg and stands — as 1.1's does.
    /// </summary>
    [Fact]
    public void London_s_police_go_round_again_after_the_first_lap()
    {
        s.Ready();
        var (scenes, where, files) = Enter(s, "L1_WEST", player: true, minds: true);
        var sim = new Simulation(where) { Scripts = scenes };
        var bobbies = where.Things.Where(t => t.Script == "WalkCycleLawmen").OrderBy(t => t.Guid).ToArray();
        Assert.Equal(3, bobbies.Length);
        var arrivals = bobbies.ToDictionary(t => t.Guid, _ => 0);
        where.Arrived += (t, _) => { if (arrivals.ContainsKey(t.Guid)) arrivals[t.Guid]++; };
        for (int i = 0; i < 240 * 30; i++) sim.Step(1f / 30f);

        // More arrivals than keyframes: past the end of the first lap and on round again. The lap
        // itself is 4 and 7 arrivals; 1.1 would stop there.
        foreach (var b in bobbies.Take(2))
            Assert.True(arrivals[b.Guid] > b.Keyframes.Length,
                        $"bobbie {b.Guid} arrived {arrivals[b.Guid]} times on a round of {b.Keyframes.Length}");
        var third = bobbies[2];
        Assert.Equal(1, arrivals[third.Guid]);
        Assert.True(Vector3.Distance(third.Keyframes[1].Position, third.Position) < 1f);
        scenes.Leave();
        files.Dispose();
    }

    /// <summary>
    /// The repair's own rule, on its own: a script sending a character to where it already stands
    /// is answered true, and the script hears `arrived` on the next step — no walk, no clamp.
    /// </summary>
    [Fact]
    public void A_walk_to_where_you_stand_counts_as_arriving()
    {
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_OTDY", player: true, minds: true);
        var walker = where.Player!;
        var thing = walker.Thing;
        var self = s.Runtime!.NewThingRef("CodexActor", thing.Guid);
        int heard = 0;
        where.Arrived += (t, _) => { if (ReferenceEquals(t, thing)) heard++; };
        float[] here = [thing.Position.X, thing.Position.Y, thing.Position.Z];

        Assert.True(Bln(Call(s, "CodexActor.SendActorToPos", self, here, 90f)));
        Assert.False(walker.Walking);
        Assert.Equal(0, heard);                   // not inside the order: the next step
        scenes.Step(1f / 30f);
        Assert.Equal(1, heard);
        scenes.Step(1f / 30f);
        Assert.Equal(1, heard);                   // once
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void A_door_the_player_can_click_swings_when_its_script_hears_the_click()
    {
        // In play the doors had never worked. Two of the monastery's doors are
        // `door_raise1_nhl`, which carry BLOCKSELECT and are opened by a switch — clicking those is
        // meant to do nothing. This is the other kind: a plain `dungeon_door` with a
        // `PivotDoor` script, which is supposed to swing when you click it, and this is the test
        // that says whether the whole path works end to end — pick, verb, order, `clicked`, and the
        // door's own keyframes.
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_MON1", player: true, minds: true);
        var door = where.Things.First(t => t.Script == "PivotDoor" && t.MoveType == WorldThing.MoveTrack);
        var pose = (door.Position, door.Orientation);
        Assert.Null(door.Motion);

        Assert.True(scenes.Clicked(door, where.Player!.Thing), "PivotDoor did not hear the click");
        Assert.NotNull(door.Motion);

        // And it goes somewhere: a swing about its hinge, which `Advance` walks.
        door.Advance(0.2f);
        Assert.NotEqual(pose, (door.Position, door.Orientation));
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void Pulling_a_lever_raises_the_door_across_the_room()
    {
        // The thing the monastery could not do in play, and the reason is not the lever: **both of
        // P1_MON1's `small_lever1`s have no script at all**. The script that answers for them is
        // the `DoorSwitchPivot` sitting on the *door*, which captured the lever in its own
        // beginscene — so the click has to travel down the capture list (the message's
        // second loop) to reach it. Vitomir only ran the first loop, so
        // clicking a lever in this game did nothing whatsoever.
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_MON1", player: true, minds: true);

        var levers = where.Things.Where(t => t.Template.Contains("lever", StringComparison.OrdinalIgnoreCase)
                                             && t.Script.Length == 0).ToArray();
        Assert.NotEmpty(levers);
        var held = levers.Where(t => t.Captures.Count > 0).ToArray();
        Assert.NotEmpty(held);

        var doors = where.Things.Where(t => t.Script == "DoorSwitchPivot").ToArray();
        Assert.NotEmpty(doors);
        var before = doors.Select(d => (d.Position, d.Orientation)).ToArray();

        // Pull one. Nothing on the lever hears it; the door's script does.
        Assert.True(scenes.Clicked(held[0], where.Player!.Thing), "nobody heard the lever");
        for (int i = 0; i < 20; i++) scenes.Step(0.05f);
        // The same through the **order** rather than the seam, because that is the path a click
        // takes and the one play showed as dead. It was not: the door it opens is in another
        // room, and the console said "no script to answer" whatever happened.
        var second = where.Things.First(t => t.Template.Contains("lever", StringComparison.OrdinalIgnoreCase)
                                             && t.Captures.Count > 0 && !ReferenceEquals(t, held[0]));
        var other = where.Things.Where(d => d.Script == "DoorSwitchPivot").ToArray();
        var poses = other.Select(d => (d.Position, d.Orientation)).ToArray();
        where.Player.Thing.Position = second.Position;
        where.Player.Ground();
        Assert.Equal(Vitomir.Engine.Rpg.OrderResult.Started,
                     Vitomir.Engine.Rpg.Actions.Use(where.Player.Thing, second, 0, where.Motions));
        for (int i = 0; i < 40; i++) scenes.Step(0.05f);
        Assert.Contains(other.Select((d, i) => (Door: d, Was: poses[i])),
                        pair => (pair.Door.Position, pair.Door.Orientation) != pair.Was);
        Assert.Contains(doors.Select((d, i) => (Door: d, Was: before[i])),
                        p => p.Door.Motion is not null || (p.Door.Position, p.Door.Orientation) != p.Was);
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void Opening_a_container_is_a_death_and_only_happens_once()
    {
        // The use order sends THING_TYPE_CONTAINER somewhere else entirely, and what is
        // at the other end plays MOTION_DEATHQUICK and calls `killed`. A play-test clicked a barrel seven
        // times in the monastery and nothing happened, because the use order was
        // sending it down the `clicked` path, where a barrel has nothing to say.
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_MON1", player: true, minds: true);
        var barrel = where.Things.First(t => t.Type == ThingType.Container);
        var who = where.Player!.Thing;

        Assert.Equal(0u, barrel.State & Vitomir.Engine.Rpg.Container.Opened);
        Assert.False(barrel.Has(WorldThing.Flag.NoHighlight));

        Assert.True(Vitomir.Engine.Rpg.Container.Open(barrel, who, where.Motions));
        // Spent, and it stops asking to be looted again.
        Assert.NotEqual(0u, barrel.State & Vitomir.Engine.Rpg.Container.Opened);
        Assert.True(barrel.Has(WorldThing.Flag.NoHighlight));
        // **And it cannot be opened twice** — that is the open's first test, so there is no
        // re-looting anything in this game.
        Assert.False(Vitomir.Engine.Rpg.Container.Open(barrel, who, where.Motions));

        // The seam the engine reaches is `killed`, not `clicked`, and it is wired.
        Assert.NotNull(where.Killed);
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void A_use_order_on_a_container_goes_to_the_open_rather_than_to_clicked()
    {
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_MON1", player: true, minds: true);
        var barrel = where.Things.First(t => t.Type == ThingType.Container);
        var who = where.Player!.Thing;
        // Standing on it, so the order arrives on its first tick rather than after a walk.
        who.Position = barrel.Position;
        where.Player.Ground();

        Assert.Equal(Vitomir.Engine.Rpg.OrderResult.Started,
                     Vitomir.Engine.Rpg.Actions.Use(who, barrel, 0, where.Motions));
        Assert.NotEqual(0u, barrel.State & Vitomir.Engine.Rpg.Container.Opened);
        scenes.Leave();
        files.Dispose();
    }

    /// <summary>What the walker was told to move at, which is what `Follow` puts into `Speed`.</summary>
    static float Paced(Player walker)
    {
        walker.Follow(0.001f);
        return walker.Speed;
    }

    /// <summary>
    /// A walkable spot at least <paramref name="away"/> from where the walker is standing and with
    /// a route to it — checked by ordering the walk and calling it off again, because a point on
    /// real ground that nothing can reach would make these tests about pathfinding instead.
    /// </summary>
    static (Vector3 Point, int Node) Somewhere(Location where, Player walker, float away)
    {
        var walk = where.Walk!;
        for (int i = 0; i < walk.Nodes.Length; i++)
        {
            var node = walk.Nodes[i];
            if (node.Outline.Length == 0) continue;
            var mid = Vector2.Zero;
            foreach (var v in node.Outline) mid += v;
            mid /= node.Outline.Length;
            if (!node.Contains(mid)) mid = Vector2.Lerp(mid, node.Outline[0], 0.5f);
            var p = new Vector3(mid, node.Height(mid.X, mid.Y));
            if (Vector3.Distance(p, walker.Position) < away) continue;
            if (!walker.WalkTo(p, i)) continue;
            walker.Stop();
            return (p, i);
        }
        throw new InvalidOperationException($"nowhere reachable {away} from {walker.Position}");
    }

    [Fact]
    public void The_scripts_are_told_who_the_player_is()
    {
        s.Ready();
        // Every console line, camera shot and fade is addressed to a client, and the scripts get
        // that number from GetCurrentPlayer — 19 calls across the shipped ones.
        var (scenes, where, files) = Enter(s, "P1_OTDY", player: true);
        Assert.NotNull(where.Player);
        Assert.Equal(where.Player!.Thing.Guid, Int(Call(s, "CodexPlayer.GetCurrentPlayer", null)));
        Assert.Equal(where.Player.Thing.Guid, Int(Call(s, "CodexPlayer.GetPartyPlayer", null, 0)));
        Assert.Equal(0, Int(Call(s, "CodexPlayer.GetPartyPlayer", null, 1)));   // a party of one
        Assert.Equal(1, Int(Call(s, "CodexPlayer.GetNumPartyPlayers", null)));
        Assert.True(Bln(Call(s, "Codex.IsActorGuid", null, where.Player.Thing.Guid)));
        // And the player is a character like any other: named, clanned, with a motion set running.
        Assert.Equal("Christof", where.Player.Thing.Actor!.CastId);
        Assert.Equal("Christof", Str(Call(s, "CodexPlayer.GetPlayerName",
                                          s.Runtime!.NewThingRef("CodexPlayer", where.Player.Thing.Guid))));
        scenes.Leave();
        files.Dispose();
    }

    /// <summary>
    /// **The scripts' "current player" is whoever is steered**, and the party
    /// natives read the roster. With Wilhem taken over
    /// in the Monastery and talking to people, as in 1.1, a region answering only `causeGUID ==
    /// GetCurrentPlayer()` and an enemy told `AISetTarget(GetCurrentPlayer())` follow the one steered.
    /// `SetCurrentPlayer` is the party's own take-over, which three shipped scenes use to hand the
    /// controls back to Christof.
    /// </summary>
    [Fact]
    public void The_current_player_is_whoever_is_steered()
    {
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_OTDY", player: true);
        var christof = where.Player!.Thing;
        var wilhem = where.Place("Wilhem", christof.Position + new Vector3(96f, 0f, 0f), christof.Orientation, christof.Sector)!;
        wilhem.Actor!.Flags |= Actor.Flag.Party;
        christof.Actor!.Flags |= Actor.Flag.Party;
        where.Awaken(new AiClasses(files));

        Assert.Equal(2, Int(Call(s, "CodexPlayer.GetNumPartyPlayers", null)));
        Assert.Equal(wilhem.Guid, Int(Call(s, "CodexPlayer.GetPartyPlayer", null, 1)));
        Assert.Equal(0, Int(Call(s, "CodexPlayer.GetPartyPlayer", null, 2)));

        Assert.True(where.SteerSlot(1));
        Assert.Equal(wilhem.Guid, Int(Call(s, "CodexPlayer.GetCurrentPlayer", null)));

        Assert.True(Bln(Call(s, "CodexPlayer.SetCurrentPlayer", s.Runtime!.NewThingRef("CodexPlayer", christof.Guid))));
        Assert.Same(christof, where.Steered);
        Assert.Equal(christof.Guid, Int(Call(s, "CodexPlayer.GetCurrentPlayer", null)));
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void Without_a_player_the_scripts_still_get_a_stable_number()
    {
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_OTDY");
        Assert.Null(where.Player);
        Assert.Equal(scenes.PlayerGuid, Int(Call(s, "CodexPlayer.GetCurrentPlayer", null)));
        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void What_the_scripts_print_reaches_the_console()
    {
        s.Ready();
        var (scenes, _, files) = Enter(s, "P1_OTDY");
        int before = scenes.Lines.Count;
        Call(s, "CodexConsole.Print", null, 0, 0, "the sun is up", 0);
        Assert.Equal("the sun is up", scenes.Lines[^1]);
        Call(s, "CodexConsole.Print", null, 0, 0, "", 0);          // nothing to say, nothing kept
        Assert.Equal(before + 1, scenes.Lines.Count);
        Call(s, "CodexConsole.Clear", null, 0, 0);
        Assert.Empty(scenes.Lines);
        scenes.Leave();
        files.Dispose();
    }

    /// <summary>
    /// **The type verbs, which are how Christof stops being a vampire.** `darkAges.not` gives him
    /// `ACTOR_TYPE 0 VAMPIRE` and the first scene of the game, `CNVT_3_1`, calls
    /// `SetActorType(ACTOR_TYPE_HUMAN)` under the comment *"make him a human"* — the only
    /// `SetActorType` call in all 467 shipped scripts. So the contract matters: the setter **rejects**
    /// anything outside 0..3 and leaves the stored type alone, and both verbs answer -1 rather than
    /// 0 when the thing is not a character — 0 being VAMPIRE, which is a wrong answer and not an
    /// error code.
    /// </summary>
    [Fact]
    public void The_type_verbs_reject_a_type_that_is_not_one_and_never_answer_vampire_by_accident()
    {
        const int Vampire = 0, Ghoul = 1, Human = 2, Monster = 3;
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_OTDY");

        var someone = where.Things.First(t => t.Actor is not null);
        var actor = s.Runtime!.NewThingRef("CodexActor", someone.Guid);
        int was = Int(Call(s, "CodexActor.GetActorType", actor));

        // The setter answers the type it replaced, which is how a script puts one back.
        Assert.Equal(was, Int(Call(s, "CodexActor.SetActorType", actor, Human)));
        Assert.Equal(Human, Int(Call(s, "CodexActor.GetActorType", actor)));
        Assert.Equal(Human, Int(Call(s, "CodexActor.SetActorType", actor, Monster)));

        // Out of range is refused outright: -1 back, and MONSTER still stored.
        foreach (int bad in new[] { -1, 4, 99 })
            Assert.Equal(-1, Int(Call(s, "CodexActor.SetActorType", actor, bad)));
        Assert.Equal(Monster, Int(Call(s, "CodexActor.GetActorType", actor)));
        Call(s, "CodexActor.SetActorType", actor, was);

        // Not a character: -1 from both, never Vampire's 0.
        var prop = s.Runtime.NewThingRef("CodexActor", where.Things.First(t => t.Type == ThingType.Prop).Guid);
        Assert.Equal(-1, Int(Call(s, "CodexActor.GetActorType", prop)));
        Assert.Equal(-1, Int(Call(s, "CodexActor.SetActorType", prop, Ghoul)));
        Assert.NotEqual(Vampire, -1);                              // the point of the previous two

        scenes.Leave();
        files.Dispose();
    }

    [Fact]
    public void An_aura_is_answered_as_its_text_and_a_set_name_as_it_was_set()
    {
        // `GetActorAura` looks the numbered aura up in the string table —
        // ACTOR_AURA_CALM is "Calm", ACTOR_AURA_NONE "Unreadable" — which is what Aura Perception
        // prints; a name `SetActorAura` gave is answered as it was given (byte 0x22).
        s.Ready();
        var (scenes, where, files) = Enter(s, "P1_OTDY", player: true);
        var strings = new Vitomir.Engine.Ui.Strings(files);
        scenes.Nls = strings.Find;
        var christof = where.Player!.Thing;
        var self = s.Runtime!.NewThingRef("CodexActor", christof.Guid);
        christof.Actor!.AuraName = "";
        christof.Actor.Aura = 5;
        Assert.Equal("Calm", Str(Call(s, "CodexActor.GetActorAura", self)));
        christof.Actor.Aura = 0;
        Assert.Equal("Unreadable", Str(Call(s, "CodexActor.GetActorAura", self)));
        Call(s, "CodexActor.SetActorAura", self, "brooding");
        Assert.Equal("brooding", Str(Call(s, "CodexActor.GetActorAura", self)));
        // Every numbered aura has its text.
        for (uint i = 0; i < Actor.Auras.Length; i++)
        {
            christof.Actor.AuraName = "";
            christof.Actor.Aura = i;
            Assert.NotNull(strings.Find(CodexScenes.AuraKey(christof.Actor)));
        }
        scenes.Leave();
        files.Dispose();
    }
}
