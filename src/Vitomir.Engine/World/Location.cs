// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Scene;
using Vitomir.Engine.Vfs;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// Everything a place in the game is made of, short of the GPU: the level's geometry, the scene's
/// things, its sky and fog, and where the player starts. One of these is what the viewer, the tests
/// and (later) the game all load; `Vitomir.Render` is what draws it.
/// </summary>
public sealed partial class Location
{
    public required string Name { get; init; }
    public required NilLevel Level { get; init; }
    public NsdScene? Scene { get; set; }
    /// <summary>The scene whose settings are in force: the chapter's if there is one, else the level's.</summary>
    public string SceneFile { get; set; } = "";
    /// <summary>The level's own scene — the place as it always is: props, exits, player starts.</summary>
    public NsdScene? WorldScene { get; init; }
    /// <summary>The chronicle's scene for this chapter, laid on top of the world: who is here now. A
    /// <c>ChangeScene</c> swaps it in place (<see cref="SwapChapter"/>).</summary>
    public NsdScene? ChapterScene { get; set; }
    public required MaterialCache Materials { get; init; }
    public required SceneMesh Mesh { get; init; }
    /// <summary>The scene's sky model and its animation, when it has one.</summary>
    public Sky? Sky { get; set; }
    /// <summary>Where the sky may show: the screen bounds of the level's Sky surfaces.</summary>
    public SkyWindow? SkyWindow { get; set; }
    public FogSettings Fog { get; set; } = FogSettings.None;
    /// <summary>The player start this place was opened at, at eye height, and the way it faces.</summary>
    public Vector3 StartPosition { get; set; }
    public float StartYaw { get; set; }

    /// <summary>
    /// Which **entrance** this place was opened at, or −1 when nobody asked for one. An entrance is
    /// a *group* of player starts: the scene file stamps every start with a group number
    /// (`NsdStart.Group`), a chronicle exit names one (`NscExit.Entrance`), and arriving through
    /// that exit puts the party on that group's starts. See <see cref="Arrivals"/>.
    /// </summary>
    public int Entrance { get; set; } = -1;

    /// <summary>
    /// The starts of <see cref="Entrance"/>, in file order — **one per party slot**, four of them
    /// in 499 of the game's 500 resolvable exits. The leader takes the first. Falls back to a
    /// single start when the entrance names a group the level does not have, which ten of the
    /// game's exits do.
    /// </summary>
    public NsdStart[] Arrivals { get; set; } = [];

    /// <summary>Every player start in this place, whatever its group.</summary>
    public NsdStart[] AllStarts { get; init; } = [];

    /// <summary>The entrances this place has, by group number, each with its starts in file order.</summary>
    public IEnumerable<IGrouping<uint, NsdStart>> Entrances => AllStarts.GroupBy(s => s.Group);
    /// <summary>The scene's own far clip plane (RENDERINFO_ZCLIP), or 0 when it doesn't set one.</summary>
    public float FarPlane { get; set; }
    /// <summary>The scene scripts its things name, in scene order and without repeats.</summary>
    public string[] ScriptNames { get; set; } = [];

    /// <summary>
    /// The templates this place was built from, kept because a thing can be made after the level
    /// has loaded: `CodexThing.SpawnThing` names a template and the engine looks it up in the same
    /// table the level builder used. Null only when a test built a location by hand.
    /// </summary>
    public TemplateSet? Templates { get; init; }

    /// <summary>The install, for the model bounds a spawned box-shaped thing needs.</summary>
    public Vfs.GameFiles? Files { get; init; }

    /// <summary>
    /// The engine's millisecond clock, kept here by <see cref="Simulation"/> so anything that
    /// happens to a thing can date itself without being handed the clock. Zero before the first
    /// step, which is what a test that never runs the simulation sees.
    /// </summary>
    public int Milliseconds { get; set; }

    /// <summary>
    /// The Fixes this place's shape changes read, when not the run's (<see cref="World.Fixes.Current"/>).
    /// Null in the game; a test sets it, so the shipped scripts' own path reads a switch without the
    /// test installing the process-wide register (ours).
    /// </summary>
    public Fixes? Fixes { get; set; }

    /// <summary>
    /// The quality-of-life switches this place's code reads, when not the run's (<see cref="World.Qol.Current"/>). Null in
    /// the game; a test sets it, as it sets <see cref="Fixes"/> (ours).
    /// </summary>
    public Qol? Qol { get; set; }

    /// <summary>
    /// The game's sound, when there is any. Everything in the engine that makes a noise reaches it
    /// through here — a thing knows its place, and its place knows its sound — so a test or a tool
    /// that loads a place without one simply hears nothing.
    /// </summary>
    public Audio.GameAudio? Audio { get; set; }

    /// <summary>
    /// The game this place is part of: the message log, the quest log and the purse, which outlive
    /// it (<see cref="Engine.Session"/>). Null for a place loaded on its own, which then posts
    /// nothing and keeps no quests.
    /// </summary>
    public Session? Session { get => session; set { session = value; CharacterSheets.Paid(this); } }
    Session? session;
    /// <summary>
    /// The 32 light styles of this place. Scripts switch them, the renderer multiplies them into
    /// the lightmap layers they drive, and <c>flicker</c> materials follow their brightness byte.
    /// </summary>
    public LightStyles Styles { get; init; } = new();
    /// <summary>
    /// Every thing here, as the engine and the scripts see it at run time — the scene's own, then
    /// whatever has been spawned since. It **grows**: the engine keeps things in a GUID hashtable
    /// rather than an array precisely so a script can make one mid-scene, and
    /// a removed thing leaves it.
    /// </summary>
    public IReadOnlyList<WorldThing> Things => things;
    readonly List<WorldThing> things = [];

    /// <summary>
    /// The GUID the next spawned thing gets: the game's one counter (<see cref="Guids"/>),
    /// which skips anything already in the hashtable and never yields 0.
    /// </summary>
    public int NextGuid { get => Guids.Next; private set => Guids.Next = value; }

    readonly Dictionary<int, WorldThing> byGuid = [];
    /// <summary>The motion sets and animations this place's characters play from.</summary>
    public MotionSets? Motions { get; init; }
    /// <summary>The ground a person can stand on here, and how the pieces of it join up.</summary>
    public WalkMap? Walk { get; init; }
    /// <summary>What is solid here: the level's own per-sector collision trees.</summary>
    public Collision? Solid { get; init; }
    /// <summary>The things here that have a body — the barrels, carts and people you bump into.</summary>
    public Bodies? Props { get; init; }
    /// <summary>
    /// This frame's decision on what is drawn see-through for being in the camera's way (ours, presentation only): made once
    /// a frame (<see cref="WorldFrame.Frame"/>), read by the picture and by the pointer. Empty with see-through off.
    /// </summary>
    public SeeThrough SeeThrough { get; } = new();
    /// <summary>The level compiler's path graph, searched the way the engine searches it.</summary>
    public PathFinder? Paths { get; init; }
    /// <summary>The player, when one was asked for. It is the last thing in <see cref="Things"/>.</summary>
    public Player? Player { get; init; }

    /// <summary>
    /// The walkers made for things a script walks that have no AI mind (<see cref="WalkerFor"/>).
    /// The simulation advances these alongside the minds' own.
    /// </summary>
    public List<Player> ScriptWalkers { get; } = [];

    /// <summary>
    /// The walker that moves <paramref name="thing"/>: the world's for the steered character, the
    /// mind's for anything with one, and otherwise one made here and kept on the thing. Null when
    /// the place has no walk map, or the thing is not something that walks.
    ///
    /// The engine needs no such bookkeeping because a walker is part of a thing there
    /// (<see cref="WorldThing.Walker"/> has the detail). This is the join that gets our shape back
    /// to that behaviour.
    /// </summary>
    public Player? WalkerFor(WorldThing thing)
    {
        if (ReferenceEquals(Player?.Thing, thing)) return Player;
        if (thing.Mind is { } mind) return mind.Walker;
        if (thing.Walker is { } kept) return kept;
        if (Walk is null || thing.Actor is null) return null;
        var made = new Player
        {
            Thing = thing, Map = Walk, Motions = Motions, Solid = Solid, Props = Props, Path = Paths,
        };
        made.Settle();
        thing.Walker = made;
        ScriptWalkers.Add(made);
        return made;
    }
    /// <summary>
    /// The regions, by sector, and the test that tells their scripts somebody has come or gone.
    /// Null only for a location a test built by hand.
    /// </summary>
    public Regions? Regions { get; set; }

    /// <summary>
    /// Where a conversation stands its party (<c>convstarts_convstartNN</c> in the scene file):
    /// the world scene's, then the chapter scene's. See <see cref="ConversationStart"/>.
    /// </summary>
    public NsdStart[] ConversationStarts { get; set; } = [];

    /// <summary>Everybody's AI: the minds attached to the characters here. Null until <see cref="Awaken"/>.</summary>
    public Minds? Minds { get; private set; }

    /// <summary>
    /// Attaches an AI to every character whose template names one, which is what the engine does
    /// when a thing is finalized (from tag 56). Kept out of <c>Load</c> so a
    /// test or the model viewer can load a place without waking anyone up.
    /// </summary>
    /// <param name="fromSave">
    /// The place has just been given a save's state (<see cref="Save.SaveApply"/>): a thing the save
    /// brought back was finalized long ago and already carries its effects, so it is not born again.
    /// </param>
    public Minds Awaken(AiClasses classes, int seed = 1, Rpg.EffectDefinitions? effects = null,
                        Rpg.DisciplineDefinitions? powers = null, bool fromSave = false)
    {
        Minds = new Minds(this, classes, seed)
        {
            Level = Level, Wanted = effects?.Wanted, Effects = effects, Powers = powers,
        };
        Effects = effects;
        Powers = powers;
        if (!fromSave) CharacterSheets.Give(this);                             // the companions' sheets
        // The disciplines every character's AI file gives it, now that there are minds.
        if (powers is not null) foreach (var mind in Minds.All) Minds.Learn(mind);
        // The effects a character is born with (`Born`). The engine puts them on as each thing is
        // placed; here the definitions only exist once somebody has asked for them, so everyone
        // already standing about gets them now and anything spawned later gets them on arrival.
        foreach (var thing in Things)
        {
            if (fromSave && thing.Finalized) { Recreate(thing); Save.LoadedMinds.Restore(Minds, thing); continue; }     // a load's re-create, then its minds
            thing.Finish();
            PlayCreate(thing);                                                   // the finalize's `create`
            Born(thing);
        }
        return Minds;
    }

    /// <summary>`misc/effects.ned`, once the caller has loaded it. See <see cref="Born"/>.</summary>
    public Rpg.EffectDefinitions? Effects { get; internal set; }

    /// <summary>
    /// The treasure tables and the one generator every roll steps (`Rpg.Treasure`), shared by
    /// every place in the install as 1.1's are. Null with no game files; a test
    /// may give its own.
    /// </summary>
    public Rpg.Treasure? Treasure
    {
        get => treasure ??= Files is { } f && Templates is { } t ? Rpg.Treasure.For(f, t, Effects ?? new Rpg.EffectDefinitions(f)) : null;
        set => treasure = value;
    }
    Rpg.Treasure? treasure;

    /// <summary>`misc/game.ndd` and `game.ndg`, once the caller has loaded them — what a discipline is.</summary>
    public Rpg.DisciplineDefinitions? Powers { get; internal set; }

    /// <summary>
    /// **Whether the inventory pane is on screen**, which the engine asks before it decides where
    /// a pick-up goes: the pick-up sends it to the cursor rather than the pack when the pane is up
    /// (shown and in front) or when alt is held. The base game's
    /// behaviour is this condition read from the other side: without
    /// the inventory window open, an item picked up goes to the inventory by itself.
    ///
    /// A hook rather than a field because the world cannot see the interface, and the engine's own
    /// answer is a live query on the menu rather than a flag anybody stores. Null answers false,
    /// which is what a headless test wants.
    /// </summary>
    public Func<bool>? InventoryShown { get; set; }

    /// <summary>
    /// The two script callbacks an item can veto with — `pickup` and `dropped`. Null, which is what
    /// a headless test leaves, is the same as an item with no script attached: everything is
    /// allowed. Whoever owns the script layer sets it.
    /// </summary>
    public Rpg.Ground.Scripts? ItemScripts { get; set; }

    /// <summary>
    /// The `clicked` callback a thing's script gets when somebody walks up and uses it — the
    /// engine's script call, which reaches through the thing's Codex instance to its
    /// class's `clicked` method and invokes `clicked(thingGuid, clickerGuid, 0)`.
    ///
    /// Held as a delegate for the same reason <see cref="ItemScripts"/> is: the engine half must
    /// not know that the other end is Java. The arguments are (the thing used, whoever used it).
    /// Null is a world where nothing is scripted, which is what a headless test has.
    /// </summary>
    public Func<WorldThing, WorldThing, bool>? Clicked { get; set; }

    /// <summary>
    /// The other script seam the use order reaches: `killed(thingGuid, killerGuid, captureId)`,
    /// which is what **opening a container** calls (`Rpg.Container.Open`). Same shape as
    /// <see cref="Clicked"/> and the same two loops behind it — the thing's own scripts and then
    /// everything that has captured it.
    /// </summary>
    public Func<WorldThing, WorldThing?, bool>? Killed { get; set; }

    /// <summary>
    /// A thing entering the world for the first time, once the place's scripts are running — the
    /// engine's finalize ends in `created(thingGuid)` for a thing that carries a script.
    /// The script layer sets it; the things a level loads with are told by
    /// the script layer itself when it starts, before `beginscene`.
    /// </summary>
    public Action<WorldThing>? Created { get; set; }

    /// <summary>
    /// A move that got where it was going: `arrived(thingGuid, frameNum,
    /// captureId)` to the thing's own script and then to every script holding a capture on it.
    /// Three times in 1.1: the walker's arrival at the end of any walk (with a
    /// frame of 0) and the two track movers' arrival at a keyframe (with the
    /// frame). It is what drives every patrol in the game — `WalkCycle` (90 characters),
    /// `WalkCycleLawmen`, `ClassWander` — and a string of scene choreography: Orsi's walk in the
    /// Teutonic keep, Pink's exit from the warehouse, the servant in the Setite temple, the skiff
    /// on the sewer lake.
    /// </summary>
    public Action<WorldThing, int>? Arrived { get; set; }

    /// <summary>
    /// A character waking up to fight (the react, which ends in the script message): `awaken(thingGuid,
    /// targetGuid, captureId)` to the thing's own script and then to every script holding a capture
    /// on it. The buried Cappadocians are made visible by exactly this (`ClassCappadocian`).
    /// </summary>
    public Action<WorldThing, WorldThing?>? Awakened { get; set; }

    /// <summary>
    /// The script objects the engine hangs on characters of its own accord — an effect's and a
    /// discipline's (<see cref="IScriptObjects"/>). The script layer sets it as it enters the place;
    /// null is a world with no scripts, where effects and disciplines still work and simply have
    /// nothing to tell.
    /// </summary>
    public IScriptObjects? ScriptObjects { get; set; }

    /// <summary>
    /// The character the player is steering: the minds' idea of it when
    /// the place is awake, else the player thing.
    /// </summary>
    public WorldThing? Steered => Minds?.Steered ?? Player?.Thing;

    /// <summary>A thing by the GUID scripts use, or null. The engine's own GUID hashtable.</summary>
    public WorldThing? Thing(int guid) => byGuid.GetValueOrDefault(guid);

    /// <summary>
    /// **The thing list as it was when the walk began, in the order of entry** — what the frame's
    /// walk iterates, reversed (<see cref="ListOrder"/>).
    ///
    /// Anything that ticks can make or unmake a thing: an effect that kills somebody spawns their
    /// skeleton, a projectile's timer becomes its explosion, a script's `arrived` callback calls
    /// `SpawnThing`, and an arriving ground order puts an item on the floor or takes one off it.
    /// All of those reach <see cref="Enter"/>, <see cref="Withdraw"/> or <see cref="Remove"/>,
    /// which add to or remove from <see cref="Things"/> — the list the walk is in the middle of
    /// enumerating.
    ///
    /// Play found it in the first minute with items on the floor, and it had been
    /// reachable for a long time: `Physics.StepAll` was already calling `.ToArray()` and the corpse step
    /// was already deferring its removals into lists of their own, each solving the same hazard
    /// once, locally, without naming it. This is it named.
    ///
    /// **The engine has the same problem and two answers to it**: its things are a linked list,
    /// which tolerates a node leaving mid-walk, and a script that wants a thing gone safely sets
    /// `THING_FLAG_REMOVE`, whose own comment in `Codex.java` is *"Remove object at start of next
    /// frame (for safe removals)"*. A snapshot is the same guarantee reached differently, and it
    /// costs one array per stage per frame — which is worth naming as the price rather than
    /// hiding, and worth revisiting only if a level ever has enough things for it to show.
    /// </summary>
    public WorldThing[] Snapshot => [.. things];

    /// <summary>
    /// **The things the mesh was built from, in the order it was built from them** — which is not
    /// the same list as <see cref="Things"/> and must never be confused with it.
    ///
    /// A drawn batch names its thing by **position** (`DrawBatch.Thing`), because that is what the
    /// mesh builder had to hand. <see cref="Things"/> is the live membership of the place and it
    /// shrinks: anything removed or picked up comes out of it, and every index after that one
    /// shifts by a place. Pointing the renderer at the live list therefore draws things at other
    /// things' transforms the moment anything leaves — a bug that has been reachable since things
    /// could be destroyed at run time, and that picking items up off the floor would have made an
    /// everyday one.
    ///
    /// **It is appended to and never reordered**, which is what keeps those indices meaning what
    /// they meant. A thing that enters the world after the place has loaded is added to the end and
    /// given geometry there (<see cref="Show"/>); a thing that leaves stays in it, holding its
    /// place, and simply stops being drawn. The objects in it are the same objects, so a thing that
    /// moves still draws where it is now.
    /// </summary>
    public IReadOnlyList<WorldThing> Drawn => drawn;
    readonly List<WorldThing> drawn = [];
    /// <summary>Where each of them sits in <see cref="Drawn"/>, so a thing is never given geometry twice.</summary>
    readonly Dictionary<WorldThing, int> drawnAt = [];

    /// <summary>
    /// Gives a thing that has just come into the world its geometry, if it has none yet.
    ///
    /// The engine needs no such step, and it is worth being plain about why we do. Its level
    /// geometry is baked per sector at load but its **things are not baked at all**:
    /// each one is submitted on its own, every frame, by walking the live linked list at
    /// the sector's own list, which has no type test and no cached copy. So the entire
    /// render-side registration of a thing made at run time is the single pointer store that links
    /// it into that list, and it draws on the very next frame. This tree trades that for
    /// one vertex buffer per place, and <see cref="SceneMesh.AddThing"/> is the bill.
    ///
    /// Called from <see cref="Enter"/>, which is our version of the world's enter — so the two engines register a new
    /// thing for drawing at the same moment, for the same reason.
    /// </summary>
    void Show(WorldThing thing)
    {
        if (Files is null || drawnAt.ContainsKey(thing)) return;
        int at = drawn.Count;
        drawn.Add(thing);
        drawnAt[thing] = at;
        Mesh.AddThing(thing, at, Files, Materials, Motions);
    }

    /// <summary>
    /// `CodexThing.SetModel`: the thing is drawn with another model from now
    /// on. The engine swaps the render model object on the thing and refuses a
    /// name it cannot load, answering false; a thing with no render model at all is refused too.
    /// The motion set is the thing's, not the model's, so what it was playing carries on.
    ///
    /// Ours has one mesh per place, so the swap retires the old geometry — its slot in
    /// <see cref="Drawn"/> is handed to a thing that is never in the world, which is exactly how a
    /// removed thing's batches stop drawing — and appends the new model's, as a thing made at run
    /// time is appended. The Convent's morning is the use everybody sees: Christof gets out of bed
    /// in `christof_human.nod`.
    /// </summary>
    public bool SetModel(WorldThing thing, string model)
    {
        if (model.Length == 0 || thing.Model.Length == 0) return false;
        if (Files is not null && Mesh.Model(model, Files) is null) return false;
        var was = thing.Animation;
        thing.Model = model;
        if (Files is null || !drawnAt.TryGetValue(thing, out int at)) return true;
        drawn[at] = Retired;
        drawnAt.Remove(thing);
        Mesh.Unanimate(at);
        thing.Animation = null;
        Show(thing);
        // Whatever the old model was doing, the new one does from the same frame.
        if (was is not null && thing.Animation is { } now && Motions is { } motions)
        {
            if (was.Motion is { } m) { now.Play(m.Mode.Index, motions, onMotion: true, rate: m.Player.Rate); if (now.Motion is { } nm) nm.Player.Frame = m.Player.Frame; }
            if (was.Action is { Finished: false } a) { now.Play(a.Mode.Index, motions, onMotion: false, rate: a.Player.Rate); if (now.Action is { } na) na.Player.Frame = a.Player.Frame; }
        }
        return true;
    }

    /// <summary>
    /// A script tag as the class it names: the tag's handler cuts the name's extension before it looks
    /// the class up, which cuts it at its **last** dot — so the four templates that name
    /// `Door.class`, `Exit0.class`, `Exit1.class` and `Test.class` get `Door`, `Exit0`, `Exit1` and
    /// `Test`. (It also means to strip a leading path at the last backslash, but only moves its own
    /// pointer past it, so a path would survive; no shipped name has one.)
    /// </summary>
    public static string ScriptName(string tag)
    {
        int dot = tag.LastIndexOf('.');
        return dot >= 0 ? tag[..dot] : tag;
    }

    /// <summary>The stand-in a retired slot in <see cref="Drawn"/> points at: never in any world.</summary>
    static readonly WorldThing Retired = new()
    {
        Index = -1, Guid = 0, Template = "", Scene = NsdThing.Placed("", System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero, 0),
    };

    /// <summary>Takes the scene's own things, which are numbered as they are read.</summary>
    void Adopt(IEnumerable<WorldThing> scene)
    {
        foreach (var t in scene)
        {
            things.Add(t);
            t.Place = this;
            byGuid[t.Guid] = t;
            Guids.Past(t.Guid);
        }
    }

    /// <summary>
    /// A GUID for a thing that is about to exist. The engine's counter never yields 0 and skips
    /// anything the hashtable already holds, and both matter: 0 is "no thing" to a
    /// script, and a save's things are numbered from the file rather than from the counter. The same
    /// allocation is inlined again on its own for callers that only want a free GUID —
    /// a multiplayer character's actor among them — with nothing else the thing's making does.
    /// </summary>
    public int TakeGuid() => Guids.TakeHeld(byGuid.ContainsKey);

    /// <summary>How high above a player start the eye sits.</summary>
    public const float EyeHeight = 48f;

    /// <summary>
    /// Loads a place the way the game builds one: the level's geometry, the level's own scene as
    /// the world, and — when a chronicle names one — that chapter's scene on top of it.
    ///
    /// The original does this in two steps (the world load reads the <c>.nil</c> and then
    /// loads the scene of the same name; the location load then loads the location's scene from the
    /// chronicle into the same world). So <c>l1_east.nsd</c> holds East London's benches, exits and
    /// player starts, and <c>EAST_28_1.nsd</c> adds the nine people who are there in that chapter.
    /// Settings — fog, sky, music, far plane — come from the chapter scene when there is one and
    /// from the world scene otherwise, which is the rule the sky renderer applies
    /// (else the world scene's).
    ///
    /// <paramref name="things"/> and <paramref name="sky"/> leave those out when false, which the
    /// viewer uses to compare. <paramref name="scene"/> is the chapter scene's name
    /// (<c>HAVN_25_1</c>); without one only the world scene is loaded.
    /// </summary>
    /// <summary>
    /// The starts an entrance puts the party on, and where the camera stands for the first of them —
    /// the entrance's group, or the first start in the file when the group is missing or none was
    /// asked for. Yaw is about +Z and a start faces its own +Y.
    /// </summary>
    static (NsdStart[] Arriving, Vector3 Position, float Yaw) ArrivalsAt(NsdStart[] starts, int entrance, Vector3 fallback)
    {
        var arriving = entrance < 0 ? [] : starts.Where(s => s.Group == entrance).ToArray();
        if (arriving.Length == 0) arriving = starts.Length > 0 ? [starts[0]] : [];
        var position = fallback;
        float yaw = 0f;
        if (arriving.Length > 0)
        {
            var start = arriving[0];
            yaw = MathF.Atan2(start.Rotation[1], start.Rotation[0]) * 180f / MathF.PI + 90f;
            position = start.Position + new Vector3(0, 0, EyeHeight);
        }
        return (arriving, position, yaw);
    }

    /// <summary>
    /// Coming back to a place that is still loaded (the main sequence's arrival on a resident
    /// location): it is not built again. The party is put on the entrance's starts — the leader's
    /// thing, which was taken out of this world when the party left, goes back in on the first of
    /// them facing the way it faces — and everything else is as it was left.
    /// <para>
    /// **Except who is steered**. The steered character and the party's
    /// selected member are the engine's, not the level's (<c>SetSteered</c> sets one global,
    /// and <c>party+4</c> is the party's), so a journey never leaves them behind. Vitomir keeps both
    /// on the place's <see cref="Minds"/>, and a place kept loaded still named whoever was steered
    /// when the party last left it — a companion's thing from that visit, out of the world since.
    /// The keys still drove the leader, but the place's minds took him for a follower of that ghost:
    /// he slid along standing still. So they are the leader again here, as they are in a place built
    /// for the journey (<see cref="Ai.Minds"/>' own start), and the harness hands the controls on to
    /// whoever was steered when the party set out, as it does after any journey.
    /// </para>
    /// </summary>
    public void Reenter(int entrance)
    {
        var (arriving, position, yaw) = ArrivalsAt(AllStarts, entrance, StartPosition);
        Entrance = entrance;
        Arrivals = arriving;
        StartPosition = position;
        StartYaw = yaw;
        if (Player is not { } player) return;
        var leader = player.Thing;
        if (Minds is { } minds) minds.Steered = minds.Selected = leader;
        if (arriving.Length > 0)
        {
            var s0 = arriving[0];
            float facing = MathF.Atan2(s0.Rotation[1], s0.Rotation[0]) * (180f / MathF.PI);
            leader.Orientation = new Vector3(0, 0, facing);
            if (leader.InWorld) Move(leader, s0.Position);
            else { leader.Position = s0.Position; leader.Sector = At(s0.Position, s0.Sector); }
        }
        Enter(leader);
        player.Stop();
        player.Ground();
    }

    /// <summary>
    /// <paramref name="player"/> names the template to put a player in as — the chronicle's
    /// metafile says which (<c>PLAYER Christof</c>). It is appended to the thing list after every
    /// scene thing, so a script's CODEXPARAMS thing index, which counts through the scene's own
    /// things, cannot reach it; leave it null and nothing changes.
    /// </summary>
    /// <param name="entrance">
    /// Which **entrance** to put the player at, or −1 for "the first start in the file", which is
    /// what every caller wanted before travel existed. See <see cref="Entrances"/>.
    /// </param>
    /// <param name="guids">The game's GUID counter (<see cref="ThingGuids"/>); a new one when null.</param>
    public static Location Load(GameFiles files, string name, bool things = true, bool sky = true,
                                string? scene = null, string? player = null, int entrance = -1,
                                ThingGuids? guids = null)
    {
        guids ??= ThingGuids.Building ?? new ThingGuids();
        int playerAt = -1;
        var sceneGuids = (First: guids.Peek, End: guids.Peek);
        var level = Levels.L2.L2Levels.Read(files, name);   // the .nil; a converted L2 package only when --l2 names a folder (ours)
        var styles = new LightStyles();
        var materials = new MaterialCache(files);
        var mesh = new SceneMesh();
        mesh.AddLevel(level, materials);

        var world = Levels.Cells.CellScene.World(level, files, name, ReadScene(files, name));   // a cooked cell level's starts, or the 1.1 scene it carries (ours)
        var chapter = string.IsNullOrEmpty(scene) || scene.Equals(name, StringComparison.OrdinalIgnoreCase)
            ? null : Levels.Cells.CellScene.Chapter(level, files, scene, ReadScene(files, scene));
        // Whichever scene the game would call current: its settings are the ones in force.
        var current = chapter ?? world;

        var worldThings = Array.Empty<WorldThing>();
        var motions = new MotionSets(files);
        TemplateSet? templates = null;
        if (world is not null || chapter is not null)
        {
            templates = new TemplateSet(files);
            foreach (var extra in (world?.TemplateFiles ?? []).Concat(chapter?.TemplateFiles ?? []))
                if (files.TryRead("misc/templates/" + (extra.Contains('.') ? extra : extra + ".not"), out var notData))
                    templates.AddFile(NotFile.Read(notData, extra));
            // One thing list, the world scene's first: a thing reference in a script's CODEXPARAMS
            // is an index into it, and the original appends the chapter's things to the world's.
            var placed = new List<NsdThing>([.. (world?.Things ?? []).Concat(chapter?.Things ?? [])]);
            // The player stands on the start of the entrance asked for — the first start in the
            // file when nobody asked, which is every caller that predates travel — facing the way
            // it faces.
            var startList = chapter is { Starts.Length: > 0 } ? chapter.Starts : world?.Starts ?? [];
            if (entrance >= 0 && startList.FirstOrDefault(s => s.Group == entrance) is { } atGroup)
                startList = [atGroup, .. startList.Where(s => !ReferenceEquals(s, atGroup))];
            if (!string.IsNullOrEmpty(player) && startList.Length > 0)
            {
                var s0 = startList[0];
                float facing = MathF.Atan2(s0.Rotation[1], s0.Rotation[0]) * (180f / MathF.PI);
                placed.Add(NsdThing.Placed(player, s0.Position, new Vector3(0, 0, facing), s0.Sector));
                playerAt = placed.Count - 1;
            }
            worldThings = BuildThings([.. placed], templates);
            sceneGuids = NumberThings(worldThings, playerAt, guids, files, templates, PlaceKey(name, scene));   // the game's counter (LocationGuids.cs)
            // The level and the styles are what a thing with no baked colours is lit from.
            mesh.Level = level;
            mesh.Styles = styles;
            if (things) mesh.AddThings(worldThings, files, materials, motions);
        }
        // The sky's materials have to reach the cache before a renderer uploads the textures.
        var skyModel = current is not null && sky ? World.Sky.Load(current, files, materials) : null;

        // Player starts live on the world, and a scene replaces them only when it has some of its
        // own. No shipped chapter scene has any, so in practice they are the level's.
        var starts = chapter is { Starts.Length: > 0 } ? chapter.Starts : world?.Starts ?? [];
        // **A start's GROUP is its entrance number, and the group holds one start per party slot.**
        // Measured rather than assumed: across all 14 chronicles, 500 of the 510 exits that lead
        // anywhere name an entrance the destination level has a group for, and **499 of those
        // groups hold exactly four starts** — four being the party. The ten that miss are
        // level-builder slips (an exit pointing at an entrance nobody built) and fall back here to
        // the first start, which is what the engine has to do with them too.
        var (arriving, position, yaw) = ArrivalsAt(starts, entrance, (mesh.BoundsMin + mesh.BoundsMax) * 0.5f);
        // The walkable ground and the solid space are the level's, whether or not anyone is
        // standing on the one or bumping into the other yet.
        var walk = new WalkMap(level);
        var solid = new Collision(level);
        LoadBoxes(worldThings, files);
        var props = new Bodies(level, worldThings);
        var paths = new PathFinder(level);
        Player? thePlayer = null;
        if (!string.IsNullOrEmpty(player) && worldThings.Length > 0 && worldThings[^1].Template == player)
        {
            thePlayer = new Player
            {
                Thing = worldThings[^1], Map = walk, Motions = motions, Solid = solid, Props = props, Path = paths,
            };
            thePlayer.Ground();
            // Making the player is `AddPlayer` and then the formation bit, the same pair
            // `CodexPlayer.AddToParty` does for a companion. THING_AF_PARTY is the engine's own
            // (flag `0x20000000`), and the status bar's pip is the formation
            // bit — so the steered character's pip is lit from the moment the level opens.
            if (thePlayer.Thing.Actor is { } started)
            {
                started.Flags |= Actor.Flag.Party;
                started.Flags2 |= Actor.Flag2.InFormation;
            }
        }

        // THING_TYPE_PLAYER is what the AI's paired main flags mean by "party" rather than "enemy",
        // and what the quick-slot filters and the five-times weighting both test.
        foreach (var built in worldThings)
            if (built.Actor is { } who) { who.IsPlayerThing = built.Type == ThingType.Player; who.ClanRecord = Rpg.Clans.For(files)[who.Clan]; }

        var place = new Location
        {
            Name = name, Level = level, Scene = current, WorldScene = world, ChapterScene = chapter,
            SceneFile = chapter is null ? (world is null ? "" : name) : scene!,
            Materials = materials, Mesh = mesh, Templates = templates, Files = files,
            Sky = skyModel, SkyWindow = skyModel is not null ? new SkyWindow(level) : null, SkyLoaded = sky,
            Fog = FogSettings.FromScene(current), StartPosition = position, StartYaw = yaw,
            Entrance = entrance, Arrivals = arriving, AllStarts = starts,
            FarPlane = current?.ZClip ?? 0f, Styles = styles, Motions = motions,
            Walk = walk, Solid = solid, Props = props, Paths = paths, Player = thePlayer, Guids = guids,
            ScriptNames = [.. worldThings.Select(t => t.Script).Where(n => n.Length > 0)
                                         .Distinct(StringComparer.OrdinalIgnoreCase)],
        };

        place.SceneGuids = sceneGuids;
        place.Adopt(worldThings);
        // The regions go into their sectors' lists once everything is placed, in the scene order
        // the thing loader lays them in (region by region).
        place.Regions = new Regions(worldThings, level.Sectors.Length);
        // The conversation starts `TeleportPartyToConversation` searches: the chapter
        // scene's when it has any, as for the player starts above. Every chapter scene that has
        // conversations carries their starts itself (MON1_8_2: six groups of two) and the level
        // scenes carry none, so the two rules — this one and appending — come to the same list.
        place.ConversationStarts = chapter is { ConversationStarts.Length: > 0 } ? chapter.ConversationStarts
                                 : world?.ConversationStarts ?? [];
        // The mesh was built from this array in this order, so `Drawn` starts as a copy of it and
        // `Show` appends to the end from here on.
        place.drawn.AddRange(worldThings);
        for (int i = 0; i < worldThings.Length; i++) place.drawnAt[worldThings[i]] = i;

        var log = Diag.For<Location>();
        log.LogInformation("loaded {Summary}", place.Summary);
        if (chapter is not null)
            log.LogDebug("{Level}: world scene {World}, chapter scene {Chapter} ({WorldThings} + {ChapterThings} things)",
                         name, name, scene, world?.Things.Length ?? 0, chapter.Things.Length);
        foreach (var skipped in mesh.Skipped) log.LogDebug("{Level}: {Skipped}", name, skipped);
        return place;
    }

    /// <summary>
    /// Gives every THING_COLLIDE_BBOX thing the box it collides with: its model's own bounds. The
    /// engine copies them in when the thing is finalized and only for a thing that is a box then,
    /// so a thing a script turns into a box later has none — which is what
    /// leaving every other thing's box empty reproduces.
    /// </summary>
    static void LoadBoxes(IEnumerable<WorldThing> things, GameFiles files)
    {
        var bounds = new Dictionary<string, (Vector3 Min, Vector3 Max)?>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in things) LoadBox(t, files, bounds);
    }

    /// <summary>One thing's box, so a spawned thing gets the same treatment a placed one does.</summary>
    static void LoadBox(WorldThing t, GameFiles files,
                        Dictionary<string, (Vector3 Min, Vector3 Max)?> bounds)
    {
        if (t.Collide != CollideShape.Box) return;
        // A thing whose geometry the scene carries inline — the trackers and the doors the
        // level compiler cut out of the world — has its bounds in that record instead.
        if (t.Scene.Model is { } brush) { (t.BoxMin, t.BoxMax) = (brush.BoundsMin, brush.BoundsMax); return; }
        if (t.Model.Length == 0) return;
        if (!bounds.TryGetValue(t.Model, out var b))
            bounds[t.Model] = b = files.TryRead("3d/models/" + t.Model, out var data)
                                   ? NodModel.ReadBounds(data, t.Model) : null;
        if (b is { } box) (t.BoxMin, t.BoxMax) = (box.Min, box.Max);
    }

    /// <summary>One scene file, or null when the install has not got it.</summary>
    static NsdScene? ReadScene(GameFiles files, string name) =>
        files.TryRead($"scenes/{name}.nsd", out var data) ? NsdScene.Read(data, name + ".nsd") : null;

    /// <summary>
    /// The scene's things with their starting state: the flags and description id their template
    /// gives them, their placement, and their keyframes. Scripts take it from here.
    /// </summary>
    static WorldThing[] BuildThings(NsdThing[] scene, TemplateSet templates)
    {
        var list = new WorldThing[scene.Length];
        for (int i = 0; i < list.Length; i++)
            list[i] = Build(scene[i], templates, index: i, guid: 0);       // numbered next, from the game's counter (NumberThings)
        return list;
    }

    /// <summary>
    /// One thing, from its placement record and its template. Pulled out of the scene loader
    /// because <see cref="Spawn"/> builds a thing exactly the same way — the engine's own
    /// thing making is one path for both, and a thing a script spawns differs from one the level
    /// builder placed only in where its GUID comes from. That routine is itself a thin shell over
    /// another, which does the actual work this method does: walk the template's parent chain
    /// (<see cref="TemplateSet.Resolve"/>), allocate the thing once its `TYPE` tag is found,
    /// copy the given name onto it, and apply every other tag.
    /// </summary>
    public static WorldThing Build(NsdThing t, TemplateSet templates, int index, int guid, int now = 0)
    {
        var tags = templates.Resolve(t);
        var thing = new WorldThing
        {
            Index = index,
            Guid = guid,
            Scene = t,
            Template = t.Template,
            Type = tags.TryGetValue(Tags.Type, out var ty) ? (ThingType)ty.AsUInt() : ThingType.None,
            Sector = t.Sector,
            Flags = tags.TryGetValue(Tags.ThingFlags, out var fl) ? fl.AsUInt() : 0,
            DescriptionId = tags.TryGetValue(Tags.DescriptionId, out var d) ? d.AsString() : "",
            // **The template's script counts, not only the scene's.** Tag 11 goes through the same
            // merged modifier list as every other tag (the template chain walked, then the
            // placement's own tags on top), and its handler (the script tag's, reached from
            // the tag dispatch's category 2) attaches the class. Vitomir read only the placement's tag, so
            // the 89 templates that carry their own script — the haven's vault and save point, the
            // teleporters, the torch-bearers, the Cappadocians' awakening, every discipline tome —
            // came into the world without one. A click on the convent's vault and
            // nothing opened.
            Script = tags.TryGetValue(Tags.Script, out var scr) ? ScriptName(scr.AsString()) : "",
            Position = t.Position,
            Orientation = t.Orientation,
            Keyframes = t.Keyframes,
            Scale = tags.TryGetValue(Tags.ModelScale, out var sc) && sc.AsFloat() > 0 ? sc.AsFloat() : 1f,
            ModelFlags = tags.TryGetValue(Tags.ModelFlags, out var mf) ? mf.AsUInt() : 0,
            // Tag 8 goes straight onto the thing.
            RenderFlags = tags.TryGetValue(Tags.RenderFlags, out var rf) ? rf.AsUInt() : 0,
            MoveType = tags.TryGetValue(Tags.MoveType, out var mt) ? mt.AsUInt() : 0,
            Model = tags.TryGetValue(Tags.Model, out var mo) ? mo.AsString() : "",
            Radius = tags.TryGetValue(Tags.Radius, out var ra) && ra.AsFloat() >= 0f ? ra.AsFloat() : 0f,
            Height = tags.TryGetValue(Tags.Height, out var he) && he.AsFloat() >= 0f ? he.AsFloat() : 0f,
            ProxRadius = tags.TryGetValue(Tags.ProxRadius, out var prox) && prox.AsFloat() > 0f ? prox.AsFloat() : 0f,
            // The engine takes the collide type only when it is one of the four it knows;
            // anything else leaves the thing bodiless.
            Collide = tags.TryGetValue(Tags.Collide, out var co) && co.AsUInt() < 4
                      ? (CollideShape)co.AsUInt() : CollideShape.None,
            MotionSet = tags.TryGetValue(Tags.MotionSet, out var ms) ? ms.AsString() : "",
            // Tag 6 names the `.nag`, tag 110 the struck material (stone by default).
            Foley = tags.TryGetValue(Tags.Foley, out var fo) ? fo.AsString() : "",
            SoundType = tags.TryGetValue(Tags.SoundType, out var st) ? st.AsInt() : 1,
            DropItem = tags.TryGetValue(Tags.DropItem, out var di) ? di.AsString() : "",
            // Only a character (type 0) and a container (type 8) keep one.
            TreasureClass = tags.TryGetValue(Tags.TreasureClass, out var tc)
                            && (tags.TryGetValue(Tags.Type, out var tct) ? tct.AsUInt() : 9u) is 0u or 8u
                            ? (int)tc.AsUInt() : 0,
            DestroyTemplate = tags.TryGetValue(Tags.DestroyTemplate, out var dt) ? dt.AsString() : "",
            ExplosionTemplate = tags.TryGetValue(Tags.Explosion, out var ex) ? ex.AsString() : "",
            // Only a character carries character state, and the type tag is what says so.
            Actor = (ThingType)(tags.TryGetValue(Tags.Type, out var at) ? at.AsUInt() : (uint)ThingType.None)
                    is ThingType.Actor or ThingType.Player ? World.Actor.From(tags) : null,
        };
        // The physics tags, applied the way the engine applies them: its setter tests
        // the move type before it looks at the tag at all, and answers 0 for anything that is not a
        // physics thing. So these are read here, after MoveType, rather than in the initialiser —
        // and a template that sets a gravity on a door really does have it thrown away.
        if (thing.MoveType == WorldThing.MovePhysics)
        {
            if (tags.TryGetValue(Tags.PhysicsFlags, out var pf)) thing.PhysicsFlags = pf.AsUInt();
            if (tags.TryGetValue(Tags.PhysicsVelocity, out var pv)) thing.Velocity = pv.AsVector3();
            if (tags.TryGetValue(Tags.PhysicsRotVelocity, out var pr)) thing.RotVelocity = pr.AsVector3();
            if (tags.TryGetValue(Tags.PhysicsElasticity, out var pe)) thing.Restitution = pe.AsFloat();
            if (tags.TryGetValue(Tags.PhysicsGravity, out var pg)) thing.Gravity = pg.AsFloat();
        }
        // A region's flags share the actor's word and are applied only to a region (case 0x5b).
        if (thing.Type == ThingType.Region && tags.TryGetValue(Tags.RegionFlags, out var rgf))
            thing.RegionFlags = rgf.AsUInt();
        // The projectile tags, gated on the type the way the physics ones are gated on the move kind.
        // It is the same word an actor keeps its own flags in.
        if (thing.Type == ThingType.Projectile)
        {
            if (tags.TryGetValue(Tags.ProjectFlags, out var jf)) thing.ProjectileFlags = jf.AsUInt();
            if (tags.TryGetValue(Tags.ProjectDamage, out var jd)) thing.ProjectileDamage = (int)jd.AsUInt();
            if (tags.TryGetValue(Tags.ProjectDamageType, out var jt)) thing.ProjectileDamageType = jt.AsUInt();
            if (tags.TryGetValue(Tags.ProjectMaxDist, out var jr) && jr.AsFloat() >= 0f)
                thing.ProjectileRange = jr.AsFloat();
        }
        // A container's own setter takes one tag, CONTAINER_HEALTH, and a health above
        // zero is what makes it breakable.
        if (thing.Type == ThingType.Container && tags.TryGetValue(Tags.ContainerHealth, out var hp))
        {
            thing.ContainerHealth = hp.AsUInt();
            if (thing.ContainerHealth > 0f) thing.State |= Rpg.Container.Breakable;
        }
        // TIMER becomes an absolute stamp the moment the template is applied, so a thing made at run
        // time counts from then. A gib chunk's five seconds start as it leaves the body.
        if (tags.TryGetValue(Tags.Timer, out var tr) && tr.AsUInt() != 0)
            thing.ExpireAt = now + (int)tr.AsUInt();

        // What it fights with. The engine reads ACTOR_WEAPON's template when the character is
        // finalized and hangs it off the actor; no weapon means it cannot attack at
        // all, which is the first thing an attack order tests.
        // What it fights with **when it is holding nothing** — the engine's attack record, which is
        // where ACTOR_WEAPON goes. What it is actually holding starts out the same
        // and is replaced the moment something is equipped (`Rpg.Equipment`).
        if (thing.Actor is { WeaponTemplate.Length: > 0 } a)
            a.Weapon = a.DefaultWeapon = Rpg.Weapon.From(a.WeaponTemplate, templates.Resolve(a.WeaponTemplate));

        // And what it *is*, when it is an item. **An item is a thing** — `rpgItem` derives from
        // `simThing`, a save writes every carried item as a thing record with a GUID, and the only
        // thing that separates a sword on the floor from a sword in a pack is whether its world
        // is set. So this is built here, beside the actor, for the same reason and from the same
        // tags: the two are the type-specific half of one constructor.
        if (Rpg.Item.From(t.Template, tags) is { } carried)
        {
            thing.Item = carried;
            carried.Thing = thing;
        }
        return thing;
    }

    /// <summary>A camera at the scene's player start, with the original's field of view and clip planes.</summary>
    public FreeCamera Camera()
    {
        var camera = new FreeCamera { Position = StartPosition, Yaw = StartYaw };
        if (FarPlane != 0) camera.Far = FarPlane;
        return camera;
    }

    /// <summary>One line about what was loaded, for the viewer and the test log.</summary>
    public string Summary =>
        $"{Name}: {Level.Sectors.Length} sectors, {Mesh.ThingsDrawn} things, {Mesh.Batches.Count} batches, " +
        $"{Mesh.Indices.Length / 3} triangles, {Mesh.Animated.Count} animating, {Materials.Items.Count} materials, " +
        $"{Level.Lightmaps.Length} lightmap pages, " +
        (Sky is null ? "no sky" : $"sky {Sky.Name} ({SkyWindow!.SurfaceCount} sky surfaces)");
}
