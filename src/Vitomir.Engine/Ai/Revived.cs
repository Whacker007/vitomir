// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A revived character's mind starts over (from `rpgActor::Revive`).
namespace Vitomir.Engine.Ai;

public sealed partial class Minds
{
    /// <summary>
    /// **A revive resets the mind** (called by the actor's revive for any
    /// actor with a mind, dead or not, after its effects have come off and its body
    /// is back up). In 1.1's order:
    /// <list type="bullet">
    /// <item>the AI flags are cleared but for "has noticed" (0x2, kept here as
    /// <see cref="Mind.HasNoticed"/>, which stays) — so the pet mark, the possessed mark and the
    /// behead's "unaffected" all go;</item>
    /// <item>the mode is set straight back to the home mode (a plain
    /// write, not the mode change);</item>
    /// <item>the target is let go (<c>SetTarget(0, 0)</c> — which calls off the character's
    /// own pets too);</item>
    /// <item>the approach's give-up time is cleared;</item>
    /// <item>and it thinks at once (its next think set to now).</item>
    /// </list>
    /// So a townsman `Pedestrian` gets up at the start of a scene wandering as he was placed, not
    /// still running from — or still after — whoever killed him; and a corpse raised by Vigor Mortis,
    /// Shambling Hordes or Awaken comes up in its own home mode before the discipline's own effect
    /// makes it anything else.
    /// </summary>
    /// <remarks>
    /// A revived pet has lost its mark but keeps its master and its slot in his list;
    /// the list's release (<see cref="ReleasePets"/>) then passes it over, as 1.1's does.
    /// </remarks>
    public void Revived(Mind mind)
    {
        mind.Flags &= Noticed;                                   // only the noticed bit kept
        mind.Enter(mind.BaseMode, andBase: false);
        SetTarget(mind, null);
        mind.ApproachUntil = 0;
        mind.NextThink = Now;
    }

    /// <summary>The one AI flag a revive keeps, "has noticed" — <see cref="Mind.HasNoticed"/>.</summary>
    const uint Noticed = 0x2;
}
