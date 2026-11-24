// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A set of switches being edited under the registry's rules: what the Fixes page's boxes stand on (ours).
namespace Vitomir.Engine.Features;

/// <summary>
/// **The switches being edited** (ours): which features are on, held to what the registry allows one tick at a
/// time. The Fixes page's boxes are a picture of this (<see cref="Ui.FixesPage"/>); it knows nothing of panes, so a
/// test can press it with invented features.
///
/// - A tick the registry refuses does not take, and the reason comes back (<see cref="Tick"/>).
/// - A feature that <see cref="Feature.Refines">refines</see> another is **locked** while that one is off
///   (<see cref="LockedUnder"/>): it cannot be ticked, and the page greys it.
/// - Unticking a feature unticks every feature that needed it, and says which (<see cref="Ticked.AlsoOff"/>).
/// </summary>
public sealed class FeatureTicks(FeatureRegistry registry)
{
    readonly HashSet<Feature> on = [];

    public FeatureRegistry Registry => registry;

    /// <summary>Whether a feature is on. One with no switch always is.</summary>
    public bool On(Feature feature) => !feature.Switchable || on.Contains(feature);

    /// <summary>The switchable features that are on, in the registry's order.</summary>
    public IEnumerable<Feature> AllOn => registry.All.Where(f => f.Switchable && on.Contains(f));

    /// <summary>
    /// Starts from <paramref name="features"/> made allowed (<see cref="FeatureRegistry.Settle"/>): the page edits
    /// a set the registry accepts, whatever was in force when it opened.
    /// </summary>
    public void Put(IEnumerable<Feature> features)
    {
        on.Clear();
        on.UnionWith(registry.Settle(features));
    }

    /// <summary>
    /// The feature <paramref name="feature"/> refines when that one is off — so its own box is locked — else null.
    /// </summary>
    public Feature? LockedUnder(Feature feature) =>
        feature.Refines is { } id && registry.Find(id) is { } refined && !On(refined) ? refined : null;

    /// <summary>What a press of a box did.</summary>
    /// <param name="Taken">Whether the feature is now as the press asked.</param>
    /// <param name="Refused">Why not, when it is not.</param>
    /// <param name="AlsoOff">The features switched off with it because they needed it, each with its reason.</param>
    public readonly record struct Ticked(bool Taken, Conflict? Refused, IReadOnlyList<Conflict> AlsoOff);

    /// <summary>
    /// A box pressed: <paramref name="feature"/> switched <paramref name="to"/>. On, it takes only if the registry
    /// allows it beside the rest (<see cref="FeatureRegistry.Blocks"/>); off, it always takes, and whatever needed
    /// it goes off with it. A feature with no switch does not move.
    /// </summary>
    public Ticked Tick(Feature feature, bool to)
    {
        if (!feature.Switchable) return new Ticked(false, null, []);
        if (to)
        {
            if (registry.Blocks(feature, on) is { } why) return new Ticked(false, why, []);
            on.Add(feature);
            return new Ticked(true, null, []);
        }
        on.Remove(feature);
        var gone = new List<Conflict>();
        var kept = registry.Settle(on, gone.Add);
        on.Clear();
        on.UnionWith(kept);
        return new Ticked(true, null, gone);
    }
}
