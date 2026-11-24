// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The status bar's heads lit by their own two lights, the renderer's way.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **The portraits' own lights** (one routine writes them, another clears and re-adds them for every
/// head): a warm key (255, 226, 173) of intensity <b>500</b> at (−35, 26, 51) and a cool fill
/// (191, 226, 255) of intensity <b>200</b> at (26, 28, −28), both point lights, style 0. Their
/// fall-off is <b>never written</b> — the two records are in zeroed data — so
/// neither fades with distance. The light pass hands each to the renderer at its intensity times its style
/// (1 for style 0), and `rlDirect` lights every vertex of the head in software as it lights any model
/// (the point light, <see cref="ModelLight.Vertex"/>): the colour times
/// <c>(L·n / |L|) × I / 392</c> for each light the vertex faces; then the base — the model instance's,
/// which nothing writes for a head, so none — and the colour scaled down to fit where a channel is
/// past 255 (<see cref="ModelLight.Pack"/>). So the key alone, square to a face, is
/// 255 × 500 / 392 — past white — and a head is bright and warm where it faces the key and cool on
/// the fill's side.
/// </summary>
public static class HeadLighting
{
    /// <summary>The key light's intensity (the same number as its range).</summary>
    public const float KeyIntensity = 500f;
    /// <summary>The fill light's intensity (the same number as its range).</summary>
    public const float FillIntensity = 200f;

    /// <summary>The two lights as the renderer is handed them: point, no fall-off, this frame's intensity the set one.</summary>
    public static readonly PickedLight[] Lights =
    [
        Make(PartyHeads.Lights[0].At, PartyHeads.Lights[0].Colour, KeyIntensity, PartyHeads.Lights[0].Range),
        Make(PartyHeads.Lights[1].At, PartyHeads.Lights[1].Colour, FillIntensity, PartyHeads.Lights[1].Range),
    ];

    static PickedLight Make(Vector3 at, uint colour, float intensity, float range) =>
        new(0, new Vector3((colour >> 16) & 0xFF, (colour >> 8) & 0xFF, colour & 0xFF), at, Vector3.Zero,
            intensity, Falloff: 0f, range, 0f, 0, "head light", intensity, intensity);

    /// <summary>A head vertex's colour at <paramref name="position"/> with <paramref name="normal"/>, packed as a scene vertex carries it.</summary>
    public static uint Colour(Vector3 position, Vector3 normal) =>
        ModelLight.Pack(ModelLight.Vertex(Lights, position, normal, Vector3.Zero), Vector3.Zero);
}
