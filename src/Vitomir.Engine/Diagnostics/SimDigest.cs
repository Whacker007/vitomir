// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The replay harness's proof: ours entirely — 1.1 has no such thing.
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Diagnostics;

/// <summary>
/// **The simulation at the end of one frame, as the replay harness compares it** (ours;
/// <c>--record</c>, <c>--replay</c> and <c>--replay-check</c>). The frame's own fields (<see cref="Header"/>): the game clock, the
/// next GUID the place will hand out, how far each of the launch's random streams has gone (<see cref="Chance"/>:
/// its draws, the shared generator's state, a hash of <see cref="Random"/>'s own), a hash of the story flags
/// set and one of the scripts' timers. Then a row for each thing in the place (<see cref="Things"/>, by GUID):
/// its template, position, orientation, health, flags, mind's mode and actor's action, and then what else a
/// save keeps of it, most of it hashed (<see cref="SimDigest.Version"/>). Floats are written to round-trip,
/// so two digests agree only when every bit does.
/// </summary>
public sealed class DigestFrame
{
    /// <summary>The frame of the tape this is the end of (1 is the first).</summary>
    public int Frame { get; init; }
    /// <summary>The frame's own fields, in their order: name → value.</summary>
    public List<KeyValuePair<string, string>> Header { get; init; } = [];
    /// <summary>Each thing's row by GUID: its fields, name → value, in their order.</summary>
    public SortedDictionary<int, string[]> Things { get; init; } = [];
    /// <summary>The version of the digest this frame was read from, or taken at (<see cref="SimDigest.Version"/>).</summary>
    public int Version { get; init; } = SimDigest.Version;

    /// <summary>A thing's row as a digest file writes it (<c>t &lt;guid&gt; template=… pos=… …</c>).</summary>
    public static string Row(int guid, string[] fields) => $"t {guid.ToString(CultureInfo.InvariantCulture)} {string.Join(' ', fields)}";
}

/// <summary>Where a replay first left its recording: the frame, the thing (or the frame's own fields) and the field.</summary>
public readonly record struct Divergence(int Frame, string What, string Field, string Recorded, string Replayed)
{
    public override string ToString() =>
        $"frame {Frame}: {What} {Field} differs — recorded {Recorded}, replayed {Replayed}";
}

/// <summary>
/// Takes, writes, reads and compares <see cref="DigestFrame"/>s. A digest file (<c>*.digest</c>, beside its
/// tape) starts <see cref="Magic"/>; each frame is an <c>f</c> line with the frame's fields, then a <c>t</c> line
/// for every thing whose row changed since the frame before (every thing on the first frame) and an
/// <c>x</c> line for every thing gone — so a still prop costs one line for the whole run.
/// </summary>
public static class SimDigest
{
    /// <summary>
    /// The format's version. **2** added to a thing's row what a save keeps of it and the
    /// row did not: <c>body</c> (a hash of any thing's sector, render and physics flags, model, way of moving,
    /// velocity and spin, state, material frame, expiry and collision word, and a lying item's flags and
    /// amount), <c>actor</c> (its two flag words, team and stance), <c>stats</c> (a hash of every stat
    /// and soak with its base and bounds, the experience, the cash and the speeds), <c>pack</c> (how many
    /// items it carries and a hash of each one's name, flags, amount, place and cell, and a weapon's rounds),
    /// <c>arts</c> (its disciplines) and, for any thing, <c>fx</c> (how many effects are on it and a hash of
    /// them). Until then a change that moved only a stat, the blood pool or an effect went unseen until it
    /// changed what somebody did. A version 1 digest is still read: its rows are compared field for field
    /// and the fields it never had (<see cref="Since2"/>) are left out. **3** added the menus' trail: an
    /// <c>m</c> line for each step through the menus (a screen shown or put away, a button clicked, a box ticked, a
    /// row picked, a bar stepped; <see cref="MenuTrail"/>), so a recording of the menus alone is checked too. A
    /// digest before 3 has none, and its replay's trail is not compared.
    /// </summary>
    public const int Version = 3;
    /// <summary>The first line of every digest; the number is the format's version.</summary>
    public const string Magic = "vitomir-digest 3";
    const string MagicStem = "vitomir-digest ";
    /// <summary>The fields of a thing's row that version 2 added.</summary>
    public static readonly string[] Since2 = ["body", "actor", "stats", "pack", "arts", "fx"];

    /// <summary>The version a digest's first line names (1 or <see cref="Version"/>), or 0 when it is not a digest's.</summary>
    public static int VersionOf(string? firstLine) =>
        firstLine is not null && firstLine.StartsWith(MagicStem, StringComparison.Ordinal)
        && int.TryParse(firstLine.AsSpan(MagicStem.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int v)
        && v >= 1 && v <= Version ? v : 0;

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"{F(v.X)},{F(v.Y)},{F(v.Z)}";

    /// <summary>
    /// The place at the end of frame <paramref name="frame"/>. <paramref name="scripts"/> is the scripts'
    /// own field (the app's, since the engine cannot see the scripting runtime), or null for none.
    /// </summary>
    public static DigestFrame Take(Location place, int frame, long clockMs, string? scripts = null)
    {
        var header = new List<KeyValuePair<string, string>>
        {
            new("clock", clockMs.ToString(CultureInfo.InvariantCulture)),
            new("guid", place.NextGuid.ToString(CultureInfo.InvariantCulture)),
        };
        if (Chance.Game is { } s)
        {
            void Draws(string name, Rng rng) => header.Add(new(name, rng.Draws.ToString(CultureInfo.InvariantCulture)));
            Draws("ai", s.Ai); Draws("combat", s.Combat); Draws("motions", s.Motions); Draws("particles", s.Particles);
            header.Add(new("lcg", "0x" + s.ParticleLcg.State.ToString("x8", CultureInfo.InvariantCulture)));
            Draws("explosions", s.Explosions); Draws("decals", s.Decals); Draws("sounds", s.Sounds);
            Draws("console", s.Console); Draws("treasure", s.Treasure);
            header.Add(new("lights", RandomState(s.Lights)));
            header.Add(new("detours", RandomState(s.Detours)));
            header.Add(new("dice", RandomState(s.NetDice)));
        }
        if (place.Minds is { } minds && !ReferenceEquals(minds.Random, Chance.Game?.Ai))
            header.Add(new("minds", minds.Random.Draws.ToString(CultureInfo.InvariantCulture)));
        // Not `place.Treasure`, which makes the tables on the first look (two draws of the treasure stream):
        // a digest that did so would change the run it describes. The field, as it stands.
        treasureField ??= typeof(Location).GetField("treasure", BindingFlags.Instance | BindingFlags.NonPublic);
        if (treasureField?.GetValue(place) is Rpg.Treasure treasure)
            header.Add(new("loot", "0x" + treasure.Random.State.ToString("x8", CultureInfo.InvariantCulture)));
        if (place.Session?.Chronicle is { } chronicle)
            header.Add(new("story", Hash(string.Join(",", chronicle.SetFlags().Order()))));
        if (scripts is not null) header.Add(new("scripts", scripts));

        var modes = new Dictionary<WorldThing, Mind>(ReferenceEqualityComparer.Instance);
        foreach (var m in place.Minds?.All ?? []) modes[m.Thing] = m;
        var things = new SortedDictionary<int, string[]>();
        foreach (var t in place.Things)
        {
            if (t.Removed) continue;
            var actor = t.Actor;
            var row = new List<string>(12)
            {
                "template=" + Escape(t.Template),
                "pos=" + V(t.Position),
                "turn=" + V(t.Orientation),
                "hp=" + (actor is not null ? F(actor.Health) : F(t.ContainerHealth)),
                "flags=0x" + t.Flags.ToString("x", CultureInfo.InvariantCulture),
                "mode=" + (modes.TryGetValue(t, out var mind) ? AiModes.Name(mind.Mode) : "-"),
                "action=" + (actor is not null ? actor.Action.ToString() : "-"),
            };
            Body(row, t);
            if (actor is not null) Character(row, actor);
            Effects(row, t);
            // Two things of one GUID would be a fault of their own; the digest keeps the later, and says so in its row.
            if (!things.TryAdd(t.Guid, [.. row])) things[t.Guid] = [.. row, "twice=1"];
        }
        return new DigestFrame { Frame = frame, Header = header, Things = things };
    }

    static string Escape(string text) => ReplayTape.Escape(text);

    /// <summary>
    /// What a save keeps of a character beyond the row's first fields (<c>SaveCapture.OfActor</c> is the list
    /// this follows): the flag words in the clear, the rest hashed. A hash says that something differs and
    /// not what; the field's name says where to look.
    /// </summary>
    static void Character(List<string> row, Actor a)
    {
        row.Add($"actor=0x{a.Flags.ToString("x", CultureInfo.InvariantCulture)},0x{a.Flags2.ToString("x", CultureInfo.InvariantCulture)},{a.Team.ToString(CultureInfo.InvariantCulture)},{(int)a.Stance}");
        var h = new Fnv();
        foreach (var v in a.Stat) h.Add(v);
        foreach (var v in a.BaseStat) h.Add(v);
        foreach (var v in a.StatMin) h.Add(v);
        foreach (var v in a.StatMax) h.Add(v);
        foreach (var v in a.Soak) h.Add(v);
        foreach (var v in a.BaseSoak) h.Add(v);
        h.Add(a.MaxHealth); h.Add(a.Experience); h.Add(a.SpendableExperience); h.Add(a.Cash);
        h.Add(a.MoveSpeedScale); h.Add(a.AttackSpeedScale); h.Add(a.WalkSpeed); h.Add(a.RunSpeed);
        h.Add(a.Perception); h.Add(a.Stealth);
        row.Add("stats=" + h);

        h = new Fnv();
        int items = 0;
        foreach (var item in a.Inventory.Items)
        {
            if (item.Destroyed) continue;
            items++;
            h.Add(item.Name); h.Add((uint)item.Flags); h.Add(item.Amount); h.Add(item.Location);
            h.Add(item.Cell.X); h.Add(item.Cell.Y); h.Add(ReferenceEquals(item, a.Held));
            if (item.Weapon is { } w)
            {
                h.Add(w.CurrentAttack);
                foreach (var attack in w.Attacks) h.Add(attack.Ammo);
            }
        }
        if (a.Held is { } held && !a.Inventory.Items.Contains(held)) { items++; h.Add(held.Name); h.Add(held.Amount); }
        row.Add($"pack={items.ToString(CultureInfo.InvariantCulture)}:{h}");

        if (a.Disciplines.Count == 0) return;
        h = new Fnv();
        foreach (var d in a.Disciplines.All)
        {
            h.Add(d.Name); h.Add(d.Level); h.Add(d.QuickSlot); h.Add(d.Flags); h.Add(d.CastCount);
            h.Add(d.RecastUntil); h.Add(d.Active);
        }
        h.Add(a.Disciplines.Current?.Name ?? "");
        row.Add("arts=" + h);
    }

    /// <summary>What a save keeps of any thing beyond the row's first fields (<c>SaveCapture.OfThing</c>), hashed.</summary>
    static void Body(List<string> row, WorldThing t)
    {
        var h = new Fnv();
        h.Add(t.Sector); h.Add((uint)t.RenderFlags); h.Add(t.Model); h.Add((int)t.MoveType); h.Add((uint)t.PhysicsFlags);
        h.Add(t.Velocity.X); h.Add(t.Velocity.Y); h.Add(t.Velocity.Z);
        h.Add(t.RotVelocity.X); h.Add(t.RotVelocity.Y); h.Add(t.RotVelocity.Z);
        h.Add(t.State); h.Add(t.ExpireAt); h.Add(t.Frame); h.Add((uint)t.Collide); h.Add(t.SpawnedBy);
        if (t.Item is { } item) { h.Add((uint)item.Flags); h.Add(item.Amount); h.Add(item.Location); }
        row.Add("body=" + h);
    }

    /// <summary>The effects on a thing, as a save keeps them (<c>SaveCapture.OfEffects</c>); nothing for a thing with none.</summary>
    static void Effects(List<string> row, WorldThing t)
    {
        var h = new Fnv();
        int n = 0;
        foreach (var e in t.Effects.All)
        {
            if (e.Spent) continue;
            n++;
            h.Add(e.Definition.Name); h.Add(e.Level); h.Add(e.Value); h.Add(e.Until); h.Add(e.NextTick);
            h.Add(e.Fade); h.Add(e.Guid); h.Add(e.CreatorGuid); h.Add(e.Flags); h.Add(e.IntParam); h.Add(e.FloatParam);
        }
        if (n > 0) row.Add($"fx={n.ToString(CultureInfo.InvariantCulture)}:{h}");
    }

    /// <summary>A 64-bit FNV-1a over values, to the bit for floats: no allocation for a row that did not change.</summary>
    struct Fnv
    {
        ulong state;
        bool started;
        void Byte(byte b) { if (!started) { state = 14695981039346656037UL; started = true; } state = (state ^ b) * 1099511628211UL; }
        public void Add(uint v) { Byte((byte)v); Byte((byte)(v >> 8)); Byte((byte)(v >> 16)); Byte((byte)(v >> 24)); }
        public void Add(int v) => Add((uint)v);
        public void Add(long v) { Add((uint)v); Add((uint)(v >> 32)); }
        public void Add(float v) => Add((uint)BitConverter.SingleToInt32Bits(v));
        public void Add(double v) => Add(BitConverter.DoubleToInt64Bits(v));
        public void Add(bool v) => Byte(v ? (byte)1 : (byte)0);
        public void Add(string v) { foreach (char c in v) { Byte((byte)c); Byte((byte)(c >> 8)); } Byte(0xff); }
        public override readonly string ToString() => (started ? state : 14695981039346656037UL).ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>FNV-1a over the text's UTF-8, eight hex digits: the same in every process (unlike <see cref="string.GetHashCode()"/>).</summary>
    public static string Hash(string text)
    {
        uint h = 2166136261;
        foreach (var b in Encoding.UTF8.GetBytes(text)) { h ^= b; h *= 16777619; }
        return h.ToString("x8", CultureInfo.InvariantCulture);
    }

    static FieldInfo? treasureField;
    static FieldInfo? implField, prngField, seedsField, nextField, nextPField;
    /// <summary>
    /// A hash of a <see cref="Random"/>'s state, read from inside it (the seeded generator's 56-word table and its
    /// two indices), since it offers no way to ask. Answers <c>?</c> if a later .NET has moved them.
    /// </summary>
    public static string RandomState(Random random)
    {
        try
        {
            implField ??= typeof(Random).GetField("_impl", BindingFlags.Instance | BindingFlags.NonPublic);
            var impl = implField?.GetValue(random);
            if (impl is null) return "?";
            prngField ??= impl.GetType().GetField("_prng", BindingFlags.Instance | BindingFlags.NonPublic);
            var prng = prngField?.GetValue(impl);
            if (prng is null) return "?";
            var type = prng.GetType();
            seedsField ??= type.GetField("_seedArray", BindingFlags.Instance | BindingFlags.NonPublic);
            nextField ??= type.GetField("_inext", BindingFlags.Instance | BindingFlags.NonPublic);
            nextPField ??= type.GetField("_inextp", BindingFlags.Instance | BindingFlags.NonPublic);
            if (seedsField?.GetValue(prng) is not int[] seeds || nextField is null || nextPField is null) return "?";
            var sb = new StringBuilder();
            sb.Append(nextField.GetValue(prng)).Append(',').Append(nextPField.GetValue(prng));
            foreach (var v in seeds) sb.Append(',').Append(v.ToString(CultureInfo.InvariantCulture));
            return Hash(sb.ToString());
        }
        catch (Exception e) when (e is FieldAccessException or ArgumentException or TargetException)
        {
            return "?";
        }
    }

    /// <summary>Compares two frames: the first field of the frame's own that differs, else the first thing's (by GUID).</summary>
    public static Divergence? Compare(DigestFrame recorded, DigestFrame replayed)
    {
        int frame = recorded.Frame;
        if (recorded.Frame != replayed.Frame)
            return new(frame, "the frame", "number", recorded.Frame.ToString(CultureInfo.InvariantCulture), replayed.Frame.ToString(CultureInfo.InvariantCulture));
        var theirs = replayed.Header.ToDictionary(p => p.Key, p => p.Value);
        foreach (var (name, value) in recorded.Header)
        {
            var other = theirs.GetValueOrDefault(name) ?? "(none)";
            if (other != value) return new(frame, "the frame's", name, value, other);
        }
        foreach (var (name, value) in replayed.Header)
            if (!recorded.Header.Any(p => p.Key == name)) return new(frame, "the frame's", name, "(none)", value);
        foreach (var guid in recorded.Things.Keys.Union(replayed.Things.Keys).Order())
        {
            recorded.Things.TryGetValue(guid, out var a);
            replayed.Things.TryGetValue(guid, out var b);
            if (recorded.Version < 2 && b is not null) b = [.. b.Where(f => !Since2.Contains(Split(f).Name))];   // a version 1 recording never had them
            string what = $"thing {guid}" + (Field(a ?? b!, "template") is { } template ? $" ({ReplayTape.Unescape(template)})" : "");
            if (a is null) return new(frame, what, "presence", "absent", "present");
            if (b is null) return new(frame, what, "presence", "present", "absent");
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                var (fa, va) = Split(i < a.Length ? a[i] : null);
                var (fb, vb) = Split(i < b.Length ? b[i] : null);
                if (fa != fb) return new(frame, what, fa ?? fb ?? "?", a.ElementAtOrDefault(i) ?? "(none)", b.ElementAtOrDefault(i) ?? "(none)");
                if (va != vb) return new(frame, what, fa ?? "?", va ?? "(none)", vb ?? "(none)");
            }
        }
        return null;
    }

    static string? Field(string[] row, string name) =>
        row.FirstOrDefault(f => f.StartsWith(name + "=", StringComparison.Ordinal)) is { } f ? f[(name.Length + 1)..] : null;

    static (string? Name, string? Value) Split(string? field)
    {
        if (field is null) return (null, null);
        int eq = field.IndexOf('=');
        return eq < 0 ? (field, "") : (field[..eq], field[(eq + 1)..]);
    }
}

/// <summary>
/// Writes a digest file a frame at a time: the frame's line, then only the things whose row changed
/// (<see cref="SimDigest"/>). Flushed every frame.
/// </summary>
public sealed class DigestWriter : IDisposable
{
    readonly TextWriter output;
    readonly Dictionary<int, string[]> last = [];

    public DigestWriter(TextWriter output, string? comment = null)
    {
        this.output = output;
        output.NewLine = "\n";
        output.WriteLine(SimDigest.Magic);
        output.WriteLine("# the simulation at the end of each frame of a replay tape; a thing's row only when it changed");
        if (comment is not null) output.WriteLine("# " + comment.Replace('\n', ' '));
    }

    public void Write(DigestFrame frame)
    {
        output.WriteLine($"f {frame.Frame.ToString(CultureInfo.InvariantCulture)} {string.Join(' ', frame.Header.Select(p => $"{p.Key}={p.Value}"))}");
        foreach (var (guid, row) in frame.Things)
        {
            if (last.TryGetValue(guid, out var was) && was.AsSpan().SequenceEqual(row)) continue;
            output.WriteLine(DigestFrame.Row(guid, row));
            last[guid] = row;
        }
        foreach (var gone in last.Keys.Where(g => !frame.Things.ContainsKey(g)).ToList())
        {
            output.WriteLine($"x {gone.ToString(CultureInfo.InvariantCulture)}");
            last.Remove(gone);
        }
        output.Flush();
    }

    /// <summary>A step through the menus (<see cref="MenuTrail"/>): an <c>m</c> line, at the tape's frame.</summary>
    public void WriteMenu(int frame, string what)
    {
        output.WriteLine($"m {frame.ToString(CultureInfo.InvariantCulture)} {what}");
        output.Flush();
    }

    public void Dispose() => output.Dispose();
}

/// <summary>
/// **The menus' trail** (ours, digest version 3): every step a run takes through the menus, as the front end
/// reports it (<c>FrontEnd.Trail</c>) — a screen shown or put away, a button clicked, a box ticked, a row picked, a bar
/// stepped — at the tape's frame. A replay's steps are compared with the recording's one by one, so a tape of the menus
/// alone is checked (earlier, it compared nothing), and a click that lands on another button than it did is found at
/// once: the next batches move 1.1's menus' buttons about.
/// </summary>
public sealed class MenuTrail
{
    readonly List<(int Frame, string What)> recorded;
    int next;

    MenuTrail(List<(int Frame, string What)> recorded) => this.recorded = recorded;

    /// <summary>A recorded digest's trail, or null when the digest is older than its trail (version 1 or 2).</summary>
    public static MenuTrail? Read(string path)
    {
        using var input = LogFile.OpenText(path);
        if (SimDigest.VersionOf(input.ReadLine()) < 3) return null;
        var steps = new List<(int, string)>();
        for (string? line; (line = input.ReadLine()) is not null;)
        {
            if (!line.StartsWith("m ", StringComparison.Ordinal)) continue;
            int space = line.IndexOf(' ', 2);
            if (space < 0 || !int.TryParse(line.AsSpan(2, space - 2), NumberStyles.None, CultureInfo.InvariantCulture, out int frame)) continue;
            steps.Add((frame, line[(space + 1)..]));
        }
        return new MenuTrail(steps);
    }

    /// <summary>The first difference, kept once found.</summary>
    public Divergence? First { get; private set; }
    /// <summary>The steps compared and found the same.</summary>
    public int Agreed => First is null ? next : next - 1;
    /// <summary>Whether the recording took steps the replay has not.</summary>
    public bool RecordedLeft => next < recorded.Count;

    /// <summary>One replayed step: the recording's next must be it, at the same frame.</summary>
    public Divergence? Check(int frame, string what)
    {
        if (First is not null) return First;
        if (next >= recorded.Count)
            return First = new Divergence(frame, "the menus", "step", "(the recording's had ended)", what);
        var (f, w) = recorded[next++];
        if (f != frame || w != what)
            return First = new Divergence(frame, "the menus", "step", $"{w} (frame {f})", $"{what} (frame {frame})");
        return null;
    }

    /// <summary>The recording's step the replay did not take, at the end.</summary>
    public string? Missing => RecordedLeft ? $"{recorded[next].What} (frame {recorded[next].Frame})" : null;
}

/// <summary>
/// Reads a digest file back a frame at a time, each frame whole (the rows carried over from the frames before).
/// </summary>
public sealed class DigestReader : IDisposable
{
    readonly TextReader input;
    readonly SortedDictionary<int, string[]> things = [];
    string? pending;

    public DigestReader(TextReader input)
    {
        this.input = input;
        var first = input.ReadLine();
        Version = SimDigest.VersionOf(first);
        if (Version == 0) throw new InvalidDataException($"not a Vitomir digest this build reads (its first line is \"{first}\")");
    }

    /// <summary>The version of the format the file was written in (<see cref="SimDigest.Version"/>).</summary>
    public int Version { get; }

    public static DigestReader Open(string path) => new(LogFile.OpenText(path));   // plain or gzip

    /// <summary>The next frame, whole, or null at the end.</summary>
    public DigestFrame? Next()
    {
        string? line = pending ?? input.ReadLine();
        pending = null;
        while (line is not null && !line.StartsWith("f ", StringComparison.Ordinal)) line = input.ReadLine();
        if (line is null) return null;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int frame = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var header = new List<KeyValuePair<string, string>>();
        foreach (var p in parts.Skip(2))
        {
            int eq = p.IndexOf('=');
            header.Add(eq < 0 ? new(p, "") : new(p[..eq], p[(eq + 1)..]));
        }
        while ((line = input.ReadLine()) is not null)
        {
            if (line.StartsWith("f ", StringComparison.Ordinal)) { pending = line; break; }
            if (line.StartsWith("t ", StringComparison.Ordinal))
            {
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                things[int.Parse(fields[1], CultureInfo.InvariantCulture)] = fields[2..];
            }
            else if (line.StartsWith("x ", StringComparison.Ordinal))
                things.Remove(int.Parse(line.AsSpan(2).Trim(), CultureInfo.InvariantCulture));
        }
        return new DigestFrame { Frame = frame, Header = header, Things = new SortedDictionary<int, string[]>(things), Version = Version };
    }

    public void Dispose() => input.Dispose();
}

/// <summary>
/// The check a <c>--replay-check</c> makes as it goes (ours): each replayed frame against the recorded
/// digest's frame of the same number, until the first that differs (<see cref="First"/>), which is kept
/// and not looked past — what follows a divergence is its consequence.
/// </summary>
public sealed class DigestChecker : IDisposable
{
    readonly DigestReader recorded;
    DigestFrame? ahead;

    public DigestChecker(DigestReader recorded) { this.recorded = recorded; ahead = recorded.Next(); }

    /// <summary>The first divergence found, or null while the replay agrees.</summary>
    public Divergence? First { get; private set; }
    /// <summary>The frames compared and found the same.</summary>
    public int Agreed { get; private set; }
    /// <summary>Whether the recorded digest has frames the replay has not reached.</summary>
    public bool RecordedLeft => ahead is not null;

    /// <summary>Compares one replayed frame; answers the divergence the first time there is one.</summary>
    public Divergence? Check(DigestFrame replayed)
    {
        if (First is not null) return First;
        if (ahead is not null && ahead.Frame < replayed.Frame)   // a frame the recording stepped and the replay did not
            return First = new Divergence(ahead.Frame, "the frame", "stepped", "yes", "no");
        if (ahead is null || ahead.Frame != replayed.Frame)
        {
            First = new Divergence(replayed.Frame, "the frame", "stepped", ahead is null ? "(the recording had ended)" : "no", "yes");
            return First;
        }
        First = SimDigest.Compare(ahead, replayed);
        if (First is null) Agreed++;
        ahead = recorded.Next();
        return First;
    }

    public void Dispose() => recorded.Dispose();
}
