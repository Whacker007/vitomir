// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The console's `aitrace` writes every mode change in the message box**,
/// with the engine's own one-short name table — so the line is as wrong as 1.1's is — and nothing
/// while the trace bit is off.
/// </summary>
public class AiTraceTests
{
    [Fact]
    public void With_the_trace_on_a_mode_change_is_a_line_in_the_box_with_the_tables_wrong_names()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var session = new Session { Here = place };
        place.Session = session;
        var mind = minds.All.First(m => m.Thing.Template.Equals("cappadocian", StringComparison.OrdinalIgnoreCase));

        mind.Enter(AiMode.EnemySearch);
        Assert.Empty(session.Messages.Lines);                           // off by default

        minds.MainFlags |= AiMainFlag.Trace;
        var was = mind.Mode;
        mind.Enter(AiMode.Enemy);
        Assert.Equal(new Message($"AI 'cappadocian' [{mind.Thing.Guid}] changed mode: '{AiModes.Logged(was)}' to 'enemysearch'.", MessageLog.Own),
                     session.Messages.Lines[^1]);                       // Enemy (3) prints the name below it
        mind.Enter(AiMode.Enemy);
        Assert.Equal(2, session.Messages.Lines.Count);                   // a mode set to itself is written too
    }
}
