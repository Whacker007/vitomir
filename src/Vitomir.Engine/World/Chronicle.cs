// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A chronicle as the running game sees it: the parsed file, where the player is, and the
// thousand-odd story flags the scripts set as they go.
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// The campaign's map of places and the doors between them, plus the state the scripts keep on it:
/// which exits are shut, which location flags are set, and the chronicle flag bits that remember
/// what the player has already done. The CodexSequence natives are all views of this.
/// </summary>
public sealed class Chronicle
{
    public NscChronicle Data { get; }
    /// <summary>The metafile's contents, when it could be read.</summary>
    public NmfFile? Meta { get; init; }
    public string Name => Data.Name;

    /// <summary>Where the player is, as an index into the location list. −1 before anywhere is entered.</summary>
    public int Current { get; set; } = -1;

    /// <summary>
    /// The chronicle's story flags. The original allows indexes 0 to 1024 inclusive (the original
    /// checks <c>&lt;= 0x400</c>) against a 1024-bit array, so index 1024 writes one dword
    /// past the end. We keep 1025 bits instead: the same range answers the same way, without the
    /// overrun — a memory bug is a code bug.
    /// </summary>
    readonly uint[] bits = new uint[33];

    public const int MaxFlag = 1024;

    public Chronicle(NscChronicle data)
    {
        Data = data;
        Portal = (data.PortalLocation, data.PortalEntrance);
        Revive = (data.ReviveLocation, data.ReviveEntrance);
    }

    public int Count => Data.Locations.Count;
    public int Find(string name) => Data.Find(name);
    public NscLocation? At(int i) => Data.At(i);
    public NscLocation? At(string name) => Data.At(name);
    /// <summary>The location's name, or the original's error string when the index is out of range.</summary>
    public string NameOf(int i) => At(i)?.Name ?? InvalidLocation;

    public const string InvalidLocation = "*** ERROR : Invalid Location ***";

    // ---- exits -------------------------------------------------------------------------------

    /// <summary>Whether exit <paramref name="n"/> of a location leads anywhere.</summary>
    public bool IsExitValid(string location, int n) =>
        At(location) is { } loc && n >= 0 && n < loc.ExitCount && loc.Exits[n].To != -1;

    /// <summary>Whether it is shut. An exit that does not exist is not closed, it is invalid.</summary>
    public bool IsExitClosed(string location, int n) =>
        At(location) is { } loc && n >= 0 && n < loc.ExitCount && loc.Exits[n].Closed;

    /// <summary>Where a taken exit is going: see <see cref="Leaving"/>.</summary>
    /// <param name="Exit">Which exit of the location you are in was taken.</param>
    /// <param name="To">The location it leads to.</param>
    /// <param name="Entrance">Which entrance of that location you arrive at.</param>
    public readonly record struct Travel(int Exit, int To, int Entrance);

    /// <summary>
    /// The exit a script has asked to take, until something acts on it. **This is the whole of
    /// `TakeExit`**, and the shape is worth knowing because it is much smaller than it sounds:
    /// The main sequence validates the exit, remembers which one it was,
    /// and sets the campaign state machine to **4**. It does
    /// not tear a level down or build one. The change of place happens later, in the main loop,
    /// when it notices state 4.
    ///
    /// So Vitomir records the request faithfully and leaves the same gap the engine leaves: **the
    /// world is not rebuilt yet.** What the request is worth on its own is that the validation is
    /// real — a closed exit, an exit past the end of the list, and one that leads nowhere are all
    /// refused here, silently, exactly as they are in the original.
    /// </summary>
    public Travel? Leaving => Pending is { Kind: SequenceKind.Exit or SequenceKind.Jump or SequenceKind.AdvanceGo or SequenceKind.Teleport } p
        ? new Travel(p.Exit, p.To, p.Entrance) : null;

    /// <summary>
    /// The one request the main sequence has pending. It is a single slot in
    /// the original — a second request in the same frame replaces the first — and the main loop
    /// takes it once a frame. <see cref="Leaving"/> is the travelling kinds of it.
    /// </summary>
    public SequenceRequest? Pending { get; private set; }

    /// <summary>
    /// Where `SetupAdvance` said the next advance goes (the main sequence's fields
    /// A location index, the scene to give it, and the entrance, −1 when unset.
    /// </summary>
    public (int Location, string Scene, int Entrance) AdvanceTo { get; internal set; } = (-1, "", -1);

    /// <summary>
    /// `CodexSequence.TakeExit`'s engine half. False, and nothing recorded, when the
    /// exit does not exist, is **closed**, or leads nowhere — the three gates in that order.
    /// <paramref name="from"/> is the location being left; it defaults to <see cref="Current"/>,
    /// which is where the engine gets it from (the actor's own world).
    /// </summary>
    public bool TakeExit(int n, int from = -1)
    {
        if (At(from < 0 ? Current : from) is not { } loc) return false;
        if (n < 0 || n >= loc.ExitCount) return false;
        if (loc.Exits[n].Closed) return false;
        if (loc.Exits[n].To < 0) return false;
        if (from < 0 && Threshold?.Invoke(n) == true) return true;   // ours: a room of the same map is walked into (CellExits.Cross)
        Pending = new SequenceRequest(SequenceKind.Exit, loc.Exits[n].To, loc.Exits[n].Entrance, Exit: n);
        return true;
    }

    /// <summary>
    /// **Ours**: asked once an exit of the current place has passed the main sequence's
    /// three gates. A cell level whose map holds the exit's place as a room answers true and walks the party
    /// in through the door, and no journey is recorded. Null — always, in a 1.1 level — changes nothing.
    /// </summary>
    public Func<int, bool>? Threshold { get; set; }

    /// <summary>Takes the pending travel, clearing it — what the main loop's states 4, 5 and 0x10 do.</summary>
    public Travel? Arrive()
    {
        var going = Leaving;
        if (going is { } t) { Current = t.To; Pending = null; }
        return going;
    }

    /// <summary>Takes whatever is pending, clearing it.</summary>
    public SequenceRequest? Take()
    {
        var p = Pending;
        Pending = null;
        return p;
    }

    /// <summary>
    /// `CodexSequence.Jump(location, entrance)`: straight to a location and an
    /// entrance, exits or no exits. Nothing happens for a name that does not resolve or a negative
    /// entrance.
    /// </summary>
    public bool Jump(string location, int entrance)
    {
        int to = Find(location);
        if (to < 0 || entrance < 0) return false;
        Pending = new SequenceRequest(SequenceKind.Jump, to, entrance);
        return true;
    }

    /// <summary>
    /// `CodexSequence.ChangeScene(location, scene)` (with a by-index twin once the location
    /// is found): the location will be this scene from now on. The name is kept as
    /// given, and a scene that is already the location's (compared without regard to case) changes
    /// nothing. A changed one is marked for the chapter swap (<see cref="Swaps"/>): where the place is
    /// loaded, its chapter scene is swapped in place at the end of the frame
    /// (<c>Campaign.SwapChapters</c>); where it is not, it is built with the new scene when it is.
    /// </summary>
    public bool ChangeScene(string location, string scene)
    {
        int at = Find(location);
        if (At(at) is not { } loc) return false;
        if (!string.Equals(loc.Scene, scene, StringComparison.OrdinalIgnoreCase))
        {
            loc.Scene = scene.Length > 31 ? scene[..31] : scene;
            Swaps.Add(at);                                                  // flag 0x200000, and a swap pending
        }
        return true;
    }

    /// <summary>
    /// The locations whose scene a <see cref="ChangeScene"/> or an advance has changed since the last
    /// end of frame — 1.1's location flag 0x200000 and its pending swap, which the
    /// end of the frame reads and clears. 1.1 sets the flag only on a location that is
    /// loaded; Vitomir marks every change and the campaign passes over a place that is not loaded (ours;
    /// the same outcome — ChapterSwap.cs).
    /// </summary>
    public HashSet<int> Swaps { get; } = [];

    /// <summary>`SetupAdvance(location, scene, entrance)`: remembered, not acted on.</summary>
    public bool SetupAdvance(string location, string scene, int entrance)
    {
        int at = Find(location);
        if (at < 0) return false;
        AdvanceTo = (at, scene.Length > 31 ? scene[..31] : scene, entrance);
        return true;
    }

    /// <summary>`Advance(playerGuid)` (single player): an advance, next frame.</summary>
    public void Advance(int guid) => Pending = new SequenceRequest(SequenceKind.Advance, Guid: guid);

    /// <summary>
    /// The advance itself (state 0xf): the scene `SetupAdvance` named goes onto its
    /// location — once — and the caller opens the advancement screen for the player. The journey
    /// waits for that screen to close (<see cref="AdvanceDone"/>).
    /// </summary>
    public void ApplyAdvanceScene()
    {
        var (at, scene, entrance) = AdvanceTo;
        if (scene.Length > 0 && At(at) is { } loc && !string.Equals(loc.Scene, scene, StringComparison.OrdinalIgnoreCase))
        {
            loc.Scene = scene;
            Swaps.Add(at);                                                  // A loaded haven swaps at the frame's end
        }
        AdvanceTo = (at, "", entrance);
    }

    /// <summary>
    /// The advancement screen closing: if an entrance was set, the journey is
    /// requested (state 0x10) and the destination forgotten.
    /// </summary>
    public bool AdvanceDone()
    {
        var (at, _, entrance) = AdvanceTo;
        if (entrance < 0) return false;
        Pending = new SequenceRequest(SequenceKind.AdvanceGo, at, entrance);
        AdvanceTo = (-1, "", -1);
        return true;
    }

    /// <summary>
    /// A teleporter used: state **6**,
    /// with the slot, the way (1 into the far end, 0 back) and
    /// the player. <paramref name="to"/> is the location that end is in, which the teleport journey
    /// reads out of the slot when it takes the request (<c>World.Teleports</c>).
    /// </summary>
    public void Teleport(int slot, int way, int guid, int to) =>
        Pending = new SequenceRequest(SequenceKind.Teleport, to, Teleports.Entrance, Guid: guid, Slot: slot, Way: way);

    /// <summary>`ChangeChronicle(name)`: state 7, when the chronicle file exists.</summary>
    public void ChangeChronicle(string name) => Pending = new SequenceRequest(SequenceKind.ChangeChronicle, Name: name);

    /// <summary>
    /// The end of the game: state 0x11 with a reason. The script native `EndGame()`
    /// asks with <see cref="EndStory"/>; the engine itself asks with
    /// <see cref="EndPartyDead"/> when nobody in the party is left standing (the
    /// party's next-member search coming up empty) and with <see cref="EndHumanityLost"/> when a
    /// character who tracks Humanity loses the last of it.
    /// </summary>
    public void EndGame(int reason = EndStory) => Pending = new SequenceRequest(SequenceKind.EndGame, Reason: reason);

    /// <summary>The three ends the game knows: the story's, the party's, and Humanity's.</summary>
    public const int EndStory = 1, EndPartyDead = 2, EndHumanityLost = 3;

    /// <summary>
    /// Opens or closes one exit. Only the named location's side changes: the loader's backward link
    /// is made once, at load, and the natives never follow it — so a script that wants both ways
    /// shut has to say so twice, as the shipped ones do.
    /// </summary>
    public void SetExitClosed(string location, int n, bool closed)
    {
        if (At(location) is { } loc && n >= 0 && n < loc.ExitCount) loc.Exits[n].Closed = closed;
    }

    /// <summary>Points an existing exit somewhere else (which looks both names up). Both names must resolve.</summary>
    public bool ChangeExit(string from, int n, string to, int entrance)
    {
        int a = Find(from), b = Find(to);
        if (a < 0 || b < 0) return false;
        var loc = Data.Locations[a];
        if (n < 0 || n >= loc.ExitCount) return false;
        loc.Exits[n].To = b;
        loc.Exits[n].Entrance = entrance;
        return true;
    }

    // ---- location flags ----------------------------------------------------------------------

    /// <summary>A location's flags, or 0 when the name does not resolve.</summary>
    public uint GetLocationFlags(string location) => At(location)?.Flags ?? 0;

    /// <summary>Sets bits and returns the flags as they now stand; 0 for an unknown location.</summary>
    public uint SetLocationFlags(string location, uint mask)
    {
        if (At(location) is not { } loc) return 0;
        return loc.Flags |= mask;
    }

    /// <summary>Clears bits and returns the flags as they now stand; 0 for an unknown location.</summary>
    public uint ClearLocationFlags(string location, uint mask)
    {
        if (At(location) is not { } loc) return 0;
        return loc.Flags &= ~mask;
    }

    // ---- portal and revive -------------------------------------------------------------------

    /// <summary>
    /// Moves the Walk-the-Abyss destination. The index is compared unsigned
    /// against the location count, so a negative one fails.
    /// </summary>
    public bool SetPortal(int location, int entrance)
    {
        if ((uint)location >= (uint)Count) return false;
        Portal = (location, entrance);
        return true;
    }

    /// <summary>Moves where a multiplayer character revives. Same rule.</summary>
    public bool SetRevive(int location, int entrance)
    {
        if ((uint)location >= (uint)Count) return false;
        Revive = (location, entrance);
        return true;
    }

    /// <summary>Where Walk the Abyss leads, as the running game has it.</summary>
    public (int Location, int Entrance) Portal { get; set; }
    /// <summary>Where a multiplayer character revives, as the running game has it.</summary>
    public (int Location, int Entrance) Revive { get; set; }

    // ---- chronicle flags ---------------------------------------------------------------------

    public bool GetFlag(int i) => InRange(i) && (bits[i >> 5] & (1u << (i & 31))) != 0;

    /// <summary>Sets a flag and returns whether it was <em>already</em> set, as the original does.</summary>
    public bool SetFlag(int i)
    {
        if (!InRange(i)) return false;
        bool was = GetFlag(i);
        bits[i >> 5] |= 1u << (i & 31);
        return was;
    }

    /// <summary>Clears a flag and returns whether it had been set.</summary>
    public bool ClearFlag(int i)
    {
        if (!InRange(i)) return false;
        bool was = GetFlag(i);
        bits[i >> 5] &= ~(1u << (i & 31));
        return was;
    }

    /// <summary>Flips a flag and returns whether it had been set.</summary>
    public bool ToggleFlag(int i)
    {
        if (!InRange(i)) return false;
        bool was = GetFlag(i);
        bits[i >> 5] ^= 1u << (i & 31);
        return was;
    }

    public void ClearAllFlags() => Array.Clear(bits);

    /// <summary>Which flags are set, for the viewer and for tests.</summary>
    public IEnumerable<int> SetFlags()
    {
        for (int i = 0; i <= MaxFlag; i++) if (GetFlag(i)) yield return i;
    }

    static bool InRange(int i) => i >= 0 && i <= MaxFlag;

    /// <summary>Reads one chronicle and its metafile out of the install.</summary>
    public static Chronicle Load(GameFiles files, string name)
    {
        var data = files.Read($"chronicles/{name}.nsc");
        var nsc = NscChronicle.Read(data, name + ".nsc");
        NmfFile? meta = null;
        if (nsc.Metafile.Length > 0 && files.TryRead($"chronicles/{nsc.Metafile}", out var nmf))
            meta = NmfFile.Read(nmf, nsc.Metafile);
        return new Chronicle(nsc) { Meta = meta };
    }

    public override string ToString() =>
        $"{Name}: {Count} locations, {Data.Locations.Sum(l => l.ExitCount)} exit slots, " +
        $"script {(Data.Scripts.Count > 0 ? Data.Scripts[0] : "none")}, flags 0x{Flags:x}";

    public uint Flags => Data.Flags;
    public NscChronicleFlags Kind => Data.Kind;
}

/// <summary>What the main sequence can be asked to do next.</summary>
public enum SequenceKind
{
    /// <summary>Take an exit of the current location (`TakeExit`).</summary>
    Exit = 4,
    /// <summary>Go to a location's entrance (`Jump`).</summary>
    Jump = 5,
    /// <summary>Go through a teleporter (`ActivateTeleporter`, `World.Teleports`).</summary>
    Teleport = 6,
    /// <summary>End this chronicle and start another (`ChangeChronicle`).</summary>
    ChangeChronicle = 7,
    /// <summary>Advance: the scene, then the advancement screen (`Advance`).</summary>
    Advance = 0xf,
    /// <summary>The journey after the advancement screen.</summary>
    AdvanceGo = 0x10,
    /// <summary>The end of the game (`EndGame`).</summary>
    EndGame = 0x11,
}

/// <summary>A request to the main sequence, with whichever of its arguments that kind uses.</summary>
/// <param name="Kind">What is asked for.</param>
/// <param name="To">The location index, for the journeys.</param>
/// <param name="Entrance">The entrance, for the journeys.</param>
/// <param name="Name">The chronicle, for <see cref="SequenceKind.ChangeChronicle"/>.</param>
/// <param name="Guid">The character, for <see cref="SequenceKind.Advance"/>.</param>
/// <param name="Exit">The exit taken, for <see cref="SequenceKind.Exit"/>; −1 otherwise.</param>
/// <param name="Reason">Which end, for <see cref="SequenceKind.EndGame"/>.</param>
/// <param name="Slot">The teleport slot, for <see cref="SequenceKind.Teleport"/>.</param>
/// <param name="Way">1 into a teleport's far end, 0 back to its near one.</param>
public readonly record struct SequenceRequest(SequenceKind Kind, int To = -1, int Entrance = 0, string Name = "",
                                              int Guid = 0, int Exit = -1, int Reason = 0, int Slot = -1, int Way = 0);
