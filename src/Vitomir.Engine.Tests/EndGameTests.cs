// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The two ways to lose: the whole party down and a character's
/// Humanity gone, and the words 1.1 puts over the world for each.
/// </summary>
public class EndGameTests
{
    /// <summary>
    /// **The end text's place and time**: one pane stood at (20, 430), three lines of a 15-pixel
    /// font tall, the headline on its top line and "Press Esc…" under it; up for two hours, fading
    /// in over half a second and never out; both lines cut at the headline's width.
    /// </summary>
    [Fact]
    public void The_end_text_stands_near_the_top_left_for_two_hours()
    {
        var up = Captions.EndText("All coterie members are dead!", "Press Esc to load saved game.", 217, 1000);
        Assert.Equal(2, up.Length);
        Assert.All(up, c => Assert.Equal((20f, 430f, "DefaultFont15", 217f), (c.X, c.Y, c.Font, c.Width)));
        Assert.Equal(460f, up[0].PenY(15));                    // the pane's top line: 430 + 2 x 15
        Assert.Equal(445f, up[1].PenY(15));                    // the line under it
        Assert.Equal(0f, up[0].Alpha(1000));
        Assert.Equal(0.5f, up[0].Alpha(1250));
        Assert.Equal(1f, up[1].Alpha(1000 + 7_000_000));
        Assert.False(up[0].Over(1000 + 7_200_000));
        Assert.True(up[0].Over(1001 + 7_200_000));

        Assert.Empty(Captions.EndText("", "Press Esc to load saved game.", 0, 1000));
        Assert.Equal(640f, Captions.EndText(new string('x', 200), "Press Esc", 1200, 0)[0].Width);
    }

    /// <summary>
    /// **Humanity's end**: Christof's Humanity reaching
    /// nothing asks for the end; taking it marks the whole party dead, puts his face in frenzy, and
    /// makes the headline "Christof lost all Humanity!" out of <c>RPG_ENDGAME_HUMANITYLOST</c>.
    /// </summary>
    [Fact]
    public void Losing_the_last_of_Humanity_ends_the_game_with_his_name()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var chronicle = ChronicleSet.Load(files).ForLevel("P1_MON1")!.Value.Chronicle;
        var session = new Session { Here = place, Chronicle = chronicle };
        session.Messages.Text = new Strings(files);
        place.Session = session;
        var christof = place.Player!.Thing;
        var a = christof.Actor!;
        a.Flags |= Actor.Flag.Party | Actor.Flag.HumanityTrack;
        a.Type = ActorType.Vampire;
        a.BaseStat[(int)ActorStat.Humanity] = a.Stat[(int)ActorStat.Humanity] = 1;

        Stats.Set(christof, (int)ActorStat.Humanity, 0f);
        Assert.Equal(new SequenceRequest(SequenceKind.EndGame, Reason: Chronicle.EndHumanityLost), chronicle.Pending);

        string pairs = place.HumanityGone();
        Assert.Equal("%PChristof", pairs);
        Assert.True(a.Has(Actor.Flag.Dead));
        Assert.Equal(HeadMode.Frenzy, a.HeadAction);
        // The table's lines end in blanks, and 1.1 keeps them: the value runs to the carriage return.
        Assert.Equal("Christof lost all Humanity!", session.Messages.Format("RPG_ENDGAME_HUMANITYLOST", pairs).TrimEnd(' '));
        Assert.Equal("All coterie members are dead!", session.Messages.Format("RPG_ENDGAME_PARTYDEAD").TrimEnd(' '));
        Assert.Equal("Press Esc to load saved game.", session.Messages.Format("RPG_ENDGAME_PRESSESC").TrimEnd(' '));
    }
}
