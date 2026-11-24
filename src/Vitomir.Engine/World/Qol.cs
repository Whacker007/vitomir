// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The quality-of-life switches: things the original never had, which are on unless the player turns
// them off.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Features;

namespace Vitomir.Engine.World;

/// <summary>
/// **Not the same thing as a <see cref="Fixes">fix</see>, and the difference is the whole point.**
/// A fix switches off something 1.1 got *wrong*, so it is off by default and a plain run is 1.1. A
/// QoL is something 1.1 never had at all, and it is **on** by default.
///
/// Decided, after the item pane was checked against 1.1 and the right-click shortcut turned out to be an
/// invention rather than a reproduction:
///
/// > QoL defaults on, because it does not exactly change the experience
/// > the player is having in terms of gameplay; it only makes some things more accessible and
/// > easier.
///
/// **That sentence is also the test for what belongs here.** A QoL changes how you *reach* an
/// outcome — input, camera, convenience. It does not change the outcome: nothing here may touch
/// damage, the dice, the AI, what drops, what a script does or how long anything takes. The moment a
/// switch would change any of those it is a fix, it goes in <see cref="Fixes"/>, and it defaults
/// off. The two registers stay separate for the reason the fixes register exists at all: when a run
/// looks wrong, you want to know in one glance what is not 1.1.
///
/// Settings come from the same chain the fixes and the diagnostics use, each overriding the last: a
/// <c>[qol]</c> section in the <c>vitomir.ini</c> beside the executable, then the player's own under
/// <c>%LOCALAPPDATA%\Vitomir\</c>, then the command line — <c>--no-qol &lt;name&gt;</c> to turn one
/// off and <c>--qol &lt;name&gt;</c> to turn one back on, both repeatable, with
/// <c>--no-qol all</c> for a run with none of them.
///
/// <code>
/// [qol];
/// shift + right-click to put an item on, which the original has no gesture for
/// quickequip = off
/// </code>
///
/// **Each switch is a feature of the registry** (<see cref="GameFeatures.Qol"/>): its
/// name, its default, its one line, what it takes and changes and whose it is in a network game are declared
/// there, once, and <see cref="All"/>, the indexer and <see cref="Known"/> are read from it. The registry also
/// says which of these change the game's rules (<c>quickequip</c>, <c>driving</c>, <c>dropcells</c>: the host's
/// in a network game, and named in a save) and which only one machine's picture, sound or controls.
/// </summary>
public sealed class Qol
{
    /// <summary>The properties against their declarations, made once (before <see cref="Current"/>, which is made from it).</summary>
    static readonly SwitchTable<Qol> Table = new(FeatureKind.Qol, GameFeatures.Registry);

    /// <summary>What is in force this run. Everything on until something says otherwise — but <see cref="WinKey"/> and <see cref="FreeCameraKey"/>.</summary>
    public static Qol Current { get; set; } = new();

    /// <summary>Every switch as its declaration has it by default: on, but <see cref="WinKey"/> and <see cref="FreeCameraKey"/>.</summary>
    public Qol()
    {
        foreach (var s in Table.All) s.Set(this, s.Feature.Default);
    }

    /// <summary>
    /// **quickequip** — <b>shift</b> + right-click on an inventory icon equips a packed item or
    /// takes off a worn one.
    ///
    /// **It was plain right-click until then, and that was wrong.** The original *does* have
    /// a right-click on an icon — it **uses** the item, which is how you read a letter or drink a
    /// potion — so ours was not adding a gesture the original lacked, it was taking one away.
    /// The icon click is the only path for the first mouse binding; the second is registered separately
    /// and the icon's had not been read, which is what made it look absent.
    /// Right-click is faithful again and the shortcut took a modifier.
    ///
    /// It still changes no rule — what happens once a slot is chosen is `Equipment`'s, which is the
    /// engine's — only how many clicks it takes to get there.
    ///
    /// **While a shop is up** it does the same, and one thing more: 1.1 moves nothing while shopping (a
    /// click on your own item offers it for sale), so with this on a press on an item that is dragged a
    /// few pixels lifts it, and the release puts it on, takes it off or moves it by the pane's own rules
    /// (<see cref="Ui.InventoryHands.Press"/>). A click that does not move still offers it for sale.
    /// </summary>
    [Switch("quickequip")] public bool QuickEquip { get; set; }

    /// <summary>
    /// **driving** — steering a character with the keyboard.
    ///
    /// 1.1 is click-to-move and nothing else. Driving is ours, and it is the reason the
    /// <see cref="Fixes.BodyCollision"/> question came up at all. It reaches the same places a click
    /// does, by the same walk graph, at the same speeds.
    /// </summary>
    [Switch("driving")] public bool Driving { get; set; }

    /// <summary>
    /// **camera** — more ways onto the game camera's own controls.
    ///
    /// The game turns its camera with the keypad, zooms it with − and =, swivels it with X and
    /// looks through the character's eyes with Z. This adds the mouse wheel for
    /// the zoom, the arrow keys for the keypad and a middle-button drag for the swivel (the key page's
    /// `dragswivel`; the right button is always 1.1's cast) — each going through
    /// the same <see cref="WorldCamera"/> entry points and the same limits, so no view is reachable
    /// that 1.1's own keys could not reach. With it off, only the game's own controls move it.
    /// </summary>
    [Switch("camera")] public bool Camera { get; set; }

    /// <summary>
    /// **soakpie** — a soak wedge on the character sheet fills in even steps from **0** and is full
    /// at **100**, so the picture agrees with the number printed beside it.
    ///
    /// **The first QoL that corrects something rather than adding it**, and it is here by
    /// design rather than by the rule above. 1.1 picks the wedge's frame as
    /// `clamp(soak, 0, 100) × 0.01 × 7` truncated — sevenths of a hundred squeezed into a
    /// wedge that has only six steps past empty — so a soak under 15 draws nothing at all and one of
    /// 86 already draws full. That is odd enough to count as one of
    /// the base game's bugs that are fixed: it fills up starting at 0, and
    /// is full at 100 or more soak per value. And it belongs with the switches that
    /// are on because it does not change the gameplay.
    ///
    /// It changes a picture and nothing else. The soak, the number in the box and every rule that
    /// reads the soak are the engine's. Off, the wedges are 1.1's again.
    /// </summary>
    [Switch("soakpie")] public bool SoakPie { get; set; }

    /// <summary>
    /// **keys** — the key bindings: every command on whatever
    /// keys or buttons the <c>[keys]</c> section and the key page put it. The defaults are 1.1's own
    /// keys, so with nothing moved this changes nothing; off, 1.1's keys are in force whatever the
    /// file says. Decided and built.
    /// </summary>
    [Switch("keys")] public bool Keys { get; set; }

    /// <summary>
    /// **alttab** — Alt+Tab leaves the game even while it holds the keyboard (below). On is also how
    /// every window behaves without the hold. The pointer stays locked inside, but the
    /// player can Alt+Tab, with a game option for allowing Alt+Tab
    /// and for disabling the Windows key. (SDL's `SDL_HINT_ALLOW_ALT_TAB_WHILE_GRABBED`.)
    /// </summary>
    [Switch("alttab")] public bool AltTab { get; set; }

    /// <summary>
    /// **winkey** — the Windows key does nothing while the game window has the keyboard, so a stray
    /// press mid-fight does not throw up the Start menu. Done by holding the keyboard (SDL's keyboard
    /// grab, which on Windows takes the system keys) while the window has the focus; let go the moment
    /// it loses it. With <see cref="AltTab"/> on, Alt+Tab still gets out. The same request as above.
    ///
    /// **The one QoL that starts off.** Holding the keyboard takes every system key, not only the
    /// Windows key, so it waits to be asked for: *"Stay off."*
    /// </summary>
    [Switch("winkey")] public bool WinKey { get; set; }

    /// <summary>
    /// **whisperecho** — the one who whispers sees their own whisper in the chat box ("Anna whispers to Bob,
    /// '…'", `MP_WHISPERALT`, the string 1.1 has for the moderated host's copy). 1.1 sends a whisper only to
    /// the one whispered to and shows its sender nothing; Vitomir echoed it from the first
    /// chat until a comparison with 1.1 found it was not 1.1's, and it is kept,
    /// on. Off, the sender's machine shows nothing and the speaker's lips move only on the machine the
    /// whisper reaches, as 1.1's. Each machine's own (`Net.HostSwitches`): the host always sends the copy and the
    /// sender's machine shows it or not by its own switch — nothing but what one player sees changes.
    /// </summary>
    [Switch("whisperecho")] public bool WhisperEcho { get; set; }

    /// <summary>
    /// **freecamera** — Ctrl+P takes the game camera into the free camera and back.
    /// In 1.1 the way in is a developer's: the camera key reads Ctrl+P only with the options word's bit
    /// 0x80, which is <c>-devmode</c> on the command line, not on a player's machine in a network game
    /// and, on a host, only in a game made with Storyteller moderation (flag 0x400).
    /// Here it is an option in the game's options menu instead: on,
    /// the player can press Ctrl+P and use that mode; off, that free camera mode does not work. So this switch
    /// stands where the developer switch stood, and <c>--devmode</c> still opens it as 1.1's does; the rest of
    /// 1.1's test is kept (<see cref="FreeCameraGate"/>). Ctrl+P out of the free camera is always read, as it is
    /// in 1.1. **Off by default**, like <see cref="WinKey"/>: it is a way past what the game camera may show, so
    /// it waits to be asked for. Each machine's own (<c>Net.HostSwitches</c>).
    /// </summary>
    [Switch("freecamera")] public bool FreeCameraKey { get; set; }

    /// <summary>
    /// **filmvolume** — the story films and the material films follow the Sound slider.
    /// In 1.1 no slider reaches a film's sound: a story film's stream plays at full volume
    /// whatever the sliders say. How sounds are handled is to change
    /// slightly, to tell speech, sounds, music,
    /// cutscenes and in-game videos apart; the categories are later; this is the switch now, and
    /// <see cref="Audio.FilmSound"/> is the one place a film's level is decided, for a later category to take over.
    /// On (the default), the Sound slider; off, 1.1's full volume. Each machine's own (<c>Net.HostSwitches</c>).
    /// </summary>
    [Switch("filmvolume")] public bool FilmVolume { get; set; }

    /// <summary>
    /// **relock** — the swivel lock (Ctrl and the middle button; the pointer held at the middle of the view,
    /// <see cref="Ui.CentreLook"/>) is only set aside while a pane or a menu is open over the game camera — the inventory,
    /// the disciplines, the escape menu — and comes back on once everything opened is closed. Off, opening one ends it.
    /// A cutscene or the first-person look ends it either way. On by default; each machine's own.
    /// </summary>
    [Switch("relock")] public bool Relock { get; set; }

    /// <summary>
    /// **skipopening** — the logo and the trailer are not played before the title screen. 1.1 plays both every time
    /// it starts, Escape or Space skipping each. Off by default (1.1's way). A film and nothing else, each machine's own.
    /// </summary>
    [Switch("skipopening")] public bool SkipOpening { get; set; }

    /// <summary>
    /// **skipstoryfilms** — the story's films, a new game's opening one among them, end the moment they are asked for:
    /// the script that asked hears `videoended` at once, as when the player presses Escape on a film's first frame, which
    /// is 1.1's own path (and its `-nocutscenes`). The scripted scenes played in the world are not films and are untouched.
    /// Off by default. **Single player only**: in a network game the story's films are as they always were.
    /// </summary>
    [Switch("skipstoryfilms")] public bool SkipStoryFilms { get; set; }

    /// <summary>
    /// **dropcells** — an item put down in the pack, the vault or another item grid goes into the cells its
    /// picture covers most, at every edge of the grid (<see cref="Ui.DropCell"/>). 1.1 already puts it where
    /// its picture is — the drawn top-left rounded to the nearest cell — but rounds by truncating,
    /// so at the **top and left** edges an item hanging off by more than half a cell (up to a cell and a
    /// half) is pulled in, where at the right and bottom one is refused. On, an item drops
    /// into the squares it mostly covers on screen, and only if it is a valid place to put it
    /// down: the nearest cell at every edge; off, 1.1's rounding. Everything else — the swap with one
    /// item under it, the refusal over two, off the grid or into a grid it does not fit — is 1.1's either way.
    /// </summary>
    [Switch("dropcells")] public bool DropCells { get; set; }

    /// <summary>
    /// **heldshift** — holding Shift with the button down keeps swinging the weapon's second attack. 1.1 asks at the end
    /// of every swing whether the player is still asking (<see cref="Rpg.SwingRepeat"/>), and means a held Shift to ask
    /// for the second attack, but it tests Windows' `VK_LSHIFT` and `VK_RSHIFT` against the key-state array indexed by
    /// scan code — the Mute and Calculator keys — so a Shift+click swings the second attack once and the first after
    /// that. With this on, a held Shift goes on asking for the second.
    ///
    /// On by default, a quality of life rather than a Fix (Whacker007, 2026-11-20: *"holding down shift will keep doing
    /// the high accuracy spin attack, instead of just doing it once"*; and that it does not make a game modded). The middle
    /// button held asks for the second attack in 1.1 already.
    /// </summary>
    [Switch("heldshift")] public bool HeldShift { get; set; }

    /// <summary>
    /// **spawnwalls** — a creature made during play cannot walk through walls once it has found the ground. A thing made
    /// at run time has no surface until a step lands over ground (<see cref="WorldThing.Unsurfaced"/>), and a script's
    /// walk for such a thing is one straight leg at the point, with no route looked for (<see cref="Player"/>'s walk with
    /// no node). 1.1 walks the whole leg so, through whatever stands in the way, even after its first step has landed it
    /// on the floor: a feeder rat out of a barrel, sent 64 to 320 units off by its script, runs straight there through
    /// walls and out of the level. With this on, the moment such a walker finds its feet it looks for a route to the same
    /// point by the walk map, as any walk does, and stops if there is none (its script sends it elsewhere later). Until
    /// it finds its feet it walks as 1.1's does, so the silver mines' rats, made inside the rock of their holes, still come
    /// out (<see cref="SurfacelessStep"/>).
    ///
    /// On by default, a quality of life rather than a Fix (Whacker007, 2026-11-20: *"those feeder rats can't run outside
    /// of game level bounds, like through walls or other solid objects"*; and that it does not make a game modded).
    /// </summary>
    [Switch("spawnwalls")] public bool SpawnWalls { get; set; }

    /// <summary>
    /// **partyorder** — a companion's portrait can be dragged along the bar to put the party in a new order
    /// (<see cref="Ui.PartyDrag"/>): dropped over the middle of another companion's portrait the two swap places, dropped
    /// between two portraits it goes in between them, and anywhere else nothing happens. The bar shows the order the party
    /// will be in while the portrait is carried. The leader is always the first. 1.1 has no way to change the order: its
    /// roster keeps the members in the order they joined. With this on and three or more in the party, a plain click on a
    /// companion's portrait takes them over on the release rather than the press, which is what lets it wait to see whether
    /// it is a drag. And a portrait's click reaches the whole head drawn over it, not just 1.1's hotspot below its top
    /// (<see cref="Ui.StatusBar.HeadReach"/>).
    ///
    /// On by default, a quality of life rather than a Fix (Whacker007, 2026-11-20: *"Christof is always the party leader in
    /// slot 1, but the party members can be swapped around"*). Not in a network game.
    /// </summary>
    [Switch("partyorder")] public bool PartyOrder { get; set; }

    /// <summary>
    /// Every switch, by the name the ini and the command line use, with whether it is on here and its
    /// declaration's one line — in the registry's order, which is the Fixes page's (and the order
    /// <see cref="ToString"/> names the ones that are off in).
    /// </summary>
    public IEnumerable<(string Name, bool On, string What)> All =>
        Table.All.Select(s => (s.Feature.Name, s.Get(this), s.Feature.What));

    /// <summary>
    /// A switch by name, whatever the case. A name that is no quality-of-life switch reads off and sets
    /// nothing; setting <c>all</c> sets every one (<c>--no-qol all</c>).
    /// </summary>
    public bool this[string name]
    {
        get => Table.Find(name) is { } s && s.Get(this);
        set
        {
            if (Table.Find(name) is { } s) s.Set(this, value);
            else if (name.Equals("all", StringComparison.OrdinalIgnoreCase))
                foreach (var each in Table.All) each.Set(this, value);
        }
    }

    /// <summary>Whether a name is one of the switches (<c>all</c> counts). Read from the registry, as <see cref="All"/> is.</summary>
    public static bool Known(string name) =>
        name.Equals("all", StringComparison.OrdinalIgnoreCase) || Table.Find(name) is not null;

    static bool Truthy(string v) => v.Trim().ToLowerInvariant() is "1" or "on" or "true" or "yes";

    /// <summary>
    /// Reads the settings chain and installs the result as <see cref="Current"/>. **A test must
    /// call <see cref="Parse"/> instead** — see the same note on <see cref="Fixes.Configure"/>.
    /// </summary>
    public static Qol Configure(string[]? args = null, string? installedDir = null,
                                Action<string>? complain = null)
    {
        Current = Parse(args, installedDir, complain);
        return Current;
    }

    /// <summary>
    /// The same reading with **no side effect**. Unknown names are reported rather than ignored,
    /// for the same reason a misspelled fix is. Either flag takes one name, several with commas between,
    /// or <c>all</c>; and the reading ends by settling what it read, as <see cref="Fixes.Parse"/> does
    /// (<see cref="FeatureRegistry.Settle(Qol, Action{string}?)"/>).
    /// </summary>
    /// <param name="userPath">The player's own settings file; the machine's by default. A test names one that is not
    /// there, so a switch the player turned on cannot leak into it.</param>
    public static Qol Parse(string[]? args = null, string? installedDir = null,
                            Action<string>? complain = null, string? userPath = null)
    {
        var qol = new Qol();
        var installed = Path.Combine(installedDir ?? Diag.InstalledFolder, Diag.FileName);
        qol.Apply(IniFile.Read(installed), complain);
        var user = userPath ?? Diag.UserSettingsPath;   // a test names its own (the player's own file leaked in)
        if (!string.Equals(user, installed, StringComparison.OrdinalIgnoreCase))
            qol.Apply(IniFile.Read(user), complain);

        for (int i = 0; args is not null && i < args.Length; i++)
        {
            bool off = args[i] == "--no-qol";
            if (!off && args[i] != "--qol") continue;
            if (i + 1 >= args.Length)
            {
                complain?.Invoke($"{args[i]} wants the name of a quality-of-life switch after it");
                break;
            }
            foreach (var name in Fixes.Names(args[++i]))
            {
                if (!Known(name)) { complain?.Invoke($"{args[i - 1]} {name}: no such quality-of-life switch"); continue; }
                qol[name] = !off;
            }
        }
        GameFeatures.Registry.Settle(qol, complain);
        return qol;
    }

    void Apply(IniFile ini, Action<string>? complain)
    {
        foreach (var (key, value) in ini.Values)
        {
            if (!key.StartsWith("qol.", StringComparison.OrdinalIgnoreCase)) continue;
            var name = key["qol.".Length..];
            if (!Known(name)) { complain?.Invoke($"[qol] {name}: no such quality-of-life switch"); continue; }
            this[name] = Truthy(value);
        }
    }

    /// <summary>What to print. Says which are **off**, because on is the default here (a plain run prints <c>off: freecamera, winkey</c>).</summary>
    public override string ToString()
    {
        var off = All.Where(q => !q.On).Select(q => q.Name).ToList();
        return off.Count == 0 ? "all on" : "off: " + string.Join(", ", off);
    }
}
