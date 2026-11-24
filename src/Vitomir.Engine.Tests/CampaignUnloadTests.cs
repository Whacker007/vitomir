// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Six places loaded, and no more** (`CampaignUnload.cs`; as 1.1's chronicle keeps
/// them). A seventh place loaded lets go of the one loaded longest ago — not the one
/// visited longest ago: a return to a place still loaded does not stamp it again — and the place let
/// go comes back on the next visit as it was left, read back from the record taken as it went.
/// Driven through Prague's small interiors with `Jump`, the script native that goes anywhere.
/// </summary>
public class CampaignUnloadTests
{
    /// <summary>Eight small places of Prague, the first where the party starts.</summary>
    static readonly string[] Tour = ["Smithy", "Unorna", "StThomas", "University", "SmithyDay", "UnornaDay", "Inn4StagsDay", "ConventDay"];

    static (Campaign Campaign, GameFiles Files) Open(string? startAt = null)
    {
        var files = Install.Open();
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        var chronicle = ChronicleSet.Load(files)["prague"]!;
        var session = new Session { Chronicle = chronicle };
        session.Messages.Text = new Strings(files);
        Location Build(string name, string? scene, int entrance)
        {
            var place = Location.Load(files, name, things: false, sky: false, scene: scene, player: "Christof", entrance: entrance);
            place.Session = session;
            return place;
        }
        Location BuildSaved(LocationSave record, int entrance)
        {
            var place = Build(record.Level, record.Scene.Length > 0 ? record.Scene : null, entrance);
            SaveApply.Place(place, record, effects, powers);
            return place;
        }
        int start = chronicle.Find(startAt ?? Tour[0]);
        Assert.True(start >= 0, $"Prague has no {startAt ?? Tour[0]}");
        chronicle.Current = start;
        var at = chronicle.At(start)!;
        var here = Build(at.LevelName, at.SceneName.Length > 0 ? at.SceneName : null, -1);
        session.Here = here;
        return (new Campaign { Chronicle = chronicle, Build = Build, BuildSaved = BuildSaved, Here = here }, files);
    }

    static Campaign.Arrival Go(Campaign campaign, string where)
    {
        Assert.True(campaign.Chronicle.Jump(where, 0), $"no {where}");
        return campaign.Travel() ?? throw new InvalidOperationException($"no journey to {where}");
    }

    [Fact]
    public void A_seventh_place_lets_the_first_go_and_the_next_visit_reads_it_back_as_it_was_left()
    {
        var (campaign, files) = Open();
        using var _ = files;
        var haven = campaign.Here;
        // Something happens in the smithy before the party leaves: a thing the scene placed is gone.
        var taken = haven.Things.First(t => !ReferenceEquals(t, haven.Player?.Thing) && ThingIds.Placed(haven, t) is not null);
        string takenId = ThingIds.Placed(haven, taken)!;
        haven.Remove(taken, drop: false);

        for (int i = 1; i < 6; i++) Go(campaign, Tour[i]);
        Assert.Equal(6, campaign.Resident.Count);
        Assert.Empty(campaign.LetGo);

        Go(campaign, Tour[6]);                                            // the seventh
        int havenIndex = campaign.Chronicle.Find(Tour[0]);
        Assert.Equal(6, campaign.Resident.Count);
        Assert.Equal([havenIndex], campaign.LetGo);
        Assert.False(campaign.Resident.ContainsKey(havenIndex));
        Assert.True(campaign.Parked.TryGetValue(havenIndex, out var record));
        Assert.Contains(takenId, record!.Removed);

        // Back to the smithy: built again from its record, the taken thing still gone — and loading it
        // made seven again, so the next earliest (Unorna's) goes.
        var back = Go(campaign, Tour[0]);
        Assert.True(back.Restored);
        Assert.False(back.Returned);
        Assert.DoesNotContain(back.To.Things, t => ThingIds.Placed(back.To, t) == takenId);
        Assert.Equal([havenIndex, campaign.Chronicle.Find(Tour[1])], campaign.LetGo);
        Assert.Equal(6, campaign.Resident.Count);
    }

    [Fact]
    public void The_place_loaded_longest_ago_goes_not_the_one_visited_longest_ago()
    {
        var (campaign, files) = Open();
        using var _ = files;
        for (int i = 1; i < 6; i++) Go(campaign, Tour[i]);
        // A return to the smithy, still loaded, and on to Unorna's: no load, no new stamp
        // (stamps only a place with no world) — so the smithy, visited more lately than four of the
        // others, is still the one loaded longest ago.
        Assert.True(Go(campaign, Tour[0]).Returned);
        Assert.True(Go(campaign, Tour[1]).Returned);
        Go(campaign, Tour[6]);
        Assert.Equal([campaign.Chronicle.Find(Tour[0])], campaign.LetGo);
    }

    [Fact]
    public void The_place_being_left_is_never_the_one_let_go()
    {
        var (campaign, files) = Open();
        using var _ = files;
        for (int i = 1; i < 6; i++) Go(campaign, Tour[i]);
        // Leave from the smithy itself, the earliest loaded: it is the place being left (the chronicle's
        // current location while the next is loaded), so the next earliest goes instead.
        Go(campaign, Tour[0]);
        Go(campaign, Tour[6]);
        Assert.Equal([campaign.Chronicle.Find(Tour[1])], campaign.LetGo);
        Assert.Contains(campaign.Chronicle.Find(Tour[0]), campaign.Resident.Keys);
        Assert.Equal(6, campaign.Resident.Count);
    }

    /// <summary>
    /// Prague's haven is marked never to be freed (`NeverFree`, 0x4 — the chronicle's own flags, as
    /// are London's bridge and Vienna's church), and the unload passes over it however long ago it
    /// was loaded.
    /// </summary>
    [Fact]
    public void The_haven_is_never_let_go()
    {
        var (campaign, files) = Open(startAt: "Haven");
        using var _ = files;
        for (int i = 0; i < 6; i++) Go(campaign, Tour[i]);
        Assert.Equal([campaign.Chronicle.Find(Tour[0])], campaign.LetGo);
        Assert.Contains(campaign.Chronicle.Find("Haven"), campaign.Resident.Keys);
    }

    /// <summary>
    /// A place's record taken while the party is away — let go, or saved while still loaded — has its
    /// scripts' clock put forward by the time away, and a timer already due is due now (1.1's file is
    /// from the leaving, and its read-back re-arms every timer at its old time).
    /// </summary>
    [Fact]
    public void A_record_taken_while_the_party_is_away_is_on_the_game_s_clock()
    {
        var record = new LocationSave { ScriptClockMs = 2000 };
        record.Timers.Add(new TimerSave { Script = "a", InMs = 500 });
        record.Timers.Add(new TimerSave { Script = "b", InMs = 5000 });
        AwayTimers.CatchUp(record, 1000);
        Assert.Equal(3000, record.ScriptClockMs);
        Assert.Equal([0, 4000], record.Timers.Select(t => t.InMs));
    }

    /// <summary>
    /// A parked record read back later is put forward by the time since it was taken (the read-back re-arms
    /// each timer at its own time on the one clock): what has passed is due at once, the rest keeps
    /// its time.
    /// </summary>
    [Fact]
    public void A_record_read_back_later_is_put_forward_by_the_time_since()
    {
        var record = new LocationSave { ScriptClockMs = 500, AsOfMs = 1000 };
        record.Timers.Add(new TimerSave { Script = "a", InMs = 200 });
        record.Timers.Add(new TimerSave { Script = "b", InMs = 5000 });
        AwayTimers.Since(record, 3000);
        Assert.Equal(2500, record.ScriptClockMs);
        Assert.Equal(3000, record.AsOfMs);
        Assert.Equal([0, 3000], record.Timers.Select(t => t.InMs));
    }
}

