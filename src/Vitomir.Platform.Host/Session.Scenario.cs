// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: a scenario's program in a session.
using Vitomir.Platform.Control;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Host;

public sealed partial class Session
{
    /// <summary>
    /// The scenario director registered for <see cref="SessionOptions.Scenario"/>: its program read (a program that is not
    /// one stops the session by name, <see cref="SessionException"/>).
    /// </summary>
    static void RegisterScenario(WorldBuilder b, string programText, IDisposable? install)
    {
        ScenarioProgram program;
        try { program = ScenarioProgram.Read(programText); }
        catch (ScenarioProgramException e) { install?.Dispose(); throw new SessionException(e.Message); }
        ScenarioDirectors.Register(b, program);
        if (program.Contacts is { Count: > 0 } contacts) Combat.FixedContactServices.Register(b, contacts);   // the fixed contacts
    }

    /// <summary>
    /// The program bound to the cast's fighters, by their names, once they are made: every actor, event kind, field and
    /// observable it names checked against this world (one it does not have stops the session by name).
    /// </summary>
    void BindScenario()
    {
        if (!world.TryService<ScenarioDirector>(out var director)) return;
        var actorNames = new List<string>(fighters.Count);
        foreach (var f in fighters) actorNames.Add(names.TryGetValue(f.Packed, out var n) ? n : f.ToString());
        try { director.Bind(world, actorNames, fighters); }
        catch (ScenarioProgramException e) { throw new SessionException(e.Message); }
        // The fixed contacts' people by name
        if (world.TryService<Combat.FixedContactPlan>(out var fixedPlan))
            try { fixedPlan.Bind(actorNames, fighters); }
            catch (ScenarioProgramException e) { throw new SessionException(e.Message); }
        options.Say($"scenario: the director drives {string.Join(", ", director.Program.Actors.Select(a => $"{a.Actor} ({Describe(a)})"))}" +
                    (director.Program.Triggers.Count > 0 ? $"; {director.Program.Triggers.Count} trigger{(director.Program.Triggers.Count == 1 ? "" : "s")}" : "") +
                    (director.Program.Input is { } input ? $"; {input.Actor} by the player's controls ({input.Segments.Count} input segment{(input.Segments.Count == 1 ? "" : "s")})" : ""));
    }

    /// <summary>
    /// The frame for the next tick when the scenario drives an actor through the player's controls
    /// (<see cref="ScenarioProgram.Input"/>: PLAYER_INPUT_REPLAY), else idle — what a scenario's run loop steps with
    /// (<c>session.Step(session.ScenarioFrame())</c>) and records on its tape, as a window's frames are.
    /// </summary>
    public ActionFrame ScenarioFrame() =>
        world.TryService<ScenarioDirector>(out var director) ? director.Program.FrameAt(world.Tick) : ActionFrame.Idle;

    static string Describe(ActorControl a)
    {
        int steps = a.Tracks.Sum(t => t.Count);
        string ai = a.Ai == ScenarioAi.None ? "no AI" : $"AI {a.Ai.ToString().ToLowerInvariant()}";
        string mute = a.Mute == ControlChannels.None ? "" : $", {a.Mute.ToString().ToLowerInvariant()} muted";
        return steps == 0 ? $"{ai}{mute}" : $"{a.Tracks.Count} track{(a.Tracks.Count == 1 ? "" : "s")} of {steps} step{(steps == 1 ? "" : "s")}, then {a.Then.ToString().ToLowerInvariant()}; {ai}{mute}";
    }
}
