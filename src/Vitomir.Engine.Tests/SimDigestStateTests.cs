// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **What version 2 of the digest sees** (ours). Until
/// it, a thing's row held its place, health, flags, mind and action, so a change that moved only a stat,
/// the blood pool, an effect, the pack or a discipline went unseen until it changed what somebody did: the
/// Berserker hammer's frenzy climbed for five seconds of a recording before the check noticed, at the frame
/// the wielder's mind changed. These pin that each of those now shows in the frame it happens in, in a
/// field that says where to look, and that a version 1 digest (a recording made before) still compares.
/// </summary>
public class SimDigestStateTests
{
    static (Location Place, WorldThing Christof) Convent(Vfs.GameFiles files)
    {
        var place = Location.Load(files, "P1_OLDT", sky: false, scene: "OLDT_7_1", player: "Christof");
        place.Awaken(new Ai.AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        return (place, place.Player!.Thing);
    }

    static string Field(DigestFrame frame, WorldThing thing, string name) =>
        frame.Things[thing.Guid].FirstOrDefault(f => f.StartsWith(name + "=", StringComparison.Ordinal)) ?? "";

    static void Differs(Location place, WorldThing thing, string field, Action change)
    {
        var before = SimDigest.Take(place, 1, 0);
        change();
        var after = SimDigest.Take(place, 1, 0);
        var found = SimDigest.Compare(before, after);
        Assert.NotNull(found);
        Assert.Equal(field, found!.Value.Field);
        Assert.Contains($"thing {thing.Guid}", found.Value.What);
        Assert.NotEqual(Field(before, thing, field), Field(after, thing, field));
    }

    [Fact]
    public void A_frame_taken_twice_is_the_same_and_names_a_characters_new_fields()
    {
        using var files = Install.Open();
        var (place, christof) = Convent(files);
        var a = SimDigest.Take(place, 1, 0);
        Assert.Null(SimDigest.Compare(a, SimDigest.Take(place, 1, 0)));
        foreach (var name in new[] { "body", "actor", "stats", "pack" })
            Assert.StartsWith(name + "=", Field(a, christof, name));
        Assert.Equal(SimDigest.Version, a.Version);
    }

    [Fact]
    public void A_stat_moved_alone_is_seen_in_stats()
    {
        using var files = Install.Open();
        var (place, christof) = Convent(files);
        Differs(place, christof, "stats", () => christof.Actor!.Stat[(int)ActorStat.Blood] -= 1f);
    }

    [Fact]
    public void An_item_moved_in_the_pack_is_seen_in_pack()
    {
        using var files = Install.Open();
        var (place, christof) = Convent(files);
        var item = new Item { Name = "torchU", Amount = 1 };
        Differs(place, christof, "pack", () => christof.Actor!.Inventory.Add(item));
        Differs(place, christof, "pack", () => item.Cell = (item.Cell.X + 1, item.Cell.Y));
        Differs(place, christof, "pack", () => item.Amount = 2);
    }

    [Fact]
    public void A_flag_of_the_characters_own_is_seen_in_actor()
    {
        using var files = Install.Open();
        var (place, christof) = Convent(files);
        Differs(place, christof, "actor", () => christof.Actor!.Flags ^= Actor.Flag.AiPaused);
    }

    [Fact]
    public void A_things_velocity_is_seen_in_body()
    {
        using var files = Install.Open();
        var (place, christof) = Convent(files);
        Differs(place, christof, "body", () => christof.Velocity += new Vector3(0f, 0f, 1f));
    }

    [Fact]
    public void A_recording_of_version_1_compares_without_the_fields_it_never_had()
    {
        using var files = Install.Open();
        var (place, christof) = Convent(files);
        var now = SimDigest.Take(place, 1, 0);
        var old = new DigestFrame
        {
            Frame = 1, Header = now.Header, Version = 1,
            Things = new SortedDictionary<int, string[]>(now.Things.ToDictionary(
                p => p.Key, p => p.Value.Where(f => !SimDigest.Since2.Any(n => f.StartsWith(n + "=", StringComparison.Ordinal))).ToArray())),
        };
        Assert.Null(SimDigest.Compare(old, now));
        // What version 1 did hold is still compared.
        christof.Position += new Vector3(1f, 0f, 0f);
        Assert.Equal("pos", SimDigest.Compare(old, SimDigest.Take(place, 1, 0))!.Value.Field);
        // And a version 2 recording with a field missing is a difference.
        var short2 = new DigestFrame { Frame = 1, Header = now.Header, Things = old.Things };
        Assert.NotNull(SimDigest.Compare(short2, now));
    }

    [Theory]
    [InlineData("vitomir-digest 1", 1)]
    [InlineData("vitomir-digest 2", 2)]
    [InlineData("vitomir-digest 3", 3)]
    [InlineData("vitomir-digest 4", 0)]
    [InlineData("vitomir-tape 1", 0)]
    [InlineData(null, 0)]
    public void A_digests_first_line_names_a_version_this_build_reads_or_none(string? line, int version) =>
        Assert.Equal(version, SimDigest.VersionOf(line));

    [Fact]
    public void A_version_1_file_is_read_and_says_so()
    {
        using var one = new DigestReader(new StringReader("vitomir-digest 1\nf 1 clock=5\nt 7 template=a pos=0,0,0\n"));
        Assert.Equal(1, one.Version);
        Assert.Equal(1, one.Next()!.Version);
        Assert.Throws<InvalidDataException>(() => new DigestReader(new StringReader("vitomir-digest 9\n")));
    }
}
