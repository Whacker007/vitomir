// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Vitomir.Formats.L2;

/// <summary>
/// **Ours: a cell level's date** (manifest key <c>date</c>). The
/// day of the year the level stands on, and the hour if the level asks for one: what the modern renderer's
/// sun follows (its path over Prague on that day), and so the sky, the cook's light probes and the
/// vegetation the generator chose for the season. The day is counted from 1 January as day 1 in today's
/// calendar, which is what places the sun: the equinox falls about day 80. (1141's own dates are Julian,
/// seven days behind: Julian 24 August 1141 is day 243 here.) The hour is local solar time, noon when the
/// sun is due south; null leaves it to the look (the morning look's 10:00).
/// A package without the key keeps the date every cell level stood on before it, <see cref="DefaultDay"/>
/// (mid-May); a build that does not know the key keeps it and ignores it.
/// </summary>
public sealed class L2Date
{
    /// <summary>The day a level without a date stands on: 135, 15 May (ours; the morning look's day).</summary>
    public const int DefaultDay = 135;

    /// <summary>The day of the year, 1 (1 January) to 365.</summary>
    [JsonPropertyName("day")] public int Day { get; set; } = DefaultDay;
    /// <summary>Local solar time, hours (10.0 is 10:00); null leaves it to the look.</summary>
    [JsonPropertyName("hour")] public float? Hour { get; set; }
    /// <summary>Properties this build does not know, kept for the write (a season's name, say).</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>The day clamped to 1..365, as the sun's code takes it.</summary>
    [JsonIgnore] public int DayOfYear => Math.Clamp(Day, 1, 365);

    /// <summary>The date a level's manifest gives, or <see cref="DefaultDay"/> with no hour when it gives none.</summary>
    public static L2Date Of(L2Manifest manifest) => manifest.Date ?? new L2Date();

    /// <summary>
    /// The date in a generator's <c>layout.json</c> (its <c>date</c>: <c>{"day": 243, "hour": 10, "season": "late_summer"}</c>,
    /// what else it says kept for the manifest), or null when it has none or the day is not a whole number from 1 to
    /// 365; an hour outside 0..24 is left to the look.
    /// </summary>
    public static L2Date? FromLayout(JsonNode? layout)
    {
        if (layout?["date"] is not JsonObject d) return null;
        L2Date? date;
        try { date = JsonSerializer.Deserialize<L2Date>(d, L2Manifest.Json); }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException) { return null; }
        if (date is null || d["day"] is null || date.Day < 1 || date.Day > 365) return null;
        if (date.Hour is { } h && (h < 0 || h > 24 || !float.IsFinite(h))) date.Hour = null;
        return date;
    }

    public override string ToString() => Hour is { } h ? $"day {Day}, {TimeSpan.FromMinutes(Math.Round(h * 60.0)):hh\\:mm}" : $"day {Day}";
}
