// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;

namespace Vitomir.Engine.World;

/// <summary>
/// **Ours, the harness's: what became of a walk that was ordered** (<c>--walk-to</c>, <c>--click</c>).
/// The walker's own account of itself is two bits -- it gave up, or it is still walking -- and a walk
/// with neither set has covered its last leg. That is not the same as being where it was sent: a leg
/// is a direction and a length, so a walker that lost the ground on the way and went on
/// through the air, or along the storey underneath, covers its legs all the same and ends over or
/// under its goal. Earlier, the report measured only in plan and called that "arrived, 0 short".
///
/// So an ended walk is an arrival only when the walker **stands at its goal**: on the piece of ground
/// it was sent to, or within <see cref="AtTheGoal"/> of the destination in space, the destination
/// taken on that ground (its own height, not the height the order happened to name). Any other ended
/// walk is <see cref="Kind.OffItsGoal"/>, and the line says how far above or below the goal's ground
/// the walker is. It reads the walker and the map and changes nothing: no game rule is here.
/// </summary>
public static class WalkOutcome
{
    /// <summary>How far from its destination, in space, a walker on some other piece of ground still stands at its goal: half a metre (engine units).</summary>
    public const float AtTheGoal = 36f;

    /// <summary>What a walk came to.</summary>
    public enum Kind
    {
        /// <summary>It ended at its goal.</summary>
        Arrived,
        /// <summary>The walker gave up (<see cref="Player.GaveUp"/>).</summary>
        GaveUp,
        /// <summary>It was still walking when the time ran out.</summary>
        StillGoing,
        /// <summary>It ended, but not where it was sent: over or under its goal, or short of it on other ground.</summary>
        OffItsGoal,
    }

    /// <summary>A walk's outcome and the measurements the report prints.</summary>
    /// <param name="Short">How far the walker is from the destination in plan (engine units).</param>
    /// <param name="Above">How far above the goal's ground the walker stands; negative when below.</param>
    /// <param name="Node">The piece of ground the walker is on.</param>
    /// <param name="Sent">The piece of ground it was sent to.</param>
    public readonly record struct Result(Kind Kind, float Short, float Above, int Node, int Sent);

    /// <summary>The outcome of <paramref name="player"/>'s walk to <paramref name="destination"/> on node <paramref name="sent"/>.</summary>
    public static Result Of(Player player, WalkMap? map, Vector3 destination, int sent) =>
        Of(player.GaveUp, player.Walking, player.Position, player.Node, map, destination, sent);

    /// <summary>
    /// The outcome from the walker's state: whether it gave up or is still walking, where it is
    /// (<paramref name="at"/>, on node <paramref name="node"/>), and where it was sent
    /// (<paramref name="destination"/> on node <paramref name="sent"/>). The goal's height is the sent
    /// node's ground at the destination; the destination's own when the map has no such node.
    /// </summary>
    public static Result Of(bool gaveUp, bool walking, Vector3 at, int node, WalkMap? map, Vector3 destination, int sent)
    {
        var goal = map is not null && map.IsGround(sent) ? destination with { Z = map.Nodes[sent].Height(destination.X, destination.Y) } : destination;
        float inPlan = Bodies.Distance2D(at, goal), above = at.Z - goal.Z;
        var kind = gaveUp ? Kind.GaveUp
                 : walking ? Kind.StillGoing
                 : node == sent || Vector3.Distance(at, goal) <= AtTheGoal ? Kind.Arrived
                 : Kind.OffItsGoal;
        return new Result(kind, inPlan, above, node, sent);
    }

    /// <summary>
    /// The report's line. An arrival, a walker that gave up and one still going read as they always
    /// have -- <c>arrived after 14.5s, 2290 units from where it started, 0.4 short of the destination,
    /// on node 980</c> -- and a walk that ended off its goal reads <c>stopped off its goal after 20.0s,
    /// 2290 units from where it started, 0.0 short of the destination, 1656 below it, on node 490</c>.
    /// </summary>
    /// <param name="seconds">How long the walk was watched.</param>
    /// <param name="travelled">How far the walker is from where it started, in space (engine units).</param>
    public static string Line(Result r, float seconds, float travelled)
    {
        string what = r.Kind switch
        {
            Kind.GaveUp => "gave up",
            Kind.StillGoing => "still going",
            Kind.Arrived => "arrived",
            _ => "stopped off its goal",
        };
        string height = r.Kind == Kind.OffItsGoal
            ? string.Create(CultureInfo.InvariantCulture, $"{MathF.Abs(r.Above):0} {(r.Above < 0 ? "below" : "above")} it, ") : "";
        return string.Create(CultureInfo.InvariantCulture,
            $"{what} after {seconds:0.0}s, {travelled:0} units from where it started, {r.Short:0.#} short of the destination, {height}on node {r.Node}");
    }
}
