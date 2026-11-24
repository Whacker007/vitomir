// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The explosion scripts: `misc/Explosions/*.nex` ("Nihilistic EXplosion"), 36 of them, read the way
// the game reads one the first time a template names it, one line at a time.
//
// ```
// // time         CREATE          repeat     chance  timer templatename    distance     offset
// 1               DYNAON
// 1               CREATE          1          1.0    750      incendExplosion  5    0 0 -1
// 120             DYNAINTENSITY   RAMP       200.0   200
// 2500            NOP
// ```
//
// Each line is **one timed event**: a time in milliseconds from the explosion's birth, a command,
// and the command's numbers. The explosion runs them in file order as their times come due and
// removes itself when it has run the last one — so the last line's time is how long it lives, which
// is what every file's closing `NOP` is for.
//
// The reader is the engine's, down to its accidents, because two of them change what the shipped
// files do:
//
//   * **`DYNARADIUS` is not a command.** Every file's header documents it, and three files use it
//     (`flamering`, `flash`, `truefaith`), but the parser's chain of names (0x5043xx: NOP, CREATE,
//     DYNAON, DYNAOFF, DYNAINTENSITY, DYNACOLOR, DYNASTYLE, DYNAMATERIAL, SETDAMAGE, APPLYDAMAGE,
//     MODELALPHA, MODELSCALE, WALLMARK) has no such entry, so the line is dropped. The dispatcher has
//     a case for event kind 6 (the light's radius) that nothing can reach.
//   * **The last line without a newline reads the line before it.** Lines go through one 0x400-byte
//     buffer; the tokeniser stops a word at white space or a newline, *not* at the NUL
//     `fgets` leaves, so a final line with no line break runs on into whatever the previous, longer
//     line left in the buffer and picks up extra words. `alpha2.nex` ends `1010 NOP` with no
//     newline: its NOP reads five words, a NOP wants exactly two, and it is dropped — so in 1.1 that
//     explosion ends at 501 ms, not 1010. The reader keeps the buffer to reproduce it.
//
// What each command reads, and how many words it needs (the count includes the time and the
// command), is on <see cref="NexCommand"/>.
using System.Text;

namespace Vitomir.Formats;

/// <summary>
/// The event kinds, numbered as the parser writes them into an event (`event+4`) and as the
/// dispatcher switches on them.
/// </summary>
public enum NexCommand
{
    /// <summary>Nothing — a line to put the explosion's end at. Exactly two words.</summary>
    Nop = 1,
    /// <summary>
    /// `CREATE repeat chance timer template distance offsetX offsetY offsetZ` — makes a thing.
    /// Six words at least. <c>repeat</c> 0 is 1; a <c>chance</c> of 0 or less is 1.0; the timer is
    /// taken as its absolute value; a distance or an offset written <c>r…</c> is drawn at random.
    /// **The template is looked up as the file is read** (lower-cased, 31 characters) and
    /// a line naming one the game does not have is dropped.
    /// </summary>
    Create = 2,
    /// <summary>`DYNAON` — switches the thing's dynamic light on. Exactly two words.</summary>
    DynaOn = 3,
    /// <summary>`DYNAOFF` — switches it off. Exactly two words.</summary>
    DynaOff = 4,
    /// <summary>
    /// `DYNAINTENSITY INSTANT|RAMP|GAUSS value [time [peakValue [peakTime]]]` — four words at least.
    /// A GAUSS line is read (and its peak time of 0 made 1) but the dispatcher has no case for it,
    /// so it does nothing.
    /// </summary>
    DynaIntensity = 5,
    /// <summary>The light's radius. **No line can make one** — see the file comment.</summary>
    DynaRadius = 6,
    /// <summary>`DYNACOLOR INSTANT|RAMP r g b [time]` — six words at least. No GAUSS here.</summary>
    DynaColor = 7,
    /// <summary>`DYNASTYLE style` — three words at least; a style of 24 or more drops the line.</summary>
    DynaStyle = 8,
    /// <summary>`DYNAMATERIAL name` — three words at least.</summary>
    DynaMaterial = 9,
    /// <summary>
    /// `SETDAMAGE min max [pulse [type [radius]]]` — four words at least. Both amounts are taken as
    /// absolute values; a max below the min becomes min + 1; a radius of 0 becomes 64.
    /// </summary>
    SetDamage = 10,
    /// <summary>`APPLYDAMAGE` — deals the damage the last SETDAMAGE set, once. Two words at least.</summary>
    ApplyDamage = 11,
    /// <summary>`MODELALPHA INSTANT|RAMP|GAUSS value [time]` — two words at least.</summary>
    ModelAlpha = 12,
    /// <summary>`MODELSCALE INSTANT|RAMP|GAUSS value [time]` — two words at least.</summary>
    ModelScale = 13,
    /// <summary>`WALLMARK [size [time]]` — two words at least; size 64 and 20,000 ms unless given.</summary>
    WallMark = 14,
}

/// <summary>How a value gets where it is going: the second word of the four ramped commands.</summary>
public enum NexRamp { Instant = 0, Ramp = 1, Gauss = 2 }

/// <summary>
/// One event, as the engine keeps it: eleven words (0x2c bytes) whose meaning depends on the kind.
/// The reader writes them exactly where the parser does — including into a slot a rejected line
/// then leaves half-filled for the next accepted line to inherit — and the properties below are
/// the named views each kind is read through.
/// </summary>
public sealed class NexEvent
{
    /// <summary>The eleven words.</summary>
    public uint[] Raw { get; } = new uint[11];
    /// <summary>
    /// The name a CREATE's template or a DYNAMATERIAL's material was given. In the engine the first
    /// is a pointer to the template record and the second is the string itself, in the same field.
    /// </summary>
    public string Name { get; internal set; } = "";
    /// <summary>The 1-based line of the file it came from, for messages.</summary>
    public int Line { get; internal set; }

    /// <summary>When, in milliseconds after the explosion was made (the absolute value of what was written).</summary>
    public int Time => (int)Raw[0];
    /// <summary>What it does.</summary>
    public NexCommand Command => (NexCommand)Raw[1];
    /// <summary>For CREATE, which of the offsets (bits 0–2) and the distance (bit 3) are random; for SETDAMAGE, which of pulse (1), type (2) and radius (4) were given.</summary>
    public uint Flags => Raw[2];

    float F(int word) => BitConverter.UInt32BitsToSingle(Raw[word]);
    int I(int word) => (int)Raw[word];

    /// <summary>The ramped commands' mode.</summary>
    public NexRamp Mode => (NexRamp)I(3);
    /// <summary>The ramped commands' value: the intensity, the alpha or the scale.</summary>
    public float Value => F(4);
    /// <summary>A ramp's length in milliseconds (the eighth word for DYNACOLOR, the sixth otherwise).</summary>
    public int RampMs => Command == NexCommand.DynaColor ? I(7) : I(5);
    /// <summary>GAUSS's peak and when it comes — read, and never used.</summary>
    public float PeakValue => F(6);
    /// <inheritdoc cref="PeakValue"/>
    public int PeakMs => I(7);
    /// <summary>DYNACOLOR's colour (words five to seven), 0–255 a channel.</summary>
    public (float R, float G, float B) Color => (F(4), F(5), F(6));
    /// <summary>DYNASTYLE's style.</summary>
    public int Style => I(3);

    /// <summary>CREATE's offset (words five to seven) before any random draw; the direction the thing is put in.</summary>
    public (float X, float Y, float Z) Offset => (F(4), F(5), F(6));
    /// <summary>CREATE's distance.</summary>
    public float Distance => F(7);
    /// <summary>CREATE's repeat count, never 0.</summary>
    public int Repeat => I(8);
    /// <summary>CREATE's chance: 1.0 is no roll at all.</summary>
    public float Chance => F(9);
    /// <summary>CREATE's timer: the made thing's life in milliseconds, 0 to leave it as its template has it.</summary>
    public int Timer => I(10);
    /// <summary>CREATE: bit <paramref name="axis"/> (0–2) of the flags, an offset written <c>r…</c>.</summary>
    public bool RandomAxis(int axis) => (Flags & (1u << axis)) != 0;
    /// <summary>CREATE: the distance was written <c>r…</c> (flag 8).</summary>
    public bool RandomDistance => (Flags & 8) != 0;

    /// <summary>SETDAMAGE's least and most damage.</summary>
    public int MinDamage => I(4);
    /// <inheritdoc cref="MinDamage"/>
    public int MaxDamage => I(5);
    /// <summary>SETDAMAGE's pulse: milliseconds between blows, 0 for none.</summary>
    public int PulseMs => I(6);
    /// <summary>SETDAMAGE's damage type, a `DAMAGE_TYPE_*`.</summary>
    public int DamageType => I(3);
    /// <summary>SETDAMAGE's radius.</summary>
    public float Radius => F(7);

    /// <summary>WALLMARK's size (64 unless given) and how long the mark lasts (20, 000 ms unless given).</summary>
    public float MarkSize => F(3);
    /// <inheritdoc cref="MarkSize"/>
    public int MarkMs => I(4);

    internal void CopyFrom(NexEvent slot)
    {
        Array.Copy(slot.Raw, Raw, Raw.Length);
        Name = slot.Name;
        Line = slot.Line;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Time} {Command}" + (Name.Length > 0 ? $" {Name}" : "");
}

/// <summary>A `.nex` file as the engine has it once it has been read: its name and its events, in file order.</summary>
public sealed class NexFile
{
    /// <summary>The name it was loaded under — the template's `EXPLOSION_NAME`, e.g. <c>fireball.nex</c>.</summary>
    public string Name { get; init; } = "";
    /// <summary>The events the parser accepted, in file order.</summary>
    public IReadOnlyList<NexEvent> Events { get; init; } = [];
    /// <summary>The lines it read and dropped, with why: what the engine drops silently.</summary>
    public IReadOnlyList<string> Dropped { get; init; } = [];

    /// <summary>When the last event falls due: how long an explosion running this file lives.</summary>
    public int Length => Events.Count == 0 ? 0 : Events[^1].Time;

    /// <summary>The line buffer's size (`fgets` with 0x400).</summary>
    public const int LineBuffer = 0x400;
    /// <summary>The scratch table the events are parsed into before they are copied out (0x580 words): 128 events.</summary>
    public const int MaxEvents = 128;
    /// <summary>A word is cut at 32 characters and the rest read as the next word.</summary>
    public const int MaxWord = 32;
    /// <summary>What an absent word reads as (an empty string).</summary>
    const string Absent = "";

    /// <summary>
    /// Reads a file the way the game does. <paramref name="templateExists"/> answers whether a
    /// CREATE's template is in the game (looked up at read time); null takes every name
    /// as found.
    /// </summary>
    public static NexFile Read(byte[] data, string name, Func<string, bool>? templateExists = null)
    {
        var buffer = new byte[LineBuffer];
        var slots = new NexEvent[MaxEvents + 1];
        for (int i = 0; i < slots.Length; i++) slots[i] = new NexEvent();
        var dropped = new List<string>();
        int count = 0, pos = 0, lineNo = 0;
        while (pos < data.Length)
        {
            // fgets: up to 0x3ff characters, through the newline, then a NUL; the rest of the
            // buffer keeps whatever the lines before it left.
            int n = 0;
            while (n < LineBuffer - 1 && pos < data.Length)
            {
                byte b = data[pos++];
                buffer[n++] = b;
                if (b == (byte)'\n') break;
            }
            buffer[n] = 0;
            lineNo++;
            // A line with no newline in it that is not the file's last had its tail read away and
            // thrown out (more reads into a 0x80 buffer until a newline turns up).
            if (Array.IndexOf(buffer, (byte)'\n', 0, n) < 0)
                while (pos < data.Length && data[pos++] != (byte)'\n') { }
            // Skip white space; an empty line, a '#' or a "//" line is a comment.
            int s = 0;
            while (IsSpace(buffer[s])) s++;
            byte c = buffer[s];
            if (c == 0 || c == (byte)'\r' || c == (byte)'\n' || c == (byte)'#' || (c == (byte)'/' && buffer[s + 1] == (byte)'/'))
                continue;
            if (count >= MaxEvents) { dropped.Add($"line {lineNo}: past {MaxEvents} events (ours: the engine writes past its table)"); continue; }
            var words = Words(buffer);
            var slot = slots[count];
            slot.Line = lineNo;
            if (Parse(words, slot, templateExists, out string? why)) { count++; continue; }
            dropped.Add($"line {lineNo}: {why}");
        }
        var events = new NexEvent[count];
        for (int i = 0; i < count; i++) { events[i] = new NexEvent(); events[i].CopyFrom(slots[i]); }
        return new NexFile { Name = name, Events = events, Dropped = dropped };
    }

    /// <summary>The engine's absolute value, <c>(v ^ v &gt;&gt; 31) - (v &gt;&gt; 31)</c> — which leaves the most negative number as it is.</summary>
    static uint Abs(int v) => unchecked((uint)((v ^ (v >> 31)) - (v >> 31)));

    /// <summary>`isspace` in the C locale.</summary>
    static bool IsSpace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\v' or (byte)'\f' or (byte)'\r';

    /// <summary>
    /// The line's tokeniser, over the whole buffer. A word runs until white space,
    /// a newline or its 33rd character — **not** until the NUL that ends the line, which is why a
    /// last line without a newline runs on into the previous line's leftovers. The copy out of the
    /// buffer is a <c>strncpy</c>, which does stop at a NUL.
    /// </summary>
    static List<string> Words(byte[] line)
    {
        var words = new List<string>();
        int next = 0;
        while (true)
        {
            int start = next;
            while (start < LineBuffer && IsSpace(line[start])) start++;
            if (start >= LineBuffer || line[start] == 0) break;
            int at = start + 1, length = 1;
            while (true)
            {
                if (length > MaxWord || at >= LineBuffer || line[at] == (byte)'\n' || at > LineBuffer - 1) break;
                byte b = line[at++];
                if (IsSpace(b)) break;
                length++;
            }
            // strncpy(word, line + start, length + 1) and word[length] = 0: stops early at a NUL.
            int end = start;
            while (end < start + length && end < LineBuffer && line[end] != 0) end++;
            words.Add(Encoding.Latin1.GetString(line, start, end - start));
            if (at >= LineBuffer || line[at] == (byte)'\n') break;
            next = at;
        }
        return words;
    }

    /// <summary>One line into <paramref name="e"/>, as the game's line parser writes it. False drops the line.</summary>
    static bool Parse(List<string> w, NexEvent e, Func<string, bool>? templateExists, out string? why)
    {
        why = null;
        int n = w.Count;
        if (n < 2) { why = "fewer than two words"; return false; }
        int k = 0;
        string Next() => ++k < n ? w[k] : Absent;
        e.Raw[0] = Abs(NpdFile.Int(w[0]));                             // abs(atoi(time))
        string command = Next();
        static uint Bits(float f) => BitConverter.SingleToUInt32Bits(f);

        switch (command.ToUpperInvariant())
        {
            case "NOP":
                if (n != 2) { why = $"NOP with {n} words"; return false; }
                e.Raw[1] = (uint)NexCommand.Nop;
                return true;

            case "CREATE":
            {
                if (n < 6) { why = "CREATE with fewer than six words"; return false; }
                e.Raw[1] = (uint)NexCommand.Create;
                int repeat = NpdFile.Int(Next());
                e.Raw[8] = (uint)(repeat == 0 ? 1 : repeat);
                float chance = NpdFile.Float(Next());
                e.Raw[9] = Bits(chance <= 0f ? 1f : chance);
                e.Raw[10] = Abs(NpdFile.Int(Next()));
                string template = Next();
                // The template, found now or never.
                string key = template.Length > 31 ? template[..31] : template;
                if (templateExists is not null && !templateExists(key)) { why = $"no template '{template}'"; e.Raw[3] = 0; return false; }
                e.Raw[3] = 1;
                e.Name = key;
                string distance = Next();
                if (distance.Length > 0 && distance[0] == 'r') { e.Raw[2] |= 8; distance = distance[1..]; }
                else e.Raw[2] &= ~8u;
                e.Raw[7] = Bits(NpdFile.Float(distance));
                for (int axis = 0; axis < 3; axis++)
                {
                    string o = Next();
                    if (o.Length > 0 && o[0] == 'r')
                    {
                        e.Raw[2] |= 1u << axis;
                        // A bare "r" (strlen < 2) is 0; otherwise what follows it.
                        e.Raw[4 + axis] = o.Length < 2 ? 0u : Bits(NpdFile.Float(o[1..]));
                    }
                    else
                    {
                        e.Raw[2] &= ~(1u << axis);
                        e.Raw[4 + axis] = Bits(NpdFile.Float(o));
                    }
                }
                return true;
            }

            case "DYNAON":
                if (n != 2) { why = $"DYNAON with {n} words"; return false; }
                e.Raw[1] = (uint)NexCommand.DynaOn;
                return true;

            case "DYNAOFF":
                if (n != 2) { why = $"DYNAOFF with {n} words"; return false; }
                e.Raw[1] = (uint)NexCommand.DynaOff;
                return true;

            case "DYNAINTENSITY":
            {
                if (n < 4) { why = "DYNAINTENSITY with fewer than four words"; return false; }
                e.Raw[1] = (uint)NexCommand.DynaIntensity;
                if (!Mode(Next(), e, gauss: true)) { why = "DYNAINTENSITY with an unknown mode"; return false; }
                e.Raw[4] = Bits(NpdFile.Float(Next()));
                if (n > 4)
                {
                    e.Raw[5] = (uint)NpdFile.Int(Next());
                    if (n > 5)
                    {
                        e.Raw[6] = Bits(NpdFile.Float(Next()));
                        if (n > 6) e.Raw[7] = (uint)NpdFile.Int(Next());
                        if (e.Raw[7] == 0) e.Raw[7] = 1;
                    }
                }
                return true;
            }

            case "DYNACOLOR":
            {
                if (n < 6) { why = "DYNACOLOR with fewer than six words"; return false; }
                e.Raw[1] = (uint)NexCommand.DynaColor;
                if (!Mode(Next(), e, gauss: false)) { why = "DYNACOLOR with an unknown mode"; return false; }
                e.Raw[4] = Bits(NpdFile.Float(Next()));
                e.Raw[5] = Bits(NpdFile.Float(Next()));
                e.Raw[6] = Bits(NpdFile.Float(Next()));
                if (n > 6) e.Raw[7] = (uint)NpdFile.Int(Next());
                return true;
            }

            case "DYNASTYLE":
            {
                if (n < 3) { why = "DYNASTYLE with fewer than three words"; return false; }
                e.Raw[1] = (uint)NexCommand.DynaStyle;
                uint style = Abs(NpdFile.Int(Next()));
                e.Raw[3] = style;
                if (style >= 0x18) { why = $"DYNASTYLE {style} is not a style"; return false; }
                return true;
            }

            case "DYNAMATERIAL":
            {
                if (n < 3) { why = "DYNAMATERIAL with fewer than three words"; return false; }
                e.Raw[1] = (uint)NexCommand.DynaMaterial;
                // strncpy(word, 0x20), event[0x2b] = 0: the name overwrites every
                // later word of the event.
                var bytes = new byte[32];
                var src = Encoding.Latin1.GetBytes(Next());
                Array.Copy(src, bytes, Math.Min(src.Length, 32));
                bytes[31] = 0;
                for (int i = 0; i < 8; i++) e.Raw[3 + i] = BitConverter.ToUInt32(bytes, i * 4);
                e.Name = Encoding.Latin1.GetString(bytes, 0, Array.IndexOf(bytes, (byte)0));
                return true;
            }

            case "SETDAMAGE":
            {
                if (n < 4) { why = "SETDAMAGE with fewer than four words"; return false; }
                e.Raw[1] = (uint)NexCommand.SetDamage;
                e.Raw[4] = Abs(NpdFile.Int(Next()));
                uint max = (uint)NpdFile.Int(Next());
                e.Raw[5] = max < e.Raw[4] ? e.Raw[4] + 1 : max;         // unsigned compare
                e.Raw[2] = 0;
                if (n > 4)
                {
                    e.Raw[6] = (uint)NpdFile.Int(Next());
                    e.Raw[2] |= 1;
                    if (n > 5)
                    {
                        e.Raw[3] = (uint)NpdFile.Int(Next());
                        e.Raw[2] |= 2;
                        if (n > 6)
                        {
                            float radius = MathF.Abs(NpdFile.Float(Next()));
                            e.Raw[7] = Bits(radius == 0f ? 64f : radius);
                            e.Raw[2] |= 4;
                        }
                    }
                }
                return true;
            }

            case "APPLYDAMAGE":
                e.Raw[1] = (uint)NexCommand.ApplyDamage;
                return true;

            case "MODELALPHA":
            case "MODELSCALE":
            {
                e.Raw[1] = (uint)(command.Equals("MODELALPHA", StringComparison.OrdinalIgnoreCase)
                    ? NexCommand.ModelAlpha : NexCommand.ModelScale);
                if (!Mode(Next(), e, gauss: true)) { why = $"{command} with an unknown mode"; return false; }
                e.Raw[4] = Bits(NpdFile.Float(Next()));
                if (n > 4) e.Raw[5] = (uint)NpdFile.Int(Next());
                return true;
            }

            case "WALLMARK":
            {
                e.Raw[1] = (uint)NexCommand.WallMark;
                e.Raw[3] = Bits(64f);
                e.Raw[4] = 20000;
                if (n > 2)
                {
                    e.Raw[3] = Bits(NpdFile.Float(Next()));
                    if (n > 3) e.Raw[4] = Abs(NpdFile.Int(Next()));
                }
                return true;
            }
        }
        why = $"'{command}' is not a command";
        return false;
    }

    /// <summary>INSTANT, RAMP and (for all but DYNACOLOR) GAUSS, compared without regard to case; anything else drops the line.</summary>
    static bool Mode(string word, NexEvent e, bool gauss)
    {
        if (word.Equals("INSTANT", StringComparison.OrdinalIgnoreCase)) e.Raw[3] = 0;
        else if (word.Equals("RAMP", StringComparison.OrdinalIgnoreCase)) e.Raw[3] = 1;
        else if (gauss && word.Equals("GAUSS", StringComparison.OrdinalIgnoreCase)) e.Raw[3] = 2;
        else return false;
        return true;
    }
}
