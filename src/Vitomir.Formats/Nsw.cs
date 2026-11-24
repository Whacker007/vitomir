// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// One place as 1.1 left it: `SaveGames\SP<n>\location_<n>.nsw`.
//
// The game writes it and reads it, nine sections in one order, each
// behind its own tag and none with a length — the same framing as `!main.nsg` (`Nsg.cs`). The
// things in it are the very records `!main.nsg` keeps its party in, read
// here by the same reader (`NsgReader.ReadThing`).
//
//   tag 0x12                                  the file
//   tag 0x15   the sectors                    per sector: tag 0x16, three words, the GUIDs in it
//   tag 0x14   aPlayerStarts, aConversationStarts
//   tag 0x13   the world's table of points
//   tag 0x17   the world's own scene          (present-flag, then the record)
//   tag 0x17   the chapter scene              (the same)
//   tag 0x18   the things                     (every one but projectiles)
//   tag 0x19   the scripts' timers
//   tag 0x1a   the captures
namespace Vitomir.Formats;

/// <summary>The `.nsw` section sentinels, by the number the writer puts in the file.</summary>
public static class NswTag
{
    public const uint File = 0x12, Table13 = 0x13, Starts = 0x14, Sectors = 0x15, Sector = 0x16,
                      Scene = 0x17, Things = 0x18, Timers = 0x19, Captures = 0x1a, SceneRecord = 0x1b;
}

/// <summary>
/// One sector of the place (tag 0x16): two words of the sector's own, and the GUIDs of the things
/// standing in it — which the reader turns back into pointers once every thing is made (the load's
/// last loop).
/// </summary>
/// <param name="Field04">The sector's first word — 5 in every sector of every file measured.</param>
/// <param name="Index">The sector's index word — the low half is the sector's own number; the high half is kept whole (0xc in some files, 0 in others).</param>
/// <param name="CountWord">The sector's count word — the low half is how many GUIDs follow; the high half is kept whole.</param>
public sealed record NswSector(uint Field04, uint Index, uint CountWord, uint[] Guids);

/// <summary>
/// An entry of the two start tables under tag 0x14 — a place a player or a conversation starts from,
/// named by the reader's own asserts (<c>pWorld-&gt;aPlayerStarts</c>, <c>pWorld-&gt;aConversationStarts</c>):
/// the sector (−1 for none), a 3 × 4 transform and two words.
/// </summary>
public sealed record NswStart(int Sector, byte[] Transform, uint A, uint B);

/// <summary>
/// An entry of the world's table of points (tag 0x13, 0x48 bytes each): the sector, a 3 × 4
/// transform and four words, which the reader hands with the transform and the sector to a lookup and
/// keeps what it answers in the entry. The words are written in the order of the four fields' names.
/// What the table is has not been read (a light or a sound source is the likeliest).
/// </summary>
public sealed record NswPoint(int Sector, byte[] Transform, uint Field38, uint Field40, uint Field3C, uint Field44);

/// <summary>
/// A scene as the world keeps it (tag 0x1b). <see cref="Count"/>
/// is how many things the scene placed — the length of its table of thing pointers,
/// which a thing's scene index points into.
/// </summary>
public sealed class NswScene
{
    /// <summary>How many things the scene placed.</summary>
    public uint Count { get; init; }
    /// <summary>The scene record's second and third words (the scene's id, which a thing record's scene field names).</summary>
    public uint Field04 { get; init; }
    public uint Id { get; init; }
    /// <summary>The scene's three names (its file first).</summary>
    public string Name { get; init; } = "";
    public string Text2C { get; init; } = "";
    public string Text4C { get; init; } = "";
    /// <summary>The scene's words after its names, its twelve bytes, and the five words after them.</summary>
    public uint Field70 { get; init; }
    public byte[] Bytes74 { get; init; } = [];
    public uint[] Fields80 { get; init; } = [];
    /// <summary>The two names the reader loads (then plays at 30.0) — the scene's sounds, by the look of the calls.</summary>
    public string Sound1 { get; init; } = "";
    public string Sound2 { get; init; } = "";
    /// <summary>The scene's list of names, counted by the word before it.</summary>
    public string[] Names { get; init; } = [];
    /// <summary>Five more of the scene's words, in the file's order.</summary>
    public uint Field9C { get; init; }
    public string TextA0 { get; init; } = "";
    public uint FieldC0 { get; init; }
    public string TextC4 { get; init; } = "";
    public uint FieldE4 { get; init; }
}

/// <summary>
/// A timer a script set that had not fired (tag 0x19): whose (the codex object's GUID),
/// the timer's id and time, and its four arguments. The reader gives it
/// back only when the codex object it names exists.
/// </summary>
/// <param name="CodexGuid">The script's GUID — the same number a thing's script record carries.</param>
/// <param name="Id">The timer's id.</param>
/// <param name="Time">When it fires.</param>
/// <param name="Args">The four arguments (floats, as the script passed them).</param>
public sealed record NswTimer(uint CodexGuid, uint Id, uint Time, uint[] Args);

/// <summary>
/// A capture (tag 0x1a): the thing captured, the codex object that captured it, and the id
/// it gave. Given back on load when both still exist.
/// </summary>
public readonly record struct NswCapture(uint ThingGuid, uint CodexGuid, uint Id);

/// <summary>`location_&lt;n&gt;.nsw` — one place, as it was left.</summary>
public sealed class NswWorld
{
    public NswSector[] Sectors { get; init; } = [];
    public NswStart[] PlayerStarts { get; init; } = [];
    public NswStart[] ConversationStarts { get; init; } = [];
    public NswPoint[] Points { get; init; } = [];
    /// <summary>The level's own scene and the chapter's laid over it, when there are.</summary>
    public NswScene? WorldScene { get; init; }
    public NswScene? ChapterScene { get; init; }
    /// <summary>Every thing in the place the filter kept: no party member, no projectile, nothing equipped.</summary>
    public NsgThing[] Things { get; init; } = [];
    public NswTimer[] Timers { get; init; } = [];
    public NswCapture[] Captures { get; init; } = [];

    /// <summary>A thing record's scene field when it came from the level's own scene.</summary>
    public const int OwnScene = -2;

    public static NswWorld Read(byte[] data, string name = "location.nsw", NsgReadOptions? options = null) =>
        new NswReader(new BinReader(data, name), options ?? NsgReadOptions.Default).ReadFile();

    public static NswWorld ReadFile(string path, NsgReadOptions? options = null) =>
        Read(File.ReadAllBytes(path), Path.GetFileName(path), options);

    /// <summary>The location number a `location_&lt;n&gt;.nsw` file is for, or −1.</summary>
    public static int LocationOf(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        return stem.StartsWith("location_", StringComparison.OrdinalIgnoreCase)
               && int.TryParse(stem.AsSpan("location_".Length), out int n) ? n : -1;
    }
}

/// <summary>The sequential parse of a `.nsw`, in the order the game writes it.</summary>
sealed class NswReader(BinReader r, NsgReadOptions options)
{
    const int Name = 0x20;
    readonly NsgReader things = new(r, options);

    void Tag(uint tag, string what) => things.Tag(tag, what);

    public NswWorld ReadFile()
    {
        Tag(NswTag.File, "file");

        Tag(NswTag.Sectors, "sectors");
        // The count is the level's, not the file's: the writer walks the level's sectors and the reader
        // walks as many again. Nothing says how many, so read until the next tag is not a sector's.
        var sectors = new List<NswSector>();
        while (r.Remaining >= 4 && Peek() == NswTag.Sector)
        {
            Tag(NswTag.Sector, "sector");
            uint f4 = r.U32(), index = r.U32(), countWord = r.U32();
            sectors.Add(new NswSector(f4, index, countWord, r.U32s((int)(countWord & 0xffff))));
        }

        Tag(NswTag.Starts, "starts");
        var players = Starts();
        var conversations = Starts();

        Tag(NswTag.Table13, "table 0x13");
        int points = r.Count32(65536);
        var table = new NswPoint[points];
        for (int i = 0; i < points; i++)
            table[i] = new NswPoint(r.I32(), r.Bytes(0x30), r.U32(), r.U32(), r.U32(), r.U32());

        var world = Scene("the world's scene");
        var chapter = Scene("the chapter scene");

        Tag(NswTag.Things, "things");
        int count = r.Count32(65536);
        var list = new NsgThing[count];
        for (int i = 0; i < count; i++) list[i] = things.ReadThing();

        Tag(NswTag.Timers, "timers");
        int timers = r.Count32(65536);
        var timerList = new NswTimer[timers];
        for (int i = 0; i < timers; i++) timerList[i] = new NswTimer(r.U32(), r.U32(), r.U32(), r.U32s(4));

        Tag(NswTag.Captures, "captures");
        int captures = r.Count32(65536);
        var captureList = new NswCapture[captures];
        for (int i = 0; i < captures; i++) captureList[i] = new NswCapture(r.U32(), r.U32(), r.U32());
        r.ExpectEof();

        return new NswWorld
        {
            Sectors = [.. sectors], PlayerStarts = players, ConversationStarts = conversations, Points = table,
            WorldScene = world, ChapterScene = chapter, Things = list, Timers = timerList, Captures = captureList,
        };
    }

    uint Peek() => BitConverter.ToUInt32(r.Data, r.Pos);

    NswStart[] Starts()
    {
        int n = r.Count32(65536);
        var list = new NswStart[n];
        for (int i = 0; i < n; i++) list[i] = new NswStart(r.I32(), r.Bytes(0x30), r.U32(), r.U32());
        return list;
    }

    /// <summary>A scene: tag 0x17, a word saying whether the scene is there, and its record.</summary>
    NswScene? Scene(string what)
    {
        Tag(NswTag.Scene, what);
        if (r.U32() == 0) return null;
        Tag(NswTag.SceneRecord, "scene record");
        uint count = r.U32(), f4 = r.U32(), id = r.U32();
        string name = r.CStr(Name), t2c = r.CStr(Name), t4c = r.CStr(Name);
        uint f70 = r.U32();
        byte[] b74 = r.Bytes(0xc);
        uint[] f80 = r.U32s(5);
        string sound1 = r.CStr(Name), sound2 = r.CStr(Name);
        int names = r.Count32(64);
        var list = new string[names];
        for (int i = 0; i < names; i++) list[i] = r.CStr(Name);
        return new NswScene
        {
            Count = count, Field04 = f4, Id = id, Name = name, Text2C = t2c, Text4C = t4c,
            Field70 = f70, Bytes74 = b74, Fields80 = f80, Sound1 = sound1, Sound2 = sound2, Names = list,
            Field9C = r.U32(), TextA0 = r.CStr(Name), FieldC0 = r.U32(), TextC4 = r.CStr(Name), FieldE4 = r.U32(),
        };
    }
}
