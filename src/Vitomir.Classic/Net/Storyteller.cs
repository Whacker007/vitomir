// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Storyteller's seat: the host's alone, taken from
// the multiplayer bar's `sbSTButton` ("Only the server can switch to Storyteller mode.")
// and only in a game made with Storyteller moderation. The bar becomes `statusBarST`
// with its five pane boxes, and the panes — actors, controls, locations, objects, scenes — are the
// front end's own kind of pane, kept in a `FrontEnd` of their own so the world goes on taking the
// clicks that miss them. What each control does is `Engine.Net.StoryPowers`; this is the wiring.
using Vitomir.Platform.Modules;
using Vitomir.Engine;
using Vitomir.Engine.Net;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;
using Vitomir.Classic;

namespace Vitomir;

public sealed class Storyteller
{
    public const string BarFile = "statusBarST", ActorFile = "stActorPane", ControlFile = "stControlPane",
                        LocationFile = "stLocationPane", ObjectFile = "stObjectPane", SceneFile = "stScenePane";

    /// <summary>The bar's five boxes and the pane each shows, with its side (`visGroup` 1 left, 2 right).</summary>
    static readonly (string Box, string Pane, int Side)[] PaneBoxes =
    [
        ("sbObjectPaneCheck", ObjectFile, 2), ("sbLocationPaneCheck", LocationFile, 2), ("sbActorPaneCheck", ActorFile, 2),
        ("sbScenePaneCheck", SceneFile, 1), ("sbControlsPaneCheck", ControlFile, 1),
    ];

    readonly Session session;
    readonly Chronicle? chronicle;
    readonly Scripting.CodexScenes? scenes;
    readonly StatusBarPass hud;
    readonly Func<WorldThing?> hovered;
    readonly Strings strings;
    readonly List<StoryPowers.Step> steps;
    readonly List<string> shortList;
    List<WorldThing> actorRows = [];
    List<string> templateRows = [];
    List<(string Row, int Slot)> castRows = [];
    int nextRefresh, startedMs = -1;
    StoryPowers.Kind kind = StoryPowers.Kind.Actors;
    bool shortOnly, pressed;

    public Storyteller(GameServer server, Location place, Strings strings, UiAssets assets, Session session, GameFiles files,
                       Chronicle? chronicle, Scripting.CodexScenes? scenes, StatusBarPass hud, Func<WorldThing?> hovered)
    {
        this.session = session;
        this.chronicle = chronicle;
        this.scenes = scenes;
        this.hud = hud;
        this.hovered = hovered;
        this.strings = strings;
        Powers = new StoryPowers(server, place, strings);
        place.SteeredChanged += Steered;                           // one control
        Menus = new FrontEnd(assets.Pane, assets.Font, strings.Find) { State = FrontEnd.StateGame, SinglePlayer = false, Dress = MenuLettering.Dresser(assets), Trail = InputTape.Menu };
        Menus.OtherClick = Click;
        steps = StoryPowers.Steps(files, chronicle);
        Index.Read(chronicle?.Name ?? "", steps.Count);            // the list read, once a chronicle
        shortList = StoryPowers.ShortList(files, chronicle);
        // The multiplayer bar's own Storyteller button is shown on the server only.
        // Greyed in a game made without Storyteller moderation (ours): 1.1 shows it lit and does nothing; its click still says why.
        if (hud.Bar["sbSTButton"] is { } st) { st.Shown = true; st.Greyed = !server.Flags.HasFlag(GameFlags.Moderated); }
        hud.Bar.Storyteller = () => { Take(); return "the Storyteller button"; };
    }

    /// <summary>The scene index the pane greys by, which the scripts move too (the host's, `Net.SceneIndex`).</summary>
    SceneIndex Index => Powers.Server.SceneIndex;

    /// <summary>The scene pane's colour for a step already reached.</summary>
    public const uint ReachedRow = 0xff949494u;

    /// <summary>The game's advancement screen, opened on a character (`Program.cs`'s).</summary>
    public Action<WorldThing>? Advance { get; set; }

    /// <summary>
    /// The camera given a body carried to another place (`StoryView`); true when the
    /// window now looks there.
    /// </summary>
    public Func<WorldThing, bool>? Follow { get; set; }

    /// <summary>The game's one camera, which a seat click gives the thing clicked; null with no window.</summary>
    public Func<WorldCamera?>? Camera { get; set; }

    public StoryPowers Powers { get; }
    /// <summary>The Storyteller's interface: its bar and its panes.</summary>
    public FrontEnd Menus { get; }
    /// <summary>Whether the host sits in the seat.</summary>
    public bool On { get; private set; }

    void Say(string line)
    {
        if (line.Length == 0) return;
        session.Messages.Add(line);
        Console.WriteLine($"  st: {line}");
    }

    string Text(string key) => strings.Find(key) ?? key;

    /// <summary>
    /// **Jump to Location**: the current target (in the seat) to the location
    /// in row <paramref name="row"/>, alone (<see cref="HostPlaces.JumpTo"/>). A character nobody plays goes
    /// only while possessed, and then, being the current target, **the camera goes with it**:
    /// the window looks into its place (<see cref="Follow"/>, `StoryView`), and the
    /// target is its new thing there (ours: it has a new GUID). The Storyteller **goes on steering it
    /// there** (`StoryHand`): 1.1's control is one for the whole game and stays on the body, so here
    /// the body is made the steered one of its new place and the window's clicks go to it, until Possess
    /// is asked again (or another possession, Set Ghost, the host's journey) — the host's own place is
    /// steered by whoever it was before meanwhile (ours: steering is a place's own here).
    /// A player's possessed body leaves the Storyteller's hands as it goes (ours: 1.1 then sets the
    /// ghost, which is not known in detail). Answers the line to say, or nothing when nobody went.
    /// </summary>
    string JumpTo(int row)
    {
        if (chronicle?.At(row) is not { } there || Powers.Target is not { } who) return "";
        bool unplayed = !Powers.Server.Players.Any(p => ReferenceEquals(p.Avatar, who));
        if (!unplayed && ReferenceEquals(Powers.Server.Possessed, who)) Powers.Possess(null);
        // Whether the Storyteller holds the body is read before it goes. The carry takes it out of the host's
        // place (Location.Remove), whose steering passes on to the host's own character (SteerNext), and the
        // seat's one control (Steered) then ends the possession: read after the jump, the body was never held, so
        // it was not taken in hand in its new place ("civ07drunk is not in hand").
        bool held = unplayed && ReferenceEquals(Powers.Server.Possessed, who);
        if (!Powers.Server.Places.JumpTo(who, row)) return "";
        if (unplayed && Powers.Server.Places.Carried is { Place: not null } went && !ReferenceEquals(went, who))
        {
            if (ReferenceEquals(Powers.Server.Possessed, who)) Powers.Possess(null);    // the host's own place steered as before
            Powers.Target = went;
            // The control stays on the body (1.1's move leaves it alone): steered in its new place,
            // the window's clicks its orders, until the Storyteller lets it go (StoryHand).
            if (Follow?.Invoke(went) == true && held) StoryHand.Take(Powers.Server, went);
        }
        return Text(there.Name);
    }

    /// <summary>
    /// `/carry &lt;thing&gt; &lt;location&gt;` on the host's chat line (ours, a test aid like `/jump`): the
    /// Storyteller's three clicks at once — the thing (a GUID, or the first character of that template in the
    /// host's place) made the target, possessed, and jumped to the location (a row of the chronicle, by
    /// number or name) as <see cref="JumpTo"/> does. Null for any other line.
    /// </summary>
    public string? Command(string line)
    {
        var words = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3 || words[0] != "/carry") return null;
        if (chronicle is null) return "  carry: the game has no chronicle";
        var place = Powers.Place;
        var who = int.TryParse(words[1], out int guid) ? place.Things.FirstOrDefault(t => t.Guid == guid)
            : place.Things.FirstOrDefault(t => t.Actor is not null && t.Template.Equals(words[1], StringComparison.OrdinalIgnoreCase));
        if (who is null) return $"  carry: no {words[1]} here";
        int row = int.TryParse(words[2], out int n) ? n : chronicle.Find(words[2]);
        Powers.Target = who;
        Say(Powers.Possess(who));
        string went = JumpTo(row);
        return went.Length > 0 ? $"  carry: {who.Template} to {went}" : $"  carry: {who.Template} did not go";
    }

    // ---- taking and leaving the seat --------------------------------------------------------------

    /// <summary>
    /// The multiplayer bar's Storyteller button: only with Storyteller
    /// moderation (game flag 0x400) — without it 1.1 does nothing; Vitomir says why (ours).
    /// </summary>
    public void Take()
    {
        if (!Powers.Server.Flags.HasFlag(GameFlags.Moderated))
        {
            Say("The game was not made with Storyteller moderation: tick it in Game Setup before Start to use this.");
            return;
        }
        On = true;
        Powers.Server.Storyteller = true;
        Powers.Before ??= Powers.Place.Steered;
        hud.Enabled = false;
        Menus.ClearLayers();
        Menus.Show(Menus.Pane(BarFile));
        Refresh(force: true);
        Say(Text("UI_NET_STORYTELLER"));
    }

    /// <summary>
    /// The character button: the multiplayer bar back and the target made the
    /// one steered — and **the control is left as it is**: a body the Storyteller
    /// possessed stays his to play out of the seat, where a click on a thing is its order (the click's player
    /// arm) — attack, talk, pick up — until F6 or the seat's Possess gives the host's character back.
    /// (An earlier build let go here; this keeps the control.)
    /// </summary>
    public void Leave()
    {
        On = false;
        Powers.Server.Storyteller = false;
        Powers.Target = Powers.Server.Possessed ?? Powers.Place.Steered;
        Menus.ClearLayers();
        hud.Enabled = true;
    }

    /// <summary>
    /// The host's keys (<see cref="SeatKeys"/>): **\** takes the seat or leaves it (as
    /// the bar's two buttons do), **.** in the seat gives the host their own character back
    /// (<see cref="SeatKeys.Home"/>) and redoes the panes. The caller plays the sound.
    /// </summary>
    public void Key(SeatKey key)
    {
        if (key == SeatKey.Seat) { if (On) Leave(); else Take(); }
        else if (key == SeatKey.Home && SeatKeys.Home(Powers, On, Camera?.Invoke()))
        {
            Refresh(force: true);                                                    // the bar refreshed
            Console.WriteLine($"  story: . — back to {Powers.NameOf(Powers.Target!)}");
        }
    }

    /// <summary>
    /// **Tab in the seat**: the next player's character becomes the target
    /// (<see cref="StoryTab.Next"/>, then the steer — the camera to it, the ghost out, as a seat click's,
    /// <see cref="StoryPowers.Clicked(WorldThing, WorldThing?, WorldCamera?)"/>); one in another of the host's
    /// places takes the camera there (<see cref="Follow"/>, as a carried body does). False out of the seat.
    /// </summary>
    public bool Tab()
    {
        if (!On) return false;
        if (StoryTab.Next(Powers.Server.Slots.Select(p => p?.Avatar).ToList(), Powers.Target) is not { } next) return true;
        bool here = ReferenceEquals(next.Place, Powers.Place);
        Powers.Clicked(next, Powers.Server.Possessed ?? Powers.Place.Steered, here ? Camera?.Invoke() : null);   // the control, wherever it is
        if (!here) Follow?.Invoke(next);
        Refresh(force: true);                                                    // the bar refreshed
        Console.WriteLine($"  story: Tab — the target is {Powers.NameOf(next)}");
        return true;
    }

    /// <summary>
    /// Possess from the bar or the actor pane: <see cref="StoryPowers.Possess"/>, and when the body
    /// taken is in another of the host's places — one carried there and let go — the camera goes with it
    /// there (<see cref="Follow"/>), as it did when it was carried.
    /// </summary>
    string Possess(WorldThing? who)
    {
        string said = Powers.Possess(who);
        if (who is { Place: { } there } && !ReferenceEquals(there, Powers.Place) && ReferenceEquals(Powers.Server.Possessed, who)
            && Follow?.Invoke(who) != true)
            Say(Powers.Possess(who));                                // no view to take it: let it go again
        return said;
    }

    /// <summary>
    /// 1.1's control is one: when the host's place steers somebody else — F6, Tab, Set Ghost,
    /// a possession's end — a possession the Storyteller held there is over (ours in the way of it).
    /// </summary>
    void Steered(WorldThing who)
    {
        if (Powers.Server.Possessed is { } held && ReferenceEquals(held.Place, Powers.Place) && !ReferenceEquals(held, who))
            Powers.Server.Possessed = null;
    }

    // ---- input --------------------------------------------------------------------------------------

    /// <summary>
    /// An input event: the multiplayer bar's Storyteller button when the seat is empty; while it is
    /// taken, the Storyteller's panes' clicks, and a click on a thing in the world makes it the
    /// target and gives it the camera (the click's seat branch, <see cref="StoryPowers.Clicked(WorldThing, WorldThing?, WorldCamera?)"/>)
    /// — a click on the ground goes on to walk whoever the Storyteller steers. With the game's own menu up
    /// (<paramref name="menuUp"/>) none of it: the escape menu is a dialog and takes every click
    /// (<see cref="StoryClick.Route"/>, Vitomir's seat took them first, so Quit took several clicks).
    /// </summary>
    public bool Event(in LoopInput e, int width, int height, bool menuUp = false)
    {
        if (menuUp) return false;
        switch (e.Kind)
        {
            case LoopInputKind.PointerMove:
            {
                var (x, y) = StatusBar.FromWindow(e.X, e.Y, width, height);
                Menus.PointerAt(x, y);
                return false;
            }
            case LoopInputKind.ButtonDown when e.Code == LoopButton.Left:
            {
                var (x, y) = StatusBar.FromWindow(e.X, e.Y, width, height);
                if (!On)
                {
                    if (hud.Enabled && hud.Bar["sbSTButton"] is { Shown: true } st && st.Contains(x, y)) { Take(); return true; }
                    return false;
                }
                Menus.PointerAt(x, y);
                var thing = hovered();
                var to = StoryClick.Route(menuUp, Menus.Active() is not null, thing is not null);
                if (to == StoryClickTo.Pane)
                {
                    Menus.LeftHeld = true;
                    Menus.Press();
                    pressed = true;
                    return true;
                }
                if (to == StoryClickTo.Thing)
                {
                    // The click's seat branch: the target, the camera to it and the ghost out of the world
                    // when it is not the one steered — in the host's own place too.
                    Powers.Clicked(thing!, Powers.Place.Steered, Camera?.Invoke());
                    Refresh(force: true);                                                // the bar refreshed
                    return true;
                }
                return false;
            }
            case LoopInputKind.ButtonUp when e.Code == LoopButton.Left && pressed:
            {
                var (x, y) = StatusBar.FromWindow(e.X, e.Y, width, height);
                Menus.PointerAt(x, y);
                Menus.LeftHeld = false;
                Menus.Release();
                pressed = false;
                return true;
            }
            case LoopInputKind.Wheel when On && Menus.Active() is not null:
                Menus.Wheel(e.DY > 0 ? 1 : e.DY < 0 ? -1 : 0);
                return true;
        }
        return false;
    }

    /// <summary>Every control of the Storyteller's bar and panes, by name.</summary>
    bool Click(MenuPane pane, NuiElement e)
    {
        string name = e.Name.ToLowerInvariant();
        var target = Powers.Target;
        switch (name)
        {
            // ---- the bar ----
            case "sbobjectpanecheck" or "sblocationpanecheck" or "sbactorpanecheck" or "sbscenepanecheck" or "sbcontrolspanecheck":
                TogglePane(e.Name);
                break;
            case "sbpossessbutton": Say(Possess(target)); break;
            case "sbpausebutton": Powers.Pause(!Powers.Server.Paused); break;
            case "sbsetghostbutton":
                Say(Powers.SetGhost(target));
                FollowGhost();
                break;
            case "sbadvancementbutton":
                // On the server only, the advancement screen on **slot 0's
                // character** — the host's own, not the target.
                if (Powers.Server.Slots[0]?.Avatar is { } mine && Advance is { } open) open(mine);
                break;
            // Delete: the ghost is set where the target stood, so the camera may follow it.
            case "sbdeleteobjectbutton": Say(Powers.Delete(target)); FollowGhost(); break;
            case "sbstcharacterbutton": Leave(); return true;
            case "sbrevivebutton": Say(Powers.Revive(target)); break;
            case "sbcharmodebutton": Say(Powers.CycleDisposition(target)); break;

            // ---- the actor pane ----
            case "stapselect": if (Chosen() is { } a1) Powers.Target = a1; break;   // Select
            case "stappossess": Say(Possess(Chosen())); break;                      // Possess
            case "staprevive": Powers.ReviveChosen(Chosen()); break;                // Revive: no line
            // Talk/Neutral/Enemy: cycles (mode + 1) % 3 and sends the flags. Built on the
            // host; the disposition reaching the players' machines is the "things" flags' gap (message 4).
            case "stapcharmode": Say(Powers.CycleDisposition(Chosen())); break;
            case "stapdelete": Say(Powers.DeleteChosen(Chosen()) ?? ""); FollowGhost(); break;   // Delete: no protected test
            case "stapkill": Say(Powers.Kill(Chosen())); break;

            // ---- the controls pane ----
            case "stcpautost": Say(Powers.SetFlag(GameFlags.AutoStoryteller, !Powers.Server.Flags.HasFlag(GameFlags.AutoStoryteller))); break;
            case "stcpaicheck": Say(Powers.SetAi(!Powers.AiOn)); break;
            case "stcpgamelock": Say(Powers.SetFlag(GameFlags.Locked, !Powers.Server.Flags.HasFlag(GameFlags.Locked))); break;
            case "stcpcommlock": Say(Powers.SetFlag(GameFlags.CommLocked, !Powers.Server.Flags.HasFlag(GameFlags.CommLocked))); break;
            case "stcpkickbutton":
            {
                // Kick: after `UI_STCPKICKQUESTION` in a warning box; the host cannot kick itself.
                int slot = Menus.Pane(ControlFile).Lists["stcpCastList"].CurrentData;
                if (slot <= 0) break;
                string who = Powers.Server.Slots[slot]?.Name ?? "";
                Menus.Message(Text("UI_WARNING"), $"{Text("UI_STCPKICKQUESTION")} {who}?", ok: true, cancel: true, r =>
                {
                    if (r == 1) { Powers.Server.Kick(slot); Refresh(force: true); }
                });
                break;
            }

            // ---- the location pane ----
            case "stlpload":
            {
                // Jump to Location: the current target (in the seat), alone (HostPlaces).
                Say(JumpTo(Menus.Pane(LocationFile).Lists["stlpListBox"].CurrentData));
                break;
            }
            case "stlpjumpall":
            {
                // Jump All: the whole game to the chosen location — the chronicle's
                // own jump, which the host's journey then carries every player along on.
                int row = Menus.Pane(LocationFile).Lists["stlpListBox"].CurrentData;
                if (chronicle?.At(row) is { } there && chronicle.Jump(there.Name, 0)) Say(Text(there.Name));
                break;
            }

            // ---- the object pane ----
            case "stopcreate":
            {
                int row = Menus.Pane(ObjectFile).Lists["stopListBox"].CurrentData;
                if (row >= 0 && row < templateRows.Count) Say(Powers.Create(templateRows[row]));
                break;
            }
            // Short-list toggle: the box read, the list refilled from the chronicle's own
            // .nst quick list when it is on (<see cref="StoryPowers.ShortList"/>).
            case "stopshortlisttoggle": shortOnly = !shortOnly; FillObjects(); break;
            case "stopitemradio": kind = StoryPowers.Kind.Items; FillObjects(); break;     // filter 0
            case "stopactorradio": kind = StoryPowers.Kind.Actors; FillObjects(); break;   // filter 2
            case "stoppropradio": kind = StoryPowers.Kind.Props; FillObjects(); break;     // filter 1

            // ---- the scene pane ----
            case "stspadvance":
                // Advance: the next step, and the chronicle's `advance` — from the last step the
                // empty slot past the list, `advance(0, 0)` (<see cref="StoryPowers.Advance"/>).
                int at = Index.Reached;
                if (StoryPowers.Advance(ref at, steps.Count, i => steps[i]) is { } next) RunStep(at, next);
                break;
            case "stspskipto":
            {
                int row = Menus.Pane(SceneFile).Lists["stspListBox"].CurrentData;
                if (row >= 0 && row < steps.Count) RunStep(row);
                break;
            }
            default:
                return false;
        }
        Refresh(force: name.StartsWith("sb") || name.StartsWith("stap"));
        return true;
    }

    void RunStep(int at) => RunStep(at, steps[at]);

    void RunStep(int at, StoryPowers.Step s)
    {
        Index.Reached = at;
        bool ran = scenes?.AdvanceChronicle(s.Scene, s.Subscene) == true;
        if (at >= steps.Count) Console.WriteLine($"  st: past the last step, advance({s.Scene}, {s.Subscene}){(ran ? "" : " — no script to advance")}");
        else Say($"{Powers.Row(s)}{(ran ? "" : " — the chronicle has no script to advance")}");
        FillScenes();
    }

    /// <summary>A ghost set in another of the host's places (Set Ghost or Delete on a target there): the camera goes with it.</summary>
    void FollowGhost()
    {
        if (Powers.Ghost is { InWorld: true, Place: { } away } ghost && !ReferenceEquals(away, Powers.Place)) Follow?.Invoke(ghost);
    }

    /// <summary>The thing on the actor list's chosen row, or null.</summary>
    WorldThing? Chosen()
    {
        int row = Menus.Pane(ActorFile).Lists["stapListBox"].CurrentData;
        return row >= 0 && row < actorRows.Count ? actorRows[row] : null;
    }

    /// <summary>A pane box: its pane shown or hidden, and the other panes of its side put away (the `visGroup`).</summary>
    void TogglePane(string box)
    {
        var (_, file, side) = PaneBoxes.First(p => p.Box.Equals(box, StringComparison.OrdinalIgnoreCase));
        var pane = Menus.Pane(file);
        bool show = !Menus.IsUp(pane);
        foreach (var (_, other, otherSide) in PaneBoxes)
            if (otherSide == side) Menus.Hide(Menus.Pane(other));
        if (show) Menus.Show(pane);
    }

    /// <summary>
    /// The object list's hover (<see cref="NetRollovers.Object"/>): the hovered row's template
    /// in the small box, nothing with no row. <paramref name="on"/> says whether the pointer is on the list at all —
    /// when it is not, the other rollovers are asked.
    /// </summary>
    public (IReadOnlyList<RolloverLine>, IReadOnlyList<RolloverLine>, bool, object)? ObjectRollover(out bool on)
    {
        var pane = Menus.Pane(ObjectFile);
        on = On && Menus.IsUp(pane) && ReferenceEquals(Menus.Active(), pane)
             && pane.ControlAt(Menus.PointerX, Menus.PointerY)?.Name.Equals("stopListBox", StringComparison.OrdinalIgnoreCase) == true;
        return on ? NetRollovers.Object(pane.Lists["stopListBox"], templateRows, Powers.Place.Templates, strings.Find) : null;
    }

    /// <summary>A still's pointer (`--pointer`): the panes' pointer put there and their hover run — as a mouse movement does (ours).</summary>
    public void PointAt(float x, float y)
    {
        Menus.PointerAt(x, y);
        Menus.Frame(Menus.NowMs);
    }

    // ---- every frame --------------------------------------------------------------------------------

    public void Frame(int nowMs)
    {
        if (!On) return;
        if (startedMs < 0) startedMs = nowMs;
        Menus.Frame(nowMs);
        if (nowMs >= nextRefresh)
        {
            nextRefresh = nowMs + 1000;
            Refresh();
        }
        // The elapsed time, `%02d: %02d: %02d`.
        int secs = Math.Max(0, (nowMs - startedMs) / 1000);
        Menus.Pane(ControlFile).Texts["stcpElapsedTime"] = $"{secs / 3600:00} : {secs / 60 % 60:00} : {secs % 60:00}";
        // What the chosen rows say (the lists' select hooks).
        Menus.Pane(ActorFile).Texts["stapCurrentActor"] = Chosen() is { } a ? Powers.NameOf(a) : "";
        int cast = Menus.Pane(ControlFile).Lists["stcpCastList"].CurrentData;
        Menus.Pane(ControlFile).Texts["stcpSelectedBox"] = cast >= 0 ? Powers.Server.Slots[cast]?.Name ?? "" : "";
        int obj = Menus.Pane(ObjectFile).Lists["stopListBox"].CurrentData;
        Menus.Pane(ObjectFile).Texts["stopItemName"] = obj >= 0 && obj < templateRows.Count ? templateRows[obj] : "";
        int loc = Menus.Pane(LocationFile).Lists["stlpListBox"].CurrentData;
        Menus.Pane(LocationFile).Texts["stlpLocationName"] = loc >= 0 ? Text(chronicle?.At(loc)?.Name ?? "") : "";
        Menus.Drawn();
    }

    /// <summary>
    /// The lists filled afresh (once a second, and after the controls that change them), keeping each
    /// one's choice. The actor pane's own show hook refreshes the ST bar and fills its list
    /// just when it is raised; here it is kept live instead.
    /// </summary>
    internal void Refresh(bool force = false)
    {
        Menus.Pane(BarFile).Texts["sbCurrentTarget"] = Powers.Target is { InWorld: true } t ? Powers.NameOf(t) : "";
        FillActors();
        FillCast();
        if (force || templateRows.Count == 0) FillObjects();
        if (force) { FillLocations(); FillScenes(); }
    }

    void FillActors()
    {
        var list = Menus.Pane(ActorFile).Lists["stapListBox"];
        var keep = Chosen();
        int scroll = list.Scroll;
        var rows = Powers.Actors().ToList();
        actorRows = [.. rows.Select(r => r.Who)];
        list.Clear();
        // The players' rows in 0xff317ff4, the rest white.
        for (int i = 0; i < rows.Count; i++) list.Add(rows[i].Row, i, rows[i].Colour);
        list.Select(Math.Max(0, keep is null ? 0 : actorRows.IndexOf(keep)));
        list.ScrollTo(scroll);
    }

    void FillCast()
    {
        var list = Menus.Pane(ControlFile).Lists["stcpCastList"];
        int keep = list.CurrentData;
        castRows = [.. Powers.Cast()];
        list.Clear();
        foreach (var (row, slot) in castRows)
        {
            list.Add(row, slot);
            if (Powers.Server.Players.FirstOrDefault(p => p.Slot == slot)?.Host == true) list.Disable(list.Rows.Count - 1);   // the host's own row greyed
        }
        list.Select(Math.Max(0, castRows.FindIndex(c => c.Slot == keep)));
    }

    // The object pane's show hook sets the short-list box and chosen radio (read live via
    // `Checked` here instead) and refills the list, which this does too, on first show (below).
    void FillObjects()
    {
        templateRows = [.. Powers.Templates(kind, shortOnly && shortList.Count > 0 ? shortList : null)];
        var list = Menus.Pane(ObjectFile).Lists["stopListBox"];
        list.Clear();
        for (int i = 0; i < templateRows.Count; i++) list.Add(templateRows[i], i);
        list.Select(0);
    }

    void FillLocations()
    {
        var list = Menus.Pane(LocationFile).Lists["stlpListBox"];
        list.Clear();
        if (chronicle is null) return;
        int here = -1;
        for (int i = 0; i < chronicle.Count; i++)
        {
            var loc = chronicle.At(i)!;
            bool current = loc.LevelName.Equals(Powers.Place.Name, StringComparison.OrdinalIgnoreCase);
            // The current one `"%s *"` and chosen.
            string name = Text(loc.Name);
            list.Add(current ? $"{name} *" : name, i);
            if (current && here < 0) here = list.Rows.Count - 1;
        }
        list.Select(Math.Max(0, here));
    }

    // The scene pane's show hook: the .nss steps listed "%s [%d,%d]".
    void FillScenes()
    {
        // The steps already reached are grey (0xff949494) — by the host's index, which the
        // scripts' SetNSSIndex moves as well as the two buttons.
        var list = Menus.Pane(SceneFile).Lists["stspListBox"];
        list.Clear();
        for (int i = 0; i < steps.Count; i++)
        {
            list.Add(Powers.Row(steps[i]), i, Index.Greyed(i) ? ReachedRow : MenuList.RowColour);
            if (Index.Greyed(i)) list.Disable(i);                   // and out of the choosing
        }
        list.Select(Math.Max(0, Math.Min(Index.Reached + 1, steps.Count - 1)));
    }

    /// <summary>
    /// The check boxes' pictures: the pane boxes lit for the panes up, Possess while possessing, Pause
    /// while paused, the controls pane's four, and the object pane's toggle and radio boxes. The
    /// controls pane's show hook ticks Auto ST, Game Lock, AI and Comm Lock once, fills the
    /// cast list and refreshes the four boxes every 100 ms; here they are read live below
    /// instead, and the list is kept live by <see cref="FillCast"/>.
    /// </summary>
    public IEnumerable<(string Art, float X, float Y, float W, float H)> Checkboxes() => Menus.Visible().SelectMany(Checkboxes);

    /// <summary>
    /// One pane's boxes (<see cref="Checkboxes()"/>), for the panes' pass to draw right after that pane (`MenuPass.Pictures`),
    /// so a pane or a dialog drawn after it covers them as 1.1's pane order does.
    /// </summary>
    public IEnumerable<(string Art, float X, float Y, float W, float H)> Checkboxes(MenuPane pane)
    {
        if (!On) yield break;
        foreach (var e in pane.Nui.Elements.Where(e => e.Type == "checkbox" && e.Pos.Length > 1 && e.Size.Length > 1))
        {
            string? art = Checked(e.Name) ? e.Get("checkedbitmap") : e.Get("bitmap");
            if (art is { Length: > 0 }) yield return (art, pane.X + e.Pos[0], pane.Y + e.Pos[1], e.Size[0], e.Size[1]);
        }
    }

    bool Checked(string box) => box.ToLowerInvariant() switch
    {
        "sbobjectpanecheck" => Menus.IsUp(Menus.Pane(ObjectFile)),
        "sblocationpanecheck" => Menus.IsUp(Menus.Pane(LocationFile)),
        "sbactorpanecheck" => Menus.IsUp(Menus.Pane(ActorFile)),
        "sbscenepanecheck" => Menus.IsUp(Menus.Pane(SceneFile)),
        "sbcontrolspanecheck" => Menus.IsUp(Menus.Pane(ControlFile)),
        "sbpossessbutton" => Powers.Server.Possessed is not null,
        "sbpausebutton" => Powers.Server.Paused,
        "stcpautost" => Powers.Server.Flags.HasFlag(GameFlags.AutoStoryteller),
        "stcpaicheck" => Powers.AiOn,
        "stcpgamelock" => Powers.Server.Flags.HasFlag(GameFlags.Locked),
        "stcpcommlock" => Powers.Server.Flags.HasFlag(GameFlags.CommLocked),
        "stopshortlisttoggle" => shortOnly,
        "stopitemradio" => kind == StoryPowers.Kind.Items,
        "stopactorradio" => kind == StoryPowers.Kind.Actors,
        "stoppropradio" => kind == StoryPowers.Kind.Props,
        _ => false,
    };

    /// <summary>For a headless still: the seat taken with these pane boxes ticked (`--st [boxes]`).</summary>
    public void Shot(string? boxes)
    {
        Take();
        foreach (var box in (boxes ?? "sbActorPaneCheck,sbControlsPaneCheck").Split(',', StringSplitOptions.RemoveEmptyEntries))
            TogglePane(box.Trim());
        Refresh(force: true);
        Frame(1000);
    }
}
