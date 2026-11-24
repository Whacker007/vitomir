// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game run's saves: the autosave, a save the menus or the command line ask for.
using System.Globalization;
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Scene;
using Vitomir.Classic;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Engine.Video;
using Vitomir.Formats;
using Vitomir.Platform.Audio;
using Vitomir.Scripting;
using Vitomir;
using Vitomir.Platform.Modules;

namespace Vitomir.Classic;

unsafe sealed partial class ClassicGame
{

    // A location's name as the save list shows it: `Convent` is "The Convent" (the save's description).
    string PlaceWords(string key) => strings.Find(key) ?? key;


    // The autosave (slot 0): written straight away, with the automatic
    // description and no message.
    void Autosave() => SaveNow(SlotPath(0), plain: false);


    bool SaveNow(string path, bool plain, string? description = null)
    {
        path = InputTape.SavePath(path);                             // a replay saves as recorded, but not over the player's
        try
        {
            var game = SaveCapture.Take(location, session, campaign, PlaceWords);
            game.Header.Mods = [.. files.RulesModules];                 // the rules modules it is made with, in load order (look modules are each player's own)
            // What the player typed, when they typed something.
            if (!string.IsNullOrEmpty(description)) game.Header.Description = description;
            if (plain) SaveArchive.WritePlain(game, path);
            else SaveArchive.Write(game, path);
            Console.WriteLine($"save: {game.Header.Description} -> {path} ({game.World.Count} places, " +
                              $"{game.World.Values.Sum(w => w.Things.Count)} things, {game.Party.Members.Count} in the party)");
            Journal.Current?.Write(frame.Sim.Milliseconds, JournalKind.Story, ("what", "save"), ("to", path),
                ("description", game.Header.Description));
            return true;
        }
        catch (Exception e)
        {
            // **Said on the screen, not only in the console** (ours): a player who thinks
            // the game saved when it did not is a play-through at risk. Nothing of the slot is
            // touched by a failed save (SaveArchive.Write), and the game goes on.
            Console.WriteLine($"save: could not write {path}: {e}");
            if (!plain && windowed)
                menus.Message("Save", $"The game could not be saved.\n{e.Message}\nThe slot was not changed.", ok: true, cancel: false);
            return false;
        }
    }


    // `--save` and `--save-plain`, once the run has done what it was asked to.
    void SaveAsAsked()
    {
        if (Arg("--save") is { } to && !to.StartsWith("--")) SaveNow(ResolveSave(to), plain: false);
        if (Arg("--save-plain") is { } dir && !dir.StartsWith("--")) SaveNow(dir, plain: true);
    }
}
