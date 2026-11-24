// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Engine.World;

/// <summary>
/// The scene's scripts, as the rest of the engine sees them. The real implementation lives in
/// <c>Vitomir.Scripting</c>, where it runs the game's own Java on IKVM; the engine keeps only this
/// interface so that loading a level and drawing a frame don't drag a Java compiler along. The
/// headless rendering tests and the level sweep use <see cref="NoScripts"/> and pay nothing.
/// </summary>
public interface ISceneScripts
{
    /// <summary>
    /// Entering a location: create the scripts its things name and run each one's <c>beginscene</c>.
    /// </summary>
    void Enter(Location location);

    /// <summary>
    /// One whole frame of the scripts on their own, in the frame's order: <see cref="StepThings"/>,
    /// <see cref="StepTimers"/>, <see cref="EndFrame"/>. <see cref="Simulation.Step"/> calls the
    /// three parts where 1.1's frame has them; this is for a caller with no simulation.
    /// </summary>
    void Step(float seconds);

    /// <summary>
    /// The scripts' part of the things' tick all at once, for a caller with no
    /// simulation: <see cref="BeginThings"/>, then each thing of the world's list, newest first,
    /// moved and touched. <see cref="Simulation"/> calls the parts from each thing's own tick
    /// instead. The default does nothing — an implementation with only <see cref="Step"/> does all
    /// of it in <see cref="StepTimers"/> (ours).
    /// </summary>
    void StepThings(float seconds) { }

    /// <summary>
    /// The scripts' part of the frame before the world's walk (`World/ThingTick.cs`): the scripts'
    /// clock, the deletions left from the frame before, the walks of no length due. The default
    /// does nothing.
    /// </summary>
    void BeginThings(float seconds) { }

    /// <summary>One thing's track move and its `arrived`, from its own tick. The default does nothing.</summary>
    void MoveThing(WorldThing thing, float seconds) { }

    /// <summary>One `THING_FLAG_PROXIMITYTRIGGER` thing asking who touches it, from its own tick. The default does nothing.</summary>
    void TouchThing(WorldThing thing) { }

    /// <summary>
    /// A destroyed thing's deletion as the scripts see it (`destroyed`, the
    /// holds on it, its script freed), when the walk reaches it. The default does nothing.
    /// </summary>
    void DeleteThing(WorldThing thing) { }

    /// <summary>
    /// The script timers that have come due, which the world tick runs after the things,
    /// the camera and the ramps. The default runs the whole <see cref="Step"/> (ours).
    /// </summary>
    void StepTimers(float seconds) => Step(seconds);

    /// <summary>
    /// The films a script put on a material (which 1.1's frame runs before the
    /// world tick): each one's due frame painted into its texture. The default does nothing.
    /// </summary>
    void StepFilms(float seconds) { }

    /// <summary>The end of the frame's requests (after the draw): a film asked for. The default does nothing.</summary>
    void EndFrame() { }

    /// <summary>Leaving, by the exit the player took: each script's <c>endscene</c>.</summary>
    void Leave(int exitNumber = 0);
}

/// <summary>A world with no scripts running. Levels still draw; nothing scripted happens.</summary>
public sealed class NoScripts : ISceneScripts
{
    public static readonly NoScripts Instance = new();
    NoScripts() { }
    public void Enter(Location location) { }
    public void Step(float seconds) { }
    public void Leave(int exitNumber = 0) { }
}

/// <summary>
/// The script objects the engine makes of its own accord and hangs on a character: an effect's
/// (`effects.ned`'s script column) and a learnt discipline's (`game.ndd`'s).
/// Each is a Codex object of the named class, made fresh for its owner — the engine's codex
/// wrapper with the owner in it — and each hears the callbacks
/// below. The script layer implements this; the engine half never learns that the other end is
/// Java.
/// </summary>
public interface IScriptObjects
{
    /// <summary>A new object of the class, owned by <paramref name="owner"/>, or null when there is no such class.</summary>
    object? Make(string className, WorldThing owner);

    /// <summary>Lets one go, when its effect is reaped or its discipline forgotten.</summary>
    void Free(object script);

    /// <summary>`effectstarted(actorGuid, effectGuid, creatorGuid, duration)` — class slot 33.</summary>
    void EffectStarted(object script, WorldThing actor, Rpg.Effect effect, int duration);

    /// <summary>`effectended(actorGuid, effectGuid, creatorGuid, reason)` — class slot 34.</summary>
    void EffectEnded(object script, WorldThing actor, Rpg.Effect effect, int reason);

    /// <summary>
    /// `cancast(level, casterGuid, targetGuid, x, y, z, bIsPos)` — class slot 44. **A
    /// class that does not declare it answers true.**
    /// </summary>
    bool CanCast(object script, int level, WorldThing caster, WorldThing? target, System.Numerics.Vector3? at);

    /// <summary>
    /// **The same <c>cancast</c> asked on a player's machine, to judge by** (<see cref="Net.OwnCast"/>): its answer when the
    /// script read only what that machine has as the host has it, saying nothing to anybody (a refusal's <c>DisplayNoCast</c> line
    /// is the host's to send); **null** when it read anything else (a pet count, another's blood, an item's flags, the chronicle's
    /// places …), failed, or this layer cannot tell — and then the cast's start is not guessed. A layer that does not say answers null.
    /// </summary>
    bool? Judge(object script, int level, WorldThing caster, WorldThing? target, System.Numerics.Vector3? at) => null;

    /// <summary>
    /// One of the three `cast` overloads (III, II, IIFFF) — which one is
    /// the caller's to choose, from the discipline's flags. The script's answer is not returned:
    /// the engine throws it away.
    /// </summary>
    void Cast(object script, Rpg.CastForm form, int level, WorldThing caster, WorldThing? target,
              System.Numerics.Vector3 at);

    /// <summary>`actorcast(casterGuid, disciplineName, level, castNum, captureId)` to the caster's own script and captures.</summary>
    void ActorCast(WorldThing caster, string discipline, int level, int castNum);

    /// <summary>`actorfeed(feederGuid, victimGuid, status, captureId)`: 0 start, 1 in progress, 2 stopped, 3 stopped by a kill.</summary>
    void ActorFeed(WorldThing feeder, WorldThing victim, int status);

    /// <summary>`actorstill(actorGuid, captureId)`, when a script holds a character still.</summary>
    void Stilled(WorldThing actor);

    /// <summary>`actorunstill(actorGuid, captureId)`, when that hold is cancelled.</summary>
    void Unstill(WorldThing actor);

    /// <summary>
    /// The object now belongs to another thing — a party member rebuilt at the far end of an exit
    /// (<see cref="Campaign"/>), whose effects and disciplines go with it. What its `GetClassThing`
    /// answers follows.
    /// </summary>
    void Rehome(object script, WorldThing owner);
}
