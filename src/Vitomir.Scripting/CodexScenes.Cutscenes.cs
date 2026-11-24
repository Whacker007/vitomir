// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The natives a cutscene is made of: the camera (CodexCamera, table 1), the conversation and the AI
// switch (Codex, table 2), the party's placing and its experience (CodexPlayer, table 7), and the
// region flags (CodexRegion, table 11).
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>
    /// The game's camera, which the <c>CodexCamera</c> natives drive. Null leaves every one of them
    /// doing nothing, which is what a test with no camera wants.
    /// </summary>
    public WorldCamera? Camera { get; set; }

    /// <summary>The game's one running conversation, which <c>ExecuteConversation</c> starts.</summary>
    public Conversations? Talk { get; set; }

    /// <summary>
    /// Somebody has gone up a rank. The engine prints <c>RPG_NEWRANKGAINED</c> to the message log
    /// and plays a sound; the harness decides what to do with it.
    /// </summary>
    public Action<WorldThing, int>? RankGained { get; set; }

    /// <summary>
    /// A script has handed somebody experience. The engine says nothing about it on screen — only a
    /// new rank gets a line — so this is for the harness's console and the journal.
    /// </summary>
    public Action<WorldThing, int>? ExperienceGiven { get; set; }

    /// <summary>Camera paths already read, by file name.</summary>
    readonly Dictionary<string, NcpCameraPath?> paths = new(StringComparer.OrdinalIgnoreCase);

    int NowMs => location?.Milliseconds ?? 0;

    /// <summary>
    /// The character the player steers, which a released camera goes back to — 1.1 reads the
    /// steered character for the yaw and for the subject, not the party's leader. Until
    /// Once this was the leader, so a scene ended with a companion steered left the camera on Christof
    /// while the keys drove the companion (seen in play: the reliquary at the prince's).
    /// </summary>
    WorldThing? Steered => location?.Steered;

    void RegisterCutscenes()
    {
        // ------------------------------------------------------------------- Codex (table 2)

        // A THING_TYPE_PLAYER, and nothing else (the thing's type is 7). Every party
        // member is one, so a region guarded by this fires for any of them.
        Engine.On("Codex.IsPlayerGuid", (_, a) =>
            CodexHost.Box(location?.Thing(CodexHost.Int(a, 0))?.Type == ThingType.Player));

        // The AI's main flags word: get, or-in and and-out, each of
        // the last two answering the word as it was. `AIOff`/`AIOn` are these with PAUSED.
        Engine.On("Codex.AIGetMainFlags", (_, _) => CodexHost.Box((int)((location?.Minds ?? wordHolder)?.MainFlags ?? 0)));
        Engine.On("Codex.AISetMainFlags", (_, a) =>
        {
            if ((location?.Minds ?? wordHolder) is not { } minds) return CodexHost.Box(0);
            uint was = minds.MainFlags;
            minds.MainFlags = was | (uint)CodexHost.Int(a, 0);
            return CodexHost.Box((int)was);
        });
        Engine.On("Codex.AIClearMainFlags", (_, a) =>
        {
            if ((location?.Minds ?? wordHolder) is not { } minds) return CodexHost.Box(0);
            uint was = minds.MainFlags;
            minds.MainFlags = was & ~(uint)CodexHost.Int(a, 0);
            return CodexHost.Box((int)was);
        });

        Engine.On("Codex.ExecuteConversation", (self, a) => CodexHost.Box(Converse(self, a)));
        // Six instructions and no conversation begun: it answers whether the script is real.
        Engine.On("Codex.BeginConversation", (self, _) => CodexHost.Box(self is not null));
        // The teardown on whatever is running, with no convended.
        Engine.On("Codex.EndConversation", (_, _) =>
            CodexHost.Box(location is not null && Talk?.End(location) == true));

        // "Unused AFAIK", says Codex.java, and nothing ships a call — but they work: into cutscene
        // mode and back out.
        Engine.On("Codex.BeginCutscene", (_, _) => { Camera?.EnterCutscene(); return null; });
        Engine.On("Codex.EndCutscene", (_, _) => { Camera?.Release(Steered); return null; });

        // `PlayVideo(script, name, flag)`: 1.1 does not play the film here either — it
        // **queues it**, by copying the name into the main sequence and setting its request word to
        // 14 (the same word the menus use), and the film runs at the end of the frame.
        // The answer is only whether the request was taken.
        //
        // So the answer is true whenever the script resolves — even for a
        // film that will not open, which the script learns of by hearing `videoended` at once. The
        // name is the **second** argument, 127 characters of it (a 0x80 copy); the third is the id
        // `videoended` hands back. The request is run at the end of the step (`RunFilm`, in the
        // callbacks), through <see cref="PlayFilm"/> when a host has one. Earlier, this answered
        // false with no host and read the name from the id's slot.
        Engine.On("Codex.PlayVideo", (_, a) =>
        {
            int script = CodexHost.Int(a, 0);
            if (ScriptOf(script) is null) return CodexHost.Box(false);
            string name = CodexHost.Str(a, 1) ?? "";
            film = (name.Length > 127 ? name[..127] : name, script, CodexHost.Int(a, 2));
            return CodexHost.Box(true);
        });

        // -------------------------------------------------------------- CodexCamera (table 1)

        Engine.On("CodexCamera.SetupCutscene", (_, a) =>
        {
            // The first target must be a thing; the second may be nothing.
            if (location?.Thing(CodexHost.Int(a, 1)) is not { } first) return null;
            // A network player's camera is on their machine (Engine.Net.SceneRoutes).
            if (SceneRoutes.For(location)?.SetupCutscene(CodexHost.Int(a, 0), first, location.Thing(CodexHost.Int(a, 2))) == true) return null;
            Camera?.SetupCutscene(first, location.Thing(CodexHost.Int(a, 2)));
            return null;
        });
        Engine.On("CodexCamera.SetShot", (_, a) =>
        {
            // The native refuses a shot, angle or distance out of range before the camera sees it,
            // and turns the lerp's seconds into whole milliseconds by truncation.
            int shot = CodexHost.Int(a, 1), angle = CodexHost.Int(a, 2), dist = CodexHost.Int(a, 3);
            if (shot is < 0 or >= 10 || angle is < 0 or >= 5 || dist is < 0 or >= 5) return null;
            int ms = (int)(CodexHost.Float(a, 5) * 1000f);
            if (SceneRoutes.For(location)?.SetShot(CodexHost.Int(a, 0), shot, angle, dist, CodexHost.Int(a, 4), ms) == true) return null;
            Camera?.SetShot(shot, angle, dist, CodexHost.Int(a, 4), ms);
            return null;
        });
        Engine.On("CodexCamera.Release", (_, a) =>
        {
            if (SceneRoutes.For(location)?.Release(CodexHost.Int(a, 0)) != true) Camera?.Release(Steered);
            return null;
        });
        Engine.On("CodexCamera.PlayPath", (_, a) =>
        {
            int client = CodexHost.Int(a, 0);
            if (SceneRoutes.For(location) is { } route && PathFor(route, client, a) is { } sent) return CodexHost.Box(sent);
            if (Camera is not { } camera) return CodexHost.Box(false);
            var script = live.FirstOrDefault(l => l.Guid == CodexHost.Int(a, 1))?.Script;
            // The name is copied into 32 bytes and cut at 31, and the file is looked for
            // under `Paths\`.
            var name = CodexHost.Str(a, 2) ?? "";
            if (name.Length > 31) name = name[..31];
            if (Path(name) is not { } ncp) return CodexHost.Box(false);
            Action? ended = script is null ? null : () =>
            {
                try { runtime.CallPathEnded(script, client); }
                catch (Exception e) { Engine.Log($"pathended failed: {ScriptException.From("pathended", e).JavaType}"); }
            };
            return CodexHost.Box(camera.PlayPath(ncp, CodexHost.Float(a, 3), ended));
        });
        Engine.On("CodexCamera.AddFade", (_, a) => CodexHost.Box(Camera?.Effects.AddFade(
            CodexHost.Float(a, 1), CodexHost.Float(a, 2), (int)(CodexHost.Float(a, 3) * 1000f),
            CodexHost.Bool(a, 4), NowMs) ?? 0));
        Engine.On("CodexCamera.AddFlash", (_, a) => CodexHost.Box(Camera?.Effects.AddFlash(
            CodexHost.Float(a, 1), CodexHost.Float(a, 2), (int)(CodexHost.Float(a, 3) * 1000f),
            (uint)CodexHost.Int(a, 4), CodexHost.Bool(a, 5), NowMs) ?? 0));
        Engine.On("CodexCamera.AddFilter", (_, a) => CodexHost.Box(Camera?.Effects.AddFilter(
            (int)(CodexHost.Float(a, 1) * 1000f), (uint)CodexHost.Int(a, 2), NowMs) ?? 0));
        Engine.On("CodexCamera.ClearAllEffects", (_, _) => { Camera?.Effects.ClearAll(); return null; });
        // The three "old, obsolete verbs". SetupOneShot is SetupCutscene on one target;
        // ShotType and ShotAngle are a bare `ret` each.
        Engine.On("CodexCamera.SetupOneShot", (_, a) =>
        {
            if (location?.Thing(CodexHost.Int(a, 0)) is { } only) Camera?.SetupCutscene(only, null);
            return null;
        });
        Engine.On("CodexCamera.ShotType", (_, _) => null);
        Engine.On("CodexCamera.ShotAngle", (_, _) => null);

        // --------------------------------------------------------------- CodexPlayer (table 7)

        Engine.On("CodexPlayer.TeleportPartyToConversation", (_, a) =>
        {
            // The named thing must be a character (type 0 or 7); which one it is does not
            // matter to the single-player branch, which moves the whole party.
            if (location?.Thing(CodexHost.Int(a, 0)) is not { Type: ThingType.Actor or ThingType.Player })
                return CodexHost.Box(false);
            return CodexHost.Box(location.TeleportPartyToConversation((uint)CodexHost.Int(a, 1)));
        });
        Engine.On("CodexPlayer.AwardPartyExperience", (_, a) =>
        {
            if (location is null) return null;
            int gift = Math.Abs(CodexHost.Int(a, 0));
            foreach (var who in location.AwardPartyExperience(gift, RankGained)) ExperienceGiven?.Invoke(who, gift);
            return null;
        });
        Engine.On("CodexPlayer.AwardPlayerExperience", (_, a) =>
        {
            // A THING_TYPE_PLAYER only, and the amount made positive.
            if (location?.Thing(CodexHost.Int(a, 0)) is not { Type: ThingType.Player } who) return CodexHost.Box(false);
            int amount = Math.Abs(CodexHost.Int(a, 1));
            Experience.Award(who, amount, RankGained);
            ExperienceGiven?.Invoke(who, amount);
            return CodexHost.Box(true);
        });

        // --------------------------------------------------------------- CodexRegion (table 11)

        // A region's flags, and 0 for anything that is not one.
        Engine.On("CodexRegion.GetRegionFlags", (self, _) =>
            CodexHost.Box(Of(self) is { Type: ThingType.Region } r ? (int)r.RegionFlags : 0));
        Engine.On("CodexRegion.SetRegionFlags", (self, a) =>
        {
            if (Of(self) is not { Type: ThingType.Region } r) return CodexHost.Box(0);
            uint was = r.RegionFlags;
            r.RegionFlags = was | (uint)CodexHost.Int(a, 0);
            return CodexHost.Box((int)was);
        });
        Engine.On("CodexRegion.ClearRegionFlags", (self, a) =>
        {
            if (Of(self) is not { Type: ThingType.Region } r) return CodexHost.Box(0);
            uint was = r.RegionFlags;
            r.RegionFlags = was & ~(uint)CodexHost.Int(a, 0);
            return CodexHost.Box((int)was);
        });
    }

    /// <summary>
    /// <c>ExecuteConversation(scriptGuid, conversationName, conversationFile, flags, starterGuid,
    /// npcGuid)</c>. A starter of 0 is refused; the id every callback hands back is the starter. The callbacks go to the calling
    /// script.
    /// </summary>
    bool Converse(object? self, object?[] a)
    {
        if (location is null || Talk is null || self is null) return false;
        var conversation = CodexHost.Str(a, 1) ?? "";
        var file = CodexHost.Str(a, 2) ?? "";
        var flags = (ConversationFlag)CodexHost.Int(a, 3);
        int starter = CodexHost.Int(a, 4);
        if (starter == 0) return false;
        string who = live.FirstOrDefault(l => ReferenceEquals(l.Script, self))?.Name ?? "a script";
        bool started = Talk.Execute(location, file, conversation, flags, starter, NowMs, npc: CodexHost.Int(a, 5), wire: made =>
        {
            made.Reached += (id, ev, step, ms, speaker) =>
                Callback(who, $"convreached (event {ev}, line {step})", () => runtime.CallIIIII(self, "convreached", id, ev, step, ms, speaker));
            made.Chosen += (id, ev, answer, ms, speaker) =>
                Callback(who, $"convchosen (event {ev}, answer {answer})", () => runtime.CallIIIII(self, "convchosen", id, ev, answer, ms, speaker));
            made.Ended += (id, aborted, code) =>
                Callback(who, "convended", () => runtime.CallConvEnded(self, id, aborted, code));
        });
        if (started)
            Diag.For("Vitomir.Scripting").LogInformation("{Who} started the conversation {Name} from {File}",
                                                          who, conversation, file);
        else Engine.Log($"{who} asked for the conversation {conversation} from {file}, which is not there");
        return started;
    }

    void Callback(string who, string what, Func<bool> call)
    {
        try { call(); }
        catch (Exception e)
        {
            // Which callback, and where in the script: the innermost frame that is the script's own.
            var inner = e; while (inner.InnerException is { } deeper) inner = deeper;
            string at = (inner.StackTrace ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("at ")) ?? "";
            Engine.Log($"{who}'s conversation callback {what} failed: {ScriptException.From(who, e).JavaType} {at}");
        }
    }

    /// <summary>
    /// `PlayPath` for a network player (the native's server branch): sent to their machine, with the calling
    /// script's `pathended` kept for when it ends there; null when the client is not a network player's.
    /// </summary>
    bool? PathFor(ISceneRoute route, int client, object?[] a)
    {
        var script = live.FirstOrDefault(l => l.Guid == CodexHost.Int(a, 1))?.Script;
        var name = CodexHost.Str(a, 2) ?? "";
        if (name.Length > 31) name = name[..31];
        Action? ended = script is null ? null : () =>
        {
            try { runtime.CallPathEnded(script, client); }
            catch (Exception e) { Engine.Log($"pathended failed: {ScriptException.From("pathended", e).JavaType}"); }
        };
        // The server arm answers 1 once it has sent it, whether or not the file is there.
        return route.PlayPath(client, name, CodexHost.Float(a, 3), ended) ? true : null;
    }

    /// <summary>A camera path from `paths/`, read once (1.1 reads the file on every call; the answer is the same).</summary>
    NcpCameraPath? Path(string name)
    {
        if (paths.TryGetValue(name, out var cached)) return cached;
        NcpCameraPath? read = null;
        if (location?.Files is { } files && files.TryRead("paths/" + name, out var data))
        {
            try { read = NcpCameraPath.Read(data, name); }
            catch (NodFormatException) { read = null; }
        }
        if (read is null) Engine.Log($"camera path {name} not found");
        paths[name] = read;
        return read;
    }

    /// <summary>
    /// Starts one of the game's films by name and answers whether it is showing — what a script's
    /// `PlayVideo` comes to at the end of the step (the main loop's request 14). True: the host
    /// holds the game while it shows and calls <see cref="FilmEnded"/> when it is over, and the
    /// script hears `videoended` then. False: the script hears it at once, as 1.1's does when its film
    /// would not open. Null — which is what it is until a host sets it — plays nothing, as 1.1 does
    /// with its films turned off.
    /// </summary>
    public Func<string, bool>? PlayFilm { get; set; }

}
