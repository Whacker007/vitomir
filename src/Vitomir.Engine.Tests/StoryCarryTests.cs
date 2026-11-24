// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Net;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The Storyteller's carried body** (`Net.StoryHand`, `Campaign.Visit`; 1.1's mover's last
/// branch). In a play-test, `/carry civ07drunk 1` on a
/// windowed host stood the host's own character beside the drunk in the Temesvar street, and the drunk
/// took no orders there. A place loaded for somebody else has nobody of the party in it — only the
/// party's own journey stands it in a place — and 1.1's control stays on the carried body, which walks
/// on the Storyteller's clicks in its new place until he lets it go. Driven as the windowed host's run
/// is wired: *To Curse the Darkness*' inn, the places built with the host's character template and
/// awakened, the host's places loaded through its campaign.
/// </summary>
public class StoryCarryTests
{
    const int Temesvar = 1;
    const string HostTemplate = "MP_Christof_DA";

    internal sealed record Host(GameServer Server, Location Inn, Campaign Campaign) : IDisposable
    {
        public void Dispose() => Server.Dispose();
    }

    internal static Host Open(Vfs.GameFiles files)
    {
        var story = ChronicleSet.Load(files)["MP_CurseDarkness"]!;
        var settings = GameSettings.For(story, "Hosta's game", GameFlags.AutoStoryteller);
        var ai = new Ai.AiClasses(files);
        var session = new Session { Chronicle = story };
        // The windowed host's own build: its character at the entrance of every place it loads (Program).
        Location Build(string level, string? scene, int entrance)
        {
            var built = Location.Load(files, level, scene: scene, player: HostTemplate, entrance: entrance);
            built.Session = session;
            built.Awaken(ai);
            return built;
        }
        story.Current = story.Data.ReviveLocation;
        var at = story.At(story.Current)!;
        var inn = Build(at.LevelName, at.SceneName.Length > 0 ? at.SceneName : null, settings.Entrance);
        session.Here = inn;
        var server = new GameServer(settings, new NetPeer(0)) { Qol = new Qol(), Fixes = new Fixes() };
        var campaign = new Campaign { Chronicle = story, Build = Build, Here = inn };
        campaign.Host();
        server.Places.Chronicle = story;
        campaign.Occupied = server.Places.Kept;
        campaign.Discarded += server.Places.Forget;
        server.Places.Load = (index, entrance) => campaign.Visit(index, entrance);
        server.Start();
        server.Enter(inn, inn.Player?.Thing);
        return new Host(server, inn, campaign);
    }

    /// <summary>The drunk possessed in the inn and jumped to the Temesvar street, as `/carry civ07drunk 1` does.</summary>
    static WorldThing Carry(Host host)
    {
        var drunk = host.Inn.Things.First(t => t.Template.Equals("civ07drunk", StringComparison.OrdinalIgnoreCase));
        Assert.True(host.Inn.SetSteered(drunk));
        host.Server.Possessed = drunk;
        Assert.True(host.Server.Places.JumpTo(drunk, Temesvar));
        var body = host.Server.Places.Carried!;
        Assert.NotNull(body.Place);
        host.Server.Places.Watched = body.Place;
        return body;
    }

    static void Step(Host host, float seconds)
    {
        for (float t = 0; t < seconds; t += 1f / 30) host.Server.Places.Step(1f / 30);
    }

    internal static Vector3 FloorNear(Location place, Vector3 from, float distance)
    {
        var map = place.Walk!;
        for (int a = 0; a < 16; a++)
        {
            float r = a * MathF.PI / 8;
            var p = from + new Vector3(MathF.Cos(r), MathF.Sin(r), 0) * distance;
            int node = map.Find(p, 256f);
            if (map.IsGround(node) && map.Nodes[node].Contains(new Vector2(p.X, p.Y))) return p with { Z = map.Nodes[node].Height(p.X, p.Y) };
        }
        throw new InvalidOperationException($"no floor {distance} units from {from}");
    }

    static string? Motion(WorldThing thing) =>
        thing.Place!.Mesh.Animated.FirstOrDefault(a => ReferenceEquals(thing.Place.Drawn[a.Thing], thing))?.Animation.Mode?.Name;

    /// <summary>
    /// The host's character does not stand in a place loaded for somebody else (the load builds the world
    /// alone; the arrival stands the party only on its own journey): the carried drunk is alone at
    /// the start, and the host's character is put back when the host goes there itself.
    /// </summary>
    [Fact]
    public void A_place_loaded_for_somebody_else_has_nobody_of_the_host_s_party_in_it()
    {
        using var files = Install.Open();
        using var host = Open(files);
        var body = Carry(host);
        var street = body.Place!;
        Assert.False(street.Player!.Thing.InWorld);
        Assert.DoesNotContain(street.Things, t => t.Template.Equals(HostTemplate, StringComparison.OrdinalIgnoreCase));

        // The host's own journey there stands its character at the entrance, in the world.
        Assert.True(host.Campaign.Chronicle.Jump(host.Campaign.Chronicle.NameOf(Temesvar), 0));
        var arrival = host.Campaign.Travel()!.Value;
        Assert.Same(street, arrival.To);
        Assert.True(arrival.Returned);
        Assert.True(street.Player!.Thing.InWorld);
    }

    /// <summary>
    /// **The Storyteller goes on steering the body there** (the mover leaves the control on it): taken in
    /// hand, its own mind does not walk it; a click on the ground ahead of it (the ray straight down) walks it
    /// there with its walk; let go, its mind has it again.
    /// </summary>
    [Fact]
    public void The_storyteller_walks_the_body_he_carried_in_its_new_place_until_he_lets_it_go()
    {
        using var files = Install.Open();
        using var host = Open(files);
        var body = Carry(host);
        var street = body.Place!;
        Assert.True(StoryHand.Take(host.Server, body));
        Assert.Same(body, host.Server.Possessed);
        Assert.Same(body, street.Steered);
        Assert.True(StoryHand.Holds(body));

        var stood = body.Position;
        Step(host, 3f);
        Assert.True(Vector3.Distance(stood, body.Position) < 1f, "the body's own mind walked it while in hand");

        var spot = FloorNear(street, body.Position, 150f);
        string said = StoryHand.Walk(body, spot + new Vector3(0, 0, 200f), -Vector3.UnitZ, InputModifier.None);
        Assert.StartsWith("walking", said);
        Step(host, 0.5f);
        Assert.Contains("walk", Motion(body) ?? "", StringComparison.OrdinalIgnoreCase);
        Step(host, 4f);
        Assert.True(Vector2.Distance(new(body.Position.X, body.Position.Y), new(spot.X, spot.Y)) < 40f,
                    $"the body stopped at {body.Position}, not near {spot}");

        // The keys push it too (the QoL `driving`).
        var before = body.Position;
        for (int i = 0; i < 30; i++) { StoryHand.Drive(body, Vector2.UnitX, run: false, 1f / 30); host.Server.Places.Step(1f / 30); }
        Assert.True(Vector3.Distance(before, body.Position) > 10f, "the push did not move it");

        StoryHand.LetGo(body);
        Assert.False(StoryHand.Holds(body));
        Assert.NotSame(body, street.Minds!.Steered);
        Assert.StartsWith("the body is not in hand", StoryHand.Walk(body, spot + new Vector3(0, 0, 200f), -Vector3.UnitZ, InputModifier.None));
    }

    /// <summary>
    /// **The pointer and the clicks in the carried body's place** (cast from the camera's
    /// sector). Out of the seat the pointer offers the body's player what it would offer
    /// any player — never the body itself — and a click on it is the body's order by the cursor's verb; in
    /// the seat it offers anything but the target, and a click makes the thing the target and takes the
    /// ghost out of the world when it is not the one steered.
    /// </summary>
    [Fact]
    public void The_pointer_offers_the_body_s_place_and_a_click_is_its_order_or_the_seat_s_target()
    {
        using var files = Install.Open();
        using var host = Open(files);
        var body = Carry(host);
        var street = body.Place!;
        Assert.True(StoryHand.Take(host.Server, body));
        var down = -Vector3.UnitZ;

        // Steeply down on the body: out of the seat it is never offered to itself. Not straight down,
        // and not from straight over its axis: the engine's cylinder test never meets either.
        var above = body.Position + new Vector3(60f, 0, 300f);
        var onto = Vector3.Normalize(body.Position + new Vector3(0, 0, 40f) - above);
        Assert.NotSame(body, StoryHand.Point(body, above, onto, seat: false, target: body, out _, out var ground));
        Assert.NotNull(ground);
        // In the seat, anything but the target: the body is offered once somebody else is the target.
        var other = street.Things.First(t => t.InWorld && !ReferenceEquals(t, body) && t.Type == ThingType.Actor);
        Assert.Same(body, StoryHand.Point(body, above, onto, seat: true, target: other, out _, out _));

        // Something the body's player could act on, and the click on it is the body's order.
        var thing = street.Things.FirstOrDefault(t => t.InWorld && !ReferenceEquals(t, body)
            && Ui.Highlight.Offers(body, t) && Rpg.Cursor.Verb(body, t) != Rpg.CursorVerb.None);
        if (thing is not null)
        {
            Assert.True(StoryHand.Order(body, thing, InputModifier.None, out var said));
            Assert.Matches("^(attacking|picking up|using|pulling)", said);
        }
        // A thing with no verb is not an order: the click goes on to the ground.
        var inert = street.Things.FirstOrDefault(t => t.InWorld && !ReferenceEquals(t, body) && Rpg.Cursor.Verb(body, t) == Rpg.CursorVerb.None);
        if (inert is not null) Assert.False(StoryHand.Order(body, inert, InputModifier.None, out _));

        // The walk answers the spot the ring goes to (the move order's end).
        var spot = FloorNear(street, body.Position, 150f);
        Assert.StartsWith("walking", StoryHand.Walk(body, spot + new Vector3(0, 0, 200f), down, InputModifier.None, out var walked));
        Assert.NotNull(walked);

        // In the seat: the click makes the thing the target and the ghost, not steered, leaves the world.
        var powers = new StoryPowers(host.Server, host.Inn, null);
        powers.SetGhost(host.Inn.Player!.Thing);
        var ghost = powers.Ghost!;
        host.Inn.SetSteered(host.Inn.Player!.Thing);
        Assert.True(powers.Clicked(other, body));
        Assert.Same(other, powers.Target);
        Assert.False(ghost.InWorld);
    }

    /// <summary>
    /// **Possess again on a body let go in another place**: 1.1's control is one, so the body is
    /// steered in its own place again — the host's place keeps its own — and a second Possess lets it go.
    /// </summary>
    [Fact]
    public void Possess_takes_a_body_let_go_in_another_place_back_in_hand_there()
    {
        using var files = Install.Open();
        using var host = Open(files);
        var body = Carry(host);
        var powers = new StoryPowers(host.Server, host.Inn, null);
        var mine = host.Inn.Steered;
        StoryHand.LetGo(body);
        host.Server.Possessed = null;
        Assert.False(StoryHand.Holds(body));

        Assert.NotEqual("", powers.Possess(body));
        Assert.True(StoryHand.Holds(body));
        Assert.Same(body, host.Server.Possessed);
        Assert.Same(mine, host.Inn.Steered);

        powers.Possess(body);
        Assert.Null(host.Server.Possessed);
    }

    /// <summary>
    /// **The carried body's mind goes as it is** (the mover's last branch, `MoverMinds.Carried`): no
    /// possession ended and no reset — those are a player's — so the drunk, afraid in the inn, is afraid in
    /// the street, and his interest spots are the street's.
    /// </summary>
    [Fact]
    public void A_carried_body_s_mind_goes_as_it_is_with_the_new_place_s_spots()
    {
        using var files = Install.Open();
        using var host = Open(files);
        var drunk = host.Inn.Things.First(t => t.Template.Equals("civ07drunk", StringComparison.OrdinalIgnoreCase));
        var was = drunk.Mind!;
        was.Enter(Ai.AiMode.Afraid, andBase: false);
        was.HasNoticed = true;
        var body = Carry(host);
        var mind = body.Mind!;
        Assert.NotSame(was, mind);
        Assert.Equal(Ai.AiMode.Afraid, mind.Mode);
        Assert.Equal(was.BaseMode, mind.BaseMode);
        Assert.True(mind.HasNoticed);
        Assert.Equal(body.Place!.Minds!.Spots.Run(mind.Class.InterestNum), (mind.FirstSpot, mind.SpotCount));
    }
    // ---- the view's click, the held button, the seat's click and Alt in the host's own place ----------

    /// <summary>
    /// **A click in the carried body's view goes through the pixel under the pointer**. The view draws the
    /// street into the rows above the status bar (`FrameRenderer.WorldViewport`), and its pick asked the host's
    /// renderer for its rows — which, stepped and never drawn while the view is up, answered the whole window.
    /// With a carried drunk and the bar up, the white dot for the move location was well above where the mouse
    /// pointer was. A spot on the cobbles is projected as the view draws it, and the click through that pixel
    /// with the view's rows (`StoryView.Ray`) walks the body to that spot; through the whole window's rows it
    /// lands far off, which was the fault.
    /// </summary>
    [Fact]
    public void A_click_in_the_carried_body_s_view_walks_to_the_ground_drawn_under_the_pointer()
    {
        using var files = Install.Open();
        using var host = Open(files);
        var body = Carry(host);
        var street = body.Place!;
        Assert.True(StoryHand.Take(host.Server, body));
        const int W = 1600, H = 900;
        int rows = (int)MathF.Round(H * Ui.StatusBar.WorldShare);           // FrameRenderer.WorldRows, the bar up
        var spot = FloorNear(street, body.Position, 150f);
        // The game camera's kind of look — above and behind, down at the body — from a side with a clear sight of the spot.
        FreeCamera? eye = null;
        for (int yaw = 0; yaw < 360 && eye is null; yaw += 30)
        {
            var c = new FreeCamera { Yaw = yaw, Pitch = -40f };
            c.Orbit(body.Position + new Vector3(0, 0, 64f), 500f);
            if (WorldSweep.Pointer(street, c.Position, Vector3.Normalize(spot - c.Position)) is { } seen && Vector3.Distance(seen.Point, spot) < 2f)
                eye = c;
        }
        Assert.NotNull(eye);
        // Where the view draws the spot: exactly what `Render.FrameRenderer.WorldViewport` builds, into the rows above the bar.
        float fovY = 2f * MathF.Atan(MathF.Tan(eye.VerticalFov * 0.5f) * rows / H);
        var clip = Vector4.Transform(new Vector4(spot, 1f), eye.View * Matrix4x4.CreatePerspectiveFieldOfView(fovY, W / (float)rows, eye.Near, eye.Far));
        float px = (clip.X / clip.W * 0.5f + 0.5f) * W - 0.5f, py = (0.5f - clip.Y / clip.W * 0.5f) * rows - 0.5f;
        Assert.True(px >= 0 && px < W && Pick.OnWorld(py, H, rows), $"the spot is drawn at {px:0},{py:0}, off the world");

        var (o, d) = Pick.Ray(eye, px, py, W, H, rows, ViewColumns.Whole);
        StoryHand.Walk(body, o, d, InputModifier.None, out var walked);
        Assert.NotNull(walked);
        Assert.True(Vector3.Distance(walked.Value.Point, spot) < 2f, $"the click walked to {walked.Value.Point}, not the {spot} drawn under the pointer");

        var (o2, d2) = Pick.Ray(eye, px, py, W, H, H, ViewColumns.Whole);
        var wrong = WorldSweep.Pointer(street, o2, d2);
        Assert.True(wrong is null || Vector3.Distance(wrong.Value.Point, spot) > 60f,
                    $"through the whole window's rows the click met {wrong?.Point}, too near {spot} to show the fault");
    }

    /// <summary>
    /// **A held left button swings the body in hand again** (`StoryHand.Repeat`). Holding the
    /// left button repeats the attack in 1.1; an earlier view gave one swing a click. With the *auto repeat attack*
    /// option and the button held on somebody to attack, the drunk's swing ends in another; let go, the next
    /// swing's end stops him — and a body not in hand (not the control) is never asked.
    /// </summary>
    [Fact]
    public void A_held_button_swings_the_body_in_hand_again_at_what_the_pointer_is_over()
    {
        using var files = Install.Open();
        using var host = Open(files);
        var body = Carry(host);
        var street = body.Place!;
        Assert.True(StoryHand.Take(host.Server, body));
        // Somebody at the drunk's elbow, of a team he will fight, standing still.
        var foe = street.Things.First(t => t.InWorld && !ReferenceEquals(t, body) && t.Type == ThingType.Actor && t.Actor is { Dead: false } a
                                           && !a.Has(Actor.Flag.Pickup) && (a.Flags & (Ai.Minds.LeaveAloneFlags | Rpg.Actions.Unattackable)) == 0);
        foe.Actor!.Team = Actor.Teams.PlayerFirst;
        foe.Actor.Flags |= Actor.Flag.AiPaused;
        street.Move(foe, body.Position + new Vector3(30f, 0, 0));
        Assert.Equal(Rpg.CursorVerb.Attack, Rpg.Cursor.Verb(body, foe));
        body.Actor!.Weapon ??= new Rpg.Weapon { Name = "sword" };           // a drunk carries nothing to swing

        // A swing is counted when one starts: the action turning to a swing, or a swing re-stamping
        // its busy time. A held button's next swing comes inside the attack order itself,
        // so the action can stay a swing from one to the next.
        int Swings(float seconds, bool held)
        {
            int swings = 0;
            bool was = body.Actor!.Action == Rpg.ActionMode.Swing;
            long busy = body.Actor.BusyUntil;
            for (float t = 0; t < seconds; t += 1f / 30)
            {
                StoryHand.Repeat(body, option: true, foe, leftHeld: held, middleHeld: false, InputModifier.None);
                host.Server.Places.Step(1f / 30);
                bool now = body.Actor.Action == Rpg.ActionMode.Swing;
                if (now && (!was || body.Actor.BusyUntil > busy)) swings++;
                was = now;
                busy = body.Actor.BusyUntil;
            }
            return swings;
        }

        Assert.True(StoryHand.Order(body, foe, InputModifier.None, out var said), said);
        Assert.StartsWith("attacking", said);
        Assert.True(Swings(4f, held: true) >= 2, "the held button did not swing the body again");
        Swings(2f, held: false);
        Assert.Equal(0, Swings(2f, held: false));
        Assert.NotEqual(Rpg.ActionMode.Swing, body.Actor!.Action);
        // A click with the button let go at once is one swing (as every click once was).
        // (In reach the swing starts inside the order itself, so it is counted there.)
        Assert.True(StoryHand.Order(body, foe, InputModifier.None, out said), said);
        int inOrder = body.Actor.Action == Rpg.ActionMode.Swing ? 1 : 0;
        Assert.Equal(1, inOrder + Swings(4f, held: false));

        // Not in hand, not asked: its swing's end cancels.
        StoryHand.Repeat(body, option: true, foe, leftHeld: true, middleHeld: false, InputModifier.None);
        Assert.NotNull(body.Actor.Repeating);
        StoryHand.LetGo(body);
        StoryHand.Repeat(body, option: true, foe, leftHeld: true, middleHeld: false, InputModifier.None);
        Assert.Null(body.Actor.Repeating);
    }

    /// <summary>
    /// **The seat's click in the host's own place** (the click's seat branch): as in the carried body's place, the
    /// thing becomes the target, **the camera goes to it** and the ghost, when it is in the world and not
    /// the one steered, is taken out. An earlier build made it the target and nothing more. A region takes nothing.
    /// </summary>
    [Fact]
    public void A_seat_click_in_the_host_s_own_place_gives_the_thing_the_camera_and_takes_the_ghost_out()
    {
        using var files = Install.Open();
        using var host = Open(files);
        var inn = host.Inn;
        var mine = inn.Player!.Thing;
        var powers = new StoryPowers(host.Server, inn, null);
        var camera = new WorldCamera();
        camera.Start(mine);
        powers.SetGhost(mine);
        var ghost = powers.Ghost!;
        Assert.True(inn.SetSteered(mine));
        var townsman = inn.Things.First(t => t.InWorld && t.Type == ThingType.Actor && !ReferenceEquals(t, mine) && !ReferenceEquals(t, ghost));

        Assert.True(powers.Clicked(townsman, inn.Steered, camera));
        Assert.Same(townsman, powers.Target);
        Assert.Same(townsman, camera.Subject);
        Assert.False(ghost.InWorld);
        Assert.Same(mine, inn.Steered);                                  // no order, no steering

        if (inn.Things.FirstOrDefault(t => t.InWorld && t.Type == ThingType.Region) is { } region)
        {
            Assert.False(powers.Clicked(region, inn.Steered, camera));
            Assert.Same(townsman, camera.Subject);
        }
    }

    /// <summary>
    /// **The ground move, in the seat** (the ground click's first branch): the target stops and is put at the spot,
    /// its sector found again; with Space too it only turns to face it. 1.1's
    /// key is bit 0x4 of the click's mask — Alt (DirectInput's LMENU/RMENU), not Ctrl; it is the key
    /// command `storymove`, Alt and the right button by default (`StorySeatTests`). Out of the seat the arm is
    /// not taken and the right button casts as ever.
    /// </summary>
    [Fact]
    public void Alt_and_the_right_button_on_the_ground_in_the_seat_put_the_target_there()
    {
        using var files = Install.Open();
        using var host = Open(files);
        var inn = host.Inn;
        var powers = new StoryPowers(host.Server, inn, null);
        var townsman = inn.Things.First(t => t.InWorld && t.Type == ThingType.Actor && !ReferenceEquals(t, inn.Player!.Thing));
        powers.Target = townsman;
        var spot = FloorNear(inn, townsman.Position, 120f);
        var was = townsman.Position;

        host.Server.Storyteller = false;
        Assert.Null(powers.Placed(inn, spot, InputModifier.Alt));        // out of the seat: the cast's
        host.Server.Storyteller = true;

        var facing = townsman.Orientation;
        Assert.Contains("turns", powers.Placed(inn, spot, InputModifier.Alt | InputModifier.Space));
        Assert.Equal(was, townsman.Position);
        Assert.NotEqual(facing, townsman.Orientation);

        facing = townsman.Orientation;
        Assert.Contains("put at", powers.Placed(inn, spot, InputModifier.Alt));
        Assert.Equal(spot, townsman.Position);
        Assert.Equal(facing, townsman.Orientation);                      // the transform's turn kept
        Assert.Equal(inn.At(spot, townsman.Sector), townsman.Sector);
        Assert.Equal("no ground under the pointer", powers.Placed(inn, null, InputModifier.Alt));
        host.Server.Storyteller = false;
    }
}
