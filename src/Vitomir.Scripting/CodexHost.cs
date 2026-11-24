// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Where the game's scripts meet the engine.
//
// All 447 former natives arrive here as EngineBridge.call(key, self, args). A key with no
// implementation logs once, returns a harmless default and lets the scene carry on: with 447 of
// them and an engine still being built, throwing would stop every scene for years. --strict-scripts
// turns that into a throw for when the silence is the problem.
//
// This subclasses java.util.Dictionary because the scripts are compiled at run time and so cannot
// see .NET types; a JDK class is the only seam they can call through.
#nullable disable
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;

namespace Vitomir.Scripting;

/// <param name="Self">The script object the call came from, or null for a static.</param>
public delegate object CodexCall(object Self, object[] Args);

public class CodexHost : java.util.Dictionary
{
    Dictionary<string, CodexCall> handlers = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, int> missing = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, int> counts = new(StringComparer.Ordinal);

    /// <summary>Throw on an unimplemented native instead of logging once and carrying on.</summary>
    public bool Strict { get; set; }
    /// <summary>
    /// Where a script's complaints go. By default the engine's log, under Vitomir.Scripting: a
    /// native nothing implements is Debug (there are hundreds and they are expected), a script that
    /// actually failed is Warning. A caller may replace this to collect them instead.
    /// </summary>
    public Action<string> Log { get; set; } = m => Logger.LogWarning("{Message}", m);

    /// <summary>Notices about the parts of the API that are not built yet, which are not problems.</summary>
    public Action<string> LogMissing { get; set; } = m => Logger.LogDebug("{Message}", m);

    static readonly ILogger Logger = Diag.For("Vitomir.Scripting");
    /// <summary>Every call, for the tests and the script console; off by default.</summary>
    public List<string> Trace { get; set; }

    public int Calls { get; private set; }
    /// <summary>How many times each key was called, implemented or not.</summary>
    public IReadOnlyDictionary<string, int> CountByKey => counts;
    /// <summary>The keys the scripts asked for that nothing implements yet, and how often.</summary>
    public IReadOnlyDictionary<string, int> Missing => missing;

    /// <summary>Implements one native, e.g. <c>Codex.GetChronicleScript</c>.</summary>
    public void On(string key, CodexCall handler) => handlers[key] = handler;

    /// <summary>Implements a native that returns nothing.</summary>
    public void On(string key, Action<object, object[]> handler) =>
        handlers[key] = (self, args) => { handler(self, args); return null; };

    /// <summary>True when something implements this key.</summary>
    public bool Implements(string key) => handlers.ContainsKey(key);

    /// <summary>
    /// **Ours, for one process playing several machines (the tests)**: the scripts call one host (`EngineBridge.host`),
    /// and each <see cref="CodexScenes"/> registers its natives on it, the last over the first. <see cref="Branch"/>
    /// keeps the natives registered so far and starts a copy for the next machine's to go on; <see cref="Use"/> puts a
    /// machine's back before its turn. A game has one machine to a process and never calls either.
    /// </summary>
    public object Natives => handlers;

    /// <summary>A copy of the natives as they stand, taking the registrations that follow; the old ones kept as they were (<see cref="Natives"/>).</summary>
    public object Branch() => handlers = new(handlers, StringComparer.Ordinal);

    /// <summary>The natives <paramref name="natives"/> (<see cref="Natives"/>, <see cref="Branch"/>) answer from now on.</summary>
    public void Use(object natives) => handlers = (Dictionary<string, CodexCall>)natives;

    /// <summary>What implements this key now, or null — for a wrapper that adds to a native rather than replacing it (a host's network arms).</summary>
    public CodexCall Handler(string key) => handlers.GetValueOrDefault(key);

    /// <summary>
    /// While set (<see cref="CodexScenes.Judge"/>), asked of every native a script calls: true lets it answer as ever; false
    /// answers it with nothing (the type's default) and does not carry it out.
    /// </summary>
    public Func<string, bool> Judging { get; set; }

    /// <summary>Called by the scripts: <c>EngineBridge.host.get(new Object[]{ key, self, args })</c>.</summary>
    public sealed override object get(object request)
    {
        var r = (object[])request;
        var key = (string)r[0];
        var self = r[1];
        var args = (object[])r[2] ?? [];
        if (Judging is { } judge && !judge(key)) return null;
        Calls++;
        counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
        Trace?.Add(Describe(key, args));
        if (handlers.TryGetValue(key, out var handler)) return handler(self, args);
        if (Strict) throw new NotImplementedException($"the engine has no {key}; the script called {Describe(key, args)}");
        if (missing.TryAdd(key, 1)) LogMissing($"script called {key}, which the engine does not implement yet");
        else missing[key]++;
        return null;                                     // the bridge turns null into the type's default
    }

    static string Describe(string key, object[] args) =>
        $"{key}({string.Join(", ", args.Select(a => a?.ToString() ?? "null"))})";

    // Boxing helpers, so a handler reads as the signature the script sees.
    public static int Int(object[] a, int i) => a.Length > i && a[i] is java.lang.Integer v ? v.intValue() : 0;
    public static float Float(object[] a, int i) => a.Length > i && a[i] is java.lang.Float v ? v.floatValue() : 0f;
    public static bool Bool(object[] a, int i) => a.Length > i && a[i] is java.lang.Boolean v && v.booleanValue();
    public static string Str(object[] a, int i) => a.Length > i ? a[i]?.ToString() : null;
    public static object Box(int v) => java.lang.Integer.valueOf(v);
    public static object Box(float v) => java.lang.Float.valueOf(v);
    public static object Box(bool v) => java.lang.Boolean.valueOf(v);

    // java.util.Dictionary's other members: the bridge never uses them.
    public override int size() => 0;
    public override bool isEmpty() => true;
    public override java.util.Enumeration keys() => java.util.Collections.emptyEnumeration();
    public override java.util.Enumeration elements() => java.util.Collections.emptyEnumeration();
    public override object put(object key, object value) => null;
    public override object remove(object key) => null;
}
