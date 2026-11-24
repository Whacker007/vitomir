// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>
/// The tempo channel (ours, <i>"Melee and Brawl make a swing come and recover faster and a block rise
/// sooner"</i>): each tick, every fighter (a <see cref="Combatant"/> with a sheet) is given the platform's
/// <see cref="CombatTempo"/> from the effective Ability its blows use — Melee with a weapon in hand, Brawl bare-handed (a
/// bare-handed class of <see cref="SkillData.BareClasses"/>, its weapon read through <see cref="HarmWeapons"/>) — by the
/// tables of <see cref="SkillData.TempoAt"/>. So whatever changes the sheet (the arena's values set after a person is made,
/// a modifier, a weapon taken up) is played from the next blow on. Written only when it changes; a fighter with no sheet
/// keeps none (1.1's timing). <see cref="Phase.Act"/>, before the attacks. Allocates nothing per tick.
/// <para><b>Celerity.</b> The one writer of the tempo also gives Celerity's attack factor while it is live
/// (<see cref="CelerityEffect"/>, the one place it is worked out): skill × Celerity for the swing, the recovery and the
/// block's rise. With the skill's channel off (<paramref name="data"/> null: <c>skill-tempo-off</c>) it writes Celerity
/// alone, and only while Celerity is live — a fighter without it keeps no tempo (1.1's timing), as before; with
/// <paramref name="celerity"/> null (<c>celerity-attack-off</c>, or no Celerity in the data) it writes the skill alone, as
/// the first contest did.</para>
/// <para><b>The global combat tempo.</b> <paramref name="global"/> (<c>pace.json</c>'s <c>tempo.global</c>, 0.70;
/// <c>--combat-tempo</c> over it; 1 with <c>combat-tempo-off</c>) multiplies the swing and the recovery of every fighter with a sheet —
/// skill × Celerity × global — never the block's rise. With the skill's channel off it is written alone (with Celerity's while
/// live), so it holds whatever the switches. At 1 every number is as before, bit for bit.</para>
/// </summary>
public sealed class SkillTempoSystem(SkillData? data, HarmData harm, CelerityEffect? celerity = null, float global = 1f) : ISystem
{
    readonly float global = global;
    int armed = -1, bare = -1, fightersKnown;
    HarmWeapons? weapons;
    Store<ActorTempo>? own;   // the fighters' own tempos, when a scenario gave any
    bool ownKnown;

    public void Run(World world)
    {
        if (fightersKnown == 0)
        {
            try { world.Store<Combatant>(); fightersKnown = 1; }
            catch (InvalidOperationException) { fightersKnown = 2; }    // a world with no fighting
        }
        if (fightersKnown != 1) return;
        var sheets = world.Service<ActorSheets>();
        if (armed < 0 && data is not null) { armed = sheets.Layout.Require(data.ArmedAbility); bare = sheets.Layout.Require(data.BareAbility); }
        weapons ??= world.TryService<HarmWeapons>(out var hw) ? hw : new HarmWeapons();
        var fighters = world.Store<Combatant>();
        var tempos = world.Store<CombatTempo>();
        if (!ownKnown) { own = world.TryStore<ActorTempo>(out var o) ? o : null; ownKnown = true; }
        var ents = fighters.Entities;
        for (int i = 0; i < ents.Length; i++)
        {
            var e = ents[i];
            if (!sheets.Has(e)) continue;
            float swing = 1f, recovery = 1f, rise = 1f;
            if (data is not null)
            {
                bool bareHanded = harm.ClassOf(weapons.Of(world, e)) is not { } cls || data.IsBareHanded(cls.Id);
                (swing, recovery, rise) = data.TempoAt(sheets.Effective(e, bareHanded ? bare : armed));
            }
            // Celerity's attack factor while it is live (CelerityEffect: the one place, switchable).
            float c = celerity?.Attack(sheets, e, world.Tick) ?? 1f;
            float global = ActorTempos.Of(own, e, this.global);   // its own tempo in place of the global, when it has one
            ref var have = ref tempos.Find(e);
            if (data is null && c == 1f && global == 1f)
            {
                // The skill's channel off, no Celerity live and no global tempo: no tempo (1.1's timing), as the first contest's world without the channel.
                if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref have)) tempos.Remove(e);
                continue;
            }
            var want = global == 1f ? new CombatTempo(swing * c, recovery * c, rise * c)
                : new CombatTempo(swing * c * global, recovery * c * global, rise * c);   // not the rise
            if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref have)) world.Set(e, want);
            else if (have.Swing != want.Swing || have.Recovery != want.Recovery || have.Rise != want.Rise) have = want;
        }
    }
}

/// <summary>
/// <b>The dodge's numbers</b> (ours, <c>skill.json</c>'s <c>dodge</c>): each tick, every fighter (a <see cref="Combatant"/>
/// with a sheet) is given the platform's <see cref="Platform.Bodies.DodgeTempo"/> from its effective Dodge (the start and the
/// recovery) and effective Dexterity (the burst's time, and the masquerade's human-held one), by <see cref="DodgeNumbers.At"/>;
/// written only when it changes, so a Blood Dexterity raised or a sheet's value set by the arena is danced from the next
/// dodge on. <paramref name="dexterityOn"/> false (the switch <c>dodge-dexterity-off</c>): Dexterity's link off, the burst a
/// human's for everyone. Celerity and the masquerade are the platform's (Bodies/Dodge.cs). <see cref="Phase.Act"/>, before
/// the platform's dodge. Allocates nothing per tick.
/// </summary>
public sealed class DodgeTempoSystem(DodgeNumbers numbers, SkillData data, bool dexterityOn = true) : ISystem
{
    int dodge = -1, dexterity = -1, fightersKnown;

    public void Run(World world)
    {
        if (fightersKnown == 0)
        {
            try { world.Store<Combatant>(); fightersKnown = 1; }
            catch (InvalidOperationException) { fightersKnown = 2; }    // a world with no fighting
        }
        if (fightersKnown != 1 || !world.TryStore<Platform.Bodies.DodgeTempo>(out var tempos)) return;   // no bodies: no dodge
        var sheets = world.Service<ActorSheets>();
        if (dodge < 0) { dodge = sheets.Layout.Require(data.DodgeAbility); dexterity = sheets.Layout.Require(data.DexterityTrait); }
        var ents = world.Store<Combatant>().Entities;
        for (int i = 0; i < ents.Length; i++)
        {
            var e = ents[i];
            if (!sheets.Has(e)) continue;
            var want = numbers.At(sheets.Effective(e, dodge), sheets.Effective(e, dexterity), dexterityOn);
            ref var have = ref tempos.Find(e);
            if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref have)) world.Set(e, want);
            else if (!have.Equals(want)) have = want;
        }
    }
}

/// <summary>The tempo channel registered.</summary>
public static class SkillTempoServices
{
    /// <summary>In <see cref="Phase.Act"/>, before the platform's attacks (<see cref="CombatServices.AttackOrder"/>).</summary>
    public const int TempoOrder = 90;

    /// <summary>
    /// Registers the platform's <see cref="CombatTempo"/> component and the system <c>enhanced.skill-tempo</c>: the skill's
    /// channel from <paramref name="data"/> (null: off), Celerity's attack factor from <paramref name="celerity"/> (null: off),
    /// the global combat tempo <paramref name="global"/> (1: none). All three off registers nothing.
    /// </summary>
    /// <summary>The dodge's numbers' place: <c>Act/91</c>, after the tempo and before the platform's dodge (96).</summary>
    public const int DodgeOrder = 91;

    /// <summary>
    /// Registers <c>enhanced.dodge-tempo</c> (<see cref="DodgeTempoSystem"/>) when <paramref name="data"/> has a
    /// <c>dodge</c> section; <paramref name="dexterityOn"/> false is the switch <c>dodge-dexterity-off</c>; <paramref name="pace"/>
    /// (<c>dodge-paced</c>, or pace.json's <c>dodge.use</c>) puts pace.json's burst and recovery in place of
    /// skill.json's (null: skill.json's). The platform's <see cref="Platform.Bodies.DodgeTempo"/> is registered with the movement.
    /// </summary>
    public static WorldBuilder RegisterDodge(WorldBuilder b, SkillData? data, bool dexterityOn = true, PaceData? pace = null)
    {
        if (data?.Dodge is not { } n) return b;
        if (pace is not null) n = n.WithTimes(pace.DodgeMoveSeconds, pace.DodgeRecoverSeconds);   // the dodge's pace
        b.System(Phase.Act, DodgeOrder, "enhanced.dodge-tempo", new DodgeTempoSystem(n, data, dexterityOn));
        return b;
    }

    public static WorldBuilder Register(WorldBuilder b, SkillData? data, HarmData harm, CelerityEffect? celerity = null, float global = 1f)
    {
        if (data is null && celerity is null && global == 1f && !ActorTempos.Registered(b.Peek)) return b;   // a fighter's own tempo needs the system
        CombatTempos.Register(b);
        b.System(Phase.Act, TempoOrder, "enhanced.skill-tempo", new SkillTempoSystem(data, harm, celerity, global));
        return b;
    }
}
