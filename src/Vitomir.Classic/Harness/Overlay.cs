// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The diagnostic overlay laid out: text and flat quads for the finished frame (its laying-out half; its drawing half
// is the host's renderer's, DeveloperOverlay).
//
// This is a developer tool and nothing else. It is drawn **after** the presenter, so the game's
// brightness curve does not touch it and what is on screen is what was asked for; it has its own
// plain font (OverlayFont) so it can never be mistaken for the HUD being rebuilt beside it; and
// the engine knows nothing about it - `Vitomir.Engine.Diagnostics.Watch` produces the words and
// the world positions, and this file is the only thing that knows they end up as triangles.
using System.Numerics;
using Vitomir.Engine.Ui;

namespace Vitomir.Classic;

/// <summary>Lays the overlay out: filled every frame into its <see cref="Quads"/>, which the host's renderer draws.</summary>
public sealed class Overlay
{
    /// <summary>How many screen pixels one font pixel covers. The overlay is small on purpose.</summary>
    public int Scale { get; set; } = 2;

    /// <summary>A character cell, at the current <see cref="Scale"/>: the glyph plus one pixel of gap.</summary>
    public int CellWidth => (OverlayFont.Width + 1) * Scale;
    public int CellHeight => (OverlayFont.Height + 2) * Scale;

    /// <summary>The atlas is the 95 printable characters in one row.</summary>
    const int Count = OverlayFont.Last - OverlayFont.First + 1;

    /// <summary>What was queued since <see cref="Begin"/>: the frame's quads, for the host's renderer.</summary>
    public ClassicOverlayQuads Quads { get; } = new();

    List<OverlayVertex> vertices => Quads.Vertices;

    /// <summary>Starts a frame. Everything queued before the last draw is forgotten.</summary>
    public void Begin(int surfaceWidth, int surfaceHeight)
    {
        Quads.Width = Math.Max(1, surfaceWidth);
        Quads.Height = Math.Max(1, surfaceHeight);
        vertices.Clear();
    }

    /// <summary>A filled rectangle, in pixels from the top left.</summary>
    public void Box(float x, float y, float w, float h, Tint tint) => Quad(x, y, w, h, -1f, -1f, -1f, -1f, tint);

    /// <summary>
    /// One line of text at (<paramref name="x"/>, <paramref name="y"/>), the top-left of the first
    /// cell. Returns where the next character would start, so callers can colour a line in pieces.
    /// </summary>
    public float Text(float x, float y, string text, Tint tint)
    {
        // Streamer mode: an address, the user's folder or a secret is hidden on the overlay too.
        foreach (char c in Vitomir.Platform.Presentation.StreamerMode.Shown(text))
        {
            if (c != ' ')
            {
                int i = c >= OverlayFont.First && c <= OverlayFont.Last ? c - OverlayFont.First : '?' - OverlayFont.First;
                float u0 = i * OverlayFont.Width / (float)(Count * OverlayFont.Width);
                float u1 = (i + 1) * OverlayFont.Width / (float)(Count * OverlayFont.Width);
                Quad(x, y, OverlayFont.Width * Scale, OverlayFont.Height * Scale, u0, 0f, u1, 1f, tint);
            }
            x += CellWidth;
        }
        return x;
    }

    /// <summary>How wide <paramref name="text"/> comes out at the current scale.</summary>
    public float Measure(string text) => Vitomir.Platform.Presentation.StreamerMode.Shown(text).Length * CellWidth;

    /// <summary>
    /// A block of lines with a dark plate behind it, which is what makes text readable over a
    /// level. Returns the plate's rectangle so a caller can lay out around it.
    /// </summary>
    public (float X, float Y, float W, float H) Panel(float x, float y, IReadOnlyList<string> lines,
                                                      Tint tint, Tint? plate = null, float alpha = 1f)
    {
        float pad = 3f * Scale;
        float w = 0f;
        foreach (var line in lines) w = Math.Max(w, Measure(line));
        float h = lines.Count * CellHeight;
        Box(x - pad, y - pad, w + pad * 2, h + pad * 2, (plate ?? Tint.Backdrop).Fade(alpha));
        for (int i = 0; i < lines.Count; i++) Text(x, y + i * CellHeight, lines[i], tint.Fade(alpha));
        return (x - pad, y - pad, w + pad * 2, h + pad * 2);
    }

    /// <summary>A left-to-right bar - health, blood - with its own plate and a fill fraction.</summary>
    public void Bar(float x, float y, float w, float h, float fraction, Tint tint)
    {
        Box(x, y, w, h, Tint.Backdrop);
        float f = Math.Clamp(fraction, 0f, 1f);
        if (f > 0f) Box(x + Scale, y + Scale, Math.Max(Scale, (w - Scale * 2) * f), h - Scale * 2, tint);
    }

    void Quad(float x, float y, float w, float h, float u0, float v0, float u1, float v1, Tint t)
    {
        var c = new Vector4(t.R, t.G, t.B, t.A);
        var a = new OverlayVertex { Pos = new Vector2(x, y), Uv = new Vector2(u0, v0), Colour = c };
        var b = new OverlayVertex { Pos = new Vector2(x + w, y), Uv = new Vector2(u1, v0), Colour = c };
        var d = new OverlayVertex { Pos = new Vector2(x, y + h), Uv = new Vector2(u0, v1), Colour = c };
        var e = new OverlayVertex { Pos = new Vector2(x + w, y + h), Uv = new Vector2(u1, v1), Colour = c };
        vertices.Add(a); vertices.Add(b); vertices.Add(d);
        vertices.Add(b); vertices.Add(e); vertices.Add(d);
    }
}
