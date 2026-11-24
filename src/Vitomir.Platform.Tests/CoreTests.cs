// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Tests;

public sealed class CoreTests
{
    struct Pos { public float X; public int Tag; }
    struct Note { public int Value; }

    [Fact]
    public void A_stale_id_never_reaches_the_entity_made_in_its_slot()
    {
        var w = new WorldBuilder(1).Component<Pos>().Build();
        var a = w.Create();
        w.Set(a, new Pos { X = 1 });
        w.Destroy(a);
        Assert.True(w.Alive(a));            // destroyed at the end of the tick
        w.Step();
        Assert.False(w.Alive(a));
        var b = w.Create();
        Assert.Equal(a.Index, b.Index);
        Assert.NotEqual(a.Generation, b.Generation);
        Assert.False(w.Store<Pos>().Has(a));
        Assert.False(w.Store<Pos>().Has(b));
    }

    [Fact]
    public void A_store_keeps_its_order_by_fixed_rules_add_at_the_end_remove_by_moving_the_last_in()
    {
        var s = new Store<Pos>();
        var ids = Enumerable.Range(0, 5).Select(i => new EntityId(i, 1)).ToArray();
        foreach (var (e, i) in ids.Select((e, i) => (e, i))) s.Set(e, new Pos { Tag = i });
        s.Remove(ids[1]);
        Assert.Equal([0, 4, 2, 3], s.Data.ToArray().Select(p => p.Tag));
        Assert.Equal(4, s.Get(ids[4]).Tag);
        s.Get(ids[2]).Tag = 22;
        Assert.Equal(22, s.Get(ids[2]).Tag);
    }

    [Fact]
    public void Systems_run_by_phase_then_order_then_name()
    {
        var log = new List<string>();
        var b = new WorldBuilder(1);
        b.System(Phase.Post, 0, "z", _ => log.Add("post"));
        b.System(Phase.Move, 5, "b", _ => log.Add("move5b"));
        b.System(Phase.Move, 5, "a", _ => log.Add("move5a"));
        b.System(Phase.Move, 1, "c", _ => log.Add("move1"));
        b.System(Phase.Input, 9, "x", _ => log.Add("input"));
        var w = b.Build();
        w.Step();
        Assert.Equal(["input", "move1", "move5a", "move5b", "post"], log);
        Assert.Equal(1, w.Tick);
    }

    [Fact]
    public void An_event_is_seen_by_every_phase_once_this_tick_and_the_next()
    {
        var seenEarly = new List<int>(); var seenLate = new List<int>();
        var b = new WorldBuilder(1).Event<Note>();
        b.System(Phase.Input, 0, "early", w => seenEarly.AddRange(w.Events<Note>().LastTick.Select(n => n.Value)));
        b.System(Phase.Act, 0, "publish", w => { if (w.Tick == 0) w.Events<Note>().Publish(new Note { Value = 7 }); });
        b.System(Phase.Post, 0, "late", w => seenLate.AddRange(w.Events<Note>().ThisTick.Select(n => n.Value)));
        var world = b.Build();
        world.Step(); world.Step(); world.Step();
        Assert.Equal([7], seenEarly);
        Assert.Equal([7], seenLate);
    }

    [Fact]
    public void Two_worlds_doing_the_same_things_have_the_same_digest_and_a_difference_shows()
    {
        ulong Run(float nudge)
        {
            var b = new WorldBuilder(42).Component<Pos>();
            b.System(Phase.Move, 0, "drift", w =>
            {
                var rng = w.Random.For("drift");
                foreach (ref var p in w.Store<Pos>().Data) p.X += rng.NextFloat(-1, 1) + nudge;
            });
            var w = b.Build();
            for (int i = 0; i < 10; i++) w.Set(w.Create(), new Pos { Tag = i });
            for (int t = 0; t < 120; t++) w.Step();
            return w.Digest();
        }
        Assert.Equal(Run(0), Run(0));
        Assert.NotEqual(Run(0), Run(1e-3f));
    }

    [Fact]
    public void A_random_stream_depends_only_on_the_seed_and_its_purpose()
    {
        var a = new RandomStreams(5); var b = new RandomStreams(5);
        _ = a.For("other").NextULong();               // another purpose drawing first changes nothing
        Assert.Equal(b.For("walk").NextULong(), a.For("walk").NextULong());
        Assert.NotEqual(new RandomStreams(5).For("walk").NextULong(), new RandomStreams(6).For("walk").NextULong());
        var r = new Rng(1);
        for (int i = 0; i < 1000; i++) Assert.InRange(r.NextD10(), 1, 10);
        for (int i = 0; i < 1000; i++) Assert.InRange(r.NextFloat(), 0f, 0.99999994f);
    }

    [Fact]
    public void Xoshiro_gives_the_reference_numbers()
    {
        // xoshiro256** seeded by SplitMix64 from 0: the reference implementation's first outputs.
        var r = new Rng(0);
        Assert.Equal(0x99EC5F36CB75F2B4UL, r.NextULong());
        Assert.Equal(0xBF6E1F784956452AUL, r.NextULong());
    }
}
