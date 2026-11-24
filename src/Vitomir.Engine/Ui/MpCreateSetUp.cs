// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the multiplayer create screen's Create does to a new character before the advancement screen
// opens on it (read whole). `MpMenus.NewBody` makes the body;
// this is 1.1's set-up of it, in 1.1's order.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **A new multiplayer character's set-up**, in the order 1.1 does it:
/// <list type="number">
/// <item>a clan with the "no socials" weakness (flag 4, Nosferatu) has Appearance put to 0, base and
/// current (stat 13);</item>
/// <item>the first discipline of each of the clan's three groups is made, known and not yet
/// learnt (level −1) — what the advancement screen then sells;</item>
/// <item>a clan flagged to know every group (flag 1, the Caitiff) is given the first discipline of
/// **every group but those flagged 0x1** — the first of each over the whole group table, in `game.ndg`'s order, the
/// group's flag 1 tested (<see cref="ClanOnlyGroup"/>);</item>
/// <item>clan 14 (Ventrue) has its base Blood Rating (stat 15) put to 0.6 (0x3f19999a), clan 7 (Giovanni) to
/// 0.75 (0x3f400000) and the actor flag 0x800 (`THING_AF_FASTDRINKER`) — the clan's number, the record's
/// first field, which the loader takes from the name;</item>
/// <item>every new character is flagged 0x8000 (`THING_AF_HUMANITYTRACK`);</item>
/// <item>the three born effects taken off (with notify: `ef_increasemanaot`, `ef_increasebloodot`,
/// `ef_decreasefrenzy`) and those of the chosen type put back (level 0): a vampire's or a ghoul's
/// `ef_decreasefrenzy`, a human's `ef_increasemanaot` and `ef_increasebloodot`;</item>
/// <item>a vampire or a ghoul learns `feed` and `bloodhealing`.</item>
/// </list>
/// The purse (200) and a human's clan (`human`) are the character's own
/// (<see cref="Net.NetCharacter"/>), set before this runs.
/// </summary>
public static class MpCreateSetUp
{
    /// <summary>A discipline group's flag (`game.ndg`'s last column) that keeps it from a clan knowing every group: Mortis, Thaumaturgy's paths and rituals, Numina, Faith, Obtenebration, Serpentis.</summary>
    public const uint ClanOnlyGroup = 0x1;
    /// <summary>A clan's advantage (flag 1): the first discipline of every group (the Caitiff).</summary>
    public const uint AllGroups = 0x1;
    /// <summary>The clans with a Blood Rating of their own (the record's id against 0xe and 7).</summary>
    public const int Ventrue = 14, Giovanni = 7;
    /// <summary>Their base Blood Rating (0x3f19999a, 0x3f400000).</summary>
    public const float VentrueBloodRating = 0.6f, GiovanniBloodRating = 0.75f;
    /// <summary>The effects Create takes off before putting the type's back.</summary>
    public static readonly string[] Born = ["ef_increasemanaot", "ef_increasebloodot", "ef_decreasefrenzy"];

    /// <summary>1.1's set-up of <paramref name="thing"/>, whose actor has its type and its clan's record.</summary>
    /// <param name="effects">The effects' table; null leaves the born effects alone (no install).</param>
    public static void Apply(WorldThing thing, DisciplineDefinitions powers, EffectDefinitions? effects)
    {
        if (thing.Actor is not { } actor) return;
        if (actor.ClanRecord is { } clan)
        {
            if ((clan.Weaknesses & Clan.WeakSocials) != 0)
            {
                actor.SetBaseStat((int)ActorStat.Appearance, 0f);
                actor.Stat[(int)ActorStat.Appearance] = 0f;
            }
            foreach (var group in clan.Groups)
                if (powers.FirstInGroup(group) is { } first) DisciplineBook.Make(thing, first);
            if ((clan.Advantages & AllGroups) != 0 && powers.Groups is { } table)
                foreach (var group in table.All)
                    if ((group.Flags & ClanOnlyGroup) == 0 && powers.FirstInGroup(group.Name) is { } first)
                        DisciplineBook.Make(thing, first);
            switch (Clans.IdOf(clan.Name, inFile: true))
            {
                case Ventrue: actor.SetBaseStat((int)ActorStat.BloodRating, Rules.GameRules.Current.Feeding.NetClanBloodRating[0]); break;
                case Giovanni:
                    actor.SetBaseStat((int)ActorStat.BloodRating, Rules.GameRules.Current.Feeding.NetClanBloodRating[1]);
                    if (Rules.GameRules.Current.Feeding.GiovanniFastDrinker) actor.Flags |= Actor.Flag.FastDrinker;
                    break;
            }
        }
        actor.Flags |= Actor.Flag.HumanityTrack;
        foreach (var name in Born) EffectRuntime.Remove(thing, name, notify: true);
        if (effects is not null)
            foreach (var name in BornTo(actor.Type))
                if (effects[name] is { } def) EffectRuntime.Apply(thing, def, 0, thing.Place?.Milliseconds ?? 0);
        if (actor.Type is ActorType.Vampire or ActorType.Ghoul)
            foreach (var name in new[] { "feed", "bloodhealing" })
                DisciplineBook.SetLevel(thing, powers, name, 0);
    }

    /// <summary>The effects a new character of a type is born with (a switch on the type): none for a monster.</summary>
    public static string[] BornTo(ActorType type) => Rules.GameRules.Current.Healing.Born(type);
}
