// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Audio;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>The CodexSound natives and the foley natives, against the monastery.</summary>
[Collection("scripts")]
public class SoundNativeTests(Scripts s)
{
    static (CodexScenes Scenes, Location Where, GameFiles Files, GameAudio Audio) Enter(Scripts s)
    {
        s.Ready();
        var files = new GameFiles(GameFiles.Locate()!);
        Assert.SkipUnless(files.TryRead("sounds/objects/door_dungeonclose.wav", out _) || File.Exists(
            Path.Combine(GameFiles.Locate()!, "Sounds.nob")), "Sounds.nob is not in this install");
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_MON1")!.Value;
        var location = Location.Load(files, "P1_MON1", sky: false, scene: where.Entry!.SceneName, player: "Christof");
        var audio = new GameAudio(files);
        location.Audio = audio;
        audio.Enter(location);
        audio.Mixer.ListenerPosition = location.Player!.Thing.Position;
        var scenes = new CodexScenes(s.Runtime!) { Chronicles = set };
        scenes.Enter(location);
        return (scenes, location, files, audio);
    }

    static object? Call(Scripts s, string key, object? self, params object?[] args) =>
        s.Engine.get(new object[] { key, self!, args.Select(Wrap).ToArray() });

    static object? Wrap(object? v) => v switch
    {
        int i => java.lang.Integer.valueOf(i),
        float f => java.lang.Float.valueOf(f),
        bool b => java.lang.Boolean.valueOf(b),
        _ => v,
    };

    static int Int(object? o) => ((java.lang.Integer)o!).intValue();
    static float Flt(object? o) => ((java.lang.Float)o!).floatValue();
    static bool Bln(object? o) => ((java.lang.Boolean)o!).booleanValue();

    /// <summary>
    /// `new CodexSound(name, volume, pan, flags, client)`: flat, 0..100 and −100..100 turned into the
    /// mixer's 0..1, and flagged to be let go when it ends.
    /// </summary>
    [Fact]
    public void A_local_sound_plays_flat_at_the_scripts_volume_and_pan()
    {
        var (scenes, _, files, audio) = Enter(s);
        using var _f = files;
        int id = Int(Call(s, "CodexSound.CreateLocal", null, 0, "ui_negative.wav", 50, -100, 0, 0));
        var c = audio.Mixer.Get(id)!;
        Assert.Equal(ChannelKind.Flat, c.Kind);
        Assert.Equal(0.5f, c.Volume);
        Assert.Equal(0f, c.Pan);
        Assert.True((c.Flags & ChannelFlag.ReleaseAtEnd) != 0);
        Assert.Equal(0, Int(Call(s, "CodexSound.CreateLocal", null, 0, "no_such_sound.wav", 50, 0, 0, 0)));
        // The script's own way: `new CodexSound(...)` runs the native in its constructor and keeps
        // the answer as its GUID, and `Stop()` hands that GUID back as the channel.
        Assert.Equal(["java.lang.String", "int", "int", "int", "int"], s.Runtime!.ConstructorTypes("CodexSound")!);
        var sound = s.Runtime.NewScript("CodexSound", 0, ["ui_negative.wav", java.lang.Integer.valueOf(40),
            java.lang.Integer.valueOf(0), java.lang.Integer.valueOf(0), java.lang.Integer.valueOf(0)]);
        int made = s.Runtime.GuidOf(sound);
        Assert.Equal(0.4f, audio.Mixer.Get(made)!.Volume, 5);
        Call(s, "CodexSound.Stop", sound);
        Assert.Null(audio.Mixer.Get(made));
        scenes.Leave();
    }

    [Fact]
    public void A_sound_on_a_thing_follows_it_and_a_stream_cannot_be_put_on_one()
    {
        var (scenes, where, files, audio) = Enter(s);
        using var _f = files;
        var christof = where.Player!.Thing;
        int id = Int(Call(s, "CodexSound.CreateAtThing", null, 0, "ui_negative.wav", 100f, 900f, 75, 0, 0, christof.Guid));
        var c = audio.Mixer.Get(id)!;
        Assert.Same(christof, c.Thing);
        Assert.Equal((100f, 900f), (c.MinDist, c.MaxDist));
        Assert.Equal(0, Int(Call(s, "CodexSound.CreateAtThing", null, 0, "Prague_Petrin", 100f, 900f, 75, 0, 0x400, christof.Guid)));
        scenes.Leave();
    }

    /// <summary>
    /// Music by name, its name back **with its extension** (a stream keeps the name it was asked
    /// for), its volume back as a whole percentage — and a negative volume is 100, because the
    /// native's clamp compares unsigned.
    /// </summary>
    [Fact]
    public void Music_by_name_answers_the_name_it_was_given_and_a_negative_volume_is_full()
    {
        var (scenes, _, files, audio) = Enter(s);
        using var _f = files;
        Assert.SkipUnless(files.TryRead("sounds/music/Prague_Petrin.mp3", out _), "Sounds\\Music is not in this install");
        Assert.True(Bln(Call(s, "CodexSound.StartMusic", null, "Prague_Petrin.mp3", 30)));
        Assert.Equal("Prague_Petrin.mp3", Call(s, "CodexSound.GetMusicName", null)?.ToString());
        // 30 comes back as 29: 30 × 0.01 is stored as 0.29999998, and ×100 truncated is 29. The
        // same happens in 1.1 (a float, truncated), so `PushMusic`/`PopMusic` lose a point.
        Assert.Equal(29, Int(Call(s, "CodexSound.GetMusicVolume", null)));
        Call(s, "CodexSound.SetMusicVolume", null, -5);
        Assert.Equal(100, Int(Call(s, "CodexSound.GetMusicVolume", null)));
        Call(s, "CodexSound.StopMusic", null);
        Assert.Equal("", Call(s, "CodexSound.GetMusicName", null)?.ToString());
        scenes.Leave();
    }

    /// <summary>
    /// `PlayVoice` answers the recording's length in <b>whole</b> seconds, and a line with no
    /// recording is shown for its reading time plus 1,250 ms and answers 0.
    /// </summary>
    [Fact]
    public void A_voice_answers_whole_seconds_and_an_unrecorded_one_is_read_instead()
    {
        var (scenes, where, files, audio) = Enter(s);
        using var _f = files;
        var shown = new List<(string Text, int Ms)>();
        scenes.Subtitles = false;
        scenes.Subtitle = (t, ms) => shown.Add((t, ms));
        scenes.VoiceWords = id => id == "nobody_0_0_0" ? "four words right here" : id;
        var christof = where.Player!.Thing;
        Assert.Equal(0f, Flt(Call(s, "CodexSound.PlayVoice", null, christof.Guid, "nobody_0_0_0", 80)));
        Assert.Equal(("four words right here", 4 * 350 + 500 + 1250), Assert.Single(shown));
        var voice = audio.Bank.Load("Christof_0_0_2194", stream: true);
        Assert.SkipWhen(voice is null, "the voice recordings are not in this install");
        float seconds = Flt(Call(s, "CodexSound.PlayVoice", null, christof.Guid, "Christof_0_0_2194", -80));
        Assert.Equal(voice!.LengthMs / 1000, (int)seconds);
        Assert.Equal(seconds, MathF.Floor(seconds));
        Assert.Single(shown);                                       // subtitles off: nothing more shown
        Assert.True(christof.MouthBusyUntil > 0);
        scenes.Leave();
    }

    [Fact]
    public void A_script_can_change_a_things_foley_and_play_one_of_its_events()
    {
        var (scenes, where, files, audio) = Enter(s);
        using var _f = files;
        var christof = where.Player!.Thing;
        var self = s.Runtime!.NewThingRef("CodexThing", christof.Guid);
        Assert.False(Bln(Call(s, "CodexThing.SetFoley", self, "no_such.nag")));
        Assert.True(Bln(Call(s, "CodexThing.SetFoley", self, "animalBat.nag")));
        Assert.Equal("animalBat.nag", christof.Foley);
        Assert.Equal(0, Int(Call(s, "CodexThing.PlayFoleyMode", self, 151)));
        Assert.NotEqual(0, Int(Call(s, "CodexThing.PlayFoleyMode", self, FoleyEvent.StartMove)));
        scenes.Leave();
    }
}
