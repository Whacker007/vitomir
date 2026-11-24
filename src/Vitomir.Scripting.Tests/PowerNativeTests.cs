// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// The natives the discipline and effect scripts lean on (`CodexScenes.Powers`): the effect doors,
/// the discipline levels and slots, the mind questions, and the sphere query.
/// Called the way the bridge calls them, against the shipped street.
/// </summary>
[Collection("scripts")]
public class PowerNativeTests(Scripts s)
{
    sealed class Scene : IDisposable
    {
        public required GameFiles Files { get; init; }
        public required Location Place { get; init; }
        public required CodexScenes Scenes { get; init; }
        public required WorldThing Christof { get; init; }
        public void Dispose() { Scenes.Leave(); Files.Dispose(); }
    }

    Scene Enter()
    {
        s.Ready();
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_OTDY");
        var place = Location.Load(files, "P1_OTDY", sky: false, scene: where?.Entry?.SceneName, player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var scenes = new CodexScenes(s.Runtime!) { Chronicles = set };
        scenes.Enter(place);
        return new Scene { Files = files, Place = place, Scenes = scenes, Christof = place.Steered! };
    }

    object? Call(string key, object? self, params object?[] args) =>
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

    object? Ref(string cls, WorldThing t) => s.Runtime!.NewThingRef(cls, t.Guid);

    /// <summary>
    /// **A scripted shell stays on**: the material is loaded the engine's way —
    /// Fortitude's <c>redCloudShell</c> from <c>materials/General</c>, a bare texture like
    /// <c>white</c> as a plain material, and a name the install lacks as the default material — so
    /// none of them turns the shell off. Earlier, every one of them did.
    /// </summary>
    [Fact]
    public void A_scripted_shell_stays_on()
    {
        using var m = Enter();
        var me = Ref("CodexThing", m.Christof);
        Call("CodexThing.SetShell", me, "redCloudShell", 0x5000, 0f, 1f, 1f, 1f);
        Assert.Equal("redCloudShell", m.Christof.Fx!.Shell);
        Assert.True(m.Christof.Fx.ShellRamping);
        Call("CodexThing.SetShell", me, "white", 0x1000, 0f, 0.3f, 1f, 1f);
        Assert.Equal("white", m.Christof.Fx.Shell);
        Call("CodexThing.SetShell", me, "red", 0x80, 0.8f, 0f, 5f, 1f);
        Assert.Equal("red", m.Christof.Fx.Shell);
        Assert.Equal("3d/materials/dflt", m.Place.Materials.Items[m.Place.Materials.Named("red")!.Value].Name);
        Call("CodexThing.EndShell", me);
        Assert.Null(m.Christof.Fx.Shell);
    }

    /// <summary>
    /// By level with the script's own two numbers, found by name and by GUID, read back, and removed
    /// — after which it is not found, and the parameter lookups give the engine's "none" values.
    /// </summary>
    [Fact]
    public void An_effect_added_by_a_script_is_found_read_and_removed()
    {
        using var m = Enter();
        var me = Ref("CodexActor", m.Christof);
        int guid = Int(Call("CodexActor.AddActorEffectByLevel", me, "ef_disc_potence", 5000, 2, 0, 0, 7, 1.5f));
        Assert.NotEqual(0, guid);
        Assert.Equal(guid, Int(Call("CodexActor.FindActorEffect", me, "EF_DISC_POTENCE")));
        Assert.Equal(guid, Int(Call("CodexActor.FindActorEffect", me, guid)));
        Assert.Equal(2, Int(Call("CodexActor.GetActorEffectLevel", me, guid)));
        Assert.Equal(7, Int(Call("CodexActor.GetActorEffectIntParam", me, "ef_disc_potence")));
        Assert.Equal(1.5f, Flt(Call("CodexActor.GetActorEffectFloatParam", me, "ef_disc_potence")));

        Assert.True(Bln(Call("CodexActor.RemoveActorEffect", me, guid)));
        Assert.Equal(0, Int(Call("CodexActor.FindActorEffect", me, guid)));
        Assert.False(Bln(Call("CodexActor.RemoveActorEffect", me, guid)));
        Assert.Equal(-9_999_999, Int(Call("CodexActor.GetActorEffectIntParam", me, guid)));
        Assert.Equal(-1e7f, Flt(Call("CodexActor.GetActorEffectFloatParam", me, guid)));
        Assert.Equal(0, Int(Call("CodexActor.AddActorEffectByLevel", me, "ef_no_such_effect", 5000, 0, 0, 0, 0, 0f)));
    }

    [Fact]
    public void A_thing_that_is_not_a_character_answers_minus_two()
    {
        using var m = Enter();
        var prop = m.Place.Things.First(t => t.Type == ThingType.Prop);
        var r = Ref("CodexActor", prop);
        Assert.Equal(-2, Int(Call("CodexActor.GetActorEffectLevel", r, "ef_disc_potence")));
        Assert.Equal(-2, Int(Call("CodexActor.GetActorDisciplineLevel", r, "Celerity")));
    }

    /// <summary>
    /// −2 for a discipline not had; set to a level (−1 to 4 only) and the next of the group opens
    /// behind it; a quick slot only for one it has, 0 to 5 or −1.
    /// </summary>
    [Fact]
    public void Discipline_levels_and_slots_are_held_to_their_ranges()
    {
        using var m = Enter();
        var me = Ref("CodexActor", m.Christof);
        Assert.Equal(-2, Int(Call("CodexActor.GetActorDisciplineLevel", me, "Awe")));
        Assert.False(Bln(Call("CodexActor.SetActorDisciplineLevel", me, "Awe", 5)));
        Assert.True(Bln(Call("CodexActor.SetActorDisciplineLevel", me, "Awe", 1)));
        Assert.Equal(1, Int(Call("CodexActor.GetActorDisciplineLevel", me, "Awe")));
        Assert.Equal(-1, Int(Call("CodexActor.GetActorDisciplineLevel", me, "DreadGaze")));
        Assert.False(Bln(Call("CodexActor.SetActorDisciplineLevel", me, "NoSuchDiscipline", 0)));

        Assert.True(Bln(Call("CodexActor.SetActorDisciplineSlot", me, "Awe", 5)));
        Assert.Equal(5, m.Christof.Actor!.Disciplines["Awe"]!.QuickSlot);
        Assert.False(Bln(Call("CodexActor.SetActorDisciplineSlot", me, "Awe", 6)));
        Assert.False(Bln(Call("CodexActor.SetActorDisciplineSlot", me, "Majesty", 0)));
    }

    /// <summary>`AIIsPossessed` reads a bit above AL, which the VM throws away: always false.</summary>
    [Fact]
    public void The_mind_questions_answer_from_the_mode_and_possession_is_never_seen()
    {
        using var m = Enter();
        var other = m.Place.Minds!.All.First(x => !ReferenceEquals(x.Thing, m.Christof) && x.Thing.Actor is not null);
        var r = Ref("CodexActor", other.Thing);
        other.Enter(AiMode.Frenzied, andBase: false);
        Assert.True(Bln(Call("CodexActor.AIIsFrenzied", r)));
        Assert.Equal((int)AiMode.Frenzied, Int(Call("CodexActor.AIGetMode", r)));
        other.Flags |= Mind.FlagPossessed;
        Assert.False(Bln(Call("CodexActor.AIIsPossessed", r)));

        Assert.False(Bln(Call("CodexActor.AILockMode", r)));      // the old bit
        Assert.True(Bln(Call("CodexActor.AIIsModeLocked", r)));
        Assert.True(Bln(Call("CodexActor.AILockMode", r)));

        Assert.True(Bln(Call("CodexActor.AISetTarget", r, m.Christof.Guid)));
        Assert.Equal(AiMode.Enemy, other.Mode);
        Assert.Equal(m.Christof.Guid, Int(Call("CodexActor.AIGetTarget", r)));
    }

    /// <summary>
    /// The sphere query: the reference thing is not left out of its own answer, only the accepted
    /// types come back, and every one is within the radius plus its own.
    /// </summary>
    [Fact]
    public void Things_in_a_sphere_come_back_by_type_and_include_the_one_asking()
    {
        using var m = Enter();
        int h = Int(Call("CodexCollision.Create", null));
        Assert.True(h >= 0);
        var q = s.Runtime!.NewThingRef("CodexCollision", h);
        Call("CodexCollision.IgnoreAll", q);
        Call("CodexCollision.AcceptType", q, (int)ThingType.Actor);
        Call("CodexCollision.AcceptType", q, (int)ThingType.Player);
        var c = m.Christof.Position;
        int n = Int(Call("CodexCollision.ThingsInSphere", q, new[] { c.X, c.Y, c.Z + 30f }, 400f, m.Christof.Guid));
        Assert.True(n >= 1);
        Assert.Equal(n, Int(Call("CodexCollision.GetNumResults", q)));
        var found = Enumerable.Range(0, n).Select(i => m.Place.Thing(Int(Call("CodexCollision.GetResult", q, i)))!).ToList();
        Assert.Contains(m.Christof, found);
        Assert.All(found, t => Assert.True(t.Type is ThingType.Actor or ThingType.Player));
        Assert.Equal(0, Int(Call("CodexCollision.GetResult", q, n)));
        Call("CodexCollision.Free", q);
        Assert.Equal(0, Int(Call("CodexCollision.GetNumResults", q)));
    }
}
