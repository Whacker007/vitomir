// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The sound natives: CodexSound (table 14), and the three foley natives on CodexThing and
// CodexActor. All of them go through the place's GameAudio, so a place with none is silent and every
// native answers what it answers when a sound will not load.
using Vitomir.Engine.Audio;
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>
    /// The words for a voice identifier, for a <c>PlayVoice</c> that
    /// has to be read rather than heard. The engine answers the identifier itself when the string
    /// table has no such key.
    /// </summary>
    public Func<string, string>? VoiceWords { get; set; }

    /// <summary>
    /// A line for the standalone subtitle (`vuiSubtitle` at y=340): the words and how long they
    /// stay up. <c>PlayVoice</c> puts one up whenever the line has no recording, and for every line
    /// when subtitles are on.
    /// </summary>
    public Action<string, int>? Subtitle { get; set; }

    /// <summary>The engine's subtitle setting (bit 0 of its options).</summary>
    public bool Subtitles { get; set; } = true;

    GameAudio? Audio => location?.Audio;

    void RegisterSound()
    {
        // new CodexSound(name, volume, pan, flags, clientGuid) — flat, and let go when it ends.
        // Volume 0..100, pan −100..100. Flag 0x400 asks for a stream. The last argument
        // is not read.
        Engine.On("CodexSound.CreateLocal", (_, a) =>
        {
            if (Audio is not { } audio) return CodexHost.Box(0);
            float volume = Math.Clamp(CodexHost.Int(a, 2), 0, 100) * 0.01f;
            float pan = (Math.Clamp((float)CodexHost.Int(a, 3), -100f, 100f) + 100f) * 0.005f;
            uint flags = (uint)CodexHost.Int(a, 4) | ChannelFlag.ReleaseAtEnd;
            var sound = audio.Bank.Load(CodexHost.Str(a, 1) ?? "", stream: (flags & ChannelFlag.AsStream) != 0);
            if (sound is null) return CodexHost.Box(0);
            int id = audio.Mixer.Play2D(sound, volume, pan, flags);
            if (id == 0) audio.Bank.Release(sound);
            return CodexHost.Box(id);
        });
        // new CodexSound(name, minDist, maxDist, volume, pan, flags, float[] position).
        // Only in the world being played; the pan is not read.
        Engine.On("CodexSound.CreateAtPos", (_, a) =>
        {
            if (Audio is not { } audio) return CodexHost.Box(0);
            float min = CodexHost.Float(a, 2), max = CodexHost.Float(a, 3);
            float volume = Math.Clamp(CodexHost.Int(a, 4), 0, 100) * 0.01f;
            uint flags = (uint)CodexHost.Int(a, 6) | ChannelFlag.ReleaseAtEnd;
            var sound = audio.Bank.Load(CodexHost.Str(a, 1) ?? "", stream: (flags & ChannelFlag.AsStream) != 0);
            if (sound is null) return CodexHost.Box(0);
            int id = audio.Mixer.PlayAtPoint(sound, Vec(a, 7), max, min, volume, flags);
            if (id == 0) audio.Bank.Release(sound);
            return CodexHost.Box(id);
        });
        // new CodexSound(name, minDist, maxDist, volume, pan, flags, thingGuid): on the
        // thing, following it, if it is in the world being played.
        Engine.On("CodexSound.CreateAtThing", (_, a) =>
        {
            // On a host the sound goes to the players' machines too (message 0x4c).
            if (location?.Thing(CodexHost.Int(a, 7)) is { InWorld: true } heard)
                Vitomir.Engine.Net.HostSeen.Sound(heard, CodexHost.Str(a, 1) ?? "", CodexHost.Float(a, 2), CodexHost.Float(a, 3),
                    Math.Clamp(CodexHost.Int(a, 4), 0, 100) * 0.01f, (uint)CodexHost.Int(a, 6) | ChannelFlag.ReleaseAtEnd);
            if (Audio is not { } audio) return CodexHost.Box(0);
            if (location?.Thing(CodexHost.Int(a, 7)) is not { InWorld: true } thing) return CodexHost.Box(0);
            float min = CodexHost.Float(a, 2), max = CodexHost.Float(a, 3);
            float volume = Math.Clamp(CodexHost.Int(a, 4), 0, 100) * 0.01f;
            uint flags = (uint)CodexHost.Int(a, 6) | ChannelFlag.ReleaseAtEnd;
            var sound = audio.Bank.Load(CodexHost.Str(a, 1) ?? "", stream: (flags & ChannelFlag.AsStream) != 0);
            if (sound is null) return CodexHost.Box(0);
            int id = audio.Mixer.PlayAtThing(sound, thing, max, min, volume, flags);
            if (id == 0) audio.Bank.Release(sound);
            return CodexHost.Box(id);
        });
        // sound.Stop(): the object's own GUID is the channel id.
        Engine.On("CodexSound.Stop", (self, _) => { Audio?.Mixer.Stop(runtime.GuidOf(self)); });

        // Music: a stream, and the volume is applied after the play, held to 100.
        Engine.On("CodexSound.StartMusic", (_, a) =>
        {
            if (Audio is not { } audio) return CodexHost.Box(false);
            var sound = audio.Bank.Load(CodexHost.Str(a, 0) ?? "", stream: true);
            if (sound is null) return CodexHost.Box(false);
            audio.StartMusic(sound);
            audio.SetMusicLevel((int)Math.Min((uint)CodexHost.Int(a, 1), 100u));   // unsigned: a negative is 100
            return CodexHost.Box(true);
        });
        Engine.On("CodexSound.StopMusic", (_, _) => { Audio?.StopMusic(); });
        Engine.On("CodexSound.GetMusicName", (_, _) => Audio?.Music?.Name ?? "");
        Engine.On("CodexSound.SetMusicVolume", (_, a) =>
        {
            Audio?.SetMusicLevel((int)Math.Min((uint)CodexHost.Int(a, 0), 100u));
        });
        Engine.On("CodexSound.GetMusicVolume", (_, _) => CodexHost.Box(Audio?.MusicPercent ?? 0));
        Engine.On("CodexSound.StartAmbient", (_, a) =>
        {
            if (Audio is not { } audio) return CodexHost.Box(false);
            var sound = audio.Bank.Load(CodexHost.Str(a, 0) ?? "", stream: true);
            if (sound is null) return CodexHost.Box(false);
            audio.StartAmbient(sound);
            audio.SetAmbientLevel((int)Math.Min((uint)CodexHost.Int(a, 1), 100u));
            return CodexHost.Box(true);
        });
        Engine.On("CodexSound.StopAmbient", (_, _) => { Audio?.StopAmbient(); });
        Engine.On("CodexSound.GetAmbientName", (_, _) => Audio?.Ambient?.Name ?? "");
        Engine.On("CodexSound.SetAmbientVolume", (_, a) =>
        {
            Audio?.SetAmbientLevel((int)Math.Min((uint)CodexHost.Int(a, 0), 100u));
        });
        Engine.On("CodexSound.GetAmbientVolume", (_, _) => CodexHost.Box(Audio?.AmbientPercent ?? 0));
        Engine.On("CodexSound.PlayVoice", (_, a) => CodexHost.Box(PlayVoice(CodexHost.Int(a, 0), CodexHost.Str(a, 1) ?? "",
                                                                            CodexHost.Int(a, 2))));

        // CodexThing.SetFoley(name): the new set if it loads, and the answer
        // says whether it did. A name the thing already has is kept without a reload.
        Engine.On("CodexThing.SetFoley", (self, a) =>
        {
            if (Of(self) is not { } thing) return CodexHost.Box(false);
            var name = CodexHost.Str(a, 0) ?? "";
            if (string.Equals(thing.Foley, name, StringComparison.OrdinalIgnoreCase)) return CodexHost.Box(true);
            if (Audio is not { } audio || audio.Foley.Get(name) is null) return CodexHost.Box(false);
            thing.Foley = name;
            return CodexHost.Box(true);
        });
        // PlayFoleyMode(slot), and the character-only PlayActorFoleyMode: the
        // channel, or 0 for anything out of range, a thing with no set, or silence.
        Engine.On("CodexThing.PlayFoleyMode", (self, a) => CodexHost.Box(PlayFoley(Of(self), CodexHost.Int(a, 0), actorOnly: false)));
        Engine.On("CodexActor.PlayActorFoleyMode", (self, a) => CodexHost.Box(PlayFoley(Of(self), CodexHost.Int(a, 0), actorOnly: true)));
    }

    int PlayFoley(WorldThing? thing, int slot, bool actorOnly)
    {
        if (thing is null || slot is < 0 or >= Formats.Nag.Slots) return 0;
        if (actorOnly && thing.Type is not (ThingType.Actor or ThingType.Player)) return 0;
        return Audio?.Thing(thing, slot, thing) ?? 0;
    }

    /// <summary>
    /// `CodexSound.PlayVoice`: a line said by somebody, outside any conversation. It is
    /// looked up as a stream; when there is no recording — or subtitles are on — the words go up
    /// on the standalone subtitle for the recording's length plus <b>1,250 ms</b> (the reading time
    /// plus 1,250 when there is nothing to hear). A recording plays flat at the absolute volume,
    /// held to 100, on a character's mouth. The answer is its length in <b>whole seconds</b> — the
    /// engine divides the milliseconds by 1,000 as integers before it converts — and 0 for no
    /// recording.
    /// </summary>
    float PlayVoice(int speakerGuid, string voiceId, int volume)
    {
        if (location?.Thing(speakerGuid) is not { } speaker) return 0f;
        string words = VoiceWords?.Invoke(voiceId) ?? voiceId;
        var sound = Audio?.Bank.Load(voiceId, stream: true);
        if (sound is null || Subtitles)
            Subtitle?.Invoke(words, sound is null
                ? Vitomir.Engine.Rpg.Conversation.ReadingTime(words) + PlayVoiceHoldMs
                : sound.LengthMs + PlayVoiceHoldMs);
        if (sound is null || Audio is not { } audio) return 0f;
        int id = audio.Mixer.Play2D(sound, Math.Min(Math.Abs(volume), 100) * 0.01f, 0.5f, 0);
        if (id == 0) return 0f;
        if (speaker.Type is ThingType.Actor or ThingType.Player)
        {
            speaker.MouthBusyUntil = (location?.Milliseconds ?? 0) + sound.LengthMs;
            speaker.MouthChannel = id;
        }
        return (uint)sound.LengthMs / 1000u;
    }

    /// <summary>What <c>PlayVoice</c> adds to how long its subtitle stays up.</summary>
    public const int PlayVoiceHoldMs = 1250;
}
