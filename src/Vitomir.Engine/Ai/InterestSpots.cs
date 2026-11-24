// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Where the people of a place are willing to go.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ai;

/// <summary>One place a character may walk to, and the ground under it.</summary>
/// <param name="Spot">The scene's own record.</param>
/// <param name="Node">The walk node the spot stands on, or −1 when it stands on nothing walkable.</param>
public readonly record struct InterestSpot(NsdInterestSpot Spot, int Node)
{
    public Vector3 Position => Spot.Position;
    /// <summary>Which group it belongs to — what a character's <c>interestNum</c> is matched against.</summary>
    public uint Group => Spot.Group;
}

/// <summary>
/// The scene's interest spots, indexed the way the engine indexes them.
///
/// A character takes a *run* of them, not a set: the engine walks the scene's spots in file order
/// looking for the first whose group is its <c>interestNum</c>, remembers that index, and then
/// counts forward while the group keeps matching. So the spots of one group have to be contiguous
/// in the file for a character to see all of them, and a second run of the same group later in the
/// file is invisible. That is reproduced here rather than corrected.
/// </summary>
public sealed class InterestSpots
{
    /// <summary>How far above and below a spot the ground is looked for.</summary>
    public const float Reach = WalkMap.Reach;

    public InterestSpot[] All { get; }

    public InterestSpots(NsdScene? scene, WalkMap? walk)
    {
        var spots = scene?.InterestSpots ?? [];
        All = new InterestSpot[spots.Length];
        for (int i = 0; i < spots.Length; i++)
            All[i] = new InterestSpot(spots[i], walk?.Find(spots[i].Position, Reach) ?? -1);
    }

    /// <summary>The run of spots a character with this <c>interestNum</c> may use.</summary>
    /// <remarks>
    /// A negative <c>interestNum</c> — which is the default when the file does not set one — takes
    /// nothing, because the engine checks <c>-1 &lt; interestNum</c> before it looks.
    /// </remarks>
    public (int First, int Count) Run(int interestNum)
    {
        if (interestNum < 0 || All.Length == 0) return (0, 0);
        int first = 0;
        while (first < All.Length && All[first].Group != (uint)interestNum) first++;
        if (first >= All.Length) return (0, 0);
        int count = 0;
        while (first + count < All.Length && All[first + count].Group == (uint)interestNum) count++;
        return (first, count);
    }

    /// <summary>How many groups the scene has, and how many spots are in a second run of one.</summary>
    public int Groups => All.Select(s => s.Group).Distinct().Count();

    /// <summary>
    /// How many spots sit in a repeat run — a group that appears again after another group has
    /// come between. Those are the ones no character can reach, by the rule above.
    /// </summary>
    public int Stranded
    {
        get
        {
            var seen = new HashSet<uint>();
            int stranded = 0;
            for (int i = 0; i < All.Length; i++)
            {
                bool starts = i == 0 || All[i - 1].Group != All[i].Group;
                if (starts && !seen.Add(All[i].Group)) { for (int j = i; j < All.Length && All[j].Group == All[i].Group; j++) stranded++; }
            }
            return stranded;
        }
    }
}
