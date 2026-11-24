// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Every chronicle in the install, and the reverse lookup from a level or a scene back to the
// location that owns it.
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>Which chronicle a level belongs to, and which of its locations the level is.</summary>
public readonly record struct ChronicleWhere(Chronicle Chronicle, int Location)
{
    public NscLocation? Entry => Chronicle.At(Location);
    public string Name => Entry?.Name ?? "";
    public override string ToString() => $"{Chronicle.Name}/{Name}";
}

/// <summary>
/// The <c>Chronicles\</c> folder. The game is told which chronicle to load; the viewer and the
/// tests start from a level instead, so this also answers the other question — given
/// <c>P1_CNVT</c>, which chronicle and which location is that?
/// </summary>
public sealed class ChronicleSet
{
    readonly List<Chronicle> all = [];
    readonly Dictionary<string, ChronicleWhere> byScene = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, ChronicleWhere> byLevel = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Chronicle> All => all;

    /// <summary>Loads every <c>.nsc</c> in the install, single player first.</summary>
    public static ChronicleSet Load(GameFiles files)
    {
        var set = new ChronicleSet();
        var names = files.List("chronicles/")
                         .Where(n => n.EndsWith(".nsc", StringComparison.OrdinalIgnoreCase))
                         .Select(Path.GetFileNameWithoutExtension).OfType<string>()
                         .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var name in names)
        {
            try { set.all.Add(Chronicle.Load(files, name)); }
            catch (Exception e)
            {
                // A chronicle we cannot read is one we do not offer — but silence here would hide a
                // reader bug behind "that campaign just isn't in the menu".
                Diag.For<ChronicleSet>().LogWarning(e, "chronicle {Name} could not be read", name);
            }
        }
        set.Sort();
        set.Index();
        Levels.Cells.CellPlaces.Apply(set);                                     // ours: places played as cell levels; nothing unless asked (CellPlaces.cs)
        Diag.For<ChronicleSet>().LogInformation("{Count} chronicles, {Locations} locations",
                                                set.all.Count, set.all.Sum(c => c.Count));
        return set;
    }

    /// <summary>For tests and tools: a set built from chronicles already in hand.</summary>
    public static ChronicleSet Of(params Chronicle[] chronicles)
    {
        var set = new ChronicleSet();
        set.all.AddRange(chronicles);
        set.Sort();
        set.Index();
        return set;
    }

    /// <summary>
    /// Story order as far as the files state it: single player before multiplayer, and within
    /// single player the one chronicle that is not chained from another comes first — which is
    /// Prague, the campaign's start. The rest keep file order, which settles ties when the same
    /// level appears in two chronicles.
    /// </summary>
    void Sort() => all.Sort((a, b) =>
    {
        int Rank(Chronicle c) =>
            (c.Kind.HasFlag(NscChronicleFlags.Multiplayer) ? 2 : 0) +
            (c.Kind.HasFlag(NscChronicleFlags.Chained) ? 1 : 0);
        int r = Rank(a).CompareTo(Rank(b));
        return r != 0 ? r : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    });

    /// <summary>Files every level and scene again, after a place's level has been changed (ours: <c>CellPlaces.Apply</c>).</summary>
    public void Reindex() => Index();

    void Index()
    {
        byScene.Clear();
        byLevel.Clear();
        foreach (var c in all)
            for (int i = 0; i < c.Count; i++)
            {
                var loc = c.At(i)!;
                var where = new ChronicleWhere(c, i);
                // A scene belongs to exactly one location, so it is the reliable key. A level can
                // appear in several chronicles (Prague's Haven is also in Prague2), so the first
                // chronicle in story order that gives it a scene wins, and a location with no scene
                // of its own — the ones that exist only to be locked — never claims a level.
                if (loc.SceneName.Length > 0)
                {
                    byScene.TryAdd(loc.SceneName, where);
                    byLevel.TryAdd(loc.LevelName, where);
                }
            }
        foreach (var c in all)
            for (int i = 0; i < c.Count; i++)
                if (c.At(i)!.LevelName is { Length: > 0 } level) byLevel.TryAdd(level, new ChronicleWhere(c, i));
    }

    public Chronicle? this[string name] =>
        all.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The location a scene file belongs to (<c>CNVT_3_1</c>), or null.</summary>
    public ChronicleWhere? ForScene(string sceneName) =>
        byScene.TryGetValue(Strip(sceneName), out var w) ? w : null;

    /// <summary>The location a level belongs to (<c>P1_CNVT</c>), or null.</summary>
    public ChronicleWhere? ForLevel(string levelName) =>
        byLevel.TryGetValue(Strip(levelName), out var w) ? w : null;

    /// <summary>Scene first, then level: what the viewer knows about a loaded place.</summary>
    public ChronicleWhere? For(string sceneName, string levelName) =>
        ForScene(sceneName) ?? ForLevel(levelName);

    static string Strip(string s)
    {
        int dot = s.LastIndexOf('.');
        return dot < 0 ? s : s[..dot];
    }
}
