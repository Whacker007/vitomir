// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Vitomir.Engine.Diagnostics;

/// <summary>
/// The files a play-test writes — the replay tape, its digest and its logs — opened plain or
/// gzip-compressed (ours; a play-test wrote about 200 MB an hour as plain text, and
/// they compress six to twenty-two times).
///
/// **Written compressed when the name says so**: a name ending in <c>.gz</c> is written gzip, any
/// other name plain, exactly as given — nothing is renamed behind the player's back, and the replay
/// baselines, recorded as <c>.tape</c>, stay plain. A tape's companions follow its form
/// (<see cref="Beside"/>): <c>sitting07.tape.gz</c> has its digest in <c>sitting07.tape.digest.gz</c>.
///
/// **Read either way, whatever the name** (<see cref="OpenText"/>): a file that starts with gzip's
/// two magic bytes is read through gzip, anything else as plain text, and a name that is not there
/// is looked for with <c>.gz</c> added or taken off (<see cref="Find"/>). A compressed file cut
/// short — a crash before it was closed — reads up to where it stops.
///
/// **What a crash costs.** A plain file is flushed as its writer asks (a tape each frame). A gzip
/// stream flushed that often compresses about half as well (one play-test's tape: 3.7 times
/// flushed at every frame, 6.3 flushed once a second), so a compressed file passes its writer's
/// flushes on to the compressor at most once every <see cref="FlushEvery"/> — a crash loses at most
/// that much of the end, and a closed file loses nothing.
/// </summary>
public static class LogFile
{
    /// <summary>The ending that asks for gzip.</summary>
    public const string Gzip = ".gz";

    /// <summary>How often, at most, a compressed file's writer's flushes reach the compressor.</summary>
    public static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(1);

    /// <summary>Whether <paramref name="path"/> names a compressed file.</summary>
    public static bool Compressed(string path) => path.EndsWith(Gzip, StringComparison.OrdinalIgnoreCase);

    /// <summary>The name without its <c>.gz</c>, if it has one.</summary>
    public static string Stem(string path) => Compressed(path) ? path[..^Gzip.Length] : path;

    /// <summary>
    /// A companion file beside <paramref name="path"/>, in its form: <c>a.tape</c> and <c>.digest</c>
    /// give <c>a.tape.digest</c>; <c>a.tape.gz</c> gives <c>a.tape.digest.gz</c>.
    /// </summary>
    public static string Beside(string path, string suffix) =>
        Compressed(path) ? Stem(path) + suffix + Gzip : path + suffix;

    /// <summary>
    /// The file that is there for <paramref name="path"/>: the name itself, else with <c>.gz</c> added,
    /// else with it taken off; null when none is.
    /// </summary>
    public static string? Find(string path)
    {
        if (File.Exists(path)) return path;
        if (File.Exists(path + Gzip)) return path + Gzip;
        if (Compressed(path) && File.Exists(Stem(path))) return Stem(path);
        return null;
    }

    /// <summary>
    /// A text writer on <paramref name="path"/>, UTF-8 with no byte-order mark: gzip when the name ends in
    /// <c>.gz</c>. <paramref name="append"/> adds to the file; a compressed file gets another gzip member,
    /// which every gzip reader reads on from the first.
    /// </summary>
    public static StreamWriter CreateText(string path, bool append = false)
    {
        var encoding = new UTF8Encoding(false);
        if (!Compressed(path)) return new StreamWriter(path, append, encoding);
        var file = new FileStream(path, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
        return new StreamWriter(new SparseFlush(new GZipStream(file, CompressionLevel.Optimal)), encoding);
    }

    /// <summary>
    /// A text reader on <paramref name="path"/> (or the file <see cref="Find"/> answers for it), through
    /// gzip when the file begins with gzip's magic bytes, whatever its name.
    /// </summary>
    public static StreamReader OpenText(string path)
    {
        var found = Find(path) ?? path;
        var file = new FileStream(found, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> magic = stackalloc byte[2];
        int got = file.ReadAtLeast(magic, 2, throwOnEndOfStream: false);
        file.Position = 0;
        if (got == 2 && magic[0] == 0x1f && magic[1] == 0x8b)
            return new StreamReader(new CutShort(new GZipStream(file, CompressionMode.Decompress)), Encoding.UTF8);
        return new StreamReader(file, Encoding.UTF8);
    }

    /// <summary>The lines of <paramref name="path"/>, plain or compressed.</summary>
    public static IEnumerable<string> ReadLines(string path)
    {
        using var reader = OpenText(path);
        while (reader.ReadLine() is { } line) yield return line;
    }

    /// <summary>
    /// A compressed file's stream under its writer: the writer's flushes are passed on at most once every
    /// <see cref="FlushEvery"/>, since each one ends a deflate block and a block a frame compresses badly.
    /// Closing flushes everything and writes gzip's trailer.
    /// </summary>
    sealed class SparseFlush(Stream inner) : Stream
    {
        readonly Stopwatch since = Stopwatch.StartNew();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
        public override void Flush()
        {
            if (since.Elapsed < FlushEvery) return;
            inner.Flush();
            since.Restart();
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// A compressed file read to where it stops: a file its writer never closed has no gzip trailer and may
    /// end inside a block, and what came before is still the file's.
    /// </summary>
    sealed class CutShort(Stream inner) : Stream
    {
        bool ended;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (ended) return 0;
            try { return inner.Read(buffer); }
            catch (Exception e) when (e is InvalidDataException or EndOfStreamException) { ended = true; return 0; }
        }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
