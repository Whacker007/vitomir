// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Anim;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// How fast an animation plays, and which one a character's legs get. Every number here is the
/// engine's, from the mixer and the actor's own animation update.
///
/// </summary>
public class GaitTests
{
    static MotionSets Motions(GameFiles files, int seed = 1) => new(files, seed);

    [Fact]
    public void The_rate_is_frames_a_second_and_thirty_is_the_engines_own()
    {
        using var files = Install.Open();
        var motions = Motions(files);
        var walk = motions.Find(motions.Set("civ05YoungLady"), "walk");
        Assert.NotNull(walk);
        var player = new AnimationPlayer(walk!.Value.Animation, ThingAnimation.Rate);
        player.Advance(1f);
        Assert.Equal(30f, player.Frame, 3);              // curFrame += rate × seconds
        Assert.Equal(30f, ThingAnimation.Rate);
        // `Civ05YoungLady_walk.nad` is 89 frames, so her cycle takes just under three seconds —
        // which is the whole of the gait problem in one number.
        Assert.Equal(89f, walk.Value.Animation.Duration, 1);
        Assert.Equal(2_966, ThingAnimation.Milliseconds(walk.Value.Animation));
    }

    [Fact]
    public void A_rate_is_clamped_to_the_engines_own_range()
    {
        Assert.Equal(0f, ThingAnimation.ClampRate(-1f));
        Assert.Equal(0f, ThingAnimation.ClampRate(0f));
        Assert.Equal(30f, ThingAnimation.ClampRate(30f));
        Assert.Equal(300f, ThingAnimation.ClampRate(300f));
        Assert.Equal(300f, ThingAnimation.ClampRate(1_000f));
        Assert.Equal(300f, ThingAnimation.RateMax);
    }

    [Fact]
    public void A_variable_mode_plays_between_three_quarters_and_five_quarters_speed()
    {
        // The rate is rolled once, when the animation starts: rate × (1 + rand()/32767/2 − ¼).
        using var files = Install.Open();
        var motions = Motions(files);
        var variable = FindVariable(files, motions);
        Assert.NotNull(variable);
        var rates = new List<float>();
        for (int i = 0; i < 400; i++)
        {
            var animation = ThingAnimation.For(variable!.Value.Model, variable.Value.Set, motions,
                                               variable.Value.Mode)!;
            rates.Add(animation.Motion!.Player.Rate);
        }
        Assert.All(rates, r => Assert.InRange(r, 30f * 0.75f, 30f * 1.25f));
        Assert.True(rates.Distinct().Count() > 100, "the roll is not rolling");
        Assert.InRange(rates.Average(), 29f, 31f);
    }

    /// <summary>A model, set and mode in the player's install whose mode carries `variable`.</summary>
    static (NodModel Model, string Set, string Mode)? FindVariable(GameFiles files, MotionSets motions)
    {
        foreach (var name in new[] { "alligator", "Szlachta", "rat", "viper", "WarGhoul" })
        {
            var set = motions.Set(name);
            if (set?.Modes.FirstOrDefault(m => m.Has(NmsFlags.Variable)) is not { } mode) continue;
            if (motions.Find(set, mode.Name) is null) continue;
            var model = NodModel.Read(files.Read("3d/models/civ04Merchant2.nod"), "civ04Merchant2.nod");
            return (model, name, mode.Name);
        }
        return null;
    }

    [Fact]
    public void Asking_the_motion_channel_for_what_it_is_already_playing_is_not_a_restart()
    {
        // The same track again only refreshes the flags. It is what lets the actor's
        // update ask for `walk` on every frame of a walk without freezing the character on frame 0.
        using var files = Install.Open();
        var motions = Motions(files);
        var model = NodModel.Read(files.Read("3d/models/civ04Merchant2.nod"), "civ04Merchant2.nod");
        var animation = ThingAnimation.For(model, "civ04Merchant", motions, "walk")!;
        animation.Advance(0.5f);
        float frame = animation.Motion!.Player.Frame;
        Assert.True(frame > 1f, "half a second of walking went nowhere");

        for (int i = 0; i < 10; i++) Assert.True(animation.Play("walk", motions) >= 0);
        Assert.Equal(frame, animation.Motion!.Player.Frame);

        // A different mode does restart it.
        Assert.True(animation.Play("stand", motions) >= 0);
        Assert.Equal(0f, animation.Motion!.Player.Frame);
    }

    [Fact]
    public void An_action_restarts_every_time_and_leaves_the_walk_underneath()
    {
        // An action always restarts, and it does not touch the motion channel: a character swinging
        // while it walks keeps both, and the action is what it is posed from while it lasts.
        using var files = Install.Open();
        var motions = Motions(files);
        var model = NodModel.Read(files.Read("3d/models/civ04Merchant2.nod"), "civ04Merchant2.nod");
        var animation = ThingAnimation.For(model, "civ04Merchant", motions, "walk")!;
        animation.Advance(0.3f);
        float walking = animation.Motion!.Player.Frame;

        string action = animation.Set.Modes.First(m => m.Has(NmsFlags.NoLoop) && m.Name != "walk").Name;
        Assert.True(animation.Play(action, motions, onMotion: false) >= 0);
        Assert.Equal(walking, animation.Motion!.Player.Frame);       // the walk is untouched
        Assert.Same(animation.Action, animation.Current);            // and the action is what shows

        animation.Advance(0.1f);
        Assert.True(animation.Action!.Player.Frame > 0f);
        Assert.True(animation.Play(action, motions, onMotion: false) >= 0);
        Assert.Equal(0f, animation.Action!.Player.Frame);            // asked again, swung again
    }

    [Fact]
    public void A_mode_listed_twice_is_rolled_between_and_a_special_one_is_not()
    {
        // wolf.nms lists `idle` twice — wolfIdle and wolfHowl — and Christof lists `claw` three
        // times, the third being the weapon's special. 1.1 rolls between the first two and
        // reaches the third only when it is asked for.
        using var files = Install.Open();
        var motions = Motions(files);
        var wolf = motions.Set("wolf");
        Assert.NotNull(wolf);
        var rolled = new HashSet<string>();
        for (int i = 0; i < 200; i++)
            if (motions.Find(wolf, "idle") is { } found) rolled.Add(found.Mode.File);
        Assert.Equal(2, rolled.Count);

        var christof = motions.Set("Christof");
        var claws = christof!.Modes.Where(m => m.Name == "claw").ToList();
        Assert.Equal(3, claws.Count);
        var ordinary = new HashSet<string>();
        for (int i = 0; i < 200; i++)
            if (motions.Find(christof, "claw") is { } found) ordinary.Add(found.Mode.File);
        var special = claws.Single(m => m.Has(NmsFlags.Special));
        Assert.DoesNotContain(special.File, ordinary);
        Assert.Equal(special.File, motions.Find(christof, "claw", special: true)!.Value.Mode.File);
    }

    [Fact]
    public void The_run_cycle_starts_at_two_hundred_units_a_second()
    {
        // The animation update compares the walker's commanded speed with 200 and
        // nothing else decides. Christof walks 115 and runs 250, so he straddles it.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, player: "Christof");
        var player = place.Player;
        Assert.NotNull(player);
        Assert.Equal(115f, player!.WalkSpeed);
        Assert.Equal(250f, player.RunSpeed);
        Assert.InRange(Player.RunAnimationBeyond, player.WalkSpeed + 1f, player.RunSpeed - 1f);

        var facing = player.Facing;
        player.Running = false;
        player.Move(facing, 1f / 30f);
        Assert.Equal("walk", Playing(player));

        player.Running = true;
        player.Move(facing, 1f / 30f);
        Assert.Equal("run", Playing(player));

        player.Move(System.Numerics.Vector2.Zero, 1f / 30f);
        Assert.Equal("stand", Playing(player));
    }

    static string Playing(Player player) => player.Thing.Animation?.Motion?.Mode.Name ?? "";

    [Fact]
    public void A_wounded_character_limps_from_its_next_step_and_still_runs()
    {
        // A walk becomes `limp` (mode 195) when THING_AF2_LIMPING is up; a run stays `run`.
        // The bit is only looked at when something asks for a new gait — the walker starting,
        // stopping or changing speed — so a wound taken in mid-stride shows at the next one.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, player: "Christof");
        var player = place.Player!;
        var facing = player.Facing;
        player.Running = false;
        player.Move(facing, 1f / 30f);
        Assert.Equal("walk", Playing(player));

        player.Thing.Actor!.Flags2 |= Actor.Flag2.Limping;
        Assert.True(player.Hurt);
        player.Move(facing, 1f / 30f);
        Assert.Equal("walk", Playing(player));          // nothing has asked again yet

        player.Move(System.Numerics.Vector2.Zero, 1f / 30f);
        Assert.Equal("stand", Playing(player));
        player.Move(facing, 1f / 30f);
        Assert.Equal("limp", Playing(player));

        player.Running = true;
        player.Move(facing, 1f / 30f);
        Assert.Equal("run", Playing(player));           // lame or not, a run is a run

        player.Thing.Actor!.Flags2 &= ~Actor.Flag2.Limping;
        player.Running = false;
        player.Move(facing, 1f / 30f);
        Assert.Equal("walk", Playing(player));
    }

    [Fact]
    public void A_slow_runner_keeps_its_walk_cycle_because_two_hundred_is_absolute()
    {
        // The consequence of that comparison: 129 of the 662 templates with a motion set and speeds
        // never reach 200, so they play their walk animation while running. Every zombie is one.
        using var files = Install.Open();
        var motions = Motions(files);
        var model = NodModel.Read(files.Read("3d/models/civ04Merchant2.nod"), "civ04Merchant2.nod");
        var animation = ThingAnimation.For(model, "civ04Merchant", motions, "stand")!;
        // 80 units a second is a zombie's run; 250 is Christof's.
        Assert.True(80f <= Player.RunAnimationBeyond);
        Assert.True(250f > Player.RunAnimationBeyond);
        Assert.True(animation.Play(Player.ModeWalk, motions) >= 0);
        Assert.Equal("walk", animation.Motion!.Mode.Name);
        Assert.Equal(1, animation.Motion.Mode.Index);
        Assert.Equal(17, Player.ModeRun);
        Assert.Equal(33, Player.ModeStand);
        Assert.Equal(195, Player.ModeLimp);
    }

    [Fact]
    public void An_animation_can_be_stretched_to_fit_a_given_time()
    {
        // Rate = 30 × nominal ÷ wanted, so a two-second animation asked to take one
        // second plays at 60 frames a second.
        using var files = Install.Open();
        var motions = Motions(files);
        var model = NodModel.Read(files.Read("3d/models/civ04Merchant2.nod"), "civ04Merchant2.nod");
        var animation = ThingAnimation.For(model, "civ04Merchant", motions, "walk")!;
        int nominal = ThingAnimation.Milliseconds(animation.Motion!.Player.Animation);
        Assert.True(animation.PlayFor("walk", nominal / 2, motions, onMotion: false) >= 0);
        Assert.Equal(ThingAnimation.Rate * 2f, animation.Action!.Player.Rate, 0.2f);
        Assert.True(animation.PlayFor("walk", nominal * 4, motions, onMotion: false) >= 0);
        Assert.Equal(ThingAnimation.Rate / 4f, animation.Action!.Player.Rate, 0.2f);
        // And a rate over the cap is cut to it, as the mixer cuts it.
        Assert.True(animation.PlayFor("walk", 1, motions, onMotion: false) >= 0);
        Assert.Equal(ThingAnimation.RateMax, animation.Action!.Player.Rate);
    }

    // ---- the gait fixes. Faithful is the default; these are what the player can
    // turn on, and every one of them must be provably off until asked.

    [Fact]
    public void Every_fix_is_off_until_the_player_asks()
    {
        var plain = new Fixes();
        Assert.All(plain.All, f => Assert.False(f.On, f.Name + " is on by default"));
        Assert.Equal("faithful (no fixes)", plain.ToString());

        var complaints = new List<string>();
        var configured = Fixes.Parse([], installedDir: Path.GetTempPath(), complain: complaints.Add, userPath: NoUserSettings);
        Assert.All(configured.All, f => Assert.False(f.On));
    }

    [Fact]
    public void A_fix_is_turned_on_by_name_and_a_misspelling_is_reported()
    {
        var complaints = new List<string>();
        var one = Fixes.Parse(["--fix", "movespeed"], Path.GetTempPath(), complaints.Add, userPath: NoUserSettings);
        Assert.True(one.MoveSpeed);
        Assert.False(one.MatchedGait);
        Assert.Empty(complaints);

        var all = Fixes.Parse(["--fix", "all"], Path.GetTempPath(), complaints.Add, userPath: NoUserSettings);
        Assert.All(all.All, f => Assert.True(f.On));
        Assert.Empty(complaints);                                     // all eleven can be on together

        Fixes.Parse(["--fix", "matchedgate"], Path.GetTempPath(), complaints.Add, userPath: NoUserSettings);
        Assert.Single(complaints);
        Assert.Contains("no such fix", complaints[0]);
    }

    /// <summary>
    /// `matchedgait` refines `movespeed`: the two decide the same
    /// thing, a cycle's rate, so the registry allows the one only as a refinement of the other. Asked for
    /// alone — which, until the registry, switched it on — it is left off and the reason said; with
    /// `movespeed` it is on, whichever is named first.
    /// </summary>
    [Fact]
    public void The_matched_gait_fix_alone_is_left_off_with_the_reason_and_with_movespeed_it_is_on()
    {
        var complaints = new List<string>();
        var alone = Fixes.Parse(["--fix", "matchedgait"], Path.GetTempPath(), complaints.Add, userPath: NoUserSettings);
        Assert.False(alone.MatchedGait);
        Assert.False(alone.MoveSpeed);
        Assert.Equal("[fixes] matchedgait is left off: matchedgait needs movespeed: it refines it", Assert.Single(complaints));

        complaints.Clear();
        var both = Fixes.Parse(["--fix", "matchedgait", "--fix", "movespeed"], Path.GetTempPath(), complaints.Add, userPath: NoUserSettings);
        Assert.True(both.MatchedGait);
        Assert.True(both.MoveSpeed);
        Assert.Empty(complaints);
    }

    [Fact]
    public void Christofs_stride_is_measured_from_his_own_footfalls()
    {
        using var files = Install.Open();
        var motions = Motions(files);
        var set = motions.Set("Christof")!;
        var model = NodModel.Read(files.Read("3d/models/christof.nod"), "christof.nod");
        var walk = motions.Find(set, "walk")!.Value;

        var stride = Stride.Measure(model, set, walk.Animation);
        Assert.NotNull(stride);
        // `ChrisDWalkFists.nad` is 29 frames and his feet carry him about 99 units through it, so it
        // was drawn for a walk of about 102 units a second. His template says 115.
        Assert.InRange(stride!.Value, 90f, 110f);
        Assert.InRange(Stride.NaturalSpeed(walk.Animation.Duration, stride.Value), 95f, 110f);

        // And the rate that makes one cycle cover one stride puts the ground back exactly.
        float rate = Stride.RateFor(115f, walk.Animation.Duration, stride.Value);
        Assert.Equal(stride.Value, 115f * walk.Animation.Duration / rate, 0.5f);
    }

    [Fact]
    public void A_creature_with_no_footfall_tags_cannot_be_measured()
    {
        // 139 of the game's locomotion modes have no footfall tags, and they are the things with no
        // ordinary gait — a bat's walk and run are the same flight loop.
        using var files = Install.Open();
        var motions = Motions(files);
        var set = motions.Set("batFlying")!;
        var model = NodModel.Read(files.Read("3d/models/christof.nod"), "christof.nod");
        Assert.Null(Stride.Measure(model, set, motions.Find(set, "walk")!.Value.Animation));
    }

    [Fact]
    public void Faithfully_a_cycle_plays_at_thirty_whatever_the_character_is_doing()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, player: "Christof");
        var player = place.Player!;
        var walk = place.Motions!.Find(player.Thing.Animation!.Set, Player.ModeWalk)!.Value;
        foreach (float speed in (ReadOnlySpan<float>)[1f, 70f, 115f, 250f, 1000f])
            Assert.Equal(ThingAnimation.Rate,
                         player.RateFor(walk.Mode, walk.Animation, Player.ModeWalk, speed, Faithful));
    }

    [Fact]
    public void The_matched_gait_fix_makes_one_cycle_cover_one_stride()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, player: "Christof");
        var player = place.Player!;
        var animation = player.Thing.Animation!;
        var walk = place.Motions!.Find(animation.Set, Player.ModeWalk)!.Value;
        float stride = Stride.Measure(animation.Model, animation.Set, walk.Animation)!.Value;
        // The rate itself: `Player.RateFor` takes whatever pair of switches it is handed, so the fix is tried
        // here on its own — a pair the settings chain no longer produces (it refines `movespeed`, above).
        var matched = new Fixes { MatchedGait = true };
        foreach (float speed in (ReadOnlySpan<float>)[80f, 115f, 150f])
        {
            float rate = player.RateFor(walk.Mode, walk.Animation, Player.ModeWalk, speed, matched);
            Assert.Equal(stride, speed * walk.Animation.Duration / rate, 1f);
        }
        // Christof walks a little faster than his animation was drawn for, so his legs speed up.
        Assert.InRange(player.RateFor(walk.Mode, walk.Animation, Player.ModeWalk, 115f, matched), 31f, 38f);
        // With `movespeed` on beside it — the pair a player can have — it has the say wherever a stride can be
        // measured: the same rate.
        var both = new Fixes { MoveSpeed = true, MatchedGait = true };
        Assert.Equal(player.RateFor(walk.Mode, walk.Animation, Player.ModeWalk, 150f, matched),
                     player.RateFor(walk.Mode, walk.Animation, Player.ModeWalk, 150f, both));
    }

    [Fact]
    public void The_movespeed_fix_moves_only_when_the_characters_speed_does()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, player: "Christof");
        var player = place.Player!;
        var walk = place.Motions!.Find(player.Thing.Animation!.Set, Player.ModeWalk)!.Value;
        var moveSpeed = new Fixes { MoveSpeed = true };
        // At its own walk speed nothing changes — which is why this fix is invisible until
        // something (Celerity) moves a character faster than its template says.
        Assert.Equal(ThingAnimation.Rate,
                     player.RateFor(walk.Mode, walk.Animation, Player.ModeWalk, player.WalkSpeed, moveSpeed), 0.01f);
        Assert.Equal(ThingAnimation.Rate * 1.5f,
                     player.RateFor(walk.Mode, walk.Animation, Player.ModeWalk, player.WalkSpeed * 1.5f, moveSpeed), 0.01f);
        // Celerity at its top level is four times speed; the clamp holds it to double.
        Assert.Equal(ThingAnimation.Rate * Player.GaitCeiling,
                     player.RateFor(walk.Mode, walk.Animation, Player.ModeWalk, player.WalkSpeed * 4f, moveSpeed), 0.01f);
    }

    [Fact]
    public void No_fix_touches_a_mode_the_game_did_not_mark()
    {
        // `movespeed` is on every walk, run and limp and on nothing else, so `stand` is untouched
        // however fast the character is nominally going.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, player: "Christof");
        var player = place.Player!;
        var stand = place.Motions!.Find(player.Thing.Animation!.Set, Player.ModeStand)!.Value;
        Assert.False(stand.Mode.Has(NmsFlags.MoveSpeed));
        Assert.Equal(ThingAnimation.Rate,
                     player.RateFor(stand.Mode, stand.Animation, Player.ModeStand, 250f,
                                    new Fixes { MatchedGait = true, MoveSpeed = true }));
    }

    /// <summary>
    /// 1.1 with nothing switched off, passed explicitly rather than left to
    /// <see cref="Fixes.Current"/>. That is a process-wide static and xUnit runs test classes in
    /// parallel, so a test that installs one changes what every other test running at that moment
    /// sees — which is how, six new tests in another class broke two physics tests
    /// and an inventory one. No test in this assembly writes either register any more.
    /// </summary>
    static Fixes Faithful => new();

    /// <summary>A player's settings file that is not there, so the machine's own (where a Fix may be on) cannot leak into a test.</summary>
    static string NoUserSettings => Path.Combine(Path.GetTempPath(), "vitomir-no-such-settings", "user.ini");
}
