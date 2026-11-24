// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The last of the natives the single-player scripts call: the claws
// swap, the weather, the party put down on an entrance, a character ordered to attack or to use
// something, the pack walked and emptied, the shield hidden, a thing made at a point, and the
// developer console run from a script.
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>
    /// The weather effect's name (32 bytes): **one string for the whole game**,
    /// not a place's, and nothing in the engine reads it but `GetWeatherEffect`. The weather itself
    /// is the `Weather` script's doing — it spawns a thing of that template at its own thing (Vienna's
    /// `snow`, London's `heavyRain`, `lightRain` and `drizzle`), and those templates carry a rain
    /// emitter (`game.npd`'s kind 6), whose drawing is not built. It is a
    /// field of the chronicle object, so a save keeps it and a load puts
    /// it back — `ChronicleSave.Weather`.
    /// </summary>
    public string WeatherEffect { get; set; } = "";

    /// <summary>Whether the inventory pane is on screen, which sends a script's grant to the cursor. Null is never.</summary>
    public Func<bool>? InventoryPaneOpen { get; set; }

    /// <summary>`CodexWorld`'s "the world the party is in" (`WORLD_CURRENT`, 12345678).</summary>
    public const int WorldCurrent = 12345678;

    void RegisterNatives()
    {
        // ---- the claws swap --------------------------------------------------

        // `OverrideActorWeapon(template, bRemoveShield, bRemoveHands)`: a character; the
        // template (31 characters) must exist and make a **weapon** thing (type 5) — anything else
        // is destroyed and answered false; the new weapon is given its owner and handed
        // to the weapon swap. **True even when a swap was already in force**, and the second swap then
        // does nothing: the native answers before it looks.
        Engine.On("CodexActor.OverrideActorWeapon", (self, a) =>
        {
            if (Character(self) is not { } t || location?.Templates is not { } templates) return CodexHost.Box(false);
            string name = Name31(a, 0);
            if (name.Length == 0 || !templates.Has(name)) return CodexHost.Box(false);
            if (Weapon.From(name, templates.Resolve(name)) is not { } weapon) return CodexHost.Box(false);
            WeaponOverride.Override(t, weapon, CodexHost.Bool(a, 1), CodexHost.Bool(a, 2));
            return CodexHost.Box(true);
        });
        // `CancelOverrideActorWeapon()`: the swap ended on a character, true whether or not a
        // swap was in force.
        Engine.On("CodexActor.CancelOverrideActorWeapon", (self, _) =>
        {
            if (Character(self) is not { } t) return CodexHost.Box(false);
            WeaponOverride.Cancel(t);
            return CodexHost.Box(true);
        });
        // `EnableActorShield(on)`: the off hand's sub-model's hide bit (bit 8 of its
        // flags), the shield's twin of `EnableActorWeapon`. Only a character with a
        // render model; the shield is still held.
        Engine.On("CodexActor.EnableActorShield", (self, a) =>
        {
            if (Of(self) is { Type: ThingType.Actor or ThingType.Player, Actor: { } actor } t && t.Animation is not null)
                actor.OffhandHidden = !CodexHost.Bool(a, 0);
        });

        // ---- the weather ------------------------------------------------------------------------

        // `GetWeatherEffect()`: the string, as it stands.
        Engine.On("Codex.GetWeatherEffect", (_, _) => WeatherEffect);
        // `SetWeatherEffect(name)`: 31 characters into the global, and then — when the
        // party is in a place (the client's world) — `weatherchanged()` to every
        // thing's own script in it and to the chronicle's scripts. It is sent whether or
        // not the name changed.
        Engine.On("Codex.SetWeatherEffect", (_, a) =>
        {
            WeatherEffect = Name31(a, 0);
            if (location is not null) WeatherChanged();
        });

        // ---- the party and its orders ---------------------------------------------------------

        // `TeleportPartyToEntrance(player, entrance)`: the named thing must be a
        // character; the whole party is moved (to the player starts).
        Engine.On("CodexPlayer.TeleportPartyToEntrance", (_, a) =>
        {
            if (location?.Thing(CodexHost.Int(a, 0)) is not { Type: ThingType.Actor or ThingType.Player })
                return CodexHost.Box(false);
            return CodexHost.Box(location.TeleportPartyToEntrance((uint)CodexHost.Int(a, 1)));
        });
        // `ActorActionAttack(target, attackMode, controlMod)`: a character, a target that
        // is **any** thing, and the attack order with the weapon in its hand — true only
        // when the order was taken (`== 4`). `SOC3_Main` and `WAR1_Thorne` pass `MOTION_PISTOL`
        // as the attack, which the order ignores as out of range.
        Engine.On("CodexActor.ActorActionAttack", (self, a) =>
        {
            if (Character(self) is not { Actor: { } me } t) return CodexHost.Box(false);
            if (location?.Thing(CodexHost.Int(a, 0)) is not { } target) return CodexHost.Box(false);
            return CodexHost.Box(Actions.Attack(t, target, me.Weapon, CodexHost.Int(a, 1), Now,
                                                motions: location.Motions,
                                                mods: (InputModifier)CodexHost.Int(a, 2)) == OrderResult.Started);
        });
        // `ActorActionActivate(thing, controlMod)`: the use order, true when
        // taken. The modifier goes to the order, which asks only space (out of reach, the
        // use is given up where the character stands; Rpg.TakeRules). `TEU4_22_1_Main` has a corpse
        // pull the switch this way.
        Engine.On("CodexActor.ActorActionActivate", (self, a) =>
        {
            if (Character(self) is not { } t) return CodexHost.Box(false);
            if (location?.Thing(CodexHost.Int(a, 0)) is not { } target) return CodexHost.Box(false);
            return CodexHost.Box(Actions.Use(t, target, Now, location.Motions,
                                             (InputModifier)CodexHost.Int(a, 1)) == OrderResult.Started);
        });
        // `ActorActionUnstake(thing)`: both characters, then the unstake order,
        // true when taken (4). Rpg.Stake has the order.
        Engine.On("CodexActor.ActorActionUnstake", (self, a) =>
        {
            if (Character(self) is not { } t) return CodexHost.Box(false);
            if (location?.Thing(CodexHost.Int(a, 0)) is not { Type: ThingType.Actor or ThingType.Player } target) return CodexHost.Box(false);
            return CodexHost.Box(Stake.Order(t, target, Now) == OrderResult.Started);
        });

        // ---- the pack ---------------------------------------------------------------------------

        // `GetActorFirstInventoryItem()`: the head of the character's list
        // — the newest item, since an item is pushed on the front — or 0.
        Engine.On("CodexActor.GetActorFirstInventoryItem", (self, _) =>
            CodexHost.Box(Character(self)?.Actor?.Inventory.Items is { Count: > 0 } items ? items[0].Thing?.Guid ?? 0 : 0));
        // `GetNextInventoryItem()` / `GetPrevInventoryItem()`: an item's links
        // in whichever list it is in, or 0 — an item on the cursor, or on the
        // floor, is in none.
        Engine.On("CodexItem.GetNextInventoryItem", (self, _) => CodexHost.Box(Neighbour(self, +1)));
        Engine.On("CodexItem.GetPrevInventoryItem", (self, _) => CodexHost.Box(Neighbour(self, -1)));
        // `AddToActorInventory(item)`: a character, and a thing of **type 1** — an item,
        // not a weapon and not armour — given it as `Ground.Add` gives: the item's `pickup`
        // first, then the cursor or the pack. The rats of Prague's sewers come to the hand this way.
        Engine.On("CodexActor.AddToActorInventory", (self, a) =>
        {
            if (Character(self) is not { } t || location?.Thing(CodexHost.Int(a, 0)) is not { Type: ThingType.Item } item)
                return CodexHost.Box(false);
            var got = Ground.Add(location, t, item, steering: ReferenceEquals(location.Steered, t),
                                 selected: ReferenceEquals(location.Selected, t), location.ItemScripts,
                                 InventoryPaneOpen?.Invoke() ?? false);
            return CodexHost.Box(got != GroundResult.Refused);
        });
        // `RemoveItemFromInventory()`: out of its keeper's pack or hand and onto the
        // floor at the keeper's feet — and the answer is **the keeper's** GUID, or 0.
        Engine.On("CodexItem.RemoveItemFromInventory", (self, _) =>
        {
            if (location is null || Of(self) is not { Type: ThingType.Item or ThingType.Weapon or ThingType.Armor } t)
                return CodexHost.Box(0);
            return CodexHost.Box(Ground.Remove(location, t, location.ItemScripts)?.Guid ?? 0);
        });

        // ---- a thing made at a point ----------------------------------------------------------

        // `CodexWorld.CreateThing(template, position, orientation)`: in the world the
        // object names — `WORLD_CURRENT`, or a location whose world is loaded — the template (31
        // characters) made and placed with the transform built from the two vectors, the
        // same one a scene's own placement builds (the angles in degrees), entered in the world.
        // Its GUID, or 0. Vitomir has only the place the party is in to make things in, so a
        // location named by number is the current one or nothing.
        Engine.On("CodexWorld.CreateThing", (self, a) =>
        {
            int world = runtime.GuidOf(self);
            if (location is null || (world != WorldCurrent && world != (Chronicle?.Current ?? -1))) return CodexHost.Box(0);
            string name = Name31(a, 0);
            if (name.Length == 0 || location.Templates?.Has(name) != true) return CodexHost.Box(0);
            var at = Vec(a, 1);
            using (Vitomir.Engine.Net.MadeHere.ByScript(location))              // kept back unless `netprops`
                return CodexHost.Box(location.Place(name, at, Vec(a, 2), location.Player?.Thing.Sector ?? 0)?.Guid ?? 0);
        });

        // ---- the developer console, from a script ---------------------------------------------

        // `CodexConsole.Execute(line)`: 255 characters of a console line, run
        // as if typed. True when its first word is a command, false (and "Unrecognized command" in
        // the console) when it is not.
        Engine.On("CodexConsole.Execute", (_, a) =>
        {
            string line = CodexHost.Str(a, 0) ?? "";
            if (line.Length > 255) line = line[..255];
            return CodexHost.Box(ConsoleLine is { } run ? run(line) : Execute(line));
        });
    }

    /// <summary>
    /// The game's own developer console (`Engine.Ui.DevConsole` with `ConsoleCommands`), which a
    /// script's line runs through when there is one — the same line, history and table as a typed
    /// one. Without one, <see cref="Execute"/> runs the two a script can reach.
    /// </summary>
    public static Func<string, bool>? ConsoleLine { get; set; }

    /// <summary>The item after or before this one in its owner's pack, or 0.</summary>
    int Neighbour(object? self, int step)
    {
        if (Of(self) is not { Type: ThingType.Item or ThingType.Weapon or ThingType.Armor, Item: { } item } t) return 0;
        if (t.Owner?.Actor is not { } owner) return 0;
        var items = owner.Inventory.Items;
        int i = -1;
        for (int k = 0; k < items.Count; k++) if (ReferenceEquals(items[k], item)) { i = k; break; }
        if (i < 0) return 0;
        int n = i + step;
        return n >= 0 && n < items.Count ? items[n].Thing?.Guid ?? 0 : 0;
    }

    // ---- the console's commands ----------------------------------------------------------------

    /// <summary>
    /// Every command 1.1's console knows — the 98 registrations into its one table
    /// (four of them twice over),
    /// so that `Execute` answers true for a real one. Only <c>cash</c> and <c>advance</c> do anything
    /// here: <c>cash</c> is the one a shipped single-player script runs (`SOC3_Main`, taking away
    /// the Dark Ages' money), and <c>advance</c> is the only way 1.1 ever sends a chronicle its
    /// `advance`. The rest answer true and do nothing (ours) — they are developer cheats.
    /// </summary>
    public static readonly IReadOnlySet<string> ConsoleCommands = new HashSet<string>(StringComparer.Ordinal)
    {
        "commands", "framerate", "soundcache", "addthing", "playmusic", "stopmusic", "musicvolume",
        "playambient", "stopambient", "ambientvolume", "adddiscipline", "castdiscipline", "maxfps",
        "timescale", "pause", "resume", "shadow", "vertexlight", "thinglights", "fog", "zclip", "modellod",
        "lightblobs", "numshadows", "sky", "gamma", "prelights", "cash", "echo", "whereis", "jump",
        "scenelist", "revive", "god", "ai", "aitrace", "freecasting", "addalldisciplines", "particles",
        "heads", "loadpath", "savepath", "say", "statustest", "changescene", "advance",
        "changechronicle", "getcls", "stakeme", "speed", "freeammo", "combatmessages", "idkfa",
        "frenzyme", "killme", "damageme", "diseaseme", "poisonme", "scaleme", "embraceme", "xp",
        "advancement", "aura", "dropcash", "vault", "camtest", "setsoak", "shapeshift", "getchronflag",
        "setchronflag", "clearchronflag", "netstat", "read", "shell", "emit", "roomtype",
        "autoswitchtest", "totals", "enemycount", "throwgib", "setdetect", "sethide", "freeze",
        "hands", "stcmd", "stcmd2", "team", "maxopenedlocs", "noitemstatchecks", "endgame", "pack",
        "ping", "rename", "humanity",
    };

    /// <summary>
    /// The console's line: the first word — up to a comma, space, tab or line end
    /// — lower-cased and looked up; the rest of the line up to its end
    /// is the command's argument.
    /// </summary>
    public bool Execute(string line)
    {
        int start = 0;
        while (start < line.Length && IsDelimiter(line[start])) start++;
        int end = start;
        while (end < line.Length && !IsDelimiter(line[end])) end++;
        if (end == start) return false;                                   // no word, no command
        string word = line[start..end].ToLowerInvariant();
        string rest = end < line.Length ? line[(end + 1)..].Split('\n', '\r')[0] : "";
        if (!ConsoleCommands.Contains(word))
        {
            Print("Unrecognized command");                                // as 1.1 says it in single player
            return false;
        }
        switch (word)
        {
            case "cash": Cash(rest); break;
            case "advance": Advance(rest); break;
            default:
                Diag.For("Vitomir.Scripting").LogInformation("console: `{Line}` is a developer command Vitomir does not run", line);
                break;
        }
        return true;

        static bool IsDelimiter(char c) => c is ',' or ' ' or '\t' or '\n' or '\r';
    }

    /// <summary>
    /// `cash [n]`: with no number, the steered character's money is printed ("Current
    /// Cash: %d"); with one, it is **set** — the party's purse for a party member of the player type,
    /// the character's own otherwise.
    /// </summary>
    void Cash(string rest)
    {
        var who = location?.Steered;
        var session = Session;
        bool purse = who is { Type: ThingType.Player, Actor: { } a } && a.Has(Actor.Flag.Party);
        if (rest.Trim().Length == 0)
        {
            int now = purse ? session?.Gold ?? 0 : who?.Actor?.Cash ?? 0;
            Print($"Current Cash: {now}");
            return;
        }
        if (!int.TryParse(rest.Trim().Split(' ', '\t')[0], out int cash)) return;
        if (purse && session is not null) session.Gold = cash;
        else if (who?.Actor is { } actor) actor.Cash = cash;
    }

    /// <summary>
    /// `advance scene subscene`: two numbers, and the chronicle's scripts hear
    /// `advance(scene, subscene)` — which is how a developer skipped the story forward;
    /// each chronicle class answers it by setting the flags and exits of that point in the story.
    /// </summary>
    void Advance(string rest)
    {
        var parts = rest.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[0], out int scene) || !int.TryParse(parts[1], out int sub)) return;
        foreach (var (name, script) in ChronicleScripts())
            Say(name, script, "advance", "(II)V", [CodexHost.Box(scene), CodexHost.Box(sub)]);
    }
}
