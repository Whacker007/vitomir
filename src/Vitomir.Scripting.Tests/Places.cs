// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// A place running with everything a script can reach: its level and scene with Christof in it,
/// the minds awake with the effect and discipline tables, a session, a camera and conversations,
/// the scripts entered, and a simulation to step. What the native and callback tests share.
/// </summary>
public sealed class Place : IDisposable
{
    public required GameFiles Files { get; init; }
    public required Location Where { get; init; }
    public required CodexScenes Scenes { get; init; }
    public required Simulation Sim { get; init; }
    public required Session Session { get; init; }
    public required DisciplineDefinitions Powers { get; init; }
    public required EffectDefinitions Effects { get; init; }
    public WorldThing Christof => Where.Player!.Thing;

    public void Run(float seconds)
    {
        for (float t = 0; t < seconds; t += 1f / 30f) Sim.Step(1f / 30f);
    }

    /// <summary>Steps until <paramref name="done"/> holds or the time runs out; answers whether it held.</summary>
    public bool RunUntil(Func<bool> done, float seconds)
    {
        for (float t = 0; t < seconds; t += 1f / 30f)
        {
            if (done()) return true;
            Sim.Step(1f / 30f);
        }
        return done();
    }

    public void Dispose()
    {
        Scenes.Leave();
        Files.Dispose();
    }

    /// <summary>
    /// A level with its story scene — the chronicle's entry scene for it when none is named — and
    /// Christof in it as a party member of the player type.
    /// </summary>
    public static Place Enter(Scripts s, string level, string? scene = null)
    {
        s.Ready();
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var owner = set.ForLevel(level)!.Value;
        var where = Location.Load(files, level, sky: false, scene: scene ?? owner.Entry!.SceneName, player: "Christof");
        var session = new Session { Here = where, Chronicle = owner.Chronicle };
        session.Quests.Read = name => files.TryRead($"quests/{name}.nqd", out var d) ? d : null;
        where.Session = session;
        var powers = new DisciplineDefinitions(files);
        var effects = new EffectDefinitions(files);
        where.Awaken(new AiClasses(files), 1, effects, powers);
        where.Player!.Thing.Actor!.Flags |= Actor.Flag.Party;
        var camera = new WorldCamera();
        camera.Start(where.Player.Thing);
        var talk = new Conversations();
        var scenes = new CodexScenes(s.Runtime!)
        {
            Chronicles = set, Camera = camera, Talk = talk, Nls = new Vitomir.Engine.Ui.Strings(files).Find,
        };
        scenes.Enter(where);
        return new Place
        {
            Files = files, Where = where, Scenes = scenes, Session = session, Powers = powers, Effects = effects,
            Sim = new Simulation(where) { Scripts = scenes, Camera = camera, Talk = talk },
        };
    }

    /// <summary>A native, called the way a script calls it.</summary>
    public static object? Call(Scripts s, string key, object? self, params object?[] args) =>
        s.Engine.get(new object[] { key, self!, args.Select(Wrap).ToArray() });

    static object? Wrap(object? v) => v switch
    {
        int i => java.lang.Integer.valueOf(i),
        float f => java.lang.Float.valueOf(f),
        bool b => java.lang.Boolean.valueOf(b),
        _ => v,
    };

    public static int Int(object? o) => ((java.lang.Integer)o!).intValue();
    public static bool Bln(object? o) => ((java.lang.Boolean)o!).booleanValue();
}
