// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The effects' scripts on a player's machine. 1.1's player's machine makes
// an effect's script and fires its `effectstarted` as the effect arrives from the host and its `effectended` as the host takes it off: the
// `EffectDisc…` scripts spawn and attach their pads, rings and glows there, which the host never sends (they are
// flagged `NO NET SYNCH`). The place's own scripts — the level's, the scene's, the things',
// the chronicle's — are the host's by design, so a player's machine starts none of them.
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>
    /// **These scripts are a player's machine's** (`--players-machine`, which `Program` gives a player's machine in
    /// place of `--no-scripts`): entering a place starts none of its scripts — no thing's, level's, scene's or
    /// chronicle's, no `created` or `beginscene` — and only makes the engine's own scripts as the engine asks for
    /// them, which on a player's machine is an effect's as it arrives from the host (<see cref="ClientSeen"/> →
    /// <see cref="EffectRuntime"/>).
    /// </summary>
    public bool PlayersMachine { get; init; }

    bool playersMachineWrapped;

    /// <summary>
    /// A player's machine's entry into a place (<see cref="Enter"/> with <see cref="PlayersMachine"/>): the place's
    /// engine-made scripts are this object's (<see cref="Location.ScriptObjects"/>, so an effect put on makes its
    /// script and says `effectstarted`), a character's death is told to the scripts that captured it (which a player's
    /// machine reaches for the host's death too — the
    /// `EffectDisc…` scripts capture their wearer to hear it), and the effects already on the place's characters get
    /// their scripts now. Nothing else: no click reaches a script here (a player's use is the host's, sent as an
    /// order), no item's script hears `worn` (the wear does not send it on a player's machine), and no
    /// region, arrival, swap or chapter script runs.
    /// </summary>
    void EnterPlayersMachine(Location loc)
    {
        location = loc;
        wordHolder = loc.Minds;
        now = 0;
        if (loc.Player is { } who) PlayerGuid = who.Thing.Guid;
        thingGuids = [];
        worldCount = loc.Things.Count;
        chapterCount = 0;
        WrapPlayersMachine();
        loc.Killed = Killed;
        loc.ScriptObjects = this;
        MakeMissing(loc, started: true);
        // Where a place is whole (the autosave's moment), as `Program` asks without scripts (ours: kept so a
        // player's machine saves as it did before it had scripts).
        SceneStarting?.Invoke(loc, false);
    }

    /// <summary>
    /// The natives as a player's machine answers them (each native's test of the network mode). An
    /// effect put on by a script — `AddActorEffectByLevel` and `…ByValue`, both into the effect's add — is **refused** there unless it came from the host (the network
    /// argument 0 on a player's machine answers 0); and so is an effect taken off by one — `RemoveActorEffect`
    /// (refused the same way) — as the engine's own callers
    /// are there too (`EffectRuntime.Apply` and `Remove` refuse on a player's machine what is not the host's). A blow a script deals
    /// already does nothing here (`DamageActor` without its seventh argument; `Damage.Apply` on a
    /// player's machine). The rest act on this machine's own copy, as 1.1's do, and the host's next message of the
    /// kind puts the host's back: a thing made (made here, sent only by a host), an attach,
    /// a position, actor flags (only a host marks
    /// the thing to be sent), a sound on a thing (played here; a host also relays it), and
    /// those with no network test at all — `SetActorTeam`, `ExpandActorEffect`,
    /// `StopActorAction`, `ActorShapeShift` and `ActorEndShapeShift`,
    /// `OverrideActorWeapon`, `SetShell`, `SetAlpha`, `SetTimer`.
    /// </summary>
    void WrapPlayersMachine()
    {
        if (playersMachineWrapped) return;
        playersMachineWrapped = true;
        Wrap("CodexActor.AddActorEffectByLevel", (_, local) => Refused() ? CodexHost.Box(0) : local());
        Wrap("CodexActor.AddActorEffectByValue", (_, local) => Refused() ? CodexHost.Box(0) : local());
        Wrap("CodexActor.RemoveActorEffect", (_, local) => Refused() ? CodexHost.Box(false) : local());
    }

    /// <summary>Whether this place is a player's machine's, where the effect's add and removal refuse a script's effect.</summary>
    bool Refused() => location is { } here && NetOrders.Mode(here) == NetMode.Client;

    /// <summary>
    /// A timer come due: when it is an effect's script's, what it makes flagged `NO NET SYNCH`
    /// is marked as every player's machine's own (<see cref="MadeHere.ByEffect"/>) — the machine's own copy of the
    /// script has the same timer.
    /// </summary>
    MadeHere.Making EffectTimer(int scriptGuid)
    {
        if (!loose.TryGetValue(scriptGuid, out var entry) || entry.Script is not { } script) return default;
        foreach (var e in entry.Owner.Effects.All)
            if (ReferenceEquals(e.Script, script)) return MadeHere.ByEffect(entry.Owner, e);
        return default;
    }

    /// <summary>
    /// The same timer, for a save (ours): when it is an effect's script's, what it makes that no save keeps, and
    /// a shell it sets, are marked that effect's (<see cref="EffectMade.By"/>). No stretch for any other timer.
    /// </summary>
    EffectMade.Stretch EffectTimerMade(int scriptGuid)
    {
        if (!loose.TryGetValue(scriptGuid, out var entry) || entry.Script is not { } script) return default;
        foreach (var e in entry.Owner.Effects.All)
            if (ReferenceEquals(e.Script, script)) return EffectMade.By(entry.Owner, e);
        return default;
    }
}
