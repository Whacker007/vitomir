// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The quick slots' keys in the player's control: a slot key's press (an edge) writes one
/// <see cref="PowerIntent"/> on the player for one tick, the lowest slot when two go down together, none while held; a
/// press with the pointer free still uses the slot; a world without the powers' contract is left as it was, digest for
/// digest; tapes carry the slot buttons and replay them, and a tape from before has none. A stand-in for the module reads
/// and clears the intent in Act, as the module's powers do.
/// </summary>
public sealed class QuickSlotControlTests
{
    /// <summary>What the stand-in module saw: (tick, slot) for every intent it took.</summary>
    sealed class Uses
    {
        public readonly List<(long Tick, int Slot)> Seen = [];
    }

    static World Flat(out EntityId player, bool contract = true, bool reader = true, Uses? uses = null)
    {
        var b = MovementServices.Register(new WorldBuilder(1), new TestLevel().Floor(-20, -20, 200, 20).Build());
        if (contract) PowerContract.Register(b);
        if (contract && reader)
        {
            var seen = uses ?? new Uses();
            b.Service(seen);
            b.System(Phase.Act, 0, "stand-in powers", w =>
            {
                var store = w.Store<PowerIntent>();
                for (int i = 0; i < store.Count; i++)
                {
                    ref var intent = ref store.Data[i];
                    if (intent.Slot == 0) continue;
                    seen.Seen.Add((w.Tick, intent.Slot));
                    intent.Slot = 0;     // the module clears what it took
                }
            });
        }
        var w = b.Build();
        player = MovementServices.SpawnControlled(w, new(0, 0, 0.01f), 0f, Locomotion.Sandbox);
        for (int i = 0; i < 10; i++) MovementServices.Step(w, ActionFrame.Idle);
        return w;
    }

    static ActionFrame F(Buttons held = Buttons.None) => new(Vector2.Zero, Vector2.Zero, held, new Vector2(0.5f, 0.5f));

    static void Run(World w, int ticks, ActionFrame f) { for (int i = 0; i < ticks; i++) MovementServices.Step(w, f); }

    [Fact]
    public void The_slot_buttons_are_the_next_free_bits_and_number_one_to_eight()
    {
        Assert.Equal(1u << 17, (uint)Buttons.Slot1);
        Assert.Equal(1u << 24, (uint)Buttons.Slot8);
        Assert.Equal(0u, (uint)(QuickSlotButtons.All & (Buttons)((1u << 17) - 1)));    // none of the older buttons
        for (int n = 1; n <= QuickSlots.Count; n++)
        {
            Assert.Equal(n, QuickSlotButtons.Lowest(QuickSlotButtons.Of(n)));
            Assert.Equal($"slot{n}", QuickSlotButtons.Actions[n - 1]);
        }
        Assert.Equal(Buttons.None, QuickSlotButtons.Of(0));
        Assert.Equal(Buttons.None, QuickSlotButtons.Of(9));
        Assert.Equal(0, QuickSlotButtons.Lowest(Buttons.Attack | Buttons.Jump));
        Assert.Equal(3, QuickSlotButtons.Lowest(Buttons.Slot7 | Buttons.Slot3 | Buttons.Jump));
    }

    [Fact]
    public void Each_slot_keys_press_makes_one_intent_for_one_tick_and_held_makes_no_second()
    {
        var uses = new Uses();
        var w = Flat(out _, uses: uses);
        for (int n = 1; n <= QuickSlots.Count; n++)
        {
            uses.Seen.Clear();
            long down = w.Tick;      // the tick being stepped, as the systems see it
            Run(w, 30, F(QuickSlotButtons.Of(n)));      // held a quarter of a second
            Run(w, 5, F());
            Assert.Equal(new[] { (down, n) }, uses.Seen);
        }
        // Pressed again after being let go: a second use.
        uses.Seen.Clear();
        Run(w, 2, F(Buttons.Slot4));
        Run(w, 2, F());
        Run(w, 2, F(Buttons.Slot4));
        Assert.Equal(new[] { 4, 4 }, uses.Seen.Select(s => s.Slot));
    }

    [Fact]
    public void Two_slots_pressed_together_use_the_lower_and_one_pressed_while_another_is_held_is_its_own()
    {
        var uses = new Uses();
        var w = Flat(out _, uses: uses);
        Run(w, 3, F(Buttons.Slot5 | Buttons.Slot2));
        Run(w, 3, F(Buttons.Slot5 | Buttons.Slot2 | Buttons.Slot7));
        Run(w, 3, F());
        Assert.Equal(new[] { 2, 7 }, uses.Seen.Select(s => s.Slot));
    }

    [Fact]
    public void Without_a_reader_the_intent_is_the_press_on_its_tick_and_nothing_after()
    {
        var w = Flat(out var p, reader: false);
        Assert.False(w.Has<PowerIntent>(p));                // given at the first press, not before
        MovementServices.Step(w, F(Buttons.Slot6));
        Assert.Equal(6, w.Get<PowerIntent>(p).Slot);
        MovementServices.Step(w, F(Buttons.Slot6));
        Assert.Equal(0, w.Get<PowerIntent>(p).Slot);
        MovementServices.Step(w, F());
        Assert.Equal(0, w.Get<PowerIntent>(p).Slot);
    }

    [Fact]
    public void A_slot_pressed_with_the_pointer_free_is_used_and_attack_is_not()
    {
        // The freed pointer keeps Mouse Left for its click, so no attack; a slot is a key, so it is used.
        var uses = new Uses();
        var w = Flat(out var p, uses: uses);
        Run(w, 5, F(Buttons.FreePointer));
        Run(w, 2, F(Buttons.FreePointer | Buttons.Slot1));
        Assert.True(w.Get<CameraRig>(p).PointerFree);
        Assert.Equal(new[] { 1 }, uses.Seen.Select(s => s.Slot));
    }

    [Fact]
    public void A_world_without_the_powers_contract_is_unchanged_digest_for_digest_by_the_slot_keys()
    {
        // The movement alone (a world with no module that reads the intent) has no PowerIntent store, so the slot
        // buttons change nothing in it and an older tape's digests stand.
        var a = Flat(out var pa, contract: false);
        var b = Flat(out var pb, contract: false);
        Assert.False(a.TryStore<PowerIntent>(out _));
        var random = new Random(7);
        for (int i = 0; i < 600; i++)
        {
            var held = (Buttons)(random.Next(0, 3) == 0 ? (uint)Buttons.Sprint : 0u);
            var move = new Vector2(0f, i % 200 < 150 ? 1f : 0f);
            var look = new Vector2(i % 90 == 0 ? 0.3f : 0f, 0f);
            int slot = 1 + random.Next(0, 8);
            var slots = random.Next(0, 4) == 0 ? QuickSlotButtons.Of(slot) : Buttons.None;
            MovementServices.Step(a, new ActionFrame(move, look, held, new Vector2(0.5f, 0.5f)));
            MovementServices.Step(b, new ActionFrame(move, look, held | slots, new Vector2(0.5f, 0.5f)));
            Assert.Equal(a.Digest(), b.Digest());
        }
        Assert.Equal(a.Get<Body>(pa).Feet, b.Get<Body>(pb).Feet);
    }

    [Fact]
    public void A_tape_carries_the_slot_buttons_and_replays_them_with_the_same_digests_and_uses()
    {
        var uses = new Uses();
        var w = Flat(out _, uses: uses);
        var tape = new Tape();
        var frames = new List<ActionFrame>();
        for (int i = 0; i < 360; i++)
        {
            var held = (i % 40) is >= 5 and < 9 ? QuickSlotButtons.Of(1 + i / 40 % 8) : Buttons.None;
            if (i % 120 < 60) held |= Buttons.Walk;
            frames.Add(new ActionFrame(new Vector2(0f, 1f), Vector2.Zero, held, new Vector2(0.5f, 0.5f)));
        }
        foreach (var f in frames)
        {
            MovementServices.Step(w, f);
            tape.Frames.Add(f);
            if (w.Tick % Tape.DigestEvery == 0) tape.Digests[tape.Frames.Count] = w.Digest();
        }
        Assert.Equal(9, uses.Seen.Count);      // one use for each of the nine presses
        var path = Path.Combine(Path.GetTempPath(), $"vitomir-slots-{Environment.ProcessId}.tape");
        try
        {
            tape.Write(path);
            var back = Tape.Read(path);
            Assert.Equal(frames.Select(f => f.Held), back.Frames.Select(f => f.Held));
            var again = new Uses();
            var r = Flat(out _, uses: again);
            for (int i = 0; i < back.Frames.Count; i++)
            {
                MovementServices.Step(r, back.Frames[i]);
                if (back.Digests.TryGetValue(i + 1, out ulong d)) Assert.True(d == r.Digest(), $"the replay parted at frame {i + 1}");
            }
            Assert.Equal(uses.Seen, again.Seen);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_frame_from_an_older_tape_has_no_slot_bits_and_reads_back_as_it_was_written()
    {
        // A tape earlier wrote Held with the bits up to the pointer's toggle (1 << 16): it reads back the same, and no
        // slot is pressed by it.
        var old = new Tape();
        var all = (Buttons)((1u << 17) - 1);
        old.Frames.Add(new ActionFrame(new Vector2(0.5f, 1f), new Vector2(0.01f, -0.02f), all, new Vector2(0.25f, 0.75f)));
        old.Frames.Add(ActionFrame.Idle);
        var path = Path.Combine(Path.GetTempPath(), $"vitomir-oldtape-{Environment.ProcessId}.tape");
        try
        {
            old.Write(path);
            var back = Tape.Read(path);
            Assert.Equal(old.Frames, back.Frames);
            Assert.All(back.Frames, f => Assert.Equal(0, QuickSlotButtons.Lowest(f.Held)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
