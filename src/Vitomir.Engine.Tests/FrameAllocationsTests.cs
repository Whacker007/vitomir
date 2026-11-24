// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Diagnostics;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The allocation ledger (ours; <see cref="FrameAllocations"/>, what a
/// frame allocates): the instrument that found what made a full garbage collection a second on a walk
/// through the new Old Town. Pinned: without its environment variable there is no ledger at all, so the
/// game's loop pays nothing; with it, the bytes a phase allocated are counted to that phase and not to its
/// neighbours, frames before the settling count are left out, and the summary names the phases and the
/// collections.
/// </summary>
public class FrameAllocationsTests
{
    [Fact]
    public void Without_the_variable_there_is_no_ledger()
    {
        var was = Environment.GetEnvironmentVariable(FrameAllocations.Variable);
        try
        {
            Environment.SetEnvironmentVariable(FrameAllocations.Variable, null);
            Assert.Null(FrameAllocations.FromEnvironment(["step", "draw"]));
            Environment.SetEnvironmentVariable(FrameAllocations.Variable, "0");
            Assert.Null(FrameAllocations.FromEnvironment(["step", "draw"]));
        }
        finally { Environment.SetEnvironmentVariable(FrameAllocations.Variable, was); }
    }

    [Fact]
    public void A_phases_bytes_are_counted_to_that_phase_once_the_first_frames_have_settled()
    {
        var was = (Environment.GetEnvironmentVariable(FrameAllocations.Variable), Environment.GetEnvironmentVariable(FrameAllocations.SettleVariable));
        try
        {
            Environment.SetEnvironmentVariable(FrameAllocations.Variable, "1");
            Environment.SetEnvironmentVariable(FrameAllocations.SettleVariable, "2");
            using var ledger = FrameAllocations.FromEnvironment(["step", "draw"])!;
            var kept = new List<byte[]>();
            for (int frame = 1; frame <= 12; frame++)
            {
                ledger.Begin(frame);
                ledger.Mark(0);                              // the step allocates nothing
                kept.Add(new byte[20_000]);                  // the draw allocates twenty thousand bytes
                ledger.Mark(1);
                ledger.End();
            }
            string said = ledger.Summary("a made-up place");
            Assert.Contains("over 10 frames (after the first 2)", said);
            var lines = said.Split('\n');
            long Bytes(string phase) => long.Parse(lines.Single(l => l.TrimStart().StartsWith(phase + " ", StringComparison.Ordinal)).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1].Replace(",", ""));
            Assert.InRange(Bytes("draw"), 20_000, 20_200);
            Assert.InRange(Bytes("step"), 0, 100);
            Assert.InRange(Bytes("rest"), 0, 100);
            Assert.Contains("collections: generation 0", said);
            Assert.Equal(12, kept.Count);
        }
        finally
        {
            Environment.SetEnvironmentVariable(FrameAllocations.Variable, was.Item1);
            Environment.SetEnvironmentVariable(FrameAllocations.SettleVariable, was.Item2);
        }
    }
}
