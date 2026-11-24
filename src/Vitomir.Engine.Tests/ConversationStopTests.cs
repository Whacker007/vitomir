// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **`CONV_XFLAG_STOPACTORS`** (`0x100`): a conversation begun with it stops every walker in the
/// place ticked before the thing tick ends — the begin sets the world's stop bit, the
/// walker's update stops on it, and the thing tick clears it. Codex.java
/// defines the flag and no shipped script passes it, so these conversations are a script's that
/// does — the flags as `ExecuteConversation` hands them on (`CodexScenes.Converse` casts them
/// straight). Without it only the speakers stop (the end's own loop).
/// </summary>
public class ConversationStopTests
{
    static NcoConversation Lines() => NcoFile.Read(Encoding.Latin1.GetBytes(
        "CONVERSATION: T 0\r\nEVENT: ev_0\r\n\tLINE: a Christof\r\n\tLINE: b Christof\r\n"), "Test.nco").Conversations[0];

    /// <summary>Old Town by day, its errands under way; then the conversation, and one frame.</summary>
    static (int WalkingBefore, int WalkingAfter, bool UpAfter, int WalkingLater) Run(ConversationFlag flags)
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1);
        var talk = new Conversations();
        var sim = new Simulation(place) { Talk = talk };
        for (int i = 0; i < 30 * 8; i++) sim.Step(1f / 30f);
        int Walking() => minds.All.Count(m => m.Mode != AiMode.PedFly && !ReferenceEquals(m.Thing, place.Player?.Thing) && m.Walker.Walking);
        int before = Walking();
        talk.Run(place, Lines(), flags, 5, place.Milliseconds);
        sim.Step(1f / 30f);
        int after = Walking();
        bool up = ConversationStop.Up(place);
        for (int i = 0; i < 30 * 5; i++) sim.Step(1f / 30f);
        return (before, after, up, Walking());
    }

    [Fact]
    public void A_conversation_with_the_flag_stops_every_walker_ticked_that_frame()
    {
        var (before, after, up, later) = Run(ConversationFlag.Callbacks | ConversationFlag.StopActors);
        Assert.True(before >= 5, $"only {before} walking in Old Town");
        // Only a walker still inside one of its waits walks on (it returns before the test).
        Assert.True(after <= before / 4, $"{after} of {before} still walking");
        Assert.False(up);                                      // cleared at the end of the thing tick
        // The stop is that frame's alone: the minds order their walks again as their errands come
        // round (in 1.1's order of things, about half the town is walking again 3.5 s on, all of it by 5).
        Assert.True(later >= before / 2, $"only {later} of {before} walking again five seconds on");
    }

    [Fact]
    public void Without_the_flag_the_town_walks_on()
    {
        var (before, after, up, _) = Run(ConversationFlag.Shipped);
        Assert.False(up);
        Assert.True(after >= before * 3 / 4, $"{after} of {before} walking");
    }
}
