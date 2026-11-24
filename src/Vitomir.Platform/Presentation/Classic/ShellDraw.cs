// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// How a model's shell is drawn — `SetShell`'s glow over a body — worked out from `rlDirect.dll`.
using System.Numerics;

namespace Vitomir.Platform.Presentation.Classic;

/// <summary>
/// **A model's shell** (a data type of the Classic snapshot; the engine works it out, Engine.Scene's
/// <c>ShellDraws</c>) as `rlDirect.dll` draws it: the second of a model's passes (each pass set up
/// in turn and drawn over the same transformed vertices). It is **the same triangles again**, straight after the model's own: no inflating
/// and no generated coordinates — the model's own texture coordinates, with the shell material's
/// texture, in the shell material's **second** mode (the layer's second: a <c>pass 0</c> line's
/// mode when the material has one, as every shipped shell does — <c>full_add</c> — and otherwise
/// <see cref="DrawPlan.ForParticle"/>), and **every vertex's alpha replaced by the shell's**. The
/// colour the vertices already carry — the model's light — stays.
///
/// Two things carry over from the passes sharing one vertex list, which each pass edits in place:
/// the shell's texture coordinates are slid by the shell material's slide **and** by the model's
/// own layer-0 slide when the model's own pass ran first; and a shell with bit 0x80 has no model
/// pass before it, which is how a `SetShell` with 0x80 shows the shell in place of
/// the body.
/// </summary>
/// <param name="Kind">The mode the shell is drawn in.</param>
/// <param name="Image">Which of the shell material's images: <c>pass 0</c>'s, or the current frame.</param>
/// <param name="Slide">Texture coordinates per second to slide by, the shell's and the model's together.</param>
/// <param name="Alpha">The alpha every vertex gets.</param>
/// <param name="NoMip">The <c>pass 0</c> line said <c>nomip</c>.</param>
public readonly record struct ShellDraw(RenderKind Kind, int Image, Vector2 Slide, float Alpha, bool NoMip)
{
    /// <summary>
    /// The alpha a vertex gets: the shell's times 255, truncated toward zero and put in the
    /// colour's top byte by a shift that keeps only the low eight bits — so an alpha set above 1
    /// with no ramp, which `SetShell` does not clamp, wraps: 2 is 254/255, 3 is 253/255.
    /// </summary>
    public static float AlphaOf(float alpha) => ((int)(alpha * 255f) & 0xFF) / 255f;
}
