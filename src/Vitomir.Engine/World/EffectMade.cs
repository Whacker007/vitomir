// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What an effect's script made (ours): the record a save needs to make an effect's looks again after a load,
// kept as the script runs. The repair `loadedeffects`; the save's half is `Save/LoadedEffects.cs`.
using Vitomir.Engine.Rpg;

namespace Vitomir.Engine.World;

/// <summary>
/// **Knowing what an effect's script made** (ours; 1.1 keeps no such record — the script holds a pad's GUID and
/// nothing holds the script's effect). A discipline's effect script makes its looks as the effect starts: it
/// spawns a pad or a trail and attaches it to the character, and sets a shell on the character's model. None of
/// it is in a save — the things are flagged <c>SAVE NEVER</c> in 1.1's data, and a model's shell is not written
/// — and a load does not tell a restored effect's script <c>effectstarted</c>, so after a
/// load the effect is on and its looks are gone.
///
/// A stretch is opened round every call into an effect's script the engine makes for the effect itself — its
/// <c>effectstarted</c>, its <c>effectended</c> and its timers
/// — beside <see cref="Net.MadeHere.ByEffect"/>'s, whose work for a network game it leaves alone.
/// As it ends, every thing made meanwhile that no save keeps is marked with the effect's GUID
/// (<see cref="WorldThing.MadeByEffect"/>); and a shell a script sets while one is open is marked the same way
/// on its model (<see cref="Current"/>, read by the <c>SetShell</c> native; <see cref="ModelFx.ShellEffect"/>).
/// **Nothing here changes what the game does**: the marks are read by a save (<c>Save.LoadedEffects</c>) and by
/// nothing else.
/// </summary>
public static class EffectMade
{
    [ThreadStatic] static int current;

    /// <summary>The GUID of the effect whose script is being called now, or 0 when none is.</summary>
    public static int Current => current;

    /// <summary>
    /// A stretch of <paramref name="effect"/>'s script, on the character it is on. What it makes in
    /// <paramref name="on"/>'s place that no save keeps is marked the effect's as the stretch ends. A stretch
    /// inside another (a script that puts a second effect on) marks its own things first, and the outer one
    /// leaves those alone.
    /// </summary>
    public static Stretch By(WorldThing on, Effect effect) => new(on.Place, effect.Guid);

    /// <summary>The stretch <see cref="By"/> opens: the place's GUID counter as it began, and whose stretch it was inside.</summary>
    public readonly struct Stretch : IDisposable
    {
        readonly Location? place;
        readonly int effect, from, outer;

        internal Stretch(Location? place, int effect)
        {
            this.place = place;
            this.effect = effect;
            from = place?.NextGuid ?? 0;
            outer = current;
            if (effect != 0) current = effect;
        }

        public void Dispose()
        {
            if (effect == 0) return;                                             // no stretch: a timer that is no effect's
            current = outer;
            if (place is null) return;
            // The counter hands numbers out in order: what was made meanwhile has one at or past
            // where it stood (as `Net.MadeHere.Making` reads it).
            foreach (var t in place.Things)
                if (t.MadeByEffect == 0 && ThingGuids.Raw(t.Guid) >= from && (t.Flags & WorldThing.Flag.SaveNever) != 0)
                    t.MadeByEffect = effect;
        }
    }
}
