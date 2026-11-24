// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;

namespace Vitomir.Platform.Host;

/// <summary>
/// The event trace (<c>--event-trace &lt; file&gt; </c>, and the arena's taped bouts): what the rules package asked of
/// its first increments (its "first return"), as JSON lines. The first line is the run's
/// identity: the build, the game and modules with their profile and configuration hash, the seed, the level, the
/// options a recording carries (the scenario, the scripts, the switches), every attack clip's active window, and every
/// person's sheet as the module gives it (purchased and effective values). Then one line per event of the action and
/// combat stages, in the order the world published them: the attacks begun and refused (with why), the blocks, the
/// strike's moment, each contact (what it met, where, the material, the speed and impulse), each swing's end (also the
/// swings that met nothing), the repeats rejected, the protests, the harm (every stage's numbers), the zero-Health stops,
/// the masquerade's changes and risks, and each fighter's tempo when set or changed; the dodges begun, refused (with
/// why) and ended. Each line carries the tick and
/// the seconds. Not the world's state: nothing reads
/// it back, so a run with a trace is the run without one, digest for digest.
/// </summary>
public sealed class EventTrace : IDisposable
{
    /// <summary>The trace's schema, written in its first line.</summary>
    public const string Schema = "VITOMIR_EVENT_TRACE_1";

    readonly StreamWriter writer;
    readonly JsonSerializerOptions json;
    readonly List<Action<World>> sources = [];

    // ---- the scenario's stream, VITOMIR_EVENT_TRACE_2, is written by the
    // scenario runner from its event log (Scenario/ScenarioStream.cs: event ids, parents, actors by name); it shares this trace's
    // kinds (Kinds), its serialiser (NewJson) and its head (WriteHead). Trace 1 (the arena's, --event-trace) is unchanged, line for line.

    /// <summary>The scenario stream's schema (a superset of <see cref="Schema"/>).</summary>
    public const string Schema2 = "VITOMIR_EVENT_TRACE_2";

    /// <summary>The kinds trace 1 writes, in its order.</summary>
    public static readonly IReadOnlyList<Type> KindsV1 =
    [
        typeof(AttackBegun), typeof(AttackRefused), typeof(BlockRaised), typeof(BlockLowered), typeof(StrikeMoment),
        typeof(ContactMade), typeof(SwingEnded), typeof(ContactDuplicates), typeof(ProtectedProtest), typeof(CatchLetGo),
        typeof(HarmResolved), typeof(ActorDied), typeof(MasqueradeModeChanged), typeof(MasqueradeRisk),
        typeof(PowerUsed), typeof(PowerRefused), typeof(PowerEnded),     // the powers (a live window's start, end and why)
        typeof(HealthBandChanged),                                       // Health's band (labels only)
        typeof(SwingFollowThrough),                                      // a blade stopped, sprang back or went on
        typeof(AttackerStunned),                                         // a blocked blade's owner stunned
        typeof(Bodies.DodgeBegun), typeof(Bodies.DodgeRefused), typeof(Bodies.DodgeEnded),   // active Dodge
        typeof(FlurryStrike),                                            // each extra blow of a Celerity swing, its check
        typeof(Animation.GuardSolverFollows), typeof(Animation.GuardGave),   // the guard's arm solver and a struck guard's give
        typeof(AttackReleased), typeof(Staggered),                       // a blow held ready let go; the stagger
        typeof(AttackSwitched),                                          // a held blow switched to another direction
    ];

    /// <summary>
    /// The scenario stream's kinds (trace 2), in the order a tick's lines come: trace 1's, then the kinds it leaves out (the guard's
    /// verdict, parries begun, guards broken, swings cancelled, parts severed), then the scenario director's. <c>--describe events</c>
    /// lists these; the measures' log reads these (<see cref="Scenario.ScenarioEventLog"/>). The bow's are added with the bow.
    /// </summary>
    public static readonly IReadOnlyList<Type> Kinds =
    [
        typeof(FixedContactPlaced),   // a fixed contact placed — first in its tick, so it comes before its ContactMade
        typeof(BrainDecided),         // a duelling brain's decision (only in a run that asks) — before what it causes in its tick
        .. KindsV1,
        typeof(GuardResolved), typeof(ParryBegun), typeof(GuardBroken), typeof(SwingCancelled), typeof(PartSevered),
        typeof(StepBegan), typeof(StepEnded), typeof(StepTimedOut), typeof(StepRefused), typeof(StepLate),
        typeof(ControlChanged), typeof(Marker), typeof(TriggerFired), typeof(ScenarioEnded), typeof(AimedBlow),
        typeof(ArrowNocked), typeof(DrawBegan), typeof(ArrowLoosed), typeof(ArrowSpent), typeof(DrawCancelled),   // the bow's
        typeof(WeaponSwap), typeof(ShieldAgainstBow),   // the player's swap; a shield raised against a drawn bow
        typeof(ArcherAimed), typeof(ArcherToSword),     // an archer brain's aim for a shot (its error), its sword drawn up close
        typeof(MoveToRouted), typeof(MoveToFailed), typeof(ObjectiveMet), typeof(ObjectiveFailed),   // a moveTo's route or failure; the objectives (the runner's)
    ];

    /// <summary>The serialiser of the trace's events (fields included, people as <c>e3</c>, vectors as three numbers, enumerations by name).</summary>
    public static JsonSerializerOptions NewJson()
    {
        var json = new JsonSerializerOptions
        {
            IncludeFields = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        json.Converters.Add(new EntityConverter());
        json.Converters.Add(new VectorConverter());
        json.Converters.Add(new JsonStringEnumConverter());
        return json;
    }

    public EventTrace(string path)
    {
        writer = new StreamWriter(path) { AutoFlush = false, NewLine = "\n" };
        json = NewJson();
        var watch = typeof(EventTrace).GetMethod(nameof(Watch), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        foreach (var t in KindsV1) watch.MakeGenericMethod(t).Invoke(this, null);
    }

    /// <summary>Events written so far (the head not counted).</summary>
    public int Lines { get; private set; }

    void Watch<T>() where T : struct
    {
        string name = typeof(T).Name;
        sources.Add(world =>
        {
            if (!world.TryService<EventQueue<T>>(out var q)) return;
            var list = q.ThisTick;
            for (int i = 0; i < list.Count; i++) Line(name, world.Tick - 1, JsonSerializer.SerializeToElement(list[i], json));
        });
    }

    void Line(string kind, long tick, JsonElement e)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("kind", kind);
            w.WriteNumber("tick", tick);
            w.WriteNumber("seconds", Math.Round(tick / (double)World.TicksPerSecond, 4));
            w.WritePropertyName("event");
            e.WriteTo(w);
            w.WriteEndObject();
        }
        writer.WriteLine(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
        Lines++;
    }

    /// <summary>The first line: the run's identity, the clips' active windows and every person's sheet.</summary>
    public void Head(World world, TapeHeader header, IReadOnlyList<(EntityId Who, string Name)> people, string native) =>
        writer.WriteLine(HeadLine(world, header, people, native, Schema, null));

    /// <summary>
    /// The head line as text (shared with the scenario's stream, which passes <see cref="Schema2"/> and its own
    /// fields after the trace's through <paramref name="extra"/>).
    /// </summary>
    public static string HeadLine(World world, TapeHeader header, IReadOnlyList<(EntityId Who, string Name)> people, string native, string schema,
        Action<Utf8JsonWriter>? extra)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("kind", "head");
            w.WriteString("schema", schema);
            w.WriteString("evidence", native);
            w.WriteString("build", header.Build);
            w.WriteString("game", header.Game);
            w.WriteStartObject("modules");
            foreach (var (id, hash) in header.ConfigHashes) w.WriteString(id, hash);
            w.WriteEndObject();
            w.WriteNumber("seed", header.Seed);
            w.WriteString("level", header.Level);
            w.WriteString("start", header.Start);
            w.WriteNumber("ticksPerSecond", World.TicksPerSecond);
            w.WriteStartObject("notes");
            foreach (var (k, v) in header.Notes.OrderBy(p => p.Key, StringComparer.Ordinal)) w.WriteString(k, v);
            w.WriteEndObject();

            // Every attack clip's active window, by character kind (the contact's own rule; SANDBOX).
            w.WriteStartArray("activeWindows");
            if (world.TryService<CharacterAssets>(out var assets) && world.TryService<ActiveWindows>(out var windows) && world.TryService<ContactConfig>(out var config))
                for (int k = 0; k < assets.KindCount; k++)
                {
                    if (assets.Kind(k).Actions is not { } set) continue;
                    foreach (var (m, strength) in set.Quick.Select(m => (m, "quick")).Append((set.Strong, "strong")))
                    {
                        if (!m.Has) continue;
                        var win = windows.For(assets, k, m.Clip, m.StrikeSeconds, config.Window);
                        w.WriteStartObject();
                        w.WriteString("character", assets.Kind(k).Name);
                        w.WriteString("strength", strength);
                        w.WriteString("clip", assets.Clip(m.Clip).Name);
                        w.WriteNumber("clipId", m.Clip);      // AttackBegun's Clip
                        w.WriteNumber("clipSeconds", Math.Round(assets.Clip(m.Clip).Duration, 4));
                        w.WriteNumber("rate", Math.Round(m.Rate, 4));
                        w.WriteNumber("strikeSeconds", Math.Round(m.StrikeSeconds, 4));
                        w.WriteNumber("busySeconds", Math.Round(m.BusySeconds, 4));
                        w.WriteBoolean("window", win.Has);
                        if (win.Has)
                        {
                            w.WriteNumber("activeFrom", Math.Round(win.From, 4));
                            w.WriteNumber("activeTo", Math.Round(win.To, 4));
                            w.WriteNumber("strike", Math.Round(win.Strike, 4));
                            w.WriteNumber("peakSpeed", Math.Round(win.PeakSpeed, 3));
                            w.WriteBoolean("fromMotion", win.FromMotion);
                        }
                        w.WriteEndObject();
                    }
                }
            w.WriteEndArray();

            // Every person: the name, the character drawn and the sheet as the module gives it.
            world.TryService<IPersonSheets>(out var sheets);
            w.WriteStartArray("people");
            foreach (var (who, name) in people)
            {
                w.WriteStartObject();
                w.WriteString("id", Id(who));
                w.WriteString("name", name);
                if (world.Has<CharacterAnimation>(who) && assets is not null) w.WriteString("character", assets.Kind(world.Get<CharacterAnimation>(who).Kind).Name);
                if (sheets is not null && sheets.TryDescribe(who, out string sheetJson))
                {
                    w.WritePropertyName("sheet");
                    using var doc = JsonDocument.Parse(sheetJson);
                    doc.RootElement.WriteTo(w);
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            extra?.Invoke(w);
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    readonly Dictionary<EntityId, CombatTempo> tempos = [];

    /// <summary>The tick's events (call after it: the world's count has moved on, so the lines carry the tick that ran).</summary>
    public void Tick(World world)
    {
        foreach (var s in sources) s(world);
        // A fighter's tempo (the Abilities' tempo channel) when it is first set and whenever it changes.
        if (world.TryStore<CombatTempo>(out var store))
        {
            var ents = store.Entities;
            var data = store.Data;
            for (int i = 0; i < ents.Length; i++)
            {
                if (tempos.TryGetValue(ents[i], out var was) && was.Swing == data[i].Swing && was.Recovery == data[i].Recovery && was.Rise == data[i].Rise) continue;
                tempos[ents[i]] = data[i];
                Line("CombatTempo", world.Tick - 1, JsonSerializer.SerializeToElement(new { Entity = ents[i], Tick = world.Tick - 1, data[i].Swing, data[i].Recovery, data[i].Rise }, json));
            }
        }
    }

    public void Dispose()
    {
        writer.Flush();
        writer.Dispose();
    }

    /// <summary>A person's id in the trace (<c>e3</c>; <c>none</c>).</summary>
    public static string Id(EntityId e) => e.IsNone ? "none" : string.Create(CultureInfo.InvariantCulture, $"e{e.Index}");

    sealed class EntityConverter : JsonConverter<EntityId>
    {
        public override EntityId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, EntityId value, JsonSerializerOptions options) => writer.WriteStringValue(Id(value));
    }

    sealed class VectorConverter : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(Math.Round(value.X, 4));
            writer.WriteNumberValue(Math.Round(value.Y, 4));
            writer.WriteNumberValue(Math.Round(value.Z, 4));
            writer.WriteEndArray();
        }
    }
}
