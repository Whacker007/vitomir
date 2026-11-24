// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A character file of 1.1's built byte by byte for the tests: the writer's grammar (the header, then the player's own save:
// the thing, the actor, the player) with made-up content, so no original file is in the repository. Linked into the
// test projects that need one (Formats, Engine, Platform, Platform.Host).
using System.Buffers.Binary;
using System.Text;

namespace Vitomir.TestData;

/// <summary>A made-up <c>Crypt\npf%06d.npf</c>: a player with a sheet, disciplines, a pack, effects, a purse.</summary>
public sealed class NpfBytes
{
    public string Magic { get; set; } = "npf";
    public uint Version { get; set; } = 5;
    public string Name { get; set; } = "Anna";
    public string Login { get; set; } = "npf000003";
    public string ClanName { get; set; } = "Brujah";
    public string Template { get; set; } = "mp_christof_da";
    public string Clan { get; set; } = "brujah";
    public byte Type { get; set; }
    public uint Flags { get; set; } = 0x8000;
    public uint Flags2 { get; set; }
    public uint Purse { get; set; } = 345;
    public float Health { get; set; } = 80f;
    public float MaxHealth { get; set; } = 120f;
    public float[] BaseStats { get; set; } = [.. Enumerable.Range(0, 19).Select(i => 10f + i)];
    public float[] Stats { get; set; } = [.. Enumerable.Range(0, 19).Select(i => 12f + i)];
    public uint Spendable { get; set; } = 250;
    public uint Experience { get; set; } = 1250;
    public List<(string Name, int Level, int Quick, uint Flags)> Disciplines { get; } = [("potence", 2, 1, 0), ("celerity", 1, -1, 0), ("feed", 0, -1, 0), ("awe", -1, -1, 0)];
    public List<(string Template, uint Location, int X, int Y, uint Amount)> Items { get; } = [("longsword", 0x8, 0, 0, 1), ("bloodpack", 0x400, 3, 1, 4), ("stake", 0x4000, 9, 8, 1)];
    public List<(string Name, int Until, uint Next, int Level, float Value, uint Flags)> Effects { get; } = [("ef_decreasefrenzy", 0, 18258, 0, -2f, 0), ("ef_potence", 90000, 1000, 2, 3f, 0), ("ef_swordworn", 0, 0, 1, 1f, 0x1)];
    public string Selected { get; set; } = "potence";

    readonly List<byte> b = [];

    void U8(byte v) => b.Add(v);
    void U32(uint v) { Span<byte> s = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(s, v); b.AddRange(s.ToArray()); }
    void I32(int v) => U32(unchecked((uint)v));
    void F32(float v) => U32(BitConverter.SingleToUInt32Bits(v));
    void Str(string s, int n = 0x20) { var raw = new byte[n]; Encoding.ASCII.GetBytes(s).AsSpan(0, Math.Min(s.Length, n - 1)).CopyTo(raw); b.AddRange(raw); }
    void Zero(int n) => b.AddRange(new byte[n]);
    void OptionalName(string? s) { if (s is null) { U32(0); return; } U32(1); Str(s); }

    /// <summary>The file's bytes.</summary>
    public byte[] Build()
    {
        b.Clear();
        Str(Magic, 4); U32(Version); Str(Name); Str(Login); Str(ClanName); U32(Experience);
        U32(7);                                                      // the type word the reader reads before the player's load
        Thing(0x40000003, Template, "ANNA", "mpchristof.nag", model: "christof.nod");
        // the actor
        U32(0x24); U32(Flags); U32(Flags2); U8(Type); U8(0); U32(0); U32(Purse);
        U32(0);                                                      // the cast id: none
        OptionalName(Clan.Length == 0 ? null : Clan);
        if ((Flags2 & 0x800) != 0) Zero(0x50);
        U8(0); U8(0);
        F32(Health); F32(MaxHealth); F32(115f); F32(250f); F32(1f); F32(1f); U32(50);
        foreach (var v in BaseStats) F32(v);
        Zero(10 * 4);
        foreach (var v in Stats) F32(v);
        Zero(10 * 4);
        U32(0);
        U32(0x26);
        U32(0x27); U32((uint)Effects.Count);
        uint id = 20;
        foreach (var e in Effects)
        {
            Str(e.Name); I32(e.Until); U32(e.Next); F32(200f); U32(id++); U32(e.Flags == 0x1 ? 101u : 0u); U32(e.Flags); I32(e.Level); F32(e.Value); U32(0); U32(0);
            U32(0);                                                  // no script
        }
        U32(0x29); U32((uint)Items.Count);
        uint guid = 101;
        foreach (var i in Items) Item(guid++, i.Template, i.Location, i.X, i.Y, i.Amount, weapon: false);
        U32(0);                                                      // nothing in the first of the three
        U32(1); Item(1, "punch", 0, 0, 0, 0, weapon: true);         // the unarmed weapon, the second
        U32(0);                                                      // nothing in the third
        U32(1); for (int i = 0; i < 5; i++) U32(0);
        U32(0x28); U32((uint)Disciplines.Count);
        uint sv = (uint)Disciplines.Count;
        foreach (var d in Disciplines) { Str(d.Name); U32(d.Flags); I32(d.Level); I32(d.Quick); U32(0); U32(sv--); }
        Str(Selected.Length == 0 ? "..." : Selected);
        U32(0x29);
        U32(0);                                                      // no shape change
        // the player
        U32(0x2b); Str(Name); U32(Spendable); U32(Experience); OptionalName("christofH.nod"); Str(""); U8(0); U8(0);
        return [.. b];
    }

    void Thing(uint guid, string name, string description, string? template, string? model)
    {
        U32(0x1e); U32(guid); Str(name); Str(description); I32(-1); I32(-1); I32(-1);
        Zero(0x30); Zero(0x30);
        U32(0); U32(0x10000000); U32(0xc); Zero(0x28);
        U32(1);                                                      // shape 1: no data
        U32(0);                                                      // no records
        OptionalName(template);
        U32(0);
        U32(0x1f);
        if (model is null) U32(0);
        else { U32(2); U32(0); U32(0); Str(model); U32(0); }
        U32(0x21); U32(0x22);
        U32(0x23); U32(0); U32(0);
        U32(0x20);
        U32(0);                                                      // no script
    }

    void Item(uint guid, string template, uint location, int x, int y, uint amount, bool weapon)
    {
        U32(weapon ? 5u : 1u);
        Thing(guid, template, template.ToUpperInvariant(), null, null);
        U32(0x2c); U32(0); U32(amount); U32(10); U32(0); U32(0); U32(location); I32(x); I32(y);
        U32(0); U32(0);                                              // no restrictions, no effects
        if (weapon) { U32(0x2d); U8(0); U8(0); }
    }
}
