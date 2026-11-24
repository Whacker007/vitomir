// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The body's tempo (<see cref="CombatTempo"/>): a skilled swing plays faster, so its <c>Strike</c> moment and its
/// contact come sooner by its rate and it still meets the body it should; the busy time after the strike shortens by the
/// recovery rate, so a buffered blow begins sooner; the block rises sooner by its rate; and a world with no tempo — the
/// component unregistered, or registered and unset, or set to 1.1's timing — plays every blow tick for tick as the first module did.
/// Christof with the broadsword on a floor, against a townsman. Skips without the 1.1 install (VTMR_GAME_DIR).
/// </summary>
public sealed class CombatTempoTests(ITestOutputHelper output)
{
    static readonly Lazy<OneOneLibrary?> Library = new(() => GameInstall.TryOpen() is { } i ? new OneOneLibrary(i) : null);

    static readonly Dictionary<string, OneOneCharacter> Loaded = [];

    static OneOneCharacter Character(string name, string? weapon = null)
    {
        Assert.SkipUnless(Library.Value is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        lock (Loaded)
        {
            string key = name + "/" + weapon;
            if (!Loaded.TryGetValue(key, out var c))
                Loaded[key] = c = Library.Value!.Character(name, weapon) ?? throw new Xunit.Sdk.XunitException($"{name} did not load");
            return c;
        }
    }

    static int Ticks(float seconds) => (int)MathF.Round(seconds * World.TicksPerSecond);

    /// <summary>A small ring: Christof (broadsword) facing a townsman 1.3 m away; contact on; the tempo store when <paramref name="tempo"/>.</summary>
    sealed class Ring
    {
        public required World World { get; init; }
        public EntityId A, T;
        public readonly List<AttackBegun> Begun = [];
        public readonly List<StrikeMoment> Strikes = [];
        public readonly List<ContactMade> Contacts = [];
        public readonly List<BlockRaised> Raised = [];

        public void Idle(int ticks, Action<int>? each = null)
        {
            for (int i = 0; i < ticks; i++)
            {
                MovementServices.Step(World, ActionFrame.Idle);
                Begun.AddRange(World.Events<AttackBegun>().ThisTick);
                Strikes.AddRange(World.Events<StrikeMoment>().ThisTick);
                Contacts.AddRange(World.Events<ContactMade>().ThisTick);
                Raised.AddRange(World.Events<BlockRaised>().ThisTick);
                each?.Invoke(i);
            }
        }

        /// <summary>A tap of attack (made on release), aimed level.</summary>
        public void Tap()
        {
            ref var intent = ref World.Get<CombatIntent>(A);
            intent.Aims = true;
            intent.AimPitch = 0f;
            intent.Attack = true;
            Idle(1);
            World.Get<CombatIntent>(A).Attack = false;
        }
    }

    static Ring Make(bool tempo, CombatTempo? set = null, bool target = true)
    {
        var assets = new CharacterAssets();
        int christof = OneOneLibrary.Register(assets, Character("Christof", "BroadSword"));
        int townsman = OneOneLibrary.Register(assets, Character("Civ12male"));
        var level = new TestLevel().Floor(-20, -20, 20, 20).Build();
        var b = new WorldBuilder(7);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = false });
        b.Animation(assets);
        b.HitVolumes();
        BodyMotionSystem.Register(b);
        CombatServices.Register(b);
        if (tempo) CombatTempos.Register(b);
        // The blade through the body (the stop off), so a blow that meets him still reaches its Strike mark to be timed.
        ContactServices.Register(b, new ContactConfig { Stop = new BladeStopRule { Enabled = false } });
        var w = b.Build();
        w.Service<HitVolumes>().Prepare(assets);
        ContactServices.Prepare(w);
        EntityId Put(int kind, float x, float y, float yaw)
        {
            var e = MovementServices.SpawnBody(w, new(x, y, 0.01f), yaw * DetMath.DegToRad, Locomotion.Sandbox);
            w.Set(e, CharacterAnimation.For(assets, kind));
            w.Set(e, MotionInput.Standing);
            CombatServices.Arm(w, e);
            return e;
        }
        var r = new Ring { World = w };
        r.A = Put(christof, 0, 0, 90);
        r.T = target ? Put(townsman, 0, 1.3f, -90) : EntityId.None;
        if (set is { } s) w.Set(r.A, s);
        r.Idle(30);
        return r;
    }

    [Fact]
    public void The_busy_time_splits_at_the_strike_mark_the_swing_rate_before_it_and_the_recovery_rate_after()
    {
        // The broadsword's quick blow: a 29-frame clip stretched to 1.1 s (rate 0.879), its strike at frame 15, busy 1.1 s.
        var motion = new ActionMotion(0, 29f / 30f / 1.1f, 15f / 30f, 1.1f, -1f, false, "swordslash");
        float before = 15f / 30f / motion.Rate;                                                // 0.569 s to the strike
        Assert.Equal(1.1f, CombatTempo.Normal.BusyFor(motion));
        Assert.Equal(1.1f, default(CombatTempo).BusyFor(motion));                               // unset: 1.1's timing
        Assert.Equal(1.1f / 1.3f, new CombatTempo(1.3f, 1.3f, 1f).BusyFor(motion), 5);
        Assert.Equal(before / 2f + (1.1f - before), new CombatTempo(2f, 1f, 1f).BusyFor(motion), 5);
        Assert.Equal(before + (1.1f - before) / 2f, new CombatTempo(1f, 2f, 1f).BusyFor(motion), 5);
        Assert.Equal(1.1f / 1.5f, new CombatTempo(1.5f, 0.5f, 1f).BusyFor(motion with { StrikeSeconds = -1f }), 5);   // no mark: all swing
        Assert.Equal(1f, CombatTempo.Of(0f));
        Assert.Equal(1f, CombatTempo.Of(-3f));
    }

    static (long Strike, ContactMade Contact, float ClipAtContact) Blow(Ring r)
    {
        r.Tap();
        float clip = -1f;
        r.Idle(Ticks(1.3f), _ => { if (clip < 0f && r.Contacts.Count > 0) clip = r.World.Get<CharacterAnimation>(r.A).ActionTime; });
        var begun = Assert.Single(r.Begun);
        var strike = Assert.Single(r.Strikes);
        var c = r.Contacts.First();
        return (strike.Tick - begun.Tick, c with { Tick = c.Tick - begun.Tick }, clip);
    }

    [Fact]
    public void Without_a_tempo_a_blow_is_1_1_s_tick_for_tick_registered_or_not()
    {
        var none = Blow(Make(tempo: false));
        var unset = Blow(Make(tempo: true));
        var normal = Blow(Make(tempo: true, CombatTempo.Normal));
        Assert.Equal(none, unset);
        Assert.Equal(none, normal);
        Assert.Equal(ContactMet.Body, none.Contact.Met);
        output.WriteLine($"1.1's timing: strike {none.Strike} ticks after the blow begins, contact at {none.Contact.Tick} ({none.Contact.Site}), clip {none.ClipAtContact:0.000} s");
    }

    [Fact]
    public void A_skilled_swing_strikes_and_meets_sooner_by_its_rate_and_still_meets_the_body()
    {
        var plain = Blow(Make(tempo: true, CombatTempo.Normal));
        var fast = Blow(Make(tempo: true, new CombatTempo(1.3f, 1f, 1f)));
        output.WriteLine($"strike at {plain.Strike} → {fast.Strike} ticks; contact at {plain.Contact.Tick} → {fast.Contact.Tick} ({plain.Contact.Site} → {fast.Contact.Site}), clip {plain.ClipAtContact:0.000} → {fast.ClipAtContact:0.000} s, {plain.Contact.ImpactSpeed:0.0} → {fast.Contact.ImpactSpeed:0.0} m/s");
        Assert.InRange(fast.Strike, plain.Strike / 1.3f - 1.5f, plain.Strike / 1.3f + 1.5f);   // the mark is clip time: 1.3 times sooner
        Assert.InRange(fast.Contact.Tick, plain.Contact.Tick / 1.3f - 2f, plain.Contact.Tick / 1.3f + 2f);
        Assert.Equal(ContactMet.Body, fast.Contact.Met);                                         // the faster swept blade still meets him
        Assert.Equal(fast.Contact.Target, plain.Contact.Target);
        Assert.InRange(MathF.Abs(fast.ClipAtContact - plain.ClipAtContact), 0f, 0.06f);         // at about the same moment of the swing
        Assert.True(fast.Contact.ImpactSpeed > plain.Contact.ImpactSpeed, "a faster swing meets faster");
    }

    [Fact]
    public void Recovery_lets_a_buffered_blow_begin_sooner_and_the_block_rise_sooner()
    {
        static long Next(CombatTempo t)
        {
            var r = Make(tempo: true, t, target: false);
            r.Tap();
            r.Idle(Ticks(0.2f));
            r.Tap();                                                                              // buffered: begins as the busy time ends
            r.Idle(Ticks(1.4f));
            Assert.Equal(2, r.Begun.Count);
            return r.Begun[1].Tick - r.Begun[0].Tick;
        }
        long plain = Next(CombatTempo.Normal), quick = Next(new CombatTempo(1f, 2f, 1f));
        output.WriteLine($"the next blow {plain} → {quick} ticks after the first (recovery 2)");
        Assert.InRange(plain, Ticks(1.1f) - 1, Ticks(1.1f) + 1);                                // RELOADTIME 1.1 s
        float before = 15f / 30f / (29f / 30f / 1.1f);
        Assert.InRange(quick, Ticks(before + (1.1f - before) / 2f) - 1, Ticks(before + (1.1f - before) / 2f) + 1);

        static (int Full, float Clip) Rise(CombatTempo t)
        {
            var r = Make(tempo: true, t, target: false);
            r.World.Get<CombatIntent>(r.A).Block = true;
            int full = -1;
            float clip = 0f;
            r.Idle(Ticks(0.3f), i =>
            {
                var a = r.World.Get<CharacterAnimation>(r.A);
                if (i == 5) clip = a.ActionTime;
                if (full < 0 && a.Action == ActionRole.Block && a.ActionWeight >= 1f) full = i;
            });
            Assert.Single(r.Raised);
            return (full, clip);
        }
        var (slow, slowClip) = Rise(CombatTempo.Normal);
        var (fast, fastClip) = Rise(new CombatTempo(1f, 1f, 1.5f));
        output.WriteLine($"the block at full weight after {slow} → {fast} ticks; its clip 5 ticks in {slowClip:0.000} → {fastClip:0.000} s");
        Assert.InRange(fast, slow / 1.5f - 1f, slow / 1.5f + 1f);
        Assert.Equal(slowClip * 1.5f, fastClip, 3);
    }
}
