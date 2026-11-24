// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Net;
using Vitomir.Engine.Save;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Where a hosted save starts** (`Net.NetSaves.Start`, as 1.1 does, and
/// which saves a host can reach). A network game loaded from a save starts at the
/// chronicle's revive location and entrance — the save's own when a script moved it — wherever the save
/// was taken, and builds that place from the save's record of it; the place it was taken in waits with
/// the rest. A single-player game's save is not hosted at all: 1.1's host sees only `SaveGames\MP<n>`.
/// A `--host --load SP3` (Prague, the monastery) had put the monastery's scene on the inn's level.
/// </summary>
public class HostedSaveTests
{
    const int Temesvar = 1, Cottage = 2;

    static SaveGame TakenAtTheCottage(Chronicle story)
    {
        var save = new SaveGame { Chronicle = new ChronicleSave { Name = story.Name, Current = story.NameOf(Cottage) } };
        save.Header.Description = "The cottage (00:10:00)";
        save.Header.Location = story.NameOf(Cottage);
        save.World[story.NameOf(Cottage)] = new LocationSave { Level = story.At(Cottage)!.LevelName, Entrance = 0 };
        return save;
    }

    [Fact]
    public void A_hosted_save_starts_at_the_chronicle_s_revive_location_not_where_it_was_taken()
    {
        using var files = Install.Open();
        var chronicles = ChronicleSet.Load(files);
        var story = chronicles["MP_CurseDarkness"]!;
        int revive = story.Data.ReviveLocation;
        var save = TakenAtTheCottage(story);
        save.World[story.NameOf(revive)] = new LocationSave { Level = story.At(revive)!.LevelName, Scene = "INN4_CTD_1_1", Entrance = 3 };

        var start = NetSaves.Start(save, chronicles, out _)!.Value;
        Assert.Equal(revive, start.Index);
        Assert.Equal(story.Data.ReviveEntrance, start.Entrance);
        Assert.Equal(story.At(revive)!.LevelName, start.Level, ignoreCase: true);
        Assert.Equal("INN4_CTD_1_1", start.Scene);                        // the scene the save remembers for it

        NetSaves.AtRevive(save, start);
        Assert.Equal(story.NameOf(revive), save.Header.Location);
        Assert.Equal(story.NameOf(revive), save.Chronicle!.Current);        // the arrival moves the current location
        Assert.Equal(story.Data.ReviveEntrance, save.World[story.NameOf(revive)].Entrance);   // built at the revive entrance
        Assert.True(save.World.ContainsKey(story.NameOf(Cottage)));        // the place it was taken in waits

        var settings = NetSaves.Settings(new GameSettings { Name = "Hosta's game" }, save, start);
        Assert.Equal("Hosta's game", settings.Name);
        Assert.Equal(story.Name, settings.Chronicle);
        Assert.Equal(start.Level, settings.Level);
    }

    [Fact]
    public void A_revive_location_a_script_moved_is_the_save_s_own()
    {
        using var files = Install.Open();
        var chronicles = ChronicleSet.Load(files);
        var story = chronicles["MP_CurseDarkness"]!;
        var save = TakenAtTheCottage(story);
        save.Chronicle!.Revive = new PlaceRef { Location = story.NameOf(Temesvar), Entrance = 1 };
        var start = NetSaves.Start(save, chronicles, out _)!.Value;
        Assert.Equal(Temesvar, start.Index);
        Assert.Equal(1, start.Entrance);
        Assert.Equal(story.At(Temesvar)!.LevelName, start.Level, ignoreCase: true);   // not remembered: the chronicle's own
    }

    [Fact]
    public void A_single_player_save_is_not_hosted()
    {
        using var files = Install.Open();
        var chronicles = ChronicleSet.Load(files);
        var save = new SaveGame { Chronicle = new ChronicleSave { Name = "Prague", Current = "Monastery" } };
        save.Header.Description = "Monastery 1 (01:28:05)";
        save.Header.Location = "Monastery";
        Assert.Null(NetSaves.Start(save, chronicles, out string why));
        Assert.Contains("single-player", why);
    }
}
