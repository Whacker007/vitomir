// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.Host;

/// <summary>The platform program's version line: "Vitomir Platform" and the build's release version (<see cref="Vitomir.Platform.ReleaseVersion"/>).</summary>
public static class PlatformVersion
{
    /// <summary>The line, as the log's first line, the window's title, a tape's header and the join show it.</summary>
    public static readonly string Text = Vitomir.Platform.ReleaseVersion.Line("Vitomir Platform");
}
