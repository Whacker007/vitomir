// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The party heads' models and their own per-frame pass (out of the renderer's HeadsPass).
using System.Numerics;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Scene;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **The party heads' models and their per-frame pass** (ours, until then inside the renderer's <c>HeadsPass.Draw</c>).
/// One small <see cref="SceneMesh"/> of each party member's own <c>.nod</c> head, animated by its own <c>.nms</c> motion set
/// (<see cref="PartyHeads"/> decides what and where), and <see cref="Frame"/>, which the interface's update
/// runs before anything is drawn — the heads made, each put on the motion its owner's state calls for, its clock
/// moved on, a one-shot that has run out handed back to its owner (<see cref="PartyHeads.Played"/>, which writes the
/// owner's <see cref="Actor.HeadAction"/>: why this is not the renderer's), the mouth of a party member speaking, and the
/// heads posed and lit (<see cref="Poses"/>). The renderer's <c>HeadsPass</c> draws what this left.
/// </summary>
public sealed class HeadModels
{
    readonly GameFiles files;
    readonly MaterialCache materials;
    readonly MotionSets motions;
    readonly List<WorldThing> drawn = [];
    readonly Dictionary<(int Who, string Model), WorldThing> heads = [];
    readonly Dictionary<int, WorldThing> current = [];      // each member's head as last drawn, for Playing
    readonly Dictionary<WorldThing, HeadMode> playing = [];
    int next;
    float clock = float.NaN;

    /// <summary>The heads' own mesh: no level, no sector, no lightmaps.</summary>
    public SceneMesh Mesh { get; } = new();

    /// <summary>The heads posed and lit by <see cref="Frame"/>; the renderer sends up what it wrote.</summary>
    public ScenePoses Poses { get; }

    /// <summary>The heads <see cref="Frame"/> put up this frame, in status-bar order (none when they are off).</summary>
    public IReadOnlyList<PartyHead> Shown { get; private set; } = [];

    /// <summary>
    /// Whether the head **models** draw. The viewport's black backdrop is
    /// the status bar pass's and is painted either way, which is the engine's own order:
    /// The panel's draw makes its device call **before** it tests the `heads` console bit,
    /// so switching the heads off in 1.1 leaves the black panel behind rather than a
    /// window onto the level.
    /// </summary>
    public bool ShowHeads { get; set; } = true;

    /// <summary>
    /// Whose heads to draw, in status-bar order. The engine reads its own party array; the viewer
    /// hands us whatever it has, and <see cref="PartyHeads.Of"/> applies the four-slot cap.
    /// </summary>
    public IReadOnlyList<WorldThing> Party { get; set; } = [];

    /// <summary>Which of the canvases the heads go in (<see cref="PartyHeads.Canvas"/>): the bar's, `statusBarMP`'s one head (1), or the advancement screen's.</summary>
    public int Mode { get; set; } = PartyHeads.SinglePlayer;

    /// <summary>One head alone in place of the party's — the advancement screen's subject.</summary>
    public WorldThing? Alone { get; set; }

    /// <summary>
    /// True while the large description box is covering the heads. The engine does **not** disable
    /// them for it — `descLargeSP` is a separate pane over the identical rectangle, drawn later, so
    /// it simply paints over them. We draw in the same order for the same reason, so this is only
    /// here for the viewer to switch them off deliberately.
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// The clock the heads are animated from, in seconds. <see cref="Frame"/> takes it from the
    /// simulation whenever it is given one, so the viewer never has to set it; it is settable so a
    /// test can step the heads without a world.
    /// </summary>
    public float Seconds { get; set; }

    /// <summary>
    /// **The heads' own clock**: the pass advances them by the frame delta of the engine's
    /// first timeline (<c>_DAT_006F2050</c>), whose pause counter nothing ever
    /// writes — the interface's clock, which runs on through a pause, a menu and the advancement
    /// screen, so the portraits go on breathing while the game stands still. The viewer hands it its
    /// interface clock here; null, or null answered (before that clock has started — a headless
    /// still), the simulation's is used.
    /// </summary>
    public Func<float?>? Clock { get; set; }

    public HeadModels(GameFiles files, MaterialCache materials, MotionSets motions)
    {
        this.files = files;
        this.materials = materials;
        this.motions = motions;
        Poses = new ScenePoses(Mesh);
        // No level, no sector, no lightmaps: a head viewport is lit by its own two lights and
        // nothing else, which is what `LightSource` is for.
        Mesh.LightSource = _ => (HeadLights, Vector3.Zero);
        // … and lit the renderer's way: the two lights at their own intensities, no fall-off,
        // over 392 a vertex (Engine.Ui.HeadLighting).
        Mesh.VertexLight = HeadLighting.Colour;
    }

    /// <summary>
    /// The heads' frame (before anything is drawn): nothing while the viewport is off (<paramref name="shown"/>,
    /// the pass's own <c>Enabled</c>; <see cref="Hidden"/>; <see cref="ShowHeads"/>), else each head made and animated on
    /// <see cref="Clock"/> (or <paramref name="sim"/>'s seconds, or <see cref="Seconds"/> as set), then posed and lit.
    /// </summary>
    public void Frame(bool shown, Simulation? sim)
    {
        Shown = [];
        if (!shown || Hidden || !ShowHeads) return;
        // `statusBarMP` (mode 1) has room for one head, its own player's.
        var party = PartyHeads.Of(Alone is { } alone ? [alone]
                                  : Mode == PartyHeads.Narrow && Party.Count > 1 ? [Party[0]] : Party);
        if (party.Count == 0) return;
        Sync(party);
        if (Clock?.Invoke() is { } ui) Seconds = ui;
        else if (sim is not null) Seconds = sim.Seconds;
        Animate(party, Seconds);
        Shown = party;
        Poses.Pose();
    }

    /// <summary>The head drawn for <paramref name="head"/>, or null.</summary>
    public WorldThing? HeadOf(PartyHead head) => heads.TryGetValue(Key(head), out var thing) ? thing : null;

    /// <summary>
    /// The viewport's two lights, as the engine writes them (0x48 bytes a light):
    /// a warm key from the left and a cool fill from the right, cleared and re-added for every head.
    /// **The heads are lit by <see cref="HeadLighting"/>** (the renderer's own arithmetic,
    /// the intensities 500 and 200, no fall-off); these stand-ins are left for the portrait window.
    ///
    /// **The positions, colours and ranges are the engine's; the falloff is ours.** The light pass turns
    /// a light and a range into a contribution and has not been read, so this uses the level-light
    /// curve already in this tree with a falloff chosen to reach zero at the stated range. It is the
    /// one part of the head viewport that is not a transcription, and it is the first thing to
    /// re-read if the heads look wrong.
    /// </summary>
    public static readonly ChosenLight[] HeadLights = [.. PartyHeads.Lights.Select(Make)];

    /// <summary>A light at a point, of a colour and a range, as a viewport of its own lights them (the portrait window's too).</summary>
    public static ChosenLight Light(Vector3 at, uint colour, float range) => Make((at, colour, range));

    static ChosenLight Make((Vector3 At, uint Colour, float Range) light)
    {
        // A level light's colour and intensity are both in **0..255** (`ThingLight.Scale`), which is
        // the unit the engine writes these two in as well — (255, 226, 173) and (191, 226, 255).
        var colour = new Vector3((light.Colour >> 16) & 0xFF, (light.Colour >> 8) & 0xFF, light.Colour & 0xFF);
        // Falloff is **ours**: it is chosen so the light reaches zero exactly at its stated range,
        // because the light pass — which turns a light and a range into a contribution — has not been
        // read. Everything else here is the engine's.
        var nil = new NilLight(0, false, NilLight.Point, 0, colour, ThingLight.Scale, ThingLight.Scale,
                               Vector3.Zero, light.At, 0f, 0f, light.Range, ThingLight.Scale / light.Range, []);
        // The heads stand on the origin, so the strength is the same for every one of them and is
        // worked out once here rather than per head.
        return new ChosenLight(nil, ThingLight.Strength(nil, Vector3.Zero, 0f, 1f));
    }

    /// <summary>
    /// Makes sure every head in <paramref name="heads"/> has geometry, and puts each one where its
    /// offset says. One <see cref="WorldThing"/> per head **model**, reused across frames — the
    /// heads are not world things and never enter the location.
    /// </summary>
    void Sync(IReadOnlyList<PartyHead> party)
    {
        foreach (var head in party)
        {
            // Keyed by **the party member and the model**, not the model alone: two members sharing
            // a head model still have a head each, because each one's head is animating its owner's
            // own state. A member who swaps head model gets a new one and the old stays in the mesh
            // unused, which is what the mesh's append-only rule asks for.
            // **The member is their GUID, not their thing**: a companion is rebuilt from their
            // template at every journey (Campaign.Travel), keeping their GUID, and the leader's body
            // is the place's own. Keyed by the thing, every journey appended a new head for each of
            // them and posed the old ones for ever after (dozens of heads by the end of a play-test).
            if (heads.TryGetValue(Key(head), out var had)) { current[head.Who.Guid] = had; continue; }
            // **An armour's head rides the owner's own head** (the wear code's type-7 half): the plain
            // head is the thing, posed by its own motion set, and the armour's head goes into its
            // sub-model slot 0 — carried on the plain head's skeleton, skinned by its own rest
            // pose — with the plain head's own geometry hidden while it holds one (the scene mesh's
            // body slot, `SceneMesh.Rearm`). One that does not load leaves the plain face showing.
            // Drawing the armour's head as a head of its own, animated directly by the head's
            // motion set, twisted Wilhem's leather head into a sneer.
            string own = head.Own.Length > 0 ? head.Own : head.Model;
            current[head.Who.Guid] = heads[Key(head)] = Head(own, head.MotionSet,
                                                             string.Equals(own, head.Model, StringComparison.OrdinalIgnoreCase) ? null : head.Model);
        }
        Poses.Things = drawn;

        WorldThing Head(string model, string motionSet, string? armour)
        {
            var thing = new WorldThing
            {
                Index = next, Guid = ++next, Template = model, Type = ThingType.Actor,
                Scene = new NsdThing(), Model = model, MotionSet = motionSet,
                // Only the body slot is asked for: no weapon, no off hand, no hands.
                Actor = armour is null ? null : new Actor { BodyModel = Path.GetFileNameWithoutExtension(armour) },
            };
            if (Mesh.AddThing(thing, drawn.Count, files, materials, motions)) drawn.Add(thing);
            return thing;
        }
    }

    static (int, string) Key(PartyHead head) => (head.Who.Guid, head.Model);

    /// <summary>How many heads have been made — one per member and head model, however many journeys.</summary>
    public int HeadsMade => heads.Count;

    /// <summary>What a party member's portrait head is playing, or null when it has no head drawn.</summary>
    public ThingAnimation? Playing(WorldThing who) =>
        current.TryGetValue(who.Guid, out var head) ? head.Animation : null;

    /// <summary>
    /// Puts each head on the motion its owner's state calls for, and moves every head's clock on.
    /// This is the heads' own per-frame pass, which the interface's update
    /// runs before anything is drawn.
    ///
    /// The engine's two channels are kept apart here for the reason
    /// <see cref="PartyHeads.OnMotionChannel"/> gives: the resting pair may be asked for every
    /// frame because the motion channel refuses to restart what it is already playing, while a
    /// state overlay would restart from frame 0 every frame if it were asked for the same way, so
    /// it is played once on the change and dropped once on the way out.
    ///
    /// **The clock** is <see cref="Clock"/>: the pass advances the heads from the engine's frame delta,
    /// the frame delta of the engine's *first* timeline — whose pause counter nothing in the
    /// executable ever writes, so the portraits keep moving while the game itself is paused.
    /// </summary>
    void Animate(IReadOnlyList<PartyHead> party, float seconds)
    {
        float step = float.IsNaN(clock) ? 0f : seconds - clock;
        clock = seconds;
        if (step < 0f) step = 0f;
        foreach (var head in party)
        {
            if (!heads.TryGetValue(Key(head), out var thing)) continue;
            if (thing.Animation is not { } anim) continue;
            bool onMotion = PartyHeads.OnMotionChannel(head.Mode);
            playing.TryGetValue(thing, out var was);
            if (onMotion)
            {
                // Safe every frame (the add dedups); and a state that has just ended takes its
                // overlay with it, which is the engine's own un-stick (coming
                // out of torpor, un-staked, out of frenzy).
                if (!PartyHeads.OnMotionChannel(was) && was != HeadMode.None) anim.StopAction();
                anim.Play(PartyHeads.NameOf(head.Mode), motions);
            }
            else if (head.Mode != was)
            {
                anim.Play(PartyHeads.NameOf(head.Mode), motions, onMotion: false);
                // Ours: the bite's lunge is shorter than the body's bite opening, and a finished one-shot hands the head back to its
                // idle, so the portrait snapped to looking around before the feed began. The bite holds its last frame instead,
                // until the bite lands (head_feed) or is let go (the head's action set back), which every way out of it does.
                if (head.Mode == HeadMode.Bite) anim.HoldActionAtEnd();
            }
            playing[thing] = head.Mode;
            if (step > 0f) anim.Advance(step);
            // An impact or a bite that has run out hands the head back (PartyHeads.Played).
            if (!onMotion && anim.Action is null) PartyHeads.Played(head.Who.Actor, head.Mode);
            // A party member speaking moves the portrait's mouth too, by the head's own set.
            anim.Mouth = head.Who.Type == ThingType.Player ? LipSync.For(head.Who) : null;
        }
    }
}
