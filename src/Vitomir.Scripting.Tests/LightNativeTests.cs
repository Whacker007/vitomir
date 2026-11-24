// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// The dynamic-light natives (`CodexThing.AllocateDynamicLight` and its eleven neighbours)
/// against Old Town by night, where the beggar's
/// `TorchBearer` makes a `torchFlame` whose template carries a light. What is pinned is what 1.1
/// answers: the intensity a script reads and writes is this frame's (the light pass rewrites it from
/// the set intensity every frame); a template's light has no material and says so in the engine's own
/// error string; a script's light is 200, white and `lightblob`; the style is held to 0–23; and a
/// thing with no light answers 0 (a thing that is not there, −1).
/// </summary>
[Collection("scripts")]
public class LightNativeTests(Scripts s)
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
    public void The_beggars_torch_answers_its_templates_light_and_a_script_can_make_one()
    {
        var (scenes, where, files) = Enter(s, "P1_OLDT", "OLDT_7_1");
        using var _f = files;
        var sim = new Simulation(where);
        for (int i = 0; i < 30; i++) sim.Step(1f / 30f);
        var flame = where.Things.First(t => t.Template.Equals("torchFlame", StringComparison.OrdinalIgnoreCase));
        var torch = s.Runtime!.NewThingRef("CodexThing", flame.Guid);

        // This frame's: 270 times light style 1's flicker, which dips to 0.95.
        Assert.InRange(Flt(Call(s, "CodexThing.GetDynamicLightIntensity", torch)), 270f * 0.95f - 0.01f, 270f);
        Assert.Equal(270f, Flt(Call(s, "CodexThing.GetDynamicLightRadius", torch)));
        Assert.Equal(new[] { 239f, 176f, 29f }, (float[])Call(s, "CodexThing.GetDynamicLightColor", torch)!);
        Assert.Equal(1, Int(Call(s, "CodexThing.GetDynamicLightStyle", torch)));
        Assert.Equal("***ERR:Bad Material***", Call(s, "CodexThing.GetDynamicLightMaterial", torch));
        Assert.False(Bln(Call(s, "CodexThing.AllocateDynamicLight", torch)));   // it has one

        // A set lasts until the light pass rewrites it on the next frame.
        Assert.True(Bln(Call(s, "CodexThing.SetDynamicLightIntensity", torch, -40f)));
        Assert.Equal(40f, Flt(Call(s, "CodexThing.GetDynamicLightIntensity", torch)));
        sim.Step(1f / 30f);
        Assert.InRange(Flt(Call(s, "CodexThing.GetDynamicLightIntensity", torch)), 250f, 270f);
        Assert.True(Bln(Call(s, "CodexThing.SetDynamicLightStyle", torch, 99)));
        Assert.Equal(23, Int(Call(s, "CodexThing.GetDynamicLightStyle", torch)));

        // A thing without one: 0 and false, until a script makes one — 200, white, `lightblob`.
        var beggar = s.Runtime!.NewThingRef("CodexThing", flame.AttachedTo!.Guid);
        Assert.Equal(0f, Flt(Call(s, "CodexThing.GetDynamicLightIntensity", beggar)));
        Assert.Equal("***ERR:Bad DynaLight***", Call(s, "CodexThing.GetDynamicLightMaterial", beggar));
        Assert.True(Bln(Call(s, "CodexThing.AllocateDynamicLight", beggar)));
        Assert.Equal(200f, Flt(Call(s, "CodexThing.GetDynamicLightIntensity", beggar)));
        Assert.Equal(new[] { 255f, 255f, 255f }, (float[])Call(s, "CodexThing.GetDynamicLightColor", beggar)!);
        Assert.Equal("lightblob", Call(s, "CodexThing.GetDynamicLightMaterial", beggar));
        Assert.True((flame.AttachedTo.Flags & WorldThing.Flag.DynaLight) != 0);
        Assert.True(Bln(Call(s, "CodexThing.ClearDynamicLight", beggar)));
        Assert.True((flame.AttachedTo.Flags & WorldThing.Flag.DynaLight) == 0);
        Assert.Equal(0, Int(Call(s, "CodexThing.GetDynamicLightStyle", beggar)));
        scenes.Leave();
    }
}
