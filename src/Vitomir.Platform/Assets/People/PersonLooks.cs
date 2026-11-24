// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A look (PersonLook) laid on a person built from its files.
using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace Vitomir.Platform.Assets.People;

/// <summary>
/// A person's look laid on the plain build of its files (<see cref="PersonLook"/>): a model of its own that shares the
/// plain one's vertices, indices, tangents and skeleton (so two people in the same clothes hold one mesh), with its own parts (the hair's,
/// the beard's and the skin's material factors) and, when its hair greys, its own vertex colours.
/// </summary>
public static class PersonLooks
{
    /// <summary>The hair's and the beard's materials (CONVENTIONS, Hair: <i>"Named hair or beard"</i>).</summary>
    public static bool IsHair(PartMaterial m) => m.Name is "hair" or "beard";

    /// <summary>The skin's material on a base body (<c>skin_male</c>, <c>skin_female</c>).</summary>
    public static bool IsSkin(PartMaterial m) => m.Name.StartsWith("skin", StringComparison.Ordinal);

    /// <summary>
    /// The hair's material factor for <paramref name="look"/>: its colour, or where some of it is grey the lighter of its colour and the
    /// grey a channel (each vertex's colour then brings its lock down to its own: <see cref="LockColour"/>).
    /// </summary>
    public static Vector3 HairFactor(PersonLook look) => look.Grey > 0f ? Vector3.Max(look.HairColour, look.GreyLock) : look.HairColour;

    /// <summary>What a hair vertex's colour is multiplied by: its lock's colour (grey when its order is under the share) over the factor.</summary>
    public static Vector3 LockColour(PersonLook look, float order)
    {
        var factor = HairFactor(look);
        var colour = order < look.Grey ? look.GreyLock : look.HairColour;
        return new Vector3(Div(colour.X, factor.X), Div(colour.Y, factor.Y), Div(colour.Z, factor.Z));
        static float Div(float a, float b) => b > 1e-6f ? Math.Clamp(a / b, 0f, 1f) : 1f;
    }

    /// <summary><paramref name="plain"/> (the files as made) with <paramref name="look"/> laid on it, as <paramref name="recipe"/>.</summary>
    public static PersonModel Apply(PersonModel plain, PersonRecipe recipe, PersonLook look, Action<string>? say = null)
    {
        var watch = Stopwatch.StartNew();
        var model = plain.Model;
        var hair = HairFactor(look);
        int hairParts = 0, skinParts = 0;
        var parts = new ModelPart[model.Parts.Length];
        for (int p = 0; p < parts.Length; p++)
        {
            var part = model.Parts[p];
            if (part.Material is { } m && IsHair(m))
            {
                parts[p] = part with { Material = m with { BaseColorFactor = new Vector4(hair, m.BaseColorFactor.W) } };
                hairParts++;
            }
            else if (part.Material is { } s && IsSkin(s))
            {
                var f = s.BaseColorFactor;
                parts[p] = part with { Material = s with { BaseColorFactor = new Vector4(f.X * look.SkinTint.X, f.Y * look.SkinTint.Y, f.Z * look.SkinTint.Z, f.W) } };
                skinParts++;
            }
            else parts[p] = part;
        }

        // Grey locks: each hair vertex's colour carries its lock's colour over the factor.
        var colours = model.Colors;
        int grey = 0;
        if (look.Grey > 0f && plain.HairGrey is { } order && model.Colors is { } plainColours && order.Length == plainColours.Length)
        {
            colours = (Vector4[])plainColours.Clone();
            var dark = LockColour(look, float.PositiveInfinity);
            var light = LockColour(look, float.NegativeInfinity);
            for (int i = 0; i < colours.Length; i++)
            {
                if (float.IsNaN(order[i])) continue;
                bool isGrey = order[i] < look.Grey;
                var k = isGrey ? light : dark;
                if (isGrey) grey++;
                var c = colours[i];
                colours[i] = new Vector4(c.X * k.X, c.Y * k.Y, c.Z * k.Z, c.W);
            }
        }

        var dressed = new SkinnedModel
        {
            Name = "people:" + recipe.Key,
            Vertices = model.Vertices, Indices = model.Indices, Skeleton = model.Skeleton, Tangents = model.Tangents,
            Parts = parts, Colors = colours, BoundsMin = model.BoundsMin, BoundsMax = model.BoundsMax,
        };
        say?.Invoke(string.Create(CultureInfo.InvariantCulture,
            $"{recipe.Name} ({recipe.Key}): the look laid on {plain.Recipe.Key}: {hairParts} hair parts, {skinParts} skin parts, {grey} hair vertices grey, in {watch.Elapsed.TotalMilliseconds:0.0} ms (the mesh shared)"));
        return plain with { Recipe = recipe, Model = dressed };
    }
}
