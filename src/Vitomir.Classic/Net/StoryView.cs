// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Storyteller's camera gone with a body it carried to another place:
// the move's last branch moves a possessed character nobody plays and,
// when it is the Storyteller's current target, hands it to the camera. The windowed host
// draws that place in its one window until the camera follows somebody else.
using System.Numerics;
using Vitomir.Engine.Net;
using Vitomir.Engine.World;
using Vitomir.Classic;

namespace Vitomir;

/// <summary>
/// **The camera following a moved possessed body**. After the mover has stood the
/// body in the other place and on the ground, it asks for the Storyteller's current
/// target (in the seat, the Storyteller's chosen thing) and, when that is the body, gives the
/// game's one camera the body to follow: it becomes the subject — the old one
/// kept for the handover, which slides only within 1,000 units and so not across places — and takes the
/// body's **sector** as the camera's (the body's sector given to the camera), leaving the camera's world
/// the host's. The world is drawn from that sector (the camera's sector goes to the portal
/// walk), so the host's view is the other place, and the camera's place is not let go while it
/// is there (the clean-up passes over it). The camera moves on (its sector from the
/// subject's position) with the body. Nothing turns it back but the camera being given somebody
/// else — a possession, a selection, the host's own journey.
///
/// Here the host's run draws one place with one renderer, so a second renderer is made for the other
/// place and drawn in the window instead (<see cref="Draw"/>), from the camera following the body, while
/// the host's own place goes on being stepped without being drawn. **Ours**: that place is stepped by the
/// host's list of places (<see cref="HostPlaces.Kept"/>), as a place a player is in, and its things animate
/// with it. **The hand goes with the camera**: a possessed body stays possessed there
/// (`StoryHand.Take`), and while the view is up the window's click on the ground is the body's order in
/// its place (<see cref="Click"/>) and the keys' push drives it (<see cref="Drive"/>), until the camera
/// follows anybody in another place — Possess asked again, or F6, lets it go (`StoryHand.LetGo`, its own
/// mind thinking again). The pointer picks there (cast from the camera's sector) and
/// lights what it is over, a click on a thing is the seat's target or the body's order, the right button
/// casts, and the walk rings (<see cref="Point"/>, <see cref="Click"/>, <see cref="Cast"/>).
/// </summary>
public sealed unsafe class StoryView : IDisposable
{
    readonly IClassicRenderer gpu;
    readonly Func<PlaceFrame> host;
    readonly WorldCamera camera;
    readonly HostPlaces places;
    PlaceFrame? view;
    IClassicRing? ring;
    /// <summary>The glow the thing under the pointer takes on there, as the host's own place has one.</summary>
    readonly Vitomir.Engine.Ui.HoverGlow glow = new();
    (Vector3 Origin, Vector3 Direction)? ray;
    bool blocked;

    public StoryView(IClassicRenderer gpu, Func<PlaceFrame> host, WorldCamera camera, HostPlaces places)
    {
        this.gpu = gpu;
        this.host = host;
        this.camera = camera;
        this.places = places;
    }

    /// <summary>What the frame does to the game before the view's picture besides its world (the party heads' pass).</summary>
    public Action<PlaceFrame>? BeforeDraw { get; set; }

    /// <summary>The Storyteller whose seat, target and ghost a click there works with; null on no host.</summary>
    public Func<Storyteller?>? Story { get; set; }

    /// <summary>What the pointer is over in the looked-into place (<see cref="StoryHand.Point"/>), or null.</summary>
    public WorldThing? Hovered { get; private set; }

    /// <summary>The body the camera went with, in its new place, or null.</summary>
    public WorldThing? Body { get; private set; }

    /// <summary>The place being looked into, or null when the window shows the host's own.</summary>
    public Location? Place => Frame is null ? null : Body?.Place;

    /// <summary>
    /// The camera's subject for a body carried to another place: the camera follows it (<see cref="WorldCamera.SetSubject"/>),
    /// and the window draws its place from now on. False when the body is in the host's own place (the
    /// ordinary camera then follows it as it is).
    /// </summary>
    public bool Follow(WorldThing body)
    {
        if (body.Place is not { } there || ReferenceEquals(there, host().Sim.Location)) return false;
        Clear();
        Body = body;
        places.Watched = there;
        var frame = host();
        _ = LightStyles.DrawTheRenderersSpare();                   // the renderer's three draws on the lights stream, where it made them
        view = new PlaceFrame(gpu, there, places.Stepper(there))
        {
            WorldView = frame.WorldView, After = frame.After, SkyEverywhere = frame.SkyEverywhere, Effects = frame.Effects,
        };
        view.Game.Steps = false;                                   // HostPlaces steps that place
        view.Game.Aim = frame.Game.Aim;
        // Its rows before its first frame (<see cref="Ray"/>): what the host's frame asks for now, on the host's picture.
        view.ViewHeight = frame.Game.Asked(frame.Game.Target.Width, frame.Game.Target.Height);
        // The pointer's glow and the walk's ring there too, each the host's own place's kind.
        glow.Clear();
        view.Glow = glow;
        ring = view.World.NewRing(there.Materials.Get(WalkTarget.Material));
        view.Marker = ring;
        camera.SetSubject(body);
        Console.WriteLine($"  story: the camera goes with {body.Template} to {there.Name}");
        return true;
    }

    /// <summary>
    /// The renderer of the place being looked into, or null — and null from the moment the camera follows
    /// anybody else, the body is gone from the world or has left that place, or the place is let go.
    /// </summary>
    public PlaceFrame? Frame
    {
        get
        {
            if (view is null) return null;
            if (Body is not { InWorld: true, Place: { } there } body || camera.Subject is not { InWorld: true } subject
                || !ReferenceEquals(subject.Place, there) || places.Of(there) is null || ReferenceEquals(there, host().Sim.Location))
            {
                // The camera still in the place the body has gone from: given back to whoever the host steers (ours).
                bool stranded = Body is not null && !ReferenceEquals(camera.Subject?.Place, host().Sim.Location);
                Clear();
                if (stranded && host().Sim.Location.Steered is { } mine) camera.SetSubject(mine);
                return null;
            }
            return view;
        }
    }

    /// <summary>
    /// The window's frame while the camera is in another place: the host's own place stepped (the frame's
    /// world advance, undrawn), then the other place drawn from the camera. False when there is no view —
    /// the caller draws its own place as ever.
    /// </summary>
    public bool Draw(in GpuPicture picture, FreeCamera eye, float seconds)
    {
        if (Frame is not { } seen) return false;
        int width = picture.Width, height = picture.Height;
        host().Game.Advance(seconds, seconds > 0f ? seconds : 1f);
        glow.Point(Hovered, Body);                                 // brightening or fading, as the host's own (Program)
        glow.Step(seconds);
        seen.Game.Frame(eye, width, height, seconds);              // its walk, sky and poses (before the draw); HostPlaces stepped the place
        BeforeDraw?.Invoke(seen);                                  // what else the frame does before its picture (the heads)
        seen.Render(picture, eye);
        return true;
    }

    /// <summary>
    /// **The ray through a window pixel, as the view draws it**. The world is drawn into the rows above the
    /// status bar (`StatusBar.WorldShare`, `PlaceFrame.WorldViewport`), and the ray has to go through the
    /// same viewport or the click lands somewhere else than under the pointer (`Pick.Ray`). The host's own
    /// renderer keeps the rows it drew with at its last frame — and while the view is up the host's place is
    /// stepped and never drawn, so it answered the rows of whatever it last drew, the whole window when the
    /// camera went with the body before its first frame. In a test (a carried drunk, the bar up),
    /// the white dot for the move location was well above the mouse pointer, as if the pick was
    /// treating the status bar at the bottom as not there. So the view's pick asks the view's own renderer, the
    /// one that drew the picture under the pointer. The whole window with no view (ours).
    /// </summary>
    public (Vector3 Origin, Vector3 Direction) Ray(FreeCamera eye, float x, float y, int width, int height) =>
        Pick.Ray(eye, x, y, width, height, Rows(height));

    /// <summary>
    /// Whether a window pixel is on the world the view draws: the one test of the pointer
    /// (<see cref="WorldFrame.Shows(float, float, int, int)"/>) asked of the view's own frame, whose rows
    /// they are. The whole window with no view.
    /// </summary>
    public bool OnWorld(float x, float y, int width, int height) =>
        view is { } seen ? seen.Game.Shows(x, y, width, height) : x >= 0f && x < width && Pick.OnWorld(y, height, height);

    /// <summary>The rows the view drew the world into at its last frame (<see cref="PlaceFrame.WorldRows"/>); the whole window with no view.</summary>
    public int Rows(int height) => view?.WorldRows(height) ?? height;

    /// <summary>
    /// The pointer moved while the view is up (cast from the camera's sector): what it is over
    /// there — in the seat anything but the target, else what the body's player would be offered
    /// (<see cref="StoryHand.Point"/>). A null ray (the pointer on the interface) points at nothing.
    /// </summary>
    public void Point((Vector3 Origin, Vector3 Direction)? at)
    {
        ray = at;
        Hovered = null;
        blocked = false;
        if (at is not { } r || Frame is null || Body is not { } body) return;
        var story = Story?.Invoke();
        Hovered = StoryHand.Point(body, r.Origin, r.Direction, places.Server.Storyteller, story?.Powers.Target, out blocked, out _);
    }

    /// <summary>
    /// A left click at a window pixel while the view is up: off the world the view draws it is nobody's — no
    /// pick, no order, no ring, as the hover there points at nothing (<see cref="OnWorld"/>) — and still not the
    /// host's own place's (true). On it, <see cref="Click((Vector3 Origin, Vector3 Direction), InputModifier)"/>
    /// through the view's own ray. False when the view is not up (the caller's click, as ever).
    /// </summary>
    public bool Click(FreeCamera eye, float x, float y, int width, int height, InputModifier mods)
    {
        if (Frame is null || Body is null) return false;
        return !OnWorld(x, y, width, height) || Click(Ray(eye, x, y, width, height), mods);
    }

    /// <summary>
    /// A left click on the world while the view is up: true when the view is up — the host's own place takes no
    /// click it cannot see — false otherwise (the caller's click, as ever). A BLOCKSELECT thing swallows it.
    /// On a thing **in the Storyteller's seat** the thing becomes the target and the camera goes to
    /// it there (the click's seat branch, <see cref="StoryPowers.Clicked(WorldThing, WorldThing?, WorldCamera?)"/>) — no order; **out of the seat**, which
    /// keeps the control, it is the body's order on it (<see cref="StoryHand.Order"/>). On the ground
    /// it is the body's move order (<see cref="StoryHand.Walk"/>), and the ring is put where it goes
    /// (the move order's end).
    /// </summary>
    public bool Click((Vector3 Origin, Vector3 Direction) at, InputModifier mods)
    {
        if (Frame is null || Body is not { } body) return false;
        Point(at);
        string said;
        if (blocked) said = "swallowed by something marked BLOCKSELECT";
        else if (Hovered is { } on && places.Server.Storyteller && Story?.Invoke() is { } story)
        {
            story.Powers.Clicked(on, body, camera);                                  // the click's seat branch: the camera to it
            story.Refresh(force: true);                                              // the bar refreshed
            said = $"{on.Template} is the target";
        }
        else if (Hovered is { } thing && StoryHand.Order(body, thing, mods, out var ordered)) said = ordered;
        else if (!StoryHand.Holds(body)) said = $"{body.Template} is not in hand (Possess it to steer it)";
        else
        {
            said = StoryHand.Walk(body, at.Origin, at.Direction, mods, out var walked);
            if (walked is { } spot && view is { } seen && body.Place is { } there)
                seen.Sim.WalkTarget.Set(spot.Point, Pick.NormalAt(there.Level, spot));   // the ring
        }
        Console.WriteLine($"  story: {said}");
        return true;
    }

    /// <summary>
    /// The right button while the view is up: the body casts its selected discipline at
    /// what the pointer is over there, or at the ground under it (<see cref="StoryHand.Cast"/>) — but on the
    /// ground in the seat the Storyteller's move puts the target at the spot (<see cref="StoryPowers.Placed"/>,
    /// the key command, <see cref="KeyInput.StoryMove"/>), and the plain right button with nothing
    /// in the body's hand is the ghost's, not a cast (<see cref="StoryPowers.Ground"/>). Null when the view
    /// is not up (the caller's cast, as ever).
    /// </summary>
    public string? Cast(InputModifier mods)
    {
        if (Frame is null || Body is not { Place: { } there } body) return null;
        Hit? ground = null;
        if (ray is { } r) ground = WorldSweep.Pointer(there, r.Origin, r.Direction);
        // On the ground in the seat: the move, or the ghost (StoryPowers.Ground).
        if (Hovered is null && Story?.Invoke() is { } story && story.Powers.Ground(there, ground, mods, KeyInput.StoryMove, body) is { } put)
        {
            // The ghost put there is the control: the view follows it, the body let go.
            if (story.Powers.Ghost is { InWorld: true } ghost && ReferenceEquals(ghost.Place, there) && !ReferenceEquals(ghost, body)
                && ReferenceEquals(story.Powers.Target, ghost))
            {
                Follow(ghost);
                story.Refresh(force: true);                                          // the bar refreshed
            }
            return put;
        }
        return StoryHand.Cast(body, Hovered, ground?.Point, mods);
    }

    /// <summary>
    /// The held buttons, once a frame while the view is up: the body in hand swings again at what
    /// the pointer is over there when its swing ends, as the host's own steered character does (<see cref="StoryHand.Repeat"/>).
    /// </summary>
    public void Repeat(bool option, bool leftHeld, bool middleHeld, InputModifier mods)
    {
        if (Frame is null || Body is not { } body) return;
        StoryHand.Repeat(body, option, Hovered, leftHeld, middleHeld, mods);
    }

    /// <summary>The game's one camera, which the seat's click gives the thing clicked.</summary>
    public WorldCamera Camera => camera;

    /// <summary>
    /// A party member picked (F6 to F9) while the view is up: 1.1's control is the body,
    /// so the member is not "already selected and steered" and takes the control and the camera — the body is
    /// let go and the window comes back (<paramref name="member"/> is whom the host's place now steers).
    /// </summary>
    public bool Selected(WorldThing? member)
    {
        if (Frame is null || member is null || ReferenceEquals(member.Place, Body?.Place)) return false;
        camera.SetSubject(member);
        return Frame is null;
    }

    /// <summary>The keys' push while the view is up (the QoL `driving`, ours): the body's when it is in hand. True when the view is up.</summary>
    public bool Drive(Vector2 push, bool run, float seconds)
    {
        if (Frame is null || Body is not { } body) return false;
        StoryHand.Drive(body, push, run, seconds);
        return true;
    }

    /// <summary>
    /// The view let go: the window shows the host's own place again, which the camera is given back by whoever
    /// changed it — and the body, left where it stands, is no longer steered there (its mind thinks again,
    /// and the possession ends with it).
    /// </summary>
    public void Clear()
    {
        view?.Dispose();
        view = null;
        ring?.Dispose();
        ring = null;
        Hovered = null;
        ray = null;
        glow.Clear();
        if (Body is { } body)
        {
            places.Watched = null;
            StoryHand.LetGo(body);
            if (ReferenceEquals(places.Server.Possessed, body)) places.Server.Possessed = null;
        }
        Body = null;
    }

    public void Dispose() => Clear();
}

/// <summary>The Storyteller's view drawn into a host's picture (what <c>HostPicture.Draw(StoryView, …)</c> once was).</summary>
static class StoryViewDraws
{
    /// <summary>The Storyteller's view of another place drawn into it, when it has one up; false when it has not.</summary>
    public static bool Draw(this in GpuPicture picture, StoryView view, FreeCamera camera, float seconds) => view.Draw(picture, camera, seconds);
}
