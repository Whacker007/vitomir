// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Reflection;
using System.Text.RegularExpressions;

namespace Vitomir.Platform.Tests;

/// <summary>
/// **The version line** (<see cref="ReleaseVersion"/>): the release version is the one <c>src/Directory.Build.props</c> sets
/// for the whole build, read back from the built assembly, and the line is the program's name, that number and, in a
/// build that names its development tag, the tag in brackets.
/// </summary>
public class ReleaseVersionTests
{
    [Fact]
    public void The_release_version_is_the_builds_own_number_read_from_the_assembly()
    {
        var props = File.ReadAllText(Path.Combine(PlatformBoundaryTests.SourceRoot(""), "Directory.Build.props"));
        var set = Regex.Match(props, "<Version>([^<]+)</Version>");
        Assert.True(set.Success, "Directory.Build.props sets no <Version>");
        Assert.Equal(set.Groups[1].Value, ReleaseVersion.Number);
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$", ReleaseVersion.Number);   // semantic, and no source revision after a +
    }

    [Fact]
    public void The_line_is_the_name_the_number_and_a_batch_only_where_the_build_has_one()
    {
        string line = ReleaseVersion.Line("Vitomir Platform");
        Assert.Equal(ReleaseVersion.Batch is { } batch ? $"Vitomir Platform {ReleaseVersion.Number} ({batch})" : $"Vitomir Platform {ReleaseVersion.Number}", line);
        Assert.Equal(ReleaseVersion.Batch ?? ReleaseVersion.Number, ReleaseVersion.Word);
        Assert.True(ReleaseVersion.Batch is null || Regex.IsMatch(ReleaseVersion.Batch, @"^\w+$"), ReleaseVersion.Batch);
    }

    [Fact]
    public void The_development_tag_is_a_file_beside_the_assembly_and_not_in_it()
    {
        // In it, the tag would change the module versions of every assembly compiled against this one (their PDBs record
        // them), the join's four among them, so a build with a tag and one without could not join (NetIdentity.Build).
        Assert.DoesNotContain(typeof(ReleaseVersion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>(), a => a.Key == "VitomirBatch");
        string file = Path.Combine(Path.GetDirectoryName(typeof(ReleaseVersion).Assembly.Location)!, ReleaseVersion.BatchFile);
        // Every build writes it, empty without a tag, so a package unzipped over an older one replaces the older one's tag.
        Assert.True(File.Exists(file));
        Assert.Equal(File.ReadAllText(file).Trim().Length > 0, ReleaseVersion.Batch is not null);
    }
    [Fact]
    public void A_packages_notice_reaches_the_shown_line_after_the_version_and_a_build_without_one_shows_none()
    {
        const string notice = "Private beta - please do not distribute";
        string dir = Path.Combine(Path.GetTempPath(), $"vitomir-notice-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(ReleaseVersion.ReadNotice(dir));                                     // no file: none
            File.WriteAllText(Path.Combine(dir, ReleaseVersion.NoticeFile), "\n");
            Assert.Null(ReleaseVersion.ReadNotice(dir));                                     // empty: none
            File.WriteAllText(Path.Combine(dir, ReleaseVersion.NoticeFile), notice + "\n", new System.Text.UTF8Encoding(true));
            Assert.Equal(notice, ReleaseVersion.ReadNotice(dir));                            // as MSBuild writes it (UTF-8)
            Assert.Equal($"Vitomir Platform 0.9.0-dev (dev) \u00b7 {notice}", ReleaseVersion.Shown("Vitomir Platform 0.9.0-dev (dev)", notice));
            Assert.Equal("Vitomir Platform 0.9.0-dev", ReleaseVersion.Shown("Vitomir Platform 0.9.0-dev", null));
            Assert.Equal("Vitomir Platform 0.9.0-dev", ReleaseVersion.Shown("Vitomir Platform 0.9.0-dev", ""));
        }
        finally { Directory.Delete(dir, true); }
        // This build: a developer's (no VitomirNotice) has an empty file and shows the line alone.
        string beside = Path.Combine(Path.GetDirectoryName(typeof(ReleaseVersion).Assembly.Location)!, ReleaseVersion.NoticeFile);
        Assert.True(File.Exists(beside));                                                   // written in every build, empty without a notice
        Assert.Equal(File.ReadAllText(beside).Trim().TrimStart('\uFEFF').Length > 0, ReleaseVersion.Notice is not null);
        if (ReleaseVersion.Notice is null) Assert.Equal(ReleaseVersion.Line("Vitomir"), ReleaseVersion.Shown(ReleaseVersion.Line("Vitomir")));
        // The package sets it, by -Notice, and puts it in build-info.json and the packaged README's first line.
        string package = File.ReadAllText(Path.Combine(PlatformBoundaryTests.SourceRoot(""), "..", "tools", "package.ps1"));
        Assert.Contains("\"-p:VitomirNotice=$Notice\"", package);
        Assert.Contains("notice = $Notice", package);
        Assert.Contains("[string]$Notice = \"\"", package);                    // none by default since public 1.0
    }
}
