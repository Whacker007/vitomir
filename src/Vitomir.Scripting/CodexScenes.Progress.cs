// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Progression: the quest natives (CodexQuest, table 10) and the half of CodexSequence (table 13)
// that moves the story on — Jump, ChangeScene, the advance, the change of chronicle, the end.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>`Vault(clientGuid)`: the haven vault's pane, for that character.</summary>
    public Action<int>? Vault { get; set; }

    QuestLog? Quests => location?.Session?.Quests;

    Quest? QuestOf(object? self) => self is null ? null : Quests?[runtime.GuidOf(self)];

    /// <summary>An item of the calling quest, or null for a bad quest or an index outside it.</summary>
    QuestItem? ItemOf(object? self, object[] a) =>
        QuestOf(self) is { } q && CodexHost.Int(a, 0) is var i && i >= 0 && i < q.Items.Length ? q.Items[i] : null;

    /// <summary>What the quest natives answer for a quest that is not there.</summary>
    public const string BadQuest = "***ERR:Bad Quest***", BadQuestItem = "***ERR:Bad QItem***";

    /// <summary>What the Restore natives answer outside a restore.</summary>
    public const int NoStreamInt = unchecked((int)0xc521974f);
    /// <inheritdoc cref="NoStreamInt"/>
    public const float NoStreamFloat = -98765.4296875f;
    /// <inheritdoc cref="NoStreamInt"/>
    public const string NoStreamString = "***ERR:Stream not opened***";

    void RegisterProgress()
    {
        // ---- CodexQuest -------------------------------------------------------------------------
        // Load(name) is static and answers the slot, which is the quest's GUID; -1 for none.
        Engine.On("CodexQuest.Load", (_, a) =>
            CodexHost.Box(Quests?.Load(CodexHost.Str(a, 0) ?? "")?.Slot ?? QuestLog.Invalid));
        Engine.On("CodexQuest.Destroy", (self, _) =>
            CodexHost.Box(QuestOf(self) is { } q && Quests!.Destroy(q.Slot)));
        Engine.On("CodexQuest.GetDescription", (self, a) =>
            QuestOf(self) is { } q ? (CodexHost.Bool(a, 0) ? q.Long : q.Short) : BadQuest);
        Engine.On("CodexQuest.GetFlags", (self, _) => CodexHost.Box(QuestOf(self) is { } q ? (int)q.Flags : -1));
        Engine.On("CodexQuest.SetFlags", (self, a) =>
            CodexHost.Box(QuestOf(self) is { } q ? (int)Quests!.SetFlags(q, (uint)CodexHost.Int(a, 0)) : -1));
        Engine.On("CodexQuest.ClearFlags", (self, a) =>
            CodexHost.Box(QuestOf(self) is { } q ? (int)Quests!.ClearFlags(q, (uint)CodexHost.Int(a, 0)) : -1));
        Engine.On("CodexQuest.GetNumItems", (self, _) => CodexHost.Box(QuestOf(self)?.Items.Length ?? -1));
        Engine.On("CodexQuest.GetItemDescription", (self, a) =>
            QuestOf(self) is null ? BadQuest
            : ItemOf(self, a) is { } item ? (CodexHost.Bool(a, 1) ? item.Long : item.Short) : BadQuestItem);
        Engine.On("CodexQuest.GetItemFlags", (self, a) => CodexHost.Box(ItemOf(self, a) is { } i ? (int)i.Flags : -1));
        Engine.On("CodexQuest.SetItemFlags", (self, a) =>
            CodexHost.Box(ItemOf(self, a) is { } i ? (int)Quests!.SetItemFlags(i, (uint)CodexHost.Int(a, 1)) : -1));
        Engine.On("CodexQuest.ClearItemFlags", (self, a) =>
            CodexHost.Box(ItemOf(self, a) is { } i ? (int)Quests!.ClearItemFlags(i, (uint)CodexHost.Int(a, 1)) : -1));
        Engine.On("CodexQuest.GetItemNumSteps", (self, a) => CodexHost.Box(ItemOf(self, a)?.Steps ?? -1));
        Engine.On("CodexQuest.SetItemNumSteps", (self, a) =>
            CodexHost.Box(ItemOf(self, a) is { } i ? Quests!.SetItemSteps(i, CodexHost.Int(a, 1)) : -1));
        Engine.On("CodexQuest.GetItemCurStep", (self, a) => CodexHost.Box(ItemOf(self, a)?.Step ?? -1));
        Engine.On("CodexQuest.SetItemCurStep", (self, a) =>
            CodexHost.Box(ItemOf(self, a) is { } i ? Quests!.SetItemStep(i, CodexHost.Int(a, 1)) : -1));

        // ---- CodexSequence: moving the story on ------------------------------------------------
        Engine.On("CodexSequence.Jump", (_, a) => { Chronicle?.Jump(CodexHost.Str(a, 0) ?? "", CodexHost.Int(a, 1)); });
        Engine.On("CodexSequence.ChangeScene", (_, a) =>
        {
            Chronicle?.ChangeScene(CodexHost.Str(a, 0) ?? "", CodexHost.Str(a, 1) ?? "");
        });
        Engine.On("CodexSequence.SetupAdvance", (_, a) =>
        {
            Chronicle?.SetupAdvance(CodexHost.Str(a, 0) ?? "", CodexHost.Str(a, 1) ?? "", CodexHost.Int(a, 2));
        });
        Engine.On("CodexSequence.Advance", (_, a) => { Chronicle?.Advance(CodexHost.Int(a, 0)); });
        // ChangeChronicle(name): only when the chronicle file is there (a resource of kind 0xa).
        Engine.On("CodexSequence.ChangeChronicle", (_, a) =>
        {
            string name = CodexHost.Str(a, 0) ?? "";
            if (name.Length > 31) name = name[..31];
            if (Chronicles?[StripNsc(name)] is not null) Chronicle?.ChangeChronicle(name);
        });
        Engine.On("CodexSequence.EndGame", (_, _) => { Chronicle?.EndGame(); });
        // SaveGame asks the main loop for a save (request 2, outside a network game).
        Engine.On("CodexSequence.SaveGame", (_, _) => { SaveRequested?.Invoke(); });
        // PlayerJump and the NSS index are network natives: in a single-player game (and on a player's
        // machine) the first does nothing and the index answers -1 and is not moved; a host's are CodexScenes.SceneIndex.cs.
        Engine.On("CodexSequence.PlayerJump", (_, _) => { });
        Engine.On("CodexSequence.GetNSSIndex", (_, _) => CodexHost.Box(-1));
        Engine.On("CodexSequence.SetNSSIndex", (_, _) => { });
        Engine.On("CodexSequence.Vault", (_, a) => { Vault?.Invoke(CodexHost.Int(a, 0)); });

        // Save* and Restore* are CodexScenes.Saving.cs, with the stream they write to and read from.
    }

    /// <summary>
    /// `SaveGame()` from a script — the haven's coffin (`HavenSave`) — which opens the **save screen**
    /// (request 2, outside a network game); it is not an autosave.
    /// </summary>
    public Action? SaveRequested { get; set; }

    static string StripNsc(string name) =>
        name.EndsWith(".nsc", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
