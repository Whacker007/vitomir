// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `--record-sound <file.wav>`: what the mixer played, to a file, for listening to a run afterwards.
using Vitomir.Engine.Audio;

namespace Vitomir;

/// <summary>
/// A 22,050 Hz, 16-bit stereo `.wav` of everything the mixer produced. With speakers it is a copy of
/// what they were sent; without (a headless run) it renders the mixer itself, a frame's worth at a
/// time, so the file keeps the game's own timing.
/// </summary>
sealed class SoundTape : IDisposable
{
    readonly FileStream file;
    readonly BinaryWriter writer;
    long frames;
    double owed;
    short[] buffer = [];

    public string Path { get; }

    public SoundTape(string path)
    {
        Path = path;
        if (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) is { } dir) Directory.CreateDirectory(dir);
        file = File.Create(path);
        writer = new BinaryWriter(file);
        writer.Write(new byte[44]);                                 // the header, written at the end
    }

    /// <summary>What the speakers were sent.</summary>
    public void Take(ReadOnlySpan<short> stereo)
    {
        foreach (var s in stereo) writer.Write(s);
        frames += stereo.Length / 2;
    }

    /// <summary>With no speakers: render <paramref name="seconds"/> of the mixer straight to the file.</summary>
    public void Render(Mixer mixer, float seconds)
    {
        owed += seconds * Miles.OutputRate;
        int n = (int)owed;
        if (n <= 0) return;
        owed -= n;
        if (buffer.Length < n * 2) buffer = new short[n * 2];
        mixer.Render(buffer, n);
        Take(buffer.AsSpan(0, n * 2));
    }

    public void Dispose()
    {
        long bytes = frames * 4;
        file.Position = 0;
        writer.Write("RIFF"u8); writer.Write((int)(36 + bytes)); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((short)1); writer.Write((short)2); writer.Write(Miles.OutputRate); writer.Write(Miles.OutputRate * 4);
        writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write((int)bytes);
        writer.Dispose();
        Console.WriteLine($"sound: {frames / (double)Miles.OutputRate:0.0}s written to {System.IO.Path.GetFullPath(Path)}");
    }
}
