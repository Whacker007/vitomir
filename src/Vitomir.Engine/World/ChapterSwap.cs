// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The live chapter swap ("Absolutely build it v1.1's way, that is faithful.").
//
// 1.1 never builds a loaded place again. `ChangeScene` on a location that is loaded flags it
// flag `0x200000`, and a swap is pending), and at the end of that frame
// (just before the main sequence takes its request) each flagged
// location that has a world has its **chapter scene alone** swapped: the regions cut back
// to the level scene's, the chapter scene torn down and the new one loaded into the same
// world. Everything else — the level scene's things, and whatever was spawned, dropped,
// opened, moved or killed in the place — stays as it was. A place read back from its `.nsw` whose
// chapter has changed since is swapped the same way over what the file kept.
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Scene;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// What one chapter swap did, for whoever keeps something per thing — the script layer
/// drops the scripts of <see cref="Gone"/> and makes those of <see cref="Arrived"/>.
/// </summary>
/// <param name="Place">The place swapped.</param>
/// <param name="From">The chapter scene torn down, or null when the place had none.</param>
/// <param name="To">The chapter scene loaded, or null when the place now has none.</param>
/// <param name="Gone">The old chapter's things that were still in its table, destroyed without debris.</param>
/// <param name="Arrived">The new chapter's things, in its file order, numbered from the running counter.</param>
/// <param name="WorldCount">How many things the level scene has — where the chapter's scene indices start.</param>
public sealed record ChapterSwap(Location Place, NsdScene? From, NsdScene? To, WorldThing[] Gone, WorldThing[] Arrived,
                                 int WorldCount)
{
    /// <summary>
    /// The new chapter's file is there but would not load (the scene load answered 0): the old
    /// chapter is gone all the same, the place has no chapter scene, and the location's scene name
    /// becomes <see cref="Location.InvalidScene"/> (<see cref="Campaign.SwapChapters"/>).
    /// </summary>
    public bool Failed { get; init; }
}

public sealed partial class Location
{
    /// <summary>
    /// Told of each chapter swap once the new chapter's things are in the world and finalized. The
    /// script layer sets it as it enters the place, as it sets <see cref="Created"/>.
    /// </summary>
    public Action<ChapterSwap>? ChapterSwapped { get; set; }

    /// <summary>
    /// The new chapter's things whose scripts have not been made yet (ours). 1.1 makes a thing's
    /// script as the thing is loaded and tells it `created` as it is
    /// finalized, whatever place the party is in. Vitomir's natives act on the place
    /// the party is in, so a place swapped while the party is elsewhere — or while it is being read
    /// back and has no scripts yet — keeps its arrivals here, and the script layer makes their
    /// scripts and tells them `created` as the party next enters, before `beginscene`.
    /// </summary>
    public List<WorldThing> AwaitingScripts { get; } = [];

    /// <summary>
    /// **The chapter swap**: this place's chapter scene is replaced by
    /// <paramref name="scene"/> in the live world. In order:
    /// <list type="number">
    /// <item>A scene file that is not there changes nothing (the file test answers 0).</item>
    /// <item>Each sector's region list is cut back to the level scene's regions, and every thing in
    /// the sector loses the bits past them — **silently**: nobody hears `exited`.</item>
    /// <item>The chapter scene is torn down: each of its things still in its table is
    /// destroyed **with no debris** and deleted, which frees its script, the script's
    /// timers and its captures. A thing picked up has left the table
    /// and stays in its pack; one already destroyed is gone already. Its placed sounds
    /// are taken off their sectors and stopped.</item>
    /// <item>The new chapter scene is loaded into the same world: its extra template
    /// files, its things in file order **numbered from the game's running counter** (the
    /// read-back flag is down even on a read-back), its sounds,
    /// its conversation starts when it has any, its settings and sky; then its things
    /// are finalized.</item>
    /// </list>
    /// No loading plaque, no `endscene` for the old chapter's scripts and no `beginscene` for the
    /// new chapter's: those go out when a scene next starts. Null when nothing was swapped.
    /// </summary>
    /// <param name="scene">The new chapter scene's name, with or without its <c>.nsd</c>. The level's own
    /// name, or empty, leaves the place with no chapter scene (ours, as <see cref="Load"/> reads it;
    /// no shipped script names a level scene).</param>
    public ChapterSwap? SwapChapter(string scene)
    {
        if (Files is not { } files) return null;
        string name = StripScene(scene);
        bool none = name.Length == 0 || name.Equals("null", StringComparison.OrdinalIgnoreCase)
                    || name.Equals(Name, StringComparison.OrdinalIgnoreCase);
        // The file test (name, 8) — a file that is not there changes nothing. One that is there but
        // will not load fails only after the old chapter is gone (the scene load answers 0).
        var (there, next) = none ? (true, null) : ReadChapter(files, name);
        next = Levels.Cells.CellScene.Chapter(Level, files, name, next);   // a cell level that carries a 1.1 scene: placed on its ground (ours)
        if (!there) return null;
        bool failed = !none && next is null;
        if (none && ChapterScene is null) return null;                             // nothing to swap (ours)

        int worldCount = WorldScene?.Things.Length ?? 0;
        var old = ChapterScene;
        string oldName = old is null ? "(none)" : SceneFile;
        int oldCount = old?.Things.Length ?? 0;
        var gone = Array.Empty<WorldThing>();
        if (old is not null)
        {
            // The regions first, while the chapter's are still there to be counted.
            Regions?.CutBack(this, worldCount);
            // Every thing still in the chapter's table destroyed without debris. A thing out
            // of the world — picked up, or a member who has left with the party — has left the table
            // (its slot is freed), and one destroyed already is gone from it.
            gone = [.. things.Where(t => InChapter(t, worldCount, oldCount))];
            foreach (var t in gone) Remove(t, drop: false);
            // ... and deleted: a character's destructor frees its disciplines' scripts.
            foreach (var t in gone) Deleted(t);
            if (ReferenceEquals(Audio?.Bound, this)) Audio!.Unbind(old);
        }

        // The things after the scene's own — the player's, which the loader appends — move past the
        // new chapter's, so a scene index still names one scene's thing (ours; 1.1 makes the player
        // apart from any scene). And an old chapter's thing that outlives its chapter — one
        // picked up, which had left the table — is no scene's thing any more: it is named
        // as a thing made in the place, as a save names a drop (Save.ThingIds).
        int newCount = next?.Things.Length ?? 0;
        foreach (var t in byGuid.Values.Concat(things).Distinct())
        {
            if (t.Index >= worldCount + oldCount) t.Index += newCount - oldCount;
            else if (InChapter(t, worldCount, oldCount))
            {
                t.SaveId ??= Save.ThingIds.Made(this, ++Made);
                t.Index = -1;
            }
        }
        // What a load took away of the old chapter is no scene's either (Location.Gone).
        foreach (int index in Gone.Keys.Where(i => i >= worldCount).ToArray())
            if (index < worldCount + oldCount) Gone.Remove(index);

        var arrived = Array.Empty<WorldThing>();
        if (next is not null && Templates is { } templates)
        {
            foreach (var extra in next.TemplateFiles)                                // The scene's own template files
                if (files.TryRead("misc/templates/" + (extra.Contains('.') ? extra : extra + ".not"), out var notData))
                    templates.AddFile(NotFile.Read(notData, extra));
            arrived = new WorldThing[newCount];
            for (int i = 0; i < newCount; i++)
            {
                var t = Build(next.Things[i], templates, index: worldCount + i, guid: 0, now: Milliseconds);
                // The scene load mid-game: the character's fallback weapon first (LocationGuids.SpendFallback),
                // then its own number — the counter's, past anything the table holds.
                CharacterSheets.Hold(t, files, templates, Guids);                        // a companion's kit first
                SpendFallback(t, templates, Guids);
                t.Guid = Guids.Take(byGuid.ContainsKey);
                arrived[i] = t;
            }
            LoadBoxes(arrived, files);
            foreach (var t in arrived)
            {
                t.Place = this;
                things.Add(t);
                byGuid[t.Guid] = t;
                Props?.Enter(t);
                Show(t);
                if (t.Actor is { } who) { who.IsPlayerThing = t.Type == ThingType.Player; who.ClanRecord = Rpg.Clans.For(files)[who.Clan]; }
            }
            if (Effects is not null) CharacterSheets.Give(this);                      // their sheets, the place being awake
            Regions?.Lay(arrived, Level.Sectors.Length);
            // The scene's numbers now run on to the new chapter's last, as a rebuild's did (the level's
            // first kept; ours — 1.1's own field is not written again). What the network tells a
            // joining machine; a copy numbered in one run from there does not match the gap (not built).
            SceneGuids = (SceneGuids.First, Guids.Peek);
            if (next.ConversationStarts.Length > 0) ConversationStarts = next.ConversationStarts;   // only when it has some
            if (ReferenceEquals(Audio?.Bound, this)) Audio!.Rebind(next);
        }

        ChapterScene = next;
        Scene = next ?? WorldScene;
        SceneFile = next is not null ? name : WorldScene is null ? "" : Name;
        // The settings in force are the chapter scene's (the world draw reads them).
        Fog = FogSettings.FromScene(Scene);
        FarPlane = Scene?.ZClip ?? 0f;
        if (SkyLoaded)                                                             // The unload lets the old sky go; the new scene's
        {
            Sky = Scene is { } current ? World.Sky.Load(current, files, Materials) : null;
            SkyWindow = Sky is not null ? SkyWindow ?? new SkyWindow(Level) : null;
        }
        ScriptNames = [.. things.Where(t => t.Index >= 0 && t.Index < worldCount + newCount).Select(t => t.Script)
                                .Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];

        // Each thing finalized — the spawn transform, `create`, what it is born with, and
        // its mind (made with the thing in 1.1).
        foreach (var t in arrived)
        {
            if (!t.Finish()) continue;
            PlayCreate(t);
            Born(t);
            Minds?.Adopt(t);
        }

        var swap = new ChapterSwap(this, old, next, gone, arrived, worldCount) { Failed = failed };
        AwaitingScripts.RemoveAll(t => gone.Contains(t));
        AwaitingScripts.AddRange(arrived.Where(t => t.Script.Length > 0));
        ChapterSwapped?.Invoke(swap);
        Diag.For<Location>().LogInformation("{Place}: chapter {From} swapped for {To} in place ({Gone} things gone, {Arrived} arrived)",
            Name, oldName, failed ? name + " (would not load)" : next is null ? "(none)" : name, gone.Length, arrived.Length);
        return swap;
    }

    /// <summary>
    /// The name a location's scene takes when its new chapter would not load (the swap copies a fixed
    /// name into the location). A later journey there finds no such file and builds the level
    /// alone; a `ChangeScene` back to any real name is a change, and swaps again.
    /// </summary>
    public const string InvalidScene = "__INVALID__";

    /// <summary>
    /// Whether the place was loaded with its sky (<see cref="Load"/>'s <c>sky</c>; ours — a viewer or
    /// a test may load without one). A swap takes the new chapter's sky only then, and takes it
    /// whether or not the old chapter had one (the unload lets the old go, the scene load reads the new).
    /// </summary>
    public bool SkyLoaded { get; init; } = true;

    /// <summary>
    /// A chapter scene read for a swap: whether its file is there, and the scene, or null
    /// when the file is there but will not load — the scene load gives up on a header it cannot read, a
    /// version of 7 or less, or a list that runs short. Vitomir's reader refuses
    /// anything but version 8 and anything that runs past its end (<see cref="NsdScene.Read"/>); a
    /// file that fails part-way in 1.1, after its things are made, is treated as failing at its
    /// header (ours — no shipped scene fails).
    /// </summary>
    static (bool There, NsdScene? Scene) ReadChapter(GameFiles files, string name)
    {
        if (!files.TryRead($"scenes/{name}.nsd", out var data)) return (false, null);
        try { return (true, NsdScene.Read(data, name + ".nsd")); }
        catch (NodFormatException e)
        {
            Diag.For<Location>().LogWarning("chapter scene {Scene} will not load: {Why}", name, e.Message);
            return (true, null);
        }
    }

    /// <summary>
    /// What deleting a thing the chapter swap destroyed frees beyond its script (the unload calls the
    /// thing's destructor; the character's frees its disciplines): each discipline it has is let go —
    /// the discipline bar's hold on it and its script, with the script's timers and captures.
    /// Its effects came off as it was destroyed (<see cref="Remove"/>).
    /// </summary>
    static void Deleted(WorldThing thing)
    {
        if (thing.Actor is not { } who) return;
        foreach (var d in who.Disciplines.All) Rpg.DisciplineBook.Discard(thing, d);
    }

    /// <summary>Whether a thing is one of the chapter scene's own, by its scene index.</summary>
    static bool InChapter(WorldThing t, int worldCount, int chapterCount) =>
        t.Index >= worldCount && t.Index < worldCount + chapterCount;

    /// <summary>A scene name without its <c>.nsd</c> (the scripts write `CNVT_3_3.nsd`).</summary>
    internal static string StripScene(string scene)
    {
        int dot = scene.LastIndexOf('.');
        return dot < 0 ? scene : scene[..dot];
    }
}

public sealed partial class Regions
{
    /// <summary>
    /// The chapter swap's cut: in each sector, the regions of the level scene
    /// are counted — each region's index against the world's count; they come first, being laid first — the
    /// count becomes the sector's, the slots past it are emptied, and every thing in the sector
    /// loses the bits past it **without a word**: no `exited` for a region that has gone.
    /// </summary>
    internal void CutBack(Location place, int worldCount)
    {
        foreach (var (sector, slots) in bySector)
        {
            int used = Used(slots), kept = 0;
            for (int i = 0; i < used; i++)
                if (slots[i] is { } r && r.Index >= 0 && r.Index < worldCount) kept++;
            for (int i = kept; i < PerSector; i++) slots[i] = null;
            uint lost = kept >= 32 ? 0u : ~((1u << kept) - 1);
            foreach (var t in place.Things)
                if (t.Sector == sector) t.InRegions &= ~lost;
        }
    }

    /// <summary>Lays regions into their sectors' lists, first empty slot first — the loader's pass.</summary>
    internal void Lay(IEnumerable<WorldThing> things, int sectorCount)
    {
        foreach (var region in things)
        {
            if (region.Type != ThingType.Region || region.Scene.Model is not { Planes.Length: > 0 }) continue;
            bool placed = false;
            foreach (uint sector in region.Scene.Sectors)
            {
                if (sector >= sectorCount) continue;
                if (!bySector.TryGetValue(sector, out var slots)) bySector[sector] = slots = new WorldThing?[PerSector];
                for (int i = 0; i < PerSector; i++)
                {
                    if (slots[i] is not null) continue;
                    slots[i] = region;
                    placed = true;
                    break;
                }
            }
            if (placed) Count++;
        }
    }
}

public sealed partial class Campaign
{
    /// <summary>
    /// **The end of the frame's chapter swap** (before the main sequence takes
    /// its request): each location a <c>ChangeScene</c> or an advance has marked, and that is loaded,
    /// has its chapter scene swapped in place (<see cref="Location.SwapChapter"/>) for the one its name
    /// says now — whatever scene the place has, as the swap is called on the mark alone: a change from
    /// A to B and back to A in one frame loads A afresh. A new chapter whose file will not load leaves
    /// the location's scene named <see cref="Location.InvalidScene"/>. The marks are
    /// cleared. A location not loaded is left to be built with its new scene when it is.
    /// </summary>
    public IReadOnlyList<ChapterSwap> SwapChapters()
    {
        if (Chronicle.Swaps.Count == 0) return [];
        var done = new List<ChapterSwap>();
        foreach (int index in Chronicle.Swaps.Order().ToArray())                     // the locations in order
        {
            if (LoadedAt(index) is not { } place || Chronicle.At(index) is not { } at) continue;
            // Whatever the place has now: a ChangeScene from A to B and back to A in one frame loads A
            // afresh (the swap tests only the flag, which `ChangeScene` set on the change to B).
            if (place.SwapChapter(at.SceneName.Length > 0 ? at.SceneName : place.Name) is not { } swapped) continue;
            if (swapped.Failed) at.Scene = Location.InvalidScene;
            done.Add(swapped);
            log.LogInformation("chapter: {Location} is now {Scene}, swapped in place", Chronicle.NameOf(index), place.SceneFile);
        }
        Chronicle.Swaps.Clear();
        return done;
    }

    /// <summary>The place loaded for a location, the one the party is in included, or null.</summary>
    Location? LoadedAt(int index) => index == Chronicle.Current ? Here : resident.GetValueOrDefault(index);

    /// <summary>
    /// Swaps <paramref name="place"/>'s chapter for <paramref name="scene"/> unless it has it already
    /// — the read-back's test (no chapter in the file, or not the one the chronicle names).
    /// </summary>
    static ChapterSwap? Chapter(Location place, string scene)
    {
        string want = scene.Length > 0 ? scene : place.Name;
        if (string.Equals(place.SceneFile, want, StringComparison.OrdinalIgnoreCase)) return null;
        return place.SwapChapter(want);
    }

    /// <summary>
    /// A place read back from its record, once it is built from what the record kept and
    /// the file's own numbers are given back (the read-back flag down): its chapter swapped for the
    /// one the chronicle now names, when that has changed since the record was taken. Until then
    /// Vitomir threw such a record away and built the place from its files, so a
    /// door left open or a chest emptied there came back as the level made it.
    /// </summary>
    static void ReadBack(Location place, string? scene) => Chapter(place, scene ?? "");
}
