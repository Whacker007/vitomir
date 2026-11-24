// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json.Nodes;
using Vitomir.Formats.L2;

namespace Vitomir.Formats.Tests;

/// <summary>
/// A cell level's date (ours; <see cref="L2Date"/>): the manifest's
/// <c>date</c> written and read back with what this build does not know kept, a manifest without one standing
/// on mid-May (day 135) as every cell level did before, and the generator's layout giving it to the cook.
/// </summary>
public class L2DateTests
{
    [Fact]
    public void A_manifests_date_comes_back_as_written()
    {
        var m = new L2Manifest { Version = L2Manifest.Version2, Name = "oldtown", Date = new L2Date { Day = 243, Hour = 10f } };
        var text = m.Write();
        Assert.Contains("\"date\"", text);
        var back = L2Manifest.Read(text);
        Assert.Equal(243, back.Date!.Day);
        Assert.Equal(10f, back.Date.Hour);
        Assert.Equal("day 243, 10:00", back.Date.ToString());

        // What a later build adds to the date (a season's name, say) is kept through a read and a write.
        var later = L2Manifest.Read(text.Replace("\"day\": 243", "\"day\": 243, \"season\": \"late_summer\""));
        Assert.Contains("\"season\": \"late_summer\"", later.Write());
        Assert.Equal(243, L2Manifest.Read(later.Write()).Date!.Day);
    }

    [Fact]
    public void A_manifest_without_a_date_stands_on_mid_May_with_the_hour_left_to_the_look()
    {
        var old = new L2Manifest { Version = L2Manifest.Version2, Name = "round9" };
        var text = old.Write();
        Assert.DoesNotContain("\"date\"", text);                  // nothing written when there is none
        var back = L2Manifest.Read(text);
        Assert.Null(back.Date);
        var date = L2Date.Of(back);
        Assert.Equal(135, L2Date.DefaultDay);
        Assert.Equal(135, date.Day);
        Assert.Null(date.Hour);

        // A date without an hour keeps the hour to the look.
        var dayOnly = L2Manifest.Read(text.Replace("\"name\": \"round9\"", "\"name\": \"round9\", \"date\": { \"day\": 20 }"));
        Assert.Equal((20, (float?)null), (L2Date.Of(dayOnly).Day, L2Date.Of(dayOnly).Hour));
    }

    [Fact]
    public void The_generators_layout_gives_the_date_and_a_layout_without_a_sensible_one_gives_none()
    {
        var layout = JsonNode.Parse("""{ "level": "P1_OTDY", "date": { "day": 243, "hour": 10.0, "season": "late_summer" } }""");
        var date = L2Date.FromLayout(layout)!;
        Assert.Equal((243, (float?)10f), (date.Day, date.Hour));
        var manifest = new L2Manifest { Version = L2Manifest.Version2, Name = "oldtown", Date = date }.Write();
        Assert.Contains("\"season\": \"late_summer\"", manifest);      // the layout's season goes on into the manifest
        Assert.DoesNotContain("DayOfYear", manifest);
        var dayOnly = L2Date.FromLayout(JsonNode.Parse("""{ "date": { "day": 20 } }"""))!;
        Assert.Equal((20, (float?)null), (dayOnly.Day, dayOnly.Hour));
        Assert.Null(L2Date.FromLayout(JsonNode.Parse("""{ "level": "P1_OTDY" }""")));
        Assert.Null(L2Date.FromLayout(JsonNode.Parse("""{ "date": { "day": 0 } }""")));
        Assert.Null(L2Date.FromLayout(JsonNode.Parse("""{ "date": { "day": 400 } }""")));
        Assert.Null(L2Date.FromLayout(JsonNode.Parse("""{ "date": "late summer" }""")));
        Assert.Null(L2Date.FromLayout(JsonNode.Parse("""{ "date": { "day": 243.5 } }""")));
        Assert.Null(L2Date.FromLayout(JsonNode.Parse("""{ "date": { "hour": 10 } }""")));
        Assert.Null(L2Date.FromLayout(null));
        Assert.Null(L2Date.FromLayout(JsonNode.Parse("""{ "date": { "day": 200, "hour": 30 } }"""))!.Hour);   // a nonsense hour is the look's
    }
}
