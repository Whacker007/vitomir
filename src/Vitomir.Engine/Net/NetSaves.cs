// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a network game's save leaves out. 1.1's filter of the
// things a place writes drops every player-type thing while a network session
// is up (the flag that also puts the save in `SaveGames\MP<n>`): no player's character
// is in a network save's worlds, and each comes back with its player.
using Vitomir.Engine.Save;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Net;

public static class NetSaves
{
    /// <summary>
    /// Whether a thing of a place is left out of the save because the game is a network one (type
    /// 7, `THING_TYPE_PLAYER`, while a network session is up): every player's character — the host's own
    /// goes as the party, as in single player (the writer's other type-7 test, `THING_AF_PARTY`). So a
    /// loaded network save has nobody's character in it: each player who joins it brings their own (the
    /// character file their machine keeps, written when they left; `NetCharacter.Saved`) and arrives as a
    /// newcomer does, at the chronicle's revive location — there is no one to return to.
    /// </summary>
    public static bool LeftOut(Location place, WorldThing t) =>
        t.Type == ThingType.Player && NetOrders.Mode(place) != NetMode.Single;

    /// <summary>
    /// **A save loaded to host a network game, without its party**. A network game's save holds no party at all (the
    /// writer saves the party only outside a network game, `NsgSave.HasParty`), and the load reads one back
    /// only when no network session is up,
    /// and the load's reset before it has emptied the party. Load Saved then makes the game as Create Game does, whose session puts **the character chosen on the select or create screen** —
    /// the local player, its own `.npf` — in slot 0; and the start makes that
    /// character the party, selected and steered, and stands it
    /// at the chronicle's revive location and entrance. So
    /// the host plays the character it chose, with that character's own sheet and pack, and the one who
    /// was the party when the game was saved is in the world no more. The rest of the save — its worlds,
    /// its chronicle, its session — is as it was.
    /// </summary>
    public static Save.SaveGame ForHost(Save.SaveGame save)
    {
        save.Party = new Save.PartySave();
        return save;
    }

    /// <summary>
    /// Where a hosted save starts: the chronicle location, its name, the entrance to stand at, and the
    /// level and scene to build it with.
    /// </summary>
    public readonly record struct HostedStart(int Index, string Name, int Entrance, string Level, string Scene);

    /// <summary>
    /// **Where a save hosted as a network game starts** (the start, at the revive location
    /// At the chronicle's **revive location and entrance**, wherever the save was taken —
    /// the start stands slot 0's character there and loads that place, as it does for a new network game.
    /// The revive location is the save's (`SetReviveLocation` moves it; whether
    /// 1.1's load restores it from `!main.nsg` or leaves the chronicle's own is unread, and
    /// Vitomir's save keeps it), else the chronicle's `REVIVELOC`. The place is built with the level and
    /// scene the save remembers for it, or the chronicle's as the save left them.
    ///
    /// **Only a network game's save can be hosted.** Every save path is `SaveGames\MP<n>` while a network
    /// session is up and `SP<n>` otherwise (the load list and the load
    /// itself choose by the network session the same way), so 1.1's Load Saved on mpCreateGame lists
    /// and loads only the saves a network game wrote — whose chronicle is a multiplayer one, since only those
    /// are hosted. A single-player save (`--host --load SP3`, *Prague* at the monastery) cannot be
    /// reached; null here, with the reason in <paramref name="why"/>. (Vitomir once hosted it: the
    /// host's own chronicle's level with the monastery's scene on it — the inn full of Cappadocians.)
    /// </summary>
    public static HostedStart? Start(SaveGame save, ChronicleSet chronicles, out string why)
    {
        why = "";
        if (save.Chronicle is not { Name.Length: > 0 } saved || chronicles[saved.Name] is not { } story)
        {
            why = $"{save.Header.Description} has no chronicle this install knows";
            return null;
        }
        if (!story.Kind.HasFlag(NscChronicleFlags.Multiplayer))
        {
            why = $"{save.Header.Description} is a single-player game's save ({story.Name}); 1.1 hosts only a network game's saves (SaveGames\\MP<n>)";
            return null;
        }
        int index = saved.Revive is { Location: { } named } ? story.Find(named) : -1;
        int entrance = index >= 0 ? saved.Revive!.Entrance : story.Data.ReviveEntrance;
        if (index < 0) index = Math.Max(0, story.Data.ReviveLocation);
        if (story.At(index) is not { LevelName.Length: > 0 } at)
        {
            why = $"{story.Name} has no revive location to start at";
            return null;
        }
        string name = story.NameOf(index);
        string scene = save.World.TryGetValue(name, out var remembered) ? remembered.Scene
            : saved.Locations.TryGetValue(name, out var left) ? left.Scene : at.SceneName;
        return new HostedStart(index, name, Math.Max(0, entrance), remembered?.Level ?? at.LevelName, scene);
    }

    /// <summary>
    /// The save made to start at <paramref name="start"/>: the chronicle's current location and the save's
    /// own place are the revive location (the start moves the current location), and the place's record, when
    /// the save has one, is built at the revive entrance. Every other place the save remembers stays as it
    /// was, to be read back when somebody goes there — the place it was taken in among them.
    /// </summary>
    public static SaveGame AtRevive(SaveGame save, HostedStart start)
    {
        save.Header.Location = start.Name;
        if (save.Chronicle is { } saved) saved.Current = start.Name;
        if (save.World.TryGetValue(start.Name, out var record)) record.Entrance = start.Entrance;
        return save;
    }

    /// <summary>The game settings a hosted save makes: the create screen's own, in its chronicle at <paramref name="start"/>.</summary>
    public static GameSettings Settings(GameSettings made, SaveGame save, HostedStart start) => made with
    {
        Chronicle = save.Chronicle!.Name,
        Level = start.Level,
        Scene = start.Scene,
        Entrance = start.Entrance,
    };
}
