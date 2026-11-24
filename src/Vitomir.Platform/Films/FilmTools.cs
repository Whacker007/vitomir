// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The two programs that improve the films, fetched from their own releases on the player's say-so, and removed on it.
using System.IO.Compression;
using System.Security.Cryptography;

namespace Vitomir.Platform.Films;

/// <summary>
/// One program the films are improved with, as its makers release it: where its zip is, its SHA-256 (a download that does not match is
/// thrown away), its size, its program's name, and the files of the zip that are kept (the rest, sample pictures and models not used,
/// are never written).
/// </summary>
public sealed record FilmToolPack(string Id, string Name, string Url, string Sha256, long Bytes, string Program, IReadOnlyList<string> Keep,
                                  string Licence, string Home);

/// <summary>
/// **The film tools** (ours): Real-ESRGAN, which makes each frame larger, and RIFE, which makes a frame between each two, both
/// portable programs that run on any graphics card with Vulkan. Vitomir never ships them: the launcher fetches each from its makers'
/// release on GitHub when the player agrees, checks it against a SHA-256 pinned here, and keeps only the files the films need in the
/// user folder's <c>Tools\</c>. <see cref="Remove"/> takes them out again with everything they left (they write nothing outside
/// their folder and the films' work folder).
/// </summary>
public sealed class FilmTools
{
    /// <summary>The two programs for this machine (Windows, else Linux), from the releases the films were tried with.</summary>
    public static IReadOnlyList<FilmToolPack> ForThisMachine() => OperatingSystem.IsWindows() ? Windows : Linux;

    static readonly string[] EsrganKeep = ["models/realesrgan-x4plus-anime.bin", "models/realesrgan-x4plus-anime.param",
                                            "models/realesr-animevideov3-x2.bin", "models/realesr-animevideov3-x2.param"];
    static readonly string[] RifeKeep = ["rife-v4.6/flownet.bin", "rife-v4.6/flownet.param"];
    const string EsrganHome = "https://github.com/xinntao/Real-ESRGAN", RifeHome = "https://github.com/nihui/rife-ncnn-vulkan";

    static readonly FilmToolPack[] Windows =
    [
        new("realesrgan", "Real-ESRGAN", "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-ncnn-vulkan-20220424-windows.zip",
            "abc02804e17982a3be33675e4d471e91ea374e65b70167abc09e31acb412802d", 45474481, "realesrgan-ncnn-vulkan.exe",
            ["realesrgan-ncnn-vulkan.exe", "vcomp140.dll", .. EsrganKeep], "BSD 3-Clause", EsrganHome),
        new("rife", "RIFE", "https://github.com/nihui/rife-ncnn-vulkan/releases/download/20221029/rife-ncnn-vulkan-20221029-windows.zip",
            "d8e4d772d26cd8006ef0ad0bc82eb191b53c68677d1ae2f42506d74cbbbea606", 431540241, "rife-ncnn-vulkan.exe",
            ["rife-ncnn-vulkan.exe", "vcomp140.dll", .. RifeKeep], "MIT", RifeHome),
    ];

    static readonly FilmToolPack[] Linux =
    [
        new("realesrgan", "Real-ESRGAN", "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-ncnn-vulkan-20220424-ubuntu.zip",
            "e5aa6eb131234b87c0c51f82b89390f5e3e642b7b70f2b9bbe95b6a285a40c96", 46931474, "realesrgan-ncnn-vulkan",
            ["realesrgan-ncnn-vulkan", .. EsrganKeep], "BSD 3-Clause", EsrganHome),
        new("rife", "RIFE", "https://github.com/nihui/rife-ncnn-vulkan/releases/download/20221029/rife-ncnn-vulkan-20221029-ubuntu.zip",
            "1e2c7ee7fa7daa326542d50622f0afedc80cf6f1858bda411d16385ffa5cdf68", 431302796, "rife-ncnn-vulkan",
            ["rife-ncnn-vulkan", .. RifeKeep], "MIT", RifeHome),
    ];

    readonly Func<string, CancellationToken, Task<Stream>> open;

    /// <param name="userFolder">The user folder; the tools are kept in its <c>Tools</c> folder.</param>
    /// <param name="packs">The programs (this machine's, unless a test's).</param>
    /// <param name="open">How a zip is fetched (the web, unless a test's).</param>
    public FilmTools(string userFolder, IReadOnlyList<FilmToolPack>? packs = null, Func<string, CancellationToken, Task<Stream>>? open = null)
    {
        Folder = Path.Combine(userFolder, "Tools");
        Packs = packs ?? ForThisMachine();
        this.open = open ?? Download;
    }

    /// <summary>Where they are kept.</summary>
    public string Folder { get; }

    /// <summary>The programs.</summary>
    public IReadOnlyList<FilmToolPack> Packs { get; }

    /// <summary>Where a download lands before it is checked (removed once it is unpacked or refused).</summary>
    string Downloads => Path.Combine(Folder, "download");

    /// <summary>A program's folder.</summary>
    public string FolderOf(string id) => Path.Combine(Folder, id);

    /// <summary>A program's path when it and every file it needs are there, else null.</summary>
    public string? ProgramOf(string id) =>
        Packs.FirstOrDefault(p => p.Id == id) is { } pack && pack.Keep.All(k => File.Exists(Path.Combine(FolderOf(id), k)))
            ? Path.Combine(FolderOf(id), pack.Program) : null;

    /// <summary>Whether both are there.</summary>
    public bool Ready => Packs.All(p => ProgramOf(p.Id) is not null);

    /// <summary>What still has to be fetched, and how many bytes that is.</summary>
    public (IReadOnlyList<FilmToolPack> Missing, long Bytes) ToFetch()
    {
        var missing = Packs.Where(p => ProgramOf(p.Id) is null).ToList();
        return (missing, missing.Sum(p => p.Bytes));
    }

    /// <summary>How much room the tools take now, in bytes.</summary>
    public long Size() => Directory.Exists(Folder) ? new DirectoryInfo(Folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;

    /// <summary>
    /// **The missing programs fetched**: each zip downloaded, checked against its SHA-256 (refused, and thrown away, when it does not
    /// match), the files kept unpacked, the zip deleted. <paramref name="progress"/> is told the bytes so far, of all. Throws on a
    /// refusal or a failed download (its message in plain words); a stopped fetch leaves nothing half-made.
    /// </summary>
    public async Task Fetch(Action<long, long>? progress, CancellationToken stop)
    {
        var (missing, total) = ToFetch();
        long before = 0;
        foreach (var pack in missing)
        {
            Directory.CreateDirectory(Downloads);
            string zip = Path.Combine(Downloads, pack.Id + ".zip");
            try
            {
                await using (var source = await open(pack.Url, stop))
                await using (var file = File.Create(zip))
                {
                    var buffer = new byte[1 << 20];
                    long got = 0;
                    int n;
                    while ((n = await source.ReadAsync(buffer, stop)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, n), stop);
                        got += n;
                        progress?.Invoke(before + got, total);
                    }
                }
                string sha;
                await using (var check = File.OpenRead(zip)) sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(check, stop));
                if (!string.Equals(sha, pack.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{pack.Name}'s download does not match its official release (its SHA-256 differs), so it was not used");
                Unpack(zip, pack);
                before += pack.Bytes;
            }
            catch
            {
                try { if (Directory.Exists(FolderOf(pack.Id)) && ProgramOf(pack.Id) is null) Directory.Delete(FolderOf(pack.Id), true); } catch (IOException) { }
                throw;
            }
            finally
            {
                try { if (File.Exists(zip)) File.Delete(zip); } catch (IOException) { }
                try { if (Directory.Exists(Downloads)) Directory.Delete(Downloads, true); } catch (IOException) { }
            }
        }
    }

    /// <summary>The kept files of a zip written into the program's folder (a zip's own top folder, if it has one, left out).</summary>
    void Unpack(string zip, FilmToolPack pack)
    {
        string into = FolderOf(pack.Id);
        if (Directory.Exists(into)) Directory.Delete(into, true);
        using var archive = ZipFile.OpenRead(zip);
        foreach (string keep in pack.Keep)
        {
            var entry = archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/') == keep || e.FullName.Replace('\\', '/').EndsWith("/" + keep, StringComparison.Ordinal))
                        ?? throw new InvalidDataException($"{pack.Name}'s download is missing {keep}");
            string to = Path.Combine(into, keep.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            entry.ExtractToFile(to, overwrite: true);
        }
        if (!OperatingSystem.IsWindows())
            try { File.SetUnixFileMode(Path.Combine(into, pack.Program), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); } catch (IOException) { }
    }

    /// <summary>**The tools removed**: their folder with everything in it. Answers why not, or null.</summary>
    public string? Remove()
    {
        try
        {
            if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
    }

    static readonly HttpClient Web = new() { Timeout = TimeSpan.FromMinutes(30) };

    static async Task<Stream> Download(string url, CancellationToken stop)
    {
        var answer = await Web.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stop);
        answer.EnsureSuccessStatusCode();
        return await answer.Content.ReadAsStreamAsync(stop);
    }
}
