// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The quest log: up to 64 quests, loaded by name from `quests/*.nqd` when a script
// asks for one, flagged complete by the script when it is done, and shown in the quest pane.
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>A part of a quest (0x80 bytes in the engine).</summary>
public sealed class QuestItem
{
    /// <summary>`QUESTITEM_FLAG_*` (<see cref="QuestLog.ItemComplete"/>, <see cref="QuestLog.ItemHidden"/>), and 4 once its experience has been given.</summary>
    public uint Flags { get; internal set; }
    /// <summary>How many steps it has (<c>+4</c>).</summary>
    public int Steps { get; internal set; }
    /// <summary>Which step it is on (<c>+8</c>), always below <see cref="Steps"/> when set by the engine.</summary>
    public int Step { get; internal set; }
    public string ShortKey { get; internal set; } = "";
    public string LongKey { get; internal set; } = "";
    /// <summary>The words, looked up once when the item was read.</summary>
    public string Short { get; internal set; } = "";
    /// <inheritdoc cref="Short"/>
    public string Long { get; internal set; } = "";
    /// <summary>The icon its line names — kept, and never drawn (see <see cref="NqdItem.Icon"/>).</summary>
    public string Icon { get; internal set; } = "";
    /// <summary>Experience for completing it.</summary>
    public uint Xp { get; internal set; }
    /// <summary>The quest it belongs to.</summary>
    public required Quest Parent { get; init; }
    public bool Complete => (Flags & QuestLog.ItemComplete) != 0;
    public bool Hidden => (Flags & QuestLog.ItemHidden) != 0;
}

/// <summary>One quest in the log (0xa4 bytes).</summary>
public sealed class Quest
{
    /// <summary>Its slot, 0..63, which is also its GUID as the scripts see it.</summary>
    public int Slot { get; init; }
    /// <summary>The file name it was loaded by, lower-cased — the key the log finds it under.</summary>
    public string Name { get; internal set; } = "";
    /// <summary><see cref="QuestLog.Complete"/>, and <see cref="QuestLog.Rewarded"/> once the experience has gone out.</summary>
    public uint Flags { get; internal set; }
    /// <summary>Experience for completing it, to every living member of the party.</summary>
    public uint Xp { get; internal set; }
    public string ShortKey { get; internal set; } = "";
    public string LongKey { get; internal set; } = "";
    /// <summary>Its name and its description in the player's language.</summary>
    public string Short { get; internal set; } = "";
    /// <inheritdoc cref="Short"/>
    public string Long { get; internal set; } = "";
    /// <summary>The icon the pane shows beside it once it is done — `QuestCheck.tga` for most.</summary>
    public string Icon { get; internal set; } = "";
    /// <summary>Whether that icon's texture loaded. Without it neither the quest nor its items ever show one.</summary>
    public bool IconLoaded { get; internal set; }
    /// <summary>Its parts. The array is the size the `quest` line said, whatever the `qitem` lines filled.</summary>
    public QuestItem[] Items { get; internal set; } = [];
    public bool Complete => (Flags & QuestLog.Complete) != 0;
}

/// <summary>
/// The quest log: 64 slots (<c>MAX_QUESTS</c>) and a table by name.
///
/// <para><b>Loading is adding.</b> <c>CodexQuest.Load(name)</c> answers the quest if the
/// log already has it, and otherwise reads <c>quests/&lt;name&gt;.nqd</c> into the first free slot —
/// so the scripts never "add a quest", they load one, and the first load is what puts it in the
/// log. A new one says so: the steered character says <c>vox_newquest</c> if it is free to speak,
/// and the log is announced changed.</para>
///
/// <para><b>Completing is a flag.</b> The Java <c>Complete()</c> is <c>SetFlags(1)</c>. Setting the
/// complete bit on a quest that did not have it marks it rewarded (bit 4), gives its
/// experience to every living member of the party, and has the steered character say
/// <c>vox_questcomplete</c>. An item's complete bit gives the item's experience the same way but says
/// nothing.</para>
///
/// <para><b>"Changed"</b> is the engine's own three-step notice: the
/// pane is refreshed, the quest button is asked to flash (<see cref="Changed"/>), and <c>Quest log
/// has been updated.</c> goes to the message log.</para>
/// </summary>
// A new game empties the log explicitly; here a fresh `Session` starts a `QuestLog` that
// is already empty.
public sealed class QuestLog
{
    public const int Max = 64;
    /// <summary>`QUEST_FLAG_COMPLETE`, and the bit the engine adds once the experience has been given.</summary>
    public const uint Complete = 0x1, Rewarded = 0x4;
    /// <summary>`QUESTITEM_FLAG_COMPLETE`, `QUESTITEM_FLAG_HIDDEN`.</summary>
    public const uint ItemComplete = 0x1, ItemHidden = 0x2;
    /// <summary>`INVALID_QUEST_GUID`.</summary>
    public const int Invalid = -1;

    /// <summary>What the pane's buttons and the notice are keyed to: the string the engine posts.</summary>
    public const string ChangedKey = "UI_QUESTCHANGED";

    readonly Quest?[] slots = new Quest?[Max];
    readonly Dictionary<string, Quest> byName = new(StringComparer.Ordinal);

    /// <summary>Reads <c>quests/&lt;name&gt;.nqd</c>, or null when there is no such file.</summary>
    public Func<string, byte[]?>? Read { get; set; }
    /// <summary>Whether an icon's texture can be made. Null answers yes.</summary>
    public Func<string, bool>? IconLoads { get; set; }
    /// <summary>The string table the keys are resolved through. Null keeps the keys.</summary>
    public Func<string, string>? Text { get; set; }
    /// <summary>Gives experience to every living member of the party.</summary>
    public Action<int>? AwardParty { get; set; }
    /// <summary>The steered character's line, when it is free to say one: <c>vox_newquest</c>, <c>vox_questcomplete</c>.</summary>
    public Action<int>? Speak { get; set; }
    /// <summary>The message log's <c>Quest log has been updated.</c></summary>
    public Action<string>? Post { get; set; }

    /// <summary>The pane refresh: the log's contents have changed.</summary>
    public event Action? Refreshed;
    /// <summary>The full notice: refreshed, the button asked to flash, the message posted.</summary>
    public event Action? Changed;

    /// <summary>The quest in a slot, or null.</summary>
    public Quest? this[int slot] => (uint)slot < Max ? slots[slot] : null;

    /// <summary>Every quest, in slot order — the order the pane lists them in.</summary>
    public IEnumerable<Quest> All => slots.OfType<Quest>();

    public int Count => slots.Count(q => q is not null);

    /// <summary>A quest by name, if the log has it.</summary>
    public Quest? Find(string name) => byName.GetValueOrDefault(Key(name));

    static string Key(string name) => (name.Length > 31 ? name[..31] : name).ToLowerInvariant();

    /// <summary>
    /// `CodexQuest.Load`: the quest by that name, reading it into the first free slot the
    /// first time. Null when the log is full or the file will not open — and a file that will not
    /// open costs nothing, the slot is given back.
    /// </summary>
    public Quest? Load(string name)
    {
        if (Find(name) is { } known) return known;
        int slot = Array.IndexOf(slots, null);
        if (slot < 0) return null;
        var quest = new Quest { Slot = slot };
        slots[slot] = quest;
        if (!Parse(quest, name))
        {
            slots[slot] = null;
            return null;
        }
        Speak?.Invoke(Audio.FoleyEvent.VoxNewQuest);
        return quest;
    }

    /// <summary>
    /// The reader, side effects and all: every `qitem` line runs the item setters as it is
    /// read, so each one announces the log changed three times, and one whose line says complete gives
    /// its experience as it loads. The quest goes in the name table and is announced once more at the end.
    /// </summary>
    bool Parse(Quest quest, string name)
    {
        if (Read?.Invoke(name) is not { } data) return false;
        bool found = false;
        int read = 0;
        foreach (var (word, a, b, s, l, icon, xp) in Nqd.Lines(data))
        {
            if (word == "quest")
            {
                found = true;
                quest.Name = Key(name);
                quest.Items = new QuestItem[Math.Max(0, a)];
                for (int i = 0; i < quest.Items.Length; i++) quest.Items[i] = new QuestItem { Parent = quest };
                quest.Flags = unchecked((uint)b);
                quest.ShortKey = s;
                quest.LongKey = l;
                quest.Short = Say(s);
                quest.Long = Say(l);
                quest.Xp = xp;
                quest.Icon = icon;
                quest.IconLoaded = IconLoads?.Invoke(icon) ?? true;
            }
            else if (word == "qitem" && found && read != quest.Items.Length && read < quest.Items.Length)
            {
                var item = quest.Items[read];
                int steps = a < 1 ? 1 : a;
                SetSteps(item, steps);                                           // notice
                // The flags go on before the experience is copied in, so an item written
                // complete is marked paid and pays nothing.
                SetItemFlags(item, unchecked((uint)b));                          // notice
                SetSteps(item, steps);                                           // notice
                item.ShortKey = s;
                item.LongKey = l;
                item.Short = Say(s);
                item.Long = Say(l);
                item.Xp = xp;
                item.Icon = icon;
                // Counted only when the texture made for it loads — and that texture is the quest's.
                if (IconLoads?.Invoke(quest.Icon) ?? true) read++;
            }
        }
        _ = found;
        byName[quest.Name] = quest;
        Notice();
        return true;

        void SetSteps(QuestItem item, int steps)
        {
            item.Steps = steps;
            if (item.Step >= steps) item.Step = steps - 1;
            Notice();
        }
    }

    string Say(string key) => Text?.Invoke(key) ?? key;

    /// <summary>`Destroy`: out of the log, slot and name both. The pane is refreshed but nothing is announced.</summary>
    public bool Destroy(int slot)
    {
        if (this[slot] is not { } quest) return false;
        byName.Remove(quest.Name);
        slots[slot] = null;
        Refreshed?.Invoke();
        return true;
    }

    /// <summary>
    /// The log as a load leaves it: every quest in its own slot, with no notices, no
    /// experience and no voice — a save's quests are read, not received.
    /// </summary>
    internal void Restore(IEnumerable<Quest> quests)
    {
        Array.Clear(slots);
        byName.Clear();
        foreach (var q in quests)
        {
            if ((uint)q.Slot >= Max) continue;
            q.Short = Say(q.ShortKey);
            q.Long = Say(q.LongKey);
            foreach (var i in q.Items) { i.Short = Say(i.ShortKey); i.Long = Say(i.LongKey); }
            q.IconLoaded = IconLoads?.Invoke(q.Icon) ?? true;
            slots[q.Slot] = q;
            byName[q.Name] = q;
        }
        Refreshed?.Invoke();
    }

    /// <summary>
    /// Every quest's words looked up again, for a log restored before the string tables were there
    /// to look them up in.
    /// </summary>
    public void ResolveTexts()
    {
        foreach (var q in All)
        {
            q.Short = Say(q.ShortKey);
            q.Long = Say(q.LongKey);
            q.IconLoaded = IconLoads?.Invoke(q.Icon) ?? true;
            foreach (var i in q.Items) { i.Short = Say(i.ShortKey); i.Long = Say(i.LongKey); }
        }
        Refreshed?.Invoke();
    }

    /// <summary>Every quest gone — which is what a change of chronicle does.</summary>
    public void Clear()
    {
        for (int i = 0; i < Max; i++)
            if (slots[i] is not null) Destroy(i);
    }

    /// <summary>`SetFlags`. Returns the flags as they were.</summary>
    public uint SetFlags(Quest quest, uint flags)
    {
        uint was = quest.Flags;
        quest.Flags = was | flags;
        if ((was & Complete) == 0 && (flags & Complete) != 0)
        {
            quest.Flags |= Rewarded;
            if (quest.Xp != 0) AwardParty?.Invoke(unchecked((int)quest.Xp));      // the party award ignores 0
            Speak?.Invoke(Audio.FoleyEvent.VoxQuestComplete);
        }
        if (was != quest.Flags) Notice();
        return was;
    }

    /// <summary>
    /// The host's flags taken on a player's machine (reached from message 0x45): the word is
    /// replaced, a newly complete quest is marked rewarded, and the log announces the change. The
    /// experience the quest hands to the party award is nothing on a player's machine (the award returns at
    /// once there) — the host's split is what gives it.
    /// </summary>
    public void TakeFlags(Quest quest, uint flags)
    {
        uint was = quest.Flags;
        quest.Flags = flags;
        if ((was & Complete) == 0 && (flags & Complete) != 0) quest.Flags |= Rewarded;
        Notice();
    }

    /// <summary>`ClearFlags`. Clearing the complete bit takes the rewarded bit with it — the experience stays given.</summary>
    public uint ClearFlags(Quest quest, uint flags)
    {
        uint was = quest.Flags;
        quest.Flags = was & ~flags;
        if ((was & Complete) != 0 && (flags & Complete) != 0) quest.Flags &= ~Rewarded;
        if (was != quest.Flags) Notice();
        return was;
    }

    /// <summary>`SetItemFlags`. The notice goes out whether or not anything changed.</summary>
    public uint SetItemFlags(QuestItem item, uint flags)
    {
        uint was = item.Flags;
        item.Flags = was | flags;
        if ((was & ItemComplete) == 0 && (flags & ItemComplete) != 0)
        {
            item.Flags |= Rewarded;
            if (item.Xp != 0) AwardParty?.Invoke(unchecked((int)item.Xp));
        }
        Notice();
        return was;
    }

    /// <summary>`ClearItemFlags`. Likewise announced every time.</summary>
    public uint ClearItemFlags(QuestItem item, uint flags)
    {
        uint was = item.Flags;
        item.Flags = was & ~flags;
        if ((was & ItemComplete) != 0 && (flags & ItemComplete) != 0) item.Flags &= ~Rewarded;
        Notice();
        return was;
    }

    /// <summary>
    /// `SetItemNumSteps`. A new count is taken as its absolute value — which the reader's
    /// "at least 1" does not apply here, so a script can set 0 — and pulls the current step under it.
    /// </summary>
    public int SetItemSteps(QuestItem item, int steps)
    {
        int was = item.Steps;
        if (steps == was) return was;
        int count = Math.Abs(steps);
        item.Steps = count;
        if ((uint)item.Step >= (uint)count) item.Step = count - 1;
        Notice();
        return was;
    }

    /// <summary>`SetItemCurStep`: taken as its absolute value, and only if it is below the count; announced either way.</summary>
    public int SetItemStep(QuestItem item, int step)
    {
        int was = item.Step;
        if (step == was) return was;
        int at = Math.Abs(step);
        if ((uint)at < (uint)item.Steps) item.Step = at;
        Notice();
        return was;
    }

    void Notice()
    {
        Refreshed?.Invoke();
        Changed?.Invoke();
        Post?.Invoke(ChangedKey);
    }
}
