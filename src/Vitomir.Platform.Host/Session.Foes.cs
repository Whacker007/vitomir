// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Host;

/// <summary>
/// Foes to fight in play (<c>--foe &lt; who&gt; @x, y, z[,yaw]</c>; <i>"Set up a fight where I can
/// go after one"</i>). Each is a hostile fighter made as the arena makes its fighters (<c>Session.Arena.cs</c>) — a body where it is
/// asked for, said to the modules as a person of its 1.1 character, drawn and animated as that character holding its preset's weapon,
/// armed, its shield checked, given its preset's sheet through the rules module — and driven by the duelling brain against the player
/// (<see cref="DuelBrain.For"/>, the brain's default numbers, as the arena's <c>fight-*.json</c> fighters have). Several come at once.
/// A foe is not an observer and has no routes: it always knows where the player is and closes straight (the free yard is open ground);
/// while the player is down (a zero-Health stop) it stands, and comes again when the sandbox recovery gets him up.
/// </summary>
public sealed partial class Session
{
    readonly List<EntityId> foes = [];

    /// <summary>The archers' numbers' page in the archer brains' book (the module's data), once added.</summary>
    int? archerBooks;

    /// <summary>Whether any foe asked for is an archer (its world then has the bow and the archer brain registered).</summary>
    internal static bool FoeArchersIn(SessionOptions options, LoadedGame modules)
    {
        if (options.Foes.Count == 0 || options.Arena is not null) return false;
        var presets = FoePresets.From(modules, _ => { });
        foreach (var text in options.Foes)
            if (FoeRequest.TryParse(text, out var r, out _) && presets.TryGetValue(r.Who, out var p) && p.Archer) return true;
        return false;
    }

    /// <summary>The templates of the archer foes asked for (their bow's 1.1 look is loaded).</summary>
    internal static IEnumerable<string> FoeArcherTemplates(SessionOptions options, LoadedGame modules)
    {
        if (options.Foes.Count == 0 || options.Arena is not null) yield break;
        var presets = FoePresets.From(modules, _ => { });
        foreach (var text in options.Foes)
            if (FoeRequest.TryParse(text, out var r, out _) && presets.TryGetValue(r.Who, out var p) && p.Archer) yield return p.Template;
    }

    /// <summary>The foes placed (<c>--foe</c>), in the order given; empty without them.</summary>
    public IReadOnlyList<EntityId> Foes => foes;

    /// <summary>
    /// The foes' characters registered with the content (each preset's template with its weapon, under the arena's key
    /// <see cref="ArenaCast.KindKey"/>, once): a foe that is no preset, or whose character or weapon 1.1 has not, stops the start by name.
    /// </summary>
    static void RegisterFoes(SessionOptions options, LoadedGame modules, OneOneLibrary library, CharacterAssets assets, Dictionary<string, int> kinds,
        Dictionary<string, TextureImage> textures, GameInstall install, Action<string> say)
    {
        if (options.Foes.Count == 0) return;
        var presets = FoePresets.From(modules, say);
        foreach (var text in options.Foes)
        {
            if (!FoeRequest.TryParse(text, out var request, out string error)) { install.Dispose(); throw new SessionException(error); }
            FoePreset p;
            try { p = FoePresets.Find(presets, request.Who); }
            catch (SessionException) { install.Dispose(); throw; }
            string key = ArenaCast.KindKey(p.Template, p.Weapon);
            if (kinds.ContainsKey(key)) continue;
            if (p.Weapon is { } w && OneOneWeapon.From(library.Templates, w) is null)
            {
                install.Dispose();
                throw new SessionException($"--foe {request.Who}: there is no 1.1 weapon template '{w}' (the preset's weapon)");
            }
            var c = library.Character(p.Template, p.Weapon);
            if (c is null)
            {
                install.Dispose();
                throw new SessionException($"--foe {request.Who}: there is no 1.1 character '{p.Template}' (the preset's template)");
            }
            kinds[key] = OneOneLibrary.Register(assets, c, options.StitchedAsQuick, !options.Switches.Contains(OneOneStitched.SwingsOneOneSwitch));
            foreach (var t in c.Textures) textures.TryAdd(t.Name, t);
            say(ActionsLine(c));
        }
    }

    /// <summary>
    /// The foes asked for, placed after the townsfolk and the witnesses (<see cref="Populate"/>): each made, dressed, armed and given
    /// its sheet as the arena's fighters are, then the duelling brain against the player; named "<i>Szlachta (foe)</i>" ("<i>(foe 2)</i>",
    /// … when there are several), and said in the log.
    /// </summary>
    void PlaceFoes(IReadOnlyDictionary<string, int> kinds, CharacterAssets assetsInWorld, PeopleHooks people)
    {
        if (options.Foes.Count == 0) return;
        var presets = FoePresets.From(modules, options.Say);
        world.TryService<IPersonSheets>(out var sheets);
        if (!world.TryService<DuelBrainBook>(out var book)) throw new SessionException("--foe: this world has no duelling brain (it is registered with the fights of perception's, in play)");
        // The brain's defaults (the arena's fighters' numbers), by the fights' own book so a fight of perception's keeps its indices.
        int numbers = world.TryService<WorldFightBook>(out var fightBook) ? fightBook.BookOf(world, -1) : book.Add(new DuelBrainSettings());
        ref var playerAnim = ref world.Store<CharacterAnimation>().Find(player);
        float playerReach = System.Runtime.CompilerServices.Unsafe.IsNullRef(ref playerAnim) || playerAnim.Kind < 0 ? DuelBrains.BareReach : DuelBrains.ReachOf(assetsInWorld, playerAnim.Kind);
        int n = 0;
        foreach (var text in options.Foes)
        {
            if (!FoeRequest.TryParse(text, out var request, out string error)) throw new SessionException(error);
            var p = FoePresets.Find(presets, request.Who);
            var e = MovementServices.SpawnBody(world, request.Feet, DetMath.WrapAngle(request.YawDegrees * DetMath.DegToRad), Locomotion.Sandbox);
            people.Made(e, PersonRole.Townsperson, p.Template);       // its character named, so a module dresses it as that character
            int kind = kinds.TryGetValue(ArenaCast.KindKey(p.Template, p.Weapon), out int k) ? k : -1;
            if (kind >= 0)
            {
                var walk = assetsInWorld.Kind(kind).Motion(MotionRole.Walk).NaturalSpeed;
                if (walk > 0.05f) world.Get<Locomotion>(e).WalkSpeed = walk;
                Animate(e, assetsInWorld, kind);
            }
            else CombatServices.Arm(world, e);
            GiveMovement(e, $"--foe {request.Who}", p.Template, p.Movement);   // the preset's movement (the szlachta's: its 1.1 template's)
            n++;
            names[e.Packed] = $"{ShownName(p.Shown)} (foe{(options.Foes.Count > 1 ? $" {n}" : "")})";
            foes.Add(e);
            if (p.Shield is { } wantsShield && kind >= 0 && HoldsShield(assetsInWorld, kind) != wantsShield)
                throw new SessionException($"--foe {request.Who}: the preset says \"shield\": {(wantsShield ? "true" : "false")}, but {p.Template} with {p.Weapon ?? "its own weapon"} holds {(wantsShield ? "none" : "one")}");
            if (sheets is null) throw new SessionException($"--foe {request.Who}: the game's modules keep no sheets, so its sheet {p.Sheet} cannot be given");
            if (!sheets.TryTemplate(e, p.Sheet, out string why)) throw new SessionException($"--foe {request.Who}: its sheet {p.Sheet}: {why}");
            float reach = kind >= 0 ? DuelBrains.ReachOf(assetsInWorld, kind) : DuelBrains.BareReach;
            // The preset's approach (the szlachta's: 1.1's manoeuvre, with its AI file's numbers) on its own page of the book.
            int mine = numbers;
            if (p.Approach == DuelApproach.OneOne && !FoesUniform)
                mine = book.Add(Approached(book.All[numbers] with { Approach = DuelApproach.OneOne }, $"--foe {request.Who}", p.Template));
            // An archer carries its bow and is driven by the archer brain (the duelling brain only once it draws its sword).
            if (p.Archer && world.TryService<ArcherBrainBook>(out var archers) && BowServices.Give(world, e, p.Ranged!, p.Arrows))
            {
                LookTheBow(e, kinds, p.Template);
                archerBooks ??= archers.Add(world.Service<BowConfig>().Archer);
                world.Set(e, ArcherBrain.For(player, archerBooks.Value, mine, reach, playerReach) with { WakeMetres = p.WakeMetres });
                var a = world.Service<ArcherBrainBook>().All[archerBooks.Value];
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"foe: {names[e.Packed]} (e{e.Index}), {p.Name}: {p.Template} with {p.Ranged} ({p.Arrows} arrows) and {p.Weapon ?? "its own weapon"}, sheet {p.Sheet}, at {request.Feet.X:0.##} {request.Feet.Y:0.##} {request.Feet.Z:0.##} facing {request.YawDegrees:0.#}°; the archer brain against the player (it comes on to {a.KeepMetres:0.#} m, aims at the chest, draws, holds {a.HoldSeconds:0.##} s and looses, its aim's error by its Range times {a.ErrorScale:0.##}; within {a.SwordMetres:0.#} m it draws its sword: the duelling brain, reach {reach:0.00} m); {(p.WakeMetres > 0f ? $"it waits until the player is within {p.WakeMetres:0.#} m or something meets it" : "it comes at once")}"));
                continue;
            }
            world.Set(e, DuelBrain.For(BrainKind.Duel, player, mine, reach, playerReach));
            FoeWakes.Wait(world, e, p.WakeMetres);   // it stands until the player comes near (or a blow meets it)
            options.Say(string.Create(CultureInfo.InvariantCulture,
                $"foe: {names[e.Packed]} (e{e.Index}), {p.Name}: {p.Template}{(p.Weapon is null ? "" : $" with {p.Weapon}")}, sheet {p.Sheet}, {(mine == numbers ? "the duelling brain's approach" : "1.1's approach")}, at {request.Feet.X:0.##} {request.Feet.Y:0.##} {request.Feet.Z:0.##} facing {request.YawDegrees:0.#}°; the duelling brain against the player, reach {reach:0.00} m (the player's {playerReach:0.00} m); {(p.WakeMetres > 0f ? $"it waits until the player is within {p.WakeMetres:0.#} m or a blow meets it" : "it comes at once")}"));
        }
    }

    /// <summary>A recording's head with the foes' note (<see cref="FoeRequest.TapeNote"/>) when there are foes; a head without foes is as before.</summary>
    TapeHeader WithFoes(TapeHeader h)
    {
        if (options.Foes.Count > 0) h.Notes[FoeRequest.TapeNote] = string.Join(";", options.Foes);
        return h;
    }
}
