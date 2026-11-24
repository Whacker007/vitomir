// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// A destroyed thing's script goes with it (a comparison with 1.1). The destroy only marks the thing;
/// its next tick deletes it, and the deletion's teardown sends `destroyed`
/// (class slot 16) to its own script and its holders, releases the holds on it
/// and frees its script with the script's timers and its holds on other things. Until then
/// only a chapter swap freed a script, so a door a script removed went on answering its levers.
/// </summary>
[Collection("scripts")]
public class DestroyedScriptTests(Scripts s)
{
    [Fact]
    public void A_destroyed_door_lets_go_of_its_levers_at_its_next_tick()
    {
        using var m = Place.Enter(s, "P1_MON1");
        // P1_MON1's levers have no script; the `DoorSwitchPivot` on each door captured its lever.
        var lever = m.Where.Things.First(t => t.Template.Contains("lever", StringComparison.OrdinalIgnoreCase)
                                              && t.Script.Length == 0 && t.Captures.Count > 0);
        var doors = m.Where.Things.Where(t => t.Script == "DoorSwitchPivot").ToArray();
        Assert.NotEmpty(doors);
        int scripts = m.Scenes.ThingScripts;

        foreach (var door in doors) m.Where.Remove(door);
        // Destroyed, not yet deleted: until its tick the door's script still holds the lever.
        Assert.NotEmpty(lever.Captures);
        Assert.Equal(scripts, m.Scenes.ThingScripts);

        m.Sim.Step(1f / 30f);
        // Deleted: every door's script freed, and with it its hold on the lever.
        Assert.Equal(scripts - doors.Length, m.Scenes.ThingScripts);
        Assert.Empty(lever.Captures);
        Assert.False(m.Scenes.Clicked(lever, m.Christof), "a destroyed door's script still heard its lever");
    }
}
