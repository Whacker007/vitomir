// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Formats.Tests;

/// <summary>
/// The 36 explosion scripts (`misc/Explosions/*.nex`) against the shipped files, read as the game
/// reads them. What is pinned: every file reads, how many events each keeps and which lines 1.1
/// drops — the three files that use the documented-but-unbuilt `DYNARADIUS`, and `alpha2.nex`'s
/// last line, which runs on into the line before it because it has no newline (every CREATE names
/// a template the game has, so none of those goes) — and the numbers of the two explosions the
/// tests fire (the fireball and the grenade), word for word.
/// </summary>
public class NexTests
{
    static (NobArchive Nob, List<string> Names) Open()
    {
        var nob = GameData.Open("Resource.nob");
        var names = nob.NamesWithExtension(".nex").ToList();
        return (nob, names);
    }

    /// <summary>Every template name in the four shipped template files, which is what a CREATE's template is looked up in.</summary>
    static HashSet<string> Templates(NobArchive nob)
    {
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in nob.NamesWithExtension(".not"))
            foreach (var t in NotFile.Read(nob.Read(name), name).Templates) all.Add(t.Name);
        return all;
    }

    static NexFile Read(NobArchive nob, List<string> names, string file, HashSet<string>? templates = null)
    {
        var name = names.First(n => n.EndsWith("/" + file, StringComparison.OrdinalIgnoreCase));
        return NexFile.Read(nob.Read(name), file, templates is null ? null : templates.Contains);
    }

    [Fact]
    public void All_36_shipped_files_read_and_drop_only_what_the_engine_drops()
    {
        var (nob, names) = Open();
        using var _ = nob;
        Assert.Equal(36, names.Count);
        var templates = Templates(nob);
        int events = 0;
        var dropped = new List<string>();
        foreach (var name in names)
        {
            var file = NexFile.Read(nob.Read(name), Path.GetFileName(name), templates.Contains);
            Assert.NotEmpty(file.Events);
            // Times never go backwards except in rocketExp.nex, whose 500 ms MODELALPHA is written
            // before two 430 ms lines; the engine runs events in file order, so that one waits.
            events += file.Events.Count;
            dropped.AddRange(file.Dropped.Select(d => $"{Path.GetFileName(name)} {d}"));
        }
        Assert.Equal(240, events);
        // What 1.1 throws away, and why (see Nex.cs):
        Assert.Equal(5, dropped.Count(d => d.Contains("'DYNARADIUS' is not a command")));
        Assert.Contains(dropped, d => d.StartsWith("alpha2.nex line 10") && d.Contains("NOP with 5 words"));
        Assert.Equal(6, dropped.Count);           // and every CREATE's template is in the game
    }

    /// <summary>
    /// `fireball.nex`, which `exp_fireball` names and every fireball, incendiary arrow and flame
    /// thrower detonates into: light on, the flame particle 5 units down for 750 ms, the light up to
    /// 200 and down to 10 while it reddens, off at 2450, gone at 2500.
    /// </summary>
    [Fact]
    public void The_fireball_reads_word_for_word()
    {
        var (nob, names) = Open();
        using var _ = nob;
        var f = Read(nob, names, "fireball.nex", Templates(nob));
        Assert.Empty(f.Dropped);
        Assert.Equal(new[] { NexCommand.DynaOn, NexCommand.Create, NexCommand.DynaIntensity, NexCommand.DynaIntensity,
                             NexCommand.DynaColor, NexCommand.DynaOff, NexCommand.Nop }, f.Events.Select(e => e.Command));
        Assert.Equal(new[] { 1, 1, 120, 430, 430, 2450, 2500 }, f.Events.Select(e => e.Time));
        Assert.Equal(2500, f.Length);

        var create = f.Events[1];
        Assert.Equal("incendExplosion", create.Name);
        Assert.Equal(1, create.Repeat);
        Assert.Equal(1f, create.Chance);
        Assert.Equal(750, create.Timer);
        Assert.Equal(5f, create.Distance);
        Assert.Equal((0f, 0f, -1f), create.Offset);
        Assert.False(create.RandomDistance);

        var up = f.Events[2];
        Assert.Equal(NexRamp.Ramp, up.Mode);
        Assert.Equal(200f, up.Value);
        Assert.Equal(200, up.RampMs);
        var red = f.Events[4];
        Assert.Equal((128f, 0f, 0f), red.Color);
        Assert.Equal(1000, red.RampMs);
    }

    /// <summary>
    /// `grenadeEXP.nex`: the damage line reads <c>SETDAMAGE 30 1 0 4 128.0</c> — a max below the
    /// min, which the parser makes min + 1, so a grenade deals 30 to 31; a pulse of 0,
    /// fire, and a 128 radius. The WALLMARK and both model ramps are kept.
    /// </summary>
    [Fact]
    public void The_grenade_deals_thirty_to_thirty_one_fire_over_128()
    {
        var (nob, names) = Open();
        using var _ = nob;
        var f = Read(nob, names, "grenadeEXP.nex", Templates(nob));
        Assert.Empty(f.Dropped);
        var set = f.Events.Single(e => e.Command == NexCommand.SetDamage);
        Assert.Equal(120, set.Time);
        Assert.Equal(30, set.MinDamage);
        Assert.Equal(31, set.MaxDamage);
        Assert.Equal(0, set.PulseMs);
        Assert.Equal(4, set.DamageType);
        Assert.Equal(128f, set.Radius);
        Assert.Equal(7u, set.Flags);
        Assert.Equal(125, f.Events.Single(e => e.Command == NexCommand.ApplyDamage).Time);
        var mark = f.Events.Single(e => e.Command == NexCommand.WallMark);
        Assert.Equal(120f, mark.MarkSize);
        Assert.Equal(15000, mark.MarkMs);
        Assert.Equal(2000, f.Length);
    }

    /// <summary>
    /// A random offset: <c>smokepuffs.nex</c> makes ten puffs at <c>r30 r100 r100 r100</c> with a
    /// 0.9 chance each — every axis and the distance flagged random, the numbers after the r kept.
    /// </summary>
    [Fact]
    public void An_r_marks_a_number_as_drawn_at_random()
    {
        var (nob, names) = Open();
        using var _ = nob;
        var e = Read(nob, names, "smokepuffs.nex").Events[0];
        Assert.Equal(NexCommand.Create, e.Command);
        Assert.Equal(10, e.Repeat);
        Assert.Equal(0.9f, e.Chance);
        Assert.Equal(0xfu, e.Flags & 0xf);
        Assert.Equal(30f, e.Distance);
        Assert.Equal((100f, 100f, 100f), e.Offset);
    }

    /// <summary>
    /// The parser's own rules on text made up here: a DYNARADIUS line is dropped, a word stops at
    /// 32 characters and the rest is read as the next word, a comment is <c>//</c> or <c>#</c>, a
    /// CREATE with a repeat of 0 makes one, and a chance of 0 is certain.
    /// </summary>
    [Fact]
    public void The_parser_keeps_its_own_rules_on_made_up_lines()
    {
        var text = "# a comment\r\n// and another\r\n\r\n"
                 + "1 DYNARADIUS INSTANT 64\r\n"
                 + "2 CREATE 0 0 0 thing 0 0 0 0\r\n"
                 + "3 DYNASTYLE 30\r\n"
                 + "4 DYNASTYLE 3\r\n"
                 + "5 NOP\r\n";
        var f = NexFile.Read(System.Text.Encoding.Latin1.GetBytes(text), "made-up.nex");
        Assert.Equal(new[] { NexCommand.Create, NexCommand.DynaStyle, NexCommand.Nop }, f.Events.Select(e => e.Command));
        Assert.Equal(1, f.Events[0].Repeat);
        Assert.Equal(1f, f.Events[0].Chance);
        Assert.Equal(3, f.Events[1].Style);
        Assert.Equal(2, f.Dropped.Count);
    }
}
