// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// F12: a screenshot, the way 1.1 writes one, in the player's own folder.
using Vitomir.Engine.Diagnostics;

namespace Vitomir;

/// <summary>
/// 1.1's F12 writes **`SHOT%04d.BMP`** — the first number from a counter that
/// lives for the whole process and gives up at 10,000 — as an uncompressed Windows bitmap at the
/// screen's own depth (16 or 32 bits; 1.1 allows no other) and the screen's size,
/// 2,834 pixels a metre, into the folder the game runs in. Vitomir writes the same name and the same
/// kind of file, 32 bits, but into <c>%LOCALAPPDATA%\Vitomir\Screenshots</c>: it never writes into
/// the game's own folder (ours).
/// </summary>
static class Screenshots
{
    /// <summary>Where the search for a free name starts, kept for the process.</summary>
    static int next;

    public static string Folder => Path.Combine(Diag.UserDataFolder, "Screenshots");

    /// <summary>The next free `SHOT%04d.BMP`, or null when all 10,000 are taken.</summary>
    public static string? NextPath(string folder)
    {
        for (; next < 10000; next++)
        {
            string path = Path.Combine(folder, $"SHOT{next:0000}.BMP");
            if (!File.Exists(path)) { next++; return path; }
        }
        return null;
    }

    /// <summary>
    /// The pixels a render handed back, as RGBA (ours). A window's frames are drawn in the swapchain's
    /// format, which is B8G8R8A8 on every driver Vitomir runs on, and a download of one is in that
    /// order — so an in-game shot and <c>--shot</c> came out with red and blue exchanged (a torch's
    /// flame blue) until then. A headless run draws R8G8B8A8 and is handed back as it is.
    /// </summary>
    public static byte[] Rgba(byte[] pixels, bool bgra)
    {
        if (!bgra) return pixels;                                  // the GPU's colour format (Gpu.ColourIsBgra)
        for (int i = 0; i + 3 < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        return pixels;
    }

    /// <summary>Writes an RGBA image, top row first, as a 32-bit bottom-up BMP (the header 1.1 builds).</summary>
    public static void WriteBmp(string path, int width, int height, byte[] rgba)
    {
        using var file = File.Create(path);
        using var w = new BinaryWriter(file);
        int size = width * height * 4;
        w.Write((ushort)0x4d42); w.Write(size + 0x36); w.Write(0); w.Write(0x36);
        w.Write(0x28); w.Write(width); w.Write(height); w.Write((ushort)1); w.Write((ushort)32);
        w.Write(0); w.Write(size); w.Write(0xb12); w.Write(0xb12); w.Write(0); w.Write(0);
        for (int y = height - 1; y >= 0; y--)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                w.Write(rgba[i + 2]); w.Write(rgba[i + 1]); w.Write(rgba[i]); w.Write((byte)0);
            }
    }

    /// <summary>A shot of <paramref name="rgba"/> into the next free name; answers the path, or null.</summary>
    public static string? Take(int width, int height, byte[] rgba)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            if (NextPath(Folder) is not { } path) return null;
            WriteBmp(path, width, height, rgba);
            return path;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
