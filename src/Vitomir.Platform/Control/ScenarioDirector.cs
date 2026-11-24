// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the scenario director.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;

namespace Vitomir.Platform.Control;

/// <summary>A program the director cannot run in this world (an actor, an event kind, a field or an observable it does not have), said by name when the world is made.</summary>
public sealed class ScenarioProgramException(string message) : Exception(message);

/// <summary>The director registered: only in a world with a scenario program, so every other world is unchanged.</summary>
public static class ScenarioDirectors
{
    /// <summary>The director's place: <c>Think/96</c>, after the duelling brain (95), before the routes (100).</summary>
    public const int Order = 96;

    /// <summary>
    /// Registers the director's events (<see cref="StepBegan"/>, <see cref="StepEnded"/>, <see cref="StepTimedOut"/>,
    /// <see cref="StepRefused"/>, <see cref="StepLate"/>, <see cref="ControlChanged"/>, <see cref="Marker"/>, <see cref="TriggerFired"/>,
    /// <see cref="ScenarioEnded"/>, <see cref="AimedBlow"/>), the <see cref="ScenarioDirector"/> service (digested) and
    /// <c>Think/96 platform.scenario-director</c>. After <see cref="CombatServices.Register"/>. The actors are bound by
    /// <see cref="ScenarioDirector.Bind"/> once they are made.
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b, ScenarioProgram program)
    {
        Event<StepBegan>(b); Event<StepEnded>(b); Event<StepTimedOut>(b); Event<StepRefused>(b); Event<StepLate>(b);
        Event<ControlChanged>(b); Event<Marker>(b); Event<TriggerFired>(b); Event<ScenarioEnded>(b); Event<AimedBlow>(b);
        Event<MoveToRouted>(b); Event<MoveToFailed>(b);   // a moveTo's route round the level, and its failure
        b.Service(new ScenarioDirector(program));
        b.System(Phase.Think, Order, "platform.scenario-director", new ScenarioDirectorSystem());
        return b;
    }

    static void Event<T>(WorldBuilder b) where T : struct
    {
        if (!b.Peek.TryService<EventQueue<T>>(out _)) b.Event<T>();
    }
}

/// <summary>The director's turn (<see cref="Phase.Think"/> 96).</summary>
public sealed class ScenarioDirectorSystem : ISystem
{
    public void Run(World world)
    {
        if (world.TryService<ScenarioDirector>(out var director)) director.Run(world);
    }
}

/// <summary>How a step's tick went.</summary>
public enum StepFlow { Running, Done, TimedOut, Refused }

/// <summary>A step's tick's result: still running, or ended (done, timed out with why, refused with why).</summary>
public readonly record struct StepResult(StepFlow Flow, string? Why = null, StepRefusal Refusal = StepRefusal.NoSlot, string? Aimed = null, string? Met = null)
{
    public static StepResult Running => new(StepFlow.Running);
    public static StepResult Done => new(StepFlow.Done);
    public static StepResult TimedOut(string why, string? aimed = null, string? met = null) => new(StepFlow.TimedOut, why, StepRefusal.NoSlot, aimed, met);
    public static StepResult Refused(StepRefusal why) => new(StepFlow.Refused, null, why);
}

/// <summary>
/// <b>The scenario director</b> (ours): each tick, after the duelling brain, it
/// turns each actor's steps and the program's triggers into <b>the same intents a player's buttons or the brain write</b> —
/// <see cref="MoveIntent"/> (the way, the gait, the facing), <see cref="CombatIntent"/> (attack and block held, the blow's direction
/// and pitch), <see cref="DodgeIntent"/>, <see cref="PowerIntent"/> — and clears the channels muted for the AI. Nothing reaches a
/// hit, a block or a power by a shortcut: the rules admit or refuse as for anyone, and a step that cannot be honoured falls short
/// and says so (<see cref="StepTimedOut"/>, <see cref="StepRefused"/>).
/// <para><b>Who holds what.</b> While any of an actor's tracks runs (and after <see cref="ScenarioThen.Stand"/>), the script holds
/// every channel: the channels no running step writes are written still (no way, the facing kept, attack as the last step left
/// it — a drawn bow stays drawn through a <c>wait</c> — no block, no dodge, no slot), so an actor with an AI is not walked about
/// by its AI between steps. After its tracks end with <see cref="ScenarioThen.Ai"/>, or a <c>handOver</c>, the AI drives every
/// channel but the muted ones. A trigger's steps hold only their own channels, over any track step holding them (which waits,
/// and resumes after). <see cref="ControlChanged"/> says who holds each channel whenever that changes.</para>
/// <para><b>Determinism.</b> It reads only the world and last tick's events (a trigger on an event starts its steps one tick after
/// it), draws nothing, and feeds its cursors to the world's digest (<see cref="IDigestedService"/>). Registered only in a world
/// with a program. Allocates nothing per tick but when a trigger fires.</para>
/// </summary>
public sealed partial class ScenarioDirector : IDigestedService
{
    readonly ScenarioProgram program;
    ActorRun[] actors = [];
    TriggerRun[] triggers = [];
    readonly Dictionary<string, int> byName = new(StringComparer.Ordinal);
    readonly List<string> names = [];
    readonly List<EntityId> entities = [];
    readonly Dictionary<string, long> markers = new(StringComparer.Ordinal);
    bool bound, started;
    long nextId;   // the ids of step runnings and trigger firings (the output's eventId/actionId/parentEventId)

    public ScenarioDirector(ScenarioProgram program) { this.program = program; }

    /// <summary>The program it runs.</summary>
    public ScenarioProgram Program => program;

    /// <summary>Whether the program has ended the run (an <c>end</c> step, a trigger's <c>end</c>, <c>then: end</c>), and why and when.</summary>
    public bool Ended { get; private set; }
    public string EndReason { get; private set; } = "";
    public long EndTick { get; private set; } = -1;

    /// <summary>The actors' cast names, in the order bound.</summary>
    public IReadOnlyList<string> Names => names;

    /// <summary>The entity of an actor's name, or <see cref="EntityId.None"/>.</summary>
    public EntityId Entity(string? name) => name is not null && byName.TryGetValue(name, out int i) ? entities[i] : EntityId.None;

    /// <summary>The name of an actor's entity, or null.</summary>
    public string? NameOf(EntityId e)
    {
        for (int i = 0; i < entities.Count; i++) if (entities[i] == e) return names[i];
        return null;
    }

    /// <summary>The tick of the first <see cref="Marker"/> with this text, or −1.</summary>
    public long MarkerTick(string text) => markers.TryGetValue(text, out long t) ? t : -1;

    // ---- binding ----

    /// <summary>
    /// Binds the program to the world's actors (the cast's names and their entities, in the cast's order) once they are made:
    /// resolves every name, event kind, field and observable the program names, and compiles each actor's tracks. Throws
    /// <see cref="ScenarioProgramException"/> by name for anything this world does not have.
    /// </summary>
    public void Bind(World world, IReadOnlyList<string> actorNames, IReadOnlyList<EntityId> actorEntities)
    {
        if (actorNames.Count != actorEntities.Count) throw new ArgumentException("names and entities differ in count");
        byName.Clear(); names.Clear(); entities.Clear();
        for (int i = 0; i < actorNames.Count; i++)
        {
            if (!byName.TryAdd(actorNames[i], i)) throw new ScenarioProgramException($"scenario: two actors are named {actorNames[i]}");
            names.Add(actorNames[i]);
            entities.Add(actorEntities[i]);
        }
        if (program.Input is { } input)
        {
            Need(input.Actor, "input");
            if (program.Actors.Exists(c => c.Actor == input.Actor) || program.Triggers.Exists(t => t.Do.Exists(d => d.Actor == input.Actor)))
                throw new ScenarioProgramException($"scenario: {input.Actor} is driven by the player's controls (\"input\"), so it can have no control block and no trigger's steps");
        }
        var runs = new List<ActorRun>();
        foreach (var control in program.Actors)
        {
            var e = Need(control.Actor, $"control of {control.Actor}");
            var run = new ActorRun { Control = control, Entity = e, Name = control.Actor, Foe = control.Foe is null ? EntityId.None : Need(control.Foe, $"{control.Actor}'s foe") };
            run.Tracks = new Track[control.Tracks.Count];
            for (int t = 0; t < control.Tracks.Count; t++)
            {
                var code = new List<Instr>();
                int slots = 0;
                int number = NumberBefore(control, t);
                Compile(world, control.Tracks[t], code, ref slots, -1, ref number, $"{control.Actor}'s track {t}");
                run.Tracks[t] = new Track(t, [.. code], slots);
            }
            if (world.TryStore<Body>(out var bodies) && bodies.Has(e)) run.FaceLatch = bodies.Get(e).Yaw;
            run.FacesLatch = true;
            runs.Add(run);
        }
        actors = [.. runs];
        triggers = new TriggerRun[program.Triggers.Count];
        for (int i = 0; i < program.Triggers.Count; i++)
        {
            var tr = program.Triggers[i];
            var where = $"trigger {i}{(tr.Label is null ? "" : $" ({tr.Label})")}";
            int given = (tr.When.Event is null ? 0 : 1) + (tr.When.Value is null ? 0 : 1) + (tr.When.Count is null ? 0 : 1);
            if (given != 1) throw new ScenarioProgramException($"scenario: {where}: \"when\" needs exactly one of an event, a value or a count");
            var run = new TriggerRun
            {
                Trigger = tr,
                Event = tr.When.Event is { } ev ? EventMatcher.For(world, this, ev, where) : null,
                Count = tr.When.Count is { } cc ? EventMatcher.For(world, this, cc.Of, where) : null,
                Value = tr.When.Value is { } vc ? Observable.Parse(world, this, vc.Observable, where) : null,
                Steps = [],
            };
            foreach (var act in tr.Do)
            {
                if (act.End is not null) continue;
                if (act.Step is null || act.Actor is null) throw new ScenarioProgramException($"scenario: {where}: a \"do\" needs an actor and a step, or \"end\"");
                Need(act.Actor, where);
                if (!Array.Exists(actors, a => a.Name == act.Actor)) throw new ScenarioProgramException($"scenario: {where}: {act.Actor} has no control block, so it cannot be given steps");
                var code = new List<Instr>();
                int slots = 0, number = i;
                Compile(world, [act.Step], code, ref slots, -1, ref number, where, fixedNumber: i);
                run.Steps.Add((act.Actor, code[0].Step, [.. code], slots));
            }
            triggers[i] = run;
        }
        bound = true;
    }

    static int NumberBefore(ActorControl control, int track)
    {
        int n = 0;
        for (int t = 0; t < track; t++) n += CountSteps(control.Tracks[t]);
        return n;
    }

    static int CountSteps(IReadOnlyList<ScenarioStep> steps)
    {
        int n = 0;
        foreach (var s in steps) n += 1 + (s is RepeatStep r ? CountSteps(r.Steps) : 0);
        return n;
    }

    EntityId Need(string name, string where) =>
        byName.TryGetValue(name, out int i) ? entities[i] : throw new ScenarioProgramException($"scenario: {where}: no actor is named {name} (the actors: {string.Join(", ", names)})");

    void Compile(World world, IReadOnlyList<ScenarioStep> steps, List<Instr> code, ref int slots, int parent, ref int number, string where, int fixedNumber = -1)
    {
        foreach (var step in steps)
        {
            var instr = new Instr
            {
                Step = step,
                Number = fixedNumber >= 0 ? fixedNumber : number,
                Parent = parent,
                Channels = ChannelsOf(step),
                On = step.On is { } on ? EventMatcher.For(world, this, on, $"{where}, step {number} ({step.Kind})") : null,
                Until = step is BlockStep { Until: { } until } ? EventMatcher.For(world, this, until, $"{where}, step {number} (block until)") : null,
            };
            number++;
            CheckNames(step, $"{where}, step {instr.Number} ({step.Kind})");
            if (step is RepeatStep repeat)
            {
                instr.Kind = InstrKind.RepeatBegin;
                instr.Slot = slots++;
                int begin = code.Count;
                code.Add(instr);
                Compile(world, repeat.Steps, code, ref slots, instr.Slot, ref number, where, fixedNumber);
                code.Add(new Instr { Kind = InstrKind.RepeatEnd, Step = step, Number = instr.Number, Slot = instr.Slot, Other = begin, Parent = parent });
                instr.Other = code.Count - 1;
            }
            else code.Add(instr);
        }
    }

    void CheckNames(ScenarioStep step, string where)
    {
        foreach (var n in NamesIn(step))
            if (n is not null) Need(n, where);
    }

    static IEnumerable<string?> NamesIn(ScenarioStep step)
    {
        switch (step)
        {
            case FaceStep f: yield return f.Toward.Actor; break;
            case MoveToStep m: yield return m.To.Actor; yield return m.FaceWhileMoving; break;
            case KeepDistanceStep k: yield return k.From; break;
            case RetreatStep r: yield return r.From; break;
            case StrafeStep s: yield return s.Facing; break;
            case CircleStep c: yield return c.Around; break;
            case AttackStep a: yield return a.Target; yield return a.AimAt?.Actor; break;
            case ParryStep p: yield return p.Against; break;
            case AimStep b: yield return b.At.Actor; break;
        }
    }

    /// <summary>The channels a step holds while it runs (section 4.4's step table). An attack aimed at a region, a point or a yaw also holds the facing.</summary>
    public static ControlChannels ChannelsOf(ScenarioStep step) => step switch
    {
        StandStep => ControlChannels.Move,
        FaceStep => ControlChannels.Face,
        MoveToStep or KeepDistanceStep or RetreatStep or StrafeStep or CircleStep or PathStep => ControlChannels.Move | ControlChannels.Face,
        AttackStep a => a.AimAt is { Region: not null } or { Point: not null } or { YawDegrees: not null } ? ControlChannels.Attack | ControlChannels.Face : ControlChannels.Attack,
        BlockStep or ParryStep => ControlChannels.Block,
        CancelStep => ControlChannels.Attack,
        DodgeStep => ControlChannels.Dodge | ControlChannels.Move,
        UsePowerStep => ControlChannels.Power,
        EquipStep or DrawStep or LooseStep or CancelDrawStep => ControlChannels.Attack,
        AimStep => ControlChannels.Attack | ControlChannels.Face,
        _ => ControlChannels.None,
    };

    // ---- the tick ----

    // The stores of this tick (set at the top of Run; null when the world does not register them).
    Store<Body> bodies = null!;
    Store<MoveIntent> moves = null!;
    Store<CombatIntent> intents = null!;
    Store<Combatant> fighters = null!;
    Store<CharacterAnimation>? anims;
    World w = null!;
    long tick;

    /// <summary>One tick of the director (<see cref="ScenarioDirectorSystem"/>).</summary>
    public void Run(World world)
    {
        if (!bound) throw new InvalidOperationException("scenario: the director's program was never bound to the world's actors (ScenarioDirector.Bind)");
        if (Ended) return;
        w = world;
        tick = world.Tick;
        bodies = world.Store<Body>();
        moves = world.Store<MoveIntent>();
        intents = world.Store<CombatIntent>();
        fighters = world.Store<Combatant>();
        anims = world.TryStore<CharacterAnimation>(out var an) ? an : null;
        if (!started)
        {
            started = true;
            foreach (var a in actors) BeginTracks(a);
        }

        RunTriggers();
        for (int i = 0; i < actors.Length && !Ended; i++) RunActor(actors[i]);
        w = null!;
    }

    void BeginTracks(ActorRun a)
    {
        foreach (var t in a.Tracks) Enter(t, 0);
    }

    // ---- triggers ----

    void RunTriggers()
    {
        float now = (float)(tick / (double)World.TicksPerSecond);
        for (int i = 0; i < triggers.Length; i++)
        {
            ref var tr = ref triggers[i].State;
            var t = triggers[i];
            // A count counts every match, in or out of its window or cooldown.
            if (t.Count is { } cm) tr.Count += cm.Matches(w, tick);
            bool cond;
            if (t.Event is { } em) cond = em.Matches(w, tick) > 0;
            else if (t.Count is not null) cond = Compare(t.Trigger.When.Count!.Compare, tr.Count);
            else cond = Compare(t.Trigger.When.Value!.Compare, t.Value!.Read(w, this));
            bool edge = t.Event is not null ? cond : cond && !tr.WasTrue;
            tr.WasTrue = cond;
            if (!edge) continue;
            if (!t.Trigger.Repeat && tr.Fired > 0) continue;
            if (t.Trigger.AfterSeconds is { } after && now < after) continue;
            if (t.Trigger.BeforeSeconds is { } before && now > before) continue;
            if (tr.Fired > 0 && t.Trigger.CooldownSeconds > 0f && tick - tr.LastFired < Ticks(t.Trigger.CooldownSeconds)) continue;
            tr.Fired++;
            tr.LastFired = tick;
            long firing = ++nextId;
            w.Events<TriggerFired>().Publish(new TriggerFired(tick, i, t.Trigger.Label, firing));
            Fire(i, t, firing);
            if (Ended) return;
        }
    }

    void Fire(int index, TriggerRun t, long firing)
    {
        foreach (var act in t.Trigger.Do)
        {
            if (act.End is { } reason) { End(EntityId.None, reason); return; }
        }
        // Each actor's steps of this firing, in order, as one interrupting track.
        foreach (var a in actors)
        {
            List<Instr>? code = null;
            int slots = 0;
            foreach (var (actor, _, steps, s) in t.Steps)
            {
                if (actor != a.Name) continue;
                code ??= [];
                foreach (var instr in steps)
                    code.Add(instr with { Slot = instr.Kind == InstrKind.Step ? 0 : instr.Slot + slots, Parent = instr.Parent >= 0 ? instr.Parent + slots : -1 });
                slots += s;
            }
            if (code is null) continue;
            Rebase(code);
            var track = new Track(-1, [.. code], slots) { Trigger = index, Cause = firing };
            a.Interrupts.Add(track);
            Enter(track, 0);
        }
    }

    /// <summary>Fixes the begin/end links of repeats in a code list built from separately compiled pieces.</summary>
    static void Rebase(List<Instr> code)
    {
        var open = new Stack<int>();
        for (int i = 0; i < code.Count; i++)
        {
            if (code[i].Kind == InstrKind.RepeatBegin) open.Push(i);
            else if (code[i].Kind == InstrKind.RepeatEnd)
            {
                int b = open.Pop();
                code[b] = code[b] with { Other = i };
                code[i] = code[i] with { Other = b };
            }
        }
    }

    static bool Compare(Comparison c, double v) => c.Op switch
    {
        CompareOp.Below => v < c.Value,
        CompareOp.AtMost => v <= c.Value,
        CompareOp.Above => v > c.Value,
        CompareOp.AtLeast => v >= c.Value,
        CompareOp.Equals => v == c.Value,
        CompareOp.Between => v >= c.Value && v <= c.High,
        CompareOp.IsTrue => v != 0,
        CompareOp.IsFalse => v == 0,
        _ => false,
    };

    internal static long Ticks(float seconds) => (long)MathF.Round(seconds * World.TicksPerSecond);

    // ---- an actor's tick ----

    void RunActor(ActorRun a)
    {
        var e = a.Entity;
        if (!w.Alive(e) || !bodies.Has(e)) return;
        bool stopped = IsStopped(e);
        if (stopped && !a.Stopped)
        {
            a.Stopped = true;
            foreach (var t in a.Tracks) StopTrack(a, t, StepFlow.Refused, StepRefusal.Stopped);
            foreach (var t in a.Interrupts) StopTrack(a, t, StepFlow.Refused, StepRefusal.Stopped);
            a.Interrupts.Clear();
        }
        AimedBlowSeen(a);
        a.Written = ControlChannels.None;

        // The trigger's steps first: they hold their channels over the tracks'.
        ControlChannels interrupting = ControlChannels.None;
        for (int i = 0; i < a.Interrupts.Count; i++)
        {
            var t = a.Interrupts[i];
            Advance(a, t, ControlChannels.None);
            if (Ended) return;
            if (!t.Ended && t.Run.Running) interrupting |= t.Code[t.Pc].Channels;
        }
        for (int i = a.Interrupts.Count - 1; i >= 0; i--) if (a.Interrupts[i].Ended) a.Interrupts.RemoveAt(i);
        foreach (var t in a.Tracks)
        {
            Advance(a, t, interrupting);
            if (Ended) return;
        }

        // Every track over: what follows.
        if (!a.ThenDone && !a.HandedOver && AllEnded(a))
        {
            a.ThenDone = true;
            switch (a.Control.Then)
            {
                case ScenarioThen.Stand: a.StandAfter = true; break;
                case ScenarioThen.End: End(e, "script"); return;
                default: a.HandedOver = true; break;
            }
        }

        // Who holds what, said when it changes; the held channels nobody wrote this tick written still; the muted cleared.
        bool scripted = !a.Stopped && (!a.HandedOver && !AllEnded(a) || a.StandAfter);
        var script = scripted ? ControlChannels.All : interrupting | a.Written;
        if (a.Stopped) script = ControlChannels.None;
        bool hasAi = a.Control.Ai != ScenarioAi.None;
        var muted = hasAi ? a.Control.Mute & ~script : ControlChannels.None;
        var ai = hasAi ? ControlChannels.All & ~script & ~a.Control.Mute : ControlChannels.None;
        if (!a.Published || script != a.Script || muted != a.Muted || ai != a.Ai)
        {
            a.Published = true;
            a.Script = script; a.Muted = muted; a.Ai = ai;
            w.Events<ControlChanged>().Publish(new ControlChanged(e, tick, script, ai, muted));
        }
        if (a.Stopped) return;
        if (scripted) Still(a, script & ~a.Written);
        if (muted != ControlChannels.None) Mute(e, muted);
    }

    static bool AllEnded(ActorRun a)
    {
        foreach (var t in a.Tracks) if (!t.Ended) return false;
        return true;
    }

    bool IsStopped(EntityId e) => w.TryStore<Dead>(out var dead) && dead.Has(e);

    /// <summary>The script's held channels that no step wrote this tick, written still: no way, the facing kept, attack as the last step left it, no block, no dodge, no slot.</summary>
    void Still(ActorRun a, ControlChannels channels)
    {
        var e = a.Entity;
        if ((channels & ControlChannels.Move) != 0)
        {
            ref var m = ref moves.Find(e);
            if (!Unsafe.IsNullRef(ref m)) { m.Direction = Vector2.Zero; m.Jump = m.JumpHeld = m.Crouch = m.Cancel = m.Use = false; }
        }
        if ((channels & ControlChannels.Face) != 0)
        {
            ref var m = ref moves.Find(e);
            if (!Unsafe.IsNullRef(ref m)) { m.Faces = a.FacesLatch; if (a.FacesLatch) m.Facing = a.FaceLatch; }
        }
        if ((channels & (ControlChannels.Attack | ControlChannels.Block)) != 0)
        {
            ref var c = ref intents.Find(e);
            if (!Unsafe.IsNullRef(ref c))
            {
                if ((channels & ControlChannels.Attack) != 0)
                {
                    c.Attack = a.AttackLatch;
                    c.Direction = a.DirectionLatch;
                    c.Aims = a.AimsLatch;
                    c.AimPitch = a.PitchLatch;
                }
                if ((channels & ControlChannels.Block) != 0) c.Block = false;
            }
        }
        if ((channels & ControlChannels.Dodge) != 0 && w.TryStore<DodgeIntent>(out var dodges))
        {
            ref var d = ref dodges.Find(e);
            if (!Unsafe.IsNullRef(ref d)) d.Pressed = false;
        }
        if ((channels & ControlChannels.Power) != 0 && w.TryStore<PowerIntent>(out var powers))
        {
            ref var p = ref powers.Find(e);
            if (!Unsafe.IsNullRef(ref p)) p.Slot = 0;
        }
    }

    /// <summary>The AI's muted channels cleared, after the AI wrote them.</summary>
    void Mute(EntityId e, ControlChannels channels)
    {
        if ((channels & (ControlChannels.Move | ControlChannels.Face)) != 0)
        {
            ref var m = ref moves.Find(e);
            if (!Unsafe.IsNullRef(ref m))
            {
                if ((channels & ControlChannels.Move) != 0) { m.Direction = Vector2.Zero; m.Jump = m.JumpHeld = false; }
                if ((channels & ControlChannels.Face) != 0) m.Faces = false;
            }
        }
        if ((channels & (ControlChannels.Attack | ControlChannels.Block)) != 0)
        {
            ref var c = ref intents.Find(e);
            if (!Unsafe.IsNullRef(ref c))
            {
                if ((channels & ControlChannels.Attack) != 0) { c.Attack = false; c.Direction = AttackDirection.None; }
                if ((channels & ControlChannels.Block) != 0) c.Block = false;
            }
        }
        if ((channels & ControlChannels.Dodge) != 0 && w.TryStore<DodgeIntent>(out var dodges))
        {
            ref var d = ref dodges.Find(e);
            if (!Unsafe.IsNullRef(ref d)) d.Pressed = false;
        }
        if ((channels & ControlChannels.Power) != 0 && w.TryStore<PowerIntent>(out var powers))
        {
            ref var p = ref powers.Find(e);
            if (!Unsafe.IsNullRef(ref p)) p.Slot = 0;
        }
    }

    // ---- tracks ----

    /// <summary>Puts a track on an instruction: the step waits for its start (its <c>at</c>, <c>afterSeconds</c> or <c>on</c>).</summary>
    void Enter(Track t, int pc)
    {
        t.Pc = pc;
        t.Run = default;
        t.Run.Entered = tick;
        if (pc >= t.Code.Length) { t.Ended = true; return; }
        var instr = t.Code[pc];
        if (instr.Kind == InstrKind.RepeatEnd) { t.Run.ReadyAt = tick; return; }
        var s = instr.Step;
        if (s.AtSeconds is { } at) t.Run.ReadyAt = Math.Max(tick, Ticks(at));
        else if (s.AfterSeconds is { } after) t.Run.ReadyAt = tick + Ticks(after);
        else if (s.On is not null) t.Run.ReadyAt = -1;
        else t.Run.ReadyAt = tick;
    }

    void Advance(ActorRun a, Track t, ControlChannels interrupting)
    {
        for (int guard = 0; guard < 256 && !t.Ended && !Ended; guard++)
        {
            var instr = t.Code[t.Pc];
            if (!t.Run.Running)
            {
                if (instr.Kind == InstrKind.RepeatEnd) { RepeatEnd(a, t, instr); continue; }
                if (t.Run.ReadyAt < 0)
                {
                    // Waiting for its event (read one tick after it), then its delay.
                    if (instr.On!.Matches(w, tick) == 0 || tick <= t.Run.Entered) return;
                    t.Run.ReadyAt = tick + Ticks(instr.Step.DelaySeconds ?? 0f);
                }
                if (tick < t.Run.ReadyAt) return;
                Begin(a, t, instr);
                if (instr.Kind == InstrKind.RepeatBegin) { RepeatBegin(a, t, instr); continue; }
            }
            if (instr.Kind != InstrKind.Step) return;
            if (t.Number >= 0 && (instr.Channels & interrupting) != 0) return;   // a trigger's step holds its channels: it waits
            var result = Tick(a, t, instr);
            if (result.Flow == StepFlow.Running) return;
            Finish(a, t, instr, result);
            if (a.HandedOver && t.Number >= 0 && t.Ended) return;
        }
    }

    void Begin(ActorRun a, Track t, Instr instr)
    {
        t.Run.Running = true;
        t.Run.Began = tick;
        var s = instr.Step;
        int iteration = instr.Parent >= 0 ? t.Iter[instr.Parent] : 0;
        t.Run.Instance = ++nextId;
        long parent = instr.Parent >= 0 ? t.RepeatInstance[instr.Parent] : t.Cause;
        if (s.AtSeconds is { } at && t.Run.Entered > Ticks(at))
            w.Events<StepLate>().Publish(new StepLate(a.Entity, tick, t.Number, instr.Number, s.Kind, s.Label, (t.Run.Entered - Ticks(at)) / (float)World.TicksPerSecond, t.Run.Instance));
        w.Events<StepBegan>().Publish(new StepBegan(a.Entity, tick, t.Number, instr.Number, s.Kind, s.Label, iteration, t.Run.Instance, parent));
    }

    void RepeatBegin(ActorRun a, Track t, Instr instr)
    {
        var r = (RepeatStep)instr.Step;
        t.Left[instr.Slot] = r.UntilStop ? int.MaxValue : r.Count;
        t.Iter[instr.Slot] = 0;
        t.RepeatInstance[instr.Slot] = t.Run.Instance;
        if (!r.UntilStop && r.Count <= 0) { EndRepeat(a, t, instr); return; }
        Enter(t, t.Pc + 1);
    }

    void RepeatEnd(ActorRun a, Track t, Instr end)
    {
        int slot = end.Slot;
        if (t.Left[slot] != int.MaxValue) t.Left[slot]--;
        if (t.Left[slot] > 0)
        {
            t.Iter[slot]++;
            Enter(t, end.Other + 1);
            return;
        }
        var begin = t.Code[end.Other];
        t.Pc = end.Other;
        t.Run.Began = t.Run.Entered;
        EndRepeat(a, t, begin);
    }

    void EndRepeat(ActorRun a, Track t, Instr begin)
    {
        var s = begin.Step;
        w.Events<StepEnded>().Publish(new StepEnded(a.Entity, tick, t.Number, begin.Number, s.Kind, s.Label, StepOutcome.Done, 0f, t.RepeatInstance[begin.Slot]));
        Enter(t, begin.Other + 1);
    }

    void Finish(ActorRun a, Track t, Instr instr, StepResult result)
    {
        var s = instr.Step;
        var e = a.Entity;
        var outcome = result.Flow switch { StepFlow.TimedOut => StepOutcome.TimedOut, StepFlow.Refused => StepOutcome.Refused, _ => StepOutcome.Done };
        if (result.Flow == StepFlow.TimedOut)
            w.Events<StepTimedOut>().Publish(new StepTimedOut(e, tick, t.Number, instr.Number, s.Kind, s.Label, result.Why ?? "", result.Aimed, result.Met, t.Run.Instance));
        if (result.Flow == StepFlow.Refused)
            w.Events<StepRefused>().Publish(new StepRefused(e, tick, t.Number, instr.Number, s.Kind, s.Label, result.Refusal, t.Run.Instance));
        w.Events<StepEnded>().Publish(new StepEnded(e, tick, t.Number, instr.Number, s.Kind, s.Label, outcome, (tick - t.Run.Began) / (float)World.TicksPerSecond, t.Run.Instance));
        if (Ended) { t.Ended = true; return; }
        if (s is HandOverStep)
        {
            HandOver(a, t);
            return;
        }
        Enter(t, t.Pc + 1);
    }

    /// <summary>Every channel to the AI from here: every track and every trigger's step of the actor ends (<see cref="StepOutcome.HandedOver"/>).</summary>
    void HandOver(ActorRun a, Track from)
    {
        a.HandedOver = true;
        a.StandAfter = false;
        a.AttackLatch = false;
        from.Ended = true;
        foreach (var t in a.Tracks) if (t != from) StopTrack(a, t, StepFlow.Done, default, StepOutcome.HandedOver);
        foreach (var t in a.Interrupts) if (t != from) StopTrack(a, t, StepFlow.Done, default, StepOutcome.HandedOver);
        foreach (var t in a.Tracks) t.Ended = true;
    }

    void StopTrack(ActorRun a, Track t, StepFlow flow, StepRefusal why, StepOutcome outcome = StepOutcome.Refused)
    {
        if (t.Ended) return;
        if (t.Run.Running && t.Pc < t.Code.Length && t.Code[t.Pc].Kind == InstrKind.Step)
        {
            var instr = t.Code[t.Pc];
            var s = instr.Step;
            if (flow == StepFlow.Refused)
                w.Events<StepRefused>().Publish(new StepRefused(a.Entity, tick, t.Number, instr.Number, s.Kind, s.Label, why, t.Run.Instance));
            w.Events<StepEnded>().Publish(new StepEnded(a.Entity, tick, t.Number, instr.Number, s.Kind, s.Label, outcome, (tick - t.Run.Began) / (float)World.TicksPerSecond, t.Run.Instance));
        }
        t.Ended = true;
    }

    void End(EntityId by, string reason)
    {
        if (Ended) return;
        Ended = true;
        EndReason = reason;
        EndTick = tick;
        w.Events<ScenarioEnded>().Publish(new ScenarioEnded(tick, by, reason));
    }

    // ---- the steps ----

    StepResult Tick(ActorRun a, Track t, Instr instr)
    {
        var s = instr.Step;
        long held = tick - t.Run.Began;
        bool first = held == 0 && !t.Run.Touched;
        t.Run.Touched = true;
        // A goal step not met in its time ends timed out; a step that lasts a while ends done.
        if (s.ForSeconds is { } most && held >= Ticks(most) && s is not WaitStep and not StandStep)
        {
            if (Lasts(s)) return StepResult.Done;
            if (s is MoveToStep or PathStep) MoveToTimedOut(a, t, s);   // the path walked, said
            if (s is AttackStep) ReleaseAttack(a, t);
            if (s is BlockStep or ParryStep) { ref var ci = ref intents.Find(a.Entity); if (!Unsafe.IsNullRef(ref ci)) ci.Block = false; a.Written |= ControlChannels.Block; }
            return StepResult.TimedOut(TimeOutWhy(s, t), AimWords(s), null);
        }
        var r = s switch
        {
            WaitStep ws => held >= Ticks(ws.Seconds) ? StepResult.Done : StepResult.Running,
            StandStep st => Stand(a, st, held),
            FaceStep f => Face(a, f, first),
            MoveToStep m => MoveToRouted(a, t, m, first),   // round the level when the straight line meets it (ScenarioDirector.Routes.cs)
            KeepDistanceStep k => KeepDistance(a, k),
            RetreatStep rs => Retreat(a, rs),
            StrafeStep sf => Strafe(a, sf),
            CircleStep c => Circle(a, c),
            PathStep p => PathRouted(a, t, p, first),   // each leg round the level as a moveTo's
            AttackStep at => Attack(a, t, instr, at),
            BlockStep b => Block(a, t, instr, b),
            ParryStep p => Parry(a, t, p),
            CancelStep => Cancel(a),
            DodgeStep d => Dodge(a, t, d),
            UsePowerStep u => UsePower(a, t, u),
            EquipStep or AimStep or DrawStep or LooseStep or CancelDrawStep => RunBow(w, a, t, s, first),
            MarkStep mk => Mark(a, mk),
            HandOverStep => StepResult.Done,
            EndStep end => EndStep(a, end),
            _ => StepResult.Done,
        };
        return r;
    }

    static bool Lasts(ScenarioStep s) => s is KeepDistanceStep or StrafeStep or CircleStep or BlockStep or PathStep { Loop: true };

    static string TimeOutWhy(ScenarioStep s, Track t) => s switch
    {
        FaceStep => "not faced",
        MoveToStep or PathStep => "not arrived",
        RetreatStep => "not far enough",
        AttackStep => t.Run.Sub == 0 ? "never ready" : "still held",
        ParryStep => "no blow to parry",
        _ => "not done in its time",
    };

    static string? AimWords(ScenarioStep s) => s is AttackStep { AimAt: { Region: { } r } aim } ? RegionWords(r, aim.Side) : null;

    /// <summary>A region as <c>aimAt</c> names it, with its side when given: <c>chest</c>, <c>left upperArm</c>.</summary>
    public static string RegionWords(AimRegion r, StepSide? side)
    {
        string name = r switch
        {
            AimRegion.UpperArm => "upperArm",
            _ => char.ToLowerInvariant(r.ToString()[0]) + r.ToString()[1..],
        };
        return side is null ? name : $"{(side == StepSide.Left ? "left" : "right")} {name}";
    }

    StepResult Mark(ActorRun a, MarkStep mk)
    {
        markers.TryAdd(mk.Text, tick);
        w.Events<Marker>().Publish(new Marker(a.Entity, tick, mk.Text));
        return StepResult.Done;
    }

    StepResult EndStep(ActorRun a, EndStep end)
    {
        End(a.Entity, end.Reason);
        return StepResult.Done;
    }

    // ---- moving ----

    ref MoveIntent MoveOf(ActorRun a, ControlChannels channels)
    {
        a.Written |= channels;
        return ref moves.Find(a.Entity);
    }

    void FaceToward(ActorRun a, ref MoveIntent m, Vector3 from, Vector3 to)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        if (dx * dx + dy * dy < 1e-8f) { m.Faces = a.FacesLatch; m.Facing = a.FaceLatch; return; }
        FaceAngle(a, ref m, DetMath.Atan2(dy, dx));
    }

    static void FaceAngle(ActorRun a, ref MoveIntent m, float yaw)
    {
        m.Facing = DetMath.WrapAngle(yaw);
        m.Faces = true;
        a.FaceLatch = m.Facing;
        a.FacesLatch = true;
    }

    static void FaceWay(ActorRun a, ref MoveIntent m)
    {
        m.Faces = false;
        a.FacesLatch = false;
    }

    bool Feet(EntityId e, out Vector3 feet)
    {
        ref var b = ref bodies.Find(e);
        if (Unsafe.IsNullRef(ref b)) { feet = default; return false; }
        feet = b.Feet;
        return true;
    }

    EntityId Or(string? name, EntityId fallback) => name is null ? fallback : Entity(name);

    StepResult Stand(ActorRun a, StandStep st, long held)
    {
        ref var m = ref MoveOf(a, ControlChannels.Move);
        if (!Unsafe.IsNullRef(ref m)) m.Direction = Vector2.Zero;
        return held >= Ticks(st.Seconds) ? StepResult.Done : StepResult.Running;
    }

    StepResult Face(ActorRun a, FaceStep f, bool first)
    {
        ref var b = ref bodies.Get(a.Entity);
        ref var m = ref MoveOf(a, ControlChannels.Face);
        if (Unsafe.IsNullRef(ref m)) return StepResult.Refused(StepRefusal.NoTarget);
        float yaw;
        if (f.Toward.Degrees is { } deg) yaw = deg * DetMath.DegToRad;
        else if (!Where(f.Toward, b.Feet, out var to, out _)) return StepResult.Refused(StepRefusal.NoTarget);
        else
        {
            float dx = to.X - b.Feet.X, dy = to.Y - b.Feet.Y;
            yaw = dx * dx + dy * dy < 1e-8f ? b.Yaw : DetMath.Atan2(dy, dx);
        }
        FaceAngle(a, ref m, yaw);
        // Faced when the body's yaw is within 2° of it (the motor turns a body that is told its facing at once: the tick after).
        if (!first && MathF.Abs(DetMath.WrapAngle(b.Yaw - m.Facing)) <= 2f * DetMath.DegToRad) return StepResult.Done;
        return StepResult.Running;
    }

    /// <summary>Where a target is: an actor's feet (or the place that far from it, toward <paramref name="from"/>), or a point.</summary>
    bool Where(ScenarioTarget target, Vector3 from, out Vector3 at, out EntityId actor)
    {
        actor = EntityId.None;
        if (target.Point is { } p) { at = new Vector3(p.X, p.Y, p.Z ?? from.Z); return true; }
        if (target.Actor is { } name)
        {
            actor = Entity(name);
            if (!Feet(actor, out var feet)) { at = default; return false; }
            if (target.DistanceMetres > 0f)
            {
                var away = new Vector2(from.X - feet.X, from.Y - feet.Y);
                float len = away.Length();
                away = len > 1e-4f ? away / len : Vector2.UnitX;
                at = new Vector3(feet.X + away.X * target.DistanceMetres, feet.Y + away.Y * target.DistanceMetres, from.Z);
            }
            else at = feet;
            return true;
        }
        if (target.Degrees is { } deg)
        {
            float yaw = deg * DetMath.DegToRad;
            at = from + new Vector3(DetMath.Cos(yaw), DetMath.Sin(yaw), 0f);
            return true;
        }
        at = default;
        return false;
    }

    /// <summary>Goes toward a place: the way and the gait, braking in time for the body's own deceleration; true when within <paramref name="within"/>.</summary>
    bool Go(ActorRun a, ref MoveIntent m, in Body b, Vector3 to, Gait gait, float within, bool brake)
    {
        var delta = new Vector2(to.X - b.Feet.X, to.Y - b.Feet.Y);
        float d = delta.Length();
        m.Gait = gait;
        if (d <= within) { m.Direction = Vector2.Zero; return true; }
        var way = delta / d;
        float v = b.Velocity.X * way.X + b.Velocity.Y * way.Y;
        float stopping = 0f;
        if (brake && v > 0f)
        {
            ref var loco = ref w.Store<Locomotion>().Find(a.Entity);
            float braking = Unsafe.IsNullRef(ref loco) || loco.BrakingDeceleration <= 0f ? 8f : loco.BrakingDeceleration;
            stopping = v * v / (2f * braking) + v * World.TickSeconds;
            // A body on the spring stops by the spring.
            if (w.TryStore<StartStopSpring>(out var springs) && springs.Has(a.Entity) && springs.Get(a.Entity).HalfLife > 0f)
                stopping = BodyMotor.SpringStoppingAlong(springs.Get(a.Entity), v, way);
        }
        m.Direction = d - stopping <= within * 0.5f ? Vector2.Zero : way;
        return false;
    }

    // MoveTo and path are ScenarioDirector.Routes.cs's (MoveToRouted, PathRouted): straight as before while the line is clear.

    StepResult KeepDistance(ActorRun a, KeepDistanceStep k)
    {
        ref var b = ref bodies.Get(a.Entity);
        ref var m = ref MoveOf(a, ControlChannels.Move | ControlChannels.Face);
        if (Unsafe.IsNullRef(ref m) || !Feet(Or(k.From, a.Foe), out var from)) return StepResult.Refused(StepRefusal.NoTarget);
        var delta = new Vector2(from.X - b.Feet.X, from.Y - b.Feet.Y);
        float d = delta.Length();
        var toward = d > 1e-4f ? delta / d : Vector2.UnitX;
        m.Gait = k.Gait;
        m.Direction = d > k.Metres + k.ToleranceMetres ? toward : d < k.Metres - k.ToleranceMetres ? -toward : Vector2.Zero;
        FaceToward(a, ref m, b.Feet, from);
        return StepResult.Running;
    }

    StepResult Retreat(ActorRun a, RetreatStep r)
    {
        ref var b = ref bodies.Get(a.Entity);
        ref var m = ref MoveOf(a, ControlChannels.Move | ControlChannels.Face);
        if (Unsafe.IsNullRef(ref m) || !Feet(Or(r.From, a.Foe), out var from)) return StepResult.Refused(StepRefusal.NoTarget);
        var delta = new Vector2(b.Feet.X - from.X, b.Feet.Y - from.Y);
        float d = delta.Length();
        FaceToward(a, ref m, b.Feet, from);
        m.Gait = r.Gait;
        if (d >= r.Metres) { m.Direction = Vector2.Zero; return StepResult.Done; }
        m.Direction = d > 1e-4f ? delta / d : -Vector2.UnitX;
        return StepResult.Running;
    }

    StepResult Strafe(ActorRun a, StrafeStep s)
    {
        ref var b = ref bodies.Get(a.Entity);
        ref var m = ref MoveOf(a, ControlChannels.Move | ControlChannels.Face);
        if (Unsafe.IsNullRef(ref m) || !Feet(Or(s.Facing, a.Foe), out var at)) return StepResult.Refused(StepRefusal.NoTarget);
        var delta = new Vector2(at.X - b.Feet.X, at.Y - b.Feet.Y);
        float d = delta.Length();
        var toward = d > 1e-4f ? delta / d : new Vector2(DetMath.Cos(b.Yaw), DetMath.Sin(b.Yaw));
        var left = new Vector2(-toward.Y, toward.X);
        m.Gait = s.Gait;
        m.Direction = s.Side == StepSide.Left ? left : -left;
        FaceToward(a, ref m, b.Feet, at);
        return StepResult.Running;
    }

    StepResult Circle(ActorRun a, CircleStep c)
    {
        ref var b = ref bodies.Get(a.Entity);
        ref var m = ref MoveOf(a, ControlChannels.Move | ControlChannels.Face);
        if (Unsafe.IsNullRef(ref m) || !Feet(Or(c.Around, a.Foe), out var centre)) return StepResult.Refused(StepRefusal.NoTarget);
        var r = new Vector2(b.Feet.X - centre.X, b.Feet.Y - centre.Y);
        float d = r.Length();
        var outward = d > 1e-4f ? r / d : Vector2.UnitX;
        var ccw = new Vector2(-outward.Y, outward.X);
        var tangent = c.Direction == StepSide.Left ? ccw : -ccw;
        float radial = Math.Clamp((d - c.RadiusMetres) / 0.5f, -1f, 1f);
        var way = tangent - outward * radial;
        float len = way.Length();
        m.Gait = c.Gait;
        m.Direction = len > 1e-4f ? way / len : tangent;
        FaceToward(a, ref m, b.Feet, centre);
        return StepResult.Running;
    }

    // ---- fighting ----

    /// <summary>Whether a body may swing now: no swing busy or waiting, no stun, no stagger, no dodge, no block, attack let go last tick, not stopped.</summary>
    bool Ready(ActorRun a)
    {
        var e = a.Entity;
        ref var c = ref fighters.Find(e);
        if (Unsafe.IsNullRef(ref c)) return false;
        if (c.Busy > 0f || c.Pending != AttackStrength.None || c.Stun > 0f || c.Blocking || c.WasAttack || a.AttackLatch) return false;
        if (w.TryStore<Stagger>(out var staggers))
        {
            ref var st = ref staggers.Find(e);
            if (!Unsafe.IsNullRef(ref st) && (st.Pending || tick < st.Until)) return false;
        }
        if (w.TryStore<Dodging>(out var dodging))
        {
            ref var dg = ref dodging.Find(e);
            if (!Unsafe.IsNullRef(ref dg) && dg.Phase != DodgePhase.None) return false;
        }
        return !IsStopped(e);
    }

    StepResult Attack(ActorRun a, Track t, Instr instr, AttackStep at)
    {
        var e = a.Entity;
        ref var c = ref intents.Find(e);
        if (Unsafe.IsNullRef(ref c) || !fighters.Has(e)) return StepResult.Refused(StepRefusal.NoTarget);
        a.Written |= instr.Channels;
        var target = Or(at.Target, a.Foe);
        if (at.AimAt is { Region: not null, Actor: null } && target.IsNone) return StepResult.Refused(StepRefusal.NoTarget);
        // The aim, at the press and while held.
        if (t.Run.Sub == 0 && tick == t.Run.Began) t.Run.Value = bodies.Get(e).Yaw;
        AimFor(a, at.AimAt, at.Direction, target, t.Run.Value, ref c, out var regionAimed);
        c.Direction = at.Direction;
        a.DirectionLatch = at.Direction;
        if (t.Run.Sub == 0)
        {
            // Waiting until the body may swing again (at most readySeconds), or pressed at once.
            if (at.WaitReady && !Ready(a))
            {
                c.Attack = false;
                a.AttackLatch = false;
                if (tick - t.Run.Began >= Ticks(at.ReadySeconds)) return StepResult.TimedOut("never ready", AimWords(at), null);
                return StepResult.Running;
            }
            // A press is a press: attack let go for a tick first when it was held (the last blow's button, or a latch).
            if (fighters.Get(e).WasAttack || a.AttackLatch)
            {
                c.Attack = false;
                a.AttackLatch = false;
                return StepResult.Running;
            }
            t.Run.Sub = 1;
            t.Run.Mark = tick;
            ref var f = ref fighters.Get(e);
            a.PendingAim = new PendingAim(f.Swings, target, regionAimed, at.AimAt is { Region: { } rg } aimAt ? RegionWords(rg, aimAt.Side) : "", t.Number, instr.Number, at.Label, tick, t.Run.Instance);
        }
        long holdTicks = at.HoldSeconds is { } h ? Math.Max(1, Ticks(h))
            : at.Strength == AttackStrength.Strong ? (long)MathF.Ceiling(((w.TryService<CombatSettings>(out var cs) ? cs.StrongAfter : 0.3f) + 0.05f) * World.TicksPerSecond)
            : 1;
        // A refusal by the rules on a press (seen the tick after): the step is refused and the button let go.
        if (tick > t.Run.Mark && Refused(e))
        {
            ReleaseAttack(a, t);
            return StepResult.Refused(StepRefusal.RulesRefused);
        }
        if (tick - t.Run.Mark < holdTicks)
        {
            c.Attack = true;
            a.AttackLatch = true;
            return StepResult.Running;
        }
        ReleaseAttack(a, t);
        return StepResult.Done;
    }

    bool Refused(EntityId e)
    {
        if (!w.TryService<EventQueue<AttackRefused>>(out var q)) return false;
        var list = q.LastTick;
        for (int i = 0; i < list.Count; i++) if (list[i].Entity == e && list[i].Why != AttackRefusal.Stunned) return true;   // a stunned press is buffered, not refused
        return false;
    }

    void ReleaseAttack(ActorRun a, Track t)
    {
        ref var c = ref intents.Find(a.Entity);
        if (!Unsafe.IsNullRef(ref c)) c.Attack = false;
        a.AttackLatch = false;
        a.Written |= ControlChannels.Attack;
    }

    /// <summary>
    /// The blow's aim: toward a region of an actor's posed hit volumes, a point, or angles — the yaw by the facing, the pitch
    /// as the duelling brain works its own (<see cref="SwingAim"/>: a cut's level sweep at <see cref="SwingAim.SweepHeight"/>, a
    /// thrust from the eyes), so the aim offset clamps and eases it as every aim. Null: level, the facing kept.
    /// </summary>
    void AimFor(ActorRun a, ScenarioAim? aim, AttackDirection direction, EntityId target, float yawAtStart, ref CombatIntent c, out BodyRegion region)
    {
        region = BodyRegion.None;
        var e = a.Entity;
        ref var b = ref bodies.Get(e);
        if (aim is null) { c.Aims = true; c.AimPitch = 0f; a.AimsLatch = true; a.PitchLatch = 0f; return; }
        if (aim.Region is null && aim.Point is null)
        {
            c.Aims = true;
            c.AimPitch = (aim.PitchDegrees ?? 0f) * DetMath.DegToRad;
            if (aim.YawDegrees is { } yaw)
            {
                ref var m = ref moves.Find(e);
                if (!Unsafe.IsNullRef(ref m)) FaceAngle(a, ref m, yawAtStart + yaw * DetMath.DegToRad);
            }
            a.AimsLatch = true; a.PitchLatch = c.AimPitch;
            return;
        }
        if (!AimPoint(e, in b, aim, target, out var point, out region)) return;
        c.Aims = true;
        c.AimPitch = PitchToward(e, in b, point, direction);
        a.AimsLatch = true; a.PitchLatch = c.AimPitch;
        ref var mv = ref moves.Find(e);
        if (!Unsafe.IsNullRef(ref mv)) FaceToward(a, ref mv, b.Feet, point);
    }

    /// <summary>The pitch (radians) of a blow aimed at <paramref name="point"/>, worked as <see cref="SwingAim"/> works the brain's.</summary>
    internal float PitchToward(EntityId self, in Body b, Vector3 point, AttackDirection direction)
    {
        var sa = w.TryService<SwingAim>(out var s) ? s : DefaultAim;
        float dx = point.X - b.Feet.X, dy = point.Y - b.Feet.Y;
        float d = MathF.Max(sa.LeastDistance, MathF.Sqrt(dx * dx + dy * dy));
        float from = direction == AttackDirection.Thrust ? b.Feet.Z + SwingAim.Top(w, self, in b) * sa.ThrustFromShare : b.Feet.Z + sa.SweepHeight;
        return DetMath.Atan2(point.Z - from, d);
    }

    static readonly SwingAim DefaultAim = new();

    /// <summary>The point an aim names: a point, or the middle of a region's posed hit volumes on the actor (the side nearer the aimer when none is given; without volumes, the region's share of the actor's height).</summary>
    internal bool AimPoint(EntityId self, in Body b, ScenarioAim aim, EntityId target, out Vector3 point, out BodyRegion region)
    {
        region = BodyRegion.None;
        if (aim.Point is { } p)
        {
            Feet(target.IsNone ? self : target, out var tf);
            point = new Vector3(p.X, p.Y, p.Z ?? tf.Z + 1.2f);
            return true;
        }
        var who = aim.Actor is { } name ? Entity(name) : target;
        ref var tb = ref bodies.Find(who);
        if (Unsafe.IsNullRef(ref tb) || aim.Region is not { } r) { point = default; return false; }
        var (leftR, rightR) = Sides(r);
        BodyRegion want = aim.Side == StepSide.Left ? leftR : aim.Side == StepSide.Right ? rightR : leftR;
        if (w.TryService<HitVolumes>(out var volumes))
        {
            var posed = volumes.Posed(w, who);
            if (posed.Length > 0)
            {
                if (aim.Side is null && leftR != rightR)
                {
                    // Both sides: the nearer to the aimer.
                    var l = Middle(posed, leftR, out int nl);
                    var rr = Middle(posed, rightR, out int nr);
                    if (nl > 0 && nr > 0) want = Vector2.DistanceSquared(new(l.X, l.Y), new(b.Feet.X, b.Feet.Y)) <= Vector2.DistanceSquared(new(rr.X, rr.Y), new(b.Feet.X, b.Feet.Y)) ? leftR : rightR;
                    else want = nl > 0 ? leftR : rightR;
                }
                var mid = Middle(posed, want, out int n);
                if (n > 0) { point = mid; region = want; return true; }
            }
        }
        // No volumes: the region's height on the body's capsule.
        float top = SwingAim.Top(w, who, in tb);
        point = new Vector3(tb.Feet.X, tb.Feet.Y, tb.Feet.Z + top * HeightShare(r));
        region = want;
        return true;
    }

    static Vector3 Middle(ReadOnlySpan<HitVolume> posed, BodyRegion region, out int n)
    {
        var sum = Vector3.Zero;
        n = 0;
        foreach (var v in posed)
        {
            if (v.Region != region) continue;
            sum += (v.Capsule.A + v.Capsule.B) * 0.5f;
            n++;
        }
        return n > 0 ? sum / n : Vector3.Zero;
    }

    static (BodyRegion Left, BodyRegion Right) Sides(AimRegion r) => r switch
    {
        AimRegion.Head => (BodyRegion.Head, BodyRegion.Head),
        AimRegion.Neck => (BodyRegion.Neck, BodyRegion.Neck),
        AimRegion.Chest => (BodyRegion.Chest, BodyRegion.Chest),
        AimRegion.Belly => (BodyRegion.Belly, BodyRegion.Belly),
        AimRegion.Hips => (BodyRegion.Hips, BodyRegion.Hips),
        AimRegion.UpperArm => (BodyRegion.LeftUpperArm, BodyRegion.RightUpperArm),
        AimRegion.Forearm => (BodyRegion.LeftForearm, BodyRegion.RightForearm),
        AimRegion.Hand => (BodyRegion.LeftHand, BodyRegion.RightHand),
        AimRegion.Thigh => (BodyRegion.LeftThigh, BodyRegion.RightThigh),
        AimRegion.Shin => (BodyRegion.LeftShin, BodyRegion.RightShin),
        _ => (BodyRegion.LeftFoot, BodyRegion.RightFoot),
    };

    static float HeightShare(AimRegion r) => r switch
    {
        AimRegion.Head => 0.93f, AimRegion.Neck => 0.85f, AimRegion.Chest => 0.72f, AimRegion.Belly => 0.6f, AimRegion.Hips => 0.5f,
        AimRegion.UpperArm => 0.75f, AimRegion.Forearm => 0.6f, AimRegion.Hand => 0.5f, AimRegion.Thigh => 0.38f, AimRegion.Shin => 0.2f,
        _ => 0.05f,
    };

    /// <summary>A pressed blow's swing begun: <see cref="AimedBlow"/> links it to the step and the region aimed (for the region met against the region aimed).</summary>
    void AimedBlowSeen(ActorRun a)
    {
        if (a.PendingAim is not { } p) return;
        ref var f = ref fighters.Find(a.Entity);
        if (Unsafe.IsNullRef(ref f)) { a.PendingAim = null; return; }
        if (f.Swings > p.SwingsBefore)
        {
            w.Events<AimedBlow>().Publish(new AimedBlow(a.Entity, tick, f.Swing, p.Target, p.Region, p.Asked, p.Track, p.Step, p.Label, p.Instance));
            a.PendingAim = null;
        }
        else if (tick - p.Tick > 2 * World.TicksPerSecond) a.PendingAim = null;
    }

    StepResult Block(ActorRun a, Track t, Instr instr, BlockStep b)
    {
        ref var c = ref intents.Find(a.Entity);
        if (Unsafe.IsNullRef(ref c)) return StepResult.Refused(StepRefusal.NoTarget);
        a.Written |= ControlChannels.Block;
        if (instr.Until is { } until && tick > t.Run.Began && until.Matches(w, tick) > 0) { c.Block = false; return StepResult.Done; }
        c.Block = true;
        return StepResult.Running;
    }

    StepResult Parry(ActorRun a, Track t, ParryStep p)
    {
        var e = a.Entity;
        ref var c = ref intents.Find(e);
        var foe = Or(p.Against, a.Foe);
        ref var foeC = ref fighters.Find(foe);
        ref var own = ref fighters.Find(e);
        if (Unsafe.IsNullRef(ref c) || Unsafe.IsNullRef(ref foeC) || Unsafe.IsNullRef(ref own) || anims is null) return StepResult.Refused(StepRefusal.NoTarget);
        a.Written |= ControlChannels.Block;
        ref var fa = ref anims.Find(foe);
        if (t.Run.Sub == 0) { t.Run.Index = foeC.Swing; t.Run.Sub = 1; }
        bool swinging = !Unsafe.IsNullRef(ref fa) && ActionRoles.IsAttack(fa.Action) && !fa.ActionEnding && foeC.StrikeAt >= 0f && fa.ActionRate > 0f;
        if (t.Run.Sub == 1)
        {
            c.Block = false;
            // The foe's next blow, before its strike; a blow held at its ready point is waited for (the brain's own timing).
            if (foeC.Swing == t.Run.Index || !swinging || fa.ActionTime >= foeC.StrikeAt) return StepResult.Running;
            if (foeC.Readied && ActionChannel.AtReady(fa)) return StepResult.Running;
            float due = (foeC.StrikeAt - fa.ActionTime) / fa.ActionRate;
            if (due > p.LeadSeconds) return StepResult.Running;
            if (own.Blocking) return StepResult.Running;   // let go this tick: a parry is a fresh press
            t.Run.Sub = 2;
            t.Run.Mark = tick;
            t.Run.Index = foeC.Swing;
            c.Block = true;
            return StepResult.Running;
        }
        // Held until the blow's strike has passed (and a little), or its swing is over; at most 1.5 s.
        c.Block = true;
        bool past = foeC.Swing != t.Run.Index || !swinging || fa.ActionTime >= foeC.StrikeAt;
        if (past && t.Run.Since <= 0) t.Run.Since = tick;
        if (t.Run.Since > 0 && tick - t.Run.Since >= Ticks(0.1f) || tick - t.Run.Mark >= Ticks(1.5f))
        {
            c.Block = false;
            return StepResult.Done;
        }
        return StepResult.Running;
    }

    StepResult Cancel(ActorRun a)
    {
        var e = a.Entity;
        ref var c = ref intents.Find(e);
        if (Unsafe.IsNullRef(ref c)) return StepResult.Refused(StepRefusal.NoTarget);
        a.Written |= ControlChannels.Attack;
        c.Attack = false;
        a.AttackLatch = false;
        // The cancel key's own call (PlayerControl's feint): a swing in its wind-up, or a press held or buffered.
        if (!SwingCancelCalls.Can(w, e, SwingCancelCause.Cancel)) return StepResult.Refused(StepRefusal.RulesRefused);
        SwingCancelCalls.Do(w, e, SwingCancelCause.Cancel);
        return StepResult.Done;
    }

    StepResult Dodge(ActorRun a, Track t, DodgeStep d)
    {
        var e = a.Entity;
        if (!w.TryStore<DodgeIntent>(out var dodges)) return StepResult.Refused(StepRefusal.NotBuilt);
        a.Written |= ControlChannels.Dodge | ControlChannels.Move;
        ref var m = ref moves.Find(e);
        if (!Unsafe.IsNullRef(ref m)) m.Direction = Vector2.Zero;
        if (t.Run.Sub == 0)
        {
            ref var b = ref bodies.Get(e);
            var fwd = new Vector2(DetMath.Cos(b.Yaw), DetMath.Sin(b.Yaw));
            var way = d.Way switch
            {
                DodgeWay.Forward => fwd,
                DodgeWay.Left => new Vector2(-fwd.Y, fwd.X),
                DodgeWay.Right => new Vector2(fwd.Y, -fwd.X),
                _ => Vector2.Zero,   // back from the facing
            };
            ref var di = ref dodges.Find(e);
            if (Unsafe.IsNullRef(ref di)) w.Set(e, new DodgeIntent { Pressed = true, Way = way });
            else { di.Pressed = true; di.Way = way; }
            t.Run.Sub = 1;
            return StepResult.Running;
        }
        ref var dn = ref dodges.Find(e);
        if (!Unsafe.IsNullRef(ref dn)) dn.Pressed = false;
        // The dodge's rules answered in the last tick's Act: begun or refused.
        if (w.TryService<EventQueue<DodgeRefused>>(out var refused))
        {
            var list = refused.LastTick;
            for (int i = 0; i < list.Count; i++) if (list[i].Entity == e) return StepResult.Refused(StepRefusal.RulesRefused);
        }
        return StepResult.Done;
    }

    StepResult UsePower(ActorRun a, Track t, UsePowerStep u)
    {
        var e = a.Entity;
        if (!w.TryStore<PowerIntent>(out var presses)) return StepResult.Refused(StepRefusal.NoSlot);
        a.Written |= ControlChannels.Power;
        if (t.Run.Sub == 1)
        {
            ref var pn = ref presses.Find(e);
            if (!Unsafe.IsNullRef(ref pn) && pn.Slot == t.Run.Index) pn.Slot = 0;
            return StepResult.Done;
        }
        int slot = u.Slot;
        if (slot <= 0 && !FindSlot(e, u.Power, out slot)) return StepResult.Refused(StepRefusal.NoSlot);
        if (slot < 1 || slot > QuickSlots.Count) return StepResult.Refused(StepRefusal.NoSlot);
        ref var p = ref presses.Find(e);
        if (Unsafe.IsNullRef(ref p)) w.Set(e, new PowerIntent { Slot = slot });
        else p.Slot = slot;
        t.Run.Sub = 1;
        t.Run.Index = slot;
        return StepResult.Running;
    }

    /// <summary>The quick slot whose view shows <paramref name="power"/> (a power's id: <c>celerity</c> matches the catalogue's <c>celerity.active</c>).</summary>
    bool FindSlot(EntityId e, string? power, out int slot)
    {
        slot = 0;
        if (power is null || !w.TryStore<PowerSlots>(out var views) || !w.TryService<IPowerCatalogue>(out var catalogue)) return false;
        ref var v = ref views.Find(e);
        if (Unsafe.IsNullRef(ref v)) return false;
        for (int s = 0; s < QuickSlots.Count; s++)
        {
            int index = v.Slots[s].Power;
            if (index < 0 || index >= catalogue.Count || v.Slots[s].State == SlotState.Empty) continue;
            if (PowerMatches(catalogue.Id(index), power)) { slot = s + 1; return true; }
        }
        return false;
    }

    /// <summary>Whether a catalogue's option id is the power named (<c>celerity</c> for <c>celerity.active</c>; the id itself).</summary>
    public static bool PowerMatches(string id, string power) =>
        string.Equals(id, power, StringComparison.OrdinalIgnoreCase)
        || id.Length > power.Length && id[power.Length] == '.' && id.StartsWith(power, StringComparison.OrdinalIgnoreCase);

    // ---- the digest ----

    /// <summary>The director's state: whether it ended, each actor's cursors and latches, each trigger's state, the markers seen.</summary>
    public void Digest(ref Fnv1a64 h)
    {
        h.Add(Ended ? 1 : 0);
        h.Add(EndTick);
        h.Add(nextId);
        foreach (var a in actors)
        {
            h.Add(a.Entity.Packed);
            h.Add((a.HandedOver ? 1 : 0) | (a.StandAfter ? 2 : 0) | (a.ThenDone ? 4 : 0) | (a.Stopped ? 8 : 0) | (a.AttackLatch ? 16 : 0) | (a.FacesLatch ? 32 : 0) | (a.AimsLatch ? 64 : 0));
            h.Add((int)a.DirectionLatch);
            h.Add(a.FaceLatch);
            h.Add(a.PitchLatch);
            h.Add((int)a.Script | ((int)a.Ai << 8) | ((int)a.Muted << 16));
            foreach (var t in a.Tracks) t.Digest(ref h);
            h.Add(a.Interrupts.Count);
            foreach (var t in a.Interrupts) t.Digest(ref h);
        }
        foreach (var t in triggers)
        {
            h.Add(t.State.Fired);
            h.Add(t.State.LastFired);
            h.Add(t.State.Count);
            h.Add(t.State.WasTrue ? 1 : 0);
        }
        h.Add(markers.Count);
    }

    // ---- the runtime's records ----

    internal enum InstrKind { Step, RepeatBegin, RepeatEnd }

    internal sealed record Instr
    {
        public InstrKind Kind;
        public ScenarioStep Step = null!;
        public int Number, Slot, Other, Parent = -1;
        public ControlChannels Channels;
        public EventMatcher? On, Until;
    }

    /// <summary>A step's running state (digested).</summary>
    internal struct StepRun
    {
        public long Entered, ReadyAt, Began, Mark, Since, Instance;
        public int Sub, Index;
        public float Value;
        public bool Running, Touched;
    }

    internal sealed class Track(int number, Instr[] code, int slots)
    {
        public readonly int Number = number;
        public readonly Instr[] Code = code;
        public readonly int[] Left = new int[slots], Iter = new int[slots];
        public readonly long[] RepeatInstance = new long[slots];
        public int Pc, Trigger = -1;
        public long Cause;
        public bool Ended;
        public StepRun Run;
        public readonly RouteRun Route = new();   // a moveTo's route and walk (ScenarioDirector.Routes.cs)

        public void Digest(ref Fnv1a64 h)
        {
            h.Add(Pc);
            h.Add(Ended ? 1 : 0);
            h.Add(Run.Entered); h.Add(Run.ReadyAt); h.Add(Run.Began); h.Add(Run.Mark); h.Add(Run.Since); h.Add(Run.Instance);
            h.Add(Run.Sub); h.Add(Run.Index); h.Add(Run.Value);
            h.Add((Run.Running ? 1 : 0) | (Run.Touched ? 2 : 0));
            foreach (int l in Left) h.Add(l);
            foreach (int i in Iter) h.Add(i);
            foreach (long r in RepeatInstance) h.Add(r);
            h.Add(Cause);
            Route.Digest(ref h);   // nothing unless a route is being walked
        }
    }

    internal readonly record struct PendingAim(int SwingsBefore, EntityId Target, BodyRegion Region, string Asked, int Track, int Step, string? Label, long Tick, long Instance);

    internal sealed class ActorRun
    {
        public ActorControl Control = null!;
        public string Name = "";
        public EntityId Entity, Foe;
        public Track[] Tracks = [];
        public readonly List<Track> Interrupts = [];
        public bool HandedOver, StandAfter, ThenDone, Stopped, Published;
        public ControlChannels Script, Ai, Muted, Written;
        // What the script left the channels at, written while it holds them and no step writes them.
        public bool AttackLatch, FacesLatch, AimsLatch;
        public AttackDirection DirectionLatch;
        public float FaceLatch, PitchLatch;
        public PendingAim? PendingAim;
    }

    internal struct TriggerState
    {
        public int Fired, Count;
        public long LastFired;
        public bool WasTrue;
    }

    internal sealed class TriggerRun
    {
        public ScenarioTrigger Trigger = null!;
        public EventMatcher? Event, Count;
        public Observable? Value;
        public List<(string Actor, ScenarioStep Step, Instr[] Code, int Slots)> Steps = [];
        public TriggerState State;
    }
}
