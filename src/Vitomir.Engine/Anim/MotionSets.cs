// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.Anim;

/// <summary>
/// The motion sets (<c>3D/Motions</c>) and animations (<c>3D/Anims</c>) a location needs, read once
/// each and kept. A template names its set in tag 9 (MOTIONSET); the set says which animation the
/// character plays for each thing it can be doing.
///
/// The engine drops a mode whose animation will not load, so a mode only
/// counts as playable once its <c>.nad</c> is actually there — which is what <see cref="Find"/>
/// decides, and why nine modes in the shipped sets never play. The drop happens while the set is
/// being read, so what the engine searches later is the surviving list in file order: that is what
/// <see cref="Playable"/> holds, and what the variant roll below counts through.
/// </summary>
public sealed class MotionSets(GameFiles files, int seed = 1)
{
    readonly Dictionary<string, NmsMotionSet?> sets = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, NadAnimation?> animations = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Per set, the modes that can actually play, in file order, and the animation each runs.</summary>
    readonly Dictionary<NmsMotionSet, (List<NmsMode> Modes, List<NadAnimation> Animations)> playable = [];

    /// <summary>
    /// The engine's <c>rand()</c>. In the original this is one global sequence shared with
    /// everything else that rolls; here each location's animation layer has its own, seeded, so a
    /// run repeats and a test can say which variant a character will swing with.
    /// </summary>
    readonly Rng rng = Chance.Game?.Motions ?? new(seed);   // a game's is the launch's, carried on

    /// <summary>One <c>rand() × (1/32767)</c>, the engine's own 0..1 (inclusive at both ends).</summary>
    public float Roll() => rng.Unit();

    /// <summary>How many sets and animations were asked for and not found.</summary>
    public int MissingSets { get; private set; }
    public int MissingAnimations { get; private set; }

    /// <summary>The named motion set, or null when the install has not got it.</summary>
    public NmsMotionSet? Set(string name)
    {
        if (name.Length == 0) return null;
        if (sets.TryGetValue(name, out var set)) return set;
        string file = name.Contains('.') ? name : name + ".nms";
        set = files.TryRead("3d/Motions/" + file, out var data) ? NmsMotionSet.Read(data, file) : null;
        if (set is null) { MissingSets++; Diag.For<MotionSets>().LogDebug("motion set {Name} not found", name); }
        else Load(set);
        return sets[name] = set;
    }

    /// <summary>One animation from <c>3D/Anims</c>, or null when the install has not got it.</summary>
    public NadAnimation? Animation(string file)
    {
        if (file.Length == 0) return null;
        if (animations.TryGetValue(file, out var a)) return a;
        string name = file.Contains('.') ? file : file + ".nad";
        try
        {
            a = files.TryRead("3d/Anims/" + name, out var data) ? NadAnimation.Read(data, name) : null;
        }
        catch (NodFormatException e)
        {
            a = null;
            Diag.For<MotionSets>().LogWarning("animation {Name} will not read: {Message}", name, e.Message);
        }
        if (a is null) { MissingAnimations++; Diag.For<MotionSets>().LogDebug("animation {Name} not found", name); }
        return animations[file] = a;
    }

    /// <summary>
    /// Every animation a set names, read when the set is — which is when the engine reads them, and
    /// why it can drop a mode whose file will not load. Nine modes across
    /// five of the shipped sets go this way.
    ///
    /// An animation named twice is read once and shared, as the engine shares it (its track cache is
    /// keyed by name), which matters for the same-animation check in
    /// <see cref="ThingAnimation"/>: two mode lines naming the same file are the same track.
    /// </summary>
    void Load(NmsMotionSet set)
    {
        List<NmsMode> modes = [];
        List<NadAnimation> anims = [];
        foreach (var mode in set.Modes)
            if (Animation(mode.File) is { } a) { modes.Add(mode); anims.Add(a); }
        playable[set] = (modes, anims);
        int dropped = set.Modes.Count - modes.Count;
        if (dropped > 0)
            Diag.For<MotionSets>().LogDebug("motion set {Name}: {Dropped} of {Total} modes have no animation",
                                            set.Name, dropped, set.Modes.Count);
    }

    /// <summary>
    /// A set's mode and its animation, or null when the set has no such mode or the animation is
    /// missing — the two cases in which the engine has nothing to play either.
    ///
    /// When a set lists the same mode more than once, this is where the engine picks: see
    /// <see cref="Choose"/>. <paramref name="special"/> asks for the mode's <c>special</c> variant
    /// instead, which is how a weapon reaches its special attack.
    /// </summary>
    public (NmsMode Mode, NadAnimation Animation)? Find(NmsMotionSet? set, string mode, bool special = false)
    {
        if (set?.Mode(mode) is not { } named) return null;
        return At(set, Choose(set, named.Kind, named.Index, special));
    }

    /// <summary>The same, by the mode number a script asks with.</summary>
    public (NmsMode Mode, NadAnimation Animation)? Find(NmsMotionSet? set, int mode, bool special = false)
    {
        if (set?.Mode(mode) is not { } named) return null;
        return At(set, Choose(set, named.Kind, named.Index, special));
    }

    /// <summary>
    /// A set's playable modes, loading it on demand.
    ///
    /// **A set this instance has not loaded is loaded now rather than answered "no such mode".**
    /// The list is keyed by the set *object*, so a set that came from another <c>MotionSets</c> — a
    /// test that made its own, a caller holding one across a location change — used to fall out
    /// silently, and the caller could not tell that from a set that genuinely has not got the mode.
    /// It cost half an hour once; it will not cost it twice.
    /// </summary>
    (List<NmsMode> Modes, List<NadAnimation> Animations) Loaded(NmsMotionSet set)
    {
        if (!playable.TryGetValue(set, out var list)) { Load(set); list = playable[set]; }
        return list;
    }

    (NmsMode Mode, NadAnimation Animation)? At(NmsMotionSet set, int position)
    {
        if (position < 0) return null;
        var list = Loaded(set);
        return position >= list.Modes.Count ? null : (list.Modes[position], list.Animations[position]);
    }

    /// <summary>
    /// Which of a mode's entries the engine plays. A set may list the same
    /// mode more than once — Christof has three <c>claw</c> lines and 142 (set, mode) pairs in the
    /// game are doubled or tripled — and this is what gives a character more than one swing.
    ///
    /// <list type="number">
    /// <item>With <paramref name="special"/> set, the whole list is scanned for an entry of that mode
    /// carrying <see cref="NmsFlags.Special"/>, and that one wins outright. 113 modes carry it and
    /// every one is an attack: it is the variant a weapon's own "special" flag asks for, never one
    /// the roll can reach.</item>
    /// <item>Otherwise the first entry of that mode is found, and the run of **consecutive** entries
    /// from there with the same mode and without <c>special</c> is counted. A run of one is the
    /// answer; longer and the engine rolls
    /// <c>first + (int)(rand() × (1/32767) × run)</c>.</item>
    /// </list>
    ///
    /// **The maximum roll lands one past the run.** <c>rand()</c> reaching 32767 makes the
    /// multiplier exactly 1, so the pick is <c>first + run</c> — the entry *after* the variants,
    /// which is a different mode, and past the end of the array when the run finishes the file. The
    /// engine's clamp lets that through (it rejects only <c>&gt; first + run</c>). Vitomir plays the
    /// next entry when there is one, which is the observable half, and falls back to the first when
    /// there is not, because reading past the array is a code bug rather than a behaviour. One roll
    /// in 32,768.
    /// </summary>
    public int Choose(NmsMotionSet set, NmsModeKind kind, int index, bool special = false)
    {
        var modes = Loaded(set).Modes;
        bool Is(int i) => modes[i].Kind == kind && modes[i].Index == index;

        if (special)
            for (int i = 0; i < modes.Count; i++)
                if (Is(i) && modes[i].Has(NmsFlags.Special)) return i;

        int first = -1;
        for (int i = 0; i < modes.Count; i++)
            if (Is(i)) { first = i; break; }
        if (first < 0) return -1;

        int run = 0;
        for (int i = first; i < modes.Count && Is(i) && !modes[i].Has(NmsFlags.Special); i++) run++;
        if (run <= 1) return first;

        int pick = first + (int)(Roll() * run);
        if (pick > first + run) return first;                   // the engine's own clamp
        return pick < modes.Count ? pick : first;               // the maximum roll, off the end
    }

    /// <summary>
    /// The ground one loop of an animation is drawn as covering, measured once per model and kept
    /// (<see cref="Anim.Stride"/>). Null when it cannot be measured. Only the <c>matchedgait</c> fix
    /// asks for it, so a faithful run never pays for the posing.
    /// </summary>
    public float? StrideOf(NodModel model, NmsMotionSet set, NadAnimation animation)
    {
        var key = (model, animation);
        if (strides.TryGetValue(key, out var cached)) return cached;
        return strides[key] = Stride.Measure(model, set, animation);
    }

    readonly Dictionary<(NodModel, NadAnimation), float?> strides = [];

    /// <summary>How many of a set's modes can actually play.</summary>
    public int Playable(NmsMotionSet set) => Loaded(set).Modes.Count;
}
