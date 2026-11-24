// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **The scripts of a place loaded before the party goes there** (`CodexScenes.Preloads.cs`). 1.1's
/// chronicle start loads Prague's haven before the convent, and reading its scene
/// makes every script and tells it `created` — but no scene
/// begins until the party arrives. So the haven's scripts are made at the start,
/// wait while the party is in the convent without the chronicle's current location moving, and the
/// first visit begins **the same objects** rather than making them again.
/// </summary>
[Collection("scripts")]
public class PreloadScriptTests(Scripts s)
{
    [Fact]
    public void The_haven_s_scripts_are_made_at_the_start_and_the_first_visit_begins_the_same_ones()
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var prague = set["prague"]!;
        int conventAt = prague.Current = prague.Find("Convent");
        var session = new Vitomir.Engine.Session { Chronicle = prague };
        Location Build(string name, string? scene, int entrance)
        {
            var place = Location.Load(files, name, sky: false, scene: scene, player: "Christof", entrance: entrance,
                                      guids: name == "P1_CNVT" ? Preloads.NewGame(prague, files) : null);
            place.Awaken(new AiClasses(files), 1);
            place.Session = session;
            return place;
        }
        var convent = Build("P1_CNVT", "CNVT_3_1", 0);
        session.Here = convent;
        var campaign = new Campaign { Chronicle = prague, Build = Build, Here = convent };
        var haven = campaign.Preload(conventAt).Single();
        var camera = new WorldCamera();
        camera.Start(convent.Player!.Thing);
        var scenes = new CodexScenes(s.Runtime!)
        {
            Chronicles = set, Camera = camera, Talk = new Conversations(),
            Nls = new Vitomir.Engine.Ui.Strings(files).Find,
        };

        // The start: the haven's scripts made and put aside, and the convent's scene begun.
        foreach (var early in campaign.TakePreloaded()) scenes.Preload(early, prague);
        Assert.True(scenes.Waiting(haven));
        var made = scenes.WaitingScripts(haven);
        Assert.NotEmpty(made);
        Assert.Equal(conventAt, prague.Current);                                // the chronicle has not moved (the arrival moves it)
        scenes.Enter(convent);
        Assert.Same(convent, scenes.Current);
        Assert.DoesNotContain(scenes.RunningScripts, o => made.Contains(o));

        // The first visit: the journey finds the haven built, and its scene begins on those objects.
        Assert.True(prague.Jump("Haven", 0));
        var arrival = campaign.Travel()!.Value;
        Assert.True(arrival.Returned);
        scenes.Leave(0, keep: true);
        haven.Milliseconds = convent.Milliseconds + 60_000;                      // a minute in the convent (Simulation.Resume)
        scenes.Enter(haven);
        Assert.Same(haven, scenes.Current);
        Assert.Equal(made, scenes.RunningScripts.Take(made.Count));
        Assert.False(scenes.Waiting(haven));
    }

    /// <summary>
    /// **An early place whose chapter changes before the first visit** (the join of `z-early`'s
    /// preload and `z-swap`'s swap). `OLDT_7_4` changes the haven to `HAVN_7_4` while the party is
    /// in Old Town, and the haven has been loaded since the chronicle's start: the swap frees the scripts
    /// of the old chapter's things it destroys and leaves the new chapter's to be made as the party
    /// comes (<c>CodexScenes.Swapped</c>, the kept arm — the preload's <c>Open</c> hooked the place),
    /// so the first visit begins the level scene's same objects and makes `HAVN_7_4`'s.
    /// </summary>
    [Fact]
    public void An_early_place_swapped_before_the_first_visit_begins_its_new_chapter()
    {
        s.Ready();
        using var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var prague = set["prague"]!;
        int conventAt = prague.Current = prague.Find("Convent");
        var session = new Vitomir.Engine.Session { Chronicle = prague };
        Location Build(string name, string? scene, int entrance)
        {
            var place = Location.Load(files, name, sky: false, scene: scene, player: "Christof", entrance: entrance,
                                      guids: name == "P1_CNVT" ? Preloads.NewGame(prague, files) : null);
            place.Awaken(new AiClasses(files), 1);
            place.Session = session;
            return place;
        }
        var convent = Build("P1_CNVT", "CNVT_3_1", 0);
        session.Here = convent;
        var campaign = new Campaign { Chronicle = prague, Build = Build, Here = convent };
        var haven = campaign.Preload(conventAt).Single();
        var camera = new WorldCamera();
        camera.Start(convent.Player!.Thing);
        var scenes = new CodexScenes(s.Runtime!)
        {
            Chronicles = set, Camera = camera, Talk = new Conversations(),
            Nls = new Vitomir.Engine.Ui.Strings(files).Find,
        };
        foreach (var early in campaign.TakePreloaded()) scenes.Preload(early, prague);
        scenes.Enter(convent);
        int before = scenes.WaitingScripts(haven).Count;
        Assert.True(before > 0);

        // OLDT_7_4's ChangeScene, at the end of a frame: the haven is loaded, so it is swapped in place.
        prague.ChangeScene("Haven", "HAVN_7_4.nsd");
        var swaps = campaign.SwapChapters();
        Assert.Contains(swaps, w => ReferenceEquals(w.Place, haven));
        Assert.True(scenes.Waiting(haven));
        Assert.True(scenes.WaitingScripts(haven).Count <= before);                // the old chapter's freed

        Assert.True(prague.Jump("Haven", 0));
        Assert.True(campaign.Travel()!.Value.Returned);
        scenes.Leave(0, keep: true);
        scenes.Enter(haven);
        Assert.Same(haven, scenes.Current);
        Assert.Contains("HAVN_7_4", scenes.Running);                           // the new chapter's own script, made and begun
        Assert.Empty(haven.AwaitingScripts);
    }
}
