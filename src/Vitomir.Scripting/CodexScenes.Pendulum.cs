// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The repair `pendulumhome`: Stephansdom's swinging weight after a load.
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>The script of the weight that swings across the walkway in Stephansdom's first level (`Codex\SP\STE1_Pendulum.java`).</summary>
    public const string PendulumClass = "STE1_Pendulum";

    /// <summary>
    /// **The repair `pendulumhome`** (<see cref="Repairs.PendulumHome"/>, ours; in play the
    /// weight stopped across the walkway after its switch was pulled). The script swings the weight with
    /// `RotatePivot`, a turn **from wherever the thing is** (the thing's transform is copied as the
    /// swing's start), there and back for ever, and its switch lets it finish the swing home and stop — at
    /// the place the scene put it, up out of the way. But it saves one field, `bTrapActive`: which way the
    /// swing is going and where it is are forgotten, and a load's `beginscene` starts a fresh outward swing
    /// from wherever the save caught the weight. From then on "home" is that place — in the worst case the
    /// far end of the swing, across the walkway, where one stopped in play — and the way on is shut for good.
    /// 1.1 should do the same (a load runs `restore`, then `beginscene`; the thing's
    /// saved transform is the one caught mid-swing); it has not been watched in 1.1.
    ///
    /// So as the script is restored the weight is put back where the scene placed it, at rest, before
    /// `beginscene`: a trap still on then swings from home as on a first visit, and one already switched
    /// off rests at home — which also mends a save already taken with the weight parked in the way.
    /// </summary>
    void PendulumHome(WorldThing weight)
    {
        weight.StopMoving();
        weight.Orientation = weight.SpawnOrientation;
        if (weight.Place is { } place && weight.InWorld) place.Move(weight, weight.SpawnPosition);
        else weight.Position = weight.SpawnPosition;
    }
}
