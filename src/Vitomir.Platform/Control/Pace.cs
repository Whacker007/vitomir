// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Control;

/// <summary>Why a kept sprint ended (<see cref="SprintKept"/>).</summary>
public enum SprintLetGo : byte
{
    /// <summary>Not let go: the sprint was kept (<see cref="SprintKept.Kept"/>).</summary>
    None = 0,
    /// <summary>No move wished: the body stopped.</summary>
    Stopped = 1,
    /// <summary>Walk was pressed.</summary>
    Walk = 2,
    /// <summary>Sprint was tapped again.</summary>
    Tapped = 3,
    /// <summary>Sprint was pressed again and held: back to the plain pace when let go.</summary>
    Held = 4,
}

/// <summary>
/// <b>The sprint kept or let go</b> (for the log): a tap of sprint kept it (<see cref="Kept"/> true) or a kept
/// sprint ended (<see cref="Kept"/> false, <see cref="Why"/>); <see cref="Supernatural"/> whether it was the body's
/// supernatural speed. The log's line: "sprint kept" / "sprint let go (stopped | walk | tapped | held)".
/// </summary>
public readonly record struct SprintKept(EntityId Entity, long Tick, bool Kept, SprintLetGo Why, bool Supernatural, Vector3 Feet);

/// <summary>
/// <b>Supernatural speed on or off</b> (for the log): a double tap of sprint gave the body its resting speed
/// (<see cref="On"/>, at <see cref="Speed"/> m/s — what its numbers in force give, never less than its sprint), or that
/// sprint ended. Only a body with a resting speed (<see cref="Locomotion.RestingSpeed"/> above 0) has it: the human's
/// double tap is a plain sprint and raises nothing. The log's line: "supernatural speed on (7.0 m/s)" / "… off".
/// </summary>
public readonly record struct SupernaturalSpeed(EntityId Entity, long Tick, bool On, float Speed, Vector3 Feet);

/// <summary>
/// <b>The pace from the keys</b> (ours):
/// the options menu has 'always run', on by default: just moving runs. With
/// that option on, walk held walks; Ctrl tapped or held sprints; and
/// a double tap of Ctrl may be 'supernatural speed'.
/// <list type="bullet">
/// <item><b>Always run</b> (<see cref="TickInput.AlwaysRun"/>, on by default): plain movement runs and walk held walks;
/// off, plain movement walks and walk held runs.</item>
/// <item>Sprint <b>held</b> (still down after <see cref="TapTicks"/>): a sprint while held, the plain pace on letting
/// go.</item>
/// <item>Sprint <b>tapped</b> (down and up within <see cref="TapTicks"/>): the sprint is <b>kept</b> until no move is
/// wished, walk is pressed, or sprint is tapped again.</item>
/// <item>Sprint <b>double-tapped</b> (a second press within <see cref="DoubleTapTicks"/> of the first):
/// <b>supernatural speed</b> (<see cref="Gait.Supernatural"/>), held or kept as the second press goes, for a body with
/// a resting speed; the human's is a plain sprint.</item>
/// </list>
/// All timing is in ticks of the tape's frames, kept in the body's <see cref="Controlled"/> tag, so a replay is exact.
/// </summary>
public static class Pace
{
    /// <summary>A press of sprint let go within this many ticks is a tap (0.25 s).</summary>
    public const int TapTicks = World.TicksPerSecond / 4;

    /// <summary>A press of sprint within this many ticks of the one before is a double tap (0.3 s).</summary>
    public const int DoubleTapTicks = World.TicksPerSecond * 3 / 10;

    /// <summary>A wish shorter than this is no move wished (a kept sprint ends).</summary>
    public const float StillWish = 0.05f;

    /// <summary>How old a press is counted up to (ticks): far past any window, so the count never overflows.</summary>
    const int AgeCap = 1 << 20;

    /// <summary>What the press that is down was (<see cref="Controlled.PressKind"/>).</summary>
    const int PressPlain = 0, PressWhileKept = 1, PressDouble = 2;

    /// <summary>
    /// This tick's gait for a controlled body from its frame (<paramref name="frame"/>, with the buttons a cancel
    /// swallowed already taken out) and the presses (<paramref name="pressed"/>), updating its sprint state in
    /// <paramref name="tag"/> and publishing <see cref="SprintKept"/> and <see cref="SupernaturalSpeed"/> when they change.
    /// </summary>
    public static Gait Of(World world, EntityId e, ref Controlled tag, in ActionFrame frame, Buttons pressed, bool alwaysRun, Vector2 wish)
    {
        bool down = frame.Holds(Buttons.Sprint);
        bool press = (pressed & Buttons.Sprint) != 0;
        var locos = world.Store<Locomotion>();
        float resting = locos.Has(e) ? locos.Get(e).RestingSpeed : 0f;
        int keptWas = tag.SprintKept, superWas = tag.Supernatural;
        var why = SprintLetGo.None;

        if (tag.PressAge > 0 && tag.PressAge < AgeCap) tag.PressAge++;
        if (press)
        {
            bool second = tag.PressAge > 0 && tag.PressAge - 1 <= DoubleTapTicks;
            tag.PressKind = second ? PressDouble : tag.SprintKept != 0 ? PressWhileKept : PressPlain;
            tag.Supernatural = second && resting > 0f ? 1 : 0;
            tag.PressAge = 1;
            tag.SprintDown = 1;
        }
        else if (down && tag.SprintDown > 0) { if (tag.SprintDown < AgeCap) tag.SprintDown++; }
        else if (!down && tag.SprintDown > 0)
        {
            // Let go: a tap keeps the sprint (or, while kept, ends it); a hold ends it.
            bool tap = tag.SprintDown <= TapTicks;
            if (tap && tag.PressKind != PressWhileKept) tag.SprintKept = 1;
            else
            {
                if (tag.SprintKept != 0 || tag.PressKind == PressWhileKept) why = tap ? SprintLetGo.Tapped : SprintLetGo.Held;
                tag.SprintKept = 0;
                tag.Supernatural = 0;
            }
            tag.SprintDown = 0;
        }

        // A kept sprint ends when no move is wished or walk is pressed (a held one goes on while held).
        if (tag.SprintKept != 0 && !down)
        {
            if ((pressed & Buttons.Walk) != 0) why = SprintLetGo.Walk;
            else if (wish.LengthSquared() < StillWish * StillWish) why = SprintLetGo.Stopped;
            if (why is SprintLetGo.Walk or SprintLetGo.Stopped) { tag.SprintKept = 0; tag.Supernatural = 0; }
        }
        else if (tag.SprintKept != 0 && (pressed & Buttons.Walk) != 0) { tag.SprintKept = 0; why = SprintLetGo.Walk; }
        if (!down && tag.SprintKept == 0) tag.Supernatural = 0;

        bool sprinting = down || tag.SprintKept != 0;
        var bodies = world.Store<Body>();
        var feet = bodies.Has(e) ? bodies.Get(e).Feet : Vector3.Zero;
        if (tag.SprintKept != keptWas)
        {
            bool kept = tag.SprintKept != 0;
            world.Events<SprintKept>().Publish(new SprintKept(e, world.Tick, kept, kept ? SprintLetGo.None : why, (superWas | tag.Supernatural) != 0, feet));
        }
        if (tag.Supernatural != superWas)
        {
            float speed = locos.Has(e) ? locos.Get(e).SpeedOf(Gait.Supernatural) : 0f;
            world.Events<SupernaturalSpeed>().Publish(new SupernaturalSpeed(e, world.Tick, tag.Supernatural != 0, speed, feet));
        }

        if (sprinting) return tag.Supernatural != 0 ? Gait.Supernatural : Gait.Sprint;
        bool walkKey = frame.Holds(Buttons.Walk);
        return walkKey == alwaysRun ? Gait.Walk : Gait.Run;
    }
}
