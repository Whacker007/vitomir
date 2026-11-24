// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The characters' texture limit — 2048 by default (4096 the files' own), saved and loaded as
/// <c>characterTextureLimit</c>, a number between taken as the largest of 1024, 2048 and 4096 not above it, and
/// <c>--character-texture-limit</c> for one run (only those three). Needs no game files.
/// </summary>
public sealed class CharacterTextureLimitTests
{
    [Fact]
    public void The_limit_is_2048_by_default_the_settings_or_the_flags()
    {
        var s = new PlatformSettings();
        Assert.Equal(2048, s.CharacterTextureLimit);
        Assert.Equal(2048, s.CharacterTextureLimitFor(null));
        Assert.Equal(4096, new PlatformSettings { CharacterTextureLimit = 4096 }.CharacterTextureLimitFor(null));
        Assert.Equal(2048, s.CharacterTextureLimitFor("2048"));
        Assert.Equal(1024, new PlatformSettings { CharacterTextureLimit = 1500 }.CharacterTextureLimitFor(null));
        Assert.Equal(4096, new PlatformSettings { CharacterTextureLimit = 1024 }.CharacterTextureLimitFor("4096"));
        Assert.Throws<SessionException>(() => s.CharacterTextureLimitFor("3000"));
        Assert.Throws<SessionException>(() => s.CharacterTextureLimitFor("big"));
    }

    [Fact]
    public void It_is_saved_and_loaded()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-cl-render-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        new PlatformSettings { CharacterTextureLimit = 4096 }.Save(path);
        Assert.Contains("\"characterTextureLimit\": 4096", File.ReadAllText(path));
        Assert.Equal(4096, PlatformSettings.Load(path).CharacterTextureLimit);
    }
}
