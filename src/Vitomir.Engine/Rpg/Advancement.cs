// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Spending experience: what a point of an attribute costs, what a level of a
// discipline costs, buying either, and taking it all back. The screen that shows it is the
// character sheet and the discipline pane with their learn buttons up (`Ui.DisciplinePane`,
// `Ui.CharacterPane`); this is the arithmetic and the bookkeeping underneath, which is testable
// without either.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// One advancement session (`rpgAdvance_pPlayer` and its snapshot). It starts on
/// the party's first member, can be reverted to where it started any number of times, and walks on
/// to the next member when the player is done with this one.
/// </summary>
public sealed class Advancement
{
    /// <summary>The party, in slot order — the session walks it from the start (party[0], whoever asked).</summary>
    readonly IReadOnlyList<WorldThing> party;
    int index;

    /// <summary>The character on the screen.</summary>
    public WorldThing Subject { get; private set; }

    /// <summary>The discipline table, which the costs read.</summary>
    public DisciplineDefinitions Powers { get; }

    /// <summary>A network game's rules for this screen; null in single player.</summary>
    public NetAdvanceRules? Network { get; set; }

    // The snapshot: both experience figures, both stat rows, and a copy of every
    // discipline instance.
    long spendable, lifetime;
    float[] baseStats = [], stats = [];
    List<Discipline> disciplines = [];
    readonly HashSet<Discipline> snapshotted = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Opens a session for <paramref name="asked"/>: a party member starts the walk at the
    /// party's **first** slot, whoever asked; anybody else is advanced alone, at slot 0 — and the
    /// walk then goes on to party[1], because the engine's loop only counts.
    /// </summary>
    public Advancement(WorldThing asked, IReadOnlyList<WorldThing> party, DisciplineDefinitions powers)
    {
        this.party = party;
        Powers = powers;
        Subject = party.Contains(asked) && party.Count > 0 ? party[0] : asked;
        Snapshot();
    }

    // ---- attributes ----------------------------------------------------------------------------

    /// <summary>
    /// The attributes the sheet puts a raise button on: the six physical and mental,
    /// the three social, and Faith — for a **human** only.
    /// </summary>
    public static bool Raisable(Actor who, int stat) =>
        Array.IndexOf(Rules.GameRules.Current.Stats.RaisableStats, stat) >= 0
        || who.Type == ActorType.Human && Array.IndexOf(Rules.GameRules.Current.Stats.RaisableStatsHuman, stat) >= 0;

    /// <summary>The raisable attributes by stat number (1.1's), the rules file's default: 0–5 and 11–13.</summary>
    public static readonly int[] RaisableDefault = [0, 1, 2, 3, 4, 5, 11, 12, 13];

    /// <summary>The attributes only a human may raise, the rules file's default: Faith.</summary>
    public static readonly int[] RaisableHumanDefault = [(int)ActorStat.Faith];

    /// <summary>The cost rate's bands (an attribute's base under each) and the rates (the last for at or over the last band).</summary>
    public static readonly float[] CostBands = [50f, 60f, 70f, 80f, 90f], CostRates = [4f, 5f, 6f, 8f, 10f, 15f];

    /// <summary>
    /// What the next point of an attribute costs, or −1 when it cannot be raised: at the
    /// character's own maximum (`.NPC`'s max column, 100 for Christof), at the clan's cap, or
    /// Appearance for a clan with the "no socials" weakness — **Appearance only**; Nosferatu can
    /// raise Charisma and Manipulation. Otherwise the base value times a rate that climbs with it:
    /// 4 under 50, 5 under 60, 6 under 70, 8 under 80, 10 under 90 and 15 above, truncated. Stats
    /// are on the 0–100 scale, so Christof's Strength 30 costs 120 to raise.
    /// </summary>
    public static int StatCost(Actor who, int stat)
    {
        if (stat < 0 || stat >= Actor.Stats) return -1;
        float have = who.BaseStat[stat];
        if (have >= who.StatMax[stat]) return -1;
        if (who.ClanRecord is { } clan)
        {
            if (clan.Caps[stat] != Clan.NoCap && have >= clan.Caps[stat]) return -1;
            if ((clan.Weaknesses & Clan.WeakSocials) != 0 && stat == (int)ActorStat.Appearance) return -1;
        }
        var a = Rules.GameRules.Current.Advancement;
        float rate = a.StatRaiseCostRates[^1];
        for (int i = 0; i < a.StatRaiseCostBands.Length; i++)
            if (have < a.StatRaiseCostBands[i]) { rate = a.StatRaiseCostRates[i]; break; }
        return (int)(have * rate);
    }

    /// <summary>
    /// Buys a point: refused for no cost or not enough; else the cost comes off the
    /// spendable pool, the base goes up one through the clamp, and the effects are folded back in.
    /// No sound and no message.
    ///
    /// **Lowering**: in a Storyteller-moderated network game the host with **Shift** held
    /// (either: `DIK_LSHIFT` or
    /// `DIK_RSHIFT`) takes a point **off** instead — any character on
    /// the screen, seated or not, nothing refunded — unless the base is 1 or less (<see
    /// cref="LowerStep"/>), when the press is taken and nothing happens.
    /// </summary>
    public bool BuyStat(int stat)
    {
        if (Subject.Actor is not { } who) return false;
        if (Network is { Moderated: true, Host: true, Lowering: { } held } && held())
        {
            if (stat < 0 || stat >= Actor.Stats) return false;
            float step = Rules.GameRules.Current.Advancement.StatRaiseStep;
            if (who.BaseStat[stat] <= step) return true;
            who.SetBaseStat(stat, who.BaseStat[stat] - step);
            EffectRuntime.Recompute(Subject, EffectKind.Stat, stat);
            return true;
        }
        if (!MayRaise()) return false;
        int cost = StatCost(who, stat);
        if (cost < 0) return false;
        Grant(cost);
        if (who.SpendableExperience < cost) return false;
        who.SpendableExperience -= cost;
        who.SetBaseStat(stat, who.BaseStat[stat] + Rules.GameRules.Current.Advancement.StatRaiseStep);
        EffectRuntime.Recompute(Subject, EffectKind.Stat, stat);
        return true;
    }

    // ---- disciplines ---------------------------------------------------------------------------

    /// <summary>What a raise adds and a lowering takes, and the floor a lowering stops at (1.0).</summary>
    public const float LowerStep = 1f;

    /// <summary>The level multipliers: learning, then 0→1, 1→2, 2→3, 3→4.</summary>
    public static readonly float[] LevelMultipliers = [1f, 1.4f, 2f, 4f, 8f];

    /// <summary>What an out-of-clan discipline costs over an in-clan one (0x3fa00000) — and what everything costs a Caitiff.</summary>
    public const float OutOfClan = 1.25f;

    /// <summary>
    /// What the next level of a discipline costs, or −1: an unknown discipline, the
    /// stat requirements not met (<see cref="Cast.MeetsRequirements"/>), one the character does not
    /// already have (learning is only of disciplines the pane shows at "cannot use yet"), or one at
    /// the top level. The clan decides a multiplier: 1.0 for one of its own three groups, 1.25 for
    /// anything else — and **1.25 for everything** for a clan with the discipline-cost weakness
    /// (Caitiff), or for a character with no clan at all.
    ///
    /// Learning (level −1) is the row's experience column; learning a group's **first** discipline
    /// also pays the group's own price (1,000 in `game.ndg`, none for `DGRP_Common`). Each level
    /// after costs the column times 1.4, 2, 4 and 8.
    ///
    /// **The arithmetic is single precision**, which settles the 1.4 step: 500 × 1.4f is 700 in
    /// 24-bit arithmetic and 699 in 64-bit. 1.1 does this arithmetic in single
    /// precision (Direct3D's default for a program that does not ask otherwise), and never
    /// changes that — so 700, which is what 1.1 charges (the learn button shows it).
    /// </summary>
    public static int DisciplineCost(WorldThing who, DisciplineDefinitions powers, NddDiscipline def)
    {
        if (who.Actor is not { } me) return -1;
        if (!Cast.MeetsRequirements(who, def)) return -1;
        if (me.Disciplines.Of(def) is not { } inst) return -1;
        int level = inst.Level;
        if (level >= NddDiscipline.Levels - 1) return -1;
        var rules = Rules.GameRules.Current.Advancement;
        float mult = rules.DisciplineOutOfClanMultiplier;
        if (!(me.ClanRecord is { } clan && (clan.Weaknesses & Clan.WeakDisciplineCost) != 0)
            && me.ClanRecord is { } own && own.Owns(def.Group))
            mult = 1f;
        if (level == -1 && rules.DisciplineGroupFirstPrice && powers.FirstInGroup(def.Group) is { } first && first.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase))
        {
            float group = powers.Group(def.Group)?.Experience ?? 0;
            return (int)(((float)def.Experience * rules.DisciplineLevelMultipliers[0] + group) * mult);
        }
        return (int)((float)def.Experience * rules.DisciplineLevelMultipliers[level + 1] * mult);
    }

    /// <summary>
    /// Buys the next level: cost as above, refused for none or not enough; else the cost
    /// comes off the spendable pool and the level goes up through <see cref="DisciplineBook.SetLevel"/>
    /// — which opens the next of the group at "cannot use yet". Nothing else: no sound, no message,
    /// and lifetime experience and rank are untouched.
    /// </summary>
    public bool BuyDiscipline(NddDiscipline def)
    {
        if (Subject.Actor is not { } me || me.Disciplines.Of(def) is not { } inst || !MayRaise()) return false;
        int cost = DisciplineCost(Subject, Powers, def);
        if (cost < 0) return false;
        Grant(cost);
        if (me.SpendableExperience < cost) return false;
        me.SpendableExperience -= cost;
        // In a network game the instance is marked advanced there (its flag 4).
        if (DisciplineBook.SetLevel(Subject, Powers, def.Name, inst.Level + 1) is { } raised && Network is not null)
            raised.Flags |= Discipline.NetAdvanced;
        return true;
    }

    /// <summary>
    /// The network gate both raises open with: in a game without Storyteller
    /// moderation only this machine's own character can be raised — the host's walk
    /// shows the players' characters but cannot spend for them.
    /// </summary>
    bool MayRaise() => Network is not { Moderated: false } rules || ReferenceEquals(Subject, rules.Mine);

    /// <summary>
    /// A moderated game's host in the Storyteller's seat raises for nothing: the cost is awarded first
    /// (lifetime experience and rank with it) and then paid.
    /// </summary>
    void Grant(int cost)
    {
        if (Network is { Moderated: true, Seated: true, Host: true }) Experience.Award(Subject, cost);
    }

    // ---- the snapshot, and taking it all back ------------------------------------------------

    /// <summary>Both experience figures, both stat rows, and a copy of every instance, marked.</summary>
    void Snapshot()
    {
        snapshotted.Clear();
        if (Subject.Actor is not { } me) return;
        spendable = me.SpendableExperience;
        lifetime = me.Experience;
        baseStats = [.. me.BaseStat];
        stats = [.. me.Stat];
        // The copies are **prepended** one by one, so the copy list is the live list reversed.
        disciplines = [];
        foreach (var d in me.Disciplines.All)
        {
            snapshotted.Add(d);
            disciplines.Insert(0, new Discipline
            {
                Definition = d.Definition, Owner = d.Owner, Level = d.Level, QuickSlot = d.QuickSlot,
                Flags = d.Flags, CastCount = d.CastCount, RecastUntil = d.RecastUntil, Active = d.Active,
                ScrollGuid = d.ScrollGuid, Script = d.Script,
            });
        }
    }

    /// <summary>
    /// "Revert character stats", and Esc: both experience figures and both stat rows back
    /// as they were; the selected discipline deselected; every instance let go — freeing the script of
    /// any made since the snapshot — and the snapshot's copies installed in their place. The copies
    /// were made in reverse, so **each revert reverses the discipline list's order**; levels and quick
    /// slots come back exactly. Then a fresh snapshot of what was restored.
    /// </summary>
    public void Reset()
    {
        if (Subject.Actor is not { } me) return;
        me.SpendableExperience = spendable;
        me.Experience = lifetime;
        Array.Copy(baseStats, me.BaseStat, Math.Min(baseStats.Length, me.BaseStat.Length));
        Array.Copy(stats, me.Stat, Math.Min(stats.Length, me.Stat.Length));
        me.Disciplines.Current = null;
        Cast.Discard(Subject);
        foreach (var d in me.Disciplines.All)
            if (!snapshotted.Contains(d) && d.Script is { } s) Subject.Place?.ScriptObjects?.Free(s);
        me.Disciplines.Replace(disciplines);
        Snapshot();
    }

    /// <summary>
    /// "Finished": this member's changes stand, and the session moves to the next party
    /// slot when there is one — answering true, and the screen stays up for them — else it is over.
    /// </summary>
    public bool Next()
    {
        if (++index >= party.Count || party[index] is not { Actor: not null } next) return false;
        Subject = next;
        Snapshot();
        return true;
    }
}

/// <summary>
/// A network game's advancement rules (the advancement's network tests): whether the game is
/// moderated (the session's flag `0x400`), whether this machine is the host and sits in the Storyteller's
/// seat, and this machine's own character; and whether Shift is held
/// now (`DIK_LSHIFT` / `DIK_RSHIFT`), which on a moderated game's host
/// turns a raise into a lowering (<see cref="Advancement.BuyStat"/>).
/// </summary>
public sealed record NetAdvanceRules(bool Moderated, bool Host, bool Seated, WorldThing? Mine, Func<bool>? Lowering = null);
