// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Fixes switches: which of the original's own faults the player has chosen to turn off.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Features;

namespace Vitomir.Engine.World;

/// <summary>
/// Vampire 1.1 gets a number of things wrong, and Vitomir reproduces them on purpose
/// This is where the player says which of them
/// to switch off — the in-game **Fixes** menu, ahead of there being a menu to
/// put it in.
///
/// **Every fix is off by default.** The faithful behaviour is the baseline, so a plain run is 1.1
/// and anything that differs is something the player asked for. Faithful by default,
/// so that whoever plays can recognise the game and call out what looks off
/// against it.
///
/// Settings come from the same places the diagnostics do, each overriding the last: a
/// <c>[fixes]</c> section in the <c>vitomir.ini</c> beside the executable, then the player's own
/// under <c>%LOCALAPPDATA%\Vitomir\</c>, then <c>--fix &lt;name&gt;</c> on the command line
/// (repeatable, and <c>--fix all</c> turns on everything).
///
/// <code>
/// [fixes];
/// the walk cycle follows how fast the character is actually moving
/// movespeed = on
/// matchedgait = on
/// </code>
///
/// **Each switch is a feature of the registry** (<see cref="GameFeatures.Fixes"/>): its
/// name, its one line, what it takes and changes and whose it is in a network game are declared there, once. This
/// class is the typed face of that — a property a switch, tied to its declaration by name
/// (<see cref="SwitchAttribute"/>) — and <see cref="All"/>, the indexer and <see cref="Known"/> are read from the
/// registry, so there is no second list of names here.
/// </summary>
public sealed class Fixes
{
    /// <summary>The properties against their declarations, made once (before <see cref="Current"/>, which is made from it).</summary>
    static readonly SwitchTable<Fixes> Table = new(FeatureKind.Fix, GameFeatures.Registry);

    /// <summary>What is in force this run. Faithful until something says otherwise.</summary>
    public static Fixes Current { get; set; } = new();

    /// <summary>Every switch as its declaration has it by default: off.</summary>
    public Fixes()
    {
        foreach (var s in Table.All) s.Set(this, s.Feature.Default);
    }

    /// <summary>
    /// **movespeed** — a locomotion cycle's playback rate follows the character's *current* speed
    /// against the speed its own template gives it, for the modes the game marks with the
    /// <c>movespeed</c> flag. This is the flag's own intent: the original parses it and never reads
    /// it, so a character under Celerity moves at up to four times its walk speed with a completely
    /// unchanged walk cycle.
    ///
    /// It changes nothing until something modifies a character's movement speed, which the effect
    /// subsystem can now do — <c>EffectRuntime.Recompute</c> folds MOVESPEED onto the actor, and a
    /// drink applies one, and so does a cast (Celerity puts <c>ef_disc_celerity1</c> on the caster).
    /// <see cref="MatchedGait"/> refines it: with both on, that one has the say wherever a stride can be
    /// measured and this one keeps the rest (<c>Player.RateFor</c>).
    /// </summary>
    [Switch("movespeed")] public bool MoveSpeed { get; set; }

    /// <summary>
    /// **matchedgait** — a locomotion cycle plays at the rate that makes its feet match the ground,
    /// measured from the animation's own footfall tags (<see cref="Anim.Stride"/>). This is the one
    /// that changes what you see today: over the 477 measurable walk modes the median character is
    /// moving **1.47×** faster than the pace its animation was drawn for, which is the skating.
    ///
    /// Ours, not the original's, and the more invented of the two. **It refines <see cref="MoveSpeed"/>** and
    /// needs it on: the two decide the same thing — a cycle's rate — so the registry allows
    /// this one only as a refinement of that one, and on its own it is refused.
    /// </summary>
    [Switch("matchedgait")] public bool MatchedGait { get; set; }

    /// <summary>
    /// **bodycollision** — a character being driven from the keyboard has to fit its whole body,
    /// tested against the level's own geometry, wherever it puts its feet.
    ///
    /// Off, because 1.1 does not do it: a walker asks the body test and the engine keeps only the
    /// *thing* results out of that query, throwing the surfaces away. It trusts the walk graph,
    /// which the level compiler laid out. Vitomir's click-to-move already works that way; the
    /// keyboard tested the body as well, because driving is our own addition and it looked like the
    /// safe choice.
    ///
    /// It was not. Measured over 39,215 points of walkable ground across ten levels, the body test
    /// refuses **42.5%** of the floor the game lets you stand on — every staircase among it, because
    /// the test samples a ring of radius 32 at three heights and a stair's own riser is inside that
    /// ring. Meanwhile the walk graph on its own puts a walker's centre inside solid geometry
    /// **0.8%** of the time, and in five of the ten levels never. So the body test is not what keeps
    /// a driver out of walls; the walk graph is.
    ///
    /// What turning it on buys is the 32-unit margin: the driver's centre stops short of the edge of
    /// the ground rather than reaching it, so his shoulder never overhangs. That is the
    /// corner-clipping entry in the register, and this is where a stricter body test belongs when
    /// "Better collision" is built — applied to both ways of moving, not only to the keyboard.
    /// </summary>
    [Switch("bodycollision")] public bool BodyCollision { get; set; }

    /// <summary>
    /// **glowcolours** — the hover glow in the colours 1.1 asks for: a door or a lever a
    /// dim yellow, an enemy a dark red, something to pick up teal, a person to talk to dark blue.
    ///
    /// Off, because the reference build does not show those. The interface's hover writes
    /// `0xFF505000` for a door and `rlDirect.dll` hands it to Direct3D untouched as the texture
    /// factor, which reads it as red 0x50, green 0x50, blue 0 — but the reference build draws through
    /// the GOG DirectX 1-7 wrapper, and on it the door gains (0, 80, 80) on every pixel: cyan. The
    /// outer two bytes arrive exchanged, so an enemy is blue rather than red. Measured off a
    /// video of Monastery 1. See <see cref="Ui.HoverGlow"/>.
    /// </summary>
    [Switch("glowcolours")] public bool GlowColours { get; set; }

    /// <summary>
    /// **shapetorch** — a character taking another shape lets go of the utility item in its off
    /// hand (the torch, above all) as the utility's wear with null does — out of the hand and out of the world, its
    /// flame out — but keeps it **in its slot**, still equipped, and has it back in the hand, lit,
    /// when the shape ends (<see cref="Rpg.ShapeTorchFix"/>). So the shape starts with nothing in
    /// that hand, and a full pack is no obstacle.
    ///
    /// Off, because 1.1 does not: the shape keeps the torch, a wolf wears it on its bone 29 — the
    /// middle of its tail — and the torch's utility stance asks the wolf's motion set for
    /// `util_punch`, which it lacks, so the wolf swings nothing and bites nobody while it holds it.
    /// Confirmed in 1.1 by a
    /// run and video: the torch at the wolf's tail, the attack order standing in its
    /// stance with no act for 2.3 s. Hence a Fix for it.
    ///
    /// Every shape, not only the wolf: the item leaves the hand before the shape goes on and comes
    /// back when it ends, a save taken in between included. A shape that is refused (the template
    /// missing, one already on) takes nothing. Ours. (Its first version put it in the pack, which
    /// a full pack could not take;: *"figure out a way to just hide it"*.)
    /// </summary>
    [Switch("shapetorch")] public bool ShapeTorch { get; set; }

    /// <summary>
    /// **liverollover** — a rollover's lines kept current while the pointer rests on what it
    /// describes: rebuilt every frame, so a party portrait's `Health: … Blood: … Frenzy: …` climbs as
    /// the member feeds, a hovered character's name reddens as it is hurt or clears as it heals, and a
    /// price or an item's name follows what changed under it (<see cref="Ui.LiveRollover"/>).
    ///
    /// Off, because 1.1 writes a rollover only when the hover changes: the interface's hover runs a
    /// control's rollover when it is not last frame's control and a
    /// world thing's when it is not last frame's thing, and the only
    /// other writer is the hover's refresh, for a few events — the hovered thing hurt, an item
    /// identified, the character sheet's, discipline pane's and pack's own actions. 1.1 does the same: it
    /// updates only when the pointer moves
    /// off and back over, which is a rendering bug; so a Fix at least, and
    /// a candidate to be always on, but a Fix for now and flagged as a permanent
    /// candidate. **A candidate to become permanent.** Ours.
    /// </summary>
    [Switch("liverollover")] public bool LiveRollover { get; set; }

    /// <summary>
    /// **soakedvoid** — a blow soaked away whole flashes Fortitude's white shell: the two shipped
    /// handlers of the soak message, `EffectDiscFortitude.soaked` and `EffectDiscSkinOfTheAdder.soaked`,
    /// are called where 1.1 would call a `soaked` of its own shape, and each sets a white shell on its
    /// character, faded in over a second and out over the next, whenever a soak roll keeps off the whole
    /// blow (<see cref="Rpg.SoakedMessage"/>, `Scripting.CodexScenes.Soaked`).
    ///
    /// Off, because 1.1 never calls them: the engine looks each callback up by name **and full
    /// signature** (the native message table has `soaked(IIFIFI)F`), and both
    /// shipped classes declare it returning `void`, so the method is never found and the message
    /// (sent from the soak roll) reaches nobody. That is a
    /// bug to fix, clearly not intended behaviour. Their `void` has no answer, so the soak itself
    /// is the same either way — only the flash differs. Ours.
    /// </summary>
    [Switch("soakedvoid")] public bool SoakedVoid { get; set; }

    /// <summary>
    /// **netprops** — in a network game the host sends the players' machines the props a script makes flagged
    /// `NO NET SYNCH` that no player's machine makes for itself: a worn item's (the lit torch's flame) and a
    /// place's or a thing's (`ConveyerHold`'s blood, `MysticSpawner`'s magic) (`Net.NetSynch.Kept`,
    /// `Net.MadeHere.Scripted`).
    ///
    /// Off, because 1.1's host sends nothing so flagged, and its player's machine skips a
    /// worn item's scripts and runs no place's or thing's: so on a player's screen another character's
    /// lit torch is dark. The host's switch (it changes what the game sends). Ours.
    /// </summary>
    [Switch("netprops")] public bool NetProps { get; set; }

    /// <summary>
    /// **berserker** — the Berserker weapon's strongest frenzy takes hold. `ItemWeaponBerserker`'s `hit`
    /// rolls a frenzy level from 0 to 5 and looks its length up in `FRENZY_DURATIONS`, which has five
    /// entries, so level 5 — every hit worth 99 or more, and the top of any other roll — throws
    /// `ArrayIndexOutOfBoundsException` before the frenzy or its red shell. With this on, the blow ends as
    /// the script meant it to: `ef_item_frenzy` at level 5 (the effect table has a level 5, strength 10)
    /// for the table's longest length, its last entry (10.5 s; the table has no sixth), and the red cloud
    /// shell (<c>Scripting.CodexScenes.Berserker</c>).
    ///
    /// Off, because 1.1 throws, and a strong wielder's heavy hits never frenzy. A Fix with a toggle.
    /// The host's switch in a network game (scripts run there).
    /// Ours.
    /// </summary>
    [Switch("berserker")] public bool Berserker { get; set; }

    /// <summary>
    /// **wakeonhit** — a character a script holds still is let go when a party member hurts it. The
    /// Society of Leopold's levels hold dozens of soldiers with `ActorAIOff_TriggerOn`
    /// (`THING_AF_AIPAUSED` until the player enters their region); 1.1's damage event hands a
    /// held character its attacker, but its think is gated on the flag and nothing clears it
    /// on damage, so one shot at from outside its region stands there until it is dead. With this on,
    /// the blow takes the hold off and it fights (<c>Ai.Minds.WakeOnHit</c>).
    ///
    /// Off, because 1.1 leaves it standing: humans can be shot at and
    /// damaged from the greatest range and do not react, so a Fix for it.
    /// The host's switch in a network game (the AI runs there). Ours.
    /// </summary>
    [Switch("wakeonhit")] public bool WakeOnHit { get; set; }

    /// <summary>
    /// **gunreach** — a gun fired from the very edge of its reach hits. 1.1 lets an attacker fire when
    /// the gap between the two bodies is within the weapon's `MAXDISTANCE`, and then casts
    /// the shot from the muzzle for the projectile's own range — the same 1,000 on nearly
    /// every gun — so from the last twenty units or so of the reach the ray ends just short of the
    /// target and every shot misses. With this on the ray is longer by the shooter's own radius and a
    /// little (<see cref="GunReachSlack"/>), which is what the reach test took off.
    ///
    /// Off, because 1.1 misses (two clips from 1, 062 units
    /// did nothing): a Fix for it, off by default. The host's switch in a network game.
    /// </summary>
    [Switch("gunreach")] public bool GunReach { get; set; }

    /// <summary>
    /// **heldattack** — a held attack starts the moment the character is free. 1.1 asks whether the attack button is still
    /// held only at the end of a swing (<c>Actions.Swinging</c>, under the Controls screen's *auto repeat attack*), so a click
    /// on an enemy while a discipline is being cast is refused as busy, and the button held on afterwards does nothing; nor
    /// does it after a blow makes the character flinch. With this on, while the button is held on something to attack, the
    /// attack starts at the first moment the character is free — doing nothing, or done flinching — Shift held asking for the
    /// second attack as the held swing's repeat does (<c>[qol] heldshift</c>): attack constantly, starting and restarting
    /// itself whenever able. Only while the button is held, and only with *auto repeat attack* on.
    ///
    /// Off, because 1.1 does not: a Fix, off by default. The host's switch in a network
    /// game, for the host's own character (a player's held button is asked at her swing's end, as ever).
    /// </summary>
    [Switch("heldattack")] public bool HeldAttack { get; set; }

    /// <summary>What <see cref="GunReach"/> adds beyond the shooter's radius: the slant of a shot from a raised muzzle. Ours.</summary>
    public const float GunReachSlack = 8f;


    // **`bounceminimum` and `bounceboost` were here, and were removed.**
    // They switched off the two untextbook additions in `simThing::OnSurfaceContact` —
    // the −1 floor on the normal speed, and the bare 0.1 added to the impulse — so that slow
    // contacts settled smoothly instead of being kicked. They are gone by design: the
    // physics of the gibs is the base game's own janky physics for things, until some better
    // physics is wired up for objects in certain situations.
    //
    // So the engine now does both unconditionally, which is what 1.1 does. The readings themselves
    // are not lost — they are in `World/Physics.cs` beside the constants.
    // A switch that nobody wants is worse than no switch: it is a branch that has to stay correct.

    /// <summary>
    /// Every switch, by the name the ini and the command line use, with whether it is on here and its
    /// declaration's one line — in the registry's order, which is the Fixes page's.
    /// </summary>
    public IEnumerable<(string Name, bool On, string What)> All =>
        Table.All.Select(s => (s.Feature.Name, s.Get(this), s.Feature.What));

    /// <summary>
    /// A switch by name, whatever the case. A name that is no Fix reads off and sets nothing; setting
    /// <c>all</c> sets every one (<c>--fix all</c>).
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

    /// <summary>
    /// Whether a name is one of the switches (<c>all</c> counts). **Read from the registry**, as
    /// <see cref="All"/> is. It had once been a hand-written list of two, so `bodycollision`,
    /// `bounceminimum` and `bounceboost` had been unreachable from both the ini and the command line
    /// since the day each was added — `--fix bounceminimum` answered *no such fix*. A list that has to
    /// be kept in step with another list does not stay in step.
    /// </summary>
    public static bool Known(string name) =>
        name.Equals("all", StringComparison.OrdinalIgnoreCase) || Table.Find(name) is not null;

    /// <summary>The ini and command-line values that describe the same as <c>on</c>.</summary>
    static bool Truthy(string v) =>
        v.Trim().ToLowerInvariant() is "1" or "on" or "true" or "yes";

    /// <summary>
    /// Reads the settings chain and installs the result as <see cref="Current"/>.
    ///
    /// **A test must call <see cref="Parse"/> instead.** This one writes a process-wide static, and
    /// xUnit runs test classes in parallel, so a test that calls this changes what every other test
    /// running at that moment sees — which is exactly what happened: a new test called
    /// `Configure` once per fix and left `bounceboost` on, and two physics tests in another class
    /// failed with the engine's spare tenth missing. Restoring it in a `finally` is not enough; the
    /// window is still open.
    /// </summary>
    public static Fixes Configure(string[]? args = null, string? installedDir = null,
                                  Action<string>? complain = null)
    {
        Current = Parse(args, installedDir, complain);
        return Current;
    }

    /// <summary>
    /// The same reading with **no side effect**: it answers what the settings say and installs
    /// nothing. Unknown names are reported rather than ignored, because a misspelled fix that
    /// silently does nothing is worse than one that complains.
    ///
    /// <c>--fix</c> takes one name, several with commas between (<c>--fix gunreach,wakeonhit</c>) or
    /// <c>all</c>, and may be given more than once. **The reading ends by settling what it read**
    /// (<see cref="FeatureRegistry.Settle(Fixes, Action{string}?)"/>): a switch the registry refuses beside
    /// the others — <c>matchedgait</c> without the <c>movespeed</c> it refines — is left off and complained
    /// about by name, with the reason.
    /// </summary>
    /// <param name="userPath">The player's own settings file; the machine's by default. A test names one that is not
    /// there, so a switch the player turned on cannot leak into it.</param>
    public static Fixes Parse(string[]? args = null, string? installedDir = null,
                              Action<string>? complain = null, string? userPath = null)
    {
        var fixes = new Fixes();
        var installed = Path.Combine(installedDir ?? Diag.InstalledFolder, Diag.FileName);
        fixes.Apply(IniFile.Read(installed), complain);
        var user = userPath ?? Diag.UserSettingsPath;   // a test names its own (the player's own file leaked in)
        if (!string.Equals(user, installed, StringComparison.OrdinalIgnoreCase))
            fixes.Apply(IniFile.Read(user), complain);

        for (int i = 0; args is not null && i < args.Length; i++)
        {
            if (args[i] != "--fix") continue;
            if (i + 1 >= args.Length) { complain?.Invoke("--fix wants the name of a fix after it"); break; }
            foreach (var name in Names(args[++i]))
            {
                if (!Known(name)) { complain?.Invoke($"--fix {name}: no such fix"); continue; }
                fixes[name] = true;
            }
        }
        GameFeatures.Registry.Settle(fixes, complain);
        return fixes;
    }

    /// <summary>The names in one command-line value: one, or several with commas between.</summary>
    internal static IEnumerable<string> Names(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } names ? names : [value];

    void Apply(IniFile ini, Action<string>? complain)
    {
        foreach (var (key, value) in ini.Values)
        {
            if (!key.StartsWith("fixes.", StringComparison.OrdinalIgnoreCase)) continue;
            var name = key["fixes.".Length..];
            if (!Known(name)) { complain?.Invoke($"[fixes] {name}: no such fix"); continue; }
            this[name] = Truthy(value);
        }
    }

    public override string ToString()
    {
        var on = All.Where(f => f.On).Select(f => f.Name).ToList();
        return on.Count == 0 ? "faithful (no fixes)" : string.Join(", ", on);
    }
}
