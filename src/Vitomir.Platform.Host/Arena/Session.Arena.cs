// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Host;

public sealed partial class Session
{
    readonly List<EntityId> fighters = [];

    /// <summary>The arena's fighters, in the cast's order; empty outside the arena.</summary>
    public IReadOnlyList<EntityId> Fighters => fighters;

    /// <summary>
    /// The view of an arena's bout (there is no player to look from): a spectator 4.5 m out to the side of the first
    /// two fighters' line, 1.7 m up, looking at their middle, with a field of 60°; between ticks as the bodies are drawn.
    /// </summary>
    public ViewPoint Spectator(float alpha = 1f)
    {
        var bodies = world.Store<Body>();
        Vector3 At(EntityId e) => before.TryGetValue(e.Packed, out var was) ? Vector3.Lerp(was.Feet, bodies.Get(e).Feet, alpha) : bodies.Get(e).Feet;
        if (fighters.Count < 2 || !bodies.Has(fighters[0]) || !bodies.Has(fighters[1])) return new ViewPoint(Vector3.UnitZ * 1.7f, 0f, 0f, 60f);
        var a = At(fighters[0]);
        var b = At(fighters[1]);
        var mid = (a + b) * 0.5f;
        var along = new Vector2(b.X - a.X, b.Y - a.Y);
        along = along.LengthSquared() > 1e-6f ? Vector2.Normalize(along) : Vector2.UnitX;
        var side = new Vector2(-along.Y, along.X);
        var eye = new Vector3(mid.X - side.X * 4.5f, mid.Y - side.Y * 4.5f, mid.Z + 1.7f);
        return new ViewPoint(eye, MathF.Atan2(side.Y, side.X), MathF.Atan2(1.0f - 1.7f, 4.5f), 60f);
    }

    /// <summary>
    /// The arena's fighters made instead of the player and the townsfolk (<see cref="SessionOptions.Arena"/>): each a
    /// body where the cast puts it, said to the modules as a townsperson (the enhanced module gives it the sandbox's sheet and
    /// the human's movement), drawn and animated as its 1.1 character with its weapon, armed, named; then its sheet as the
    /// cast asks, through the module (<see cref="IPersonSheets"/>: a template or a whole sheet, then each value, the swept
    /// one last; a refusal stops the bout by name); then its brain — the duelling brain or a dummy (<see cref="DuelBrain"/>,
    /// facing its foe with both reaches measured) — or its script (<see cref="ActionScript"/>, <c>--npc-actions</c>'s
    /// grammar). Nobody is the player.
    /// </summary>
    void PopulateArena(string castText, IReadOnlyDictionary<string, int> kinds)
    {
        ArenaCast cast;
        try { cast = ArenaCast.Read(castText); }
        catch (ArenaSpecException e) { throw new SessionException(e.Message); }
        var people = world.Service<PeopleHooks>();
        var assetsInWorld = world.Service<CharacterAssets>();
        world.TryService<IPersonSheets>(out var sheets);
        var kindOf = new int[cast.Fighters.Count];
        string? controlled = world.TryService<ScenarioDirector>(out var director) ? director.Program.Input?.Actor : null;   // PLAYER_INPUT_REPLAY
        for (int i = 0; i < cast.Fighters.Count; i++)
        {
            var f = cast.Fighters[i];
            // The scenario's input actor is the controlled body, driven by the player's controls (no brain below).
            var e = f.Name == controlled
                ? MovementServices.SpawnControlled(world, new Vector3(f.X, f.Y, f.Z), DetMath.WrapAngle(f.Facing * DetMath.DegToRad), Locomotion.Sandbox)
                : MovementServices.SpawnBody(world, new Vector3(f.X, f.Y, f.Z), DetMath.WrapAngle(f.Facing * DetMath.DegToRad), Locomotion.Sandbox);
            if (f.Name == controlled) player = e;
            people.Made(e, PersonRole.Townsperson, f.Template);       // its character named, so a module dresses it as that character
            kindOf[i] = kinds.TryGetValue(ArenaCast.KindKey(f), out int kind) ? kind : -1;   // its own kind when it has clip rates
            if (kindOf[i] >= 0)
            {
                var walk = assetsInWorld.Kind(kindOf[i]).Motion(MotionRole.Walk).NaturalSpeed;
                if (walk > 0.05f) world.Get<Locomotion>(e).WalkSpeed = walk;
                Animate(e, assetsInWorld, kindOf[i]);
            }
            else CombatServices.Arm(world, e);
            names[e.Packed] = f.Name;
            fighters.Add(e);
            GiveMovement(e, $"arena: fighter {f.Name}", f.Template, f.Movement);   // its movement (none said: the human's, as before)

            // What it holds — a shield or none, as the spec says (1.1's shield is part of the weapon's held model).
            if (f.Shield is { } wantsShield && kindOf[i] >= 0 && HoldsShield(assetsInWorld, kindOf[i]) != wantsShield)
                throw new SessionException($"arena: fighter {f.Name}: \"shield\": {(wantsShield ? "true" : "false")}, but {f.Template} with {f.Weapon ?? "its own weapon"} holds {(wantsShield ? "none" : "one")} " +
                    $"(in 1.1 a shield is part of the weapon: {(wantsShield ? "give a weapon that carries one, as \"weapRapier_shield\"" : "give a weapon without one, as \"Rapier\" for rudolfGuard's weapRapier_shield")})");

            // The sheet, through the module.
            bool asks = f.Sheet.Template is not null || f.Sheet.Text is not null || f.Sheet.Values.Count > 0 || f.Blood is not null || f.Health is not null;   // Health
            if (asks && sheets is null) throw new SessionException($"arena: fighter {f.Name}: the game's modules keep no sheets, so its sheet cannot be set");
            if (sheets is not null) GiveSheet(sheets, e, f);
            // What it wears, exactly the spec's (none without "armor"), after its sheet.
            if (sheets is not null && (f.Armor is { Count: > 0 } || sheets.ArmorPieces.Count > 0))
            {
                if (!sheets.TryWear(e, f.Armor ?? [], out string why)) throw new SessionException($"arena: fighter {f.Name}: \"armor\": {why}");
                if (f.Armor is { Count: > 0 } worn) options.Say($"arena: {f.Name} wears {string.Join(", ", worn)}");
            }
            // Its bow, put away, and its arrows.
            if (f.Ranged is { } bowId)
            {
                if (!BowServices.Give(world, e, bowId, f.Arrows ?? 12))
                    throw new SessionException($"arena: fighter {f.Name}: \"ranged\": no bow \"{bowId}\" (the module's bows: {(world.TryService<BowConfig>(out var bc) ? string.Join(", ", bc.Bows.Select(x => x.Id)) : "none")})");
                options.Say($"arena: {f.Name} carries {bowId} with {f.Arrows ?? 12} arrows");
                LookTheBow(e, kinds, f.Template);   // drawn and posed with 1.1's bow while it is in hand (unless bow-look-off)
            }
        }

        GiveOverrides(cast);   // each fighter's own combat tempo, when given

        // The brains, once everyone is made (a foe is another fighter); the scripts as the townsfolk's.
        var book = world.Service<DuelBrainBook>();
        var scripts = new List<string>();
        for (int i = 0; i < cast.Fighters.Count; i++)
        {
            var f = cast.Fighters[i];
            if (fighters[i] == player && !player.IsNone) { options.Say($"arena: {f.Name} (e{player.Index}) is driven by the player's controls (the scenario's input)"); continue; }
            if (f.Brain == CastBrain.Script)
            {
                foreach (var a in (f.Script ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) scripts.Add($"{f.Name}:{a}");
                continue;
            }
            int foe = Math.Clamp(f.Foe, 0, cast.Fighters.Count - 1);
            float reach = kindOf[i] >= 0 ? DuelBrains.ReachOf(assetsInWorld, kindOf[i]) : DuelBrains.BareReach;
            float foeReach = kindOf[foe] >= 0 ? DuelBrains.ReachOf(assetsInWorld, kindOf[foe]) : DuelBrains.BareReach;
            var kind = f.Brain switch { CastBrain.Stand => BrainKind.Stand, CastBrain.Block => BrainKind.Block, _ => BrainKind.Duel };
            var numbers = f.Numbers ?? new DuelBrainSettings();
            if (f.Slots is { } slots) numbers = numbers with { PowerSlots = SlotMask(slots) };   // the quick slots its brain may use
            if (kind == BrainKind.Duel) numbers = Approached(numbers, $"arena: fighter {f.Name}", f.Template);   // 1.1's approach with its AI file's numbers
            // The archer brain (its bow given above); the duelling brain only once it draws its sword up close.
            if (f.Brain == CastBrain.Archer)
            {
                if (f.Ranged is null || !world.Has<Bow>(fighters[i])) throw new SessionException($"arena: fighter {f.Name}: the archer brain needs a bow (\"gear\": {{ \"ranged\": {{ \"weapon\": \"war_bow\" }} }})");
                var archer = (f.Archer ?? new CastArcher()).Over(world.Service<BowConfig>().Archer);
                world.Set(fighters[i], ArcherBrain.For(fighters[foe], world.Service<ArcherBrainBook>().Add(archer), book.Add(numbers), reach, foeReach));
                options.Say(string.Create(CultureInfo.InvariantCulture,
                    $"arena: {f.Name} (e{fighters[i].Index}) {f.Template} with {f.Ranged} at {f.X:0.##} {f.Y:0.##} facing {f.Facing:0.#}°, archer against {cast.Fighters[foe].Name}: keeps {archer.KeepMetres:0.#} m, holds {archer.HoldSeconds:0.##} s, its aim's error by its Range times {archer.ErrorScale:0.##}, its sword within {archer.SwordMetres:0.#} m"));
                continue;
            }
            world.Set(fighters[i], DuelBrain.For(kind, fighters[foe], book.Add(numbers), reach, foeReach));
            options.Say(string.Create(CultureInfo.InvariantCulture,
                $"arena: {f.Name} (e{fighters[i].Index}) {f.Template}{(f.Weapon is null ? "" : $" with {f.Weapon}")} at {f.X:0.##} {f.Y:0.##} facing {f.Facing:0.#}°, {kind.ToString().ToLowerInvariant()}{(kind == BrainKind.Duel ? $" against {cast.Fighters[foe].Name}, reach {reach:0.00} m (the foe's {foeReach:0.00} m)" : "")}"));
        }
        if (scripts.Count > 0) Script(string.Join(",", scripts));

        // The sides (one against several), every fighter's, in the order the cast first names them.
        if (cast.HasSides)
        {
            var sideNames = new List<string>();
            for (int i = 0; i < cast.Fighters.Count; i++)
            {
                string side = cast.Fighters[i].Side!;
                int index = sideNames.IndexOf(side);
                if (index < 0) { index = sideNames.Count; sideNames.Add(side); }
                world.Set(fighters[i], new DuelSide { Side = index });
            }
            options.Say($"arena: sides {string.Join(" against ", sideNames.Select(sn => $"{sn} ({string.Join(", ", cast.Fighters.Where(f => f.Side == sn).Select(f => f.Name))})"))}; each duelling brain fights the nearest of another side still up; the bout ends when one side is left");
        }
    }

    /// <summary>
    /// A fighter's sheet as the cast asks, through the module (powers with their options, Blood): the template or
    /// the sheet file, each value in order (the swept ones last) — a trait's purchased value, a power's rank with the options
    /// named (<see cref="IPersonSheets.TryLearn(EntityId, string, int, IReadOnlyList{string}, out string)"/>: the module refuses
    /// an option it does not know or one its rules do not read), or a rank alone, which keeps the sheet's options and is refused
    /// by name when the sheet has none (<see cref="LearnsAnOption"/>) — then its Blood. A refusal stops the bout by name
    /// (<see cref="SessionException"/>).
    /// </summary>
    public static void GiveSheet(IPersonSheets sheets, EntityId e, CastFighter f)
    {
        var sheet = f.Sheet;
        string error = "";
        if (sheet.Template is { } template && !sheets.TryTemplate(e, template, out error)) throw new SessionException($"arena: fighter {f.Name}: {error}");
        if (sheet.Text is { } text && !sheets.TryImport(e, text, out error)) throw new SessionException($"arena: fighter {f.Name}: its sheet file was refused: {error}");
        foreach (var v in sheet.Values)
        {
            bool ok = !v.Power ? sheets.TrySet(e, v.Id, v.Value, out error)
                : v.Options is { Count: > 0 } options && v.Value != 0 ? sheets.TryLearn(e, v.Id, v.Value, options, out error)
                : sheets.TryLearn(e, v.Id, v.Value, out error) && LearnsAnOption(sheets, e, v, out error);
            if (!ok) throw new SessionException($"arena: fighter {f.Name}: {(v.Power ? "power" : "trait")} {v.Id} = {v.Value}{(v.Options is { Count: > 0 } o && v.Value != 0 ? $" ({string.Join(", ", o)})" : "")}: {error}");
        }
        // Blood at the start, after the sheet (its capacity may be among the values).
        if (f.Blood is { } blood && !sheets.TrySet(e, BloodResource, blood, out error))
            throw new SessionException($"arena: fighter {f.Name}: blood = {blood}: {error}");
        // ---- Health at the start, after the sheet (its maximum may be among the values), as Blood is.
        if (f.Health is { } health && !sheets.TrySet(e, HealthResource, health, out error))
            throw new SessionException($"arena: fighter {f.Name}: health = {health}: {error}");
        // ---- end
    }

    /// <summary>The module's Blood (the powers work's <c>blood</c> resource, set through <see cref="IPersonSheets.TrySet"/>).</summary>
    public const string BloodResource = "blood";

    /// <summary>The module's Health (the enhanced module's <c>health</c> resource, rules/harm.json, set through <see cref="IPersonSheets.TrySet"/>).</summary>
    public const string HealthResource = "health";

    /// <summary>The brain's mask of quick slots (bit <c>s − 1</c> for slot <c>s</c>).</summary>
    public static int SlotMask(IEnumerable<int> slots)
    {
        int mask = 0;
        foreach (int s in slots) if (s >= 1 && s <= Powers.QuickSlots.Count) mask |= 1 << (s - 1);
        return mask;
    }

    /// <summary>Whether a kind's held models hold a shield.</summary>
    public static bool HoldsShield(CharacterAssets assets, int kind)
    {
        foreach (var h in assets.Kind(kind).Held)
            foreach (var t in h.Things)
                if (t.Kind == HeldKind.Shield) return true;
        return false;
    }

    /// <summary>
    /// After a rank learned alone (the survey's finding: Fortitude or Potence swept where the sheet has not their option
    /// did nothing and said nothing): a nonzero rank must come with at least one option on the sheet, read from the module's
    /// description of it (<see cref="IPersonSheets.TryDescribe"/>, its <c>sheet.powers</c>), else it is refused by name. Whether
    /// the module's rules read the options is the module's to refuse (<see cref="IPersonSheets.TryLearn(EntityId, string, int, IReadOnlyList{string}, out string)"/>);
    /// a module that does not describe a sheet's powers is not checked.
    /// </summary>
    public static bool LearnsAnOption(IPersonSheets sheets, EntityId e, CastValue v, out string error)
    {
        error = "";
        if (v.Value == 0 || !sheets.TryDescribe(e, out string json)) return true;
        var options = OptionsOf(json, v.Id);
        if (options is null || options.Count > 0) return true;
        error = $"the sheet has no option of {v.Id}, and a rank learns none by itself, so it would do nothing; name its options: \"powers\": {{ \"{v.Id}\": {{ \"rank\": {v.Value}, \"options\": [\"{v.Id}.…\"] }} }}";
        return false;
    }

    /// <summary>The options a sheet's description gives a power (<c>sheet.powers[].power/options</c>), empty when it has none, null when the description has no such list.</summary>
    public static List<string>? OptionsOf(string describedSheet, string power)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(describedSheet);
            if (!doc.RootElement.TryGetProperty("sheet", out var sheet) || !sheet.TryGetProperty("powers", out var powers) || powers.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
            foreach (var p in powers.EnumerateArray())
            {
                if (!p.TryGetProperty("power", out var id) || id.GetString() != power) continue;
                if (!p.TryGetProperty("options", out var os) || os.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
                return [.. os.EnumerateArray().Select(o => o.GetString() ?? "")];
            }
            return [];
        }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
