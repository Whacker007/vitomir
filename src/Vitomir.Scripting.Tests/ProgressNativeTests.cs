// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>The quest natives and the CodexSequence natives that move the story on, against the monastery.</summary>
[Collection("scripts")]
public class ProgressNativeTests(Scripts s)
{
    static (CodexScenes Scenes, Location Where, GameFiles Files, Session Session) Enter(Scripts s)
    {
        s.Ready();
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_MON1")!.Value;
        var location = Location.Load(files, "P1_MON1", sky: false, scene: where.Entry!.SceneName, player: "Christof");
        var session = new Session { Here = location, Chronicle = where.Chronicle };
        session.Quests.Read = name => files.TryRead($"quests/{name}.nqd", out var d) ? d : null;
        location.Session = session;
        location.Player!.Thing.Actor!.Flags |= Actor.Flag.Party;
        var scenes = new CodexScenes(s.Runtime!) { Chronicles = set };
        scenes.Enter(location);
        return (scenes, location, files, session);
    }

    static object? Call(Scripts s, string key, object? self, params object?[] args) =>
        s.Engine.get(new object[] { key, self!, args.Select(Wrap).ToArray() });

    static object? Wrap(object? v) => v switch
    {
        int i => java.lang.Integer.valueOf(i),
        bool b => java.lang.Boolean.valueOf(b),
        _ => v,
    };

    static int Int(object? o) => ((java.lang.Integer)o!).intValue();
    static bool Bln(object? o) => ((java.lang.Boolean)o!).booleanValue();

    object Quest(int guid) => s.Runtime!.NewScript("CodexQuest", guid, [java.lang.Integer.valueOf(guid)]);

    /// <summary>
    /// **The scripts' console is the message box**. In single player the three print
    /// natives post to the status bar's box, in the script's colour
    /// plus <c>0xFF000000</c> — so "Christof uses Discipline Possession." is a white line in the log,
    /// as it is in 1.1 — and Clear empties it.
    /// </summary>
    [Fact]
    public void What_a_script_prints_goes_to_the_message_box()
    {
        var (_, _, files, session) = Enter(s);
        using var _f = files;
        session.Messages.Text = new Vitomir.Engine.Ui.Strings(files);

        Call(s, "CodexConsole.Print", null, 0, 8, "Christof uses Discipline Possession.", 0xFFFFFF);
        Assert.Equal(new Vitomir.Engine.Ui.Message("Christof uses Discipline Possession.", 0xFFFFFFFFu), session.Messages.Lines[^1]);

        Call(s, "CodexConsole.PrintNLS", null, 0, 0, "UI_STSBPAUSE", 0x00FF00);
        Assert.Equal(new Vitomir.Engine.Ui.Message("Game paused.", 0xFF00FF00u), session.Messages.Lines[^1]);

        Call(s, "CodexConsole.PrintFormatNLS", null, 0, 0, "RPG_HUMANITYLOST", "%PChristof", 0xFF0000);
        Assert.Equal(new Vitomir.Engine.Ui.Message("Christof lost some Humanity!", 0xFFFF0000u), session.Messages.Lines[^1]);

        Call(s, "CodexConsole.Print", null, 0, 0, new string('x', 300), 0xFFFFFF);
        Assert.Equal(255, session.Messages.Lines[^1].Text.Length);           // a 256-byte copy

        Call(s, "CodexConsole.Clear", null, 0, 0);
        Assert.Empty(session.Messages.Lines);
    }

    [Fact]
    public void A_script_loads_a_quest_completes_it_and_the_party_is_paid()
    {
        var (scenes, where, files, session) = Enter(s);
        using var _f = files;
        int guid = Int(Call(s, "CodexQuest.Load", null, "P1_SilverMines"));
        Assert.Equal(0, guid);
        Assert.Equal(guid, Int(Call(s, "CodexQuest.Load", null, "p1_silvermines")));   // already in the log
        Assert.Equal(-1, Int(Call(s, "CodexQuest.Load", null, "NoSuchQuest")));
        var q = Quest(guid);
        Assert.Equal("q_silvermines_s", Call(s, "CodexQuest.GetDescription", q, false));
        Assert.Equal("q_silvermines_l", Call(s, "CodexQuest.GetDescription", q, true));
        Assert.Equal(0, Int(Call(s, "CodexQuest.GetNumItems", q)));
        Assert.Equal(CodexScenes.BadQuestItem, Call(s, "CodexQuest.GetItemDescription", q, 0, false));
        Assert.Equal(-1, Int(Call(s, "CodexQuest.GetItemFlags", q, 0)));

        var christof = where.Player!.Thing.Actor!;
        long earned = christof.Experience;
        Assert.Equal(0, Int(Call(s, "CodexQuest.SetFlags", q, 1)));                    // the old flags
        Assert.Equal(5, Int(Call(s, "CodexQuest.GetFlags", q)));                       // complete, and paid
        Assert.Equal(earned + 1200, christof.Experience);
        Assert.Contains(session.Messages.Lines, m => m.Text == "UI_QUESTCHANGED");

        Assert.True(Bln(Call(s, "CodexQuest.Destroy", q)));
        Assert.Equal(-1, Int(Call(s, "CodexQuest.GetFlags", q)));
        Assert.Equal(CodexScenes.BadQuest, Call(s, "CodexQuest.GetDescription", q, false));
        Assert.False(Bln(Call(s, "CodexQuest.Destroy", q)));
        scenes.Leave();
    }

    [Fact]
    public void The_sequence_natives_ask_the_chronicle_for_the_next_scene()
    {
        var (scenes, _, files, _) = Enter(s);
        using var _f = files;
        var c = scenes.Chronicle!;
        Call(s, "CodexSequence.ChangeScene", null, "Monastery", "P1_MON1.nsd");
        Assert.Equal("P1_MON1.nsd", c.At("Monastery")!.Scene);
        Call(s, "CodexSequence.Jump", null, "Monastery", 1);
        Assert.Equal((SequenceKind.Jump, c.Find("Monastery"), 1), (c.Pending!.Value.Kind, c.Pending.Value.To, c.Pending.Value.Entrance));
        Call(s, "CodexSequence.ChangeChronicle", null, "nosuch.nsc");
        Assert.Equal(SequenceKind.Jump, c.Pending!.Value.Kind);                           // no such file: nothing
        Call(s, "CodexSequence.ChangeChronicle", null, "vienna.nsc");
        Assert.Equal((SequenceKind.ChangeChronicle, "vienna.nsc"), (c.Pending!.Value.Kind, c.Pending.Value.Name));
        Call(s, "CodexSequence.EndGame", null);
        Assert.Equal(SequenceKind.EndGame, c.Pending!.Value.Kind);
        Assert.Equal(Chronicle.EndStory, c.Pending.Value.Reason);                // the story's end: the credits
        c.Take();
        Call(s, "CodexSequence.SetupAdvance", null, "Monastery", "MON1_8_2.nsd", 0);
        Call(s, "CodexSequence.Advance", null, 7);
        Assert.Equal((SequenceKind.Advance, 7), (c.Pending!.Value.Kind, c.Pending.Value.Guid));
        c.Take();
        // The network natives and the save stream, outside a network game and outside a save.
        Assert.Equal(-1, Int(Call(s, "CodexSequence.GetNSSIndex", null)));
        Assert.Equal(CodexScenes.NoStreamInt, Int(Call(s, "CodexSequence.RestoreInt", null)));
        Assert.Equal(CodexScenes.NoStreamString, Call(s, "CodexSequence.RestoreString", null));
        Assert.False(Bln(Call(s, "CodexSequence.SaveInt", null, 3)));
        scenes.Leave();
    }
}
