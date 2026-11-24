// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Perception;

namespace Vitomir.Platform.Host;

/// <summary>
/// What a scenario's overrides put in a world beyond the modules' data (that is the loader's,
/// <see cref="SessionOptions.DataOverrides"/>): the light given over the time's (<see cref="SessionOptions.Light"/>,
/// <see cref="LightSchedule"/>), each fighter's own combat tempo (<see cref="CastFighter.Tempo"/>, <see cref="ActorTempo"/>) and its clips'
/// own play rates (<see cref="CastFighter.ClipRates"/>: a kind of its own, <see cref="WithClipRates"/>). Nothing of it is registered
/// or made when none is given, so every other world is unchanged.
/// </summary>
public sealed partial class Session
{
    /// <summary>Registers the light given and the fighters' own tempos' component (before the modules, which read it as they register).</summary>
    static void RegisterOverrides(WorldBuilder b, SessionOptions options, GameInstall? install)
    {
        if (options.Light is { Length: > 0 } light)
        {
            if (!LightSchedule.TryParse(light, out var schedule, out string problem)) { install?.Dispose(); throw new SessionException(problem); }
            LightSchedule.Register(b, schedule);
        }
        if (ArenaCast.TemposIn(options.Arena)) ActorTempos.Register(b);
    }

    /// <summary>Each fighter's own tempo given, once the cast is made (the arena's fighters in the cast's order).</summary>
    void GiveOverrides(ArenaCast cast)
    {
        for (int i = 0; i < cast.Fighters.Count && i < fighters.Count; i++)
            if (cast.Fighters[i].Tempo is { } tempo)
            {
                world.Set(fighters[i], new ActorTempo { Tempo = tempo });
                options.Say(string.Create(CultureInfo.InvariantCulture, $"arena: {cast.Fighters[i].Name} fights at its own combat tempo {tempo:0.###} (the global's place)"));
            }
    }

    /// <summary>
    /// The kinds of the cast's fighters that have clip rates, each registered as a kind of its own (<see cref="WithClipRates"/>) under
    /// <see cref="ArenaCast.KindKey(CastFighter)"/>, after its base kind; a rate naming no clip of the kind stops the load by name.
    /// </summary>
    static void RegisterClipRates(CharacterAssets assets, Dictionary<string, int> kinds, ArenaCast cast, Action<string> say)
    {
        foreach (var f in cast.Fighters)
        {
            if (f.ClipRates is not { Count: > 0 } rates) continue;
            string key = ArenaCast.KindKey(f);
            if (kinds.ContainsKey(key)) continue;
            if (!kinds.TryGetValue(ArenaCast.KindKey(f.Template, f.Weapon), out int baseKind)) continue;
            var made = WithClipRates(assets, assets.Kind(baseKind), rates, out var problems, out var changed);
            if (problems.Count > 0) throw new SessionException($"arena: fighter {f.Name}: {string.Join("; ", problems)}");
            kinds[key] = assets.AddKind(made);
            say($"arena: {f.Name}'s clips at their own rates: {string.Join(", ", changed)}");
        }
    }

    /// <summary>A clip's names as a clip rate may name it: its own (1.1's, <c>windup:left</c>, <c>stitched:overhead</c>).</summary>
    static string ClipName(CharacterAssets assets, int clip) => clip >= 0 && clip < assets.ClipCount ? assets.Clip(clip).Name : "";

    /// <summary>
    /// Whether a clip rate's name names a clip: its own name, or the part after its prefix (<c>chrisAttackSword</c> names the flattened
    /// <c>flat:chrisAttackSword</c> played in its place; <c>right</c> names <c>windup:right</c>).
    /// </summary>
    static bool Names(string given, string clip) =>
        clip.Length > 0 && (clip == given || clip.EndsWith(":" + given, StringComparison.Ordinal));

    /// <summary>The names of the clips a kind's actions play (what a clip rate may name), in order.</summary>
    public static IReadOnlyList<string> ClipNames(CharacterAssets assets, CharacterKind kind)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        if (kind.Actions is { } set)
            foreach (var m in set.Quick.Append(set.Strong).Append(set.Block).Append(set.Hurt).Append(set.DeathQuick).Append(set.DeathSlow).Concat(set.Stitched.Select(x => x.Motion)))
                if (m.Has && ClipName(assets, m.Clip) is { Length: > 0 } n) names.Add(n);
        return [.. names];
    }

    /// <summary>The action kinds a clip rate may name in place of a clip.</summary>
    public static readonly string[] ClipRateKinds = ["quick", "strong", "block", "hurt"];

    /// <summary>
    /// A copy of <paramref name="kind"/> whose action motions named by <paramref name="rates"/> play at their rate times the rate given,
    /// their busy time divided by it (the clip's <c>Strike</c> mark, its hold frame and its contact window are clip time, so they follow):
    /// by a clip's name (1.1's, a stitched or wind-up blow's), or by an action kind — <c>quick</c> (every quick and directional blow),
    /// <c>strong</c>, <c>block</c>, <c>hurt</c>. Several rates on one motion multiply. What each changed is said in
    /// <paramref name="changed"/>; a name that is none of the kind's is a problem with the kind's clips listed.
    /// </summary>
    public static CharacterKind WithClipRates(CharacterAssets assets, CharacterKind kind, IReadOnlyList<CastClipRate> rates, out List<string> problems, out List<string> changed)
    {
        problems = [];
        changed = [];
        var set = kind.Actions;
        if (set is null) { problems.Add($"{kind.Name} plays no actions, so it has no clip to give a rate"); return kind; }
        var quick = (ActionMotion[])set.Quick.Clone();
        var stitched = (StitchedMotion[])set.Stitched.Clone();
        ActionMotion strong = set.Strong, block = set.Block, hurt = set.Hurt, dq = set.DeathQuick, ds = set.DeathSlow;
        var names = ClipNames(assets, kind);
        var log = changed;
        // A hurt or a death with no busy time of its own is busy for its clip's length (the attacks' and the stagger's rule): that
        // length made explicit, so the rate scales it as it scales a blow's.
        ActionMotion Busy(ActionMotion m) => m.Has && m.BusySeconds <= 0f ? m with { BusySeconds = assets.Clip(m.Clip).Duration } : m;
        ActionMotion Scale(ActionMotion m, float rate, string why)
        {
            if (!m.Has) return m;
            log.Add(string.Create(CultureInfo.InvariantCulture, $"{ClipName(assets, m.Clip)} x{rate:0.###} ({why})"));
            return m with { Rate = m.Rate * rate, BusySeconds = m.BusySeconds / rate };
        }
        foreach (var r in rates)
        {
            int hits = 0;
            bool kindWord = ClipRateKinds.Contains(r.Clip, StringComparer.Ordinal);
            for (int i = 0; i < quick.Length; i++)
                if (r.Clip == "quick" || !kindWord && Names(r.Clip, ClipName(assets, quick[i].Clip))) { quick[i] = Scale(quick[i], r.Rate, r.Clip); hits++; }
            for (int i = 0; i < stitched.Length; i++)
                if (r.Clip == "quick" || !kindWord && Names(r.Clip, ClipName(assets, stitched[i].Motion.Clip))) { stitched[i] = stitched[i] with { Motion = Scale(stitched[i].Motion, r.Rate, r.Clip) }; hits++; }
            if (r.Clip == "strong" || !kindWord && strong.Has && Names(r.Clip, ClipName(assets, strong.Clip))) { strong = Scale(strong, r.Rate, r.Clip); hits++; }
            if (r.Clip == "block" || !kindWord && block.Has && Names(r.Clip, ClipName(assets, block.Clip))) { block = Scale(block, r.Rate, r.Clip); hits++; }
            if (r.Clip == "hurt" || !kindWord && hurt.Has && Names(r.Clip, ClipName(assets, hurt.Clip)))
            {
                hurt = Scale(Busy(hurt), r.Rate, r.Clip);
                hits++;
            }
            if (!kindWord && dq.Has && Names(r.Clip, ClipName(assets, dq.Clip))) { dq = Scale(Busy(dq), r.Rate, r.Clip); hits++; }
            if (!kindWord && ds.Has && Names(r.Clip, ClipName(assets, ds.Clip))) { ds = Scale(Busy(ds), r.Rate, r.Clip); hits++; }
            if (hits == 0)
                problems.Add($"clipRate \"{r.Clip}\": {kind.Name} has no such clip (its clips: {string.Join(", ", names)}; or an action kind: {string.Join(", ", ClipRateKinds)})");
        }
        var copy = new ActionSet
        {
            Quick = quick, Strong = strong, Block = block, Hurt = hurt, DeathQuick = dq, DeathSlow = ds,
            UpperBody = set.UpperBody, Spine = set.Spine, WeaponHand = set.WeaponHand, Weapon = set.Weapon, Carry = set.Carry,
            Stitched = stitched,
        };
        return new CharacterKind
        {
            Name = kind.Name, Model = kind.Model, Motions = kind.Motions, Head = kind.Head, Limbs = kind.Limbs, FacingYaw = kind.FacingYaw,
            Scale = kind.Scale, Actions = copy, Held = kind.Held,
        };
    }
}
