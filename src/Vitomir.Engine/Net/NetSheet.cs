// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A multiplayer character's whole sheet — what 1.1 keeps of it between games and sends when it
// changes. 1.1's character file is the actor record itself (`Crypt\npf%06d.npf`, written
// after every advancement and when the player leaves); its join sends the numbers in the join,
// every discipline and its level and every item carried (which the
// host makes and answers with its GUID). The advancement's answer is the same
// numbers (message 0x36) and disciplines (0x38). This record is ours: the parts of
// that record Vitomir's actor has, written as JSON in the `.vplayer` and as our bytes on the wire.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>One discipline a character knows: its name, its level and the quick slot it sits in (−1 for none).</summary>
public sealed record SheetDiscipline(string Name, int Level, int QuickSlot);

/// <summary>One item a character carries: its template, where it is (<see cref="Slot"/>), its cell and its count.</summary>
public sealed record SheetItem(string Template, int Slot, int X, int Y, int Amount);

/// <summary>
/// One effect on a character: its `effects.ned` name, level and value, how long it has left
/// (0 for ever) and until its next tick, its fade, flags and parameters, and its creator's GUID. The
/// clocks are kept as what is left, because the place it comes back in has a clock of its own.
/// </summary>
public sealed record SheetEffect(string Name, int Level, float Value, int Left, int NextIn, float Fade, uint Flags,
                                 int IntParam, float FloatParam, int Creator);

/// <summary>
/// A character's sheet: the nineteen stats and their bases, both experience figures, health, the
/// disciplines, the pack and the effects on it — 1.1's file is the whole actor, effects and all, which
/// it clears only when the player leaves (<see cref="NetArms.TidyForLeaving"/>).
/// </summary>
public sealed record NetSheet
{
    public float[] BaseStats { get; init; } = [];
    public float[] Stats { get; init; } = [];
    /// <summary>All ever earned.</summary>
    public long Experience { get; init; }
    /// <summary>Still to spend.</summary>
    public long Spendable { get; init; }
    public float Health { get; init; }
    public float MaxHealth { get; init; }
    /// <summary>Written back dead (the actor's `0x10000000`): brought back at a tenth when it is next played.</summary>
    public bool Dead { get; init; }
    public List<SheetDiscipline> Disciplines { get; init; } = [];
    public List<SheetItem> Pack { get; init; } = [];
    /// <summary>
    /// The template's kit rather than a pack of its own: the sheet of a character advanced on
    /// the create screen, which has never been dressed — the host dresses it from its template and
    /// leaves that pack alone.
    /// </summary>
    public bool Kit { get; init; }
    /// <summary>The character's own money (which a network game spends from).</summary>
    public int Cash { get; init; }
    /// <summary>
    /// The effects on it but worn gear's, which come back with the gear. Null for a sheet written
    /// before they were kept: the character then keeps what it is made with.
    /// </summary>
    public List<SheetEffect>? Effects { get; init; }

    /// <summary>
    /// The actor flags (THING_AF_*), which 1.1's join carries (the word after the four
    /// names) and the host sets on the joiner whole  — so Create's `FASTDRINKER` (0x800, a
    /// Giovanni) and `HUMANITYTRACK` (0x8000, everybody) reach the game, and whatever the character had
    /// when it was written back (the whole actor is written). Null for a sheet written before it was kept (a
    /// `.vplayer` without it, protocol 17): the character keeps its template's.
    /// </summary>
    public uint? Flags { get; init; }

    /// <summary>
    /// The second actor flags (THING_AF2_*), which 1.1's join carries after the first and the host
    /// sets on the joiner whole too (before it adds the joined bit 0x400) — **stale state bits
    /// included**, as 1.1 does: whatever the character had when it was written back (the whole
    /// actor is written) — limping (0x2), in a conversation (0x8), the talking hands (0x10), speaking (0x40), a
    /// base stat changed on a host (0x10000000) — comes into the game with it. Null for a sheet written before
    /// it was kept (a `.vplayer` without it, protocol 18): the character keeps its template's.
    /// </summary>
    public uint? Flags2 { get; init; }

    /// <summary>The sheet of a character as it stands.</summary>
    public static NetSheet From(WorldThing thing)
    {
        var a = thing.Actor!;
        return new NetSheet
        {
            BaseStats = (float[])a.BaseStat.Clone(),
            Stats = (float[])a.Stat.Clone(),
            Experience = a.Experience,
            Spendable = a.SpendableExperience,
            Health = a.Health,
            MaxHealth = a.MaxHealth,
            Dead = a.Has(Actor.Flag.Dead),
            // In list order, so putting them back one at a time on the front would reverse
            // them: Apply walks them backwards.
            Disciplines = a.Disciplines.All.Where(d => (d.Flags & Discipline.Temporary) == 0)
                           .Select(d => new SheetDiscipline(d.Definition.Name, d.Level, d.QuickSlot)).ToList(),
            Pack = a.Inventory.Items.Where(i => i.Thing is not null)
                    .Select(i => new SheetItem(i.Thing!.Template, i.Location, i.Cell.X, i.Cell.Y, i.Amount)).ToList(),
            Effects = EffectsOf(thing),
            Cash = a.Cash,
            Flags = a.Flags,
            Flags2 = a.Flags2,
        };
    }

    static List<SheetEffect> EffectsOf(WorldThing thing)
    {
        int now = thing.Place?.Milliseconds ?? 0;
        return thing.Effects.All.Where(e => !e.Spent && (e.Flags & Effect.Worn) == 0)
            .Select(e => new SheetEffect(e.Definition.Name, e.Level, e.Value, e.Until == 0 ? 0 : Math.Max(1, e.Until - now),
                                         Math.Max(0, e.NextTick - now), e.Fade, e.Flags, e.IntParam, e.FloatParam, e.CreatorGuid))
            .ToList();
    }

    /// <summary>
    /// The kept effects put back (ours in the way of it — 1.1 loads the actor record whole): every
    /// effect the character was made with but worn gear's comes off, and each kept one goes on as it
    /// was, its clocks counted from this place's now, as a load puts a saved effect back (no script:
    /// a Codex effect's script state is not kept). An effect `effects.ned` does not have is dropped.
    /// </summary>
    void Reeffect(WorldThing thing, Location place)
    {
        if (Effects is null || place.Effects is not { } defs) return;
        int now = place.Milliseconds;
        var worn = thing.Effects.All.Where(e => !e.Spent && (e.Flags & Effect.Worn) != 0).ToList();
        foreach (var old in thing.Effects.All) if (old.Script is { } os && (old.Flags & Effect.Worn) == 0) place.ScriptObjects?.Free(os);
        thing.Effects.Clear();
        foreach (var w in worn) thing.Effects.Append(w);
        foreach (var e in Effects)
        {
            if (defs[e.Name] is not { } def) continue;
            thing.Effects.Append(new Effect
            {
                Definition = def, Level = e.Level, Value = e.Value, Until = e.Left == 0 ? 0 : now + e.Left,
                NextTick = now + e.NextIn, Fade = e.Fade, Guid = EffectRuntime.NextGuid++, CreatorGuid = e.Creator,
                Flags = e.Flags, IntParam = e.IntParam, FloatParam = e.FloatParam,
            });
        }
    }

    /// <summary>
    /// Puts the sheet on a character: the stats and their bases, the experience, health, and the
    /// disciplines — each known one set to its level, each new one learnt (as the host does
    /// for the advancement's answer, without checks). <paramref name="withPack"/> also replaces what it
    /// carries with the sheet's items, made in <paramref name="place"/> (as the join's pack is made):
    /// a worn one is put on again, the rest go back to their slot and cell.
    /// </summary>
    public void Apply(WorldThing thing, Location place, bool withPack)
    {
        if (thing.Actor is not { } a) return;
        for (int i = 0; i < Math.Min(BaseStats.Length, a.BaseStat.Length); i++) a.BaseStat[i] = BaseStats[i];
        for (int i = 0; i < Math.Min(Stats.Length, a.Stat.Length); i++) a.Stat[i] = Stats[i];
        a.Experience = Math.Max(0, Experience);
        a.SpendableExperience = Math.Max(0, Spendable);
        if (MaxHealth > 0) a.MaxHealth = MaxHealth;
        if (Health > 0) a.Health = Math.Min(Health, a.MaxHealth);
        if (place.Powers is { } powers)
            for (int i = Disciplines.Count - 1; i >= 0; i--)
            {
                var d = Disciplines[i];
                if (powers[d.Name] is not { } def) continue;
                var known = a.Disciplines.Of(def) ?? DisciplineBook.Make(thing, def);
                if (known is null) continue;
                DisciplineBook.SetLevel(thing, powers, d.Name, d.Level);
                known.QuickSlot = d.QuickSlot;
            }
        if (withPack && Flags is { } flags) a.Flags = flags;                // the join's flags, whole
        if (withPack && Flags2 is { } flags2) a.Flags2 = flags2;            // and the second word, whole, stale bits and all
        if (withPack && !Kit) Repack(thing, a, place);
        if (withPack) Reeffect(thing, place);
        if (withPack) a.Cash = Math.Max(0, Cash);
        // A character written back dead is revived at a tenth of its health and blood as it is loaded
        // (the character select's load revives it at 0.1 and 0.1, with the sound). **Ours**: here, as the
        // host makes it, and without the revive's sound, which 1.1 plays over the select screen.
        if (Dead && withPack) Damage.Revive(thing, 0.1f, 0.1f, sound: false);
    }

    void Repack(WorldThing thing, Actor a, Location place)
    {
        foreach (var old in a.Inventory.Items.ToList())
        {
            if ((old.Location & Rpg.Slot.Worn) != 0) Equipment.Unequip(thing, old, place.Effects);
            a.Inventory.Remove(old);
            if (old.Thing is { } gone) place.Remove(gone, drop: false);
        }
        for (int i = Pack.Count - 1; i >= 0; i--)
        {
            var s = Pack[i];
            if (place.Make(s.Template) is not { Item: { } item } made) continue;
            made.Owner = thing;
            item.Amount = s.Amount;
            if ((s.Slot & Rpg.Slot.Worn) != 0 && place.Effects is { } effects)
            {
                a.Inventory.Add(item);
                if (Equipment.Equip(thing, item, s.Slot, effects, place.Milliseconds) != EquipResult.Equipped)
                    Stow.Place(a, item, Rpg.Slot.Inventory);
                continue;
            }
            item.Location = s.Slot;
            item.Cell = (s.X, s.Y);
            a.Inventory.Add(item);
        }
    }

    public void Write(NetWriter w)
    {
        w.U8((byte)Stats.Length);
        for (int i = 0; i < Stats.Length; i++) w.F32(BaseStats.ElementAtOrDefault(i)).F32(Stats[i]);
        w.I32((int)Math.Min(Experience, int.MaxValue)).I32((int)Math.Min(Spendable, int.MaxValue)).F32(Health).F32(MaxHealth).Bool(Dead);
        w.U8((byte)Math.Min(Disciplines.Count, 255));
        foreach (var d in Disciplines.Take(255)) w.Str(d.Name).U8((byte)d.Level).I16((short)d.QuickSlot);
        w.U16((ushort)Math.Min(Pack.Count, ushort.MaxValue));                                 // protocol 15: a word
        foreach (var s in Pack.Take(ushort.MaxValue)) w.Str(s.Template).I32(s.Slot).I16((short)s.X).I16((short)s.Y).I32(s.Amount);
        // Protocol 5: the purse, and the effects when known.
        w.I32(Cash).Bool(Kit);
        w.Bool(Flags is not null).U32(Flags ?? 0);                                             // protocol 18: the actor flags
        w.Bool(Flags2 is not null).U32(Flags2 ?? 0);                                           // protocol 19: and the second word
        w.Bool(Effects is not null);
        if (Effects is null) return;
        w.U8((byte)Math.Min(Effects.Count, 255));
        foreach (var e in Effects.Take(255))
            w.Str(e.Name).I16((short)e.Level).F32(e.Value).I32(e.Left).I32(e.NextIn).F32(e.Fade).U32(e.Flags)
             .I32(e.IntParam).F32(e.FloatParam).I32(e.Creator);
    }

    public static NetSheet Read(NetReader r)
    {
        int n = r.U8();
        var bases = new float[n];
        var stats = new float[n];
        for (int i = 0; i < n; i++) { bases[i] = r.F32(); stats[i] = r.F32(); }
        long xp = r.I32(), spend = r.I32();
        float health = r.F32(), max = r.F32();
        bool dead = r.Bool();
        var disciplines = new List<SheetDiscipline>();
        // The level is a signed byte: −1, a discipline the character may learn and has not, goes as 0xff (read as 255, the
        // host's SetLevel held it to 4, so every learnable discipline of a joiner's came into the game at the top level).
        for (int i = r.U8(); i > 0; i--) disciplines.Add(new SheetDiscipline(r.Str(), (sbyte)r.U8(), r.I16()));
        var pack = new List<SheetItem>();
        for (int i = r.U16(); i > 0; i--) pack.Add(new SheetItem(r.Str(), r.I32(), r.I16(), r.I16(), r.I32()));
        int cash = r.I32();
        bool kit = r.Bool();
        bool hasFlags = r.Bool();
        uint flags = r.U32();
        bool hasFlags2 = r.Bool();
        uint flags2 = r.U32();
        List<SheetEffect>? effects = null;
        if (r.Bool())
        {
            effects = [];
            for (int i = r.U8(); i > 0; i--)
                effects.Add(new SheetEffect(r.Str(), r.I16(), r.F32(), r.I32(), r.I32(), r.F32(), r.U32(), r.I32(), r.F32(), r.I32()));
        }
        return new NetSheet
        {
            BaseStats = bases, Stats = stats, Experience = xp, Spendable = spend, Health = health, MaxHealth = max, Dead = dead,
            Disciplines = disciplines, Pack = pack, Effects = effects, Cash = cash, Kit = kit,
            Flags = hasFlags ? flags : null,
            Flags2 = hasFlags2 ? flags2 : null,
        };
    }
}
