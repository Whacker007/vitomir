// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Which of the Fixes and quality-of-life switches a network game takes from the host (ours).
using Vitomir.Engine.Features;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// **Whose switch it is in a network game** — ours, since 1.1 has no switches:
///
///
/// > *"For things that are purely visual, I think the client's machine should govern that.
/// > Resolution, detail, advance graphics options, control mapping. Things that govern game
/// > functionality should be controlled at the server level."*
///
/// So every switch is one of two kinds. A **machine** switch changes only what one player sees or
/// how their own hands reach the game — a picture, an animation's rate, the camera's controls, the
/// keys — and each machine keeps its own. A **host** switch changes what the game does — where a
/// character may go, what an order does, whether a character can be steered from the keyboard at
/// all — and the host's is in force on every machine: it is sent in the welcome and again with every
/// journey, and a player's machine lays it over its own for as long as it is in that game.
///
/// The player's own settings are not lost under the host's: <see cref="Own"/> keeps them, the
/// Fixes page edits and writes those, and a change made on the page during someone else's game
/// comes back into force the moment the player is on their own again. The wider design for Fixes and
/// modifications across single and multiplayer is to come once the base game is faithful with
/// multiplayer.
/// </summary>
public static class HostSwitches
{
    /// <summary>Who governs a switch in a network game.</summary>
    public enum Governor
    {
        /// <summary>Each machine its own: what one player sees, or how their own controls reach the game.</summary>
        Machine,
        /// <summary>The host's for everyone: what the game does.</summary>
        Host,
    }

    /// <summary>One switch as the host has it.</summary>
    public readonly record struct Setting(string Register, string Name, bool On);

    /// <summary>
    /// Who governs each switch, by register (<c>fixes</c> or <c>qol</c>) and name: **what its declaration
    /// says** (<see cref="Feature.Whose"/>). A declaration that does not say is the
    /// host's, and so is a name the registry does not hold — a new switch that changes the game is safe by
    /// default, and one that is purely one machine's has to be declared so. The registry's own check keeps
    /// the two honest: a switch declared a machine's must come out presentation, taking no rules switch
    /// point and changing nothing the rules read (<c>FeatureDeclarationTests</c>).
    /// </summary>
    public static Governor Of(string register, string name) =>
        GameFeatures.Registry.Find(register, name) is { Whose: FeatureWhose.Machine } ? Governor.Machine : Governor.Host;

    /// <summary>The host's switches that every machine in its game takes, in a fixed order.</summary>
    public static IReadOnlyList<Setting> Taken(Fixes fixes, Qol qol) =>
    [
        .. fixes.All.Where(f => Of("fixes", f.Name) == Governor.Host).Select(f => new Setting("fixes", f.Name, f.On)),
        .. qol.All.Where(q => Of("qol", q.Name) == Governor.Host).Select(q => new Setting("qol", q.Name, q.On)),
    ];

    /// <summary>Writes the host's switches after a welcome's or a journey's other fields.</summary>
    public static void Write(NetWriter w, Fixes fixes, Qol qol)
    {
        var taken = Taken(fixes, qol);
        w.U8((byte)taken.Count);
        foreach (var s in taken) w.Str(s.Register).Str(s.Name).Bool(s.On);
    }

    /// <summary>Reads what <see cref="Write"/> wrote.</summary>
    public static IReadOnlyList<Setting> Read(NetReader r)
    {
        int n = r.U8();
        var list = new List<Setting>(n);
        for (int i = 0; i < n; i++) list.Add(new Setting(r.Str(), r.Str(), r.Bool()));
        return list;
    }

    /// <summary>
    /// Lays the host's switches over a pair of registers, and only the host's: a name this machine
    /// does not know — the registry holds no such switch, so <c>all</c> is no name here, whatever a host
    /// sends — or one it counts as its own, is left alone. Then the pair is settled
    /// (<see cref="FeatureRegistry.Settle(Fixes, Qol, Action{string}?, Func{Feature, bool}?)"/>): one of this
    /// machine's own switches that the registry refuses beside what the host laid over it goes off for as
    /// long as the host's are in force (<see cref="Own"/> keeps the player's).
    /// </summary>
    public static void Apply(IReadOnlyList<Setting> fromHost, Fixes fixes, Qol qol)
    {
        foreach (var s in fromHost)
        {
            if (GameFeatures.Registry.Find(s.Register, s.Name) is not { Switchable: true, Whose: FeatureWhose.Host } feature) continue;
            FeatureRegistry.Put(fixes, qol, feature, s.On);
        }
        GameFeatures.Registry.Settle(fixes, qol, held: f => f.Whose == FeatureWhose.Host);
    }

    /// <summary>What the host sent last, while this machine is a player in its game; null otherwise.</summary>
    public static IReadOnlyList<Setting>? FromHost { get; private set; }

    /// <summary>The player's own switches, kept while the host's are in force; null otherwise.</summary>
    public static (Fixes Fixes, Qol Qol)? Own { get; private set; }

    /// <summary>
    /// A player's machine takes the host's switches: its own are kept aside (the first time) and the
    /// host's laid over <see cref="Fixes.Current"/> and <see cref="Qol.Current"/>. **Process-wide**,
    /// like <see cref="Fixes.Configure"/>: the game calls it, a test uses <see cref="Apply"/>.
    /// </summary>
    public static void Install(IReadOnlyList<Setting> fromHost)
    {
        Own ??= (Copy(Fixes.Current), Copy(Qol.Current));
        FromHost = fromHost;
        Apply(fromHost, Fixes.Current, Qol.Current);
    }

    /// <summary>
    /// The player changed their own switches (the Fixes page): they become the player's own, and in
    /// someone else's game the host's are laid over them again at once.
    /// </summary>
    public static void Changed()
    {
        if (FromHost is null) return;
        Own = (Copy(Fixes.Current), Copy(Qol.Current));
        Apply(FromHost, Fixes.Current, Qol.Current);
    }

    /// <summary>The player's machine has left the game: their own switches are theirs again.</summary>
    public static void Leave()
    {
        if (Own is { } own)
        {
            foreach (var f in own.Fixes.All) Fixes.Current[f.Name] = f.On;
            foreach (var q in own.Qol.All) Qol.Current[q.Name] = q.On;
        }
        Own = null;
        FromHost = null;
    }

    static Fixes Copy(Fixes from)
    {
        var to = new Fixes();
        foreach (var f in from.All) to[f.Name] = f.On;
        return to;
    }

    static Qol Copy(Qol from)
    {
        var to = new Qol();
        foreach (var q in from.All) to[q.Name] = q.On;
        return to;
    }
}
