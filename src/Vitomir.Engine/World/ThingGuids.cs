// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// One GUID counter for the whole game, as 1.1 has it.
//
// 1.1 numbers every thing it makes from one global, `nextGUID`:
// set to 1 when the game library starts (at the program's start)
// and never again — a new game and a quit to the menu leave it alone — written
// into `!main.nsg` as the id counter (section 10) and read back from it.
// A thing's making takes the next number: the counter goes
// up by one, never gives 0, and skips any number the GUID table already holds; a thing is never given
// a number twice, and a load hands every saved thing the number it was saved with (two flags
// stop the counter while a save or a saved place is read). So a GUID names one
// thing for the whole game — in 1.1 Christof is 5 in every place — and a script may keep
// one across places and saves.
//
// Vitomir numbered each place's things from 1 on its own, so Christof was 68, 66, 42 and 122 in four
// places and an old GUID came back in the next place as somebody else, where 1.1's stays one thing's.
// Now every place a game builds draws from this, and a journey, a load and a save keep the numbers.
//
// A network game's host marks every number it hands out with 0x40000000 (`HostBit`): the
// counter counts on bare, and the host's things are Christof 0x40000005 and so on.
namespace Vitomir.Engine.World;

/// <summary>
/// The game's GUID counter, `nextGUID`. Every <see cref="Location"/> of one game
/// shares one — the campaign hands it to each place it builds — and a place loaded on its own has
/// its own, which is the whole game as far as it knows.
/// </summary>
public sealed class ThingGuids
{
    /// <summary>
    /// The number the next thing gets: 1 at the start (the library's start sets it, and
    /// it holds 1 before), and what a save writes and a load puts back.
    /// Always the bare count: a host's <see cref="HostBit"/> is set on the numbers handed out, not here.
    /// </summary>
    public int Next
    {
        get => next;
        set => next = Math.Max(1, Raw(value));
    }
    int next = 1;

    /// <summary>
    /// Where a load puts the counter: the number the save says, and nothing held any
    /// more — the kit a new game's player was to be made with is the save's now.
    /// </summary>
    public void Resume(int value)
    {
        Next = value;
        held.Clear();
        trails.Clear();
    }

    /// <summary>
    /// The number a new thing gets (the same allocation inlined twice): the
    /// counter's, stepped past 0 on a wrap and past every number <paramref name="taken"/> says is in
    /// use — 1.1 asks its GUID table. While <see cref="Loading"/>, a placeholder instead.
    /// </summary>
    public int Take(Func<int, bool>? taken = null)
    {
        if (Loading)
        {
            while (taken?.Invoke(placeholder) == true) placeholder++;
            return placeholder++;
        }
        return Fresh(taken);
    }

    /// <summary>The counter's next number, loading or not — for a thing a load has no number for.</summary>
    public int Fresh(Func<int, bool>? taken = null)
    {
        while (next == 0 || taken?.Invoke(Mark(next)) == true) next++;          // the table is asked with the bit set
        return Mark(next++);
    }

    /// <summary>
    /// The bit a network game's host sets on every number it hands out (the counter's two readers and each
    /// inlined allocation — all
    /// <c>| 0x40000000</c> in network mode 2). The counter itself never holds it: a save
    /// writes the bare count and the counter is set bare.
    /// </summary>
    public const int HostBit = 0x4000_0000;

    /// <summary>
    /// This game is a network game's host (1.1's net state 2): every GUID it
    /// takes and peeks carries <see cref="HostBit"/>, so the host's things never share a number with
    /// anything a joining machine numbers for itself, which it does from its own counter, bare. Single
    /// player and a joining machine hand out bare numbers.
    /// </summary>
    public bool Host { get; set; }

    /// <summary>A counter number as this game hands it out: with <see cref="HostBit"/> on a host.</summary>
    int Mark(int n) => Host ? n | HostBit : n;

    /// <summary>A GUID's count, the host's bit taken off — what the counter is compared with.</summary>
    public static int Raw(int guid) => guid & ~HostBit;

    /// <summary>
    /// The counter moved past a number now in use (a load's, a journey's, a host's): never backwards,
    /// never onto a placeholder, and compared by count — a host's bit is not part of the counter.
    /// </summary>
    public void Past(int guid)
    {
        if (guid <= 0 || IsPlaceholder(guid)) return;
        int n = Raw(guid);
        if (n >= next) next = n + 1;
    }

    /// <summary>
    /// The counter a game's first place is built with: <paramref name="guids"/> (a load's, a new
    /// game's) or a fresh one, marked as a host's when this machine hosts a network game.
    /// </summary>
    public static ThingGuids For(bool host, ThingGuids? guids = null)
    {
        guids ??= new ThingGuids();
        guids.Host = host;
        return guids;
    }

    /// <summary>
    /// A save or a saved place is being read (1.1's two flags, one up for the game's load
    /// and one up while a place's `.nsw` is read): the making of
    /// a thing takes **no** number from the counter (the GUID's taking skips its allocation) because the file
    /// gives it one. Vitomir builds the place from its scene first and hands the saved numbers back
    /// after (<see cref="Location.Rekey"/>), so what a thing is made with meanwhile is a placeholder
    /// from a range the counter never reaches (<see cref="PlaceholderBase"/>, ours) — and a placed
    /// thing the load takes away keeps it, a number nothing answers to (<see cref="Location.Gone"/>).
    /// </summary>
    public bool Loading { get; set; }

    /// <summary>Where the placeholders a load's building hands out start (ours): 2^29, far past any game's count.</summary>
    public const int PlaceholderBase = 0x2000_0000;
    int placeholder = PlaceholderBase;

    /// <summary>The next placeholder, loading or not — for a thing a load moves out of another's way.</summary>
    public int Placeholder() => placeholder++;

    /// <summary>Whether a number is a load's placeholder rather than a thing's own.</summary>
    public static bool IsPlaceholder(int guid) => Raw(guid) >= PlaceholderBase;

    /// <summary>
    /// The number for a thing made while the game runs: while a kit is being given
    /// (<see cref="GivingKit"/>), one <see cref="Hold"/> set aside for it, in the order they were set
    /// aside; the counter's otherwise.
    /// </summary>
    public int TakeHeld(Func<int, bool>? taken = null) => GivingKit && held.Count > 0 ? held.Dequeue() : Take(taken);

    /// <summary>
    /// A player's kit is being given (<see cref="Rpg.StartingKit.Give"/>), so the things it makes —
    /// its items and an equipped sword's trail — take the numbers held for them.
    /// </summary>
    public bool GivingKit { get; set; }

    /// <summary>The kit is given: whatever was held and not taken is spent, as it was in 1.1.</summary>
    public void KitGiven()
    {
        GivingKit = false;
        held.Clear();
    }

    /// <summary>
    /// The number a thing is about to take, without taking it — the counter's peek, which the scene reader
    /// records as the world's first GUID before it reads the scene's things —
    /// with <see cref="HostBit"/> on a host, as the peek answers it there.
    /// </summary>
    public int Peek => Loading ? placeholder : Mark(next);

    /// <summary>
    /// Sets numbers aside now for things made later — ours. 1.1 makes the player of a new game
    /// before any place (from the chronicle's start), and its making makes its
    /// kit first (the `.NPC`'s `item` lines) — `CuthbertLetter` is GUID 1 and the
    /// broadsword 2 in every 1.1 save. Vitomir gives the kit after the place is built, so the
    /// numbers 1.1 spent on it are held here and handed out, in order, to the next things made
    /// (<see cref="TakeHeld"/>) — the kit's items and the trail its sword hangs.
    /// </summary>
    public void Hold(int count)
    {
        for (int i = 0; i < count; i++) held.Enqueue(Take());
    }

    /// <summary>How many held numbers are still waiting for their things.</summary>
    public int Held => held.Count;

    readonly Queue<int> held = new();

    /// <summary>
    /// Sets a number aside for the blade trail an equipped kit weapon hangs as the player is made.
    /// Vitomir hangs trails from its own watch a step later
    /// (<see cref="Particles.BladeTrails"/>), which takes it with <see cref="TakeTrail"/>.
    /// </summary>
    public void HoldTrail() => trails.Enqueue(Take());

    /// <summary>A number held for a trail, or 0 when none is.</summary>
    public int TakeTrail() => trails.Count > 0 ? trails.Dequeue() : 0;

    readonly Queue<int> trails = new();

    /// <summary>
    /// The GUID the next place built for this game gives its player thing instead of a new one — ours,
    /// the way Vitomir's journey works. In 1.1 the party's things themselves travel (the arrival moves
    /// each member's thing into the new world, GUID and all); Vitomir builds each place with a fresh
    /// body for the player and carries the character into it (<see cref="Campaign"/>), so the
    /// campaign says here whose body it is, and the body takes that number and spends none.
    /// 0 when no one is arriving (a new game's first place, a harness run).
    /// </summary>
    public int Arriving { get; set; }

    /// <summary>
    /// The level scene's things' GUIDs, by scene index, of a place being built again because the
    /// chronicle changed its chapter scene (ours: Vitomir rebuilds the place). 1.1 swaps the chapter
    /// in place: the level's own things stay, numbers and all, and only the new
    /// chapter's are read — so the rebuilt place's level things take these back and spend nothing.
    /// </summary>
    public IReadOnlyDictionary<int, int>? KeepWorld { get; set; }

    /// <summary>
    /// The counter a campaign is building a place with (ours): a place <see cref="Location.Load"/>ed
    /// meanwhile without being handed one draws from it, so whatever builds a campaign's places — the
    /// game, a network host, a test — keeps one count for the game.
    /// </summary>
    public static ThingGuids? Building => building;
    [ThreadStatic] static ThingGuids? building;

    /// <summary>Builds with <paramref name="guids"/> until the answer is disposed.</summary>
    public static Scope Use(ThingGuids guids)
    {
        var was = building;
        building = guids;
        return new Scope(was);
    }

    /// <summary>The end of a <see cref="Use"/>.</summary>
    public readonly struct Scope(ThingGuids? was) : IDisposable
    {
        public void Dispose() => building = was;
    }

    /// <summary>
    /// Scenes whose numbers are set aside, by <see cref="Location.PlaceKey"/>, each with the first
    /// number it gets: the places a chronicle loads before its first (<see cref="Preloads"/>). The
    /// place is numbered from there when it is first built, and the counter is not touched.
    /// </summary>
    public Dictionary<string, int> Reserved { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Preloaded scenes and how many numbers each takes, waiting for the player to be made (<see cref="ReservePending"/>).</summary>
    public List<(string Key, int Count)> Pending { get; } = [];

    /// <summary>
    /// Sets aside the numbers of every <see cref="Pending"/> scene, in the chronicle's order — which
    /// is when 1.1 reads them (after the player, before the first place).
    /// </summary>
    public void ReservePending()
    {
        foreach (var (key, count) in Pending)
        {
            Reserved[key] = next;
            next += count;
        }
        Pending.Clear();
    }
}
