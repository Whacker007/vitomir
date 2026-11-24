// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Money, music, the unstake and the path search, one rule a test: the chronicle's money bags and the roll that picks one,
/// the music and ambient percentages read unsigned, the music bar that ignores the duck, the unstake's root kept out of the
/// fade, and the path search's crossing of an edge it runs parallel to.
/// </summary>
public class MoneyMusicAndPathTests
{
    static (Location Place, Session Session) Monastery(GameFiles files, string? chronicle)
    {
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var session = new Session { Here = place, Chronicle = chronicle is null ? null : Chronicle.Load(files, chronicle) };
        place.Session = session;
        return (place, session);
    }

    /// <summary>
    /// **Money dropped in London is a bundle of notes** (the chronicle's table, filled by
    /// its metafile's `CASH cashus`/`cashuk` line). `london.nmf` names only `cashUK`, and
    /// the pick still rolls once on the shared stream — one entry or not. Vitomir made a
    /// `GoldBag` everywhere, having taken the table for one nothing fills.
    /// </summary>
    [Fact]
    public void Money_dropped_in_London_is_a_bundle_of_notes_and_costs_one_roll()
    {
        using var files = Install.Open();
        var (place, _) = Monastery(files, "London");
        long before = place.Minds!.Random.Draws;
        var bag = DropCash.Bag(place.Steered!, 25)!;
        Assert.Equal("cashUK", bag.Template, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(25, bag.Item!.Amount);
        Assert.True(files.TryRead("3d/models/" + (bag.Model.Contains('.') ? bag.Model : bag.Model + ".nod"), out _), bag.Model);   // and it has a body to draw
        Assert.Equal(before + 1, place.Minds.Random.Draws);
    }

    /// <summary>
    /// Prague's line names two (`GoldBag goldCoins`), both templates exist, and the roll picks one:
    /// <c>trunc(rand() × 1/32767 × 2)</c>. Over a run of drops both turn up, each exactly where a copy
    /// of the stream says it should.
    /// </summary>
    [Fact]
    public void Money_dropped_in_Prague_is_a_bag_or_a_pile_of_coins_by_the_roll()
    {
        using var files = Install.Open();
        var (place, session) = Monastery(files, "Prague");
        Assert.Equal(new[] { "GoldBag", "goldCoins" }, ChronicleCash.Templates(session.Chronicle, place.Templates),
                     StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < 12; i++)
        {
            var control = new Rng(1000 + i);
            string expected = control.Next() * (double)Rng.Scale * 2 >= 1 ? "goldCoins" : "GoldBag";
            Assert.Equal(expected, ChronicleCash.Choose(ChronicleCash.Templates(session.Chronicle, place.Templates), new Rng(1000 + i)),
                         StringComparer.OrdinalIgnoreCase);
            var bag = DropCash.Bag(place.Steered!, 5)!;
            Assert.True(files.TryRead("3d/models/" + (bag.Model.Contains('.') ? bag.Model : bag.Model + ".nod"), out _), bag.Model);
            seen.Add(bag.Template);
        }
        Assert.Equal(2, seen.Count);
    }

    /// <summary>
    /// With no chronicle loaded  the bag is a `GoldBag` and nothing is
    /// rolled — the branch every earlier test of the coin button stood in.
    /// </summary>
    [Fact]
    public void Without_a_chronicle_the_bag_is_a_GoldBag_and_nothing_is_rolled()
    {
        using var files = Install.Open();
        var (place, _) = Monastery(files, null);
        long before = place.Minds!.Random.Draws;
        Assert.Equal(DropCash.GoldBag, DropCash.Bag(place.Steered!, 5)!.Template, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(before, place.Minds.Random.Draws);
    }

    /// <summary>
    /// The metafile's reader keeps the first three words of the `CASH` line (1.1 reads three),
    /// each only when its template exists: `MP_Salon.nmf` names four and loses the last,
    /// and a name with no template is skipped.
    /// </summary>
    [Fact]
    public void The_cash_table_keeps_three_names_that_are_templates()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var nsc = NscChronicle.Read(files.Read("chronicles/London.nsc"), "London.nsc");
        var salon = NmfFile.Read(files.Read("chronicles/MP_Salon.nmf"), "MP_Salon.nmf");
        Assert.Equal(4, salon.Cash.Count);
        var chronicle = new Chronicle(nsc) { Meta = salon };
        Assert.Equal(new[] { "GoldBag", "goldCoins", "cashUS" }, ChronicleCash.Templates(chronicle, place.Templates),
                     StringComparer.OrdinalIgnoreCase);
        var made = NmfFile.Read("CASH nosuchbag cashUK\n"u8.ToArray(), "x.nmf");
        Assert.Equal(new[] { "cashUK" }, ChronicleCash.Templates(new Chronicle(nsc) { Meta = made }, place.Templates),
                     StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The maximum roll (<c>rand() = 32767</c>) makes the factor a hair over 1, so the pick is one
    /// past the table, where 1.1 asserts on an empty slot; Vitomir takes the last entry (ours).
    /// </summary>
    [Fact]
    public void The_maximum_roll_takes_the_last_entry_rather_than_reading_past_the_table()
    {
        int seed = Enumerable.Range(0, 1_000_000).First(s => new Rng(s).Next() == Rng.Max);
        Assert.Equal("b", ChronicleCash.Choose(["a", "b"], new Rng(seed)));
        Assert.Equal("c", ChronicleCash.Choose(["a", "b", "c"], new Rng(seed)));
        Assert.Equal(ChronicleCash.Fallback, ChronicleCash.Choose([], new Rng(seed)));
    }

    /// <summary>
    /// **A negative music percentage is full volume** (the percentage is read as an unsigned
    /// 32-bit number, so −5 is 4,294,967,291 per cent, held to 1.0). The ambient's
    /// is read the same way and is not held at all. Vitomir held both at 0.
    /// </summary>
    [Fact]
    public void A_negative_music_percentage_is_full_volume_not_silence()
    {
        using var install = new AudioTests.FakeInstall();
        var audio = new GameAudio(install.Files);
        audio.SetMusicLevel(-5);
        Assert.Equal(1f, audio.MusicLevel);
        audio.SetAmbientLevel(-1);
        Assert.True(audio.AmbientLevel > 4e7f);
        audio.SetMusicLevel(60);
        Assert.Equal(0.59999996f, audio.MusicLevel);               // 60 × 0.01 in single precision, as before
    }

    /// <summary>
    /// **The music bar is not ducked** (the music sets level × volume under the ceiling and never looks
    /// Moved during a conversation, the music comes back to its full level until
    /// the duck is next set. Vitomir halved it.
    /// </summary>
    [Fact]
    public void The_music_bar_moved_during_a_conversation_is_not_ducked()
    {
        using var install = new AudioTests.FakeInstall();
        var audio = new GameAudio(install.Files);
        audio.Configure(0.75f, 0.8f);
        Assert.True(audio.StartMusic(audio.Bank.Load("a", stream: true)));
        var c = audio.MusicChannel!;
        audio.Duck(true);
        Assert.Equal(0.4f, c.Volume, 5);
        audio.SetMusicVolume(0.6f);
        Assert.Equal(0.6f, c.Volume, 5);                            // 1.0 × 0.6, no half
        audio.Duck(true);
        Assert.Equal(0.3f, c.Volume, 5);                            // the next duck halves it again
    }

    /// <summary>
    /// **The unstake keeps its root out of the fade** (the unstake's mode adds 0x2a0; the action start turns its 0x200
    /// into a mixer flag; the blend then leaves bone 0's angles alone while the other bones
    /// blend; the weight's end clears it with the weight). Two copies of the same character, one given the
    /// unstake as the paired motion and one plainly: a twentieth of a second in, every bone but the
    /// root is posed alike, and the paired one's root is the animation's own.
    /// </summary>
    [Fact]
    public void The_unstake_turns_its_root_at_once_while_its_limbs_blend()
    {
        using var files = Install.Open();
        var (a, _) = Monastery(files, null);
        var (b, _) = Monastery(files, null);
        var paired = a.Steered!.Animation!;
        var plain = b.Steered!.Animation!;
        paired.PoseBones();
        plain.PoseBones();
        Assert.True(paired.PlayPaired(Stake.UnstakeMode, a.Motions!) > 0);
        Assert.True(plain.Play(Stake.UnstakeMode, b.Motions!, onMotion: false) > 0);
        Assert.True(paired.RootKept);
        Assert.False(plain.RootKept);
        paired.Advance(0.05f);
        plain.Advance(0.05f);
        paired.PoseBones();
        plain.PoseBones();
        Assert.NotEqual(0f, paired.Fade);
        for (int i = 1; i < paired.BoneCount; i++) Assert.Equal(plain.BoneRotation(i), paired.BoneRotation(i));
        Assert.NotEqual(plain.BoneRotation(0), paired.BoneRotation(0));
        paired.Advance(1f);                                         // the fade runs out, and the flag with it
        Assert.Equal(0f, paired.Fade);
        Assert.False(paired.RootKept);
    }

    /// <summary>
    /// **An edge the straight line runs parallel to is crossed at its origin** (0.0 is taken
    /// when the cross product is under 0.001). Vitomir took the half-unit margin point.
    /// A slanted edge is still crossed where the line's plane cuts it.
    /// </summary>
    [Fact]
    public void An_edge_the_line_runs_parallel_to_is_crossed_at_its_origin()
    {
        var origin = new Vector3(100, 40, 0);
        var along = new NilPathLink(0, 0, 80f, Vector3.UnitX, origin);
        Assert.Equal(origin, PathFinder.Cross(along, Vector3.Zero, Vector3.UnitX, 16f));

        var across = new NilPathLink(0, 0, 80f, Vector3.UnitY, new Vector3(100, -40, 0));
        var point = PathFinder.Cross(across, Vector3.Zero, Vector3.UnitX, 16f);
        Assert.Equal(100f, point.X, 3);
        Assert.Equal(0f, point.Y, 3);                               // where y = 0 cuts it: 40 along
    }
}
