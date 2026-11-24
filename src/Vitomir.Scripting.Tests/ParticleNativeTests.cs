// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Particles;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// The emitter natives (`CodexThing.EnableEmitter` and its neighbours) against the
/// Convent's candles, and the one script that makes a flame out of nothing: `TorchBearer`, whose
/// `created` spawns a `torchFlame` and hangs it on the bearer's hand. What is pinned is what 1.1
/// does: disabling an emitter only hides it (it keeps spawning), a thing with no emitter answers
/// false, 0 and the SDK's error string, and the render count is always 0.
/// </summary>
[Collection("scripts")]
public class ParticleNativeTests(Scripts s)
{
    static (CodexScenes Scenes, Location Where, GameFiles Files) Enter(Scripts s, string level, string scene)
    {
        s.Ready();
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var location = Location.Load(files, level, sky: false, scene: scene, player: "Christof");
        var scenes = new CodexScenes(s.Runtime!) { Chronicles = set };
        scenes.Enter(location);
        return (scenes, location, files);
    }

    static object? Call(Scripts s, string key, object? self, params object?[] args) =>
        s.Engine.get(new object[] { key, self!, args.Select(Wrap).ToArray() });

    static object? Wrap(object? v) => v switch
    {
        int i => java.lang.Integer.valueOf(i),
        float f => java.lang.Float.valueOf(f),
        _ => v,
    };

    static int Int(object? o) => ((java.lang.Integer)o!).intValue();
    static float Flt(object? o) => ((java.lang.Float)o!).floatValue();
    static bool Bln(object? o) => ((java.lang.Boolean)o!).booleanValue();

    [Fact]
    public void Disabling_a_candle_hides_it_and_it_goes_on_burning()
    {
        var (scenes, where, files) = Enter(s, "P1_CNVT", "CNVT_3_1");
        using var _f = files;
        var sim = new Simulation(where);
        var candle = where.Things.First(t => where.Particles.Of(t) is { Name: "candleflame" });
        var emitter = where.Particles.Of(candle)!;
        var self = s.Runtime!.NewThingRef("CodexThing", candle.Guid);
        for (int i = 0; i < 30; i++) sim.Step(1f / 30f);
        Assert.Contains(where.Particles.Batches, b => b.Emitter == emitter && b.Count > 0);

        Assert.True(Bln(Call(s, "CodexThing.DisableEmitter", self)));
        Assert.True(emitter.Hidden);
        for (int i = 0; i < 30; i++) sim.Step(1f / 30f);
        Assert.DoesNotContain(where.Particles.Batches, b => b.Emitter == emitter);
        // Still spawning: the flag is read by the draw and by nothing else.
        Assert.Contains(emitter.Particles, p => p.Life > 0f);

        Assert.True(Bln(Call(s, "CodexThing.EnableEmitter", self)));
        sim.Step(1f / 30f);
        Assert.Contains(where.Particles.Batches, b => b.Emitter == emitter);
        scenes.Leave();
    }

    [Fact]
    public void The_emitter_answers_its_kind_its_name_and_its_members()
    {
        var (scenes, where, files) = Enter(s, "P1_CNVT", "CNVT_3_1");
        using var _f = files;
        var candle = where.Things.First(t => where.Particles.Of(t) is { Name: "candleflame" });
        var self = s.Runtime!.NewThingRef("CodexThing", candle.Guid);
        Assert.Equal((int)EmitterKind.Fire, Int(Call(s, "CodexThing.GetEmitterType", self)));
        Assert.Equal("candleflame", Call(s, "CodexThing.GetEmitterName", self)?.ToString());
        Assert.Equal(150, Int(Call(s, "CodexThing.GetEmitterIntMember", self, EmitterMembers.Lifespan)));
        Assert.Equal(20, Int(Call(s, "CodexThing.GetEmitterIntMember", self, EmitterMembers.Rate)));
        Assert.Equal(2f, Flt(Call(s, "CodexThing.GetEmitterFloatMember", self, EmitterMembers.Size)));
        // The gas's drag is not a fire's member: −1.0, as the SDK says an error is.
        Assert.Equal(-1f, Flt(Call(s, "CodexThing.GetEmitterFloatMember", self, EmitterMembers.GasDrag)));
        Assert.True(Bln(Call(s, "CodexThing.SetEmitterIntMember", self, EmitterMembers.Lifespan, -400)));
        Assert.Equal(400u, where.Particles.Of(candle)!.Settings.Lifespan);            // the setter's abs
        Assert.True(Bln(Call(s, "CodexThing.SetEmitterFloatMember", self, EmitterMembers.FireTurbulence, 3f)));
        Assert.Equal(3f, where.Particles.Of(candle)!.Settings.Turbulence);

        // A thing with no emitter.
        var bare = s.Runtime.NewThingRef("CodexThing", where.Player!.Thing.Guid);
        Assert.False(Bln(Call(s, "CodexThing.DisableEmitter", bare)));
        Assert.Equal(0, Int(Call(s, "CodexThing.GetEmitterType", bare)));
        Assert.Equal("***ERR:EmitterName***", Call(s, "CodexThing.GetEmitterName", bare)?.ToString());
        Assert.Equal(-1, Int(Call(s, "CodexThing.GetEmitterIntMember", bare, EmitterMembers.Rate)));
        // Nothing in 1.1 ever writes the count this answers.
        Assert.Equal(0, Int(Call(s, "CodexWorld.GetLastEmitterRenderCount", null)));
        scenes.Leave();
    }

    /// <summary>
    /// The colour pair: the setter packs three floats into the start or end
    /// colour as red, green and blue — a component past 255 spills into its neighbour — and the
    /// getter answers (−1, −1, −1) whatever it is asked, because it reads nothing.
    /// </summary>
    [Fact]
    public void The_colour_members_pack_three_floats_and_the_getter_reads_nothing()
    {
        Assert.Equal(0xc0ff8000u, EmitterMembers.PackColor(255f, 128f, 0f, 0xc0000000));
        Assert.Equal(0x00010000u | 0x00000100u, EmitterMembers.PackColor(0f, 257f, 0f, 0));   // green's 256 is red's 1
        Assert.Equal(0x7f010203u, EmitterMembers.PackColor(1.4f, 2.5f, 2.6f, 0x7fffffff));    // rounded, halves to even

        var (scenes, where, files) = Enter(s, "P1_CNVT", "CNVT_3_1");
        using var _f = files;
        var candle = where.Things.First(t => where.Particles.Of(t) is { Name: "candleflame" });
        var self = s.Runtime!.NewThingRef("CodexThing", candle.Guid);
        var emitter = where.Particles.Of(candle)!;
        uint alpha = emitter.Settings.StartColor & 0xff000000;
        Assert.True(Bln(Call(s, "CodexThing.SetEmitterVectorMember", self, EmitterMembers.StartColor, new[] { 16f, 32f, 64f })));
        Assert.Equal(alpha | 0x102040u, emitter.Settings.StartColor);
        Assert.False(Bln(Call(s, "CodexThing.SetEmitterVectorMember", self, 1002, new[] { 1f, 2f, 3f })));
        Assert.Equal(new[] { -1f, -1f, -1f }, (float[])Call(s, "CodexThing.GetEmitterVectorMember", self, EmitterMembers.StartColor)!);
        scenes.Leave();
    }

    /// <summary>
    /// Old Town by night: the beggar's `TorchBearer` spawns `torchFlame` in its `created` and
    /// attaches it to his hand; the flame's template names `flametest`, so the next step gives it
    /// its emitter and the flame burns where his hand is.
    /// </summary>
    [Fact]
    public void A_torch_bearer_lights_his_torch()
    {
        var (scenes, where, files) = Enter(s, "P1_OLDT", "OLDT_7_1");
        using var _f = files;
        var sim = new Simulation(where);
        for (int i = 0; i < 30; i++) sim.Step(1f / 30f);
        var flame = where.Things.FirstOrDefault(t => t.Template.Equals("torchFlame", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(flame);
        Assert.NotNull(flame.AttachedTo);
        Assert.Equal("civ02beggar_pe", flame.AttachedTo!.Template, ignoreCase: true);
        var emitter = where.Particles.Of(flame);
        Assert.IsType<FireEmitter>(emitter);
        Assert.Equal("flametest", emitter!.Name, ignoreCase: true);
        Assert.True(emitter.Registered);
        var alive = emitter.Particles.Where(p => p.Life > 0f && float.IsFinite(p.Position.X)).ToArray();
        Assert.NotEmpty(alive);
        // Rising from the hand, within a torch's reach of it.
        Assert.All(alive, p => Assert.InRange(System.Numerics.Vector3.Distance(p.Position, flame.Position), 0f, 200f));
        scenes.Leave();
    }
}
