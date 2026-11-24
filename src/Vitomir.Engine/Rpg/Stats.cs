// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Writing a character's current stat, and what the write sets off. The stat write is
// the only door the current row goes through — the effect recompute calls it for every
// stat — and several whole systems hang off its switch: the frenzy check, the low-blood bark, a
// human bleeding to death from a drained vein, and the end of the game at Humanity 0.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>The current-stat write and its consequences.</summary>
public static class Stats
{
    /// <summary>
    /// `SetStat`: clamps into the character's bounds (<see cref="Actor.ClampStat"/>),
    /// writes, and then by stat —
    /// <list type="bullet">
    /// <item><b>Blood</b>: a <b>human</b> without `THING_AF_NOBLEED` whose blood reaches exactly 0
    /// is killed on the spot, by nobody, as hit kind 5. A vampire or ghoul whose blood
    /// falls to a fifth of its pool or below from above it says `vox_lowblood` — when it is free to
    /// speak and the game is in play.</item>
    /// <item><b>Humanity</b>: a character that tracks it (`THING_AF_HUMANITYTRACK`) reaching 0 stops
    /// what it is doing, a vampire or ghoul plays `humanitylost` and says `vox_lostallhumanity`, and
    /// the game ends — Humanity's end, not the story's (end text 3).</item>
    /// <item><b>Frenzy</b>: a human or a monster has none — it is forced to 0; a vampire or ghoul
    /// runs the frenzy check (<see cref="Frenzy.Watch"/>) with the value it had before.</item>
    /// </list>
    /// </summary>
    public static void Set(WorldThing thing, int index, float value)
    {
        if (thing.Actor is not { } a || index < 0 || index >= Actor.Stats) return;
        float was = a.Stat[index];
        a.Stat[index] = a.ClampStat(index, value);
        switch ((ActorStat)index)
        {
            case ActorStat.Blood:
                if (Rules.GameRules.Current.Healing.HumanBleedsOutAtZeroBlood
                    && a.Type == ActorType.Human && !a.Has(Actor.Flag.NoBleed) && a.Stat[index] == 0f)
                {
                    Damage.Kill(thing, null, HitKind.Drain, DamageType.Normal, thing.Place?.Milliseconds ?? 0,
                                motions: thing.Place?.Motions);
                    return;
                }
                float fifth = a.Stat[(int)ActorStat.BloodPool] * Rules.GameRules.Current.Frenzy.LowBloodBarkFraction;
                if (a.Stat[index] <= fifth && fifth < was
                    && a.Type is ActorType.Vampire or ActorType.Ghoul
                    && Audio.Noise.CanSpeak(thing) && InPlay(thing))
                    Audio.Noise.Foley(thing, Audio.FoleyEvent.VoxLowBlood);
                return;
            case ActorStat.Humanity:
                if (Rules.GameRules.Current.Frenzy.HumanityZeroEndsGame && a.Has(Actor.Flag.HumanityTrack) && a.Stat[index] == 0f)
                {
                    Actions.Cancel(thing, force: false);
                    if (a.Type is ActorType.Vampire or ActorType.Ghoul)
                    {
                        Actions.PlayAction(thing, MotionHumanityLost);
                        Audio.Noise.Foley(thing, Audio.FoleyEvent.VoxLostAllHumanity);
                    }
                    thing.Place?.Session?.Chronicle?.EndGame(Chronicle.EndHumanityLost);
                }
                return;
            case ActorStat.Frenzy:
                if (a.Type is not (ActorType.Vampire or ActorType.Ghoul))
                {
                    if (Rules.GameRules.Current.Frenzy.FrenzyCapActorTypes) a.Stat[index] = 0f;
                    return;
                }
                Frenzy.Watch(thing, was);
                return;
        }
    }

    /// <summary>A current stat plus something, through <see cref="Set"/>.</summary>
    public static void Add(WorldThing thing, int index, float delta)
    {
        if (thing.Actor is { } a && index >= 0 && index < Actor.Stats) Set(thing, index, a.Stat[index] + delta);
    }

    /// <summary>The fraction of the pool at or under which a vampire says `vox_lowblood`.</summary>
    public const float LowBloodFraction = 0.2f;

    /// <summary>`MOTION_HUMANITYLOST`, 235 (0xeb).</summary>
    public const int MotionHumanityLost = 235;

    /// <summary>
    /// Whether the game is being played rather than sitting in a menu or a load — the engine's
    /// the main sequence's state 3 or 4. A place that is being stepped is in play.
    /// </summary>
    static bool InPlay(WorldThing thing) => thing.Place is not null;
}
