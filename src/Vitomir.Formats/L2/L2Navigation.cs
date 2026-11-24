// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vitomir.Formats.L2;

/// <summary>
/// How a cell level's navigation mesh was baked, where the cook did not do it Recast's own way (ours,
/// the navigation's agent): the manifest's <c>navigation</c>, absent when nothing differs. The
/// agent itself stays in every cell's <c>NAVM</c>. A build that does not know the key keeps it and ignores it.
/// </summary>
public sealed class L2Navigation
{
    /// <summary>
    /// <see cref="Ledges"/>'s word for the cook's stairs' ledge filter: a span is a ledge where a neighbour drops more
    /// than the climb (Recast's test) and where its accessible neighbours spread more than <b>twice</b> the climb
    /// (Recast's is once), so the treads of steep stairs stay ground. The cook uses it with <c>--nav-agent</c>.
    /// </summary>
    public const string StairLedges = "stairs";

    /// <summary>The ledge filter the mesh was baked with: <see cref="StairLedges"/>, or null for Recast's own.</summary>
    [JsonPropertyName("ledges")] public string? Ledges { get; set; }

    /// <summary>Properties this build does not know, kept for the write.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }
}
