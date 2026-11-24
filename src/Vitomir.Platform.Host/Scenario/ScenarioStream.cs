// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Powers;
using Vitomir.Platform.Control;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>
/// The scenario's event stream, <c>events/p000-r003.events.jsonl</c> (schema <c>VITOMIR_EVENT_TRACE_2</c>): the
/// event trace's head (build, game, modules' configuration hashes, seed, level, every clip's active window, every person's sheet
/// purchased and effective) plus the run's identity and its actors' ids and names; then a line an event, as trace 1 writes it
/// (<c>kind</c>, <c>tick</c>, <c>seconds</c>, <c>event</c>: the record as published, people as <c>e3</c>) plus <c>runId</c>, <c>seed</c>,
/// <c>eventId</c> (its place in the run, 0 up), <c>parentEventId</c> (the event it follows from), <c>actorId</c> (who did it: <c>a0</c>),
/// <c>actors</c> (each person field by name) and <c>derived</c> (the fields the selectors may use that the record has not:
/// <c>region</c>, <c>side</c>, <c>aimedRegion</c>, <c>regionAsAimed</c>, <c>weaponClass</c>, <c>verdict</c>, <c>power</c>).
/// The kinds: <see cref="EventTrace.Kinds"/> and <c>CombatTempo</c>.
/// </summary>
public static class ScenarioStream
{
    static readonly JsonSerializerOptions Json = EventTrace.NewJson();
    static readonly string[] DerivedNames = ["region", "side", "aimedRegion", "regionAsAimed", "weaponClass", "verdict", "power", "fixed",   // fixed
        .. ScenarioEventLog.ReceiptNames];   // the receipts' derived fields (ScenarioEventLog.Receipts.cs)

    /// <summary>The head's own fields after the trace's: the run's identity and the actors' ids.</summary>
    public static void HeadExtra(Utf8JsonWriter w, ScenarioPlan plan, ScenarioPlanPoint point, string runId, ulong seed,
        IReadOnlyList<EntityId> fighters, IReadOnlyDictionary<EntityId, string> names)
    {
        w.WriteString("runId", runId);
        w.WriteString("scenario", Path.GetFileName(plan.File));
        w.WriteString("scenarioSha256", plan.Sha256);
        w.WriteNumber("point", point.Index);
        w.WriteString("pointName", point.Name);
        w.WriteStartObject("values");
        foreach (var (path, value) in point.Values) w.WriteString(path, value);
        w.WriteEndObject();
        w.WriteNumber("runSeed", seed);
        w.WriteStartArray("actors");
        for (int i = 0; i < fighters.Count; i++)
        {
            w.WriteStartObject();
            w.WriteString("actorId", $"a{i.ToString(CultureInfo.InvariantCulture)}");
            w.WriteString("id", EventTrace.Id(fighters[i]));
            w.WriteString("name", names.TryGetValue(fighters[i], out var n) ? n : "");
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    /// <summary>Writes a run's stream: the head, then every event of the log.</summary>
    public static void Write(string path, string head, ScenarioEventLog log, string runId, ulong seed)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = new StreamWriter(path) { NewLine = "\n" };
        file.WriteLine(head);
        var actorIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in log.Events)
        {
            if (e.By is { } by && !actorIds.ContainsKey(by)) actorIds[by] = log.ActorId(by);
            using var buffer = new MemoryStream();
            using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            {
                w.WriteStartObject();
                w.WriteString("kind", e.Kind);
                w.WriteNumber("tick", e.Tick);
                w.WriteNumber("seconds", Math.Round(e.Seconds, 4));
                w.WritePropertyName("event");
                JsonSerializer.SerializeToElement(e.Raw, e.Raw.GetType(), Json).WriteTo(w);
                w.WriteString("runId", runId);
                w.WriteNumber("seed", seed);
                w.WriteNumber("eventId", e.Id);
                if (e.Parent is { } p) w.WriteNumber("parentEventId", p);
                else w.WriteNull("parentEventId");
                if (e.By is { } b) w.WriteString("actorId", actorIds[b]);
                w.WriteStartObject("actors");
                foreach (var (field, name) in e.Actors ?? new Dictionary<string, string>()) w.WriteString(field, name);
                w.WriteEndObject();
                bool any = false;
                foreach (var d in DerivedNames)
                {
                    if (!e.Fields.TryGetValue(d, out var v) || v is null) continue;
                    if (d == "fixed" && v is false) continue;   // written for a fixed contact's harm or verdict only (native streams unchanged)
                    if (e.Raw.GetType().GetProperty(char.ToUpperInvariant(d[0]) + d[1..]) is not null && d != "verdict" && d != "power") continue;
                    if (!any) { w.WriteStartObject("derived"); any = true; }
                    switch (v)
                    {
                        case bool bv: w.WriteBoolean(d, bv); break;
                        case double dv: w.WriteNumber(d, dv); break;
                        default: w.WriteString(d, Convert.ToString(v, CultureInfo.InvariantCulture)); break;
                    }
                }
                if (any) w.WriteEndObject();
                w.WriteEndObject();
            }
            file.WriteLine(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
        }
    }
}

/// <summary>
/// The per-tick state trace, <c>ticks/p000-r000.ticks.jsonl</c> (when the scenario asks: <c>outputs.ticks</c>): every n ticks each
/// actor's feet (m), facing (degrees counter-clockwise from +X), velocity (m/s) and ground speed, Health, Blood, live powers, distance
/// to its foe (m, feet to feet), the step it is on and who holds its channels (the director's last word). Not the world's state.
/// </summary>
public sealed class ScenarioTicks(World world, IReadOnlyList<EntityId> actors, IReadOnlyList<string> names, BoutResult result,
    ScenarioEventLog log, ArenaCast cast, int every)
{
    readonly List<TickLine> lines = [];
    readonly Dictionary<string, string> step = new(StringComparer.Ordinal);
    readonly Dictionary<string, Dictionary<string, string>> channels = new(StringComparer.Ordinal);

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    // ---- the player-input actor's receipts (B1/CP-04): the frame fed in, the last line's feet
    ActionFrame frame, before;
    readonly Dictionary<EntityId, (Vector3 Feet, long Tick)> lastLine = [];

    /// <summary>The frame fed into the tick about to be stepped (call before <c>Session.Step</c>; the player-input actor's receipts read it).</summary>
    public void Frame(in ActionFrame next) { before = frame; frame = next; }

    TickPlayer? Player(EntityId e, in Body body)
    {
        if (!world.TryStore<Control.CameraRig>(out var rigs) || !rigs.Has(e)) return null;
        var rig = rigs.Get(e);
        static float D(float rad) => MathF.Round(rad * 180f / MathF.PI, 3);
        static string[] Names(Buttons b) => b == Buttons.None ? [] : [.. b.ToString().Split(", ").Select(n => char.ToLowerInvariant(n[0]) + n[1..])];
        var input = new TickInput(R(frame.Move.X), R(frame.Move.Y), D(frame.Look.X), D(frame.Look.Y), Names(frame.Held), Names(ButtonEdges.Pressed(frame.Held, before.Held)));
        TickCamera? camera = null;
        TickAim? aim = null;
        if (world.TryService<Cameras.ViewState>(out var view) && view.HasView && view.Of == e)
        {
            var v = view.View;
            string kind = view.Kind == Control.ViewKind.First ? "firstPerson" : rig.Shoulder >= 0 ? "rightShoulder" : "leftShoulder";
            camera = new TickCamera(kind, [R(v.Eye.X), R(v.Eye.Y), R(v.Eye.Z)], D(v.Yaw), D(v.Pitch), R(v.FieldOfViewDegrees),
                view.Kind == Control.ViewKind.First ? null : R(view.Boom), view.Kind == Control.ViewKind.First ? null : D(rig.Orbit));
            var f = v.Forward;
            if (world.TryService<Cameras.ReticleState>(out var ret))
            {
                var a = ret.Answer;
                string meets = a.Kind switch { ReticleTarget.Level => "level", ReticleTarget.Nothing => "nothing", _ => "actor" };
                string? who = null;
                for (int k = 0; k < actors.Count; k++) if (actors[k].Packed == a.Entity && a.Entity != 0) who = names[k];
                aim = new TickAim([R(v.Eye.X), R(v.Eye.Y), R(v.Eye.Z)], [R(f.X), R(f.Y), R(f.Z)], meets, meets == "actor" ? who ?? a.Name : null, a.Part,
                    a.Kind == ReticleTarget.Nothing ? null : [R(a.Point.X), R(a.Point.Y), R(a.Point.Z)], a.Kind == ReticleTarget.Nothing ? null : R(a.Distance), a.Blocked);
            }
        }
        // What the controls asked of the body this tick (its move intent) and what it did.
        var wantWay = Vector2.Zero; string? gait = null; float wantSpeed = 0f;
        if (world.TryStore<MoveIntent>(out var intents) && intents.Has(e))
        {
            var mi = intents.Get(e);
            wantWay = mi.Direction.LengthSquared() > 1e-8f ? Vector2.Normalize(mi.Direction) : Vector2.Zero;
            gait = mi.Gait.ToString().ToLowerInvariant();
            if (wantWay != Vector2.Zero && world.TryStore<Locomotion>(out var locos) && locos.Has(e))
            {
                var lo = locos.Get(e);
                wantSpeed = mi.Gait switch { Gait.Walk => lo.WalkSpeed, Gait.Sprint => lo.SprintSpeed, _ => lo.RunSpeed } * MathF.Min(1f, mi.Direction.Length());
            }
        }
        var requested = new TickMove([R(wantWay.X), R(wantWay.Y)], gait, R(wantSpeed));
        float[]? moved = null; float? over = null; float speedOver = 0f;
        if (lastLine.TryGetValue(e, out var last) && world.Tick > last.Tick)
        {
            var dxy = new Vector2(body.Feet.X - last.Feet.X, body.Feet.Y - last.Feet.Y);
            over = R((world.Tick - last.Tick) / (float)World.TicksPerSecond);
            moved = [R(dxy.X), R(dxy.Y), R(body.Feet.Z - last.Feet.Z)];
            speedOver = dxy.Length() / over.Value;
        }
        lastLine[e] = (body.Feet, world.Tick);
        float along = body.Velocity.X * wantWay.X + body.Velocity.Y * wantWay.Y;
        var actual = new TickMove([R(body.Velocity.X), R(body.Velocity.Y)], null, R(MathF.Sqrt(body.Velocity.X * body.Velocity.X + body.Velocity.Y * body.Velocity.Y)), moved, over,
            wantWay == Vector2.Zero ? null : R(along));
        TickBlock? blocked = null;
        if (wantSpeed > 0.1f && along < 0.5f * wantSpeed && world.TryService<LevelSpace>(out var space))
        {
            var from = body.Feet + new Vector3(0, 0, 0.9f);
            if (space.Collision.SphereSweep(from, new Vector3(wantWay.X, wantWay.Y, 0f), 0.6f, 0.3f) is { } hit)
                blocked = new TickBlock("level", [R(hit.Point.X), R(hit.Point.Y), R(hit.Point.Z)], [R(hit.Normal.X), R(hit.Normal.Y), R(hit.Normal.Z)],
                    "derived: under half the requested speed along the requested way; a sphere 0.3 m round swept 0.6 m from the waist along it met the level");
            else if (body.State != BodyState.Ground)
                blocked = new TickBlock("air", [], [], "derived: under half the requested speed and not on the ground (the controls steer little in the air)");
        }
        return new TickPlayer(input, camera, aim, requested, actual, blocked,
            ["the mouse pointer's place (the frame's pointer is the centre while captured)", "another body in the way (BlockedBy names only the level)", "the camera's own collision with the level is in its eye, not said apart"]);
    }
    // ---- end

    /// <summary>After each tick: the director's steps and channels from its events, and a line when due.</summary>
    public void Collect()
    {
        for (int i = log.Events.Count - log.Added; i < log.Events.Count; i++)
        {
            var e = log.Events[i];
            if (e.By is not { } by) continue;
            switch (e.Raw)
            {
                case StepBegan sb: step[by] = ScenarioTally.Step(sb.Kind) + (sb.Label is null ? "" : $" \"{sb.Label}\""); break;
                case StepEnded: step.Remove(by); break;
                case ControlChanged cc:
                    channels[by] = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["script"] = cc.Script.ToString().ToLowerInvariant(), ["ai"] = cc.Ai.ToString().ToLowerInvariant(), ["muted"] = cc.Muted.ToString().ToLowerInvariant(),
                    };
                    break;
            }
        }
        if (world.Tick % every == 0) Sample();
    }

    /// <summary>A line now.</summary>
    public void Sample()
    {
        world.TryStore<Body>(out var bodies);
        world.TryStore<Vitals>(out var vitals);
        world.TryStore<Animation.CharacterAnimation>(out var anims);
        world.TryStore<Control.Combatant>(out var fighters);
        var list = new List<TickActor>(actors.Count);
        for (int i = 0; i < actors.Count; i++)
        {
            var e = actors[i];
            Vector3 feet = default, vel = default;
            float yaw = 0;
            if (bodies is not null && bodies.Has(e)) { var b = bodies.Get(e); (feet, vel, yaw) = (b.Feet, b.Velocity, b.Yaw); }
            int? health = null, blood = null;
            if (vitals is not null)
            {
                ref var v = ref vitals.Find(e);
                if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref v))
                {
                    if (v.HealthMax > 0) health = (int)MathF.Round(v.Health);
                    if (v.HasBlood) blood = (int)MathF.Round(v.Blood);
                }
            }
            float? toFoe = null;
            int foe = i < cast.Fighters.Count ? cast.Fighters[i].Foe : -1;
            if (bodies is not null && foe >= 0 && foe < actors.Count && foe != i && bodies.Has(actors[foe]))
            {
                var q = bodies.Get(actors[foe]).Feet;
                toFoe = MathF.Round(MathF.Sqrt((feet.X - q.X) * (feet.X - q.X) + (feet.Y - q.Y) * (feet.Y - q.Y)), 3);
            }
            string live = i < result.Fighters.Length ? result.Fighters[i].LiveNow() : "";
            // The action playing (the action channel's role: AttackQuick, Block, Hurt, …) and how far into it (s); the locomotion's role
            // as the gait; whether a blow is held ready (attack held).
            string? action = null, gait = null, phase = null;
            bool held = false;
            if (anims is not null && anims.Has(e))
            {
                var an = anims.Get(e);
                gait = an.Role.ToString();
                if (an.Action != ActionRole.None) { action = an.Action.ToString(); phase = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{an.ActionTime:0.###} s"); }
            }
            if (fighters is not null && fighters.Has(e)) held = fighters.Get(e).AttackHeld >= 0f;
            list.Add(new TickActor(names[i], [R(feet.X), R(feet.Y), R(feet.Z)], R(yaw * 180f / MathF.PI), [R(vel.X), R(vel.Y), R(vel.Z)],
                R(MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y)), gait, action, phase, null, held, health, blood, live.Length > 0 ? live : null, toFoe,
                step.TryGetValue(names[i], out var s) ? s : null, channels.TryGetValue(names[i], out var c) ? c : null,
                bodies is not null && bodies.Has(e) ? Player(e, bodies.Get(e)) : null));   // the player-input actor's receipts
        }
        lines.Add(new TickLine(world.Tick, Math.Round(world.Tick / (double)World.TicksPerSecond, 4), list));
    }

    static float R(float x) => MathF.Round(x, 3);

    public void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = new StreamWriter(path) { NewLine = "\n" };
        foreach (var l in lines) file.WriteLine(JsonSerializer.Serialize(l, Json));
    }
}

/// <summary>results.json's and batch.json's writer: camelCase, nulls left out, enumerations by name, numbers to nine significant places.</summary>
public static class ScenarioJson
{
    public static readonly JsonSerializerOptions Options = Make();

    static JsonSerializerOptions Make()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        o.Converters.Add(new RoundDouble());
        return o;
    }

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options) + "\n";

    sealed class RoundDouble : JsonConverter<double>
    {
        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDouble();
        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) { writer.WriteNullValue(); return; }
            double r = value == 0 ? 0 : Math.Round(value, Math.Clamp(8 - (int)Math.Floor(Math.Log10(Math.Abs(value))), 0, 12));
            writer.WriteNumberValue(r);
        }
    }
}
