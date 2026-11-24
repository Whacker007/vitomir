// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: how a kind of townsfolk on the models repository's bodies moves beyond the player's set.
using System.Globalization;
using Vitomir.Platform.Assets;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// What a kind on the models repository's bodies does beyond the player's set (<see cref="PeopleKinds.Register"/>): a walk of
/// its own style in place of the walk's eight ways, its height, and its stand broken by the set's fidget. Null (or the default) is the
/// player's kind, exactly as before.
/// </summary>
public sealed record PersonStyle
{
    /// <summary>
    /// The clip its walk plays (the models repository's styles: <c>walk_old</c>, <c>walk_frail</c>, <c>walk_drunk</c>, <c>walk_proud</c>,
    /// <c>walk_shuffle</c>, <c>walk_procession</c> …), straight ahead at the ground speed it was made for, in place of the walk's eight
    /// ways (the townsfolk walk where they face); null for the plain walk's eight ways. A name the set does not have is the plain walk.
    /// </summary>
    public string? Walk { get; init; }

    /// <summary>
    /// How much bigger the person is drawn than the base it is made on (its height over the base's: <see cref="CharacterKind.Scale"/>);
    /// its strides, and so the speeds its walk and run are drawn for, by as much.
    /// </summary>
    public float Scale { get; init; } = 1f;

    /// <summary>
    /// Standing, the seconds of the stand (whole loops of it, at least one) before the set's fidget (<c>idle</c>) plays once, then the
    /// stand again (<see cref="FidgetClip"/>); 0 for the stand alone, as the player stands.
    /// </summary>
    public float FidgetAfter { get; init; }

    /// <summary>A key for logs and caches.</summary>
    public string Key => string.Create(CultureInfo.InvariantCulture, $"{Walk ?? "walk"} ×{Scale:0.###}{(FidgetAfter > 0f ? $" fidget {FidgetAfter:0.#} s" : "")}");
}

/// <summary>
/// The stand, looped for whole loops, then the set's fidget once, the whole looping (a townsperson standing: the models
/// stream's <c>idle</c> is <i>"a fidget, for later life layers"</i>; this is the simplest such layer, a pure function of the clip's time,
/// so the pose stays the animation's state's). The fidget is made from the stand and comes back to it, so the seams are the stand's own
/// pose (the tests measure them).
/// </summary>
public sealed class FidgetClip : IAnimationClip
{
    readonly IAnimationClip stand, fidget;

    /// <summary>The stand <paramref name="stand"/> for <paramref name="after"/> seconds (rounded to its whole loops, at least one), then <paramref name="fidget"/>.</summary>
    public FidgetClip(string name, IAnimationClip stand, IAnimationClip fidget, float after)
    {
        Name = name;
        this.stand = stand;
        this.fidget = fidget;
        int loops = stand.Duration > 0f ? Math.Max(1, (int)MathF.Round(after / stand.Duration)) : 1;
        StandSeconds = stand.Duration * loops;
        Duration = StandSeconds + fidget.Duration;
    }

    public string Name { get; }
    public float Duration { get; }
    public bool Loops => true;

    /// <summary>How long the stand plays before the fidget (whole loops of it).</summary>
    public float StandSeconds { get; }

    public void Sample(float seconds, Skeleton skeleton, Span<BoneTransform> local)
    {
        float t = Duration > 0f ? seconds - MathF.Floor(seconds / Duration) * Duration : 0f;
        if (t < StandSeconds) stand.Sample(t, skeleton, local);
        else fidget.Sample(MathF.Min(t - StandSeconds, fidget.Duration), skeleton, local);
    }
}
