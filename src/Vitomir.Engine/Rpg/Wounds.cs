// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What being hurt does to a character besides the lost health: slower, weaker, clumsier, and past a
// point limping. The original keeps a five-rung ladder of health fractions and re-applies three
// effects whenever a character moves between rungs — from the damage, the heal,
// and frenzy going on and off.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>The wound ladder and its penalties.</summary>
public static class Wounds
{
    /// <summary>
    /// Which rung a character is on (the ladder is inlined at every caller):
    /// health over max above 0.69 → 0; above 0.39 → 1; above 0.19 → 2; above **0.9** → 3; above 0 →
    /// 4; else 5. The fourth threshold reads 0.9, which is plainly meant to
    /// be 0.09 — so **rung 3 can never be reached** and anybody at or under 19% is on rung 4. A
    /// **frenzied** character is always on rung 0 (tested first).
    /// </summary>
    public static int Level(WorldThing thing, float health)
    {
        if (thing.Actor is not { } a) return 0;
        if (thing.Mind is { Mode: Ai.AiMode.Frenzied }) return 0;
        float f = health / a.MaxHealth;
        var t = Rules.GameRules.Current.Wounds.WoundLadderThresholds;
        for (int rung = 0; rung < t.Length; rung++)
            if (f > t[rung]) return rung;
        return t.Length;
    }

    /// <summary>The ladder's thresholds, 1.1's (the rules file's default): rungs 0 to 4, the 0.9 as 1.1 has it.</summary>
    public static readonly float[] Thresholds = [0.69f, 0.39f, 0.19f, 0.9f, 0f];

    /// <summary>
    /// Health moved from <paramref name="was"/> to what the character has now: when the rung changed,
    /// **or health went down at all**, the penalties are laid again for the new rung (the tail of
    /// the damage and of the heal, and the wound update).
    /// </summary>
    public static void Changed(WorldThing thing, float was)
    {
        if (thing.Actor is not { } a) return;
        int before = Level(thing, was), now = Level(thing, a.Health);
        if (now != before || a.Health < was) Apply(thing, now, frenzied: false);
    }

    /// <summary>
    /// The penalties. Only for a player thing (type 7) — **or anybody at all in single
    /// player** (`* == 0`): in a network game, host or player's machine, anybody else
    /// is left as they are, their wound effects neither taken off nor laid (<see cref="Laid"/>). The three wound
    /// effects come off; then,
    /// unless the call is frenzy's (which strips and lays nothing):
    /// <list type="table">
    /// <item><term>1</term><description>`ef_dmg_dexterity` level 1</description></item>
    /// <item><term>2</term><description>dexterity 2, strength 2, and `ef_dmg_movespeed` 2 for 8 s</description></item>
    /// <item><term>3</term><description>dexterity 3, strength 3, movespeed 3 for 20 s (unreachable)</description></item>
    /// <item><term>4</term><description>dexterity 3, strength 3, **movespeed 4** for 50 s</description></item>
    /// </list>
    /// The movespeed effect is the limp: `ef_dmg_movespeed` is 0.8 at level 2 and lower after, and
    /// MOVESPEED at or under 0.8 is what sets `THING_AF2_LIMPING` (<see cref="EffectRuntime.Recompute"/>).
    /// </summary>
    public static void Apply(WorldThing thing, int level, bool frenzied)
    {
        if (thing.Actor is null || !Laid(thing)) return;
        EffectRuntime.Remove(thing, "ef_dmg_dexterity");
        EffectRuntime.Remove(thing, "ef_dmg_strength");
        EffectRuntime.Remove(thing, "ef_dmg_movespeed");
        if (frenzied || level is < 1 or > 4) return;
        // By rung, 1.1's: 1 dexterity 1; 2 dexterity 2, strength 2, movespeed 2 for 8 s; 3 the threes for 20 s; 4 dexterity 3,
        // strength 3, movespeed 4 for 50 s — the rules file's [Wounds] lists (a level of -1 lays nothing).
        var r = Rules.GameRules.Current.Wounds;
        int i = level - 1;
        if (r.WoundDexterityLevels[i] >= 0) EffectRuntime.Apply(thing, "ef_dmg_dexterity", r.WoundDexterityLevels[i]);
        if (r.WoundStrengthLevels[i] >= 0) EffectRuntime.Apply(thing, "ef_dmg_strength", r.WoundStrengthLevels[i]);
        if (r.WoundMoveSpeedLevels[i] >= 0) EffectRuntime.Apply(thing, "ef_dmg_movespeed", r.WoundMoveSpeedLevels[i], durationMs: r.WoundMoveSpeedMs[i]);
    }

    /// <summary>
    /// Whether the wound update lays anything on <paramref name="thing"/>: a player thing (type 7), or anybody
    /// outside a network game (`*`, here the mode of the thing's place, `Net.NetOrders.Mode`).
    /// </summary>
    public static bool Laid(WorldThing thing) =>
        thing.Type == ThingType.Player || Net.NetOrders.Mode(thing.Place) == Net.NetMode.Single;
}
