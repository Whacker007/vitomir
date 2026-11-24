// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The script natives' network arms on a host — what a script shows to one player. In 1.1 each native
// asks whether the game is a host itself and walks the slot table for the GUID it was given: `ExecuteText`,
// the three prints, the quest's load and flags and `Advance`. Here the single-player natives are left as they are
// and each is wrapped, on a host, by the part that sends it to the player (`Net.HostTexts`); a
// player's arrival fires `joined` and `beginscene` for them, and the
// close of a player's read pane is their `textended`.
using Vitomir.Engine.Net;
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    GameServer? netServer;

    /// <summary>
    /// Makes these scripts a host's (once per session): the natives above answer as 1.1's host does,
    /// and the session's arrivals and closed read panes reach the scripts.
    /// </summary>
    public void Host(GameServer server)
    {
        if (ReferenceEquals(netServer, server)) return;
        netServer = server;
        HostShops(server);
        HostSceneIndex(server);
        Wrap("Codex.ExecuteText", (a, local) =>
            server.Texts.Text(CodexHost.Int(a, 0), CodexHost.Int(a, 1), CodexHost.Int(a, 2), Name31(a, 3)) == TextRoute.Here
                ? local() : CodexHost.Box(true));
        Wrap("CodexConsole.Print", (a, local) =>
        {
            int flags = CodexHost.Int(a, 1);
            return flags == HostTexts.ServerConsole
                   || server.Texts.Print(CodexHost.Int(a, 0), flags, CodexHost.Str(a, 2) ?? "", ConsoleColour(CodexHost.Int(a, 3)))
                ? local() : null;
        });
        Wrap("CodexConsole.PrintNLS", (a, local) =>
            CodexHost.Int(a, 1) == HostTexts.ServerConsole
            || server.Texts.PrintKey(CodexHost.Int(a, 0), CodexHost.Str(a, 2) ?? "", ConsoleColour(CodexHost.Int(a, 3)))
                ? local() : null);
        Wrap("CodexConsole.PrintFormatNLS", (a, local) =>
        {
            int flags = CodexHost.Int(a, 1);
            return flags == HostTexts.ServerConsole
                   || server.Texts.PrintFormat(CodexHost.Int(a, 0), flags, CodexHost.Str(a, 2) ?? "", CodexHost.Str(a, 3) ?? "",
                                               ConsoleColour(CodexHost.Int(a, 4)))
                ? local() : null;
        });
        // A new quest goes to every player (the name as the script gave it); a
        // change of its flags too, only when the word changed.
        Wrap("CodexQuest.Load", (a, local) =>
        {
            string name = CodexHost.Str(a, 0) ?? "";
            bool had = Quests?.Find(name) is not null;
            var answer = local();
            if (!had && Quests?.Find(name) is not null) server.Texts.QuestLoaded(name);
            return answer;
        });
        foreach (var key in new[] { "CodexQuest.SetFlags", "CodexQuest.ClearFlags" })
            Wrap(key, (a, local, self) =>
            {
                var quest = QuestOf(self);
                uint was = quest?.Flags ?? 0;
                var answer = local();
                if (quest is not null && quest.Flags != was) server.Texts.QuestFlags(quest.Name, quest.Flags);
                return answer;
            });
        // `Advance(player)`: a player's own machine opens the screen; the host's own
        // character, or nobody's, goes the single-player way; `Advance(0)` is the host's
        // walk through slot 0's character and every player's, never single player's.
        Wrap("CodexSequence.Advance", (a, local) =>
        {
            int guid = CodexHost.Int(a, 0);
            if (guid == 0) { server.Texts.Walk(); return null; }
            return server.Texts.Advance(guid) ? null : local();
        });

        HostMoves(server);                                                          // one actor moved alone (CodexScenes.Places.cs)
        server.Texts.TextEnded += NetTextEnded;
        server.Entered += (p, first) => { if (p.Avatar is { } body) Arrived(body.Guid, first); };
        server.Departing += p => Disconnected(p.Avatar is { InWorld: true } body ? body.Guid : 0);
    }

    /// <summary>
    /// A player leaving: every script of the place, and the chronicle's, hears
    /// `disconnected(guid)` — with no character in the world, only the chronicle's, with 0.
    /// </summary>
    public void Disconnected(int playerGuid)
    {
        object?[] one = [CodexHost.Box(playerGuid)];
        if (playerGuid != 0)
        {
            foreach (var l in InListOrder()) Say(l.Name, l.Script, "disconnected", "(I)V", one);   // the world's list, newest first
            foreach (var (name, script) in CarriedScripts()) Say(name, script, "disconnected", "(I)V", one);
        }
        foreach (var (name, script) in ChronicleScripts()) Say(name, script, "disconnected", "(I)V", one);
    }

    void Wrap(string key, Func<object?[], Func<object?>, object?> network) =>
        Wrap(key, (a, local, _) => network(a, local));

    void Wrap(string key, Func<object?[], Func<object?>, object?, object?> network)
    {
        var single = Engine.Handler(key);
        Engine.On(key, (self, args) => network(args, () => single?.Invoke(self, args), self)!);
    }

    /// <summary>
    /// A player's arrival, told to the scripts (at the end of the arrival): when it is their first since joining,
    /// every script hears `joined(guid)` — the things' and then the chronicle's — and then
    /// every one hears `beginscene(guid, 0)` again with the arriving player's GUID, as a
    /// place's scripts do when it begins. `CTD_BarKeep` and `LOT_LOBY_1_1` show each newcomer the intro
    /// text from `joined`; `LOT_FAC4_6_1` finds the initiator's place from `beginscene`'s GUID.
    /// </summary>
    public void Arrived(int playerGuid, bool joined)
    {
        object?[] one = [CodexHost.Box(playerGuid)];
        if (joined)
        {
            foreach (var l in InListOrder()) Say(l.Name, l.Script, "joined", "(I)V", one);          // the world's list, newest first
            foreach (var (name, script) in CarriedScripts()) Say(name, script, "joined", "(I)V", one);
            foreach (var (name, script) in ChronicleScripts()) Say(name, script, "joined", "(I)V", one);
        }
        foreach (var l in InListOrder()) Begin(l.Name, l.Script);                               // the world's list, newest first
        foreach (var (name, script) in CarriedScripts()) Begin(name, script);
        foreach (var (name, script) in ChronicleScripts()) Begin(name, script);

        void Begin(string name, object script)
        {
            try { runtime.Call(script, "beginscene", playerGuid, 0); }
            catch (Exception e) { Engine.Log($"{name}.beginscene failed: {ScriptException.From(name, e).JavaType}"); }
        }
    }

    /// <summary>The worn items' own scripts that are not on the live list — the ones <c>BeginCarried</c> tells.</summary>
    IEnumerable<(string Name, object Script)> CarriedScripts()
    {
        foreach (var item in Worn().ToList())
        {
            if (item.Thing is not { } thing || OwnScripts(thing).Length > 0) continue;
            foreach (var own in Own(thing)) yield return own;
        }
    }

    /// <summary>
    /// A player's read pane has closed (message 0x27): as <see cref="TextClosed"/>
    /// does for the host's own pane, the script that opened it hears `textended(reader)`, and then every
    /// script holding a capture on the reader but that one.
    /// </summary>
    void NetTextEnded(int script, int reader)
    {
        if (location?.Thing(reader) is not { Type: ThingType.Actor or ThingType.Player } who) return;
        if (ScriptOf(script) is not { } opener) return;
        object?[] args = [CodexHost.Box(who.Guid)];
        Say(opener.Name, opener.Script, "textended", "(I)V", args);
        foreach (var hold in who.Captures.ToArray())
            if (ScriptOf(hold.Script) is { } s && !ReferenceEquals(s.Script, opener.Script))
                Say(s.Name, s.Script, "textended", "(I)V", args);
    }
}
