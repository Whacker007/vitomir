// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The character's flags in the join**. 1.1's join carries the
/// actor flags (written after the four names) and the host sets them whole on the joiner
/// `flags = …`), so what Create set on the character (`FASTDRINKER` 0x800 for a Giovanni,
/// `HUMANITYTRACK` 0x8000 for everybody) reaches the game. These make a character the way Create does
/// (<see cref="MpMenus.NewBody"/>), keep it in the crypt as a `.vplayer`, join a host on localhost with it and look
/// at the host's character; and read a `.vplayer` written before the field, which keeps its template's flags. The
/// second word goes the same way, stale state bits and all (*"Yes,
/// faithful."*).
/// </summary>
public class NetJoinFlagsTests
{
    static bool Until(Func<bool> done, params Action[] turns)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 20000)   // a hang guard, not a speed test (levels fork, 4 s was too few on a busy laptop)
        {
            foreach (var t in turns) t();
            if (done()) return true;
            Thread.Sleep(1);
        }
        return done();
    }

    /// <summary>A character made on Create and written as its advancement leaves it, read back from the crypt.</summary>
    static NetCharacter Created(GameFiles files, string name, string clan)
    {
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        string folder = Path.Combine(Path.GetTempPath(), "vitomir-crypt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var mp = new MpMenus(menus, files, text.Find, folder) { Port = 0, Upnp = false };
            var made = new NetCharacter { Name = name, Template = "MP_Serena", Type = ActorType.Vampire, Clan = clan };
            var body = mp.NewBody(made, new DisciplineDefinitions(files))!;
            var crypt = new Crypt(folder);
            crypt.Save(made with { Saved = NetSheet.From(body) with { Kit = true, Effects = null } });   // as CreateAdvancement's Finish
            return crypt.All().Single(c => c.Name == name);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A host of MP_DAInn with <paramref name="character"/> joined; answers the host's character for her.</summary>
    static WorldThing Joined(GameFiles files, NetCharacter character)
    {
        var inn = ChronicleSet.Load(files)["MP_DAInn"]!;
        var settings = GameSettings.For(inn, "Anna's game", GameFlags.AutoStoryteller);
        var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
        place.Session = new Session { Here = place };
        using var server = new GameServer(settings, new NetPeer(0)) { Qol = new Qol(), Fixes = new Fixes() };
        server.Start();
        server.Enter(place, null);
        var sim = new Simulation(place);
        using var client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, server.Peer.Port), character);
        Location? mine = null;
        client.Started += () =>
        {
            var s = client.Settings!;
            mine = Location.Load(files, s.Level, scene: s.Scene.Length > 0 ? s.Scene : null, player: character.Template, entrance: s.Entrance);
            mine.Session = new Session { Here = mine };
            client.Enter(mine, mine.Player?.Thing);
        };
        int ms = 0;
        void All() { sim.Step(1f / 30); ms += 33; server.Poll(ms); client.Poll(1f / 30); }
        Assert.True(Until(() => server.Players.Any(p => p.Name == character.Name && p.Avatar is not null), All), "she never came in");
        return server.Players.Single(p => p.Name == character.Name).Avatar!;
    }

    [Fact]
    public void A_Giovanni_made_on_Create_joins_with_fast_drinker_and_humanity_track()
    {
        using var files = Install.Open();
        var bela = Created(files, "Bela", "giovanni");
        Assert.NotNull(bela.Saved!.Flags);
        Assert.Equal(Actor.Flag.FastDrinker | Actor.Flag.HumanityTrack,
                     bela.Saved.Flags!.Value & (Actor.Flag.FastDrinker | Actor.Flag.HumanityTrack));

        var there = Joined(files, bela).Actor!;
        Assert.True(there.Has(Actor.Flag.FastDrinker));
        Assert.True(there.Has(Actor.Flag.HumanityTrack));                         // 0x8000
    }

    [Fact]
    public void Any_other_clan_joins_with_humanity_track_and_without_fast_drinker()
    {
        using var files = Install.Open();
        var there = Joined(files, Created(files, "Anezka", "brujah")).Actor!;
        Assert.True(there.Has(Actor.Flag.HumanityTrack));
        Assert.False(there.Has(Actor.Flag.FastDrinker));
    }

    [Fact]
    public void A_vplayer_written_before_the_flags_were_kept_joins_with_its_template_s()
    {
        using var files = Install.Open();
        var old = Crypt.Parse("{ \"name\": \"Old\", \"template\": \"MP_Serena\", \"clan\": \"giovanni\", \"saved\": "
                              + "{ \"baseStats\": [], \"stats\": [], \"health\": 50, \"maxHealth\": 50, \"cash\": 200, \"kit\": true } }")!;
        Assert.Null(old.Saved!.Flags);
        var inn = GameSettings.For(ChronicleSet.Load(files)["MP_DAInn"]!, "x", GameFlags.AutoStoryteller);
        var template = Location.Load(files, inn.Level, scene: inn.Scene.Length > 0 ? inn.Scene : null, entrance: inn.Entrance)
                               .Make("MP_Serena")!.Actor!.Flags;
        var there = Joined(files, old).Actor!;
        Assert.Equal(template & Actor.Flag.FastDrinker, there.Flags & Actor.Flag.FastDrinker);
        Assert.Equal(template & Actor.Flag.HumanityTrack, there.Flags & Actor.Flag.HumanityTrack);
        Assert.False(there.Has(Actor.Flag.FastDrinker));                          // the template has none
    }

    [Fact]
    public void The_second_flags_word_is_kept_in_the_crypt_with_the_first()
    {
        using var files = Install.Open();
        var bela = Created(files, "Bela", "giovanni");
        Assert.NotNull(bela.Saved!.Flags2);                                      // written as `flags2` and read back
        var text = Crypt.Parse("{ \"name\": \"Vera\", \"template\": \"MP_Serena\", \"clan\": \"brujah\", \"saved\": "
                               + "{ \"baseStats\": [], \"stats\": [], \"health\": 50, \"maxHealth\": 50, \"flags2\": 268435466 } }")!;
        Assert.Equal(0x1000000Au, text.Saved!.Flags2);
    }

    [Fact]
    public void The_second_flags_word_is_set_whole_on_the_joiner_stale_state_bits_and_all()
    {
        using var files = Install.Open();
        var made = Created(files, "Vera", "brujah");
        // Limping, in a conversation, speaking and the host's "a base stat changed" — state a character written back
        // mid-game can carry — and none of the template's own.
        const uint stale = Actor.Flag2.Limping | Actor.Flag2.Conversation | Actor.Flag2.Speaking | 0x10000000;
        var there = Joined(files, made with { Saved = made.Saved! with { Flags2 = stale } }).Actor!;
        Assert.Equal(stale, there.Flags2 & ~GameServer.JoinedFlag2);               // `flags2 = …`, then `|= 0x400`
    }

    [Fact]
    public void A_vplayer_written_before_the_second_word_was_kept_joins_with_its_template_s()
    {
        using var files = Install.Open();
        var old = Crypt.Parse("{ \"name\": \"Old\", \"template\": \"MP_Serena\", \"clan\": \"brujah\", \"saved\": "
                              + "{ \"baseStats\": [], \"stats\": [], \"health\": 50, \"maxHealth\": 50, \"cash\": 200, \"kit\": true, "
                              + "\"flags\": 32768 } }")!;
        Assert.Null(old.Saved!.Flags2);
        var inn = GameSettings.For(ChronicleSet.Load(files)["MP_DAInn"]!, "x", GameFlags.AutoStoryteller);
        var template = Location.Load(files, inn.Level, scene: inn.Scene.Length > 0 ? inn.Scene : null, entrance: inn.Entrance)
                               .Make("MP_Serena")!.Actor!.Flags2;
        var there = Joined(files, old).Actor!;
        Assert.Equal(template & ~GameServer.JoinedFlag2, there.Flags2 & ~GameServer.JoinedFlag2);
    }
}
