// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Runtime.CompilerServices;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Combat;

/// <summary>
/// A foe placed in play that waits for the one it will fight (ours; digested, plain data): its duelling brain stands
/// (<see cref="BrainKind.Stand"/>, facing where it was put) until its foe comes within <see cref="Metres"/> across the ground, or a blow
/// meets it; then the brain duels (<see cref="FoeWokeSystem"/>) and this is taken off.: <i>"Set up a fight where I
/// can go after one"</i> — he walks up to it, rather than its running at him across the yard from the first tick.
/// </summary>
public struct FoeWaits
{
    /// <summary>How near its foe must come before it fights (m, feet to feet across the ground; 0: at once).</summary>
    public float Metres;
}

/// <summary>A foe stopped waiting and began to fight: who, at what tick, and whether a blow woke it (else its foe came near).</summary>
public readonly record struct FoeWoke(EntityId Who, EntityId Foe, long Tick, bool Struck);

/// <summary>The foes' waiting registered: <see cref="FoeWaits"/>, <see cref="FoeWoke"/> and <c>Think/93 platform.foe-wake</c>.</summary>
public static class FoeWakes
{
    /// <summary>The system's place: <c>Think/93</c>, before the duelling brain (95).</summary>
    public const int Order = 93;

    /// <summary>
    /// Registers <see cref="FoeWaits"/> (digested), the event <see cref="FoeWoke"/> and <c>Think/93 platform.foe-wake</c>. Only in a world
    /// with foes placed (the program's <c>--foe</c>): a world without them is the world before, digest for digest. After the brains.
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b)
    {
        b.Component<FoeWaits>();
        b.Event<FoeWoke>();
        b.System(Phase.Think, Order, "platform.foe-wake", new FoeWokeSystem());
        return b;
    }

    /// <summary>Puts <paramref name="e"/>'s brain to wait for its foe within <paramref name="metres"/> (nothing for 0 or less: it fights at once).</summary>
    public static void Wait(World world, EntityId e, float metres)
    {
        if (metres <= 0f || !world.Has<DuelBrain>(e)) return;
        world.Get<DuelBrain>(e).Kind = BrainKind.Stand;
        world.Set(e, new FoeWaits { Metres = metres });
    }
}

/// <summary>
/// Wakes the waiting foes (<see cref="Phase.Think"/>, before the brains): a foe whose foe is within its
/// <see cref="FoeWaits.Metres"/> (across the ground), or whom a blow met last tick (its body, shield or blade; anyone's), gets its
/// duelling brain back (<see cref="BrainKind.Duel"/>) and loses its wait; <see cref="FoeWoke"/> says so. Draws nothing; allocates nothing.
/// </summary>
public sealed class FoeWokeSystem : ISystem
{
    public void Run(World world)
    {
        var waits = world.Store<FoeWaits>();
        if (waits.Count == 0) return;
        var brains = world.Store<DuelBrain>();
        var bodies = world.Store<Body>();
        var met = world.TryService<EventQueue<ContactMade>>(out var cq) ? cq.LastTick : null;
        // Backwards: a foe woken is taken out of the store being walked.
        for (int i = waits.Count - 1; i >= 0; i--)
        {
            var e = waits.Entities[i];
            float metres = waits.Data[i].Metres;
            ref var brain = ref brains.Find(e);
            ref var body = ref bodies.Find(e);
            if (Unsafe.IsNullRef(ref brain) || Unsafe.IsNullRef(ref body)) { world.Remove<FoeWaits>(e); continue; }
            bool struck = false;
            if (met is not null) for (int k = 0; k < met.Count; k++) if (met[k].Target == e) { struck = true; break; }   // its body, its shield or its blade
            bool near = false;
            ref var foeBody = ref bodies.Find(brain.Foe);
            if (!Unsafe.IsNullRef(ref foeBody))
            {
                float dx = foeBody.Feet.X - body.Feet.X, dy = foeBody.Feet.Y - body.Feet.Y;
                near = dx * dx + dy * dy <= metres * metres;
            }
            if (!struck && !near) continue;
            brain.Kind = BrainKind.Duel;
            world.Remove<FoeWaits>(e);
            world.Events<FoeWoke>().Publish(new FoeWoke(e, brain.Foe, world.Tick, struck));
        }
    }
}
