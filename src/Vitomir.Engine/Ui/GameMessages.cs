// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The repeating game messages a script can post (`Codex.MessageAddUser` / `MessageAddSystem`): a
// string-table line put into the message box now, and again every so often, until it runs out.
namespace Vitomir.Engine.Ui;

/// <summary>
/// Sixty-four slots of repeating messages (with a high-water mark and
/// a GUID counter) and their tick, run from
/// the world's tick. **Nothing in the shipped game adds one** — the only call in the 465
/// shipped classes is `HavenRest` removing a system message nobody posted — so what follows is for
/// the mods, and it is the engine's to the letter, three bugs included:
/// <list type="bullet">
/// <item><b>Adding answers the slot, not the GUID.</b> Both adds return the slot index they filled,
/// and the first message ever added is slot 0 — the documented "0 for
/// failure". `MessageRemoveByGuid` compares the message's GUID (counted from 1), so
/// handing it what an add returned removes some other message, or none.</item>
/// <item><b>Removing by system id never finds a system message.</b> The remove compares the id against
/// a field which neither add ever writes; only an id of 0 matches, and then every system
/// message goes.</item>
/// <item><b>Removing by string id removes the ones that do not match.</b> The string remove frees a user
/// message when <c>_strcmpi</c> answers <em>non</em>-zero, against the same unwritten field — an
/// empty string — so any id but "" clears every user message.</item>
/// </list>
/// The output channel a script names is kept and never read: the tick writes to the message box.
/// </summary>
public sealed class GameMessages
{
    /// <summary>The engine's slot count.</summary>
    public const int Slots = 64;

    /// <summary>One posted message (<c>operator new(0x40)</c>).</summary>
    public sealed class Entry
    {
        /// <summary>When it is next put in the box.</summary>
        public int NextAt;
        /// <summary>0 a system message, 1 a user one.</summary>
        public int Kind;
        /// <summary>The channel the script asked for. Never read.</summary>
        public int Output;
        /// <summary>Its GUID, counted from 1.</summary>
        public int Guid;
        /// <summary>How often it repeats, in ms; 0 means once.</summary>
        public int Frequency;
        /// <summary>When it expires, or 0 for never.</summary>
        public int ExpireAt;
        /// <summary>How many more times it is put in the box, or 0 for ever.</summary>
        public int Repeat;
        /// <summary>The line, looked up when it was added.</summary>
        public string Text = "";
    }

    readonly Entry?[] slots = new Entry?[Slots];
    int high = -1, counter;

    /// <summary>Where the lines go — the status bar's box.</summary>
    public required MessageLog Log { get; init; }

    public IReadOnlyList<Entry?> All => slots;

    /// <summary>`MessageAddSystem`: the line <c>GAMEMESSAGE_SYSTEM&lt;id&gt;</c>.</summary>
    public int AddSystem(int systemId, int output, int expireMs, int repeat, int frequencyMs, int now) =>
        Add(0, Log.Format($"GAMEMESSAGE_SYSTEM{systemId}"), output, expireMs, repeat, frequencyMs, now);

    /// <summary>`MessageAddUser`: a line named by its string-table key.</summary>
    public int AddUser(string key, int output, int expireMs, int repeat, int frequencyMs, int now) =>
        Add(1, Log.Format(key), output, expireMs, repeat, frequencyMs, now);

    int Add(int kind, string text, int output, int expireMs, int repeat, int frequencyMs, int now)
    {
        for (int i = 0; i < Slots; i++)
        {
            if (slots[i] is not null) continue;
            slots[i] = new Entry
            {
                Guid = ++counter, Kind = kind, Output = output, NextAt = now,
                ExpireAt = expireMs == 0 ? 0 : now + expireMs, Repeat = repeat, Frequency = frequencyMs, Text = text,
            };
            if (high < i) high = i;
            return i;
        }
        return 0;
    }

    /// <summary>`MessageRemoveByGuid`: the first message with that GUID.</summary>
    public void RemoveByGuid(int guid)
    {
        for (int i = 0; i <= high; i++)
            if (slots[i] is { } e && e.Guid == guid) { Free(i); return; }
    }

    /// <summary>`MessageRemoveBySystemId`: see the class notes — only 0 matches.</summary>
    public void RemoveBySystemId(int systemId)
    {
        if (systemId != 0) return;
        for (int i = 0; i <= high; i++)
            if (slots[i] is { Kind: 0 }) Free(i);
    }

    /// <summary>`MessageRemoveByStringId`: see the class notes — the test is inverted.</summary>
    public void RemoveByStringId(string key)
    {
        if (key.Length == 0) return;
        for (int i = 0; i <= high; i++)
            if (slots[i] is { Kind: 1 }) Free(i);
    }

    /// <summary>
    /// The tick. A message past its expiry is freed — and then, because the engine goes
    /// on reading the block it has just freed, it is still put in the box once more if it was due.
    /// A due message is put in the box; one with repeats left, or repeating for ever, comes back
    /// after its frequency, and one with no frequency, or out of repeats, goes.
    /// </summary>
    public void Tick(int now)
    {
        int last = high;
        for (int i = 0; i <= last; i++)
        {
            if (slots[i] is not { } e) continue;
            bool expired = e.ExpireAt != 0 && (uint)e.ExpireAt < (uint)now;
            if (expired) Free(i);
            if (e.NextAt > now) continue;
            Log.Add(e.Text);
            if (expired) continue;
            if (e.Repeat == 0 || --e.Repeat != 0)
            {
                if (e.Frequency != 0) { e.NextAt = e.Frequency + now; continue; }
            }
            Free(i);
        }
    }

    void Free(int i)
    {
        slots[i] = null;
        if (i == high) high--;
    }
}
