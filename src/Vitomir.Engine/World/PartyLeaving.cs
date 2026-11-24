// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A member leaving the party while selected (the party's RemovePlayer),
// the branch a comparison with 1.1 found missing; built.
using Vitomir.Engine.Rpg;

namespace Vitomir.Engine.World;

/// <summary>
/// **The selected member leaves the party** (the script native `RemoveFromParty` passing
/// 0 as its third argument). Nobody is selected, and the next member still standing is
/// looked for — which, finding nobody, has already asked for the party's
/// end (<see cref="Location.SteerNext"/>). **And then, still nobody selected, the
/// first member left in the party is revived** at a tenth of its health and blood, with the awakening
/// noise (revived with 0.1, 0.1, 0, 1), **and taken over** — so a script that
/// takes the one member standing out of a party whose others are all down gets up a fallen one to
/// carry on with. Vitomir ended the party and left the rest dead.
/// </summary>
public static class PartyLeaving
{
    /// <summary>The health and blood fraction the fallen member is revived at (0x3dcccccd, 0.1).</summary>
    public const float ReviveFraction = 0.1f;

    /// <summary>
    /// After <paramref name="gone"/> has left the party (its <c>PARTY</c> flag cleared): when it was
    /// the selected member, the next one standing takes over, and failing that the first member left
    /// is revived and takes over. Answers who is selected after.
    /// </summary>
    public static WorldThing? Left(Location here, WorldThing gone)
    {
        if (!ReferenceEquals(gone, here.Selected)) return here.Selected;
        if (here.SteerNext(skipDead: true)) return here.Selected;
        // Still nobody. The first member in the party's slots, whatever its state.
        if (here.PartyMembers().FirstOrDefault(m => !ReferenceEquals(m, gone)) is not { } first) return null;
        Damage.Revive(first, Rules.GameRules.Current.Death.PartyLeaverReviveFraction, Rules.GameRules.Current.Death.PartyLeaverReviveFraction, sound: true);            // revive(0.1, 0.1, 0, 1)
        here.Steer(first);
        return first;
    }
}
