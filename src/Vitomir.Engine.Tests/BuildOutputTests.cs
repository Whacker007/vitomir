// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Runtime.InteropServices;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The build is pinned to the machine that runs it (`src/Directory.Build.props`), and these are the
/// tests that say so.
///
/// Without the pin the SDK copies every platform's native assets into every project's output,
/// because a portable build has to be able to run anywhere: SDL3 for thirteen platforms, four
/// Android ABIs and an iOS xcframework among them, and IKVM's 64 MB Java class library three times
/// over. It came to about 280 MB a project and **3 GB across the tree**, against 331 MB with the pin
/// on. Nothing warns you when it comes back — the folders are gitignored and the build succeeds
/// either way — so it is asserted rather than remembered.
/// </summary>
public class BuildOutputTests
{
    /// <summary>The platforms whose binaries have no business being in a build made on this one.</summary>
    static readonly string[] Foreign =
        ["android", "ios", "tvos", "freebsd", "illumos", "solaris", "browser", "maccatalyst"];

    static string Host =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
        : "linux";

    [Fact]
    public void The_build_carries_one_platform_and_not_thirteen()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var runtimes = new DirectoryInfo(Path.Combine(output.FullName, "runtimes"));
        if (!runtimes.Exists) return;     // flattened, which is what the pin is for

        // A `runtimes/` folder is not wrong on its own — a package can put a managed asset there —
        // but a folder for an operating system nobody here runs means the pin has come off.
        var strangers = runtimes.GetDirectories()
            .Select(d => d.Name)
            .Where(n => Foreign.Any(f => n.StartsWith(f, StringComparison.OrdinalIgnoreCase))
                        || (!n.StartsWith(Host, StringComparison.OrdinalIgnoreCase)
                            && (n.StartsWith("win", StringComparison.OrdinalIgnoreCase)
                                || n.StartsWith("linux", StringComparison.OrdinalIgnoreCase)
                                || n.StartsWith("osx", StringComparison.OrdinalIgnoreCase))))
            .Order()
            .ToArray();

        Assert.True(strangers.Length == 0,
            $"the build output carries binaries for {strangers.Length} platform(s) this machine is "
            + $"not: {string.Join(", ", strangers)}. UseCurrentRuntimeIdentifier has come off in "
            + "src/Directory.Build.props — see the comment there for what it costs.");
    }

    /// <summary>
    /// The other half of it: one copy of IKVM's Java class library, not three. It is 64 MB, it is
    /// the single largest file in the tree, and a portable build ships a Windows, a Linux and a
    /// macOS one side by side.
    /// </summary>
    [Fact]
    public void The_java_class_library_is_here_once()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var copies = output.GetFiles("IKVM.Java.dll", SearchOption.AllDirectories);
        if (copies.Length == 0) return;   // this test project does not reference the scripting layer

        Assert.True(copies.Length == 1,
            $"{copies.Length} copies of IKVM.Java.dll in the build output "
            + $"({copies.Sum(f => f.Length) / (1024 * 1024)} MB): "
            + string.Join(", ", copies.Select(f => Path.GetRelativePath(output.FullName, f.FullName))));
    }
}
