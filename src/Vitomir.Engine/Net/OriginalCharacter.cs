// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// 1.1's multiplayer character (`Crypt\npf%06d.npf`, Formats.Npf) made into Vitomir's own (`Crypt\<name>.vplayer`, NetCharacter):
// the setup's import of a player's characters.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Net;

/// <summary>
/// **1.1's character file, read into Vitomir's**. The file is the whole player as 1.1 last wrote it (after every
/// advancement screen and when the player left a game), and 1.1's select screen loads it whole into a new player.
/// What of it a game uses is what 1.1's join sends to the host (the name, the template, the type, the clan, the numbers and
/// the actor flags; every discipline and its level; every item carried), which is what a <c>.vplayer</c> holds
/// (<see cref="NetCharacter"/>, its <see cref="NetSheet"/>) — and the purse and the effects, which Vitomir keeps with the character
/// (ours). It is read as a save's party member is (<c>SaveImport.Member</c>): the same fields, the same meanings.
/// </summary>
public static class OriginalCharacter
{
    /// <summary>
    /// The character <paramref name="npf"/> holds, named <paramref name="name"/> when one is given (else its own), with what does not
    /// come across said in <paramref name="notes"/>. With <paramref name="files"/> the template is spelt as the install's templates
    /// spell it, and a template, clan, discipline, effect or item the install does not have is said (and kept: the host leaves out
    /// what it cannot make, as it does for any <c>.vplayer</c>).
    /// </summary>
    public static NetCharacter From(NpfCharacter npf, List<string> notes, GameFiles? files = null, string? name = null)
    {
        var t = npf.Thing;
        var a = npf.Actor;
        var p = npf.Player;
        string own = NetCharacter.CleanName(name ?? (p.Text3C4.Length > 0 ? p.Text3C4 : npf.Header.Name));
        if (own.Length == 0) own = npf.Header.Login;

        var templates = files is null ? null : new TemplateSet(files);
        var spellings = templates?.Names.ToList() ?? [];             // the install's spelling of each, for the model and the items
        string template = t.Name;
        if (templates is not null)
        {
            if (spellings.FirstOrDefault(n => n.Equals(t.Name, StringComparison.OrdinalIgnoreCase)) is { } spelt) template = spelt;
            else notes.Add($"{own}'s model, {t.Name}, is not one of this install's templates: a host cannot make it");
        }

        var type = a.ActorType <= (byte)ActorType.Human ? (ActorType)a.ActorType : ActorType.Vampire;
        if (a.ActorType > (byte)ActorType.Human) notes.Add($"{own} is of type {a.ActorType}, which the create screen does not offer: taken as Kindred");
        // A human's clan is `human`, which NetCharacter keeps as none (its Apply gives a human `human`).
        string clan = type == ActorType.Human ? "" : (a.Text208 ?? "").ToLowerInvariant();
        if (clan.Length > 0 && files is not null && Clans.For(files)[clan] is null) notes.Add($"{own}'s clan, {clan}, is not in this install's clans");

        var powers = files is null ? null : new DisciplineDefinitions(files);
        var effects = files is null ? null : new EffectDefinitions(files);
        var disciplines = new List<SheetDiscipline>();
        foreach (var d in a.Disciplines)
        {
            if ((d.Field8 & Discipline.Temporary) != 0) continue;     // lent, not known (NetSheet.From leaves them out too)
            // 1.1's record has the name lower-cased (`bloodhealing`); game.ndd's own spelling (`BloodHealing`) is what the game keeps.
            string power = d.Name;
            if (powers is not null)
            {
                if (powers[d.Name] is { } def) power = def.Name;
                else notes.Add($"{own} knows {d.Name}, which game.ndd does not have");
            }
            disciplines.Add(new SheetDiscipline(power, Math.Max(-1, d.Level), d.Field18 == uint.MaxValue ? -1 : (int)d.Field18));
        }

        var pack = new List<SheetItem>();
        int changed = 0;
        foreach (var carried in a.Inventory.Items)
        {
            if (carried.Item is not { } item) continue;
            string made = carried.Name;
            if (templates is not null)
            {
                if (spellings.FirstOrDefault(n => n.Equals(carried.Name, StringComparison.OrdinalIgnoreCase)) is { } spelt) made = spelt;
                else notes.Add($"{own} carries a {carried.Name}, which this install cannot make");
            }
            int x = item.Fields2BC.Length > 0 ? (int)item.Fields2BC[0] : 0, y = item.Fields2BC.Length > 1 ? (int)item.Fields2BC[1] : 0;
            pack.Add(new SheetItem(made, (int)item.Location, x, y, (int)item.Amount));
            if (item.Effects.Length > 0 || carried.Ammo.Length > 0) changed++;
        }
        if (changed > 0)
            notes.Add($"{changed} of {own}'s items carry their own effects or rounds, which a .vplayer does not hold: they come as their templates make them");
        foreach (var (held, where) in new[] { (a.Inventory.Held360, "first held slot"), (a.Inventory.Held36C, "third held slot") })
            if (held is not null) notes.Add($"{own} had a {held.Name} at the actor's {where}, outside the pack: not brought");

        var kept = new List<SheetEffect>();
        int timed = 0;
        foreach (var e in a.Effects)
        {
            if ((e.Flags & Effect.Worn) != 0) continue;               // a worn item's own: they come back with the item
            // How long it has left is the clock of the game it was written in, which the file does not hold: one for ever comes across,
            // its next tick at once; one that runs out is left behind (ours: 1.1 loads the old game's clock values whole).
            if (e.Level != 0) { timed++; continue; }
            string effect = e.Name;
            if (effects is not null)
            {
                if (effects[e.Name] is { } def) effect = def.Name;
                else notes.Add($"{own} has {e.Name}, which effects.ned does not have");
            }
            kept.Add(new SheetEffect(effect, e.ValueLevel, e.ValueAsFloat, 0, 0, BitConverter.UInt32BitsToSingle(e.Field10), e.Flags,
                                     (int)e.Field28, BitConverter.UInt32BitsToSingle(e.Field2C), (int)e.SourceGuid));
        }
        if (timed > 0) notes.Add($"{timed} of {own}'s effects had a time left by another game's clock: not brought");
        if (a.Mind is { Length: > 0 } shape) notes.Add($"{own} was written shape-changed ({shape}): it comes as itself");

        var sheet = new NetSheet
        {
            BaseStats = [.. a.BaseStats], Stats = [.. a.Stats],
            Experience = p.Field3EC, Spendable = p.Field3E8,
            Health = a.Health, MaxHealth = a.MaxHealth,
            Dead = (a.Field1F4 & Actor.Flag.Dead) != 0,
            Disciplines = disciplines, Pack = pack,
            // A character that carries nothing (such as one made by 1.1's Create and never taken into a game, carry only the unarmed
            // weapon) is dressed by the host from its template's kit, as one advanced on Vitomir's create screen is (ours: whether a
            // 1.1 host gives a bare joiner its kit is unread).
            Kit = pack.Count == 0,
            Cash = (int)Math.Min(a.Field230, int.MaxValue),
            Effects = kept,
            Flags = a.Field1F4, Flags2 = a.Flags,
        };
        var character = new NetCharacter
        {
            Name = own, Template = template, Type = type, Clan = clan,
            Experience = p.Field3E8, Cash = sheet.Cash, Saved = sheet,
        };
        int known = disciplines.Count(d => d.Level >= 0);
        notes.Add($"{own}: {template}, {(type == ActorType.Human ? "Human" : type == ActorType.Ghoul ? "Ghoul" : "Kindred")}" +
                  (clan.Length > 0 ? $" ({clan})" : "") + $", {p.Field3EC} experience ({p.Field3E8} to spend), {known} disciplines known, " +
                  $"{pack.Count} items{(pack.Count == 0 ? " (dressed from the template's kit)" : "")}, {sheet.Cash} in the purse, {kept.Count} effects" +
                  (sheet.Dead ? ", written back dead (brought back at a tenth)" : ""));
        return character;
    }
}
