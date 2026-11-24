// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The staged harm in a world as the program makes it, with 1.1's Christof (his broadsword) and a guard (his
/// rapier and shield) drawn, sheeted by their characters and holding their own weapons: contacts (synthetic: only tests
/// make them) staged into wounds with each one's own weapon, Health on the sheet, and the death played by the platform.
/// Skips without the 1.1 install (VTMR_GAME_DIR).
/// </summary>
public sealed class HarmWorldTests
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static LevelSpace Floor()
    {
        var v = new List<Vector3>();
        var ix = new List<int>();
        for (int x = -20; x < 20; x += 4)
            for (int y = -20; y < 20; y += 4)
            {
                int at = v.Count;
                v.Add(new(x, y, 0)); v.Add(new(x + 4, y, 0)); v.Add(new(x + 4, y + 4, 0)); v.Add(new(x, y + 4, 0));
                ix.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
            }
        return new LevelSpace("floor", CollisionWorld.FromTriangles([.. v], [.. ix]));
    }

    [Fact]
    public void Christof_s_broadsword_kills_the_guard_by_the_staged_harm_and_the_platform_plays_his_death()
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        var lib = Library.Value!;
        var assets = new CharacterAssets();
        int christofKind = OneOneLibrary.Register(assets, lib.Character("Christof", "BroadSword")!);
        int guardKind = OneOneLibrary.Register(assets, lib.Character("rudolfGuard")!);
        var contacts = new List<(long Tick, ContactMade Contact)>();
        var trace = new StringWriter();

        var m = TestData.Sandbox();
        var b = new WorldBuilder(21);
        MovementServices.Register(b, Floor());
        b.Animation(assets);
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        DeathServices.Register(b);
        b.Service(new CombatTrace(trace));
        b.Event<ContactMade>();
        b.System(Phase.Contact, 0, "test.contacts", new DelegateSystem(w =>
        {
            foreach (var (tick, c) in contacts)
                if (tick == w.Tick) w.Events<ContactMade>().Publish(c with { Tick = w.Tick });
        }));
        m.Register(b);
        var world = b.Build();
        var people = world.Service<PeopleHooks>();
        var christof = MovementServices.SpawnControlled(world, new(0, 0, 0.01f), DetMath.HalfPi, Locomotion.Sandbox);
        people.Made(christof, PersonRole.Player, "Christof");
        world.Set(christof, CharacterAnimation.For(assets, christofKind));
        world.Set(christof, MotionInput.Standing);
        CombatServices.Arm(world, christof);
        var guard = MovementServices.SpawnBody(world, new(0, 1.6f, 0.01f), -DetMath.HalfPi, Locomotion.Sandbox);
        people.Made(guard, PersonRole.Townsperson, "rudolfGuard");
        world.Set(guard, CharacterAnimation.For(assets, guardKind));
        world.Set(guard, MotionInput.Standing);
        CombatServices.Arm(world, guard);
        var sheets = world.Service<ActorSheets>();
        // The player is sandbox.player and the guard sandbox.guard_trained by default; this fight is the stress fixtures', by name.
        Assert.Equal("sandbox.player", sheets.Template(christof));
        Assert.Equal("sandbox.guard_trained", sheets.Template(guard));
        var route = world.Service<IPersonSheets>();
        Assert.True(route.TryTemplate(christof, "sandbox.christof", out string error), error);
        Assert.True(route.TryTemplate(guard, "sandbox.guard", out error), error);
        Assert.Equal("sandbox.christof", sheets.Template(christof));
        Assert.Equal("sandbox.guard", sheets.Template(guard));

        // The guard's rapier on Christof first, then Christof's broadsword on the guard, a blow a second.
        contacts.Add((30, new ContactMade(1, guard, 1, christof, ContactMet.Body, "belly", ContactMaterial.Cloth, 0, new(0, 0.3f, 1.1f), new(0, -1, 0), 8f, 8f, false, false, true, false)));
        for (int i = 0; i < 6; i++)
            contacts.Add((60 + i * 120, new ContactMade(2 + i, christof, 1 + i, guard, ContactMet.Body, "chest", ContactMaterial.Cloth, 0, new(0, 1.4f, 1.3f), new(0, 1, 0), 30f, 30f, false, false, true, false)));
        var died = new List<ActorDied>();
        for (int t = 0; t < 900; t++)
        {
            MovementServices.Step(world, ActionFrame.Idle);
            died.AddRange(world.Events<ActorDied>().ThisTick);
        }

        var lines = trace.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Equal(7, lines.Count);
        static string Weapon(JsonElement l) => l.GetProperty("events")[0].GetProperty("inputs").GetProperty("weapon").GetString()!;
        Assert.Equal("weapRapier_shield", Weapon(lines[0]));                  // the guard's own weapon, from his character
        Assert.Equal("BroadSword", Weapon(lines[1]));
        // The blows staged until he died are lethal; every blow after on his dead body is denied (no harm on the dead).
        int deathLine = lines.FindIndex(l => l.GetProperty("health_after").GetInt32() == 0);
        Assert.InRange(deathLine, 1, 6);
        Assert.All(lines.Take(deathLine + 1), l => Assert.Equal("Lethal", l.GetProperty("injuries")[0].GetProperty("type").GetString()));
        Assert.All(lines.Skip(deathLine + 1), l =>
        {
            Assert.Equal("Denied", l.GetProperty("outcome").GetString());
            Assert.Equal("IllegalTarget", l.GetProperty("admission").GetProperty("reason").GetString());
            Assert.Equal(0, l.GetProperty("injuries").GetArrayLength());
        });
        var d = Assert.Single(died);
        Assert.Equal((guard, christof, HarmKind.Lethal), (d.Who, d.Killer, d.Kind));
        Assert.Equal(0, sheets.Resource(guard, "health")!.Value.Current);
        Assert.True(world.Has<Dead>(guard));
        Assert.Equal(ActionRole.DeathQuick, world.Get<CharacterAnimation>(guard).Action);    // lethal: the quick death
        Assert.False(world.Has<Dead>(christof));
    }
}
