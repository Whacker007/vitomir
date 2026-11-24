// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Which copy of a template is found when two files name it (`TemplateSet`, a comparison with 1.1 of
/// `Gameutil`): 1.1 puts every file's templates into one hash table whose insert refuses a name it
/// already holds, so the first file loaded wins — global.not over the other
/// three, and the globals over any file a scene names. Vitomir had put a
/// scene's file in front of the rest; pinned so that it adds and never replaces.
/// </summary>
public class TemplateOrderTests
{
    [Fact]
    public void A_file_loaded_later_never_replaces_a_template_already_there()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var names = templates.Names.ToList();
        var before = names.ToDictionary(n => n, n => templates.Find(n));
        templates.AddFile(NotFile.Read(files.Read("misc/templates/darkAges.not"), "darkAges.not"));
        foreach (var name in names) Assert.Same(before[name], templates.Find(name));
    }

    [Fact]
    public void Of_the_names_the_global_files_share_global_not_s_copy_is_found()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var global = NotFile.Read(files.Read("misc/templates/global.not"), "global.not");
        foreach (var name in global.ByName.Keys)
            Assert.Equal(global.ByName[name].Tags.Count(), templates.Find(name)!.Tags.Count());
    }
}
