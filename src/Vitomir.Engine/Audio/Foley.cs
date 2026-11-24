// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a thing sounds like, event by event.
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.Audio;

/// <summary>One recording an event can pick (a 0x24-byte entry).</summary>
public sealed record FoleyEntry(Sound Sound, uint Flags, float Volume, float MaxDist, float MinDist, float RateScale)
{
    /// <summary>A foley line has no way to say a pan; every entry is centred.</summary>
    public const float Pan = 0.5f;
}

/// <summary>
/// A loaded `.nag`: for each of the 151 event slots, the recordings it can play. The engine files a
/// slot's entries <b>newest first</b> — each line is pushed onto the front of its list — and a line
/// whose recording will not load is dropped as it is read.
/// </summary>
public sealed class FoleySet
{
    public required string Name { get; init; }
    readonly List<FoleyEntry>[] slots = new List<FoleyEntry>[Nag.Slots];

    /// <summary>The entries for a slot, in the engine's order (last line first). Empty for none.</summary>
    public IReadOnlyList<FoleyEntry> this[int slot] => slot is >= 0 and < Nag.Slots && slots[slot] is { } l ? l : [];

    internal void Push(int slot, FoleyEntry entry) => (slots[slot] ??= []).Insert(0, entry);

    /// <summary>How many slots have anything in them.</summary>
    public int Used => slots.Count(s => s is { Count: > 0 });
}

/// <summary>
/// The foley sets, by file name. A set is loaded once and shared. The engine counts its
/// owners and keeps the last 32 unowned sets in a ring before freeing one; nothing about that can be
/// heard, so here a set simply stays.
/// </summary>
public sealed class FoleySets(GameFiles files, SoundBank bank)
{
    readonly Dictionary<string, FoleySet?> byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The set, or null when no file of that name opens (the engine answers null too).</summary>
    public FoleySet? Get(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = Key(name);
        if (byName.TryGetValue(key, out var hit)) return hit;
        FoleySet? made = null;
        if (Open(key, out var data)) made = Build(key, data);
        byName[key] = made;
        return made;
    }

    /// <summary>The name a set is filed and opened by: at most 31 characters.</summary>
    public static string Key(string name) => name.Length > 31 ? name[..31] : name;

    /// <summary>Whether a set of that name has been asked for already, loaded or found missing (read ahead: SoundPrefetch.cs).</summary>
    public bool Has(string name) => byName.ContainsKey(Key(name));

    /// <summary>The effect folders (resource kind 0xb), which is where `misc/Foley` is, then the bare name.</summary>
    bool Open(string name, out byte[] data)
    {
        foreach (var dir in SoundBank.EffectFolders)
            if (files.TryRead(dir + "/" + name, out data)) return true;
        return files.TryRead(name, out data);
    }

    FoleySet Build(string name, byte[] data)
    {
        var set = new FoleySet { Name = name };
        foreach (var e in Nag.Read(System.Text.Encoding.Latin1.GetString(data)))
            if (bank.Load(e.Sound) is { } sound)                                  // An effect, never a stream
                set.Push(e.Event, new FoleyEntry(sound, e.Flags, e.Volume, e.MaxDist, e.MinDist, e.Rate));
        return set;
    }
}

/// <summary>The event slots the engine plays by number (the order of <see cref="Nag.ThingEvents"/>).</summary>
public static class FoleyEvent
{
    public const int Create = 0, Activate = 1, StartMove = 2, StopMove = 3, Moving = 4, Swing = 5, Swing2 = 6,
                     HitFlesh = 7, OutOfAmmo = 17, Drop = 18, Pickup = 19, Wear = 20, Remove = 21, Use = 22,
                     Kiss = 23, KissAttack = 24, Detach = 25, Suck = 26, BloodPointDrain = 27,
                     Walk = 28, Run = 37, Scuff = 46,
                     Damage = 55, Death = 65, DeathBodyFall = 75, DeathTorpor = 76, DeathAwaken = 77,
                     AnimIdle = 78, AnimIdle2 = 79,
                     VoxAlert = 80, VoxGreet = 81, VoxFear = 82, VoxBored = 83, VoxTaunt = 84, VoxWorried = 85,
                     VoxAttack = 86, VoxCritical = 87, VoxFrenzy = 88, VoxUnfrenzy = 89, VoxNearFrenzy = 90,
                     VoxDamage = 91, VoxDeath = 101, Staked = 111, Unstaked = 112, VoxStaked = 113,
                     VoxUnstaked = 114, Flap = 115,
                     VoxLostHumanity = 116, VoxLostAllHumanity = 117, VoxGainHumanity = 118, VoxLowHumanity = 119,
                     VoxNotEnoughBlood = 120, VoxLowBlood = 121, VoxEndFeed = 122, VoxEndFeedWrist = 123,
                     VoxEndFeedKill = 124, VoxEndFeedKillInnocent = 125, VoxCannotFeed = 126, VoxSated = 127,
                     VoxFeedChase = 128, VoxPartyFeedOk = 129, VoxPartyFeedNo = 130, VoxPartyFeedStop = 131,
                     VoxRegroup = 132, VoxStartFollow = 133, VoxStopFollow = 134, VoxAttackMode = 136, VoxNeutral = 137,
                     VoxDefendMode = 138, VoxAttackModeOk = 139, VoxNeutralOk = 140,
                     VoxDefendModeOk = 141, VoxInventoryFull = 144, VoxCannotAfford = 145, VoxNewQuest = 146,
                     VoxQuestComplete = 147, VoxNewPlayer = 148, VoxGotCash = 135,
                     VoxCannotWear = 149, VoxWakeup = 150;

    /// <summary>The interface events `ui.nag` fills (the same slots, counted from 0 again).</summary>
    public const int ButtonDepress = 0, ButtonRelease = 1, ButtonRollover = 2, Negative = 5, CharacterChange = 12,
                     NewDiscipline = 13, DisciplineSelect = 14, StMessage1 = 17, NewRank = 19;

    /// <summary>
    /// The shop's four interface sounds (`ui.nag` slots 7–11). **Which one plays when is
    /// 1.1's, and it looks swapped**: a purchase that goes through plays <see cref="CantBuy"/>
    /// and one refused for want of money plays <see cref="AfterPurchaseDa"/>/<see cref="AfterPurchaseMd"/>.
    /// The sale's are the right way round.
    /// </summary>
    public const int AfterPurchaseDa = 7, AfterPurchaseMd = 8, AfterSellDa = 9, AfterSellMd = 10, CantBuy = 11;

    /// <summary>The slot for an event name, or −1.</summary>
    public static int Of(string name) => Nag.EventSlot(name);
}
