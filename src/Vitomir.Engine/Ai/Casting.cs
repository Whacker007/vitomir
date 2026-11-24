// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a character reaches for when it is not just swinging: a healing discipline, a blood vial, a
// buff, a bolt of something. In the original one routine decides all of it,
// for a party member and an enemy alike, by walking a fixed ladder of rungs and taking the first
// that answers. This file is that ladder.
//
// Every rung ends in something real: an item rung uses the item through the engine's own use path,
// and a cast rung gives the cast order (`Rpg.Cast.Order`) with Space held — an AI never
// walks into range to cast — and counts only when the order is taken. The ladder is the engine's,
// read rung by rung (it had been reconstructed from the rungs' names before, and
// was wrong in five places).
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ai;

/// <summary>
/// The rungs of the AI's discipline choice, in the order it tries them. Each one has its own
/// cooldown on the mind and its own pair of <see cref="AiMainFlag"/> bits.
/// </summary>
public enum AiReach
{
    None = 0,
    /// <summary>A healing discipline (category 1). Shares its cooldown with <see cref="HealItem"/>.</summary>
    HealCast,
    /// <summary>A healing item — an effect of kind <c>HealInstant</c> or <c>HealOverTime</c>.</summary>
    HealItem,
    /// <summary>A blood or mana item — a vial.</summary>
    BloodItem,
    /// <summary>A buff (category 2).</summary>
    BuffCast,
    /// <summary>Damage (any category above 9, and the only rung that needs a target).</summary>
    DamageCast,
    /// <summary>A feed (category 6). Vampires only, and only when they are short of blood.</summary>
    FeedCast,
    /// <summary>Hiding (`game.ndd` type 4), on a flat 20-second recast.</summary>
    Hide,
    /// <summary>Summoning (type 3), on a flat 45 seconds, and it will not spend itself dry.</summary>
    Summon,
    /// <summary>Sensing (type 5), on a flat 45 seconds — off the ladder.</summary>
    Sense,
}

/// <summary>
/// The eight cooldown slots a mind carries for the rungs above. Heal is
/// **one** slot for both the cast and the item, so drinking a salve also stops the spell — that is
/// the engine's, not a simplification.
/// </summary>
public enum AiTimer { Heal = 0, Blood, Buff, Damage, Feed, Hide, Summon, Sense }

/// <summary>What a rung came to, so a caller can say so.</summary>
/// <param name="Reach">Which rung answered, or <see cref="AiReach.None"/> if none did.</param>
/// <param name="Item">The item it chose, when the rung was an item one.</param>
/// <param name="Wanted">The discipline a cast rung chose, whether or not the cast order took it.</param>
/// <param name="Cast">Whether the cast order took it (answered 4).</param>
public readonly record struct AiChoice(AiReach Reach, Item? Item = null, string? Wanted = null, bool Cast = false)
{
    /// <summary>The rung did something: used its item, or had its cast order taken.</summary>
    public bool Acted => Reach != AiReach.None && (Item is not null || Cast);
}

public sealed partial class Minds
{
    /// <summary>
    /// The engine's own categories in `game.ndd`: a discipline's type column. The chooser asks for
    /// one of these, except for damage, which asks for <b>10</b> and means "anything above 9".
    /// </summary>
    /// <summary>
    /// The chooser's category is `game.ndd`'s own <c>type</c> column, and the file's header names
    /// every number — so the rungs are HEAL, BUFF, SUMMON, HIDE, SENSE, FEED and DAMAGE rather than
    /// "specials". One is not a plain match: **damage asks for 10 and means "anything above 9"**,
    /// which takes in <see cref="DisciplineType.Mind"/> and <see cref="DisciplineType.Frenzy"/> as
    /// well. So the AI's damage rung will happily cast Mesmerize.
    /// </summary>
    public const int DiscDamageFrom = 10;

    /// <summary>
    /// A quick-slot item or discipline is put in the candidate list <b>five times</b> rather than
    /// once, but only for a player-type character — which is the manual's
    /// *"the Disciplines you place in the Quick Use Slots are more likely to be used by the AI than
    /// any other powers"*, made a number.
    /// </summary>
    public const int QuickWeight = 5;

    /// <summary>
    /// The whole decision, for one character. <paramref name="target"/>
    /// is what it is fighting, or null. Called from both combat decisions — a party member's
    /// and an enemy's — and a rung that answers ends the turn.
    ///
    /// The gates first: the master cast flag (a second's pause), **a frenzied mind never walks the
    /// ladder** (its mode handler feeds instead), no disciplines at all, every rung on cooldown, or
    /// busy, dead or held. Then, with H = hurt (`lowhealth`), VH = badly hurt (`verylowhealth`),
    /// LB = short of blood, D = <see cref="Mind.Danger"/> and S = how long since it took its target:
    /// <list type="number">
    /// <item>H: a healing item, a healing cast, a hide.</item>
    /// <item>VH or LB: a vial.</item>
    /// <item>D&gt;1 and VH: a healing item, a healing cast; D&gt;2 and VH: a hide.</item>
    /// <item>Then only when D&gt;3 or not short of blood: D&gt;2 and S&lt;2 s — a buff; D&gt;3 and
    /// S&lt;8 s — a hide; D&gt;4 and S&lt;5 s — a summoning.</item>
    /// <item>With a target: D&lt;2 and LB — feed. A **vampire** then rolls: short of blood, 15% (30%
    /// for a party member); and whatever its blood, 5% (10% for a party member).</item>
    /// <item>D&gt;1: damage.</item>
    /// </list>
    ///
    /// **LB is the character's own blood**, never the leader's: every one of the ladder's calls to
    /// the test is made on the asking mind each time,
    /// and so it reads the mind's own actor. And the fourth line makes the
    /// feed rung behave the other way round from what its name suggests: a character short of blood
    /// with D ≤ 3 returns before the feed, so the "D&lt;2 and LB" rung can never be reached,
    /// while one with blood to spare rolls its 5% or 10% on every walk. That is why Wilhem, at 100 of
    /// 120 in a Prague save, goes to feed a few seconds into the fight while Christof is nearly
    /// dry — as 1.1's Wilhem does, at 100 of
    /// 120, with Christof at 0 (`PartyFeedTests`).
    /// </summary>
    public AiChoice Reach(Mind mind, WorldThing? target)
    {
        if (mind.Thing.Actor is not { } me) return default;

        // The master switch, before anything else — and it costs a second of not asking again.
        if (Barred(mind.Thing, AiMainFlag.NoEnemyCast, AiMainFlag.NoPartyCast))
        {
            mind.NextThink = Now + 1000;
            return default;
        }
        if (mind.Mode == AiMode.Frenzied) return default;
        // Nothing to cast with, nothing to think about. The gate is the discipline
        // list — and **nothing else**: it is tested alone, with no inventory alternative
        // anywhere in the function, so the item rungs live inside the discipline ladder.
        if (!me.HasDisciplines) return default;
        if (!mind.AnyReady(Now)) return default;
        if (me.BusyUntil > Now || me.Dead || me.Has2(Actor.Flag2.Restrained)) return default;

        bool hurt = Hurt(mind, me, mind.Class.LowHealth);
        bool badlyHurt = Hurt(mind, me, mind.Class.VeryLowHealth);
        bool lowBlood = LowBlood(mind, me);
        int danger = mind.Danger;
        int since = Now - mind.TargetAt;

        if (hurt && UseItem(mind, AiReach.HealItem) is { Acted: true } a) return a;
        if (hurt && HealRung(mind) is { Acted: true } b) return b;
        if (hurt && HideRung(mind) is { Acted: true } c) return c;
        if ((badlyHurt || lowBlood) && UseItem(mind, AiReach.BloodItem) is { Acted: true } d) return d;
        if (danger > 1 && badlyHurt && UseItem(mind, AiReach.HealItem) is { Acted: true } e) return e;
        if (danger > 1 && badlyHurt && HealRung(mind) is { Acted: true } f) return f;
        if (danger > 2 && badlyHurt && HideRung(mind) is { Acted: true } g) return g;
        if (danger <= 3 && lowBlood) return default;

        var gates = Rules.GameRules.Current.Ai.LadderDangerGates;
        if (danger > gates[0] && since < gates[1] && BuffRung(mind) is { Acted: true } h) return h;
        if (danger > gates[2] && since < gates[3] && HideRung(mind) is { Acted: true } i) return i;
        if (danger > gates[4] && since < gates[5] && SummonRung(mind) is { Acted: true } j) return j;
        if (target is null) return default;

        if (danger < gates[6] && lowBlood && Feed(mind, target) is { Acted: true } k) return k;
        if (me.Type == ActorType.Vampire)
        {
            bool companion = PlayerType(mind.Thing) && me.Has(Actor.Flag.Party);
            var ai = Rules.GameRules.Current.Ai;
            var (enemy, own) = (ai.AiOpportunisticFeedChance, ai.AiOpportunisticFeedChanceCompanion);
            if (lowBlood && !companion && Random.Unit() < enemy[0] && Feed(mind, target) is { Acted: true } l) return l;
            if (lowBlood && companion && Random.Unit() < own[0] && Feed(mind, target) is { Acted: true } m) return m;
            if (!companion && Random.Unit() < enemy[1] && Feed(mind, target) is { Acted: true } n) return n;
            if (companion && Random.Unit() < own[1] && Feed(mind, target) is { Acted: true } o) return o;
        }
        if (danger > 1 && DamageRung(mind, target) is { Acted: true } p) return p;
        return default;
    }

    /// <summary>
    /// Whether a character is a `THING_TYPE_PLAYER` (`thing+4 == 7`), the test the ladder makes
    /// wherever a party rule differs from an enemy's. Read off the character, where the build
    /// mirrors the thing's type (<see cref="Actor.IsPlayerThing"/>).
    /// </summary>
    static bool PlayerType(WorldThing who) => who.Actor?.IsPlayerThing == true;

    /// <summary>Whether a main flag bars this character, by whether it is a player-type thing.</summary>
    bool Barred(WorldThing who, uint enemyBit, uint partyBit) =>
        (MainFlags & (PlayerType(who) ? partyBit : enemyBit)) != 0;

    /// <summary>
    /// `health &lt;= share x maxHealth x stanceScale` (the health and blood tests — the same
    /// test against `verylowhealth` and `lowhealth`, whose defaults the engine's constructor has the
    /// wrong way round). The stance counts only for a party member.
    /// </summary>
    static bool Hurt(Mind mind, Actor me, float share) =>
        me.MaxHealth > 0f && me.Health <= me.MaxHealth * share * StanceScale(mind, me);

    /// <summary>The stance's weight on the three thresholds (1.2 / 1.0 / 0.75), a party member only.</summary>
    static float StanceScale(Mind mind, Actor me) =>
        PlayerType(mind.Thing) && me.Has(Actor.Flag.Party) ? Minds.FightLeashHealth(me.Stance) : 1f;

    /// <summary>
    /// `blood &lt;= lowblood x bloodPool x stanceScale`, swapping to mana for a human — the same
    /// `ACTOR_TYPE` branch the status bar makes.
    /// </summary>
    static bool LowBlood(Mind mind, Actor me)
    {
        bool human = me.Type == ActorType.Human;
        float have = me.Stat[(int)(human ? ActorStat.Mana : ActorStat.Blood)];
        float pool = me.Stat[(int)(human ? ActorStat.ManaPool : ActorStat.BloodPool)];
        return have <= pool * mind.Class.LowBlood * StanceScale(mind, me);
    }

    // ---- the rungs --------------------------------------------------------------------------

    /// <summary>
    /// A cast rung's common end: the cast order at the chosen discipline, **Space held** so an AI
    /// never walks into range, and on "taken" the rung's cooldown and its event to everybody within
    /// 500. A refusal arms nothing — but the order has already cancelled what the character was
    /// doing and stopped its walk (the cast order's first lines).
    /// </summary>
    AiChoice Order(Mind mind, AiReach reach, Discipline chosen, WorldThing? target, AiTimer timer, int until, int e)
    {
        var result = Rpg.Cast.Order(mind.Thing, target, null, chosen, Rpg.Cast.NoWalk);
        if (result != OrderResult.Started) return new AiChoice(reach, null, chosen.Name);
        mind.Arm(timer, until);
        Shout(mind, e, Rules.GameRules.Current.Ai.CastShoutRange, target);
        return new AiChoice(reach, null, chosen.Name, true);
    }

    /// <summary>How far every cast rung's event carries (0x43fa0000).</summary>
    public const float CastShoutRange = 500f;

    /// <summary>
    /// A roll failed: the rung's cooldown is set to <c>now + rate × (1 + rand01/4)</c>.
    /// </summary>
    void Jitter(Mind mind, AiTimer timer, int rate) =>
        mind.Arm(timer, (int)(Now + rate * (Random.Unit() * 0.25 + 1.0)));

    /// <summary>The heal rung: a HEAL discipline, cooldown `discHealRate`, event 10.</summary>
    AiChoice HealRung(Mind mind, bool ignoreCooldown = false)
    {
        if (Barred(mind.Thing, AiMainFlag.NoEnemyCastHeal, AiMainFlag.NoPartyCastHeal)) { mind.NextThink = Now + 1000; return default; }
        if (!ignoreCooldown && !mind.Ready(AiTimer.Heal, Now)) return default;
        if (Pick(mind.Thing, (int)DisciplineType.Heal, null) is not { } d) return default;
        return Order(mind, AiReach.HealCast, d, null, AiTimer.Heal, Now + mind.Class.DiscHealRate, AiEvent.Heal);
    }

    /// <summary>
    /// The buff rung: unless forced, a roll against `discBuffPercent` × the stance (1.2 /
    /// 1.0 / 0.7 for a player thing); a BUFF discipline; and a player thing keeps 30 ÷ that scale of
    /// blood in hand after paying for it. Cooldown `discBuffRate`, event 11.
    /// </summary>
    AiChoice BuffRung(Mind mind, bool ignoreCooldown = false, bool skipRoll = false)
    {
        if (Barred(mind.Thing, AiMainFlag.NoEnemyCastBuff, AiMainFlag.NoPartyCastBuff)) { mind.NextThink = Now + 1000; return default; }
        if (!ignoreCooldown && !mind.Ready(AiTimer.Buff, Now)) return default;
        if (mind.Thing.Actor is not { } me) return default;
        var rules = Rules.GameRules.Current.Ai;
        float scale = PlayerType(mind.Thing) ? ByStance(rules.StanceCastScaleBuff, me.Stance) : 1f;
        if (!skipRoll && scale * mind.Class.DiscBuffPercent <= Random.Unit())
        {
            Jitter(mind, AiTimer.Buff, mind.Class.DiscBuffRate);
            return default;
        }
        if (Pick(mind.Thing, (int)DisciplineType.Buff, null) is not { } d) return default;
        if (PlayerType(mind.Thing) && me.Stat[(int)ActorStat.Blood] - d.Cost < rules.CompanionKeepBlood / scale) return default;
        return Order(mind, AiReach.BuffCast, d, null, AiTimer.Buff, Now + mind.Class.DiscBuffRate, AiEvent.Buff);
    }

    /// <summary>What a player-type buffer or summoner keeps in hand after paying.</summary>
    public const float KeepBlood = 30f;

    /// <summary>
    /// The ladder's gates (1.1's): a buff over danger 2 within 2 s of taking the target, a hide over 3 within 8 s, a summon over
    /// 4 within 5 s; a feed under danger 2.
    /// </summary>
    public static readonly int[] LadderGates = [2, 2000, 3, 8000, 4, 5000, 2];

    /// <summary>A vampire's chance to feed mid-fight: with low blood 0.15 (a companion 0.30), otherwise 0.05 (a companion 0.10).</summary>
    public static readonly float[] FeedChances = [0.15f, 0.30f, 0.05f, 0.10f];

    /// <summary>A party member's cast percentages by stance (attack, neutral, defence): the buffs' 1.2/1/0.7, the damage's 1.25/1/0.5.</summary>
    public static readonly float[] StanceScales = [1.2f, 1f, 0.7f, 1.25f, 1f, 0.5f];

    /// <summary>A stance's value of a rules list of three (attack, neutral, defence) from <paramref name="at"/>.</summary>
    internal static float ByStance(float[] list, PartyStance stance, int at = 0) => stance switch
    {
        PartyStance.Attack => list[at],
        PartyStance.Defense => list[at + 2],
        _ => list[at + 1],
    };

    /// <summary>
    /// The damage rung: the master "damage" flag costs a quarter-second; unless forced, a
    /// roll against `discDamagePercent` × the stance (1.25 / 1.0 / 0.5 for a player thing); any
    /// discipline of type above 9, at the target. Cooldown `discDamageRate`, event 12.
    /// </summary>
    AiChoice DamageRung(Mind mind, WorldThing target, bool ignoreCooldown = false, bool skipRoll = false)
    {
        if (Barred(mind.Thing, AiMainFlag.NoEnemyCastDamage, AiMainFlag.NoPartyCastDamage)) { mind.NextThink = Now + 250; return default; }
        if (!ignoreCooldown && !mind.Ready(AiTimer.Damage, Now)) return default;
        if (mind.Thing.Actor is not { } me) return default;
        float scale = PlayerType(mind.Thing) ? ByStance(Rules.GameRules.Current.Ai.StanceCastScaleDamage, me.Stance) : 1f;
        if (!skipRoll && scale * mind.Class.DiscDamagePercent <= Random.Unit())
        {
            Jitter(mind, AiTimer.Damage, mind.Class.DiscDamageRate);
            return default;
        }
        if (Pick(mind.Thing, DiscDamageFrom, target) is not { } d) return default;
        return Order(mind, AiReach.DamageCast, d, target, AiTimer.Damage, Now + mind.Class.DiscDamageRate, AiEvent.CastHarm);
    }

    /// <summary>
    /// The feed rung: the "special" flag costs a quarter-second; unless forced, a roll
    /// against `discFeedPercent`, no stance; a FEED discipline at the target. Cooldown
    /// `discFeedRate`, event 12. The frenzied mode calls it forced, with no cooldown and no roll.
    /// </summary>
    public bool FeedRung(Mind mind, WorldThing target, bool ignoreCooldown = false, bool skipRoll = false) =>
        Feed(mind, target, ignoreCooldown, skipRoll).Acted;

    AiChoice Feed(Mind mind, WorldThing target, bool ignoreCooldown = false, bool skipRoll = false)
    {
        if (Barred(mind.Thing, AiMainFlag.NoEnemyCastSpecial, AiMainFlag.NoPartyCastSpecial)) { mind.NextThink = Now + 250; return default; }
        if (!ignoreCooldown && !mind.Ready(AiTimer.Feed, Now)) return default;
        if (!skipRoll && mind.Class.DiscFeedPercent <= Random.Unit())
        {
            Jitter(mind, AiTimer.Feed, mind.Class.DiscFeedRate);
            return default;
        }
        if (Pick(mind.Thing, (int)DisciplineType.Feed, target) is not { } d) return default;
        return Order(mind, AiReach.FeedCast, d, target, AiTimer.Feed, Now + mind.Class.DiscFeedRate, AiEvent.CastHarm);
    }

    /// <summary>The hide rung: a HIDE discipline, a flat 20 s, event 13.</summary>
    AiChoice HideRung(Mind mind, bool ignoreCooldown = false)
    {
        if (Barred(mind.Thing, AiMainFlag.NoEnemyCastSpecial, AiMainFlag.NoPartyCastSpecial)) { mind.NextThink = Now + 250; return default; }
        if (!ignoreCooldown && !mind.Ready(AiTimer.Hide, Now)) return default;
        if (Pick(mind.Thing, (int)DisciplineType.Hide, null) is not { } d) return default;
        return Order(mind, AiReach.Hide, d, null, AiTimer.Hide, Now + 20_000, AiEvent.Special);
    }

    /// <summary>
    /// The summoning rung: a SUMMON discipline — a player thing keeping 30 blood in hand
    /// after paying — a flat 45 s, event 13.
    /// </summary>
    AiChoice SummonRung(Mind mind, bool ignoreCooldown = false)
    {
        if (Barred(mind.Thing, AiMainFlag.NoEnemyCastSpecial, AiMainFlag.NoPartyCastSpecial)) { mind.NextThink = Now + 250; return default; }
        if (!ignoreCooldown && !mind.Ready(AiTimer.Summon, Now)) return default;
        if (Pick(mind.Thing, (int)DisciplineType.Summon, null) is not { } d) return default;
        if (PlayerType(mind.Thing) && mind.Thing.Actor!.Stat[(int)ActorStat.Blood] - d.Cost < Rules.GameRules.Current.Ai.CompanionKeepBlood) return default;
        return Order(mind, AiReach.Summon, d, null, AiTimer.Summon, Now + 45_000, AiEvent.Special);
    }

    /// <summary>
    /// The sense rung: a SENSE discipline, a flat 45 s, event 13 — not on the ladder at
    /// all; reached when an enemy loses its target and when one of the character's side
    /// is hurt or killed by something it cannot notice (events 4 and 5).
    /// </summary>
    public bool SenseRung(Mind mind, bool ignoreCooldown = false)
    {
        if (Barred(mind.Thing, AiMainFlag.NoEnemyCastSpecial, AiMainFlag.NoPartyCastSpecial)) { mind.NextThink = Now + 250; return false; }
        if (!ignoreCooldown && !mind.Ready(AiTimer.Sense, Now)) return false;
        if (Pick(mind.Thing, (int)DisciplineType.Sense, null) is not { } d) return false;
        return Order(mind, AiReach.Sense, d, null, AiTimer.Sense, Now + 45_000, AiEvent.Special).Acted;
    }

    /// <summary>The heal rung as the party think's tail calls it, and the pet's.</summary>
    AiChoice Cast(Mind mind, AiReach reach) => reach == AiReach.HealCast ? HealRung(mind) : default;

    /// <summary>
    /// The discipline chooser, the twin of the item chooser, down to
    /// the quirks. A candidate has to pass four tests:
    ///
    /// <list type="bullet">
    /// <item>the quick-slot filter, when <see cref="AiMainFlag.QuickDisciplinesOnly"/> binds it;</item>
    /// <item>its <b>category</b> — `game.ndd`'s `type`, except that <b>10 means "anything above
    /// 9"</b>, so the damage rung also casts Mind and Frenzy disciplines;</item>
    /// <item>it has to be <b>affordable</b>: the blood cost at its level, scaled by the caster's
    /// <c>BLOODCOST</c> effect multiplier, against blood — or mana for a human, the same
    /// `ACTOR_TYPE` branch as everywhere else;</item>
    /// <item>and the target, when there is one, has to be within the discipline's `maxDist`.</item>
    /// </list>
    ///
    /// Then the same two quirks as the items: a quick-slot discipline is entered <b>five times</b>
    /// for a player-type caster, and the winner is picked by the <b>clock</b> rather than at random.
    /// </summary>
    public Discipline? Pick(WorldThing who, int category, WorldThing? target)
    {
        if (who.Actor is not { } me) return null;
        var pool = new List<Discipline>();
        bool quickOnly = (MainFlags & AiMainFlag.QuickDisciplinesOnly) != 0 && PlayerType(who);
        bool human = me.Type == ActorType.Human;
        float have = me.Stat[(int)(human ? ActorStat.Mana : ActorStat.Blood)];
        float scale = who.Effects.Value(Formats.EffectKind.BloodCost);

        foreach (var known in me.Disciplines.All)
        {
            if (quickOnly && known.QuickSlot < 0) continue;
            bool wanted = category == DiscDamageFrom
                ? (int)known.Type > 9
                : (int)known.Type == category;
            if (!wanted) continue;
            if (scale * known.Cost > have) continue;
            if (target is not null && !Rpg.Cast.InRange(known, who, target.Position)) continue;

            pool.Add(known);
            if (PlayerType(who) && known.QuickSlot >= 0)
                for (int i = 1; i < Rules.GameRules.Current.Ai.QuickSlotWeight; i++) pool.Add(known);
        }
        return pool.Count == 0 ? null : pool[Now % pool.Count];
    }

    /// <summary>
    /// An item rung, which is real. The chooser is the engine's and so is the
    /// cooldown, which comes from the `.nai` rather than from a constant.
    /// </summary>
    AiChoice UseItem(Mind mind, AiReach reach)
    {
        if (mind.Thing.Actor is not { } me) return default;
        var timer = reach == AiReach.HealItem ? AiTimer.Heal : AiTimer.Blood;
        if (!mind.Ready(timer, Now)) return default;
        var item = Choose(me, reach == AiReach.HealItem ? 1 : 2);
        if (item is null) return default;
        // And this is where the ladder meets the effect subsystem: the engine's own use path,
        // which puts the item's effects on the drinker and spends a
        // charge. A refusal costs the rung nothing — no cooldown is armed and the ladder goes on.
        var used = Items.Use(mind.Thing, item, Now, Effects?.File, rng: Random);
        if (used is UseResult.Refused or UseResult.NotWritten) return default;
        if (item.Destroyed) me.Inventory.Remove(item);
        mind.Arm(timer, Now + (reach == AiReach.HealItem ? mind.Class.ItemHealRate : mind.Class.ItemBloodRate));
        mind.NextThink = Now + 500;
        return new AiChoice(reach, item);
    }

    /// <summary>
    /// The item chooser. Two categories, and both are decided by the item's
    /// **effects** rather than by its type:
    ///
    /// <list type="bullet">
    /// <item><b>1</b> — anything carrying a <c>HealInstant</c> or <c>HealOverTime</c> effect.</item>
    /// <item><b>2</b> — anything carrying a stat effect on <c>BLOOD</c>, or on <c>MANA</c> when the
    /// character is human. A vial, in other words, and the same `ACTOR_TYPE` branch as everywhere
    /// else.</item>
    /// </list>
    ///
    /// Three details that are the engine's and look like mistakes: the winner is picked by the
    /// <b>clock</b> modulo the candidate count rather than by a random number, so the same character
    /// in the same state a millisecond later can reach for a different bottle; a quick-slot item on
    /// a player-type character is entered five times, so it is five times as likely; and an item
    /// flagged <c>0x10000</c> is skipped outright.
    /// </summary>
    public Item? Choose(Actor me, int category)
    {
        var pool = new List<Item>();
        bool quickOnly = (MainFlags & AiMainFlag.QuickItemsOnly) != 0 && me.IsPlayerThing;
        foreach (var item in me.Inventory.Items)
        {
            if ((item.Flags & ItemFlags.NoPartyAiUse) != 0) continue;
            if (quickOnly && item.Location != Rpg.Slot.QuickUse) continue;
            if (!InCategory(me, item, category)) continue;
            pool.Add(item);
            if (me.IsPlayerThing && item.Location == Rpg.Slot.QuickUse)
                for (int i = 1; i < Rules.GameRules.Current.Ai.QuickSlotWeight; i++) pool.Add(item);
        }
        return pool.Count == 0 ? null : pool[Now % pool.Count];
    }

    /// <summary>Whether any of an item's effects puts it in the category.</summary>
    bool InCategory(Actor me, Item item, int category)
    {
        int want = me.Type == ActorType.Human ? (int)ActorStat.Mana : (int)ActorStat.Blood;
        foreach (var carried in item.Effects)
        {
            if (Effects?[carried.Name] is not { } def) continue;
            if (category == 1 && def.Kind is EffectKind.HealInstant or EffectKind.HealOverTime) return true;
            if (category == 2 && def.Subtype == want
                && def.Kind is EffectKind.BaseStatInstant or EffectKind.BaseStatOverTime or EffectKind.Stat)
                return true;
        }
        return false;
    }

    // How much trouble the character is in — `Threat`, the balance of strength — is in
    // Danger.cs.
}
