// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A lip-sync track: which of the nine mouth shapes a recorded line calls for, a frame at a time.
//
// Plain text beside the recording in `Sounds\Dialog`, the recording's name with `.nlp` for its
// extension. The engine reads one after **every** sound it loads,
// and a sound with no track simply has none. The shipped files are a spreadsheet's export:
//
//     Frame#,Mouth
//     0,I
//     1,I
//     2,D
//     ...
//     347,<none>
//
// How the game reads it, which is what this reader does:
//
// - A line at a time, `fgets` into 511 characters (0x1ff), opened "rb" so every line keeps
//   its `\r\n`.
// - **The first line is read and thrown away**, whatever it says — it is the header.
// - Every later line is scanned as `"%d,%c"`. Two fields and it is a frame; anything else
//   is skipped. **The frame number is read and ignored**: the shapes go into the track in file order,
//   one a line, and the track runs at 30 a second.
// - The letter goes through `tolower` and `- 'a'`, as a byte; anything past `i` — which is where
//   `<none>`'s `<` lands — is **shape 0**, the letter `a`.
// - The read stops at end of file, tested straight after each `fgets`: a last line with no newline
//   after it reaches end of file in its own read and **is dropped**.
// - At most 5,000 frames (`LIPSYNC_MAXFRAMES`) — 2 min 46 s.
namespace Vitomir.Formats;

/// <summary>Reads a `.nlp` lip-sync track (see the file comment).</summary>
public static class Nlp
{
    /// <summary>The mouth shapes a set can name: the letters `a` to `i` (`MOUTH_NUMSHAPES`).</summary>
    public const int Shapes = 9;

    /// <summary>How many frames a track may have (`LIPSYNC_MAXFRAMES`).</summary>
    public const int MaxFrames = 5000;

    /// <summary>How long a line `fgets` takes at once: its 0x1ff less the terminator.</summary>
    const int LineChars = 0x1ff - 1;

    /// <summary>
    /// The track's shapes, 0 to 8, in file order. Empty when the file has no frame lines — which is
    /// what the engine keeps as "no track" (a frame count of 0 and the sound's has-track flag clear).
    /// </summary>
    public static byte[] Read(ReadOnlySpan<byte> data)
    {
        var shapes = new List<byte>();
        int at = 0;
        if (!Line(data, ref at, out _, out bool eof) || eof) return [];
        while (shapes.Count < MaxFrames)
        {
            if (!Line(data, ref at, out var line, out eof) || eof) break;
            if (Scan(line) is { } letter) shapes.Add(Shape(letter));
        }
        return [.. shapes];
    }

    /// <summary>A letter's shape: <c>(byte)(tolower(c) - 'a')</c>, and 0 past the ninth.</summary>
    public static byte Shape(byte letter)
    {
        int c = letter is >= (byte)'A' and <= (byte)'Z' ? letter + 32 : letter;
        byte shape = (byte)(c - 'a');
        return shape < Shapes ? shape : (byte)0;
    }

    // `fgets`: up to the newline (kept) or LineChars characters. False when nothing was left to read;
    // `eof` when the read ran into the end of the file.
    static bool Line(ReadOnlySpan<byte> data, ref int at, out ReadOnlySpan<byte> line, out bool eof)
    {
        line = default;
        eof = false;
        if (at >= data.Length) { eof = true; return false; }
        int start = at;
        while (at < data.Length && at - start < LineChars)
            if (data[at++] == (byte)'\n') { line = data[start..at]; return true; }
        line = data[start..at];
        eof = at >= data.Length;
        return true;
    }

    // `sscanf(line, "%d,%c")` answering 2: the letter, or null.
    static byte? Scan(ReadOnlySpan<byte> s)
    {
        int i = 0;
        while (i < s.Length && s[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or 0x0b or 0x0c) i++;
        if (i < s.Length && s[i] is (byte)'+' or (byte)'-') i++;
        int digits = i;
        while (i < s.Length && s[i] is >= (byte)'0' and <= (byte)'9') i++;
        if (i == digits) return null;
        if (i >= s.Length || s[i] != (byte)',') return null;
        i++;
        if (i >= s.Length) return null;
        return s[i];
    }
}
