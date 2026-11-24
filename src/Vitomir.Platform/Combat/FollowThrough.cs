// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: 1.1's blows never meet anything (a reach test and a roll), so they never stop.
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Combat;

/// <summary>Where a blade's playback is held (<see cref="BladeHold"/>).</summary>
public enum BladeHoldState : byte
{
    /// <summary>Not held: the swing plays (or none does).</summary>
    None = 0,
    /// <summary>Bitten into a body: held at the contact while the rules decide, then it goes on or is brought back.</summary>
    Bite = 1,
    /// <summary>Springing back off a shield, a parrying blade or the level, before the bring-back.</summary>
    Rebound = 2,
    /// <summary>Being brought back to guard over the rest of the swing's time.</summary>
    BringBack = 3,
    /// <summary>Met at a guard and held there for the rules' verdict (<see cref="GuardResolved"/>), the same tick.</summary>
    Guard = 4,
    /// <summary>
    /// Bitten and stopped, its swing's extra blows still to fall with the re-strike motion on
    /// (<see cref="FlurrySettings.Motion"/>): the clip pulled back and driven through the strike again for each, arriving at the
    /// contact's pose at each blow's moment (<see cref="FlurryBlows.Restrike"/>), then brought back to guard.
    /// </summary>
    Restrike = 5,
}

/// <summary>What a blade did at a contact (<see cref="SwingFollowThrough"/>).</summary>
public enum BladeOutcome : byte
{
    /// <summary>It stopped in the body it met (the rules did not let it go on), and is brought back to guard.</summary>
    Stops = 0,
    /// <summary>It sprang back off a shield, a parrying blade or the level, and is brought back to guard.</summary>
    Rebounds = 1,
    /// <summary>It went on past a part it cut off (<see cref="HarmResolved.Continues"/>), to whatever it meets next.</summary>
    GoesOn = 2,
}

/// <summary>
/// A fighter's blade held where it met (digested): which swing (<see cref="Combatant.Swing"/>), what the hold is, what
/// it met (and whom, which part), the contact awaiting the rules' word and that word, the tick the hold began, the rate the
/// swing played at when it met (it goes on at it), the real seconds the swing had left then (the bring-back's time), the
/// chain's length so far, and the part a going-on blade cut through. Absent, or of another swing, is no hold.
/// </summary>
public struct BladeHold
{
    public int Swing;
    public BladeHoldState State;
    public ContactMet Met;
    public BodyRegion Region;
    public BodyRegion Severed;
    public EntityId Whom;
    public long ContactId;
    public long Since;
    public float Rate;
    public float Rest;
    public int Chain;
    int verdict;

    /// <summary>The swing whose <c>Strike</c> mark was told before its clip was put back (so a blade going on does not tell it twice).</summary>
    public int StruckSwing;

    /// <summary>The rules' word on the bitten contact: 0 not yet, 1 it goes on, 2 it stops.</summary>
    public int Verdict { readonly get => verdict; set => verdict = value; }

    // At the end: the guard's verdict on a blade held at a guard.

    /// <summary>The rules' verdict on a guard met (<see cref="GuardVerdict"/>) once it came (<see cref="GuardKnown"/>).</summary>
    public GuardVerdict GuardVerdict;
    /// <summary>The parried attacker's recovery lengthened by (s), and the overpowered guard's broken time (s), as the rules said.</summary>
    public float Riposte, Broken;
    /// <summary>The tip's speed when it met a guard (<see cref="ContactMade.TipSpeed"/>), for the rebound after the verdict.</summary>
    public float Speed;
    int guardKnown;

    /// <summary>Whether the rules' verdict on the guard met has come.</summary>
    public bool GuardKnown { readonly get => guardKnown != 0; set => guardKnown = value ? 1 : 0; }

    // ---- the rebound scaled by the blow ----

    /// <summary>How long this rebound springs back (real s): <see cref="BladeStopRule.ReboundScaledSeconds"/>, or the earlier build's <see cref="BladeStopRule.ReboundSeconds"/>.</summary>
    public float ReboundFor;

    /// <summary>What the contact held at a guard carried, for the rebound after the verdict: its impulse (N·s), its normal (out of what was met) and material.</summary>
    public float Impulse;
    public System.Numerics.Vector3 Normal;
    public ContactMaterial Material;
}

/// <summary>
/// A blade stopped, sprang back or went on (for the trace and the log): who swung, which swing, the tick, what became
/// of it, what it met (a body — whom and which part —, a shield, a parrying blade, the level), the chain's length (the
/// swing's contacts so far, this one counted), the contact, and the bring-back's length (real s; 0 for a blade that goes on,
/// and for one cut short at once, <see cref="BladeStopRule.StopFreesOwnerAtOnce"/>). For a blade that goes on,
/// <paramref name="Region"/> is the part it cut through.
/// </summary>
public readonly record struct SwingFollowThrough(EntityId Attacker, int Swing, long Tick, BladeOutcome Outcome, ContactMet Met, EntityId Whom,
    BodyRegion Region, int Chain, long ContactId, float ReadySeconds,
    float ReboundMetres = 0f);   // at the end: a rebound's tip travel asked for (m), 0 for the rest

/// <summary>
/// A blocked blade's owner stunned (Bannerlord's "attacker stun"): who, the tick, the swing, how long (real s
/// from the meeting; no attack begins until it is over, a guard and a dodge allowed), the contact, and whether the blade was
/// parried or deflected (the longer stun).
/// </summary>
public readonly record struct AttackerStunned(EntityId Attacker, long Tick, int Swing, float Seconds, long ContactId, bool Parried);

/// <summary>
/// The blade's follow-through (<see cref="Phase.Resolve"/>, after the rules' harm and the platform's death,
/// <see cref="FollowThroughServices.Order"/>): <b>the platform follows, the rules decide</b>. Contact bit a blade into a body
/// (<see cref="FollowThrough.Bite"/>: its clip held at the contact) or sprang it back off a shield, a blade or the level
/// (<see cref="FollowThrough.Rebound"/>); this system reads the rules' word on the bitten contact — the
/// <see cref="HarmResolved"/> of its own blow (<c>Flurry == 0</c>): <see cref="HarmResolved.Continues"/> lets it go on, anything
/// else (no line at all: no rules module, a protected person, a dead body) stops it — and when the hold's time is up
/// (<see cref="BladeStopRule.BiteSeconds"/>, <see cref="BladeStopRule.ReboundSeconds"/>) it either lets the clip play on at its
/// rate (the next thing met is a new contact of the same swing, <see cref="ContactMade.Chain"/> + 1) or begins the
/// <b>bring-back to guard</b> from where the blade is, over the rest of the swing's time clamped to
/// <see cref="BladeStopRule.ReadySecondsLeast"/>–<see cref="BladeStopRule.ReadySecondsMost"/> (<see cref="BringBackKind"/>).
/// The owner swings again when its busy time ends, as ever (the whole swing's time: 1.1's <c>RELOADTIME</c>).
/// <para>A part severed (<see cref="PartSevered"/>): the poses are made again (the part drawn collapsed,
/// <see cref="PartCollapse"/>), and a person who lost the weapon hand has the swing in hand cut short and the waiting blow
/// dropped (the attacks refuse them from then on, <see cref="AttackRefusal.NoWeaponHand"/>).</para>
/// Deterministic, the stores' and events' order; nothing allocated per tick.
/// </summary>
public sealed class FollowThroughSystem : ISystem
{
    static readonly ContactConfig Defaults = new();

    public void Run(World world)
    {
        var config = world.TryService<ContactConfig>(out var cc) ? cc : Defaults;
        var rule = config.Stop;
        long tick = world.Tick;
        var holds = world.Store<BladeHold>();
        var anims = world.Store<CharacterAnimation>();
        var fighters = world.Store<Combatant>();
        world.TryService<CharacterPoses>(out var poses);

        // A part severed: drawn collapsed (the poses made again), and a person without the weapon hand stops attacking.
        if (world.TryService<EventQueue<PartSevered>>(out var severed) && severed.ThisTick.Count > 0)
        {
            poses?.Invalidate();
            var lost = severed.ThisTick;
            for (int i = 0; i < lost.Count; i++)
            {
                var who = lost[i].Who;
                if (!HeldItems.WeaponHandGone(world, who)) continue;
                ref var c = ref fighters.Find(who);
                if (!Unsafe.IsNullRef(ref c)) { c.Pending = AttackStrength.None; c.Buffered = false; c.AttackHeld = -1f; }
                ref var anim = ref anims.Find(who);
                if (!Unsafe.IsNullRef(ref anim) && ActionRoles.IsAttack(anim.Action) && !anim.ActionEnding) CombatServices.CutShort(ref anim, 1f);
                ref var h = ref holds.Find(who);
                if (!Unsafe.IsNullRef(ref h)) h.State = BladeHoldState.None;
            }
        }

        // The rules' word on each bitten contact: its own blow's line.
        if (world.TryService<EventQueue<HarmResolved>>(out var harm))
        {
            var lines = harm.ThisTick;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.Flurry != 0) continue;
                ref var h = ref holds.Find(line.Attacker);
                if (Unsafe.IsNullRef(ref h) || h.State != BladeHoldState.Bite || h.ContactId != line.ContactId || h.Verdict != 0) continue;
                h.Verdict = line.Continues ? 1 : 2;
                if (line.Continues) h.Severed = line.Severed;
            }
        }

        // The rules' verdict on each blade held at a guard.
        if (world.TryService<EventQueue<GuardResolved>>(out var guarded))
        {
            var lines = guarded.ThisTick;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                ref var h = ref holds.Find(line.Attacker);
                if (Unsafe.IsNullRef(ref h) || h.State != BladeHoldState.Guard || h.ContactId != line.ContactId || h.GuardKnown) continue;
                h.GuardKnown = true;
                h.GuardVerdict = line.Verdict;
                h.Riposte = line.RiposteSeconds;
                h.Broken = line.BrokenSeconds;
            }
        }

        var ents = holds.Entities;
        var data = holds.Data;
        for (int i = 0; i < ents.Length; i++)
        {
            ref var h = ref data[i];
            if (h.State == BladeHoldState.None) continue;
            var a = ents[i];
            ref var anim = ref anims.Find(a);
            ref var c = ref fighters.Find(a);
            // Another action took the channel (a hurt, a death, a new swing): the hold is over.
            if (Unsafe.IsNullRef(ref anim) || Unsafe.IsNullRef(ref c) || !ActionRoles.IsAttack(anim.Action) || c.Swing != h.Swing
                || (anim.ActionEnding && h.State != BladeHoldState.BringBack))
            {
                h.State = BladeHoldState.None;
                continue;
            }
            float held = (tick - h.Since) * World.TickSeconds;
            switch (h.State)
            {
                case BladeHoldState.Bite:
                    if (h.Verdict == 0) h.Verdict = 2;   // no line for the contact: the blade stops where it met
                    if (h.Since == tick)
                    {
                        if (h.Verdict == 1)
                            world.Events<SwingFollowThrough>().Publish(new SwingFollowThrough(a, h.Swing, tick, BladeOutcome.GoesOn, h.Met, h.Whom, h.Severed, h.Chain, h.ContactId, 0f));
                        else
                        {
                            float ready = rule.StopFreesOwnerAtOnce ? 0f : ReadyFor(rule, h.Rest - rule.BiteSeconds);
                            world.Events<SwingFollowThrough>().Publish(new SwingFollowThrough(a, h.Swing, tick, BladeOutcome.Stops, h.Met, h.Whom, h.Region, h.Chain, h.ContactId, ready));
                            StopSwing(world, a);
                        }
                    }
                    // A stopped blade whose swing carries extra blows with the re-strike motion on
                    // re-strikes them from the contact on (no bite's pause), then is brought back.
                    if (h.Verdict == 2 && !rule.StopFreesOwnerAtOnce && FlurryBlows.Restrikes(world, a, h.Swing))
                    {
                        h.State = BladeHoldState.Restrike;
                        goto case BladeHoldState.Restrike;
                    }
                    if (held + 1e-6f < rule.BiteSeconds) break;
                    if (h.Verdict == 1)
                    {
                        // Goes on: the clip plays on at its rate; contact sweeps it again from where it bit.
                        anim.ActionRate = h.Rate;
                        h.State = BladeHoldState.None;
                    }
                    else if (rule.StopFreesOwnerAtOnce)
                    {
                        CombatServices.CutShort(ref anim, 1f);
                        h.State = BladeHoldState.None;
                    }
                    else BringBack(ref h, ref anim, rule, held, tick);
                    poses?.Invalidate();
                    break;
                case BladeHoldState.Rebound:
                    if (held + 1e-6f < (h.ReboundFor > 0f ? h.ReboundFor : rule.ReboundSeconds) && anim.ActionTime > 0f) break;   // the hold's own length
                    BringBack(ref h, ref anim, rule, held, tick);
                    poses?.Invalidate();
                    break;
                case BladeHoldState.Restrike:
                    // The clip driven to the re-strike's time for the next tick; the blows done (or ended), back to guard.
                    if (FlurryBlows.Restrike(world, a, h.Swing, tick, h.Rate, out float clip))
                        anim.ActionRate = (clip - anim.ActionTime) / World.TickSeconds;
                    else
                    {
                        anim.ActionRate = 0f;
                        BringBack(ref h, ref anim, rule, held, tick);
                        poses?.Invalidate();
                    }
                    break;
                case BladeHoldState.BringBack:
                    // By the clip reversed it ends itself at the clip's start; blended, it fades by itself: nothing to drive.
                    if (anim.ActionEnding) h.State = BladeHoldState.None;
                    break;
                case BladeHoldState.Guard:
                    // The rules' verdict on the guard met (none come: held, the earlier build's rebound).
                    Judge(world, a, ref h, ref anim, ref c, rule, tick);
                    poses?.Invalidate();
                    break;
            }
        }
    }

    static readonly GuardSettings DefaultGuard = new();

    /// <summary>
    /// What a blade held at a guard does on the rules' verdict — <see cref="GuardVerdict.Overpowered"/> and
    /// <see cref="GuardVerdict.ClashDominates"/>: it bites there and goes on (a bite whose verdict is "goes on"), the guard's
    /// owner's guard and blade met no more by this swing (<see cref="SwingContacts.Through"/>), and an overpowered guard
    /// lowered and broken for the rules' time (<see cref="GuardBroken"/>); <see cref="GuardVerdict.Parried"/>: it springs back
    /// <see cref="GuardSettings.ParryReboundScale"/> times as far and its owner's recovery is lengthened by the riposte window (no
    /// free hit: the defender must still swing); <see cref="GuardVerdict.ClashDeflected"/>: it springs back as far
    /// as a parried one, no riposte; anything else (held, a clash's mutual stop, no verdict at all): the earlier build's rebound.
    /// </summary>
    static void Judge(World world, EntityId a, ref BladeHold h, ref CharacterAnimation anim, ref Combatant c, BladeStopRule rule, long tick)
    {
        var g = world.TryService<GuardSettings>(out var gs) ? gs : DefaultGuard;
        var verdict = h.GuardKnown ? h.GuardVerdict : GuardVerdict.Held;
        var swings = world.Store<SwingContacts>();
        ref var sc = ref swings.Find(a);
        if (verdict is GuardVerdict.Overpowered or GuardVerdict.ClashDominates)
        {
            if (!Unsafe.IsNullRef(ref sc)) sc.Through = h.Whom;
            if (verdict == GuardVerdict.Overpowered) Break(world, h.Whom, a, h.ContactId, h.Broken, tick);
            h.State = BladeHoldState.Bite;
            h.Verdict = 1;
            h.Severed = BodyRegion.None;
            world.Events<SwingFollowThrough>().Publish(new SwingFollowThrough(a, h.Swing, tick, BladeOutcome.GoesOn, h.Met, h.Whom, BodyRegion.None, h.Chain, h.ContactId, 0f));
            return;
        }
        if (!Unsafe.IsNullRef(ref sc)) sc.Stopped = true;
        bool turned = verdict is GuardVerdict.Parried or GuardVerdict.ClashDeflected;
        float scale = turned ? g.ParryReboundScale : 1f;
        // The contact's impulse, normal and material kept for the rebound (the earlier build's made carried only the tip's speed).
        var made = new ContactMade(h.ContactId, a, h.Swing, h.Whom, h.Met, null, h.Material, tick, default, h.Normal, 0f, h.Impulse, false, false, true, false,
            TipSpeed: h.Speed, Chain: h.Chain - 1);
        anim.ActionRate = h.Rate;   // the rebound springs back from the rate the swing met at
        float riposte = verdict == GuardVerdict.Parried ? h.Riposte : 0f;   // read before the rebound makes the hold anew
        if (riposte > 0f) c.Busy += riposte;   // before the rebound (the same busy time), so a stun from the swing's end counts it
        FollowThrough.Rebound(world, a, ref anim, ref c, rule, 1f, in made, scale, turned);   // turned, the parried stun
    }

    /// <summary><paramref name="who"/>'s guard pushed through by <paramref name="by"/>: lowered, and not raised again for <paramref name="seconds"/>.</summary>
    static void Break(World world, EntityId who, EntityId by, long contact, float seconds, long tick)
    {
        ref var c = ref world.Store<Combatant>().Find(who);
        if (Unsafe.IsNullRef(ref c)) return;
        c.GuardBroken = MathF.Max(c.GuardBroken, seconds);
        c.ParryLeft = 0f;
        world.Events<GuardBroken>().Publish(new GuardBroken(who, by, tick, contact, seconds));
    }

    /// <summary>The bring-back's length for <paramref name="rest"/> real seconds of the swing left: clamped to the rule's least and most.</summary>
    public static float ReadyFor(BladeStopRule rule, float rest) => Math.Clamp(rest, rule.ReadySecondsLeast, MathF.Max(rule.ReadySecondsLeast, rule.ReadySecondsMost));

    static void BringBack(ref BladeHold h, ref CharacterAnimation anim, BladeStopRule rule, float spent, long tick)
    {
        float length = ReadyFor(rule, h.Rest - spent);
        h.State = BladeHoldState.BringBack;
        h.Since = tick;
        if (rule.BringBack == BringBackKind.Reverse && anim.ActionTime > 0f)
            anim.ActionRate = -anim.ActionTime / length;   // back to the clip's start (the stance it began from) in the bring-back's time
        else
        {
            ActionChannel.FadeFromHere(ref anim, length);
            h.State = BladeHoldState.None;
        }
    }

    static void StopSwing(World world, EntityId a)
    {
        ref var sc = ref world.Store<SwingContacts>().Find(a);
        if (!Unsafe.IsNullRef(ref sc)) sc.Stopped = true;
    }
}

/// <summary>The blade's stop, as contact calls it.</summary>
public static class FollowThrough
{
    /// <summary>
    /// A blade met a body: <b>bitten</b> — its clip put back to the contact (<paramref name="at"/>, the share of this tick's
    /// advance at the contact) and held there (its rate 0), the rate and the swing's time left kept, the contact awaiting the
    /// rules' word (<see cref="FollowThroughSystem"/>). The swing is not ended.
    /// </summary>
    public static void Bite(World world, EntityId a, ref CharacterAnimation anim, int swing, float at, in ContactMade made, BodyRegion region)
    {
        int struck = Struck(world, a, in anim, swing);
        float rate = Cut(ref anim, at);
        Hold(world, a) = new BladeHold
        {
            Swing = swing, State = BladeHoldState.Bite, Met = ContactMet.Body, Region = region, Whom = made.Target, ContactId = made.ContactId,
            Since = world.Tick, Rate = rate, Rest = Rest(world, in anim, rate), Chain = made.Chain + 1, StruckSwing = struck,
        };
        if (world.TryService<CharacterPoses>(out var poses)) poses.Invalidate();
    }

    /// <summary>
    /// A blade met a shield, a parrying blade or the level: put back to the contact and <b>sprung back</b> (its clip played
    /// backwards for <see cref="BladeStopRule.ReboundSeconds"/> at the rate that moves its tip <see cref="BladeStopRule.ReboundMetres"/>,
    /// at most <see cref="BladeStopRule.ReboundRate"/> times its rate), then
    /// brought back to guard (<see cref="FollowThroughSystem"/>); or, with <see cref="BladeStopRule.StopFreesOwnerAtOnce"/>, cut
    /// short as the first module did. A blocked owner (a shield, a blade) is busy <see cref="BladeStopRule.BlockedStaggerSeconds"/> longer.
    /// Published at once (<see cref="SwingFollowThrough"/>).
    /// </summary>
    public static void Rebound(World world, EntityId a, ref CharacterAnimation anim, ref Combatant c, BladeStopRule rule, float at, in ContactMade made) =>
        Rebound(world, a, ref anim, ref c, rule, at, in made, 1f);

    /// <summary>
    /// A blade met a guard while the rules decide what it does (<see cref="GuardSettings.RulesDecide"/>): put back to the
    /// contact and held there (its rate 0) for their verdict, the same tick (<see cref="FollowThroughSystem"/>); the swing is not
    /// stopped yet (an overpowering blade goes on).
    /// </summary>
    public static void Guarded(World world, EntityId a, ref CharacterAnimation anim, int swing, float at, in ContactMade made)
    {
        int struck = Struck(world, a, in anim, swing);
        float rate = Cut(ref anim, at);
        Hold(world, a) = new BladeHold
        {
            Swing = swing, State = BladeHoldState.Guard, Met = made.Met, Whom = made.Target, ContactId = made.ContactId,
            Since = world.Tick, Rate = rate, Rest = Rest(world, in anim, rate), Chain = made.Chain + 1, StruckSwing = struck, Speed = made.TipSpeed,
            Impulse = made.NormalImpulse, Normal = made.Normal, Material = made.Material,   // for the rebound after the verdict
        };
        if (world.TryService<CharacterPoses>(out var poses)) poses.Invalidate();
    }

    /// <summary>
    /// The earlier build's rebound: springing back <paramref name="scale"/> times as far (a parried blade, a deflected one). the
    /// distance from the blow (<see cref="ReboundDistance"/>), the arm's recoil (<see cref="Recoil"/>) and, off a shield or a blade,
    /// the attacker's stun (<see cref="Stun"/>; <paramref name="turned"/>: parried or deflected, the longer one).
    /// </summary>
    public static void Rebound(World world, EntityId a, ref CharacterAnimation anim, ref Combatant c, BladeStopRule rule, float at, in ContactMade made, float scale,
        bool turned = false)
    {
        float ready, asked = 0f;   // the rebound's distance asked for, for the event
        if (rule.StopFreesOwnerAtOnce)
        {
            CombatServices.CutShort(ref anim, at);
            ready = 0f;
        }
        else
        {
            float rate = Cut(ref anim, at);
            // Backwards at the rate that moves the tip ReboundMetres over ReboundSeconds at the speed it met with, ReboundRate at most.
            // The distance from the blow's impulse (ReboundDistance) over ReboundScaledSeconds, the arm recoiling with it.
            float metres = rule.ReboundByImpulse ? ReboundDistance(rule, in made) * scale : rule.ReboundMetres * scale;
            float seconds = rule.ReboundByImpulse ? rule.ReboundScaledSeconds : rule.ReboundSeconds;
            asked = metres;
            float back = rule.ReboundRate * scale;
            if (metres > 0f && seconds > 0f && made.TipSpeed > 1e-3f)
                back = MathF.Min(back, metres / (seconds * made.TipSpeed));
            anim.ActionRate = -MathF.Abs(rate) * back;
            float rest = Rest(world, in anim, rate);
            Hold(world, a) = new BladeHold
            {
                Swing = c.Swing, State = BladeHoldState.Rebound, Met = made.Met, Whom = made.Target, ContactId = made.ContactId,
                Since = world.Tick, Rate = rate, Rest = rest, Chain = made.Chain + 1, Verdict = 2, ReboundFor = seconds,
            };
            ready = FollowThroughSystem.ReadyFor(rule, rest - seconds);
            if (rule.ReboundByImpulse) Recoil.Start(world, a, ref anim, rule, in made, scale);
        }
        if (made.Met is ContactMet.Shield or ContactMet.Weapon && rule.BlockedStaggerSeconds > 0f) c.Busy += rule.BlockedStaggerSeconds;
        if (world.TryService<CharacterPoses>(out var poses)) poses.Invalidate();
        world.Events<SwingFollowThrough>().Publish(new SwingFollowThrough(a, c.Swing, world.Tick, BladeOutcome.Rebounds, made.Met, made.Target, BodyRegion.None,
            made.Chain + 1, made.ContactId, ready, asked));
        // A blade stopped by a guard (a shield, a blade) stuns its owner a moment: no attack, a guard allowed.
        if (rule.ReboundByImpulse && made.Met is ContactMet.Shield or ContactMet.Weapon)
            Stun(world, a, ref c, (turned ? rule.StunParriedSeconds : rule.StunHeldSeconds) + (rule.StunFromSwingEnd ? c.Busy : 0f), made.ContactId, turned);
    }

    // ---- the rebound's distance and the attacker's stun ----

    /// <summary>
    /// How far a blocked tip springs back (m, before a parry's scale): the contact's impulse (<see cref="ContactMade.NormalImpulse"/>:
    /// the weapon's mass × the speed into what it met — a cleaner, faster or heavier blow further, a glancing one less) times
    /// <see cref="BladeStopRule.ReboundMetresPerImpulse"/>, within <see cref="BladeStopRule.ReboundLeastMetres"/> and
    /// <see cref="BladeStopRule.ReboundMostMetres"/>; off the level, times its material's factor (<see cref="BladeStopRule.ReboundByMaterial"/>).
    /// </summary>
    public static float ReboundDistance(BladeStopRule rule, in ContactMade made)
    {
        float most = MathF.Max(rule.ReboundLeastMetres, rule.ReboundMostMetres);
        float d = Math.Clamp(MathF.Max(0f, made.NormalImpulse) * rule.ReboundMetresPerImpulse, rule.ReboundLeastMetres, most);
        if (made.Met == ContactMet.Level) d *= rule.ReboundByMaterial[made.Material];
        return d;
    }

    /// <summary>
    /// <paramref name="a"/> stunned for <paramref name="seconds"/> from now (Bannerlord's attacker stun; <see cref="Combatant.Stun"/>):
    /// no attack begins until it is over — a press in it is refused (<see cref="AttackRefusal.Stunned"/>) and buffered, as 1.1's buffer
    /// takes a press in a swing — while the guard may be raised once the rebound is over and a dodge made. Published
    /// (<see cref="AttackerStunned"/>).
    /// </summary>
    public static void Stun(World world, EntityId a, ref Combatant c, float seconds, long contact, bool turned)
    {
        if (seconds <= 0f) return;
        c.Stun = MathF.Max(c.Stun, seconds);
        c.StunSwing = c.Swing;
        if (world.TryService<EventQueue<AttackerStunned>>(out var q)) q.Publish(new AttackerStunned(a, world.Tick, c.Swing, seconds, contact, turned));
    }
    // ---- end

    /// <summary>Whether <paramref name="a"/>'s blade of <paramref name="swing"/> is held now (bitten, springing back or being brought back): contact sweeps it not.</summary>
    public static bool Held(World world, EntityId a, int swing)
    {
        if (!world.TryStore<BladeHold>(out var holds)) return false;
        ref var h = ref holds.Find(a);
        return !Unsafe.IsNullRef(ref h) && h.State != BladeHoldState.None && h.Swing == swing;
    }

    /// <summary>
    /// Whether the swing's <c>Strike</c> mark was told already (this tick, before the clip is put back, or at an earlier bite of
    /// the same swing): the swing's number, else 0. <see cref="StrikeSystem"/> does not tell it again.
    /// </summary>
    static int Struck(World world, EntityId a, in CharacterAnimation anim, int swing)
    {
        ref var c = ref world.Store<Combatant>().Find(a);
        if (!Unsafe.IsNullRef(ref c) && ActionChannel.Crossed(anim, c.StrikeAt)) return swing;
        if (world.TryStore<BladeHold>(out var holds))
        {
            ref var h = ref holds.Find(a);
            if (!Unsafe.IsNullRef(ref h) && h.StruckSwing == swing) return swing;
        }
        return 0;
    }

    /// <summary>Whether <paramref name="a"/>'s <paramref name="swing"/> told its <c>Strike</c> mark before a bite put its clip back.</summary>
    public static bool StruckBefore(World world, EntityId a, int swing)
    {
        if (!world.TryStore<BladeHold>(out var holds)) return false;
        ref var h = ref holds.Find(a);
        return !Unsafe.IsNullRef(ref h) && h.StruckSwing == swing && swing != 0;
    }

    /// <summary>The clip put back to the contact and held; answers the rate it played at.</summary>
    static float Cut(ref CharacterAnimation anim, float at)
    {
        float rate = anim.ActionRate;
        anim.ActionTime = anim.ActionLast + (anim.ActionTime - anim.ActionLast) * Math.Clamp(at, 0f, 1f);
        anim.ActionRate = 0f;
        return rate;
    }

    /// <summary>The real seconds the swing's clip had left at <paramref name="rate"/> from where it stands.</summary>
    static float Rest(World world, in CharacterAnimation anim, float rate)
    {
        if (rate <= 0f || !world.TryService<CharacterAssets>(out var assets)) return 0f;
        return MathF.Max(0f, assets.Clip(anim.ActionClip).Duration - anim.ActionTime) / rate;
    }

    static ref BladeHold Hold(World world, EntityId a)
    {
        var holds = world.Store<BladeHold>();
        ref var h = ref holds.Find(a);
        if (Unsafe.IsNullRef(ref h)) { world.Set(a, new BladeHold()); h = ref holds.Get(a); }
        return ref h;
    }
}

/// <summary>The follow-through registered, with contact (<see cref="ContactServices.Register"/>).</summary>
public static class FollowThroughServices
{
    /// <summary>Its place in <see cref="Phase.Resolve"/>: after the rules' harm (100), the hurt (800) and the death (900).</summary>
    public const int Order = 950;

    /// <summary>Registers <see cref="BladeHold"/> (digested), <see cref="SwingFollowThrough"/> and the system <c>Resolve/950 platform.follow-through</c>.</summary>
    public static WorldBuilder Register(WorldBuilder b)
    {
        b.Component<BladeHold>();
        b.Event<SwingFollowThrough>();
        b.System(Phase.Resolve, Order, "platform.follow-through", new FollowThroughSystem());
        // The attacker's stun, published; the arm's recoil moved on before the pose rig (Move/207 platform.recoil).
        b.Event<AttackerStunned>();
        b.System(AnimationRegistration.PosePhase, AnimationRegistration.AnimationOrder + RecoilSystem.Order, RecoilSystem.Name, new RecoilSystem());
        return b;
    }
}
