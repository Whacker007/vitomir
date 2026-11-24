// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// What a character can see and who it is willing to fight: the effect store underneath it, the
/// friend-or-foe test, and the two searches that 1,150 of the game's 1,277 characters run.
/// </summary>
public class SensesTests
{
    /// <summary>
    /// A place with its people in it. The characters come from the **chapter** scene rather than
    /// the level's own, so a load that does not name one gets an empty street.
    /// </summary>
    static Location Place(Vfs.GameFiles files, string level, string? player = null) =>
        Location.Load(files, level, things: true, sky: false,
                      scene: ChronicleSet.Load(files).ForLevel(level)?.Entry?.SceneName,
                      player: player);

    static NedFile Effects(Vfs.GameFiles files)
    {
        Assert.True(files.TryRead(EffectDefinitions.Path, out var data), "no misc/effects.ned");
        return NedFile.Read(data, EffectDefinitions.Path);
    }

    [Fact]
    public void Every_effect_in_the_game_parses()
    {
        using var files = Install.Open();
        var ned = Effects(files);
        Assert.Empty(ned.Dropped);
        Assert.Equal(187, ned.All.Count);

        // The two halves of the same feature, which is what `movespeed` turned out to be for.
        var celerity = ned["ef_disc_celerity1"]!;
        Assert.Equal(EffectKind.MoveSpeed, celerity.Kind);
        Assert.Equal(EffectCompose.Multiply, celerity.Compose);
        // Levels count from **zero** (1.1 asserts against 5 and indexes the
        // array directly), so the first level of Celerity is At(0) and the sixth is At(5).
        Assert.Equal(1.5f, celerity.At(0));
        Assert.Equal(4.0f, celerity.At(5));
        Assert.Equal(EffectKind.AttackSpeed, ned["ef_disc_celerity2"]!.Kind);

        // What the guards look for, and what the two sides of the detection test pull on.
        Assert.Equal(EffectKind.Wanted, ned["ef_wanted"]!.Kind);
        Assert.Equal(EffectKind.Hide, ned["ef_disc_invisibility"]!.Kind);
        Assert.Equal(EffectKind.Sense, ned["ef_disc_hsenses"]!.Kind);
        Assert.Equal(EffectKind.TeamFriendly, ned["ef_disc_mask1000faces"]!.Kind);

        // Two rows write their values with a leading plus, which a bare number parse would reject.
        Assert.Equal(4f, ned["ef_disc_drawingoutbeast"]!.At(0));

        // Only three of the eight compose modes are used by anything shipped.
        Assert.Equal([EffectCompose.None, EffectCompose.Add, EffectCompose.Multiply],
                     ned.All.Select(e => e.Compose).Distinct().Order().ToArray());
    }

    [Fact]
    public void The_store_answers_the_way_the_engine_does()
    {
        using var files = Install.Open();
        var ned = Effects(files);
        var place = Place(files, "P1_OTDY", player: "Christof");
        var who = place.Player!.Thing;
        var store = who.Effects;

        // Nothing applied: 1.0 for the types that multiply, 0.0 for the rest.
        Assert.Equal(1f, store.Value(EffectKind.MoveSpeed));
        Assert.Equal(1f, store.Value(EffectKind.AttackSpeed));
        Assert.Equal(0f, store.Value(EffectKind.Potence));
        Assert.Equal(0f, store.Value(EffectKind.Hide));

        // Celerity multiplies, and two of them multiply together.
        var celerity = ned["ef_disc_celerity1"]!;
        EffectRuntime.Apply(who, celerity, 0, now: 0, durationMs: 10_000);
        Assert.Equal(1.5f, store.Value(EffectKind.MoveSpeed), 3);
        EffectRuntime.Apply(who, celerity, 6, now: 0, durationMs: 10_000);
        Assert.Equal(6f, store.Value(EffectKind.MoveSpeed), 3);

        // And it ends when its time is up, not a moment before.
        EffectRuntime.Tick(who, 9_999, new Rng(1));
        Assert.Equal(6f, store.Value(EffectKind.MoveSpeed), 3);
        EffectRuntime.Tick(who, 10_000, new Rng(1));
        Assert.Equal(1f, store.Value(EffectKind.MoveSpeed), 3);
    }

    /// <summary>
    /// The one in the fold that would not have been guessed: an effect composed `none` makes the
    /// whole lookup return **zero**, rather than contributing its own value. 29 of the 187 shipped
    /// effects are composed that way and are meant to be found rather than valued.
    /// </summary>
    [Fact]
    public void Compose_none_makes_the_value_lookup_return_nothing()
    {
        using var files = Install.Open();
        var ned = Effects(files);
        var none = ned.All.First(e => e.Compose == EffectCompose.None && e.Kind != EffectKind.None
                                      && !e.Has(EffectFlags.Negator) && e.Script.Length == 0);
        var place = Place(files, "P1_OTDY", player: "Christof");
        var who = place.Player!.Thing;
        EffectRuntime.Apply(who, none, 5, now: 0);
        Assert.NotNull(who.Effects.Find(none.Kind, none.Subtype));
        Assert.Equal(0f, who.Effects.Value(none.Kind, none.Subtype));
    }

    [Fact]
    public void Pedestrians_and_lawmen_never_fight_each_other()
    {
        using var files = Install.Open();
        var place = Place(files, "P1_OTDY");
        var knight = place.Things.First(t => t.Template.Equals("convknight", StringComparison.OrdinalIgnoreCase));
        var townsman = place.Things.First(t => t.Template.StartsWith("civ0", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(Actor.Teams.Lawmen, knight.Actor!.Team);
        Assert.Equal(Actor.Teams.Pedestrians, townsman.Actor!.Team);
        Assert.True(Sight.Friendly(knight, townsman));
        Assert.True(Sight.Friendly(townsman, knight));
        Assert.True(Sight.Friendly(knight, knight));
    }

    /// <summary>
    /// Detection is a stealth-against-perception test before anything is traced, and both start at
    /// **zero** — the engine's actor constructor writes nothing else — so out of the box
    /// everybody can see everybody. Only <c>HIDE</c> moves it, and only <c>SENSE</c> answers.
    /// </summary>
    [Fact]
    public void Hiding_works_until_somebody_has_the_senses_for_it()
    {
        using var files = Install.Open();
        var place = Place(files, "P1_OTDY");
        var looker = place.Things.First(t => t.Actor is not null);
        var target = place.Things.Last(t => t.Actor is not null && !ReferenceEquals(t, looker));

        Assert.Equal(0, looker.Actor!.Perception);
        Assert.Equal(0, target.Actor!.Stealth);
        Assert.True(Sight.Notices(looker, target, targetMoving: false));

        target.Actor.Stealth = 3;                       // as HIDE would leave it
        Assert.False(Sight.Notices(looker, target, targetMoving: false));
        Assert.True(Sight.Notices(looker, target, targetMoving: true) == false);

        looker.Actor.Perception = 3;                    // as SENSE would leave it
        Assert.True(Sight.Notices(looker, target, targetMoving: false));

        // Moving is one easier to spot, which is what saves a looker one short of the mark.
        target.Actor.Stealth = 4;
        Assert.False(Sight.Notices(looker, target, targetMoving: false));
        Assert.True(Sight.Notices(looker, target, targetMoving: true));
    }

    /// <summary>
    /// The whole thing, in the place it matters: stand Christof among the Monastery's Cappadocians
    /// and they notice him, take him as a target and close in. Once all 32 of them
    /// stood still for ever.
    /// </summary>
    [Fact]
    public void Enemies_notice_the_player_and_come_for_him()
    {
        using var files = Install.Open();
        var place = Place(files, "P1_MON1", "Christof");
        var player = place.Player!;
        player.Thing.Position = new System.Numerics.Vector3(100, -700, -400);
        player.Ground();

        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        minds.Steered = null;                           // nobody is driving him; let them all think
        var started = minds.All.ToDictionary(m => m, m => m.Thing.Position);
        for (float t = 0; t < 25f; t += 1f / 30f) { minds.Tick(1f / 30f); minds.Follow(1f / 30f); }

        var hunting = minds.All.Where(m => ReferenceEquals(m.Target, player.Thing)).ToArray();
        Assert.True(hunting.Length >= 3,
                    $"only {hunting.Length} of {minds.All.Length} minds came after the player");
        Assert.All(hunting, m => Assert.Equal(AiMode.Enemy, m.Mode));
        // And they did not just stand there: chasing him is what moved them.
        Assert.All(hunting, m => Assert.True(
            System.Numerics.Vector3.Distance(started[m], m.Thing.Position) > 1f,
            $"{m.Thing.Template} took the player as a target and never moved"));
    }

    /// <summary>
    /// The convent knights are one of only two AI files in the game that search **style 1** — the
    /// Masquerade. They see Christof perfectly well and do nothing about it, because the only crime
    /// the engine knows is being caught mid-attack, and no blow can be struck yet.
    /// </summary>
    [Fact]
    public void The_guards_watch_without_attacking()
    {
        using var files = Install.Open();
        var place = Place(files, "P1_OTDY", "Christof");
        var player = place.Player!;
        player.Thing.Position = new System.Numerics.Vector3(-60, -420, 0);
        player.Ground();

        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        minds.Steered = null;
        // Style 1 *and* actually searching: a couple of the game's AI files carry the Masquerade
        // search style on a class whose start mode is pedwander, so they never run it.
        var guards = minds.All.Where(m => m.Class.SearchStyle == 1 && m.Mode == AiMode.EnemySearch).ToArray();
        Assert.True(guards.Length >= 1, "expected the convent knights to search the Masquerade way");

        for (float t = 0; t < 15f; t += 1f / 30f) { minds.Tick(1f / 30f); minds.Follow(1f / 30f); }
        Assert.All(guards, m => Assert.Null(m.Target));
        Assert.All(guards, m => Assert.Equal(AiMode.EnemySearch, m.Mode));
    }
}
