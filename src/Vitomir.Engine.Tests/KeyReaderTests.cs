// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The sweep of every bindable command against what reads it (`KeyReaders`). The key page lists
/// every command in `KeyBindings.All` as remappable; the bindings hand each on as its 1.1 key and the
/// host's handlers test 1.1's keys, so a command nothing tests would take a key and do nothing. These
/// fail when a command is declared without a reader, when a reader's text is gone from its host file,
/// and when the status bar is toggled by any key but its own (F4 did until then) or run is read off the
/// secondary flag's Shift.
/// </summary>
public class KeyReaderTests
{
    static string? Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Vitomir.slnx"))) dir = dir.Parent;
        return dir?.FullName;
    }

    [Fact]
    public void Every_declared_command_has_a_reader()
    {
        foreach (var info in KeyBindings.All)
            Assert.True(KeyReaders.Of(info.Command) is not null, $"{info.Id} is declared and nothing reads it");
        Assert.Equal(KeyBindings.All.Select(i => i.Command), KeyReaders.All.Select(r => r.Command));
    }

    [Fact]
    public void Each_reader_reads_its_command_the_way_its_kind_is_read()
    {
        foreach (var r in KeyReaders.All)
        {
            var kind = KeyBindings.Of(r.Command).Kind;
            var expected = kind switch
            {
                CommandKind.Click => KeyReading.Click, CommandKind.Flag => KeyReading.Flag, CommandKind.Held => KeyReading.Polled,
                CommandKind.Press => KeyReading.Pressed, _ => KeyReading.Fly,
            };
            Assert.True(expected == r.How, $"{r.Command} is a {kind} command read as {r.How}");
        }
    }

    // A host file's text, or a host folder's (the frame controller's classes): every C# file in it.
    static string Source(string path) => Directory.Exists(path)
        ? string.Join("\n", Directory.GetFiles(path, "*.cs").Order(StringComparer.Ordinal).Select(File.ReadAllText))
        : File.ReadAllText(path);

    [Fact]
    public void Every_reader_is_in_its_host_file()
    {
        if (Root() is not { } root) { Assert.Skip("not running from inside the tree"); return; }
        var files = new Dictionary<string, string>();
        foreach (var r in KeyReaders.All)
        {
            if (!files.TryGetValue(r.Host, out var text)) files[r.Host] = text = Source(Path.Combine(root, r.Host));
            Assert.True(text.Contains(r.Token, StringComparison.Ordinal), $"{r.Command}: \"{r.Token}\" is not in {r.Host}");
        }
    }

    [Fact]
    public void The_status_bar_is_toggled_by_its_own_key_alone_and_run_by_its_own()
    {
        if (Root() is not { } root) { Assert.Skip("not running from inside the tree"); return; }
        var program = Source(Path.Combine(root, "src", "Vitomir.Classic", "Controller")).Split('\n');
        // One toggle, through the in-front test (StatusBarKey.Toggle), and no bare flip beside it.
        Assert.Single(program, l => l.Contains("StatusBarKey.Toggle(hudWanted", StringComparison.Ordinal));
        Assert.DoesNotContain(program, l => l.Contains("hudWanted = !hudWanted", StringComparison.Ordinal));
        Assert.DoesNotContain(program, l => l.Contains("LoopKey.F4", StringComparison.Ordinal) && l.Contains("hudWanted", StringComparison.Ordinal));
        Assert.DoesNotContain(program, l => l.Contains("Down(LoopScancode.LeftShift)", StringComparison.Ordinal));
    }
}
