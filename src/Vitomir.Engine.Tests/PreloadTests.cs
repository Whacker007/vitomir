// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The places a chronicle loads before its first** (`CampaignPreloads.cs`). 1.1's chronicle start
/// loads every location flagged 0x80 before it enters the first (each loaded, then
/// the arrival) — Prague's haven at a new game, London's bridge at the change to London — and the first
/// visit enters the place already built: no plaque, nothing built or counted, the numbers the start
/// gave it. A save leaves it out until it has a file, as a new-game save `SP5` has no haven,
/// and letting it go leaves nothing to read back. The numbers stay as they were: Christof 5, the haven 6 to
/// 34, the convent's first thing 35.
/// </summary>
public class PreloadTests
{
    sealed record Game(Campaign Campaign, Location Convent, Session Session, Chronicle Prague, GameFiles Files) : IDisposable
    {
        public void Dispose() => Files.Dispose();
        public int Haven => Prague.Find("Haven");
    }

    /// <summary>A new game as `--new-game` starts it: the convent on the chronicle's counter, the campaign round it.</summary>
    static Game NewGame()
    {
        var files = Install.Open();
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        var prague = ChronicleSet.Load(files)["prague"]!;
        var session = new Session { Chronicle = prague };
        session.Messages.Text = new Strings(files);
        prague.Current = prague.Find("Convent");
        var convent = Location.Load(files, "P1_CNVT", sky: false, scene: "CNVT_3_1", player: "Christof", entrance: 0,
                                    guids: Preloads.NewGame(prague, files));
        convent.Session = session;
        session.Here = convent;
        Location Build(string name, string? scene, int entrance)
        {
            var place = Location.Load(files, name, sky: false, scene: scene, player: "Christof", entrance: entrance);
            place.Session = session;
            return place;
        }
        Location BuildSaved(LocationSave record, int entrance)
        {
            var place = Build(record.Level, record.Scene.Length > 0 ? record.Scene : null, entrance);
            SaveApply.Place(place, record, effects, powers);
            return place;
        }
        var campaign = new Campaign { Chronicle = prague, Build = Build, BuildSaved = BuildSaved, Here = convent };
        session.Teleports.Campaign = campaign;
        return new Game(campaign, convent, session, prague, files);
    }

    static Campaign.Arrival Go(Campaign campaign, string where)
    {
        Assert.True(campaign.Chronicle.Jump(where, 0), $"no {where}");
        return campaign.Travel() ?? throw new InvalidOperationException($"no journey to {where}");
    }

    [Fact]
    public void A_new_game_loads_the_haven_before_the_convent_on_the_numbers_1_1_gives_it()
    {
        using var g = NewGame();
        int counter = g.Convent.Guids.Next;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var early = g.Campaign.Preload(g.Prague.Current);
        sw.Stop();

        var haven = Assert.Single(early);
        Assert.Equal(17, g.Haven);
        Assert.Same(haven, g.Campaign.Resident[g.Haven]);
        Assert.Equal("HAVN_7_7", haven.SceneFile, ignoreCase: true);
        Assert.True(g.Campaign.Unentered(g.Haven));
        // The numbers the start set aside: 6 to 34, after Christof (5) and before the convent (35, SP5).
        Assert.Equal((6, 35), haven.SceneGuids);
        Assert.Equal(35, g.Convent.SceneGuids.First);
        Assert.All(haven.Things.Where(t => t != haven.Player?.Thing), t => Assert.InRange(t.Guid, 6, 34));
        Assert.Same(g.Convent.Guids, haven.Guids);
        Assert.Equal(counter, g.Convent.Guids.Next);                            // the counter is not touched
        Assert.Empty(g.Convent.Guids.Reserved);
        // Nobody of the party is in it: the stand-in is on Christof's number, out of the world.
        Assert.Equal(5, haven.Player!.Thing.Guid);
        Assert.False(haven.Player.Thing.InWorld);
        // Its scripts are handed over once.
        Assert.Same(haven, Assert.Single(g.Campaign.TakePreloaded()));
        Assert.Empty(g.Campaign.TakePreloaded());
        Console.WriteLine($"preload: the haven took {sw.ElapsedMilliseconds} ms, {haven.Things.Count} things");
    }

    [Fact]
    public void The_first_visit_enters_the_haven_already_built()
    {
        using var g = NewGame();
        var haven = g.Campaign.Preload(g.Prague.Current).Single();
        var numbers = haven.Things.ToDictionary(t => t.Index, t => t.Guid);

        Assert.True(g.Prague.Jump("Haven", 0));
        Assert.False(g.Campaign.Builds);                                        // no plaque: it has a world
        var arrival = g.Campaign.Travel()!.Value;

        Assert.True(arrival.Returned);
        Assert.Same(haven, arrival.To);
        Assert.Same(haven, g.Campaign.Here);
        Assert.Equal(5, haven.Player!.Thing.Guid);
        Assert.True(haven.Player.Thing.InWorld);
        Assert.Empty(g.Campaign.LetGo);
        Assert.All(haven.Things.Where(t => t.Index >= 0 && t != haven.Player.Thing), t => Assert.Equal(numbers[t.Index], t.Guid));
        Assert.True(g.Campaign.Unentered(g.Haven));                             // no file until the party leaves it
    }

    [Fact]
    public void A_save_leaves_the_haven_out_until_it_has_a_file()
    {
        using var g = NewGame();
        g.Campaign.Preload(g.Prague.Current);

        // As SP5: the convent and nothing of the haven, and no numbers set aside any more.
        var first = SaveCapture.Take(g.Convent, g.Session, g.Campaign);
        Assert.DoesNotContain("Haven", first.World.Keys);
        Assert.Null(first.Session.ReservedGuids);

        // In it, it is the place the save is taken in; left, it has its file.
        Go(g.Campaign, "Haven");
        Assert.Contains("Haven", SaveCapture.Take(g.Campaign.Here, g.Session, g.Campaign).World.Keys);
        Go(g.Campaign, "ConventDay");
        Assert.False(g.Campaign.Unentered(g.Haven));
        Assert.Contains("Haven", SaveCapture.Take(g.Campaign.Here, g.Session, g.Campaign).World.Keys);
    }

    [Fact]
    public void An_early_place_let_go_before_it_was_entered_is_built_fresh_on_the_first_visit()
    {
        using var g = NewGame();
        // No shipped early place can be let go (each is flagged never to be freed); this one is made so.
        var at = g.Prague.At(g.Haven)!;
        at.Flags &= ~(uint)NscLocationFlags.NeverFree;
        g.Campaign.KeepLoaded = 1;
        var haven = g.Campaign.Preload(g.Prague.Current).Single();

        Go(g.Campaign, "ConventDay");
        Assert.Contains(g.Haven, g.Campaign.LetGo);
        Assert.False(g.Campaign.Parked.ContainsKey(g.Haven));                   // no file: nothing to read back

        int counter = g.Campaign.Here.Guids.Next;
        var again = Go(g.Campaign, "Haven");
        Assert.False(again.Returned);
        Assert.False(again.Restored);
        Assert.NotSame(haven, again.To);
        Assert.Equal(counter, again.To.SceneGuids.First);                       // numbered from the counter then (no file was found)
    }

    [Fact]
    public void A_chronicle_changed_to_loads_its_early_places_on_the_numbers_after_the_party()
    {
        using var g = NewGame();
        g.Campaign.Preload(g.Prague.Current);
        g.Campaign.TakePreloaded();
        var london = ChronicleSet.Load(g.Files)["london"]!;
        int counter = g.Convent.Guids.Next;

        var change = g.Campaign.ChangeChronicle(london)!.Value;

        int bridge = london.Find("Bridge");
        Assert.Equal(9, bridge);
        var place = g.Campaign.Resident[bridge];
        Assert.True(g.Campaign.Unentered(bridge));
        Assert.Same(place, Assert.Single(g.Campaign.TakePreloaded()));
        Assert.Equal([0, bridge], g.Campaign.Resident.Keys.Order());         // Prague's haven went with Prague
        // The start's order: the bridge's numbers are set aside first, then the start's.
        Assert.Equal(counter, place.SceneGuids.First);
        Assert.Equal(place.SceneGuids.End, change.To.SceneGuids.First);
        Assert.Equal(change.To.Player!.Thing.Guid, place.Player!.Thing.Guid);
        Assert.False(place.Player.Thing.InWorld);
    }
}
