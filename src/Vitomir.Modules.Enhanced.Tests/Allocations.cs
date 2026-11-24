// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>The collection the module's allocation tests run in: alone, never beside another test (as the platform's are).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AllocationCollection
{
    public const string Name = "module allocations";
}

/// <summary>
/// Measuring that the module's ticks allocate nothing (the platform's <c>Vitomir.Platform.Tests/AllocationTests.cs</c>
/// says why so). <para><c>FlurryTests</c>' and <c>SeveranceTests</c>' windows took 720 bytes in about one full run in
/// five, inside <c>enhanced.harm</c>, never alone, never in two windows running; with tiered compilation off too. What a
/// thread allocates is counted for the thread, and the runtime makes one-offs of its own on it now and then; the code under
/// test allocating per tick or per event allocates in every window. So the classes that measure run in a collection of their
/// own (<see cref="AllocationCollection"/>), and a window is measured up to twice (<see cref="None"/>): a test fails only
/// when both measures allocated.</para>
/// </summary>
internal static class Allocations
{
    /// <summary>How many times a window is measured.</summary>
    public const int Measures = 2;

    /// <summary>
    /// Runs <paramref name="window"/> up to <see cref="Measures"/> times (each the next window of the same world, or a fresh one),
    /// measuring the bytes the thread allocated in it, and fails only when every measure allocated.
    /// </summary>
    public static void None(string what, Action window)
    {
        var spent = new long[Measures];
        for (int i = 0; i < Measures; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            window();
            spent[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            if (spent[i] == 0) return;
        }
        Assert.Fail($"{what}: allocated in every measure ({string.Join(", ", spent)} bytes)");
    }
}
