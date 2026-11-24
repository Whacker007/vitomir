// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The scripts' three places in 1.1's frame (gap 1 of a comparison with 1.1).
namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>
    /// The scripts' part of the things' tick all at once, for a caller with no
    /// simulation (<see cref="Step"/>): <see cref="BeginThings"/>, then the world's list newest
    /// first (<see cref="Vitomir.Engine.World.Location.ListOrder"/>), each thing's track move and then its proximity
    /// test. <see cref="Vitomir.Engine.World.Simulation.Step"/> asks for the same parts from inside
    /// each thing's own tick instead (<c>CodexScenes.ThingTick.cs</c>).
    /// </summary>
    public void StepThings(float seconds)
    {
        BeginThings(seconds);
        if (!stepping || location is null) return;
        foreach (var thing in location.ListOrder)
        {
            if (thing.Removed) { DeleteThing(thing); continue; }
            MoveThing(thing, seconds);
            TouchThing(thing);
        }
    }

    /// <summary>
    /// The timers that have come due, which 1.1's world tick runs after every
    /// thing has ticked, after the camera and the ramps, and before the
    /// conversation. So a timer's `SetShot`, which runs a camera frame on the spot, sees the things
    /// where this frame left them, and an animation a timer plays starts moving on the next frame's
    /// thing tick. Earlier, Vitomir fired them before the animations.
    /// </summary>
    public void StepTimers(float seconds)
    {
        if (live.Count == 0 && loose.Count == 0) return;
        for (int guard = 0; guard < 256; guard++)
        {
            int at = -1;
            // Soonest first, and of timers due at the same moment the newest first: 1.1 slots a
            // new one in front of the first whose time is not earlier than its own.
            for (int i = 0; i < timers.Count; i++)
                if (timers[i].At <= now && (at < 0 || timers[i].At <= timers[at].At)) at = i;
            if (at < 0) return;
            var t = timers[at];
            timers.RemoveAt(at);
            if (ScriptOf(t.Guid) is not { } owner) continue;
            if (ThrownAway(t.Guid)) continue;                              // a carried item's (CodexScenes.CarriedTimers.cs)
            using var mark = EffectTimer(t.Guid);                          // an effect's: each player's machine runs it too (CodexScenes.PlayersMachine.cs)
            using var made = EffectTimerMade(t.Guid);                      // and what it makes, for a save (World/EffectMade.cs)
            TimersFired++;                                                 // ours: a count for the story sweep
            try { runtime.CallTimer(owner.Script, t.Id, t.A, t.B, t.C, t.D); }   // the script's timer(id, a, b, c, d)
            catch (Exception e) { Engine.Log($"{owner.Name}.timer({t.Id}) failed: {ScriptException.From(owner.Name, e).JavaType}"); }
        }
    }

    /// <summary>**Ours**: how many script timers have fired (<see cref="StepTimers"/>), for the story sweep.</summary>
    public int TimersFired { get; private set; }

    /// <summary>
    /// The end of the frame (after the world tick and the draw): a film a script asked for
    /// is handed to the host here (<see cref="RunFilm"/>) — so a film a timer or a conversation asks
    /// for starts in the same frame, as in 1.1.
    /// </summary>
    public void EndFrame()
    {
        if (live.Count == 0 && loose.Count == 0) return;
        RunFilm();
    }
}
