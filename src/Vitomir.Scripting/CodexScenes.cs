// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game's scene scripts, running against the world.
//
// This is the engine's side of the Codex API: it creates each scene's scripts when the player
// enters, gives them their GUIDs, runs their beginscene, keeps the timer queue they set, and
// implements the natives they call. Natives nothing implements yet log once and return a default
// (see CodexHost), so a scene that asks for something we have not built still runs.
using System.Numerics;
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;

using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes : ISceneScripts
{
    /// <summary>A script the engine created for the current scene.</summary>
    /// <param name="Thing">The thing it is attached to, or −1 for a scene-wide script.</param>
    sealed record Live(string Name, object Script, int Guid, int Thing);

    /// <summary>A timer a script set: when it fires, which script, which id, and its four floats.</summary>
    sealed record Pending(float At, int Guid, int Id, float A, float B, float C, float D);

    readonly ScriptRuntime runtime;
    readonly List<Live> live = [];
    readonly List<Pending> timers = [];
    readonly Dictionary<string, object> chronicles = new(StringComparer.Ordinal);
    /// <summary>Which thing each script is attached to, known before the script is constructed.</summary>
    readonly Dictionary<int, int> scriptThing = [];

    Location? location;
    /// <summary>
    /// Whose minds hold the AI's main flags word between places: 1.1 has one word for the whole
    /// game, so a script that turns the AI back on while its place is being left — a
    /// conversation's `convended` told it was aborted by the journey — turns it on for the
    /// next place too. Vitomir keeps the word on each place's minds and carries it across
    /// (<c>MainFlagsWord.Across</c>); until the next place is entered, the natives write the last one's.
    /// </summary>
    Vitomir.Engine.Ai.Minds? wordHolder;
    object? chronicle;
    float now;
    /// <summary>Script GUIDs start well above the thing GUIDs so the two never collide.</summary>
    int nextScriptGuid = ScriptGuidBase;
    /// <summary>Each thing's GUID, by its index in the scene's thing list.</summary>
    int[] thingGuids = [];
    // Where the chapter scene's things start in the joined list, and how many it has.
    int worldCount, chapterCount;

    public const int ScriptGuidBase = 0x100000;

    /// <summary>
    /// A place's scripts while the party is somewhere else. The original keeps up to six locations
    /// loaded, and their scripts go with them: leaving
    /// sends every one <c>endscene</c> and coming back sends the same objects <c>beginscene</c>
    /// again — nothing is constructed twice, so a script's own fields remember.
    /// </summary>
    sealed record Kept(List<Live> Live, List<Pending> Timers, Dictionary<int, int> ScriptThing, int[] ThingGuids,
                       int WorldCount, int ChapterCount, float Now);

    readonly Dictionary<Location, Kept> kept = new(ReferenceEqualityComparer.Instance);

    /// <summary>Forgets a place's kept scripts — it is being rebuilt, or thrown away with its chronicle.</summary>
    public void Forget(Location place) { kept.Remove(place); held.Remove(place); }

    /// <summary>Forgets every kept place.</summary>
    public void ForgetAll() => kept.Clear();

    /// <summary>
    /// The GUID the scripts are told is the player's — the real player thing's GUID when the place
    /// has one, and a stable fallback when it does not.
    /// </summary>
    public int PlayerGuid { get; set; } = 1;

    /// <summary>
    /// What `GetCurrentPlayer` answers: the character being steered, when the place has one, and
    /// <see cref="PlayerGuid"/> otherwise.
    /// </summary>
    public int CurrentPlayerGuid => location?.Steered is { Guid: not 0 } steered ? steered.Guid : PlayerGuid;

    /// <summary>
    /// The chronicle classes to fall back on when the chronicle files are not loaded, in the order
    /// they are tried. With them loaded, the <c>script</c> directive names the class outright.
    /// </summary>
    public static readonly string[] FallbackChronicles =
        ["PragueChronicle", "ViennaChronicle", "LondonChronicle", "NewYorkChronicle",
         "Prague2Chronicle", "MP_LOTChronicle", "MP_CTDChronicle"];

    public CodexHost Engine { get; }
    /// <summary>Every chronicle in the install, when the caller has loaded them.</summary>
    public ChronicleSet? Chronicles { get; set; }
    /// <summary>The chronicle the current scene belongs to, which the CodexSequence natives read.</summary>
    public Chronicle? Chronicle { get; set; }
    /// <summary>Which chronicle the scripts of the current scene turned out to belong to.</summary>
    public string? ChronicleName { get; private set; }
    /// <summary>The scripts running in this scene, by class name.</summary>
    public IEnumerable<string> Running => live.Select(l => l.Name);
    /// <summary>How many of them are attached to a thing rather than to the scene.</summary>
    public int ThingScripts => live.Count(l => l.Thing > 0);

    public CodexScenes(ScriptRuntime runtime)
    {
        this.runtime = runtime;
        Engine = runtime.Engine;
        Register();
    }

    /// <summary>The natives this layer implements. Everything else falls through to log-once.</summary>
    void Register()
    {
        // Index 0 is the chronicle's own script; a chronicle may name up to eight (the array
        // the `script` directives fill). Every shipped one names one.
        Engine.On("Codex.GetChronicleScript", (_, a) => CodexHost.Int(a, 0) == 0 ? chronicle : null);
        RegisterSequence();
        Engine.On("Codex.EnableLightStyle", (_, a) => Styles?.Enable(CodexHost.Int(a, 0), true));
        Engine.On("Codex.DisableLightStyle", (_, a) => Styles?.Enable(CodexHost.Int(a, 0), false));
        Engine.On("Codex.MaterialSetFrame", (_, a) =>
            location?.Materials.Find(CodexHost.Str(a, 0) ?? "")?.SetFrame(CodexHost.Int(a, 1)));
        Engine.On("Codex.SetTimer", (_, a) =>
        {
            int guid = CodexHost.Int(a, 0), ms = CodexHost.Int(a, 1), id = CodexHost.Int(a, 2);
            // **Timers stack**: a new one is always added, never in place of one
            // with the same id, so a script with one zap chain per victim on id 0 runs them all. Until
            // Once Vitomir replaced the pending one (found by the survey of the unread code).
            timers.Add(new Pending(now + Math.Max(0, ms) / 1000f, guid, id,
                                   CodexHost.Float(a, 3), CodexHost.Float(a, 4), CodexHost.Float(a, 5), CodexHost.Float(a, 6)));
            return CodexHost.Box(true);
        });
        // KillTimer takes every timer of that script with that id (the whole list is walked).
        Engine.On("Codex.KillTimer", (_, a) =>
        {
            int guid = CodexHost.Int(a, 0), id = CodexHost.Int(a, 1);
            return CodexHost.Box(timers.RemoveAll(t => t.Guid == guid && t.Id == id) > 0);
        });
        // `Codex.KillAllTimers`: every timer of that script, off the one global list.
        Engine.On("Codex.KillAllTimers", (_, a) =>
            CodexHost.Box(timers.RemoveAll(t => t.Guid == CodexHost.Int(a, 0)) > 0));
        // The thing a script is attached to. Scripts call this in their constructors, which is why
        // a script's GUID has to exist before its constructor body runs.
        RegisterThings();
        // Scripts call this in their constructors — ClickExit's first line does — so the mapping
        // has to exist before the script does, not after it has been added to the live list.
        // An effect's or a discipline's script answers the character it belongs to.
        Engine.On("Codex.GetClassThing", (_, a) =>
        {
            int guid = CodexHost.Int(a, 0);
            return CodexHost.Box(loose.TryGetValue(guid, out var e) ? e.Owner.Guid : scriptThing.GetValueOrDefault(guid));
        });
        RegisterActors();
        RegisterMaterialFilms();
        RegisterConsole();
        RegisterPowers();
        RegisterShops();
        RegisterSaving();
        RegisterNatives();
    }

    LightStyles? Styles => location?.Styles;

    /// <summary>The thing a CodexThing object stands for, or null.</summary>
    WorldThing? Of(object? self) => location?.Thing(runtime.GuidOf(self));

    /// <summary>
    /// The CodexThing natives that read and write a thing's own state. They are instance methods
    /// with no GUID parameter: the object the script holds says which thing it means.
    /// </summary>
    void RegisterThings()
    {
        Engine.On("CodexThing.GetThingFlags", (self, _) => CodexHost.Box((int)(Of(self)?.Flags ?? 0)));
        Engine.On("CodexThing.SetThingFlags", (self, a) =>
        {
            var t = Of(self);
            uint old = t?.Flags ?? 0;
            if (t is not null) t.Flags = old | (uint)CodexHost.Int(a, 0);
            return CodexHost.Box((int)old);                          // both flag verbs return the old flags
        });
        Engine.On("CodexThing.ClearThingFlags", (self, a) =>
        {
            var t = Of(self);
            uint old = t?.Flags ?? 0;
            if (t is not null) t.Flags = old & ~(uint)CodexHost.Int(a, 0);
            return CodexHost.Box((int)old);
        });
        Engine.On("CodexThing.GetDescriptionID", (self, _) => Of(self)?.DescriptionId ?? "");
        Engine.On("CodexThing.SetDescriptionID", (self, a) =>
        {
            var t = Of(self);
            if (t is not null) t.DescriptionId = CodexHost.Str(a, 0) ?? "";
            return CodexHost.Box(t is not null);
        });

        // A script takes control of a thing so the engine routes that thing's events to it.
        // `CaptureThing(scriptGuid, thingGuid, captureId)` — the first argument is resolved
        // as a Codex instance and the second as a thing, in that order.
        Engine.On("Codex.CaptureThing", (_, a) =>
        {
            var t = location?.Thing(CodexHost.Int(a, 1));
            if (t is null) return CodexHost.Box(false);
            t.TakeHold(CodexHost.Int(a, 0), CodexHost.Int(a, 2));      // newest first; a re-capture re-ids
            return CodexHost.Box(true);
        });
        Engine.On("Codex.ReleaseThing", (_, a) =>
        {
            // The one capture, if any, that this script holds on this thing.
            var t = location?.Thing(CodexHost.Int(a, 1));
            int who = CodexHost.Int(a, 0);
            if (t is null || t.Captures.RemoveAll(c => c.Script == who) == 0) return CodexHost.Box(false);
            return CodexHost.Box(true);
        });

        // Single player, and this process is the server.
        Engine.On("Codex.NetIsServer", (_, _) => CodexHost.Box(true));
        Engine.On("Codex.NetIsClient", (_, _) => CodexHost.Box(false));
        // The party roster: how many are in it, and the one in slot `n` — out
        // of range, or an empty slot, is 0. Two shipped scripts walk it to take an effect off
        // everybody as the level ends (`RegionAnyDamage`, `SEW2_Main`).
        Engine.On("CodexPlayer.GetNumPartyPlayers", (_, _) =>
            CodexHost.Box(location is { } here ? here.PartyMembers().Count() : 1));
        Engine.On("CodexPlayer.GetPartyPlayer", (_, a) =>
        {
            int n = CodexHost.Int(a, 0);
            if (location is not { } here) return CodexHost.Box(n == 0 ? PlayerGuid : 0);
            return CodexHost.Box(n >= 0 && here.PartyMembers().ElementAtOrDefault(n) is { } member ? member.Guid : 0);
        });
        // **The character being steered** (the global `SetSteered` writes) —
        // Wilhem while the player has taken him over, a possessed body while a possession lasts.
        // Every console line, camera shot and fade is addressed to it, which in single player is the
        // same camera whoever it names; but a region that only answers `causeGUID ==
        // GetCurrentPlayer()`, an enemy told `AISetTarget(GetCurrentPlayer())`, and a sound hung on
        // it all follow whoever is steered. Vitomir answered Christof whoever was steered until then.
        Engine.On("CodexPlayer.GetCurrentPlayer", (_, _) => CodexHost.Box(CurrentPlayerGuid));
        // A player-type character is **selected** — the party's own take-over,
        // with its checks. Three shipped scenes use it to hand the controls back to
        // Christof before they begin (`ANZK_8_4`, `ANZK_14_1`, `JUDB_7_8`).
        Engine.On("CodexPlayer.SetCurrentPlayer", (self, _) =>
        {
            if (Of(self) is not { Type: ThingType.Player } t) return CodexHost.Box(false);
            if (location is { Minds: not null } here) return CodexHost.Box(here.Steer(t));
            PlayerGuid = t.Guid;
            return CodexHost.Box(true);
        });
        Engine.On("CodexPlayer.GetPlayerName", (self, _) => Of(self)?.Actor?.CastId ?? "");
        Engine.On("CodexPlayer.SetPlayerName", (self, a) =>
        {
            if (Of(self)?.Actor is not { } actor) return CodexHost.Box(false);
            actor.Name = CodexHost.Str(a, 0) ?? "";
            return CodexHost.Box(true);
        });
        Engine.On("CodexPlayer.AddToParty", (self, _) => Party(self, true));
        Engine.On("CodexPlayer.RemoveFromParty", (self, _) => Party(self, false));
        Engine.On("CodexPlayer.SetPlayerHeadModel", (self, a) =>
        {
            if (Of(self)?.Actor is not { } actor) return CodexHost.Box(false);
            actor.HeadModel = CodexHost.Str(a, 0) ?? "";
            return CodexHost.Box(true);
        });
        // The original reads this off the network session object and returns 0 when there is none,
        // so single player gets 0 — which is what the multiplayer scripts
        // that use it as a loop bound want.
        Engine.On("CodexPlayer.GetMaxMPPlayers", (_, _) => CodexHost.Box(0));

        // Moving. A thing travels to a keyframe's pose at a speed, or swings about one over a
        // duration; when it gets there the script that owns it hears `arrived`.
        Engine.On("CodexThing.MoveToFrame", (self, a) =>
            CodexHost.Box(Of(self)?.MoveToFrame(CodexHost.Int(a, 0), CodexHost.Float(a, 1)) ?? false));
        Engine.On("CodexThing.RotatePivot", (self, a) =>
            CodexHost.Box(Of(self)?.RotatePivot(CodexHost.Int(a, 0), CodexHost.Float(a, 1)) ?? false));

        // Where a thing is, and the keyframes it can travel between. A scene authors most of them,
        // but a script may give a thing that has none — a door the level builder left plain — its
        // own, which is what AllocateFrames is for: it makes a THING_MOVE_NONE thing a
        // THING_MOVE_TRACK one with every frame at where the thing is standing.
        Engine.On("CodexThing.GetPosition", (self, _) => Vec(Of(self)?.Position ?? default));
        // Through the engine's own move (`Location.Move`), which re-finds the sector — not a bare
        // write to the position. A teleported thing used to keep the sector it started in and go on
        // blocking people where it had been.
        Engine.On("CodexThing.SetPosition", (self, a) =>
        {
            var t = Of(self);
            if (t is not null && location is not null) location.Move(t, Vec(a, 0));
            else if (t is not null) t.Position = Vec(a, 0);
            return CodexHost.Box(t is not null);
        });
        Engine.On("CodexThing.GetOrientation", (self, _) => Vec(Of(self)?.Orientation ?? default));
        Engine.On("CodexThing.SetOrientation", (self, a) =>
        {
            var t = Of(self);
            if (t is not null) t.Orientation = Vec(a, 0);
            return CodexHost.Box(t is not null);
        });
        Engine.On("CodexThing.GetNumFrames", (self, _) => CodexHost.Box(Of(self)?.Keyframes.Length ?? 0));
        Engine.On("CodexThing.GetCurFrame", (self, _) => CodexHost.Box((int)(Of(self)?.Frame ?? 0)));
        Engine.On("CodexThing.GetGoalFrame", (self, _) => CodexHost.Box(Of(self)?.Motion?.Frame ?? 0));
        Engine.On("CodexThing.GetFramePosition", (self, a) => Vec(Frame(self, a) ?.Position ?? default));
        Engine.On("CodexThing.GetFrameOrientation", (self, a) => Vec(Frame(self, a)?.Orientation ?? default));
        Engine.On("CodexThing.SetFramePosition", (self, a) => CodexHost.Box(SetFrame(self, a, position: true)));
        Engine.On("CodexThing.SetFrameOrientation", (self, a) => CodexHost.Box(SetFrame(self, a, position: false)));
        // It is fussier than it looks. **It refuses a thing that already has frames**
        // — there is no reallocating, only ClearFrames and then
        // this — and it takes **2 to 512** frames, not one and not a thousand. What it does *not*
        // do is make the thing movable: it sets an internal net-sync bit in the thing's flags and
        // nothing else, so a THING_MOVE_NONE prop with fresh frames still will not travel along
        // them until the script also calls SetMoveType. This page said the opposite until today.
        Engine.On("CodexThing.AllocateFrames", (self, a) =>
        {
            var t = Of(self);
            int n = CodexHost.Int(a, 0);
            if (t is null || t.Keyframes.Length > 0 || n < MinFrames || n > MaxFrames)
                return CodexHost.Box(false);
            // "with all its frames at the object's position" — the thing's own pose, repeated.
            t.Keyframes = [.. Enumerable.Repeat(new NsdKeyframe(t.Orientation, t.Position), n)];
            return CodexHost.Box(true);
        });
        Engine.On("CodexThing.ClearFrames", (self, _) =>
        {
            var t = Of(self);
            if (t is null) return CodexHost.Box(false);
            t.Keyframes = [];
            return CodexHost.Box(true);
        });

        // How a thing moves, and the only way it ever changes after placement.
        // The setter returns the **previous** kind, like every other Codex setter — except that an
        // argument outside 0..3 is not stored *and* answers −1 rather than the old value, which is
        // how a script tells a rejected write from an accepted one here.
        Engine.On("CodexThing.GetMoveType", (self, _) =>
            CodexHost.Box(Of(self) is { } t ? (int)t.MoveType : NoThing));
        Engine.On("CodexThing.SetMoveType", (self, a) =>
        {
            int want = CodexHost.Int(a, 0);
            if (Of(self) is not { } t || want < 0 || want >= WorldThing.MoveKinds)
                return CodexHost.Box(NoThing);
            int was = (int)t.MoveType;
            t.MoveType = (uint)want;
            return CodexHost.Box(was);
        });

        // **Stop, and it means a different thing to each kind of mover**. One switch on
        // the thing's move type, three arms and a silent fourth: a character's walk is called off, a
        // keyframe thing halts where it stands, a physics thing has its velocity zeroed, and
        // anything else is answered `true` having been left alone. The only false is a thing that
        // could not be found at all — so a script cannot learn from the answer whether its subject
        // was moving, and the shipped scripts do not ask.
        Engine.On("CodexThing.Stop", (self, _) =>
        {
            if (Of(self) is not { } t) return CodexHost.Box(false);
            switch (t.MoveType)
            {
                case WorldThing.MoveWalker: Walker(t)?.Stop(); break;
                case WorldThing.MoveTrack: t.StopMoving(); break;
                case WorldThing.MovePhysics: t.Velocity = Vector3.Zero; break;
            }
            return CodexHost.Box(true);
        });

        // Animation. The motion channel is what the character is doing with itself; the action
        // channel plays over it. The return is how long the animation runs, in milliseconds.
        Engine.On("CodexThing.PlayMotionSetMode", (self, a) => CodexHost.Box(PlayMode(self, a)));
        Engine.On("CodexThing.StopMotion", (self, _) => Of(self)?.Animation?.StopMotion());
        Engine.On("CodexThing.StopAction", (self, _) => Of(self)?.Animation?.StopAction());

        // The flag verbs, and they all answer the same way: **Set ORs and Clear ANDs-NOT, and each
        // returns the word as it was before**, never the new one. A script
        // that wants to put a flag back reads the answer.
        Engine.On("CodexThing.GetRenderFlags", (self, _) => CodexHost.Box((int)(Of(self)?.RenderFlags ?? 0)));
        Engine.On("CodexThing.SetRenderFlags", (self, a) => CodexHost.Box(Render(self, a, set: true)));
        Engine.On("CodexThing.ClearRenderFlags", (self, a) => CodexHost.Box(Render(self, a, set: false)));

        // Physics, and the gate that governs all of it: **only a MOVETYPE 3 thing has any**.
        // Everything else answers zero or false and is left alone, so a script
        // pushing a door about with SetVelocity has never done anything.
        Engine.On("CodexThing.GetCollideType", (self, _) => CodexHost.Box(Of(self) is { } t ? (int)t.Collide : -1));
        Engine.On("CodexThing.SetCollideType", (self, a) =>
        {
            if (Of(self) is not { } t) return CodexHost.Box(-1);
            int was = (int)t.Collide, want = CodexHost.Int(a, 0);
            // Outside the four the engine knows, the write is simply not made.
            if (want >= 0 && want < 4) t.Collide = (CollideShape)want;
            return CodexHost.Box(was);
        });
        Engine.On("CodexThing.GetPhysicsFlags", (self, _) =>
            CodexHost.Box((int)(Physical(self)?.PhysicsFlags ?? 0)));
        Engine.On("CodexThing.SetPhysicsFlags", (self, a) => CodexHost.Box(Physics(self, a, set: true)));
        Engine.On("CodexThing.ClearPhysicsFlags", (self, a) => CodexHost.Box(Physics(self, a, set: false)));

        Engine.On("CodexThing.GetVelocity", (self, _) => Vec(Physical(self)?.Velocity ?? default));
        Engine.On("CodexThing.SetVelocity", (self, a) =>
        {
            if (Physical(self) is not { } t) return CodexHost.Box(false);
            t.Velocity = Vec(a, 0);
            return CodexHost.Box(true);
        });
        Engine.On("CodexThing.GetRotVelocity", (self, _) => Vec(Physical(self)?.RotVelocity ?? default));
        Engine.On("CodexThing.SetRotVelocity", (self, a) =>
        {
            if (Physical(self) is not { } t) return CodexHost.Box(false);
            t.RotVelocity = Vec(a, 0);
            return CodexHost.Box(true);
        });

        // Making a thing, and unmaking one. `SpawnThing` puts the named template exactly where the
        // calling thing is standing and answers with its GUID; `Remove` takes a
        // thing out of the world and leaves whatever its DESTROY_TEMPLATE says. 100 shipped scripts
        // call one or the other, so between them they are most of what a scene can still not do.
        Engine.On("CodexThing.SpawnThing", (self, a) =>
        {
            string template = CodexHost.Str(a, 0) ?? "";
            if (location is null || Of(self) is not { } from) return CodexHost.Box(0);
            using (Vitomir.Engine.Net.MadeHere.ByScript(location))              // kept back unless `netprops`
                return CodexHost.Box(location.Spawn(template, from)?.Guid ?? 0);   // 0 is "no thing"
        });
        Engine.On("CodexThing.Remove", (self, _) =>
            CodexHost.Box(Of(self) is { } t && location is not null && location.Remove(t)));

        // The character a script names rather than indexes: 136 templates carry a CASTID, and a
        // scene script asks for "Sorvena" instead of knowing where the level builder put her.
        Engine.On("CodexThing.GuidFromCastID", (_, a) =>
        {
            string cast = CodexHost.Str(a, 0) ?? "";
            if (location is null || cast.Length == 0) return CodexHost.Box(0);
            // Through the cast lists: the party's first, then the place's 64.
            return CodexHost.Box(Vitomir.Engine.Rpg.CastList.Find(location, cast)?.Guid ?? 0);   // 0 is "no thing"
        });
    }

    /// <summary>One of a thing's keyframes, when the script named one it has.</summary>
    NsdKeyframe? Frame(object? self, object[] a)
    {
        var t = Of(self);
        int i = CodexHost.Int(a, 0);
        return t is not null && i >= 0 && i < t.Keyframes.Length ? t.Keyframes[i] : null;
    }

    bool SetFrame(object? self, object[] a, bool position)
    {
        var t = Of(self);
        int i = CodexHost.Int(a, 0);
        if (t is null || i < 0 || i >= t.Keyframes.Length) return false;
        var v = Vec(a, 1);
        var k = t.Keyframes[i];
        t.Keyframes[i] = position ? k with { Position = v } : k with { Orientation = v };
        return true;
    }

    int PlayMode(object? self, object[] a)
    {
        var animation = Of(self)?.Animation;
        if (animation is null || location?.Motions is not { } motions) return -1;
        return animation.Play(CodexHost.Int(a, 0), motions, CodexHost.Bool(a, 1), CodexHost.Float(a, 2));
    }

    /// <summary>A vector as the scripts pass one: three floats, x, y, z (CodexVector's VEC_*).</summary>
    static float[] Vec(Vector3 v) => [v.X, v.Y, v.Z];

    /// <summary>The thing behind a CodexThing, but only when it is one physics may touch.</summary>
    WorldThing? Physical(object? self) =>
        Of(self) is { MoveType: WorldThing.MovePhysics } t ? t : null;

    /// <summary>What every CodexThing native that answers with a number says when there is no thing.</summary>
    const int NoThing = -1;

    /// <summary>How many keyframes `AllocateFrames` will make: 2 to 512 and no other count.</summary>
    const int MinFrames = 2, MaxFrames = 512;

    /// <summary>
    /// The walker driving a character. Every character with an AI file has one hanging off its
    /// mind; the player's own thing is steered by the world's walker instead, and in a level where
    /// the player has no AI file that is the only place to find it.
    /// </summary>
    Player? Walker(WorldThing thing) => location?.WalkerFor(thing);

    /// <summary>
    /// The ground a script's point is on, the way `SendActorToPos` asks for it (the walker's own
    /// surface as the hint and no extra reach): the surface the walker is standing on
    /// if the point is over it, at whatever height; otherwise a walkable surface **below** the point
    /// by no more than 128 units, never one above it — but a point within
    /// 1e-5 of a surface's plane is on it (<see cref="WalkMap.FindUnder"/>). −1 when there is none.
    /// </summary>
    static int GroundFor(Player walker, Vector3 to)
    {
        if (walker.Map.IsGround(walker.Node) && walker.Map.Nodes[walker.Node].Contains(new Vector2(to.X, to.Y)))
            return walker.Node;
        return walker.Map.FindUnder(to, SendReach);
    }

    /// <summary>How far below a script's point the ground may be.</summary>
    public const float SendReach = 128f;

    /// <summary>`SetRenderFlags` / `ClearRenderFlags`, answering with the word as it was.</summary>
    int Render(object? self, object?[]? a, bool set)
    {
        if (Of(self) is not { } t) return 0;
        uint was = t.RenderFlags, arg = (uint)CodexHost.Int(a, 0);
        t.RenderFlags = set ? was | arg : was & ~arg;
        return (int)was;
    }

    /// <inheritdoc cref="Render"/>
    int Physics(object? self, object?[]? a, bool set)
    {
        if (Physical(self) is not { } t) return 0;
        uint was = t.PhysicsFlags, arg = (uint)CodexHost.Int(a, 0);
        t.PhysicsFlags = set ? was | arg : was & ~arg;
        return (int)was;
    }

    static Vector3 Vec(object[] a, int i) =>
        a.Length > i && a[i] is float[] { Length: >= 3 } f ? new Vector3(f[0], f[1], f[2]) : default;

    /// <summary>The character a CodexActor object stands for, or null when it is not one.</summary>
    Actor? ActorOf(object? self) => Of(self)?.Actor;

    /// <summary>The string-table key of a numbered aura (the aura's switch): <c>ACTOR_AURA_CALM</c>.</summary>
    public static string AuraKey(Actor actor) => "ACTOR_AURA_" + actor.AuraOrName.ToUpperInvariant();

    /// <summary>
    /// The CodexActor natives: a character's own state. Every field here is one of the engine's
    /// category-10 template tags, and the enumerations and flag bits are the game's own
    /// (Codex/System/Codex.java). Each setter answers with the value it replaced, or −1 when there
    /// is no character to set it on, which is the convention the SDK documents for all of them.
    ///
    /// What is deliberately *not* here is behaviour: AI, inventory, disciplines, effects and the
    /// combat state machine. Those natives still log once and carry on.
    /// </summary>
    /// <summary>What every actor native answers when the thing is not one.</summary>
    const int NoActor = -1;

    /// <summary>`ACTOR_TYPE_VAMPIRE GHOUL HUMAN MONSTER`; the setter rejects anything else.</summary>
    const int ActorTypes = 4;

    /// <summary>
    /// What `SendActorToPos` walks at when the script asks for a speed of zero: 150 units a second,
    /// written into the engine as a literal and the same number as
    /// <see cref="Player.DefaultWalkSpeed"/>, which is where it came from.
    /// </summary>
    const float DefaultSendSpeed = 150f;

    void RegisterActors()
    {

        // The type is one byte of the actor. Both natives answer **-1** when
        // there is no actor, and the setter **rejects** anything outside 0..3 — it leaves the value
        // alone and answers -1 rather than storing a nonsense type. Both return the *previous* type.
        Engine.On("CodexActor.GetActorType", (self, _) =>
            CodexHost.Box(ActorOf(self) is { } ac ? (int)ac.Type : NoActor));
        Engine.On("CodexActor.SetActorType", (self, a) =>
        {
            if (ActorOf(self) is not { } ac) return CodexHost.Box(NoActor);
            int want = CodexHost.Int(a, 0);
            if (want < 0 || want >= ActorTypes) return CodexHost.Box(NoActor);
            int was = (int)ac.Type;
            ac.Type = (ActorType)want;
            // The rules file's gain at the Embrace (ours: 0 by default, as the type's setter adds nothing).
            if (was == (int)ActorType.Human && want == (int)ActorType.Vampire
                && Vitomir.Engine.Rules.GameRules.Current.Ranks.EmbraceMaxHealthGain is var gain and not 0f)
                ac.MaxHealth += gain;
            return CodexHost.Box(was);
        });
        Engine.On("CodexActor.GetActorTeam", (self, _) => CodexHost.Box(ActorOf(self)?.Team ?? -1));
        Engine.On("CodexActor.SetActorTeam", (self, a) => CodexHost.Box(Set(self, ac =>
        {
            int old = ac.Team; ac.Team = CodexHost.Int(a, 0); return old;
        })));
        Engine.On("CodexActor.GetActorOriTeam", (self, _) => CodexHost.Box(ActorOf(self)?.OriginalTeam ?? -1));
        Engine.On("CodexActor.SetActorOriTeam", (self, a) => CodexHost.Box(Set(self, ac =>
        {
            int old = ac.OriginalTeam; ac.OriginalTeam = CodexHost.Int(a, 0); return old;
        })));

        // The flag verbs all answer with the flags as they were, which is how a script that wants
        // to put a flag back the way it found it does so.
        Engine.On("CodexActor.GetActorFlags", (self, _) => CodexHost.Box((int)(ActorOf(self)?.Flags ?? 0)));
        Engine.On("CodexActor.GetActorFlags2", (self, _) => CodexHost.Box((int)(ActorOf(self)?.Flags2 ?? 0)));
        Engine.On("CodexActor.SetActorFlags", (self, a) => CodexHost.Box(Set(self, ac =>
        {
            int old = (int)ac.Flags; ac.Flags = ac.Flags | (uint)CodexHost.Int(a, 0); return old;
        })));
        Engine.On("CodexActor.SetActorFlags2", (self, a) => CodexHost.Box(Set(self, ac =>
        {
            int old = (int)ac.Flags2; ac.Flags2 = ac.Flags2 | (uint)CodexHost.Int(a, 0); return old;
        })));
        Engine.On("CodexActor.ClearActorFlags", (self, a) => CodexHost.Box(Set(self, ac =>
        {
            int old = (int)ac.Flags; ac.Flags = ac.Flags & ~(uint)CodexHost.Int(a, 0); return old;
        })));
        Engine.On("CodexActor.ClearActorFlags2", (self, a) => CodexHost.Box(Set(self, ac =>
        {
            int old = (int)ac.Flags2; ac.Flags2 = ac.Flags2 & ~(uint)CodexHost.Int(a, 0); return old;
        })));

        Engine.On("CodexActor.GetActorClan", (self, _) => ActorOf(self)?.Clan ?? "");
        // -1 for no actor or no clan record, else the clan's id from the record's name.
        Engine.On("CodexActor.GetActorClanId", (self, _) => CodexHost.Box(ActorOf(self) is { ClanRecord: { } c } ? Vitomir.Engine.Rpg.Clans.IdOf(c.Name, true) : -1));
        // A new clan brings its record with it — the caps, the groups, the weaknesses — which is
        // what the actor's clan record is.
        Engine.On("CodexActor.SetActorClan", (self, a) =>
        {
            if (ActorOf(self) is not { } ac || !ac.SetClan(CodexHost.Str(a, 0) ?? "")) return CodexHost.Box(false);
            if (location?.Files is { } files) ac.ClanRecord = Vitomir.Engine.Rpg.Clans.For(files)[ac.Clan];
            return CodexHost.Box(true);
        });
        // (A comparison with 1.1.) A name a script set is answered as it was
        // set; a numbered aura is answered as its **text**, the string table's ACTOR_AURA_<name>
        // ("Calm"; "Unreadable" for none) — which is what Aura Perception and Eyes of Chaos print
        // after "Aura:". A missing key answers as itself.
        Engine.On("CodexActor.GetActorAura", (self, _) => ActorOf(self) is not { } ac ? ""
            : ac.AuraName.Length > 0 ? ac.AuraName
            : Nls?.Invoke(AuraKey(ac)) ?? AuraKey(ac));
        Engine.On("CodexActor.SetActorAura", (self, a) =>
        {
            if (ActorOf(self) is { } ac) ac.AuraName = CodexHost.Str(a, 0) ?? "";
        });
        Engine.On("CodexActor.SetActorPredefinedAura", (self, a) =>
        {
            if (ActorOf(self) is { } ac) { ac.Aura = (uint)CodexHost.Int(a, 0); ac.AuraName = ""; }
        });

        Engine.On("CodexActor.GetActorXPValue", (self, _) => CodexHost.Box(ActorOf(self)?.Xp ?? 0));
        Engine.On("CodexActor.SetActorXPValue", (self, a) => CodexHost.Box(Set(self, ac =>
        {
            int old = ac.Xp; ac.Xp = CodexHost.Int(a, 0); return old;
        })));

        Engine.On("CodexActor.GetActorHealth", (self, _) => CodexHost.Box(ActorOf(self)?.Health ?? 0f));
        Engine.On("CodexActor.SetActorHealth", (self, a) => CodexHost.Box(SetF(self, ac =>
        {
            float old = ac.Health; ac.Health = CodexHost.Float(a, 0); return old;
        })));
        Engine.On("CodexActor.GetActorMaxHealth", (self, _) => CodexHost.Box(ActorOf(self)?.MaxHealth ?? 0f));
        Engine.On("CodexActor.SetActorMaxHealth", (self, a) => CodexHost.Box(SetF(self, ac =>
        {
            float old = ac.MaxHealth; ac.MaxHealth = CodexHost.Float(a, 0); return old;
        })));

        Engine.On("CodexActor.GetActorWalkSpeed", (self, _) => CodexHost.Box(ActorOf(self)?.WalkSpeed ?? 0f));
        Engine.On("CodexActor.SetActorWalkSpeed", (self, a) => CodexHost.Box(SetF(self, ac =>
        {
            float old = ac.WalkSpeed; ac.WalkSpeed = CodexHost.Float(a, 0); return old;
        })));
        Engine.On("CodexActor.GetActorRunSpeed", (self, _) => CodexHost.Box(ActorOf(self)?.RunSpeed ?? 0f));
        Engine.On("CodexActor.SetActorRunSpeed", (self, a) => CodexHost.Box(SetF(self, ac =>
        {
            float old = ac.RunSpeed; ac.RunSpeed = CodexHost.Float(a, 0); return old;
        })));

        // Stats and soaks are two parallel pairs: what the character has now, and what its template
        // gave it. Effects and disciplines move the first and leave the second alone.
        Engine.On("CodexActor.GetActorStat", (self, a) => CodexHost.Box(Read(ActorOf(self)?.Stat, a)));
        Engine.On("CodexActor.GetActorBaseStat", (self, a) => CodexHost.Box(Read(ActorOf(self)?.BaseStat, a)));
        // SetActorStat: a character's current stat; a change to Humanity is announced
        // the way a kill's is (Damage.HumanityChanged).
        Engine.On("CodexActor.SetActorStat", (self, a) =>
        {
            float old = Write(ActorOf(self)?.Stat, a);
            if (CodexHost.Int(a, 0) == (int)ActorStat.Humanity && Of(self) is { Type: ThingType.Actor or ThingType.Player } who)
                Vitomir.Engine.Rpg.Damage.HumanityChanged(who, old, CodexHost.Float(a, 1));
            return CodexHost.Box(old);
        });
        Engine.On("CodexActor.SetActorBaseStat", (self, a) => CodexHost.Box(Write(ActorOf(self)?.BaseStat, a)));
        Engine.On("CodexActor.GetActorSoak", (self, a) => CodexHost.Box(Read(ActorOf(self)?.Soak, a)));
        Engine.On("CodexActor.GetActorBaseSoak", (self, a) => CodexHost.Box(Read(ActorOf(self)?.BaseSoak, a)));
        Engine.On("CodexActor.SetActorSoak", (self, a) => CodexHost.Box(Write(ActorOf(self)?.Soak, a)));
        Engine.On("CodexActor.SetActorBaseSoak", (self, a) => CodexHost.Box(Write(ActorOf(self)?.BaseSoak, a)));

        // **Send a character somewhere**, and the one piece of AI a scene script can
        // reach: 8 calls at the start of the level being tested alone. It is a walk order and
        // nothing more — no fighting, no following, no mode change — and it is gated four times
        // before the walker ever hears about it:
        //
        //  1. the thing must be a character, `THING_TYPE_ACTOR` or `THING_TYPE_PLAYER` — the test
        //     is `type == 0 || type == 7`, and everything else is refused;
        //  2. **its health must be above zero**, so a corpse does not walk;
        //  3. the point must be over walkable ground, which is the `SURFACE_WALK` bit on the
        //     surface found under it;
        //  4. and then `simWalker::WalkTo` has to find a route, whose answer is this one's.
        //
        // The speed is taken as an **absolute value**, and **zero means 150** — the engine's own
        // default walk speed, written in as a constant. So `SendActorToPos(p, 0)`
        // and `SendActorToPos(p, -300)` are both meaningful orders, which is worth knowing because
        // the shipped scripts pass both.
        Engine.On("CodexActor.SendActorToPos", (self, a) =>
        {
            if (Of(self) is not { } t || t.Type is not (ThingType.Actor or ThingType.Player))
                return CodexHost.Box(false);
            if (t.Actor is not { Health: > 0f }) return CodexHost.Box(false);
            if (Walker(t) is not { } walker) return CodexHost.Box(false);
            var to = Vec(a, 0);
            int node = GroundFor(walker, to);
            if (node < 0) return CodexHost.Box(false);
            // **A repair** (`Repairs.ClosedRound`): a walk to where the character already stands is
            // no walk at all to the engine, with no `arrived`, which stops every patrol
            // whose round ends on its first keyframe. Here it counts as having arrived, and the
            // script hears so on the next step.
            if (Vector3.Distance(to, t.Position) < Vitomir.Engine.Rules.GameRules.Current.Movement.ArrivedDistance)
            {
                walker.Stop();
                if (!pendingArrivals.Contains(t)) pendingArrivals.Add(t);
                return CodexHost.Box(true);
            }
            float speed = MathF.Abs(CodexHost.Float(a, 1));
            // Straight to the walker with the point as named — no move-order clamp — so
            // a patrol arrives exactly on its keyframes.
            // And it never gives up (`Player.WalkTo`'s `persist`): nothing will order this walk
            // again, and 1.1's walker stands and retries until whatever is in the way moves.
            return CodexHost.Box(walker.WalkTo(to, node, speed: speed > 0f ? speed : DefaultSendSpeed,
                                               clamp: false, persist: true));
        });

        // A character's animation, asked for the way an actor asks: by mode number.
        Engine.On("CodexActor.PlayActorMotionSetMode", (self, a) => CodexHost.Box(PlayMode(self, a)));
        Engine.On("CodexActor.StopActorMotion", (self, _) => Of(self)?.Animation?.StopMotion());
        Engine.On("CodexActor.StopActorAction", (self, _) => Of(self)?.Animation?.StopAction());
    }

    /// <summary>THING_AF_PARTY is how the engine marks the characters travelling with the player.</summary>
    object Party(object? self, bool join)
    {
        if (Of(self)?.Actor is not { } actor) return CodexHost.Box(false);
        // Joining is `AddPlayer` (which sets THING_AF_PARTY) and then the formation bit.
        // Leaving clears only THING_AF_PARTY — the formation bit is left set, which does
        // no harm because `InParty` also wants the party slot, and is the original's behaviour.
        if (join) { actor.Flags |= Actor.Flag.Party; actor.Flags2 |= Actor.Flag2.InFormation; }
        else
        {
            actor.Flags &= ~Actor.Flag.Party;
            // The one leaving was the party's selected member: the next member still standing takes
            // over — and with nobody standing, the first member left is
            // revived and takes over (World.PartyLeaving).
            if (Of(self) is { } gone && location is { } here) PartyLeaving.Left(here, gone);
        }
        if (Of(self)?.Place is { } at) Vitomir.Engine.Rpg.CastList.PartyRebuilt(at);  // the cast list rebuilt
        return CodexHost.Box(true);
    }

    /// <summary>Changes one of a character's whole-number fields and answers with what was there.</summary>
    int Set(object? self, Func<Actor, int> change) => ActorOf(self) is { } ac ? change(ac) : -1;
    float SetF(object? self, Func<Actor, float> change) => ActorOf(self) is { } ac ? change(ac) : -1f;

    static float Read(float[]? values, object[] a)
    {
        int i = CodexHost.Int(a, 0);
        return values is not null && i >= 0 && i < values.Length ? values[i] : 0f;
    }

    static float Write(float[]? values, object[] a)
    {
        int i = CodexHost.Int(a, 0);
        if (values is null || i < 0 || i >= values.Length) return -1f;
        float old = values[i];
        values[i] = CodexHost.Float(a, 1);
        return old;
    }

    /// <summary>
    /// **"The game console" is the status bar's message box.** In single player all three print
    /// natives end in the box's formatted add — which is where 1.1 shows "Christof uses Discipline Possession." and
    /// "Monk resisted Discipline Possession.", and where a script's <c>&lt;ERROR&gt;</c> lines land in
    /// red. The colour is the script's plus <c>0xFF000000</c> (the Java overloads default to
    /// <c>0xFFFFFF</c>, so white); the client and the flags matter only to multiplayer. Print keeps
    /// the first 255 characters (a 256-byte copy); PrintNLS is the plain lookup;
    /// PrintFormatNLS keeps 31 characters of the id and 1,023 of the pairs, and an empty pair string
    /// asks for the line as it stands; with no session, the scenes' own string tables (<see cref="Nls"/>) build it. Clear empties the box.
    /// Vitomir also keeps what was printed in <see cref="Lines"/>, for its own console and tests.
    /// </summary>
    void RegisterConsole()
    {
        Engine.On("CodexConsole.Print", (_, a) => Print(Cap(CodexHost.Str(a, 2), 255), CodexHost.Int(a, 3)));
        Engine.On("CodexConsole.PrintNLS", (_, a) =>
            Print(CodexHost.Str(a, 2) is { Length: > 0 } key ? Say(key) : null, CodexHost.Int(a, 3)));
        Engine.On("CodexConsole.PrintFormatNLS", (_, a) =>
        {
            string key = Cap(CodexHost.Str(a, 2), 31) ?? "";
            string pairs = Cap(CodexHost.Str(a, 3), 1023) ?? "";
            if (key.Length == 0) return;
            // With no Session (a harness's scenes), the same line builder over the string tables the scenes were given (as
            // `FormatNLS` answers, so `Discipline`'s shipped `PrintFormatNLS` lines read as the SDK's `Print(FormatNLS(…))` do).
            Print(Session?.Messages.Format(key, pairs.Length > 0 ? pairs : null)
                  ?? Vitomir.Engine.Ui.Strings.Format(k => Nls?.Invoke(k), key, pairs.Length > 0 ? pairs : null), CodexHost.Int(a, 4));
        });
        Engine.On("CodexConsole.Clear", (_, _) =>
        {
            Lines.Clear();
            Session?.Messages.Clear();
        });

        static string? Cap(string? s, int most) => s is not null && s.Length > most ? s[..most] : s;
        string Say(string key) => Session?.Messages.Format(key) ?? key;
    }

    /// <summary>What the scripts have printed, oldest first, capped so a long session cannot grow it forever.</summary>
    public List<string> Lines { get; } = [];

    /// <summary>The most lines the console keeps.</summary>
    public const int MaxConsoleLines = 512;

    /// <summary>The colour a print is drawn in: the script's plus <c>0xFF000000</c>.</summary>
    public static uint ConsoleColour(int colour) => unchecked((uint)colour + 0xFF000000u);

    void Print(string? line, int colour = 0xFFFFFF)
    {
        if (string.IsNullOrEmpty(line)) return;
        Lines.Add(line);
        if (Lines.Count > MaxConsoleLines) Lines.RemoveRange(0, Lines.Count - MaxConsoleLines);
        Diag.For("Vitomir.Scripting").LogInformation("console: {Line}", line);
        Session?.Messages.Add(line, ConsoleColour(colour));
    }

    /// <summary>
    /// The CodexSequence natives: the campaign's map of locations, the exits between them, and the
    /// thousand story flags scripts keep on it. All of them are static and read the one chronicle
    /// the game has loaded; with no chronicle loaded they answer
    /// as an empty one would.
    /// </summary>
    void RegisterSequence()
    {
        Engine.On("CodexSequence.GetNumLocations", (_, _) => CodexHost.Box(Chronicle?.Count ?? 0));
        Engine.On("CodexSequence.GetCurrentLocation", (_, _) => CodexHost.Box(Chronicle?.Current ?? 0));
        Engine.On("CodexSequence.GetLocationName", (_, a) => Chronicle?.NameOf(CodexHost.Int(a, 0)) ?? "");
        Engine.On("CodexSequence.GetChronicleInfoFlags", (_, _) => CodexHost.Box((int)(Chronicle?.Flags ?? 0)));

        Engine.On("CodexSequence.GetLocationFlags", (_, a) =>
            CodexHost.Box((int)(Chronicle?.GetLocationFlags(Name(a, 0)) ?? 0)));
        Engine.On("CodexSequence.SetLocationFlags", (_, a) =>
            CodexHost.Box((int)(Chronicle?.SetLocationFlags(Name(a, 0), (uint)CodexHost.Int(a, 1)) ?? 0)));
        Engine.On("CodexSequence.ClearLocationFlags", (_, a) =>
            CodexHost.Box((int)(Chronicle?.ClearLocationFlags(Name(a, 0), (uint)CodexHost.Int(a, 1)) ?? 0)));

        Engine.On("CodexSequence.IsExitValid", (_, a) =>
            CodexHost.Box(Chronicle?.IsExitValid(Name(a, 0), CodexHost.Int(a, 1)) ?? false));
        Engine.On("CodexSequence.IsExitClosed", (_, a) =>
            CodexHost.Box(Chronicle?.IsExitClosed(Name(a, 0), CodexHost.Int(a, 1)) ?? false));
        Engine.On("CodexSequence.OpenExit", (_, a) => ExitState(Name(a, 0), CodexHost.Int(a, 1), closed: false));
        Engine.On("CodexSequence.CloseExit", (_, a) => ExitState(Name(a, 0), CodexHost.Int(a, 1), closed: true));
        Engine.On("CodexSequence.ChangeExit", (_, a) =>
            CodexHost.Box(Chronicle?.ChangeExit(Name(a, 0), CodexHost.Int(a, 1), Name(a, 2), CodexHost.Int(a, 3)) ?? false));

        // **Leaving.** `TakeExit(exitNum, actorGuid)` is what every `ClickExit` in the game calls —
        // and `ClickExit` is 216 things across the campaign, so this is how a level is left. The
        // native itself is thin: the second argument must resolve to a **character**
        // (type 0 or 7) and it hands off to the main sequence, which
        // validates the exit and sets the campaign's state machine to 4. The change of place
        // happens later, in the main loop, when it notices that state.
        //
        // So this records the request, with the original's three silent refusals intact, and the
        // world changes a moment later, when the main loop sees it (`Campaign.Travel`) — the same
        // two steps the engine takes.
        Engine.On("CodexSequence.TakeExit", (_, a) =>
        {
            if (location?.Thing(CodexHost.Int(a, 1)) is not { } who
                || who.Type is not (ThingType.Actor or ThingType.Player)) return null;
            if (Vitomir.Engine.Levels.Cells.CellExitSeam.Taken(location, who, CodexHost.Int(a, 0))) return null;   // a cell level's exits (ours)
            if (Chronicle?.TakeExit(CodexHost.Int(a, 0)) is true && Chronicle.Leaving is { } going)
                Diag.For("Vitomir.Scripting").LogInformation(
                    "exit {Exit} taken from {From} to {To}, entrance {Entrance}",
                    going.Exit, Chronicle.NameOf(Chronicle.Current), Chronicle.NameOf(going.To), going.Entrance);
            return null;                                        // the native returns void
        });

        // Each of these returns the flag's *previous* state, which is what the SDK documents and
        // what the original does (it reads before it writes).
        Engine.On("CodexSequence.GetChronicleFlag", (_, a) => CodexHost.Box(Chronicle?.GetFlag(CodexHost.Int(a, 0)) ?? false));
        Engine.On("CodexSequence.SetChronicleFlag", (_, a) => CodexHost.Box(Chronicle?.SetFlag(CodexHost.Int(a, 0)) ?? false));
        Engine.On("CodexSequence.ClearChronicleFlag", (_, a) => CodexHost.Box(Chronicle?.ClearFlag(CodexHost.Int(a, 0)) ?? false));
        Engine.On("CodexSequence.ToggleChronicleFlag", (_, a) => CodexHost.Box(Chronicle?.ToggleFlag(CodexHost.Int(a, 0)) ?? false));
        Engine.On("CodexSequence.ClearAllChronicleFlags", (_, _) => Chronicle?.ClearAllFlags());

        Engine.On("CodexSequence.GetPortalLocation", (_, _) => CodexHost.Box(Chronicle?.Portal.Location ?? -1));
        Engine.On("CodexSequence.SetPortalLocation", (_, a) =>
            CodexHost.Box(Chronicle?.SetPortal(CodexHost.Int(a, 0), CodexHost.Int(a, 1)) ?? false));
        Engine.On("CodexSequence.GetReviveLocation", (_, _) => CodexHost.Box(Chronicle?.Revive.Location ?? -1));
        Engine.On("CodexSequence.SetReviveLocation", (_, a) =>
            CodexHost.Box(Chronicle?.SetRevive(CodexHost.Int(a, 0), CodexHost.Int(a, 1)) ?? false));

        // A thing is in whichever location the game has loaded, which is the one we are in.
        Engine.On("CodexThing.GetLocationNum", (_, _) => CodexHost.Box(Chronicle?.Current ?? -1));

        RegisterCutscenes();
        RegisterTeleports();
        RegisterSound();
        RegisterProgress();
        RegisterBodies();
        RegisterParticles();
        RegisterLights();
    }

    /// <summary>A location name argument, as the natives take it.</summary>
    static string Name(object?[] a, int i) => CodexHost.Str(a, i) ?? "";

    /// <summary>
    /// Creates the scripts this location's things name and runs each one's <c>beginscene</c>.
    /// A scene script casts its chronicle in its constructor, so the wrong one throws
    /// ClassCastException: that is how the right chronicle is found, and it is cached after the
    /// first script settles it.
    /// </summary>
    /// <summary>
    /// Called once a place is whole and before its scripts are told the scene has begun, with
    /// whether a load is bringing it back — where 1.1 writes the autosave.
    /// </summary>
    public Action<Location, bool>? SceneStarting { get; set; }

    public void Enter(Location loc)
    {
        if (location is not null) Leave();
        if (PlayersMachine) { EnterPlayersMachine(loc); return; }                   // a player's machine: effects' scripts only (CodexScenes.PlayersMachine.cs)
        location = loc;
        wordHolder = loc.Minds;
        now = 0;
        Unhold(loc);                                                                // a host's place with players in it (CodexScenes.Places.cs)
        bool back = kept.Remove(loc, out var was);
        // A place a load is bringing back: its scripts are made afresh and then given back what
        // they had (`CodexScenes.Saving.cs`), and its clock is the one the save kept.
        armed.Remove(loc, out var restoring);
        if (restoring is not null) { back = false; now = restoring.ClockMs / 1000f; }
        // With a player in the world, that is who the scripts are told about; without one the
        // number only has to be stable, and thing 1 is as good as any.
        if (loc.Player is { } who) PlayerGuid = who.Thing.Guid;
        // Which chronicle owns this place. The scene file names exactly one location; the level
        // alone can name several, so the scene is tried first (ChronicleSet.For).
        if (Chronicles?.For(loc.SceneFile, loc.Name) is { } where)
        {
            Chronicle = where.Chronicle;
            Chronicle.Current = where.Location;
        }
        // Every thing gets a GUID; a script naming another thing is given that thing's. The list is
        // the world scene's things followed by the chapter scene's, which is the order the original
        // appends them in — but **a CODEXPARAMS thing index counts through its own scene's list**,
        // not the joined one. `MON1_Main` sits in the monastery's chapter scene and names its
        // regions as 18, 23, 44, 45, 46 and 47: the chapter's own 18th thing is a trigger region,
        // and the joined list's 18th is a torch. Read as the joined list, every chapter script in
        // the game was handed the world's things instead of its own, and captured nothing it meant
        // to — no region conversation could ever start.
        var things = loc.Things;
        if (back && was is not null)
        {
            // A place the party has been before: the same script objects, their timers and their
            // clock, picked up where they were put down.
            live.AddRange(was.Live);
            timers.AddRange(was.Timers);
            foreach (var (k, v) in was.ScriptThing) scriptThing[k] = v;
            thingGuids = was.ThingGuids;
            worldCount = was.WorldCount;
            chapterCount = was.ChapterCount;
            now = CatchUp(loc, was.Now, timers);                                    // what came due while away is gone
        }
        else
        {
            worldCount = loc.ChapterScene is not null && loc.WorldScene is { } world ? world.Things.Length : things.Count;
            chapterCount = loc.ChapterScene?.Things.Length ?? 0;
            // By each thing's own scene index, not its place in the list. The list loses things —
            // a load takes out what the save says is gone, a pick-up takes out the item — and every
            // later thing moves up one, so reading the list by position handed a script its
            // neighbour. An autosave in Old Town by day: `OTDY_4_1`'s "Stranger"
            // came out as the monk beside the knights, so clicking the monk started the old man's
            // speech, the party was teleported to the gate, and the old man himself answered nothing.
            // A thing that is gone leaves 0, which the script gets as null — unless a load took it
            // away, when it keeps the GUID it had, as 1.1's saved parameters do (Location.Gone).
            int size = Math.Max(worldCount + chapterCount, things.Count == 0 ? 0 : things.Max(t => t.Index) + 1);
            thingGuids = new int[size];
            foreach (var t in things)
                if (t.Index >= 0 && t.Index < size) thingGuids[t.Index] = t.Guid;
            foreach (var (index, guid) in loc.Gone)
                if (index >= 0 && index < size && thingGuids[index] == 0) thingGuids[index] = guid;
        }
        // The use verb reaches the script layer through here rather than through the interface,
        // for the same reason the item callbacks do: the engine half must not know about Java.
        loc.Clicked = Clicked;
        loc.Killed = Killed;
        HookCallbacks(loc);
        loc.ScriptObjects = this;
        LandTimers();
        loc.Created = Created;
        loc.Doomed = Doom;                                                          // a destroyed thing's scripts (CodexScenes.Destroyed.cs)
        loc.ChapterSwapped = Swapped;                                               // the live chapter swap (CodexScenes.Swap.cs)
        loc.Arrived = (thing, frame) => { if (ReferenceEquals(thing.Place, location)) Arrived(thing, frame); };
        loc.Awakened = (thing, target) => { if (ReferenceEquals(thing.Place, location)) Fire("awaken", thing, target); };
        // Walking into and out of a region, through the same two loops: the region's own script with capture 0, then every script that captured it.
        if (loc.Regions is { } regions)
        {
            regions.Entered = (region, mover) => Fire("entered", region, mover);
            regions.Exited = (region, mover) => Fire("exited", region, mover);
        }

        var made = new List<(Live Script, WorldThing Thing)>();
        // A place come back to keeps its scripts — but a thing made in it while the party was away
        // (the far end of Walk the Abyss) has none yet, and gets it now, as the engine's finalize
        // gave it one when it was made.
        // And so does a thing a chapter swap brought in while the party was away (CodexScenes.Swap.cs).
        foreach (var thing in back ? things.Where(t => (t.Index < 0 || loc.AwaitingScripts.Contains(t)) && live.All(l => l.Thing != t.Guid)) : things)
        {
            if (thing.Script.Length == 0) continue;
            if (Attach(thing, thing.Index >= worldCount ? worldCount : 0,
                       thing.Index >= worldCount ? chapterCount : worldCount) is { } l) made.Add((l, thing));
        }
        // Every one of them was finalized as the level went in, and the finalize ends in
        // `created(thingGuid)` — before the scene begins. TorchBearer lights
        // its torch here, SteamBlast starts its clock, ClassSpiderSac arms itself. A load's things
        // were finalized when they were first made, and are not told again.
        if (restoring is null) foreach (var (l, thing) in made) SayCreated(l, thing);

        // The effect and discipline scripts nobody could make before there were scripts.
        MakeMissing(loc, started: restoring is null);

        // A load: every script given back its state, its timers and its captures, and told so.
        if (restoring is not null) RestoreScripts(loc, restoring, made);
        RestoreChronicle();
        Awaited(loc, made, told: restoring is null);                                // a swap's arrivals (CodexScenes.Swap.cs)

        Vitomir.Engine.Rpg.CastList.Arrive(loc);                                     // the place's cast list, made once
        // The scene start's own work before its scripts hear of it: the autosave, which
        // the host takes here — after the place is whole and before any `beginscene` — and which a
        // place a load is bringing back does not get.
        if (opening) return;                                                        // a host's place opened for a player alone (CodexScenes.Places.cs)
        SceneStarting?.Invoke(loc, restoring is not null);

        // beginscene runs on every script once the whole scene exists, thing scripts included.
        // Its first argument is the *player's* GUID, not the script's: scripts wrap it in a
        // CodexThing and ask it questions (ClickExit asks which location the player is in).
        // In the world's list's order, newest thing first (`InListOrder`), over
        // the list as it stands: a script made during another's `beginscene` (a spawned thing's) is
        // told `created`, not `beginscene`, and must not break the walk.
        foreach (var l in InListOrder())
        {
            try { runtime.Call(l.Script, "beginscene", PlayerGuid, 0); }
            catch (Exception e) { Engine.Log($"{l.Name}.beginscene failed: {ScriptException.From(l.Name, e).JavaType}"); }
        }
        BeginCarried();                                            // and what the characters carry (CodexScenes.Carried.cs)
        // And then the chronicle's own scripts, which hear every scene begin (the scene
        // start's second loop) — Vienna's is how the snow starts.
        foreach (var (name, script) in ChronicleScripts())
        {
            try { runtime.Call(script, "beginscene", PlayerGuid, 0); }
            catch (Exception e) { Engine.Log($"{name}.beginscene failed: {ScriptException.From(name, e).JavaType}"); }
        }
    }

    /// <summary>The running chronicle's scripts (`gameChronicle` <c>apScripts</c>), made the first time they are asked for.</summary>
    IEnumerable<(string Name, object Script)> ChronicleScripts()
    {
        foreach (var name in Chronicle?.Data.Scripts ?? [])
        {
            if (!chronicles.TryGetValue(name, out var obj))
            {
                if (!runtime.TryInstantiate(name, out var made, out _) || made is null) continue;
                chronicles[name] = obj = made;
            }
            yield return (name, obj);
        }
    }

    /// <summary>
    /// The CODEXPARAMS of a thing, converted for its script's constructor. The original converts
    /// by the <em>constructor's</em> parameter type, not the type stored in the scene, and passes
    /// the values by position (Technical Inventory, "CODEXPARAMS → constructor"). Returns null when
    /// there is no such class. This and <see cref="Convert"/> together are the original's switch over
    /// the constructor's parameter types; the object itself is then allocated, given its
    /// GUID and constructed with these, in that order (the order matters).
    /// </summary>
    readonly HashSet<string> shortParams = new(StringComparer.Ordinal);

    object?[]? Arguments(string className, NsdThing thing, int scriptGuid, int sceneBase, int sceneCount)
    {
        var types = runtime.ConstructorTypes(className);
        if (types is null) { Engine.Log($"no script class {className}"); return null; }
        var args = new object?[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            var p = i < thing.Params.Length ? thing.Params[i] : default;
            // The original reads past the array here. That is a code bug, not a game rule. Said once a
            // class: a thing made at run time with a scripted template — the feeder rats — has no
            // scene record at all, and the line came seven times for every rat (as seen in play).
            if (i == thing.Params.Length && shortParams.Add(className))
                Engine.Log($"{className} wants {types.Length} parameters, the scene stores {thing.Params.Length} (said once)");
            args[i] = Convert(className, types[i], p, sceneBase, sceneCount);
        }
        return args;
    }

    object? Convert(string className, string type, NsdParam p, int sceneBase, int sceneCount) => type switch
    {
        "int" or "short" => java.lang.Integer.valueOf(p.Bits),
        "long" => java.lang.Long.valueOf(p.Bits),
        "float" => java.lang.Float.valueOf(p.AsFloat),
        "java.lang.String" => p.Text,
        // A thing reference is an index into the thing's own scene's thing list; the script gets a
        // wrapper around that thing's GUID. −1, or an index the scene does not have, gives null.
        "CodexThing" or "CodexRegion" or "CodexActor" or "CodexPlayer" or "CodexItem" or "CodexProp" =>
            p.Bits >= 0 && p.Bits < sceneCount && sceneBase + p.Bits < thingGuids.Length
                && thingGuids[sceneBase + p.Bits] != 0
                ? runtime.NewThingRef(type, thingGuids[sceneBase + p.Bits]) : null,
        // The original always passes these as null, whatever the scene stores.
        "CodexSector" or "CodexVector" => null,
        _ => Unknown(className, type),
    };

    object? Unknown(string className, string type)
    {
        Engine.Log($"{className}'s constructor takes a {type}, which the engine has no conversion for");
        return null;
    }

    /// <summary>
    /// Instantiates a scene script under its chronicle's script class. A scene script casts the
    /// object <c>GetChronicleScript</c> hands it, so the wrong class throws ClassCastException;
    /// the chronicle file's <c>script</c> directive says which is right. Without the chronicle
    /// files we fall back to trying each known class in turn, which is what we did before they
    /// were decoded.
    /// </summary>
    object? Create(string name, int guid, object?[] args)
    {
        var named = Chronicle?.Data.Scripts;
        var order = named is { Count: > 0 } ? named.AsEnumerable()
            : ChronicleName is { } known
                ? new[] { known }.Concat(FallbackChronicles.Where(c => c != known))
                : FallbackChronicles;
        string? why = null;
        foreach (var c in order)
        {
            if (!chronicles.TryGetValue(c, out var obj))
            {
                if (!runtime.TryInstantiate(c, out var made, out _) || made is null) continue;
                chronicles[c] = obj = made;
            }
            chronicle = obj;
            try
            {
                var script = runtime.NewScript(name, guid, args);
                ChronicleName = c;
                return script;
            }
            catch (ScriptException e) { why = e.JavaType; }
            if (why != "java.lang.ClassCastException") break;         // a real failure, not the wrong chronicle
        }
        Engine.Log($"could not create the script {name}: {why ?? "no chronicle fits"}");
        return null;
    }

    /// <summary>
    /// Runs the scene's clock forward: whatever is moving moves, anything that has arrived tells
    /// its script, and then the timers that have come due fire, soonest first (the newest of a tie first).
    /// </summary>
    /// <summary>
    /// Somebody used a thing: the message, which calls `clicked` **twice over**.
    ///
    /// First the thing's own script, with a capture id of **0**. Then, in a second loop, every
    /// script that has **captured** the thing, each with **its own capture id** as
    /// the third argument. That second loop is not a detail: a great many of the game's props have
    /// no script at all and are worked entirely through a capture. Both of the first monastery's
    /// `small_lever1`s are like that — no script, two keyframes — and the script that answers for
    /// them is the `DoorSwitchPivot` sitting on the door across the room, which captured the lever
    /// in its own `beginscene`. Pull the lever, the door rises. Without the capture loop, clicking
    /// a lever in this game does nothing at all, and Vitomir only ran the first loop.
    ///
    /// A thing may carry more than one of its own scripts too, and every one of them hears it.
    /// </summary>
    public bool Clicked(WorldThing thing, WorldThing clicker) => Fire("clicked", thing, clicker);

    /// <summary>
    /// Makes the script a thing carries and puts it on the live list: its class, a script GUID, the
    /// constructor's arguments converted from the thing's CODEXPARAMS. A thing made at run time has
    /// no parameters, and the constructor gets the converted defaults — which is the case
    /// `FeederRatClassWander` warns about in its own comments ("values were garbage or zero").
    /// </summary>
    Live? Attach(WorldThing thing, int sceneBase, int sceneCount)
    {
        var name = thing.Script;
        int guid = nextScriptGuid++;
        scriptThing[guid] = thing.Guid;
        var args = Arguments(name, thing.Scene, guid, sceneBase, sceneCount);
        if (args is null) return null;                               // no such class, or no constructor
        if (Create(name, guid, args) is not { } script) return null;
        var l = new Live(name, script, guid, thing.Guid);
        live.Add(l);
        return l;
    }

    void SayCreated(Live l, WorldThing thing)
    {
        try { runtime.TryInvoke(l.Script, "created", "(I)V", [CodexHost.Box(thing.Guid)], out _); }
        catch (Exception e) { Engine.Log($"{l.Name}.created failed: {ScriptException.From(l.Name, e).JavaType}"); }
    }

    /// <summary>
    /// A thing with a script entering the world after the scene began — a script's `SpawnThing`, a
    /// companion carried in from the last place, a gib: its script is made and told
    /// `created(thingGuid)`, as the engine's finalize tells it. It does not
    /// hear `beginscene`, which only goes out as a scene starts.
    /// </summary>
    public void Created(WorldThing thing)
    {
        if (location is null || thing.Script.Length == 0 || !ReferenceEquals(thing.Place, location)) return;
        foreach (var l in live) if (l.Thing == thing.Guid) return;
        if (Attach(thing, 0, 0) is { } made) SayCreated(made, thing);
    }

    /// <summary>
    /// `killed(thingGuid, killerGuid, captureId)`, through the same two loops as
    /// <see cref="Clicked"/> — which is what it is: the same sender with a
    /// different method slot. **Opening a container comes here**, because in this engine opening
    /// one is a death (`Rpg.Container`).
    /// </summary>
    public bool Killed(WorldThing thing, WorldThing? killer) => Fire("killed", thing, killer);

    bool Fire(string callback, WorldThing thing, WorldThing? by)
    {
        var heard = new List<string>();
        foreach (var l in live.Where(l => l.Thing == thing.Guid).ToArray())
            if (Tell(callback, l, thing, by, 0)) heard.Add(l.Name);
        // The captures, each with the id its own script chose. A script that has both — attached to
        // the thing *and* holding a capture on it — hears it twice, which is the engine's own
        // behaviour: the two loops do not know about each other.
        foreach (var hold in thing.Captures.ToArray())
            if (ScriptOf(hold.Script) is { } owner)
                if (Tell(callback, new Live(owner.Name, owner.Script, hold.Script, -1), thing, by, hold.Id))
                    heard.Add($"{owner.Name} (capture {hold.Id})");
        // **Said out loud, because the answer used to be invisible.** A lever has no script of its
        // own and the thing that answers for it is a `DoorSwitchPivot` on a door in another room,
        // so from where the player is standing a working lever and a dead one look identical.
        // A play-test pulled one three times and found it broken; it had opened the
        // door, two rooms away, every time.
        if (heard.Count > 0)
            Diag.For("Vitomir.Scripting").LogInformation("{Callback}: {Thing} -> {Heard}", callback, thing.Template, string.Join(", ", heard));
        return heard.Count > 0;
    }

    bool Tell(string callback, Live l, WorldThing thing, WorldThing? by, int captureId)
    {
        try
        {
            return callback switch
            {
                "killed" => runtime.CallKilled(l.Script, thing.Guid, by?.Guid ?? 0, captureId),
                "clicked" => runtime.CallClicked(l.Script, thing.Guid, by?.Guid ?? 0, captureId),
                _ => runtime.CallIII(l.Script, callback, thing.Guid, by?.Guid ?? 0, captureId),
            };
        }
        catch (Exception e)
        {
            Engine.Log($"{l.Name}.{callback} failed: {ScriptException.From(l.Name, e).JavaType}");
            return false;
        }
    }

    /// <summary>A whole frame of the scripts, in 1.1's order (<c>CodexScenes.Frame.cs</c>).</summary>
    public void Step(float seconds)
    {
        StepThings(seconds);
        StepTimers(seconds);
        EndFrame();
    }

    /// <summary>The walks of no length that count as arrivals (`Repairs.ClosedRound`), due next step.</summary>
    readonly List<WorldThing> pendingArrivals = [];

    /// <summary>
    /// Tells each script its character "arrived" from a walk of no length, a step after the order —
    /// through the place, so it reaches the same two loops a real arrival does.
    /// </summary>
    void ArrivePending()
    {
        if (pendingArrivals.Count == 0 || location is null) return;
        var due = pendingArrivals.ToArray();
        pendingArrivals.Clear();
        foreach (var t in due)
            if (ReferenceEquals(t.Place, location) && !t.Removed) location.Arrived?.Invoke(t, 0);
    }

    /// <summary>
    /// `arrived(thingGuid, frameNum, captureId)`, through the same two loops
    /// as <see cref="Killed"/>: the thing's own scripts with a capture id of 0, then every script
    /// holding a capture on it, with the id that script chose.
    ///
    /// **Both halves were missing at first.** Only a track mover's own script heard it, so
    /// a walk never told anybody it had arrived — and a scene script that captured a skiff or a
    /// character to hear exactly that never did. Every `WalkCycle` in the game walked its first
    /// leg and stood there; so did the patrolling lawmen, which is half of why a guard that lost
    /// the player never went back to its round.
    /// </summary>
    public void Arrived(WorldThing thing, int frame)
    {
        foreach (var l in live.Where(l => l.Thing == thing.Guid).ToArray())
        {
            try { runtime.CallArrived(l.Script, thing.Guid, frame, 0); }
            catch (Exception e) { Engine.Log($"{l.Name}.arrived failed: {ScriptException.From(l.Name, e).JavaType}"); }
        }
        foreach (var hold in thing.Captures.ToArray())
        {
            if (ScriptOf(hold.Script) is not { } owner) continue;
            try { runtime.CallArrived(owner.Script, thing.Guid, frame, hold.Id); }
            catch (Exception e) { Engine.Log($"{owner.Name}.arrived failed: {ScriptException.From(owner.Name, e).JavaType}"); }
        }
    }

    /// <summary>Every thing that is moving right now, which the viewer reports.</summary>
    public int Moving => location?.Things.Count(t => t.Motion is not null) ?? 0;

    /// <summary>Runs each script's <c>endscene</c> and forgets the scene.</summary>
    public void Leave(int exitNumber = 0) => Leave(exitNumber, keep: false);

    /// <summary>
    /// The scene ends: every script hears <c>endscene</c>, the chronicle's too. With
    /// <paramref name="keep"/> the place's scripts are put aside for when the party comes back.
    /// </summary>
    public void Leave(int exitNumber, bool keep)
    {
        DeleteDoomed();                                                             // the place's destroyed things deleted first (ours; CodexScenes.Destroyed.cs)
        foreach (var l in InListOrder())                                           // the world's list, newest first
        {
            try { runtime.CallEndscene(l.Script, 0, 0, exitNumber); }                    // 0 as the client, as 1.1 passes it
            catch (Exception e) { Engine.Log($"{l.Name}.endscene failed: {ScriptException.From(l.Name, e).JavaType}"); }
        }
        EndCarried(exitNumber);
        foreach (var (name, script) in location is null ? [] : ChronicleScripts())
        {
            try { runtime.CallEndscene(script, 0, 0, exitNumber); }
            catch (Exception e) { Engine.Log($"{name}.endscene failed: {ScriptException.From(name, e).JavaType}"); }
        }
        // The timers of what the party carries go with the party (see `CarryTimers`).
        CarryTimers();
        ForgetCarriedBodies();                                                      // CodexScenes.Carried.cs
        if (keep && location is not null)
        {
            kept[location] = new Kept([.. live], [.. timers], new(scriptThing), thingGuids, worldCount, chapterCount, now);
            LeftBehind(location);                                                   // CodexScenes.Away.cs
        }
        live.Clear();
        timers.Clear();
        scriptThing.Clear();
        location = null;
    }
}
