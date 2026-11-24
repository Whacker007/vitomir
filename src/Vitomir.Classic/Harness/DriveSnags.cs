// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.World;

namespace Vitomir;

/// <summary>
/// **Ours**, a diagnostic: where the keys pushed the driven character and he did not go. In play
/// there were a few points where Christof got caught on places where he
/// should not, hard to remember afterwards, so the log remembers. A push held for <see cref="After"/>
/// seconds that moves him less than <see cref="Still"/> units writes one line with the place, the
/// spot, his sector and the way he was pushing; the next is not written until he has gone
/// <see cref="Apart"/> units from it. A wall writes one too: the line says where, not why, and the
/// spots that are not walls are the ones to look at.
/// </summary>
sealed class DriveSnags
{
    public const float After = 0.4f, Still = 2f, Apart = 48f;

    static readonly ILogger log = Diag.For("Vitomir.Driving");
    Vector3 anchor, last = new(float.NaN);
    float held;

    /// <summary>One frame of a push: where he was before it and where he is after.</summary>
    public void Pushed(Location place, WorldThing who, Vector3 before, Vector2 push, float seconds)
    {
        if (held == 0f) anchor = before;
        held += seconds;
        if (Vector3.Distance(who.Position, anchor) > Still) { held = 0f; return; }
        if (held < After) return;
        held = 0f;
        if (!float.IsNaN(last.X) && Vector3.Distance(who.Position, last) < Apart) return;
        last = who.Position;
        float heading = MathF.Atan2(push.Y, push.X) * 180f / MathF.PI;
        log.LogInformation("Driving: {Who} is pushed and does not move in {Place} at {X:0},{Y:0},{Z:0} (sector {Sector}), pushing toward {Heading:0} degrees",
            Watch.Name(who), place.Name, who.Position.X, who.Position.Y, who.Position.Z, who.Sector, heading);
    }

    /// <summary>The keys let go, or something else moved him: the count starts again.</summary>
    public void Released() => held = 0f;
}
