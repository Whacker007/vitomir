// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The twelve natives that reach a thing's dynamic light:
// CodexThing's light natives. The scripts that use them are the player's torch and
// flashlight items, the Gangrel eye, and the discipline torch.
using System.Numerics;
using Vitomir.Engine.Explosions;
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    void RegisterLights()
    {
        // `ClearDynamicLight`: true for any thing that is there — with or
        // without a light to clear — and the light and THING_FLAG_DYNALIGHT go.
        Engine.On("CodexThing.ClearDynamicLight", (self, _) =>
        {
            if (Of(self) is not { } t || location is null) return CodexHost.Box(false);
            location.Lights.Clear(t);
            return CodexHost.Box(true);
        });
        // `AllocateDynamicLight`: a new light with the `lightblob`
        // material, switched on; false when the thing already has one.
        Engine.On("CodexThing.AllocateDynamicLight", (self, _) =>
            CodexHost.Box(Of(self) is { } t && location is not null && location.Lights.Allocate(t, blob: true)));

        // The intensity is **this frame's**, which the light
        // pass rewrites from the set intensity every frame the thing is seen — so a script's set
        // lasts a frame. No thing: −1.0; no light: 0.0 / false. The setter stores the absolute value.
        Engine.On("CodexThing.GetDynamicLightIntensity", (self, _) =>
            CodexHost.Box(Of(self) is not { } t ? -1f : LightOf(t)?.Intensity ?? 0f));
        Engine.On("CodexThing.SetDynamicLightIntensity", (self, a) =>
        {
            if (Of(self) is not { } t || LightOf(t) is not { } l) return CodexHost.Box(false);
            l.Intensity = MathF.Abs(CodexHost.Float(a, 0));
            return CodexHost.Box(true);
        });
        // The light's radius, the same shape.
        Engine.On("CodexThing.GetDynamicLightRadius", (self, _) =>
            CodexHost.Box(Of(self) is not { } t ? -1f : LightOf(t)?.Radius ?? 0f));
        Engine.On("CodexThing.SetDynamicLightRadius", (self, a) =>
        {
            if (Of(self) is not { } t || LightOf(t) is not { } l) return CodexHost.Box(false);
            l.Radius = MathF.Abs(CodexHost.Float(a, 0));
            return CodexHost.Box(true);
        });
        // The colour: three floats, 0–255 a channel; (0, 0, 0) with no light or
        // no thing. The setter takes the array's first three.
        Engine.On("CodexThing.GetDynamicLightColor", (self, _) =>
        {
            var c = Of(self) is { } t && LightOf(t) is { } l ? l.Color : Vector3.Zero;
            return new[] { c.X, c.Y, c.Z };
        });
        Engine.On("CodexThing.SetDynamicLightColor", (self, a) =>
        {
            if (Of(self) is not { } t || LightOf(t) is not { } l) return CodexHost.Box(false);
            if (a.Length > 0 && a[0] is float[] { Length: >= 3 } f) l.Color = new Vector3(f[0], f[1], f[2]);
            return CodexHost.Box(true);
        });
        // The material: its name, or the engine's error strings — a template's
        // light has none, and says so. The setter loads the material (the name cut to 31)
        // and answers false, changing nothing, when it does not load.
        Engine.On("CodexThing.GetDynamicLightMaterial", (self, _) =>
        {
            if (Of(self) is not { } t) return "***ERR:Bad Thing***";
            if (LightOf(t) is not { } l) return "***ERR:Bad DynaLight***";
            return l.Material.Length == 0 ? "***ERR:Bad Material***" : l.Material;
        });
        Engine.On("CodexThing.SetDynamicLightMaterial", (self, a) =>
        {
            if (Of(self) is not { } t || LightOf(t) is not { } l) return CodexHost.Box(false);
            var name = CodexHost.Str(a, 0) ?? "";
            if (name.Length > 31) name = name[..31];
            if (location?.Materials.Named(name) is null) return CodexHost.Box(false);
            l.Material = name;
            return CodexHost.Box(true);
        });
        // The style: 0 with no light, −1 with no thing; the setter holds it to
        // 0–23 (0x17).
        Engine.On("CodexThing.GetDynamicLightStyle", (self, _) =>
            CodexHost.Box(Of(self) is not { } t ? -1 : LightOf(t)?.Style ?? 0));
        Engine.On("CodexThing.SetDynamicLightStyle", (self, a) =>
        {
            if (Of(self) is not { } t || LightOf(t) is not { } l) return CodexHost.Box(false);
            l.Style = Math.Clamp(CodexHost.Int(a, 0), 0, MaxLightStyle);
            return CodexHost.Box(true);
        });
    }

    /// <summary>The highest style a script may give a light (the native clamps to 0x17).</summary>
    public const int MaxLightStyle = 0x17;

    /// <summary>The light a thing has, if any.</summary>
    DynamicLight? LightOf(WorldThing thing) => location?.Lights.Of(thing);
}
