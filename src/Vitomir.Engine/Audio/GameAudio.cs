// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game's sound, as the rest of the engine sees it: the mixer's globals (music, the ambient, the
// ducking), the scene's placed sounds, the foley, and the listener.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Audio;

/// <summary>A scene's sound tied to sectors (a 0x14-byte record).</summary>
public sealed class SectorSound
{
    public required Sound Sound { get; init; }
    /// <summary>The scene's 0..100 as 0..1, and its −100..100 balance as a 0..1 pan.</summary>
    public float Volume { get; init; }
    /// <inheritdoc cref="Volume"/>
    public float Pan { get; init; }
    public uint Flags { get; init; }
    /// <summary>The channel it is on, for as long as it is.</summary>
    public int Channel { get; set; }
    /// <summary>The scene it came from, which a chapter swap takes its own back out of (ChapterSwap.cs).</summary>
    public Vitomir.Formats.NsdScene? From { get; init; }
}

/// <summary>A scene's sound at a point (a 0x28-byte record).</summary>
public sealed class PointSound
{
    public required Sound Sound { get; init; }
    public float Volume { get; init; }
    public float Pan { get; init; }
    public uint Flags { get; init; }
    public Vector3 Position { get; init; }
    public float MinDist { get; init; }
    public float MaxDist { get; init; }
    public int Channel { get; set; }
    /// <inheritdoc cref="SectorSound.From"/>
    public Vitomir.Formats.NsdScene? From { get; init; }
}

/// <summary>
/// Everything that makes a noise, in one place.
/// </summary>
public sealed partial class GameAudio
{
    public SoundBank Bank { get; }
    public Mixer Mixer { get; }
    public FoleySets Foley { get; }

    /// <summary>Ours: reads a place's sounds ahead as it is entered, when set (the game sets it; SoundPrefetch.cs).</summary>
    public SoundPrefetch? Prefetch { get; set; }

    /// <summary>The interface's own set, `ui.nag`, loaded at start-up.</summary>
    public FoleySet? UiSet { get; }

    /// <summary>The engine's one <c>rand()</c>, which a foley pick draws on. Null makes a private one.</summary>
    public Rng Random { get; set; } = Chance.Game?.Sounds ?? new();

    public GameAudio(GameFiles files)
    {
        Bank = new SoundBank(files);
        Mixer = new Mixer { Bank = Bank };
        Foley = new FoleySets(files, Bank);
        UiSet = Foley.Get("ui.nag");
    }

    // --------------------------------------------------------------------------------- music & ambient

    /// <summary>`[mixer] musicVolume`, 0..1.</summary>
    public float MusicVolume { get; private set; } = 0.75f;
    /// <summary>What a scene or script set the music to, 0..1; 1 at start-up.</summary>
    public float MusicLevel { get; private set; } = 1f;
    /// <summary>
    /// What a scene or script set the ambient to — <b>0.25 at start-up</b>, and
    /// never clamped. It matters more than it looks: the ambient is started at whatever this is
    /// <em>before</em> the new scene's own level is applied, and a channel's volume can never be
    /// raised past what it started at. See <see cref="StartAmbient"/>.
    /// </summary>
    public float AmbientLevel { get; private set; } = 0.25f;

    /// <summary>The music playing, and its channel.</summary>
    public Sound? Music { get; private set; }
    /// <summary>The ambient playing, and its channel.</summary>
    public Sound? Ambient { get; private set; }
    int musicChannel = 0xff, ambientChannel = 0xff;

    /// <summary>Music and ambient at half (a conversation, a book).</summary>
    public bool Ducked { get; private set; }
    /// <summary>Music and ambient off (a video).</summary>
    public bool Silenced { get; private set; }

    public Channel? MusicChannel => Mixer.Get(musicChannel);
    public Channel? AmbientChannel => Mixer.Get(ambientChannel);

    /// <summary>The ini's two volumes.</summary>
    public void Configure(float effects, float music)
    {
        Mixer.SetEffectsVolume(Math.Clamp(effects, 0f, 1f));
        MusicVolume = Math.Clamp(music, 0f, 1f);
    }

    /// <summary>
    /// Music: the old track stops, the new one plays flat and looping at
    /// <see cref="MusicLevel"/> × <see cref="MusicVolume"/>, flagged absolute so the effects volume
    /// leaves it alone — and then its ceiling is lifted to 1, so the script can raise it later.
    /// </summary>
    public bool StartMusic(Sound? sound)
    {
        if (sound is null) return false;
        if (MusicChannel is { } old) Mixer.StopChannel(old);
        Music = null;
        int id = Mixer.Play2D(sound, MusicLevel * MusicVolume, 0.5f, ChannelFlag.Looping | ChannelFlag.Absolute);
        if (id == 0) return false;
        musicChannel = id;
        Music = sound;
        if (Mixer.Get(id) is { } c && c.Target != 1f)
        {
            bool nonNegative = c.Volume >= 0f;
            c.Target = 1f;
            c.Volume = nonNegative ? Math.Min(c.Volume, 1f) : 0f;
            c.Flags |= ChannelFlag.DirtyVolume;
        }
        return true;
    }

    /// <summary>`StopMusic`. The name goes too, so <c>GetMusicName</c> answers nothing.</summary>
    public void StopMusic()
    {
        if (MusicChannel is { } c) Mixer.StopChannel(c);
        Music = null;
    }

    /// <summary>
    /// `SetMusicVolume`: 0..100 times 0.01, held to 0..1, and applied under the
    /// ducking. **The percentage is read unsigned** — the original takes it as an unsigned 32-bit
    /// number — so a negative one is some four billion per cent and the music goes to full,
    /// not to silence (a comparison with 1.1; only the console can pass one).
    /// </summary>
    public void SetMusicLevel(int percent)
    {
        MusicLevel = Math.Clamp(Unsigned(percent), 0f, 1f);
        ApplyMusic();
    }

    /// <summary>
    /// A script's or the console's percentage as the music and ambient levels read it: unsigned, times 0.01,
    /// rounded to a float once (the product held exactly first, as in 1.1 and as a double does here).
    /// </summary>
    internal static float Unsigned(int percent) => (float)((double)(uint)percent * Percent);

    /// <summary>A percentage's scale (the float nearest 0.01).</summary>
    const float Percent = 0.01f;

    /// <summary>`GetMusicVolume`: the level as a whole percentage.</summary>
    public int MusicPercent => (int)(MusicLevel * 100f);

    /// <summary>
    /// The ambient: one looping flat sound at the <b>current</b>
    /// <see cref="AmbientLevel"/> — before a scene's own level is set. It is not absolute, so the
    /// effects volume scales it.
    /// </summary>
    public bool StartAmbient(Sound? sound)
    {
        if (sound is null) return false;
        if (AmbientChannel is { } old) Mixer.StopChannel(old);
        Ambient = null;
        int id = Mixer.Play2D(sound, AmbientLevel, 0.5f, ChannelFlag.Looping);
        ambientChannel = id;
        Ambient = id != 0 ? sound : null;
        return id != 0;
    }

    /// <summary>`StopAmbient`.</summary>
    public void StopAmbient()
    {
        if (AmbientChannel is { } c) Mixer.StopChannel(c);
        Ambient = null;
    }

    /// <summary>
    /// `SetAmbientVolume`: 0..100 as 0..1, <b>not</b> clamped, applied under the ceiling —
    /// and read unsigned like the music's, so a negative percentage is a huge level that
    /// the channel's ceiling cuts to its own (a comparison with 1.1).
    /// </summary>
    public void SetAmbientLevel(int percent)
    {
        AmbientLevel = Unsigned(percent);
        ApplyAmbient();
    }

    /// <summary>`GetAmbientVolume`.</summary>
    public int AmbientPercent => (int)(AmbientLevel * 100f);

    /// <summary>
    /// The player's music volume, from the Sound screen's bar: held to 0..1 and put on the
    /// music playing at once as level × volume under its ceiling (the music's rule) — **with neither
    /// the duck nor the silence**: the slider's setter does not test the duck or the silence as
    /// the level does, so moving the bar during a conversation brings the music back to full until the
    /// duck is next set (a comparison with 1.1; Vitomir had ducked it).
    /// </summary>
    public void SetMusicVolume(float volume)
    {
        MusicVolume = Math.Clamp(volume, 0f, 1f);
        if (MusicChannel is { } c) Mixer.SetVolume(c, MusicLevel * MusicVolume);
    }

    void ApplyMusic()
    {
        if (MusicChannel is not { } c) return;
        float v = Ducked ? MusicVolume * MusicLevel * 0.5f : Silenced ? 0f : MusicVolume * MusicLevel;
        Mixer.SetVolume(c, v);
    }

    void ApplyAmbient()
    {
        if (AmbientChannel is not { } c) return;
        float v = Ducked ? AmbientLevel * 0.5f : Silenced ? 0f : AmbientLevel;
        Mixer.SetVolume(c, v);
    }

    /// <summary>
    /// Half volume for the music and the ambient, which a conversation and a book ask
    /// for in a single-player game. Both levels go through a whole percentage on the way
    /// (<c>(int)(level × 100) × 0.01</c>), which is where a level like 0.333 becomes 0.33.
    /// </summary>
    public void Duck(bool on)
    {
        Ducked = on;
        MusicLevel = Math.Clamp((int)(MusicLevel * 100f) * 0.01f, 0f, 1f);
        ApplyMusic();
        AmbientLevel = (int)(AmbientLevel * 100f) * 0.01f;
        ApplyAmbient();
    }

    /// <summary>
    /// Music and ambient off, or back on. Back on also <b>restarts the music</b> when
    /// its channel has gone — which is what keeps the last level's music playing in a level that
    /// names none: the level change stops every channel, and the next level's start calls this.
    /// </summary>
    public void Silence(bool on)
    {
        Silenced = on;
        MusicLevel = Math.Clamp((int)(MusicLevel * 100f) * 0.01f, 0f, 1f);
        ApplyMusic();
        AmbientLevel = (int)(AmbientLevel * 100f) * 0.01f;
        ApplyAmbient();
        if (Music is { } music && MusicChannel is null)
        {
            Music = null;
            StartMusic(music);
        }
    }

    // ------------------------------------------------------------------------------------ the scene

    readonly Dictionary<long, List<SectorSound>> sectorSounds = [];
    readonly Dictionary<long, List<PointSound>> pointSounds = [];
    long lastSector = -1, currentSector = -1;

    /// <summary>The scene sounds a sector has, newest first.</summary>
    public IReadOnlyList<SectorSound> SectorSoundsIn(long sector) => sectorSounds.TryGetValue(sector, out var l) ? l : [];
    /// <inheritdoc cref="SectorSoundsIn"/>
    public IReadOnlyList<PointSound> PointSoundsIn(long sector) => pointSounds.TryGetValue(sector, out var l) ? l : [];

    /// <summary>The sector the listener was last heard in.</summary>
    public long ListenerSector => currentSector;

    /// <summary>
    /// Forgets where the listener was (zeroed), so the next
    /// frame takes its sector as newly entered and starts that sector's loops again. The game does it
    /// on its way back into play  — after a film, whose start stopped every channel.
    /// </summary>
    public void ForgetSector() => lastSector = currentSector = -1;

    /// <summary>
    /// A level starting (the level start's sound half): everything stops, the scene's
    /// placed sounds are read into their sectors, the music and the ambient of the scene in force
    /// start, and the silence comes off.
    /// </summary>
    public void Enter(Location place)
    {
        Mixer.StopAll();
        surfaces.Clear();
        sectorSounds.Clear();
        pointSounds.Clear();
        lastSector = currentSector = -1;
        Bound = place;                                                    // whose placed sounds these are (ChapterSwap.cs)
        Silence(false);                                                   // before the music
        if (place.WorldScene is { } world) Bind(world);
        if (place.ChapterScene is { } chapter) Bind(chapter);
        Prefetch?.Place(place);                                           // ours: its things' foley read ahead (SoundPrefetch.cs)
        var rules = place.ChapterScene ?? place.WorldScene;
        if (rules is null) return;
        if (rules.Music.Length > 0 && Bank.Load(rules.Music, stream: true) is { } music)
        {
            SetMusicLevel((int)rules.Volume);                             // the level first, then the play
            StartMusic(music);
        }
        if (rules.Ambient.Length > 0 && Bank.Load(rules.Ambient, stream: true) is { } ambient)
        {
            StartAmbient(ambient);
            SetAmbientLevel((int)rules.AmbientVolume);
        }
    }

    /// <summary>Stop everything and forget the placed sounds — a level going away.</summary>
    public void Leave()
    {
        Mixer.StopAll();
        sectorSounds.Clear();
        pointSounds.Clear();
        lastSector = currentSector = -1;
    }

    /// <summary>The scene file's sound section: each sound loaded as an effect and hung on its sectors.</summary>
    void Bind(NsdScene scene)
    {
        foreach (var s in scene.SectorSounds)
        {
            if (Bank.Load(s.Wav) is not { } sound) continue;
            var record = new SectorSound
            {
                Sound = sound, Volume = s.Volume * 0.01f, Pan = (s.Balance + 100) * 0.005f, Flags = (uint)s.Flags, From = scene,
            };
            foreach (var sector in s.Sectors)
                (sectorSounds.TryGetValue(sector, out var l) ? l : sectorSounds[sector] = []).Insert(0, record);
        }
        foreach (var p in scene.PointSounds)
        {
            if (Bank.Load(p.Wav) is not { } sound) continue;
            var record = new PointSound
            {
                Sound = sound, Volume = p.Volume * 0.01f, Pan = (p.Balance + 100) * 0.005f, Flags = (uint)p.Flags,
                Position = p.Position, MinDist = p.MinDist, MaxDist = p.MaxDist, From = scene,
            };
            foreach (var sector in p.Sectors)
                (pointSounds.TryGetValue(sector, out var l) ? l : pointSounds[sector] = []).Insert(0, record);
        }
    }

    /// <summary>
    /// One frame: the listener moves; on a change of sector the old sector's sounds fade
    /// out over a second and the new one's fade in over a second; the new sector's point sounds
    /// start when the listener is inside their far distance; and then every channel is updated.
    /// </summary>
    /// <param name="sector">The listener's sector, or −1 for none, which skips everything but the channels.</param>
    public void Frame(float seconds, int nowMs, Vector3 position, Vector3 forward, Vector3 right, long sector)
    {
        Mixer.GameMs = nowMs;
        Mixer.Now = nowMs;
        if (sector >= 0)
        {
            currentSector = sector;
            Mixer.ListenerPosition = position;
            Mixer.ListenerForward = forward;
            Mixer.ListenerRight = right;
            if (sector != lastSector)
            {
                var now = SectorSoundsIn(sector);
                foreach (var old in SectorSoundsIn(lastSector))
                {
                    if (now.Contains(old)) continue;
                    if (Mixer.Get(old.Channel) is { } c) Mixer.Fade(c, -c.Volume);
                }
                foreach (var s in now)
                {
                    if (Mixer.Get(s.Channel) is { } playing) { Mixer.Fade(playing, playing.Target - playing.Volume); continue; }
                    int id = Mixer.Play2D(s.Sound, s.Volume, s.Pan, s.Flags | ChannelFlag.Looping | ChannelFlag.Local);
                    s.Channel = id;
                    if (Mixer.Get(id) is not { } c) continue;
                    if (c.Volume != 0f) { c.Volume = c.Target < 0f ? c.Target : 0f; c.Flags |= ChannelFlag.DirtyVolume; }
                    Mixer.Fade(c, c.Target - c.Volume);
                }
            }
            foreach (var p in PointSoundsIn(sector))
            {
                if (Vector3.Distance(position, p.Position) >= p.MaxDist) continue;
                if (Mixer.Get(p.Channel) is not null) continue;
                p.Channel = Mixer.PlayAtPoint(p.Sound, p.Position, p.MaxDist, p.MinDist, p.Volume,
                                              p.Flags | ChannelFlag.LinkToPos | ChannelFlag.Looping);
            }
        }
        Mixer.Update(seconds);
        if (sector >= 0) lastSector = sector;
    }

    // ------------------------------------------------------------------------------------ foley

    readonly Dictionary<(int, int), int> surfaces = [];

    /// <summary><see cref="KeyframeSounds.SurfaceSound"/>, remembered per surface for the level.</summary>
    public int SurfaceSound(Location place, int sector, int surface)
    {
        if (!surfaces.TryGetValue((sector, surface), out int sound))
            surfaces[(sector, surface)] = sound = KeyframeSounds.SurfaceSound(place, sector, surface);
        return sound;
    }

    /// <summary>The thing's own set, loaded on first use from its <see cref="WorldThing.Foley"/>.</summary>
    public FoleySet? SetOf(WorldThing thing) => thing.Foley.Length > 0 ? Foley.Get(thing.Foley) : null;

    /// <summary>
    /// A thing makes a noise: its own set, unless it has none or is flagged
    /// <see cref="WorldThing.Flag.Silent"/>; placed on <paramref name="at"/> when given — a blow
    /// can sound from the attacker's set but on the victim — and on the thing itself otherwise.
    /// Answers the channel, 0 for silence.
    /// </summary>
    public int Thing(WorldThing thing, int slot, WorldThing? at = null)
    {
        if ((thing.Flags & WorldThing.Flag.Silent) != 0) return 0;
        if (SetOf(thing) is not { } set) return 0;
        return Play(set, at ?? thing, slot);
    }

    /// <summary>
    /// A set's event: a random pick when there is more than one entry (and only then is
    /// <c>rand()</c> drawn), nothing for a thing that is not in the world, positional when the entry
    /// says so and there is a thing to put it on, flat otherwise. The pitch is the entry's rate times
    /// the recording's own rate.
    /// </summary>
    public int Play(FoleySet set, WorldThing? thing, int slot)
    {
        var list = set[slot];
        if (list.Count == 0) return 0;
        var entry = list[0];
        if (list.Count > 1) entry = list[Random.Next() % list.Count];
        if (thing is not null && !thing.InWorld) return 0;               // "another world" — answers 1, plays nothing
        int rate = (int)(entry.RateScale * (entry.Sound.Rate > 0 ? entry.Sound.Rate : Miles.OutputRate));
        if ((entry.Flags & (ChannelFlag.LinkToPos | ChannelFlag.LinkToThing)) != 0 && thing is not null)
            return Mixer.PlayAtThing(entry.Sound, thing, entry.MaxDist, entry.MinDist, entry.Volume, entry.Flags, rate);
        return Mixer.Play2D(entry.Sound, entry.Volume, FoleyEntry.Pan, entry.Flags, rate);
    }

    /// <summary>
    /// A weapon's event from its own set (a weapon is a thing in the engine, with its own foley),
    /// placed on whoever is holding it. Nothing for a weapon with no set.
    /// </summary>
    public int Weapon(Rpg.Weapon? weapon, WorldThing holder, int slot) =>
        weapon is { Foley.Length: > 0 } && Foley.Get(weapon.Foley) is { } set ? Play(set, holder, slot) : 0;

    /// <summary>
    /// The swing a strike starts with: <c>swing2</c> for the second attack when the set
    /// has one that plays, <c>swing</c> otherwise.
    /// </summary>
    public int Swing(Rpg.Weapon? weapon, WorldThing holder)
    {
        if (weapon is null) return 0;
        if (weapon.CurrentAttack != 0 && Weapon(weapon, holder, FoleyEvent.Swing2) is var id and not 0) return id;
        return Weapon(weapon, holder, FoleyEvent.Swing);
    }

    /// <summary>An interface noise: `ui.nag`, always flat.</summary>
    public int Ui(int slot) => UiSet is { } set ? Play(set, null, slot) : 0;

    /// <summary>
    /// A button's noise (the interface library's sound hook): a press is
    /// <c>BUTTONDEPRESS</c> and a release <c>BUTTONRELEASE</c> for a button, a check box and a
    /// discipline slot; a list, a scroll bar and a slider only make the press.
    /// </summary>
    public int Control(string elementType, bool release)
    {
        bool both = elementType is "button" or "checkbox" or "disciplineslot";
        bool pressOnly = elementType is "textlistbox" or "scrollbar" or "scrollslider";
        if (!release && (both || pressOnly)) return Ui(FoleyEvent.ButtonDepress);
        if (release && both) return Ui(FoleyEvent.ButtonRelease);
        return 0;
    }
}

/// <summary>The engine's one-line way to make a thing make its noise.</summary>
public static class Noise
{
    /// <summary>`gameThing::PlayFoley` through the thing's place: nothing when it has no sound.</summary>
    public static int Foley(WorldThing? thing, int slot, WorldThing? at = null)
    {
        Net.HostSeen.Foley(thing, slot, at);                  // relayed to the players' machines on a host
        return thing?.Place?.Audio is { } audio ? audio.Thing(thing, slot, at) : 0;
    }

    /// <summary>
    /// Which <c>hit…</c> a blow on <paramref name="target"/> plays (for a swing, and for
    /// a shot): the target's own sound type when it was hurt, and otherwise — the blow glanced off —
    /// its shield's, else its armour's, else its own.
    /// </summary>
    public static int Struck(WorldThing target, float taken) =>
        FoleyEvent.HitFlesh + (taken > 0f ? target.SoundType
            : target.Actor?.Offhand?.SoundType ?? target.Actor?.Armor?.SoundType ?? target.SoundType);

    /// <summary>A weapon's own noise, on its holder.</summary>
    public static int Weapon(Rpg.Weapon? weapon, WorldThing holder, int slot) =>
        holder.Place?.Audio is { } audio ? audio.Weapon(weapon, holder, slot) : 0;

    /// <summary>The swing a strike starts with.</summary>
    public static int Swing(Rpg.Weapon? weapon, WorldThing holder) =>
        holder.Place?.Audio is { } audio ? audio.Swing(weapon, holder) : 0;

    /// <summary>
    /// Whether a character may start a spoken line: it is not in a conversation
    /// (second flag word bit 8) and its mouth is free — the last voice line it started has run out.
    /// The third condition, that the game is in play (state 3 or 4), always holds
    /// where this is asked. Without sound nobody speaks.
    /// </summary>
    public static bool CanSpeak(WorldThing? thing) =>
        thing?.Place?.Audio is { } audio && thing.Actor is { } actor
        && !actor.Has2(Actor.Flag2.Conversation) && (uint)thing.MouthBusyUntil < (uint)audio.Mixer.GameMs;

    /// <summary>An interface noise through a place.</summary>
    public static int Ui(Location? place, int slot) => place?.Audio is { } audio ? audio.Ui(slot) : 0;
}
