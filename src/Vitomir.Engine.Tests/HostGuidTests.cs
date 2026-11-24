// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A network game's host marks its GUIDs** (<see cref="ThingGuids.HostBit"/>). In net state 2
/// every number 1.1 hands out — the take, the peek and every allocation inlined
/// (…) — has 0x40000000 set, while the counter counts on bare (a save
/// writes it bare). A joining machine numbers for itself bare, so its own things never share a
/// number with the host's, and puts its copy of a scene on the host's (message 0x41) without
/// moving its own count. Single player never sets it (`GuidTests`).
/// </summary>
public class HostGuidTests
{
    static Location Convent(GameFiles files, bool host) =>
        Location.Load(files, "P1_CNVT", sky: false, scene: "CNVT_3_1", player: "Christof", entrance: 0,
                      guids: ThingGuids.For(host, Preloads.NewGame(Chronicle.Load(files, "Prague"), files)));

    [Fact]
    public void A_host_sets_the_bit_on_every_number_and_counts_on_bare()
    {
        using var files = Install.Open();
        var alone = Convent(files, host: false);
        var hosted = Convent(files, host: true);

        Assert.Equal(0x40000005, hosted.Player!.Thing.Guid);
        Assert.Equal((0x40000000 | 35, 0x40000000 | alone.SceneGuids.End), hosted.SceneGuids);   // the GUID counter's peek
        Assert.All(hosted.Things, t => Assert.Equal(ThingGuids.HostBit, t.Guid & ThingGuids.HostBit));
        Assert.Equal(alone.Things.Select(t => t.Guid).Order(), hosted.Things.Select(t => ThingGuids.Raw(t.Guid)).Order());
        Assert.Equal(alone.Guids.Next, hosted.Guids.Next);                      // the counter itself is bare
        Assert.DoesNotContain(hosted.Things, t => ThingGuids.IsPlaceholder(t.Guid));

        var made = hosted.Make("vitaerat")!;
        Assert.Equal(ThingGuids.HostBit | alone.Make("vitaerat")!.Guid, made.Guid);
        // A number in use moves the counter by its count, never onto the bit.
        hosted.Guids.Past(0x40000000 | 900);
        Assert.Equal(901, hosted.Guids.Next);
    }

    [Fact]
    public void A_host_s_save_keeps_the_bit_on_its_things_and_writes_the_counter_bare()
    {
        using var files = Install.Open();
        var convent = Convent(files, host: true);
        var prague = Chronicle.Load(files, "Prague");
        prague.Current = prague.Find("Convent");
        var session = new Session { Here = convent, Chronicle = prague };
        convent.Session = session;
        var save = SaveCapture.Take(convent, session);
        Assert.Equal(convent.Guids.Next, save.Session.ThingCounter);
        Assert.True(save.Session.ThingCounter < ThingGuids.HostBit);
        var numbers = convent.Things.Where(t => ThingIds.Of(convent, t) is not null).ToDictionary(t => ThingIds.Of(convent, t)!, t => t.Guid);

        var record = save.World[save.Header.Location];
        var back = Location.Load(files, record.Level, sky: false, scene: record.Scene, player: "Christof",
                                 entrance: record.Entrance, guids: ThingGuids.For(host: true, SaveGuids.ForLoad(save)));
        var restore = SaveApply.Place(back, record, new EffectDefinitions(files), new DisciplineDefinitions(files));
        SaveApply.Party(back, save.Party, restore);
        SaveGuids.Loaded(back, save);

        Assert.Equal(0x40000005, back.Player!.Thing.Guid);
        foreach (var t in back.Things)
            if (ThingIds.Of(back, t) is { } id && numbers.TryGetValue(id, out int was)) Assert.Equal(was, t.Guid);
        Assert.Equal(save.Session.ThingCounter, back.Guids.Next);
        Assert.True(back.Guids.Host);
    }

    [Fact]
    public void A_joining_machine_takes_the_host_s_numbers_and_keeps_its_own_count_bare()
    {
        using var files = Install.Open();
        var host = Convent(files, host: true);
        var mine = Location.Load(files, "P1_CNVT", sky: false, scene: "CNVT_3_1", player: "Christof", entrance: 0);
        int count = mine.Guids.Next;

        mine.RenumberScene(host.SceneGuids.First);                              // the host's numbers

        foreach (var t in mine.Things.Where(t => t.Index >= 0 && t != mine.Player?.Thing))
            Assert.Equal(host.Things.Single(h => h.Index == t.Index).Guid, t.Guid);
        Assert.Equal(host.SceneGuids, mine.SceneGuids);
        Assert.True(mine.Guids.Next < ThingGuids.HostBit, $"the joiner's count went to 0x{mine.Guids.Next:x}");
        Assert.True(mine.Guids.Next >= count);
        Assert.Equal(0, mine.Make("vitaerat")!.Guid & ThingGuids.HostBit); // its own things are bare
    }
}
