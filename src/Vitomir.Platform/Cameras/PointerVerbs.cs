// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours. 1.1 decides a click's verb for whatever is under its cursor, all the time, not at the click
// (read in src/Vitomir.Engine/Rpg/Cursor.cs at 402bef2); this is the same shape, with the
// enhanced game's verbs. The reach test is 1.1's (src/Vitomir.Engine/World/Bodies.cs, InReach).
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Cameras;

/// <summary>What a click on the focus would do: nothing; talk to a person; take a target asked for; or refuse one.</summary>
public enum PointerVerb : byte
{
    None = 0,
    /// <summary>A person within talking distance, not the player, in sight of the body's eyes.</summary>
    Talk = 1,
    /// <summary>What a target request takes (its kind, within its range, seen if it needs sight).</summary>
    Target = 2,
    /// <summary>What a target request does not take (<see cref="TargetRefusal"/> says why).</summary>
    Refused = 3,
}

/// <summary>What a target request takes: a person, a place in the level, or either.</summary>
[Flags]
public enum TargetKinds : byte
{
    Person = 1,
    Place = 2,
    Any = Person | Place,
}

/// <summary>Why a target is refused.</summary>
public enum TargetRefusal : byte
{
    None = 0,
    /// <summary>Nothing is under the focus (the sky, off the picture).</summary>
    Nothing = 1,
    /// <summary>Not a kind the request takes.</summary>
    WrongKind = 2,
    /// <summary>Beyond the request's range from the body's eyes.</summary>
    TooFar = 3,
    /// <summary>The request needs sight and something stands between the body's eyes and it.</summary>
    NotSeen = 4,
}

/// <summary>
/// A request for a target (the hook for a power's targeting, DI): its number (the asker's own, carried into the
/// events), the kinds it takes, its range from the body's eyes (m), whether it needs the body's own sight — false for
/// an act "around the corner", taken where the camera sees what the eyes do not — and whether it stays after a pick
/// (the developer's test does; a power's would not).
/// </summary>
public readonly record struct TargetRequest(int Id, TargetKinds Kinds, float Range, bool NeedsSight = true, bool Standing = false)
{
    /// <summary>
    /// The developer's switch <c>--target-test &lt;range&gt;[,nolos][,person|place|any]</c>: a standing request, number 1,
    /// for either kind unless one is named, needing sight unless <c>nolos</c>.
    /// </summary>
    public static bool TryParse(string text, out TargetRequest request, out string error)
    {
        request = default;
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float range) || !(range > 0f))
        {
            error = $"--target-test wants <range>[,nolos][,person|place|any] with a range in metres above 0, not \"{text}\"";
            return false;
        }
        bool sight = true;
        var kinds = TargetKinds.Any;
        for (int i = 1; i < parts.Length; i++)
            switch (parts[i].ToLowerInvariant())
            {
                case "nolos": sight = false; break;
                case "los": sight = true; break;
                case "person": kinds = TargetKinds.Person; break;
                case "place": kinds = TargetKinds.Place; break;
                case "any": kinds = TargetKinds.Any; break;
                default:
                    error = $"--target-test: \"{parts[i]}\" is not nolos, los, person, place or any";
                    return false;
            }
        request = new TargetRequest(1, kinds, range, sight, Standing: true);
        error = "";
        return true;
    }

    /// <summary>The request as the switch writes it (a tape's head keeps it so).</summary>
    public string Text => string.Create(CultureInfo.InvariantCulture,
        $"{Range:0.###}{(NeedsSight ? "" : ",nolos")}{(Kinds == TargetKinds.Any ? "" : Kinds == TargetKinds.Person ? ",person" : ",place")}");
}

/// <summary>The verb under the focus changed (once per change; the pointer's or the reticle's, by <c>Free</c>).</summary>
public readonly record struct PointerVerbChanged(EntityId Entity, long Tick, PointerVerb Verb, PointerVerb Was, ulong Thing, bool Free);

/// <summary>A click on a person with talk (the freed pointer's, or use on the reticle): <c>Gap</c> is the distance between their sides (m). Conversations are not built yet.</summary>
public readonly record struct TalkRequested(EntityId Who, EntityId Whom, long Tick, float Gap, bool Free);

/// <summary>A target taken for request <c>Request</c>: a person (<c>Thing</c>) or a place in the level, the point, its distance from the eyes.</summary>
public readonly record struct TargetPicked(EntityId Who, long Tick, int Request, ReticleTarget Kind, ulong Thing, Vector3 Point, float Distance, bool Free)
{
    /// <summary>The part of the person under the focus (<see cref="ReticleAnswer.Part"/>: "head", "left forearm"), or null for a place or when no drawn part is under it.</summary>
    public string? Part { get; init; }
}

/// <summary>A click on what request <c>Request</c> does not take, and why; the request stays open.</summary>
public readonly record struct TargetRefused(EntityId Who, long Tick, int Request, TargetRefusal Why, ulong Thing, Vector3 Point, float Distance, bool Free);

/// <summary>
/// The verb: one function for the reticle and the freed pointer (the module's to extend). With a target asked
/// for, the answer is <b>target</b> when it is a kind the request takes, within its range from the eyes and — when it
/// needs sight — not blocked from them, else <b>refused</b> with why. Otherwise <b>talk</b> for another body (every
/// body is a person yet) within 1.1's talking distance (<see cref="PointerSettings.TalkReach"/>, body to body) and not
/// blocked from the eyes (the reticle's own test); <b>none</b> for everything else — a person too far, a dead one,
/// the player, the level, nothing.
/// </summary>
public static class PointerVerbs
{
    public static PointerVerb Of(World world, EntityId who, in ReticleAnswer on, TargetRequest? request, PointerSettings settings,
                                 out TargetRefusal why, out float gap)
    {
        why = TargetRefusal.None;
        gap = float.PositiveInfinity;
        bool person = on.Kind == ReticleTarget.Person && on.Entity != who.Packed;
        if (person) gap = Gap(world, who, EntityId.FromPacked(on.Entity));
        if (request is { } r)
        {
            why = on.Kind == ReticleTarget.Nothing ? TargetRefusal.Nothing
                : on.Kind == ReticleTarget.Person ? (person && (r.Kinds & TargetKinds.Person) != 0 ? TargetRefusal.None : TargetRefusal.WrongKind)
                : (r.Kinds & TargetKinds.Place) != 0 ? TargetRefusal.None : TargetRefusal.WrongKind;
            if (why == TargetRefusal.None && on.Distance > r.Range) why = TargetRefusal.TooFar;
            if (why == TargetRefusal.None && r.NeedsSight && on.Blocked) why = TargetRefusal.NotSeen;
            return why == TargetRefusal.None ? PointerVerb.Target : PointerVerb.Refused;
        }
        // Nobody talks to the dead (a walk in play showed "talk" on a fallen guard).
        if (person && world.TryStore<Combat.Dead>(out var dead) && dead.Has(EntityId.FromPacked(on.Entity))) return PointerVerb.None;
        if (person && !on.Blocked && gap <= settings.TalkReach) return PointerVerb.Talk;
        return PointerVerb.None;
    }

    /// <summary>
    /// The distance between two bodies' sides across the ground (m), or infinity when their heights do not overlap —
    /// 1.1's reach test: the centres' distance in the plane less both radii.
    /// </summary>
    public static float Gap(World world, EntityId a, EntityId b)
    {
        if (!world.Alive(a) || !world.Alive(b) || !world.Has<Body>(a) || !world.Has<Body>(b) || !world.Has<Locomotion>(a) || !world.Has<Locomotion>(b))
            return float.PositiveInfinity;
        ref readonly var ba = ref world.Get<Body>(a);
        ref readonly var bb = ref world.Get<Body>(b);
        ref readonly var la = ref world.Get<Locomotion>(a);
        ref readonly var lb = ref world.Get<Locomotion>(b);
        float aLow = ba.Feet.Z, aHigh = aLow + ba.CapsuleHeight(la), bLow = bb.Feet.Z, bHigh = bLow + bb.CapsuleHeight(lb);
        if (bLow > aHigh || aLow > bHigh) return float.PositiveInfinity;
        float dx = bb.Feet.X - ba.Feet.X, dy = bb.Feet.Y - ba.Feet.Y;
        return MathF.Sqrt(dx * dx + dy * dy) - la.Radius - lb.Radius;
    }
}
