// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours (the): the flurry's cue ("Mostly a" — a simple cue now for the extra blows
// a Celerity swing carries, until flurry animations come from the models repository).
using System.Numerics;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Combat;

/// <summary>
/// A flash to draw: where, its age and its life, seconds; its place in the flurry (1 the first extra blow). An earlier build
/// (at the end): <paramref name="Guarded"/>, a blow met at a guard and held there (or parried): drawn as
/// a small steel-grey flash at the shield or the blade, not the gold star of a blow that landed.
/// </summary>
public readonly record struct BlowCueMark(Vector3 Point, float Age, float Life, int Index, bool Guarded = false);

/// <summary>
/// The flurry's cue (ours): Celerity's split resolves a swing's extra blows all at once on its
/// one contact, so nothing on screen told k blows from one. Each extra blow (<see cref="HarmResolved.Flurry"/> above 0) is
/// shown as a short flash at the contact's point, the k-th <see cref="Spacing"/> × k after the hit, each living
/// <see cref="Life"/>: k blows read as k. <b>Presentation only</b>: a service the picture reads, not digested; the harm is
/// the rules' and is resolved at the hit. A sound for each waits for audio (the platform has none yet).
/// </summary>
public sealed class BlowCues
{
    /// <summary>The time between one extra blow's flash and the next, s (ours: quick enough to read as one flurry, slow enough to count).</summary>
    public const float Spacing = 0.04f;

    /// <summary>How long a flash lives, s.</summary>
    public const float Life = 0.15f;

    readonly struct Cue(Vector3 point, long tick, int index, float delay, bool guarded = false)
    {
        public readonly Vector3 Point = point;
        public readonly long Tick = tick;
        public readonly int Index = index;
        public readonly float Delay = delay;   // 0 for a blow drawn at its own moment
        public readonly bool Guarded = guarded;   // a blow a guard held
    }

    readonly Cue[] ring = new Cue[64];
    int next, count;

    /// <summary>How many cues are kept (the newest <see cref="ring"/>'s length).</summary>
    public int Count => count;

    /// <summary>Adds the cue of the <paramref name="index"/>-th extra blow of a flurry that met at <paramref name="point"/> on <paramref name="tick"/>.</summary>
    public void Add(Vector3 point, long tick, int index) => Put(new Cue(point, tick, index, Spacing * index));

    /// <summary>
    /// Adds the cue of the <paramref name="index"/>-th extra blow of a flurry that landed at <paramref name="point"/>
    /// at its own moment, <paramref name="tick"/> (each extra blow its own strike, checked then): drawn from that
    /// tick, with no spacing added.
    /// </summary>
    public void AddAt(Vector3 point, long tick, int index) => Put(new Cue(point, tick, index, 0f));

    /// <summary>
    /// Adds the cue of the <paramref name="index"/>-th extra blow of a flurry met at a guard at its
    /// moment and held there or parried (no harm): a steel-grey flash at <paramref name="point"/>, where the blade met the shield
    /// or the guarding blade, so the defender sees his guard took it.
    /// </summary>
    public void AddGuarded(Vector3 point, long tick, int index) => Put(new Cue(point, tick, index, 0f, guarded: true));

    void Put(in Cue cue)
    {
        ring[next] = cue;
        next = (next + 1) % ring.Length;
        if (count < ring.Length) count++;
    }

    /// <summary>
    /// The flashes alive at <paramref name="seconds"/> (the picture's time, the world's seconds between ticks), into
    /// <paramref name="into"/> (cleared first). A flash before its time or past its life is not drawn.
    /// </summary>
    public void Fill(double seconds, List<BlowCueMark> into)
    {
        into.Clear();
        for (int i = 0; i < count; i++)
        {
            var c = ring[i];
            float age = (float)(seconds - c.Tick / (double)World.TicksPerSecond) - c.Delay;
            if (age < 0f || age > Life) continue;
            into.Add(new BlowCueMark(c.Point, age, Life, c.Index, c.Guarded));
        }
    }
}

/// <summary>
/// Fills <see cref="BlowCues"/> (ours, <see cref="Phase.Resolve"/>, after the rules' harm and the platform's death):
/// each <see cref="HarmResolved"/> of an extra blow this tick at its contact's point (<see cref="ContactMade"/> of the same
/// id, this tick). Reads only; writes the not-digested cue service.
/// <para>An extra blow that landed at its own moment (its <see cref="FlurryStrike"/> this tick,
/// checked by the platform) is drawn at the part it met then, from then (<see cref="BlowCues.AddAt"/>); one that did not land has
/// no <see cref="HarmResolved"/> and so no star. The earlier build's compressed blows (<c>flurry-compressed</c>) are drawn as before: at the
/// contact's point, 0.04 s apart.</para>
/// </summary>
public sealed class BlowCueSystem : ISystem
{
    public void Run(World world)
    {
        // An extra blow met at a guard and not landed (held or parried: no harm for its strike) — the
        // guard's flash where the blade met it.
        if (world.TryService<EventQueue<FlurryStrike>>(out var tried) && tried.ThisTick.Count > 0)
        {
            var cueService = world.Service<BlowCues>();
            var harmNow = world.TryService<EventQueue<HarmResolved>>(out var hq) ? hq.ThisTick : null;
            var list = tried.ThisTick;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Check == FlurryCheck.Guarded && !Landed(harmNow, list[i].Strike)) cueService.AddGuarded(list[i].Point, world.Tick, list[i].Index);
        }
        if (!world.TryService<EventQueue<HarmResolved>>(out var harms) || harms.ThisTick.Count == 0) return;
        if (!world.TryService<EventQueue<ContactMade>>(out var contacts)) return;
        var cues = world.Service<BlowCues>();
        var resolved = harms.ThisTick;
        for (int i = 0; i < resolved.Count; i++)
        {
            var h = resolved[i];
            if (h.Flurry <= 0) continue;
            if (h.Strike != 0 && world.TryService<EventQueue<FlurryStrike>>(out var strikes) && At(strikes.ThisTick, h.Strike, out var point))
            {
                cues.AddAt(point, world.Tick, h.Flurry);   // at its own moment, where it met
                continue;
            }
            var made = contacts.ThisTick;
            for (int j = 0; j < made.Count; j++)
                if (made[j].ContactId == h.ContactId)
                {
                    cues.Add(made[j].Point, world.Tick, h.Flurry);
                    break;
                }
        }
    }

    /// <summary>Whether a strike did harm this tick (an overpowered guard's: it landed, its star is drawn).</summary>
    static bool Landed(IReadOnlyList<HarmResolved>? harm, long strike)
    {
        if (harm is null) return false;
        for (int i = 0; i < harm.Count; i++) if (harm[i].Strike == strike && harm[i].Flurry > 0) return true;
        return false;
    }

    /// <summary>The point a strike checked at its moment met (not a compressed one's: those are drawn at the contact).</summary>
    static bool At(IReadOnlyList<FlurryStrike> list, long strike, out Vector3 point)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i].Strike == strike && list[i].Check != FlurryCheck.Compressed)
            {
                point = list[i].Point;
                return true;
            }
        point = default;
        return false;
    }
}

/// <summary>The cue's registration.</summary>
public static class BlowCueServices
{
    /// <summary>After the platform's death (<see cref="DeathServices.DeathOrder"/>).</summary>
    public const int CueOrder = 950;

    /// <summary>Registers <see cref="BlowCues"/> and its system (<c>Resolve/950 platform.blow-cues</c>).</summary>
    public static WorldBuilder Register(WorldBuilder b)
    {
        b.Service(new BlowCues());
        b.System(Phase.Resolve, CueOrder, "platform.blow-cues", new BlowCueSystem());
        return b;
    }
}
