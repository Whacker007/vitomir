// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The value ramps: a table of 128 slots (0x2c bytes each) that walks a float
// or a colour towards a target at a constant rate, once a frame. The explosion scripts are its only
// users that matter here — every `DYNAINTENSITY RAMP` and `DYNACOLOR RAMP` is one.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Explosions;

/// <summary>Which of a dynamic light's fields a ramp walks.</summary>
public enum RampField
{
    /// <summary><see cref="DynamicLight.Current"/>, a float ramp (kind 1).</summary>
    Intensity,
    /// <summary><see cref="DynamicLight.Radius"/>, a float ramp (kind 1). No .nex line can start one (DYNARADIUS is not parsed).</summary>
    Radius,
    /// <summary><see cref="DynamicLight.Color"/>, a colour ramp (kind 2).</summary>
    Color,
}

/// <summary>
/// The table and its tick. **Ramps on one field do not replace each other**: starting a second
/// while the first runs adds a second slot, both step the value, and each snaps it to its own
/// target as it ends. A full table refuses the ramp and changes nothing.
/// </summary>
public sealed class ValueRamps
{
    /// <summary>The table's size (0x2c apiece).</summary>
    public const int Slots = 128;

    sealed class Slot
    {
        public int Id;                  // [0] a serial, 0 when free
        public int Kind;                // [1] 1 a float, 2 a vector
        public WorldThing? Master;      // [2] the thing it belongs to
        public int EndMs;               // [3] when it ends
        public DynamicLight? Light;     // [4] the data: a light and which field
        public RampField Field;
        public Vector3 Rate;            // [5..7] per millisecond
        public Vector3 Target;          // [8..10]
    }

    readonly Slot[] slots = new Slot[Slots];
    int top = -1;                       // the highest slot in use
    int serial;

    /// <summary>The engine's clock the ramps start from, set by whoever runs them.</summary>
    public int Now { get; set; }

    /// <summary>How many slots are running, for tests.</summary>
    public int Running => slots.Count(s => s is { Id: not 0 });

    /// <summary>
    /// A float ramp: nothing if the field is already at the target; set at once for a
    /// time of 0; otherwise the first free slot, running at <c>(target − now) / ms</c> per
    /// millisecond, and the thing marked animating (flag `0x800000`). Answers the slot's serial, or 0.
    /// </summary>
    public int Start(WorldThing master, DynamicLight light, RampField field, float target, int ms)
    {
        float have = Get(light, field).X;
        if (have == target) return 0;
        if (ms == 0) { Set(light, field, new Vector3(target, 0, 0)); return 0; }
        int i = Free();
        if (i < 0) return 0;
        var s = slots[i] ??= new Slot();
        s.Kind = 1; s.Master = master; s.EndMs = Now + ms; s.Light = light; s.Field = field;
        s.Id = ++serial;
        s.Target = new Vector3(target, 0, 0);
        s.Rate = new Vector3((target - have) / ms, 0, 0);
        if (i > top) top = i;
        master.Flags |= WorldThing.Flag.Animating;
        return s.Id;
    }

    /// <summary>
    /// A colour ramp. Its early "already there" test compares **the wrong words**: the
    /// red against the target's red, then the words 12 and 24 bytes on — for a light's colour, this
    /// frame's intensity against the target's green and the spot direction's Y
    /// (always 0) against its blue. Kept. Otherwise as the float ramp, and the rate is
    /// <c>(target − now) × (1 / ms)</c>.
    /// </summary>
    public int StartColor(WorldThing master, DynamicLight light, Vector3 target, int ms)
    {
        var have = light.Color;
        if (have.X == target.X && light.Intensity == target.Y && light.Direction.Y == target.Z) return 0;
        if (ms == 0) { light.Color = target; return 0; }
        int i = Free();
        if (i < 0) return 0;
        var s = slots[i] ??= new Slot();
        s.Kind = 2; s.Master = master; s.EndMs = Now + ms; s.Light = light; s.Field = RampField.Color;
        s.Id = ++serial;
        s.Target = target;
        float inverse = 1f / ms;
        s.Rate = (target - have) * inverse;
        if (i > top) top = i;
        master.Flags |= WorldThing.Flag.Animating;
        return s.Id;
    }

    int Free()
    {
        for (int i = 0; i < Slots; i++) if (slots[i] is not { Id: not 0 }) return i;
        return -1;
    }

    /// <summary>
    /// Once a frame (from the world tick after every thing has ticked): each running slot
    /// in order either steps its value by <c>frame ms × rate</c> — while the clock is still before its
    /// end — or puts the value at the target and frees itself.
    /// </summary>
    /// <param name="now">The clock.</param>
    /// <param name="frameMs">This frame's milliseconds.</param>
    public void Step(int now, int frameMs)
    {
        Now = now;
        for (int i = 0; i <= top; i++)
        {
            if (slots[i] is not { Id: not 0 } s || s.Light is not { } light) continue;
            if (now < s.EndMs)
            {
                var v = Get(light, s.Field);
                Set(light, s.Field, s.Kind == 1 ? new Vector3(frameMs * s.Rate.X + v.X, 0, 0) : frameMs * s.Rate + v);
                continue;
            }
            Set(light, s.Field, s.Target);
            s.Id = 0;
            if (i == top) top--;
        }
    }

    /// <summary>A thing is being destroyed: every ramp it owns stops where it is.</summary>
    public void Forget(WorldThing master)
    {
        for (int i = 0; i <= top; i++)
            if (slots[i] is { Id: not 0 } s && ReferenceEquals(s.Master, master))
            {
                s.Id = 0;
                if (i == top) top--;
            }
    }

    static Vector3 Get(DynamicLight l, RampField f) => f switch
    {
        RampField.Intensity => new Vector3(l.Current, 0, 0),
        RampField.Radius => new Vector3(l.Radius, 0, 0),
        _ => l.Color,
    };

    static void Set(DynamicLight l, RampField f, Vector3 v)
    {
        switch (f)
        {
            case RampField.Intensity: l.Current = v.X; break;
            case RampField.Radius: l.Radius = v.X; break;
            default: l.Color = v; break;
        }
    }
}
