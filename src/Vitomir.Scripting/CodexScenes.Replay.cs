// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The replay harness's view of the scripts: ours.
using System.Globalization;
using System.Text;
using Vitomir.Engine.Diagnostics;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>
    /// The scripts' field of a replay's digest (<see cref="SimDigest"/>; ours): how many scripts are live, and a
    /// hash of every timer pending — its moment, its thing, its id and its four arguments, to the bit — and
    /// of the timers a carried power keeps. The scripts' own variables are not in it: reading them is a walk
    /// of every script object's fields (what a save does), too dear for every frame.
    /// </summary>
    public string ReplayDigest()
    {
        var sb = new StringBuilder();
        foreach (var t in timers)
            sb.Append(Bits(t.At)).Append(':').Append(t.Guid).Append(':').Append(t.Id).Append(':')
              .Append(Bits(t.A)).Append(':').Append(Bits(t.B)).Append(':').Append(Bits(t.C)).Append(':').Append(Bits(t.D)).Append(';');
        foreach (var (t, left) in carriedTimers)
            sb.Append('c').Append(Bits(left)).Append(':').Append(t.Id).Append(';');
        return $"{live.Count.ToString(CultureInfo.InvariantCulture)}/{timers.Count.ToString(CultureInfo.InvariantCulture)}/{SimDigest.Hash(sb.ToString())}";
    }

    static string Bits(float v) => BitConverter.SingleToUInt32Bits(v).ToString("x8", CultureInfo.InvariantCulture);
}
