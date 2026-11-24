// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A model's shell worked out from the engine's own objects (the data, ShellDraw, is the Classic snapshot's).
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Scene;

/// <summary>
/// The engine's half of <see cref="ShellDraw"/> (earlier, in the type itself): which shell pass a model's
/// <see cref="ModelFx"/> asks for, from the engine's materials. Static extension members, so <c>ShellDraw.For</c> and
/// <c>ShellDraw.HidesModel</c> read as they always did.
/// </summary>
public static class ShellDraws
{
    extension(ShellDraw)
    {
        /// <summary>
        /// The shell pass for a model whose shell is <paramref name="fx"/> and whose own material is
        /// <paramref name="model"/>, or null when there is none to draw: no shell, a shell at exactly
        /// 0 (compared with 0.0), or a material whose layer 0 has nothing to draw with — a
        /// <c>pass</c> list without a <c>pass 0</c>, or a <c>pass 0</c> of the lightmap.
        /// </summary>
        public static ShellDraw? For(ModelFx? fx, LoadedMaterial shell, LoadedMaterial? model)
        {
            if (fx is not { Shell: not null } || fx.ShellAlpha == 0f) return null;
            RenderKind kind;
            int image;
            Vector2 slide;
            bool noMip = false;
            if (shell.Passes.Length > 0)
            {
                if (Array.FindIndex(shell.Passes, p => p.Number == 0) is var at && at < 0) return null;
                var p0 = shell.Passes[at];
                if (p0.Image < 0 || p0.Kind == RenderKind.None) return null;
                (kind, image, slide, noMip) = (p0.Kind, p0.Image, p0.Slide, p0.NoMip);
            }
            else
            {
                kind = DrawPlan.ForParticle(shell.Traits);
                image = shell.CurrentImage;
                slide = SlideOf(shell);
            }
            if (model is not null && !HidesModel(fx)) slide += SlideOf(model);
            return new ShellDraw(kind, image, slide, ShellDraw.AlphaOf(fx.ShellAlpha), noMip);
        }

        /// <summary>
        /// Whether the model's own pass is left out: the shell's bit 0x80, which
        /// <c>SetShell</c> sets on the model and <c>EndShell</c> clears.
        /// </summary>
        public static bool HidesModel(ModelFx? fx) => fx is not null && (fx.ShellFlags & ModelFx.ShellBit80) != 0;
    }

    /// <summary>A material's layer-0 slide (<c>slide 0</c>, flag 0x1000000), or nothing.</summary>
    public static Vector2 SlideOf(LoadedMaterial m) =>
        m.Nam.Slides.TryGetValue(0, out var s) ? new Vector2(s.Du, s.Dv) : Vector2.Zero;
}
