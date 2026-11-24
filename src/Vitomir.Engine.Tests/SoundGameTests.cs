// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The sound in the game's own first monastery: its music, its placed sounds, its footsteps, a
/// conversation's voices, and the noises a fight and a barrel make.
/// </summary>
public class SoundGameTests
{
    static (Location Where, GameFiles Files, GameAudio Audio) Monastery()
    {
        var dir = GameFiles.Locate();
        Assert.SkipWhen(dir is null, "no game install");
        var files = new GameFiles(dir!);
        Assert.SkipUnless(File.Exists(Path.Combine(dir!, "Sounds.nob")), "Sounds.nob is not in this install");
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_MON1")!.Value;
        var place = Location.Load(files, "P1_MON1", sky: false, scene: where.Entry!.SceneName, player: "Christof");
        var audio = new GameAudio(files);
        place.Audio = audio;
        audio.Enter(place);
        return (place, files, audio);
    }

    static void Hear(GameAudio audio, Location place, WorldThing from, int frames, float seconds = 1 / 30f)
    {
        for (int i = 0; i < frames; i++)
        {
            place.Milliseconds += (int)(seconds * 1000);
            audio.Frame(seconds, place.Milliseconds, from.Position, Vector3.UnitY, Vector3.UnitX, from.Sector);
        }
    }

    [Fact]
    public void The_monastery_starts_its_music_at_half_under_the_ini_level_and_hangs_its_sounds_on_sectors()
    {
        var (place, files, audio) = Monastery();
        using var _ = files;
        Assert.SkipWhen(audio.Music is null, "Sounds\\Music\\Prague_Petrin.mp3 is not in this install");
        Assert.Equal("Prague_Petrin.mp3", audio.Music!.Name);
        Assert.Equal(0.5f * 0.75f, audio.MusicChannel!.Volume, 5);  // the scene's 50, the default 0.75
        Assert.Null(audio.Ambient);                                 // the monastery names none
        Assert.Equal(["stthomas_amb"], audio.SectorSoundsIn(22).Select(x => x.Sound.Name));
        Assert.Equal(["stthomas_amb"], audio.SectorSoundsIn(27).Select(x => x.Sound.Name));
        Assert.Equal(["wetcave_amb"], audio.SectorSoundsIn(80).Select(x => x.Sound.Name));
        Assert.NotEmpty(audio.PointSoundsIn(7));
    }

    /// <summary>
    /// Walking into a sector with a sound of its own fades that sound in over a second, flat and
    /// looping, and walking out fades it away again.
    /// </summary>
    [Fact]
    public void A_sectors_own_sound_fades_in_over_a_second_and_out_over_another()
    {
        var (place, files, audio) = Monastery();
        using var _ = files;
        var christof = place.Player!.Thing;
        Hear(audio, place, christof, 2);
        var inside = christof.Position;
        long home = christof.Sector;
        christof.Sector = 22;
        Hear(audio, place, christof, 1);
        var hum = audio.SectorSoundsIn(22).First(s => s.Sound.Name == "stthomas_amb");
        var c = audio.Mixer.Get(hum.Channel)!;
        Assert.Equal(ChannelKind.Flat, c.Kind);
        Assert.True((c.Flags & (ChannelFlag.Looping | ChannelFlag.Local)) == (ChannelFlag.Looping | ChannelFlag.Local));
        Assert.True(c.Volume < 0.1f);
        Hear(audio, place, christof, 31);
        Assert.Equal(0.29f, c.Volume, 3);
        christof.Sector = home;
        Hear(audio, place, christof, 32);
        Assert.Null(audio.Mixer.Get(hum.Channel));
        christof.Position = inside;
    }

    /// <summary>
    /// A footfall tag plays the character's own `walk_&lt;floor&gt;` — the floor being the `sound` of
    /// the material under its feet — on the character, and a `Lrun` tag plays exactly the same.
    /// </summary>
    [Fact]
    public void A_footfall_is_the_characters_walk_for_the_floor_under_it_whichever_foot_and_gait()
    {
        var (place, files, audio) = Monastery();
        using var _ = files;
        var christof = place.Player!.Thing;
        Hear(audio, place, christof, 1);
        int floor = KeyframeSounds.Floor(christof);
        Assert.InRange(floor, 0, 9);
        int walk = KeyframeSounds.Sound(christof, new NadTag(0f, KeyframeSounds.Lwalk));
        var c = audio.Mixer.Get(walk)!;
        var set = audio.SetOf(christof)!;
        Assert.Contains(c.Sound, set[FoleyEvent.Walk + floor].Select(e => e.Sound));
        Assert.Same(christof, c.Thing);
        int run = KeyframeSounds.Sound(christof, new NadTag(0f, KeyframeSounds.Rrun));
        Assert.Contains(audio.Mixer.Get(run)!.Sound, set[FoleyEvent.Walk + floor].Select(e => e.Sound));
    }

    /// <summary>
    /// A voiced line plays on the ring, the music ducks for the length of the conversation, and a
    /// skip stops the voice.
    /// </summary>
    [Fact]
    public void A_conversations_voices_play_its_music_ducks_and_a_skip_silences_the_line()
    {
        var (place, files, audio) = Monastery();
        using var _ = files;
        var talk = new Conversations
        {
            Voices = id => audio.Bank.Load(id, stream: true) is { } r ? new ConversationVoice("words", r.LengthMs, r)
                                                                     : new ConversationVoice("words", 0),
        };
        Assert.True(talk.Execute(place, "8_2_Strange", "8_2_Strange", ConversationFlag.Shipped, 1, 1000));
        var c = talk.Current!;
        if (audio.MusicChannel is { } music) Assert.Equal(0.5f * 0.5f * 0.75f, music.Volume, 5);
        c.Tick(1001);
        var voices = c.VoiceChannels.ToArray();
        Assert.SkipWhen(voices.Length == 0, "the monastery's voices are not in this install");
        var line = audio.Mixer.Get(voices[0])!;
        Assert.Equal(ChannelKind.Stream, line.Kind);
        Assert.True(c.Skip(1500));
        Assert.Null(audio.Mixer.Get(voices[0]));
        Assert.Empty(c.VoiceChannels);
        talk.End(place);
        if (audio.MusicChannel is { } after) Assert.Equal(0.5f * 0.75f, after.Volume, 5);
    }

    [Fact]
    public void Opening_a_container_is_heard()
    {
        var (place, files, audio) = Monastery();
        using var _ = files;
        var chest = place.Things.FirstOrDefault(t => t.Type == ThingType.Container && t.Foley.Length > 0);
        Assert.SkipWhen(chest is null, "no container with a foley file here");
        audio.Mixer.ListenerPosition = chest!.Position;
        int before = audio.Mixer.Channels.Count(c => c.Active);
        Assert.True(Container.Open(chest, place.Player!.Thing));
        Assert.Contains(audio.Mixer.Channels, c => c.Active && audio.SetOf(chest)![FoleyEvent.Activate]
                                                                  .Any(e => ReferenceEquals(e.Sound, c.Sound)));
        Assert.True(audio.Mixer.Channels.Count(c => c.Active) > before);
    }

    /// <summary>
    /// Dying is heard twice over — the voice and the fall — and whatever the character was still
    /// saying through a conversation line is cut off. **But the cry is swallowed**: cutting the line
    /// off does not free the mouth (the death stops the channel and leaves the line's end alone), and
    /// a voice on a busy mouth is refused, so somebody killed mid-sentence dies with only the fall.
    /// </summary>
    [Fact]
    public void A_death_is_heard_and_cuts_off_what_the_character_was_saying_but_not_its_cry()
    {
        var (place, files, audio) = Monastery();
        using var _ = files;
        var victims = place.Things.Where(t => t.Actor is { } a && !a.Dead && t.Type == ThingType.Actor
                                              && audio.SetOf(t) is { } s && s[FoleyEvent.VoxDeath].Count > 0
                                              && s[FoleyEvent.Death].Count > 0).Take(2).ToArray();
        Assert.SkipWhen(victims.Length < 2, "not two characters with death noises here");
        foreach (var (victim, talking) in new[] { (victims[0], false), (victims[1], true) })
        {
            audio.Mixer.StopAll();
            audio.Mixer.ListenerPosition = victim.Position;
            int saying = 0;
            if (talking)
            {
                saying = audio.Mixer.Play2D(AudioTests.Tone("line", ms: 5000), 1f, 0.5f, 0);
                victim.MouthBusyUntil = place.Milliseconds + 5000;
                victim.MouthChannel = saying;
            }
            Assert.True(Damage.Kill(victim, null, HitKind.Melee, DamageType.Normal, place.Milliseconds));
            var set = audio.SetOf(victim)!;
            bool Heard(int slot) => audio.Mixer.Channels.Any(c => c.Active && set[slot].Any(e => ReferenceEquals(e.Sound, c.Sound)));
            Assert.True(Heard(FoleyEvent.Death));
            Assert.Equal(!talking, Heard(FoleyEvent.VoxDeath));
            if (talking) Assert.Null(audio.Mixer.Get(saying));
        }
    }

    /// <summary>
    /// A track move to where the thing already is does nothing at all — no move, no noise, and so
    /// no `arrived` for the script — and a real one says `startmove`.
    /// </summary>
    [Fact]
    public void A_move_to_where_a_thing_already_is_is_nothing_at_all()
    {
        var (place, files, _) = Monastery();
        using var __ = files;
        var mover = place.Things.FirstOrDefault(t => t.MoveType == WorldThing.MoveTrack && t.Keyframes.Length > 1);
        Assert.SkipWhen(mover is null, "no keyframed thing here");
        mover!.Position = mover.Keyframes[0].Position;
        Assert.True(mover.MoveToFrame(0, 100f));
        Assert.Null(mover.Motion);
        Assert.True(mover.MoveToFrame(1, 100f));
        Assert.NotNull(mover.Motion);
    }

    /// <summary>
    /// The stance radios on the status bar have the steered character call the order out:
    /// Christof's <c>vox_attackmode</c>. A second press while the first line is still
    /// playing is swallowed by his busy mouth, and in a conversation he says nothing at all.
    /// </summary>
    [Fact]
    public void A_stance_order_is_called_out_by_the_steered_character_when_his_mouth_is_free()
    {
        var (place, files, audio) = Monastery();
        using var _ = files;
        var christof = place.Player!.Thing;
        christof.Foley = "playerChristof.nag";   // the monastery's Christof is still human; OLDT_7_4 swaps this in
        var bar = new Vitomir.Engine.Ui.StatusBar(new Vitomir.Engine.Ui.UiAssets(files).Pane(Vitomir.Engine.Ui.StatusBar.PaneName), place);
        Hear(audio, place, christof, 1);

        bar.Stance(Vitomir.Engine.Ai.PartyStance.Attack);
        var said = audio.Mixer.Channels.Where(c => c.Thing == christof && c.Sound is not null).ToList();
        Assert.Equal("christof_0_0_1717", Assert.Single(said).Sound!.Name, ignoreCase: true);
        int busy = christof.MouthBusyUntil;
        Assert.True(busy > audio.Mixer.GameMs);

        bar.Stance(Vitomir.Engine.Ai.PartyStance.Defense);
        Assert.Equal(busy, christof.MouthBusyUntil);

        Hear(audio, place, christof, (busy - audio.Mixer.GameMs) / 33 + 2);
        christof.Actor!.Flags2 |= Actor.Flag2.Conversation;
        bar.Stance(Vitomir.Engine.Ai.PartyStance.Neutral);
        Assert.Equal(busy, christof.MouthBusyUntil);
        Assert.Equal(Vitomir.Engine.Ai.PartyStance.Neutral, christof.Actor.Stance);
    }
    /// <summary>
    /// The pip beside a party member's bars takes them in or out of the formation, and **the
    /// member** answers for itself: Wilhem's <c>vox_stopfollow</c> out of it — the
    /// "I stand" of 1.1 — and <c>vox_startfollow</c> back in. Regroup and disperse are
    /// called out by the steered character: <c>vox_regroup</c>, and <c>vox_stopfollow</c> again.
    /// </summary>
    [Fact]
    public void Taking_a_companion_out_of_the_formation_has_him_say_so()
    {
        var (place, files, audio) = Monastery();
        using var _ = files;
        var christof = place.Player!.Thing;
        christof.Actor!.Flags |= Actor.Flag.Party;
        var wilhem = place.Place("wilhem", christof.Position + new System.Numerics.Vector3(96, 0, 0), christof.Orientation, christof.Sector)!;
        wilhem.Actor!.Flags |= Actor.Flag.Party;
        wilhem.Actor.Flags2 |= Actor.Flag2.InFormation;
        var bar = new Vitomir.Engine.Ui.StatusBar(new Vitomir.Engine.Ui.UiAssets(files).Pane(Vitomir.Engine.Ui.StatusBar.PaneName), place);
        Hear(audio, place, wilhem, 1);

        bar.Toggle(wilhem.Actor);
        Assert.False(wilhem.Actor.Has2(Actor.Flag2.InFormation));
        var said = audio.Mixer.Channels.Where(c => c.Thing == wilhem && c.Sound is not null).ToList();
        Assert.Equal("wilhem_0_0_1729", Assert.Single(said).Sound!.Name, ignoreCase: true);

        Hear(audio, place, wilhem, (wilhem.MouthBusyUntil - audio.Mixer.GameMs) / 33 + 2);
        bar.Toggle(wilhem.Actor);
        Assert.True(wilhem.Actor.Has2(Actor.Flag2.InFormation));
        Assert.Contains(audio.Mixer.Channels, c => c.Thing == wilhem && c.Sound is { } snd
                        && snd.Name.Equals("wilhem_0_0_1727", StringComparison.OrdinalIgnoreCase));

        // Regroup: the steered character's line. The monastery's Christof is human and his foley
        // has no `vox_regroup`, so give him the vampire's for the test.
        christof.Foley = "playerChristof.nag";
        Hear(audio, place, christof, 1);
        bar.Regroup();
        Assert.Contains(audio.Mixer.Channels, c => c.Thing == christof && c.Sound is not null);
    }
}
