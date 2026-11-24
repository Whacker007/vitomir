// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The developer console's commands, read handler by handler from the original's console:
// each one does what 1.1's does and says what it says, or — for the render
// and driver knobs Vitomir does not have — says so.
using System.Globalization;
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **The commands of 1.1's console** (its cheat table, filled at
/// start-up and run by the console's line). A handler is given the rest of the line after the command word — 1.1 hands it
/// the remainder as one string (<c>strtok(NULL, "\r\n")</c>) and each parses it for
/// itself, mostly with <c>sscanf</c> — and what it prints goes to the status bar's message box.
///
/// "The player" is the engine's current player: the character being steered.
/// <see cref="Game"/> is what the commands reach; the host (the game) fills in what it owns — the
/// brightness, the fog, the panes — and leaves the rest null.
/// </summary>
public static class ConsoleCommands
{
    /// <summary>What the commands work on. Everything is asked for when a command runs, because a journey replaces the place.</summary>
    public sealed class Game
    {
        public required Session Session { get; init; }
        public required Func<Location?> Here { get; init; }
        public GameAudio? Audio { get; init; }
        public Func<WorldCamera?>? Camera { get; init; }
        /// <summary>The round trip to the host, on a player's machine in a network game; null elsewhere (`ping`).</summary>
        public Func<float?>? Ping { get; init; }
        /// <summary>Lets the end text go (<see cref="EndTextHold.LetGo"/>): `revive` of a party character does.</summary>
        public Action? EndTextGone { get; init; }
        /// <summary>The menus, for the game pause (`pause`, `resume`).</summary>
        public FrontEnd? Menus { get; init; }
        /// <summary>The brightness (`gamma` → the renderer's gamma ramp).</summary>
        public Action<float>? Gamma { get; init; }
        /// <summary>The scene's fog (`fog` → the renderer's fog set-up).</summary>
        public Action<FogSettings>? Fog { get; init; }
        /// <summary>The party heads drawn or not (`heads`, bit 2 of the raster options): the getter and the setter.</summary>
        public Func<bool>? HeadsShown { get; init; }
        public Action<bool>? ShowHeads { get; init; }
        /// <summary>The sky drawn or not (`sky`, bit 8 of the raster options).</summary>
        public Action<bool>? Sky { get; init; }
        /// <summary>The system clipboard's text (`echo *PASTE`).</summary>
        public Func<string?>? Clipboard { get; init; }
        /// <summary>Every chronicle script's `advance(scene, subscene)` (only the script layer can deliver it).</summary>
        public Func<int, int, bool>? Advance { get; init; }
        /// <summary>The advancement screen opened for a character (`advancement`).</summary>
        public Action<WorldThing>? Advancement { get; init; }
        /// <summary>The vault opened (`vault`).</summary>
        public Action? Vault { get; init; }
        /// <summary>A document pane opened on a text key (`read`).</summary>
        public Action<string>? Read { get; init; }
        /// <summary>A new chronicle started from its file (`changechronicle`); false when there is no such file.</summary>
        public Func<string, bool>? ChangeChronicle { get; init; }
        /// <summary>The render view's far clip (`zclip`), which a scene sets as it starts.</summary>
        public Action<float>? FarClip { get; init; }
        /// <summary>The on-screen frame rate (`framerate`).</summary>
        public Action<bool>? FrameRate { get; init; }
        /// <summary>The string table, for the names and lines the commands print (`combatmessages`).</summary>
        public Strings? Text { get; init; }
        /// <summary>A file to write a report to (`totals`, `enemycount`); null writes none.</summary>
        public Func<string, TextWriter?>? Report { get; init; }
        /// <summary>
        /// The game's log window (`logwindow`, ours): true opens it, false closes it, null toggles; answers the line the
        /// host's log gets. Null where the host has none.
        /// </summary>
        public Func<bool?, string?>? LogWindow { get; init; }
    }

    /// <summary>
    /// **`logwindow [on|off]`** (ours): bare, the game's log window
    /// toggled; <c>on</c> (or 1) opens it, <c>off</c> (or 0) closes it; anything else says how it is used. The answer is the line the
    /// host's log gets (<c>log window: opened (...)</c>, <c>log window: closed</c>, or why not); with no host behind it, that there
    /// is none.
    /// </summary>
    public static string LogWindowCommand(string[] a, Func<bool?, string?>? logWindow)
    {
        bool? on = null;
        if (a.Length > 0)
        {
            switch (a[0].ToLowerInvariant())
            {
                case "on" or "1": on = true; break;
                case "off" or "0": on = false; break;
                default: return "logwindow: on, off, or nothing to toggle it";
            }
        }
        return logWindow?.Invoke(on) ?? "log window: not here (this game has no log window; the log has the lines)";
    }

    /// <summary>What 1.1's commands that Vitomir has no knob for answer, so a real one is never taken for a typo.</summary>
    /// <summary>
    /// `ping` (message 0x52 to the host and back): on a player's machine
    /// *"Ping reply: %d msec"*, the time since it was sent; on the host, or in single player, nothing
    /// (it sends only from a client). **Ours**: the reply is the transport's own measure of the
    /// round trip, kept as every reliable message is answered, rather than one timed message.
    /// </summary>
    public static string? PingReply(float? roundTripMs) => roundTripMs is { } ms ? $"Ping reply: {(int)ms} msec" : null;

    public static string NoKnob(string name, string why) => $"{name}: {why} — Vitomir has no such setting";

    /// <summary>
    /// The render and driver knobs of 1.1's console that Vitomir has nothing behind, each with the
    /// reason. They answer with <see cref="NoKnob"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> WithoutKnobs = new Dictionary<string, string>
    {
        ["soundcache"] = "1.1's sound cache size",
        ["vertexlight"] = "rlDirect's vertex lighting bit (bit 1)",
        ["thinglights"] = "rlDirect's most lights on a thing",
        ["modellod"] = "the model level-of-detail factor — Vitomir draws every model whole",
        ["prelights"] = "rlDirect's prelit-model bit (bit 0x1000)",
        ["loadpath"] = "the camera path editor's load",
        ["savepath"] = "the camera path editor's save",
        ["roomtype"] = "the sound card's room type (EAX)",
        ["netstat"] = "DirectPlay's traffic counters",
        ["maxopenedlocs"] = "the most places kept open",
    };

    /// <summary>Gives the console every command Vitomir runs.</summary>
    public static void Register(DevConsole console, Game game)
    {
        var c = console.Commands;
        void Post(string line) => console.Say(line);
        Location? Here() => game.Here();
        WorldThing? Player() => Here()?.Steered;
        int Now() => Here()?.Milliseconds ?? 0;

        foreach (var (name, why) in WithoutKnobs) c[name] = _ => NoKnob(name, why);

        // `commands`: every name the table holds, one line each.
        c["commands"] = _ => { foreach (var n in console.Names()) Post(n); return null; };

        // `framerate n`: the frame-rate display on for anything but 0.
        c["framerate"] = a => game.FrameRate is not { } show ? NoKnob("framerate", "the on-screen frame counter; Vitomir's is in the window's title")
            : Int(a) is { } on ? Say(() => show(on != 0), null) : null;

        // `addthing t1 t2 …`: each template made where the player stands.
        c["addthing"] = a =>
        {
            if (Player() is not { } me || Here() is not { } place) return "No current player to get coordinates from.";
            var names = Words(console.Rest, " \r\t\n");
            if (names.Length == 0) return null;
            foreach (var n in names)
                Post(place.Templates?.Find(n) is null ? $"Couldn't find template {n}."
                    : place.Spawn(n, me) is null ? $"Error while adding {n}." : $"{n} added at current player's position.");
            return null;
        };

        // `playmusic name` and `playambient name`: a stream, or why not.
        c["playmusic"] = a => Stream(a, s => game.Audio!.StartMusic(s));
        c["playambient"] = a => Stream(a, s => game.Audio!.StartAmbient(s));
        string? Stream(string[] a, Func<Sound, bool> start)
        {
            if (a.Length == 0 || game.Audio is not { } audio) return null;
            if (audio.Bank.Load(a[0], stream: true) is not { } s) return $"Couldn't load stream '{a[0]}'.";
            return start(s) ? $"Playing stream '{a[0]}'." : null;
        }
        // `stopmusic`, `stopambient`.
        c["stopmusic"] = _ => { game.Audio?.StopMusic(); return null; };
        c["stopambient"] = _ => { game.Audio?.StopAmbient(); return null; };
        // `musicvolume n` (the script's level) and `ambientvolume n`.
        c["musicvolume"] = a => Int(a) is { } n && game.Audio is { } au ? Say(() => au.SetMusicLevel(n), $"Music volume set to {n}.") : null;
        c["ambientvolume"] = a => Int(a) is { } n && game.Audio is { } au ? Say(() => au.SetAmbientLevel(n), $"Ambient volume set to {n}.") : null;

        // `adddiscipline name`: made and set to level 1 — which
        // 1.1's own line calls "level 2", counting from one.
        c["adddiscipline"] = a =>
        {
            if (Player() is not { } me || a.Length == 0 || Here()?.Powers is not { } defs) return null;
            string name = console.Rest;
            if (defs[name] is null || DisciplineBook.SetLevel(me, defs, name, 1) is not { } d)
                return $"Couldn't add discipline Discipline '{name}' to current player";
            return $"Discipline '{d.Definition.Name}' added and set to level 2 for current player";
        };
        // `castdiscipline name`: the player's own instance's script `cast(level, caster)`
        // — the TARGET NONE form, whatever the discipline is.
        c["castdiscipline"] = a =>
        {
            if (Player() is not { Actor: { } actor } me || a.Length == 0 || Here()?.Powers is not { } defs) return null;
            string name = console.Rest;
            if (defs[name] is not { } def || actor.Disciplines.Of(def) is not { } inst)
                return $"Current player doesn't have discipline '{name}'.";
            if (inst.Script is not { } script || me.Place?.ScriptObjects is not { } scripts) return null;
            scripts.Cast(script, CastForm.Self, inst.Level, me, null, Vector3.Zero);
            return $"Cast '{def.Name}'.";
        };
        // `addalldisciplines [level]`: every discipline of every group, at the level
        // given (2 when none, 4 at most).
        c["addalldisciplines"] = a =>
        {
            if (Player() is not { } me || Here()?.Powers is not { File: { } file } defs) return null;
            // atoi's answer taken unsigned (`4 < n`, unsigned), so a negative one is 4 too.
            int level = a.Length == 0 ? 2 : (int)Math.Min(unchecked((uint)(Int(a) ?? 0)), 4u);
            foreach (var d in file.All) DisciplineBook.SetLevel(me, defs, d.Name, level);
            return "Added all disciplines to the current control";
        };

        // `shadow [mode detail height full far]`: bare, the live settings; else
        // `sscanf("%d %f %f %f %f")` — a mode past 5 refused, the rest written as given (a value not
        // given keeps what it was: ours; 1.1 writes whatever its stack held).
        c["shadow"] = a =>
        {
            var now = World.ShadowConsole.Live();
            if (a.Length == 0)
                return string.Create(CultureInfo.InvariantCulture,
                    $"Current shadow detail {now.Mode} {now.Detail:F2} {now.Height:F2} {now.FullDist:F2} {now.MaxDist:F2}");
            if (Int(a) is not { } mode) return "Invalid shadow args.";
            if ((uint)mode > 5u) return $"Invalid shadow detail {mode}.";
            float F(int i, float was) => a.Length > i && float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : was;
            var set = (mode, F(1, now.Detail), F(2, now.Height), F(3, now.FullDist), F(4, now.MaxDist));
            World.ShadowConsole.Detail = set;
            string[] names = ["Off", "Drop", "Rough texture", "Full texture", "Full texture (HW)", "Full poly"];
            Post($"Shadow type {mode}: {names[mode]}");
            return string.Create(CultureInfo.InvariantCulture,
                $"Shadow detail: {mode} {set.Item2:F2} {set.Item3:F2} {set.Item4:F2} {set.Item5:F2}");
        };
        // `numshadows [n]`: bare, how many a thing casts; else `atoi`, at most 10.
        c["numshadows"] = a =>
        {
            if (a.Length == 0) return $"Current # shadows per object: {World.ShadowConsole.Live().PerThing}";
            int n = (int)Math.Min(unchecked((uint)(Int(a) ?? 0)), 10u);
            World.ShadowConsole.PerThing = n;
            return $"Max object shadows set to: {n}.";
        };

        // `maxfps [f]`: a frame at least `round(1000 / f)` milliseconds long, which 1.1's
        // frame (at its end) waits out; nothing, 0 or less is off.
        c["maxfps"] = a =>
        {
            if (a.Length == 0) { FrameCapMs = 0; return "Framerate Cap: OFF"; }
            if (Float(a) is not { } fps) return null;
            if (fps <= 0f) { FrameCapMs = 0; return "Framerate Cap: OFF"; }
            FrameCapMs = (int)Math.Round(1000.0 / fps, MidpointRounding.AwayFromZero);
            return string.Create(CultureInfo.InvariantCulture, $"Framerate Cap: ON ({1000.0 / FrameCapMs:F6} FPS)");
        };

        // `timescale f`: the game clock's rate, 0 or less set to 0 — which steps a millisecond a frame (FrameStep.Scaled).
        c["timescale"] = a => Float(a) is { } f && Here() is not null ? Say(() => TimeScale = f <= 0f ? 0f : f, null) : null;
        // `pause` / `resume`: the game clock. Which timeline 1.1's stop is not read; ours is the game pause.
        c["pause"] = _ => { if (game.Menus is { } m) m.Paused = true; return null; };
        c["resume"] = _ => { if (game.Menus is { } m) m.Paused = false; return null; };

        // `fog mode r g b start end`: the renderer's fog, as a scene sets it — the same
        // call the level start makes with the scene's fog, whose colour is 0..255 (it packs it into bytes).
        c["fog"] = a =>
        {
            if (a.Length < 6 || game.Fog is not { } set) return null;
            if (Int([a[0]]) is not { } mode) return null;
            var f = a[1..6].Select(s => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f).ToArray();
            set(new FogSettings((uint)mode, new Vector3(f[0], f[1], f[2]) / 255f, f[3], f[4], f[3]));
            return null;
        };
        // `zclip f`: the render view's far clip, silently; nothing without a number.
        c["zclip"] = a => game.FarClip is not { } set ? NoKnob("zclip", "the render view's far clip")
            : Float(a) is { } f ? Say(() => set(f), null) : null;

        // `lightblobs [off]`: **off unless a word other than "off" follows** — a bare
        // `lightblobs` turns them off, as `ai` does the AI.
        c["lightblobs"] = a =>
        {
            bool on = a.Length > 0 && !a[0].Equals("off", StringComparison.OrdinalIgnoreCase);
            if (Here() is { } place) place.Lights.BlobsOff = !on;
            return on ? "Lightblobs enabled" : "Lightblobs disabled.";
        };
        // `sky [off]`: the same way round — where the host can switch its sky.
        c["sky"] = a =>
        {
            if (game.Sky is not { } sky) return NoKnob("sky", "the sky's draw bit (bit 8); Vitomir's sky is chosen when the place loads (--no-sky)");
            bool on = a.Length > 0 && !a[0].Equals("off", StringComparison.OrdinalIgnoreCase);
            sky(on);
            return on ? "Sky rendering enabled" : "Sky rendering disabled.";
        };
        // `gamma [g]`: the brightness — **1 when no number is given**. Held to the
        // Display screen's 0.5..2 (ours: 1.1 hands the ramp whatever it is given).
        c["gamma"] = a =>
        {
            float g = a.Length == 0 ? 1f : Float(a) ?? 0f;
            game.Gamma?.Invoke(Math.Clamp(g, GameOptions.MinGamma, GameOptions.MaxGamma));
            return null;
        };
        // `particles [off|0|on|1]`: bare says which; off/0 and on/1 switch; anything else nothing.
        c["particles"] = a =>
        {
            if (Here() is not { } place) return null;
            if (a.Length == 0) return place.Particles.Off ? "Particles are disabled." : "Particles are enabled.";
            if (a[0] is "off" or "0") { place.Particles.Off = true; return "Particles disabled."; }
            if (a[0] is "on" or "1") { place.Particles.Off = false; return "Particles enabled."; }
            return null;
        };
        // `heads [off|0|…]`: bare says which; off/0 off; anything else on — with 1.1's "Headss".
        c["heads"] = a =>
        {
            if (a.Length == 0) return game.HeadsShown?.Invoke() == false ? "Heads are disabled." : "Heads are enabled.";
            bool off = a[0] is "off" or "0";
            game.ShowHeads?.Invoke(!off);
            return off ? "Heads disabled." : "Headss enabled.";
        };

        // `cash [n]`: the player's money shown, or set — the party's
        // purse for a party member of the player type, the character's own otherwise.
        c["cash"] = a =>
        {
            var who = Player();
            bool purse = who is { Type: ThingType.Player, Actor: { } pa } && pa.Has(Actor.Flag.Party);
            if (a.Length == 0) return $"Current Cash: {(purse || who is null ? game.Session.Gold : who.Actor?.Cash ?? 0)}";
            if (Int(a) is not { } n) return null;
            if (purse || who?.Actor is null) game.Session.Gold = n;
            else who.Actor.Cash = n;
            return null;
        };

        // `echo text`: the text to the message box; `*CLS` clears the box; `*PASTE` puts
        // the clipboard's text in, a line at a time, 128 characters a line at most, from its first 511.
        c["echo"] = a =>
        {
            if (a.Length == 0) return null;
            string text = console.Rest;
            if (text.Equals("*CLS", StringComparison.OrdinalIgnoreCase)) { game.Session.Messages.Clear(); return null; }
            if (!text.Equals("*PASTE", StringComparison.OrdinalIgnoreCase)) return text;
            if (game.Clipboard?.Invoke() is not { } clip) return null;
            if (clip.Length > 0x1ff) clip = clip[..0x1ff];
            foreach (var line in clip.Split('\r', '\n'))
                for (int i = 0; i < line.Length; i += 0x80) Post(line.Substring(i, Math.Min(0x80, line.Length - i)));
            return null;
        };

        // `whereis [guid]`: that thing, or the player — "Floor %d: type %d, template %s"
        // and its position with its sector, or "Outside world".
        c["whereis"] = a =>
        {
            var place = Here();
            var who = Int(a) is { } g and not 0 ? place?.Thing(g) : Player();
            if (who is null || place is null) return null;
            int floor = who.Sector >= 0 && Pick.Nearest(place.Level, who.Position + new Vector3(0, 0, 16), -Vector3.UnitZ, 64f) is { } under ? under.Surface : -1;
            Post($"Floor {floor}: type {(int)who.Type}, template {who.Template}");
            return who.Sector >= 0
                ? string.Create(CultureInfo.InvariantCulture, $"Pos ({who.Position.X:0.0},{who.Position.Y:0.0},{who.Position.Z:0.0}), sector {who.Sector}")
                : "Outside world";
        };
        // `jump location [entrance]` and `changescene location scene`: a name,
        // or `#n` for the n-th location; the chronicle's own requests.
        c["jump"] = _ =>
        {
            var a = Words(console.Rest, " \r\t\n,");
            if (a.Length == 0 || game.Session.Chronicle is not { } ch) return null;
            string name = LocationName(ch, a[0]);
            return ch.Find(name) >= 0 && ch.Jump(name, Math.Max(0, Int(a[1..]) ?? 0)) ? null : $"Location {a[0]} not found.";
        };
        c["changescene"] = _ =>
        {
            var a = Words(console.Rest, " \r\t\n,");
            if (a.Length == 0 || game.Session.Chronicle is not { } ch) return null;
            string name = LocationName(ch, a[0]);
            return ch.Find(name) >= 0 && ch.ChangeScene(name, a.Length > 1 ? a[1] : "") ? null : $"Location {a[0]} not found.";
        };
        // `scenelist`: "Loc %d: %s" for every location of the chronicle.
        c["scenelist"] = _ =>
        {
            if (game.Session.Chronicle is { } ch)
                for (int i = 0; i < ch.Count; i++) Post($"Loc {i}: {ch.NameOf(i)}");
            return null;
        };
        // `getcls`: the chronicle, the location and the scene, each with a second name.
        c["getcls"] = _ =>
        {
            if (game.Session.Chronicle is not { } ch) return null;
            Post($"Chronicle   {ch.Name} ({(ch.Data.Metafile.Length > 0 ? ch.Data.Metafile : "-")})");
            var loc = ch.At(ch.Current);
            Post($"Location    {loc?.Level ?? "-"} ({loc?.Name ?? "-"})");
            string file = Here()?.SceneFile is { Length: > 0 } s ? s : "-";
            return $"Scene       {loc?.Scene ?? "-"} ({file})";
        };

        // `revive`: the player back at full health and blood.
        // A party character revived takes the end text down first (flag 0x20000000, kind 7: the end text let go).
        c["revive"] = _ =>
        {
            if (Player() is not { } me) return null;
            if (me.Actor is { } actor && actor.Has(Actor.Flag.Party)) game.EndTextGone?.Invoke();
            Damage.Revive(me, 1f, 1f, sound: true);
            return null;
        };
        // `god n`: **0 and 1 are the party's first member's invulnerable bit, silently**;
        // 2 and 3 set and clear the current control's, and say so — only for a character, and 2
        // falls through into 3's test when it is not one (nothing more happens). No number, nothing.
        c["god"] = a =>
        {
            if (Int(a) is not { } n || Here() is not { } place) return null;
            if (place.PartyMembers().FirstOrDefault() is not { Actor: { } first }) return null;
            switch (n)
            {
                case 0: first.Flags &= ~Actor.Flag.Invulnerable; return null;
                case 1: first.Flags |= Actor.Flag.Invulnerable; return null;
                case 2 when Player() is { Type: ThingType.Actor or ThingType.Player, Actor: { } me } who:
                    me.Flags |= Actor.Flag.Invulnerable;
                    return $"{Rollover.NameOf(who, null)} is now invulnerable.";
                case 2 or 3 when Player() is { Type: ThingType.Actor or ThingType.Player, Actor: { } me } who:
                    me.Flags &= ~Actor.Flag.Invulnerable;
                    return $"{Rollover.NameOf(who, null)} is now vulnerable.";
            }
            return null;
        };
        // `ai [word]`: **disables** the AI unless the word after it is anything but
        // "off" — a bare `ai` turns it off, `ai on` back on.
        c["ai"] = a =>
        {
            if (Here()?.Minds is not { } m) return null;
            bool enable = a.Length > 0 && !a[0].Equals("off", StringComparison.OrdinalIgnoreCase);
            m.MainFlags = enable ? m.MainFlags & ~AiMainFlag.Paused : m.MainFlags | AiMainFlag.Paused;
            return enable ? "AI enabled." : "AI disabled.";
        };
        // `aitrace [word]`: the AI's trace bit — off for a bare one or for "0".
        c["aitrace"] = a =>
        {
            if (Here()?.Minds is not { } m) return null;
            bool enable = a.Length > 0 && a[0] != "0";
            m.MainFlags = enable ? m.MainFlags | AiMainFlag.Trace : m.MainFlags & ~AiMainFlag.Trace;
            return enable ? "AI trace enabled." : "AI trace disabled.";
        };
        // `freecasting [off]`: both of its debug bits, the blood and the stat checks.
        c["freecasting"] = a =>
        {
            bool on = OnUnlessOff(a);
            Cast.FreeCasting = on ? Cast.FreeCasting | Cast.FreeBlood | Cast.FreeChecks : Cast.FreeCasting & ~(Cast.FreeBlood | Cast.FreeChecks);
            return on ? "Free Casting enabled." : "Free Casting disabled.";
        };
        // `noitemstatchecks [off]`: bit 1 of its debug word, which the use test tests before a
        // player's item restrictions.
        c["noitemstatchecks"] = a =>
        {
            Equipment.NoStatChecks = OnUnlessOff(a);
            return Equipment.NoStatChecks ? "No Item Stat Checks enabled." : "No Item Stat Checks disabled.";
        };
        // `freeammo [off]` and `idkfa [off]`: bit 2 of the debug word, which the spend tests.
        c["freeammo"] = c["idkfa"] = a =>
        {
            Weapon.FreeAmmo = OnUnlessOff(a);
            return Weapon.FreeAmmo ? "Free ammo on." : "Free ammo off.";
        };
        // `combatmessages [off]`: bit 1 of the debug word, every blow's numbers in the
        // message box (`Rpg.CombatMessages`).
        if (game.Text is not null) CombatMessages.Text = game.Text;
        c["combatmessages"] = a =>
        {
            CombatMessages.On = OnUnlessOff(a);
            return CombatMessages.On ? "Combat messages on." : "Combat messages off.";
        };

        // `say name`: the sound played on the party's selected member
        // (the steered one, not the first), 5000 far and 1000 near, full volume, flag 0x100.
        c["say"] = a =>
        {
            var leader = Here()?.Selected ?? Player();
            if (a.Length == 0 || game.Audio is not { } audio || leader is null) return null;
            if (audio.Bank.Load(a[0]) is not { } s) return null;
            audio.Mixer.PlayAtThing(s, leader, 5000f, 1000f, 1f, 0x100);
            return null;
        };
        // `advance scene subscene`: two numbers, and every chronicle script hears `advance`.
        c["advance"] = a => a.Length >= 2 && Int(a) is { } scene && Int(a[1..]) is { } sub
            ? Say(() => game.Advance?.Invoke(scene, sub), null) : null;
        // `changechronicle file`.
        c["changechronicle"] = a =>
        {
            if (a.Length == 0) return null;
            return game.ChangeChronicle?.Invoke(a[0]) == false ? $"Chronicle file {a[0]} not found." : null;
        };

        // `stakeme`: the stake in, or out if it is in.
        c["stakeme"] = _ =>
        {
            if (Player() is not { Actor: { } actor } me) return null;
            if (actor.Has(Actor.Flag.Staked)) Stake.Out(me); else Stake.In(me, null);
            return null;
        };
        // `speed n` (0..6): every party member loses `ef_speedcheat` and, for n > 0, takes it at level n − 1.
        c["speed"] = a =>
        {
            if (a.Length == 0 || Here() is not { } place) return null;
            int n = (int)Math.Min(unchecked((uint)(Int(a) ?? 0)), 6u);            // unsigned, as 1.1 compares it
            foreach (var member in place.PartyMembers())
                if (EffectRuntime.Remove(member, "ef_speedcheat") || true)
                    if (n != 0) EffectRuntime.Apply(member, "ef_speedcheat", n - 1);
            return n == 0 ? "Speed cheat disabled." : $"Speed cheat set to level {n}.";
        };
        // `frenzyme`: `ef_increasefrenzy` by value 100, unless already frenzied.
        c["frenzyme"] = _ =>
        {
            if (Player() is { } me && !Frenzy.Frenzied(me)) EffectRuntime.Apply(me, "ef_increasefrenzy", 0, value: 100f);
            return null;
        };
        // `killme`: a script's kill.
        c["killme"] = _ =>
        {
            if (Player() is { Type: ThingType.Actor or ThingType.Player } me)
                Damage.Kill(me, null, HitKind.Script, DamageType.Normal, Now(), motions: me.Place?.Motions);
            return null;
        };
        // `damageme [amount [type]]`: 20 of type 0 by default, a script's blow.
        c["damageme"] = a =>
        {
            int amount = 20, type = 0;
            if (a.Length > 0)
            {
                if (Int(a) is not { } n) return "Usage: damageme <amount> [<damageType>]";
                amount = n;
                if (a.Length > 1 && Int(a[1..]) is { } t) type = t;
            }
            if (Player() is { Type: ThingType.Actor or ThingType.Player } me)
                Damage.Apply(me, null, HitKind.Script, (DamageType)type, (uint)amount, Rng(), Now(), skipSoak: true, motions: me.Place?.Motions);
            return null;
        };
        // `diseaseme [seconds [level]]` and `poisonme`: 30 s at level 0 by default.
        c["diseaseme"] = a => Affliction(a, "ef_disease", "diseaseme");
        c["poisonme"] = a => Affliction(a, "ef_poison", "poisonme");
        string? Affliction(string[] a, string effect, string name)
        {
            int seconds = 30, level = 0;
            if (a.Length > 0)
            {
                if (Int(a) is not { } s) return $"Usage: {name} <duration> [<level>]";
                seconds = s;
                if (a.Length > 1 && Int(a[1..]) is { } l) level = l;
            }
            if (Player() is { Type: ThingType.Actor or ThingType.Player } me)
                EffectRuntime.Apply(me, effect, level, seconds * 1000, creator: me);
            return null;
        }
        // `scaleme [ms x y z]`: the player's model scaled per axis — **the duration is
        // handed on as it is typed, to a ramp counted in seconds**, so the default 5000
        // takes well over an hour. Copied.
        c["scaleme"] = a =>
        {
            int ms = 5000;
            var s = Vector3.One;
            if (a.Length > 0)
            {
                if (a.Length < 4 || Int(a) is not { } d || Float(a[1..]) is not { } x || Float(a[2..]) is not { } y || Float(a[3..]) is not { } z)
                    return "Usage: scaleme duration x y z";
                (ms, s) = (d, new Vector3(x, y, z));
            }
            if (Player() is { } me && me.Model.Length > 0) me.Model3D.SetScale(s, ms);
            return null;
        };
        // `embraceme`: the player's ACTOR_TYPE set to 0, a vampire — the
        // byte and nothing else: no blood pool, no disciplines, no Humanity.
        c["embraceme"] = _ =>
        {
            if (Player() is { Type: ThingType.Actor or ThingType.Player, Actor: { } me }) me.Type = ActorType.Vampire;
            return null;
        };
        // `xp [n]`: n experience (1000 by default) to the player, a player-type character only.
        c["xp"] = a =>
        {
            if (Player() is { Type: ThingType.Player } me)
                Experience.Award(me, a.Length > 0 ? Int(a) ?? 0 : 1000, game.Session.Ranked);
            return null;
        };
        // `advancement`: the advancement screen, for a player-type character.
        c["advancement"] = _ =>
        {
            if (Player() is not { Type: ThingType.Player } me) return "The current control must be a player.";
            game.Advancement?.Invoke(me);
            return null;
        };
        // `dropcash [n]`: a `GoldBag` holding n (1000 by default) on the ground
        // beside the party's selected member (the steered one, not the
        // first member, which Vitomir used until then).
        c["dropcash"] = a =>
        {
            if (Here() is not { } place || (place.Selected ?? Player()) is not { } leader) return null;
            int n = a.Length > 0 ? Int(a) ?? 0 : 1000;
            DropCash.Bag(leader, n);
            return null;
        };
        // `vault`: the vault, opened by the player.
        c["vault"] = _ => { game.Vault?.Invoke(); return null; };
        // `camtest side` flips the side; `camtest a b c` frames the player and the first
        // other character in the place — **shot a, distance b, angle c** (the numbers go to the camera's shot
        // in that order), over half a second.
        c["camtest"] = a =>
        {
            if (a.Length > 0 && a[0].Equals("side", StringComparison.OrdinalIgnoreCase))
            {
                CamTestSide = 1 - CamTestSide;
                return $"Camera side is now: {CamTestSide}";
            }
            if (a.Length < 3 || Int(a) is not { } shot || Int(a[1..]) is not { } distance || Int(a[2..]) is not { } angle) return null;
            if (game.Camera?.Invoke() is not { } cam || Player() is not { } me || Here() is not { } place) return null;
            var other = place.Things.FirstOrDefault(t => t.InWorld && t.Type is ThingType.Actor or ThingType.Player && !ReferenceEquals(t, me));
            cam.SetupCutscene(me, other);
            cam.SetShot(shot, angle, distance, CamTestSide, 500);
            return null;
        };
        // `setsoak type value`: one of the player's ten soaks set.
        c["setsoak"] = a =>
        {
            if (a.Length < 2 || Int(a) is not { } type || Float(a[1..]) is not { } v || Player()?.Actor is not { } actor) return null;
            if ((uint)type < (uint)actor.Soak.Length) actor.Soak[type] = v;
            return null;
        };
        // `shapeshift [name]`: the steered character into that template's shape
        // with no flags and not forced (which refuses over a shape already on), or, bare, back
        // into its own. Silent either way.
        c["shapeshift"] = a =>
        {
            if (Player() is not { } me) return null;
            if (a.Length == 0) ShapeShift.End(me);
            else ShapeShift.Shift(me, a[0], 0);
            return null;
        };

        // The chronicle flags: the number held to 0x400.
        const string NeedsFlag = "You must pass a chronicle flag number to use this command.";
        c["getchronflag"] = a => game.Session.Chronicle is { } ch && Flag(a) is { } n
            ? $"Chron flag #{n} is {(ch.GetFlag(n) ? "SET" : "CLEAR")}" : NeedsFlag;
        c["setchronflag"] = a => game.Session.Chronicle is { } ch && Flag(a) is { } n
            ? Say(() => ch.SetFlag(n), $"Chron flag #{n} is now SET.") : NeedsFlag;
        c["clearchronflag"] = a => game.Session.Chronicle is { } ch && Flag(a) is { } n
            ? Say(() => ch.ClearFlag(n), $"Chron flag #{n} is now CLEAR.") : NeedsFlag;
        static int? Flag(string[] a) => a.Length > 0 && Int(a) is { } n ? (int)Math.Min((uint)n, 0x400u) : null;

        // `read key`: a document pane on that text.
        c["read"] = a =>
        {
            if (a.Length == 0) return "You must pass a text fragment to read to this command.";
            game.Read?.Invoke(console.Rest);
            return null;
        };
        // `shell [material]`: the player's model's shell on, at full, at once — or off.
        c["shell"] = a =>
        {
            if (Player() is not { } me) return null;
            if (a.Length == 0) { me.Fx?.EndShell(); return "shell off."; }
            me.Model3D.SetShell(a[0], 0, 1f, 1f, 0f);
            return "shell on.";
        };
        // `throwgib template`: that template sprayed off the player as a gib, from bone tag 0x13.
        c["throwgib"] = a =>
        {
            if (a.Length == 0 || Player() is not { } me) return null;
            Gore.Spray(me, console.Rest, 0x13, Rng());
            return null;
        };
        // `setdetect n`, `sethide n`: the player's perception and stealth bytes.
        c["setdetect"] = a => Int(a) is { } n && Player()?.Actor is { } me ? Say(() => me.Perception = (byte)n, null) : null;
        c["sethide"] = a => Player()?.Actor is { } me ? Say(() => me.Stealth = (byte)(Int(a) ?? 0), null) : null;
        // `freeze [off]`: the player's animation held (model flag 0x20000), or let go with "off".
        c["freeze"] = a =>
        {
            if (Player() is not { } me || me.Model.Length == 0) return null;
            me.Model3D.Frozen = !(a.Length > 0 && a[0].Equals("off", StringComparison.OrdinalIgnoreCase));
            return null;
        };
        // `hands [off]`: the talking hands on, or off with "off".
        c["hands"] = a =>
        {
            if (Player() is not { } me || me.Model.Length == 0) return null;
            if (a.Length > 0 && a[0].Equals("off", StringComparison.OrdinalIgnoreCase)) TalkingHands.Off(me);
            else TalkingHands.On(me);
            return null;
        };
        // `team [n]`: the player's team and original team, or the team set (19 at most),
        // with the thing's "modified" bits.
        c["team"] = a =>
        {
            if (Player() is not { Actor: { } actor } me) return null;
            if (a.Length == 0) return $"Current team: {actor.Team} (original team {actor.OriginalTeam})";
            actor.Team = (int)Math.Min((uint)(Int(a) ?? 0), 0x13u);
            me.Flags |= WorldThing.Flag.Modified | WorldThing.Flag.EverModified;
            return null;
        };
        // `pack`: the player's pack repacked.
        c["pack"] = _ => { if (Player()?.Actor is { } me) Stow.Repack(me); return null; };
        // `autoswitchtest`: the first weapon in the player's own things (in their
        // order) that is not the one in hand, put in the weapon hand (the wear
        // with slot 8) — the one in hand coming off as the equip takes it (ours: 1.1 stows it first,
        // the stow). Nothing is said when there is no other weapon.
        c["autoswitchtest"] = _ =>
        {
            if (Player() is not { Actor: { } actor } me || Here() is not { } place) return null;
            var next = actor.Inventory.Items.FirstOrDefault(i => i.Type == ItemType.Weapon && i.Location != Slot.RightWeapon);
            if (next is null) return null;
            var effects = place.Effects ?? (place.Files is { } f ? new EffectDefinitions(f) : null);
            bool done = effects is not null && Equipment.Equip(me, next, Slot.RightWeapon, effects, Now()) == EquipResult.Equipped;
            return done ? "Weapon switch successful." : "Weapon switch unsuccessful.";
        };
        // `totals [file]`: what the place is worth — every character not invulnerable and
        // not friendly to the player gives its experience and **its drop, rolled there and
        // then** (its DROPITEM, else a roll of its treasure class, which spends the
        // treasure generator, as 1.1's does), and every item on the ground its value or its cash.
        c["totals"] = a => Totals(a.Length > 0 ? game.Report?.Invoke(a[0]) : null);
        string? Totals(TextWriter? file)
        {
            if (Here() is not { } place) return null;
            var me = Player();
            int xp = 0, cash = 0, items = 0;
            var effects = place.Effects?.File;
            using (file)
            {
                file?.Write($"Level : {place.Name}\n");
                if (place.SceneFile.Length > 0) file?.Write($"Scene : {place.SceneFile}\n");
                foreach (var t in place.Things)
                {
                    if (!t.InWorld) continue;
                    if (t.Type is ThingType.Actor or ThingType.Player)
                    {
                        if (t.Actor is not { } actor || actor.Has(Actor.Flag.Invulnerable)) continue;
                        if (me is not null && Sight.Friendly(me, t)) continue;
                        xp += actor.Xp;
                        file?.Write($"\nActor XP {actor.Xp} ({Rollover.NameOf(t, game.Text)})\n");
                        var drop = t.DropItem.Length > 0 ? place.Treasure?.Build(t.DropItem) : place.Treasure?.Roll(t.TreasureClass);
                        if (drop?.Item is not { } item) continue;
                        if ((item.Flags & ItemFlags.InstantCash) != 0) { cash += item.Amount; file?.Write($"-- drops cash {item.Amount}\n"); }
                        else
                        {
                            int value = Shops.Value(item, effects);
                            items += value;
                            file?.Write($"  drops item {value} ({drop.Template})\n");
                        }
                    }
                    else if (t.Type is ThingType.Item or ThingType.Weapon or ThingType.Armor && t.Item is { } item
                             && t.Holder is null && t.Owner is null)
                    {
                        if ((item.Flags & ItemFlags.InstantCash) != 0) { cash += item.Amount; file?.Write($"\nPlaced cash = {item.Amount}\n"); }
                        else
                        {
                            int value = Shops.Value(item, effects);
                            items += value;
                            file?.Write($"\nPlaced item = {value} ({Rollover.NameOf(t, game.Text)})\n");
                        }
                    }
                }
                file?.Write($"\n\nTotal XP value in scene : {xp}\nTotal $$ value in scene : {cash}\nTotal item val in scene : {items}\n");
            }
            Post($"Total XP value in scene : {xp}");
            Post($"Total $$ value in scene : {cash}");
            return $"Total item val in scene : {items}";
        }
        // `enemycount [file]`: the characters of the place counted by template, into the file.
        c["enemycount"] = a =>
        {
            if (Here() is not { } place) return null;
            var counts = place.Things.Where(t => t.InWorld && t.Type is ThingType.Actor or ThingType.Player && t.Actor is { } x && !x.Has(Actor.Flag.Party))
                                     .GroupBy(t => t.Template, StringComparer.OrdinalIgnoreCase).ToList();
            if (a.Length > 0 && game.Report?.Invoke(a[0]) is { } file)
                using (file)
                {
                    file.Write($"Level : {place.Name}\n");
                    if (place.SceneFile.Length > 0) file.Write($"Scene : {place.SceneFile}\n");
                    file.Write("\n\n");
                    foreach (var g in counts.Take(64)) file.Write($"{g.Key}\t{g.Count()}\n");
                    if (counts.Count > 64) file.Write("\n***ERROR*** There were too many enemies and some were discarded from the list!!! \n");
                }
            return $"Enemy counts dumped to {(a.Length > 0 ? a[0] : "(null)")}";
        };

        // `endgame`: the game ends as the story's end does.
        c["endgame"] = _ => { game.Session.Chronicle?.EndGame(); return null; };

        // The multiplayer and Storyteller half: 1.1 does nothing with them in a single-player game
        // — each tests the network mode first and returns (the DirectPlay one aside).
        c["statustest"] = _ => null;                     // The Storyteller's menu, in a network game only
        c["stcmd"] = c["stcmd2"] = _ => null;            // A Storyteller's script command
        c["aura"] = _ => null;                           // A network player's aura line
        c["ping"] = _ => PingReply(game.Ping?.Invoke());   // A network client only
        // `logwindow [on|off]` (ours): the game's log window, a viewer of its own (LogWindowCommand).
        c["logwindow"] = a => LogWindowCommand(a, game.LogWindow);
        c["rename"] = c["humanity"] = _ => null;         // A network host with Storyteller moderation
        // `emit [particle]`: a thing of the `testemitter` template made where the player
        // stands. A particle name is **written into the template itself** (the tag 0x77 string,
        // into the handler), so it sticks: every later `emit`, named or not, makes that particle.
        c["emit"] = a =>
        {
            if (Here() is not { } place || place.Templates?.Find(TestEmitter) is not { } template)
                return "Could not find base test emitter template.";
            if (!template.Tags.Any(t => t.Id == Particles.ParticleSystem.ParticleNameTag))
                return "Could not find particle in test emitter template.";
            if (a.Length > 0) EmitParticle = a[0].Length > 0x1f ? a[0][..0x1f] : a[0];
            if (Player() is not { } me || place.Spawn(TestEmitter, me) is not { } made) return null;
            if (EmitParticle is { } name) place.Particles.Attach(made, name);
            return null;
        };
    }

    /// <summary>The template `emit` makes.</summary>
    public const string TestEmitter = "testemitter";

    /// <summary>The particle `emit` was last given, which 1.1 keeps by writing it into the template.</summary>
    public static string? EmitParticle { get; set; }

    // ------------------------------------------------------------------------ the globals

    /// <summary>
    /// `maxfps`'s frame length in milliseconds, which the end of 1.1's frame
    /// waits out against the frame's start; 0 is no cap.
    /// </summary>
    public static int FrameCapMs { get; set; }

    /// <summary>The game clock's rate (`timescale`). 1 is real time.</summary>
    public static float TimeScale { get; set; } = 1f;

    /// <summary>`camtest side`'s side.</summary>
    public static int CamTestSide { get; set; }

    /// <summary>The money bag `dropcash` makes (looked up by name, `GoldBag`).</summary>
    public const string GoldBag = "GoldBag";

    // ------------------------------------------------------------------------ the parsing

    /// <summary>`sscanf("%d")` of the first word: null when it is not a number.</summary>
    static int? Int(string[] a) =>
        a.Length > 0 && int.TryParse(a[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : null;

    static float? Float(string[] a) =>
        a.Length > 0 && float.TryParse(a[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : null;

    /// <summary>`strtok` over the argument with these delimiters: the words, empty ones dropped.</summary>
    static string[] Words(string rest, string delimiters) => rest.Split(delimiters.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The engine's on-unless-"off" switches (<c>strtok</c> then <c>_strcmpi("off")</c>): a bare one is off.</summary>
    static bool OnUnlessOff(string[] a) => a.Length > 0 && !a[0].Equals("off", StringComparison.OrdinalIgnoreCase);

    /// <summary>A location by name, or <c>#n</c> for the n-th.</summary>
    static string LocationName(Chronicle ch, string word) =>
        word.StartsWith('#') && int.TryParse(word[1..], out int i) && i >= 0 && i < ch.Count ? ch.NameOf(i) : word;

    static Rng Rng() => Chance.Game?.Console ?? new(Environment.TickCount);   // a game's: the launch's stream

    static string? Say(Action act, string? said)
    {
        act();
        return said;
    }
}
