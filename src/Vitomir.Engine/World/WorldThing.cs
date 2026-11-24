// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Anim;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// A thing in the world, as the engine and the scripts see it at run time: the scene's placement
/// plus the state that changes while the game runs. The scene's own record (<see cref="NsdThing"/>)
/// stays as it was loaded; this is what scripts read and write through the CodexThing natives.
/// </summary>
public sealed class WorldThing
{
    /// <summary>Its place in the scene's thing list, which is how CODEXPARAMS refer to it. The player's, which follows the
    /// scene's own, moves on when a chapter swap changes how many the chapter has (ChapterSwap.cs).</summary>
    public required int Index { get; set; }
    /// <summary>
    /// What scripts call it: its number from the game's one counter (<see cref="ThingGuids"/>), from 1,
    /// so 0 means "no thing". Set when a place numbers it or a load gives it back (<see cref="Location.Rekey"/>).
    /// </summary>
    public required int Guid { get; set; }
    public required NsdThing Scene { get; init; }
    public required string Template { get; init; }
    public ThingType Type { get; init; }
    /// <summary>The sector it sits in, or −1. It changes when the thing moves.</summary>
    public long Sector { get; set; }

    /// <summary>
    /// The place it is in — the engine's `pWorld`, which every thing carries and
    /// which the placement sets and the teardown tests. Null for a thing that has never been in a
    /// world. Having it here is what lets anything that happens *to* a thing — a death that leaves
    /// a skeleton, a crate that drops its contents — reach the world without being handed it.
    /// </summary>
    public Location? Place { get; set; }

    /// <summary>THING_FLAG_*, from the template's tag 7 and then whatever scripts set.</summary>
    public uint Flags { get; set; }
    /// <summary>The string id its description comes from (tag 82); scripts swap it for state, e.g. a locked door's "_L".</summary>
    public string DescriptionId { get; set; } = "";
    /// <summary>The script attached to it, or empty.</summary>
    public string Script { get; init; } = "";

    public Vector3 Position { get; set; }
    /// <summary>
    /// How far above <see cref="Position"/> it is drawn and looked at, in units (ours; presentation only, and
    /// always nothing in a 1.1 level): a cell level eases a walker's height over its steps
    /// (<see cref="Levels.Cells.CellStepEase"/>).
    /// </summary>
    public float ShownLift { get; set; }
    /// <summary>Degrees, as the scene stores it.</summary>
    public Vector3 Orientation { get; set; }

    /// <summary>
    /// Turn to look at a point, on the level — the engine's face-point, which is one call to
    /// the look-at.
    ///
    /// **It is instant, and it is yaw only.** The caller passes the target's x and y with *the
    /// thing's own z* (it reads its own z rather than the point's), so the direction is
    /// level however far above or below the target is; and the look-at writes the thing's 3 × 4
    /// matrix outright rather than turning towards it over time. Row 1 of that matrix is set to the
    /// normalized direction — the row that holds the model's **local +Y** — which is the other half
    /// of the statement in <see cref="World.Player.Facing"/> that a thing with no turn on it faces
    /// +Y. Rebuilding all three rows levels the thing, so any pitch or roll it had goes.
    ///
    /// A zero-length direction leaves the thing exactly as it was: the look-at compares the length
    /// against zero and returns before touching the matrix.
    /// </summary>
    public void Face(Vector3 point)
    {
        float dx = point.X - Position.X, dy = point.Y - Position.Y;
        if (dx == 0f && dy == 0f) return;
        Orientation = new Vector3(0f, 0f, Player.Wrap(MathF.Atan2(-dx, dy) * (180f / MathF.PI)));
    }

    /// <summary>Turn to look at another thing, which is what every combat caller of the face-point does.</summary>
    public void Face(WorldThing other) => Face(other.Position);

    /// <summary>
    /// The poses it can move between: doors, lifts and pivots are authored as keyframes. A script
    /// may give a thing that has none with <c>AllocateFrames</c>, so this is not fixed at load.
    /// </summary>
    public NsdKeyframe[] Keyframes { get; set; } = [];
    /// <summary>How the thing's model is scaled (tag 235), and its render flags (tag 10).</summary>
    public float Scale { get; init; } = 1f;
    /// <summary>
    /// The model's own flags (`MODELFLAGS`, tag 10, kept on the model): how it
    /// is lit — <c>PRE LIT</c> 0x1, <c>FULLY LIT</c> 0x2, <c>CONSTANT LIT</c> 0x4 — and the engine's
    /// own bits above those. Not what a script's <c>SetRenderFlags</c> reaches: that is
    /// <see cref="RenderFlags"/>.
    /// </summary>
    public uint ModelFlags { get; set; }

    /// <summary>
    /// `THING_RENDERFLAG_*` (`THINGRENDERFLAGS`, tag 8): <c>HALO</c> 0x1,
    /// <c>DON'T RENDER</c> 0x2, <c>DROP SHADOW</c> 0x4, <c>FULL SHADOW</c> 0x8, <c>FLUORO HALO</c>
    /// 0x10, <c>TWINKLE</c> 0x20, <c>SORT RENDER</c> 0x40, <c>REFLECTION ONLY</c> 0x80,
    /// <c>RECREATE</c> 0x100 (the SDK's `noteditordata.txt`). Scripts turn them on and off with
    /// `SetRenderFlags` and `ClearRenderFlags`, which is how a thing is made
    /// invisible. The draw returns at once for <c>DON'T RENDER</c>, and skips the model
    /// and everything attached to it for <c>REFLECTION ONLY</c> — which is also the bit the
    /// first-person look puts on the character whose eyes it looks through.
    /// </summary>
    public uint RenderFlags { get; set; }

    /// <summary>Whether the draw leaves this thing's model out: <c>DON'T RENDER</c> or <c>REFLECTION ONLY</c>.</summary>
    public bool ModelHidden => (RenderFlags & (0x2u | 0x80u)) != 0;

    /// <summary>
    /// How the thing moves (`MOVETYPE`, tag 78). It is **not** fixed at placement:
    /// `CodexThing.SetMoveType` writes this word outright, and that is how a script
    /// turns a plain prop into something its keyframes can carry. Four kinds, and each of them
    /// gates a different half of the engine:
    ///
    /// <list type="bullet">
    /// <item><see cref="MoveNone"/> — it does not move on its own at all.</item>
    /// <item><see cref="MoveWalker"/> — a character on the walk graph.</item>
    /// <item><see cref="MoveTrack"/> — keyframes. <see cref="MoveToFrame"/> and
    /// <see cref="RotatePivot"/> both refuse anything else.</item>
    /// <item><see cref="MovePhysics"/> — velocity and gravity. Every physics native tests this
    /// first and answers zero or false for anything else, so a door or a prop simply has none.</item>
    /// </list>
    /// </summary>
    public uint MoveType { get; set; }

    /// <summary>It does not move itself (`THING_MOVE_NONE`).</summary>
    public const uint MoveNone = 0;
    /// <summary>A character the walker drives (`THING_MOVE_WALK`). See <see cref="Player"/>.</summary>
    public const uint MoveWalker = 1;
    /// <summary>The kind of <see cref="MoveType"/> that travels between its <see cref="Keyframes"/>.</summary>
    public const uint MoveTrack = 2;
    /// <summary>The kind of <see cref="MoveType"/> that has velocity and physics flags.</summary>
    public const uint MovePhysics = 3;

    /// <summary>The highest <see cref="MoveType"/> the engine knows; `SetMoveType` rejects anything above it.</summary>
    public const uint MoveKinds = 4;

    /// <summary>
    /// `THING_PHYS_*`, for a <see cref="MovePhysics"/> thing. <see cref="Physics"/>
    /// integrates these: bit <see cref="Physics.Gravity"/> makes the thing fall, and bit
    /// <see cref="Physics.Asleep"/> stops it being stepped at all. Switching a thing to
    /// <see cref="MovePhysics"/> clears the whole word, so a physics thing does not fall
    /// until something says so.
    /// </summary>
    public uint PhysicsFlags { get; set; }

    /// <summary>Its velocity and rotational velocity. See <see cref="PhysicsFlags"/>.</summary>
    public Vector3 Velocity { get; set; }
    /// <inheritdoc cref="Velocity"/>
    public Vector3 RotVelocity { get; set; }

    /// <summary>
    /// How hard this thing is pulled down, in units per second squared. **200 is the
    /// engine's own default**, written when a thing becomes <see cref="MovePhysics"/> (and
    /// again when the move type is set). It is a per-thing number: there is no global gravity constant anywhere in
    /// 1.1, and the *direction* is a hard-coded unit vector (<see cref="Physics.Down"/>)
    /// that no data file can change.
    ///
    /// The same offset is the walker's destination Z while a thing is `MOVETYPE 1` — the walk refuses
    /// outright unless the move kind is 1 — so that block of fields is a discriminated
    /// union on the move kind. Vitomir keeps them as separate fields, which changes nothing
    /// observable; but anything reading these offsets out of a save or a network packet has to know
    /// which reading applies.
    /// </summary>
    public float Gravity { get; set; } = 200f;

    /// <summary>
    /// How bouncy it is — the `e` in the contact impulse. **1.0** when a thing becomes
    /// <see cref="MovePhysics"/>, which is a perfectly elastic bounce before the 0.8 damping.
    /// </summary>
    public float Restitution { get; set; } = 1f;

    /// <summary>
    /// The change in velocity gravity made **this frame** — a delta-v, not an
    /// acceleration, which is the shape a reimplementation naturally gets wrong. The collide sweep
    /// subtracts a fraction of it back off on a contact, so it has to be the same quantity.
    /// </summary>
    public Vector3 AppliedDeltaV { get; set; }

    /// <summary>
    /// The speed along the surface normal at the last contact, and whether that contact was loud
    /// enough to be heard (`vn &lt; -10`). The engine picks the sound from the surface's own
    /// material; Vitomir has no sound system, so this is what a test and the journal read instead.
    /// </summary>
    public float LastImpactSpeed { get; set; }
    /// <inheritdoc cref="LastImpactSpeed"/>
    public bool Audible { get; set; }
    /// <summary>
    /// When the character's current line ends, on the game clock, and the channel it
    /// is on. A voice-flagged sound on a character is refused until then, and
    /// a death stops that channel. Written whenever a line starts.
    /// </summary>
    public int MouthBusyUntil { get; set; }
    /// <inheritdoc cref="MouthBusyUntil"/>
    public int MouthChannel { get; set; }

    /// <summary>
    /// The thing's foley file (template tag 6, `FOLEY`, a `.nag` in `misc/Foley`), which is what it
    /// sounds like: its footsteps, its swings, its voice. Empty for none. `SetFoley` changes it.
    /// </summary>
    public string Foley { get; set; } = "";

    /// <summary>
    /// What it sounds like when struck (template tag 110, `SOUNDTYPE`): flesh, stone, metal, leather,
    /// cloth, mail, magic, kevlar, wood or bone — picked as the <c>hit…</c> event on a blow. A thing
    /// whose template says nothing is <b>stone</b> (the constructor writes 1).
    /// </summary>
    public int SoundType { get; set; } = 1;

    /// <summary>The NOD model it draws as (tag 4), or empty when it has an inline brush model or none.</summary>
    public string Model { get; set; } = "";

    /// <summary>What this thing rides, or null. See <see cref="World.Attachments"/>.</summary>
    public WorldThing? AttachedTo { get; set; }

    /// <summary>What rides this thing, most recently attached first.</summary>
    public List<Attachment> Attachments { get; } = [];
    /// <summary>How wide it is (tag 13), which is what keeps a walker clear of it.</summary>
    public float Radius { get; init; }
    /// <summary>How tall its body is (tag 14) — what a <see cref="CollideShape.Cylinder"/> rises by.</summary>
    public float Height { get; init; }
    /// <summary>
    /// How near a character must come to touch it (tag 234, `PROXRADIUS`, the tag setter's case
    /// 0xea, a negative value taken as 0) — what a `THING_FLAG_PROXIMITYTRIGGER` asks with
    /// (<see cref="Proximity"/>), not its <see cref="Radius"/>.
    /// </summary>
    public float ProxRadius { get; init; }
    /// <summary>
    /// The body it presents to collision (tag 12). Scripts can change it: CodexThing.SetCollideType.
    /// </summary>
    public CollideShape Collide { get; set; }
    /// <summary>
    /// Its model's bounding box, which is the body a <see cref="CollideShape.Box"/> thing has. The
    /// engine copies it out of the model when the thing is finalized, and only then — so a thing
    /// made a box later by a script keeps whatever box it was built with, which for anything else
    /// is none.
    /// </summary>
    public Vector3 BoxMin { get; set; }
    public Vector3 BoxMax { get; set; }
    /// <summary>
    /// The motion set its template names (tag 9): which animation it plays for what. A shape change
    /// swaps it (<see cref="Rpg.ShapeShift"/>).
    /// </summary>
    public string MotionSet { get; set; } = "";

    /// <summary>
    /// `DROPITEM` (tag 115): an item a character or a container **holds**, not one it scatters.
    /// The generic setter stores it in one field on a character and another
    /// on a container, and both are read at the same kind of moment — the character's death
    /// and the container's opening — which make the item and **give it to**
    /// the holder. 13 of the 14 templates that carry one are characters: the bosses and
    /// their signature items, LeComte's monocle, Ahzra's dagger, Etrius's blood ring.
    ///
    /// **What happens to it**: at the death or the opening the
    /// engine makes the item and puts it **on the floor beside** the character or the container
    /// (the loot drop into the `SpawnThingNear` placer), and a thing with a DROPITEM rolls no treasure —
    /// the one wins over the other. That is `Rpg.Loot`. The destroy drops nothing: it is
    /// <see cref="DestroyTemplate"/>'s moment, not this field's.
    /// </summary>
    public string DropItem { get; init; } = "";

    /// <summary>
    /// `TREASURECLASS` (tag 92): which class of `misc/game.ntt` the thing rolls when it dies or is
    /// opened, 0 for none. The setter keeps it only for a
    /// <see cref="ThingType.Actor"/> and a <see cref="ThingType.Container"/>
    /// — a party member (<see cref="ThingType.Player"/>) never has one. **A scene's own
    /// tag overrides the template's**, and for containers that is the usual case: 618 placed
    /// chests and barrels carry a class of their own, the progression bands by level.
    /// </summary>
    public int TreasureClass { get; init; }

    /// <summary>
    /// `DESTROY_TEMPLATE` (tag 117 — the tag setter's `case 0x75`, for any thing): what it
    /// **turns into** when it is destroyed. The remove spawns it at the dying thing's
    /// position and, when the dier was a <see cref="ThingType.Container"/>, gives it a ten-second
    /// life and a fade. Only four templates carry one, and all four are containers: the two plain
    /// barrels break into `_container_barrel1_destroyed`, and the two volatile ones — a barrel and a
    /// hazmat drum — turn into **explosions**.
    /// </summary>
    public string DestroyTemplate { get; init; } = "";

    /// <summary>
    /// `CONTAINER_HEALTH` (tag 116), a container's own hit points. The container's setter
    /// stores it and, **when it is above zero, marks the container breakable** — bit 1 of
    /// <see cref="State"/> — which is what turns the click from *open* into *attack*. Thirteen
    /// shipped containers have 0 and open; three have 1 and break at a touch; one has 20.
    /// </summary>
    public float ContainerHealth { get; set; }

    /// <summary>
    /// Who is carrying it, for an item, weapon or armour — which the engine's own
    /// assert calls `pItem->pOwner`. A spawn from a carried thing happens at the owner instead
    /// (<see cref="Location.Spawn"/>).
    ///
    /// Null means nobody's inventory holds it, which for a thing that is also
    /// <see cref="InWorld"/> means it is lying on the floor.
    /// </summary>
    public WorldThing? Owner { get; set; }

    /// <summary>
    /// A **prop's owner** — a field of a prop (type 2; the same word is an actor's second flags), the GUID
    /// of whoever it is: a teleporter's end is given its creator's, the thing-made message
    /// carries it to the players' machines, and a network game's rollover names it
    /// (the rollover's prop branch; <see cref="PropOwners"/>). 0 is nobody's.
    /// </summary>
    public int PropOwner { get; set; }

    /// <summary>
    /// Who has it on their cursor — `pItem->pHolder`, the partner of
    /// <see cref="Owner"/>. The engine checks the owner first and falls back to this one wherever
    /// it asks "whose is this" (the drop order's gate, the network transfer), and
    /// The inventory's hold writes it as it puts an item in a hand.
    ///
    /// It is the same relationship <see cref="Actor.Held"/> states from the other end; both exist
    /// because the engine keeps both, and <see cref="Rpg.Ground"/> is what keeps them in step.
    /// </summary>
    public WorldThing? Holder { get; set; }

    /// <summary>
    /// What it *is*, for a <see cref="ThingType.Item"/>, <see cref="ThingType.Weapon"/> or
    /// <see cref="ThingType.Armor"/> — and **an item is a thing**, which is the whole point.
    ///
    /// In the original there is one object: `rpgItem` is a `simThing` of one of those three types,
    /// and whether it is in the world or in a pack is nothing but whether its world is set
    /// (<see cref="InWorld"/>). A save says the same thing from the other side: every carried item
    /// in `!main.nsg` is a **thing record** with its own GUID, with the item block appended.
    /// So picking something up is not a conversion, it is an unlink from
    /// the world and a link into an inventory; the object never changes.
    ///
    /// Null on anything that is not one of those three types, and null on an
    /// <see cref="Rpg.Item"/> made without a world behind it — which a test fixture may do and the
    /// game never does.
    /// </summary>
    public Rpg.Item? Item { get; set; }

    /// <summary>
    /// Whether it is **in the world**: the engine's `pWorld`, which is exactly what
    /// `simThing::RemoveFromWorld` clears and what every "is it really there" test in
    /// the engine reads. A carried item, an item on the cursor and a destroyed thing are all out of
    /// the world; only the first two still exist.
    /// </summary>
    public bool InWorld => Place is not null && !Removed;

    /// <summary>
    /// Who made it, for a thing that was spawned rather than placed — the GUID the engine writes
    /// onto a new <see cref="ThingType.Projectile"/> and onto a new
    /// <see cref="ThingType.Explosion"/>. It is how a fireball knows whose
    /// kill it was, and it is written for those two types and no others.
    /// </summary>
    public int SpawnedBy { get; set; }

    /// <summary>
    /// **Ours**: the GUID of the effect whose script made this thing (<see cref="EffectMade"/>), for a thing no
    /// save keeps (<see cref="Flag.SaveNever"/>) — a discipline's pad, its trail — or 0. 1.1 keeps no such
    /// record: the effect's script holds the thing's GUID and nothing holds the effect's. A save writes it with
    /// the effect (<c>Save.LoadedEffects</c>), which is how a load (the repair <c>loadedeffects</c>) knows
    /// what to make again.
    /// </summary>
    public int MadeByEffect { get; set; }

    /// <summary>
    /// `PROJ_*`, for a <see cref="ThingType.Projectile"/>. **The same word an actor
    /// keeps its `THING_AF_*` in** — the field is a union on the thing's type, which is why
    /// each reading has its own table and why three flags once
    /// looked dead. No shipped script ever calls `SetProjectileFlags`, so these come entirely from
    /// the template.
    /// </summary>
    public uint ProjectileFlags { get; set; }

    /// <summary>
    /// A region's own flags (tag 91, <c>CodexRegion.SetRegionFlags</c>). Bit
    /// <see cref="Regions.NoTracking"/> stops it reporting anybody coming or going.
    /// </summary>
    public uint RegionFlags { get; set; }

    /// <summary>
    /// Which of its sector's regions this thing is inside, a bit for each by its
    /// place in the sector's region list. See <see cref="Regions"/>.
    /// </summary>
    public uint InRegions { get; set; }

    /// <summary>
    /// What a projectile is worth (tag 85) — and it is **not** a number of hit points but
    /// a **dice pool**: the damage path rolls `floor(value / 10)` dice of 1–10 and adds
    /// `rand01 x (value - 10 x that)` for the remainder. So `PROJECT_DAMAGE 40` is
    /// four d10, 4 to 40, averaging 22 — the tabletop pool, written as a tenfold integer.
    /// **10 when the template names none**: the projectile's constructor writes 10 before
    /// the tags are applied — which only `woodGib` among the shipped projectiles
    /// relies on (its `_gibs` parent names none either), so a flying wood chip is one d10 (the
    /// A comparison with 1.1; it was 0 until then).
    /// </summary>
    public int ProjectileDamage { get; set; } = 10;

    /// <summary>Which kind of damage it does (tag 86). `PROJ_BULLET` rewrites it in flight.</summary>
    public uint ProjectileDamageType { get; set; }

    /// <summary>
    /// How far it reaches (tag 136), **1,000 units on nearly every shipped template**, and
    /// 128 on the molotov. It is the length of the hitscan's ray, and for a projectile that really
    /// flies it is the distance after which it gives up — that second use is not built.
    /// </summary>
    public float ProjectileRange { get; set; } = 100f;

    /// <summary>
    /// The template it spawns when it detonates (tag 128), or empty. 114 of the 155
    /// shipped projectiles have none and simply vanish.
    /// </summary>
    public string ExplosionTemplate { get; init; } = "";

    /// <summary>
    /// Whether it has left the world. The engine unlinks a removed thing from its sector and frees
    /// it; here it is taken out of the location's list and flagged, so anything still holding a
    /// reference can tell.
    /// </summary>
    public bool Removed { get; set; }

    /// <summary>
    /// Whether the thing's once-only finalize has run (bit <c>0x10000000</c>):
    /// the spawn transform, the launch and the born-with effects. A thing that leaves the world and
    /// comes back — a dropped sword, a party member returning to a place — does not get them twice.
    /// Set through <see cref="Finish"/>.
    /// </summary>
    public bool Finalized { get; private set; }

    /// <summary>
    /// **The spawn transform**: the finalize's first act is to copy the live
    /// matrix here (twelve words copied from the transform), so its
    /// translation row is **where the thing was when it came into the world**.
    ///
    /// That is the "home" the AI measures from — the pedestrian's <c>wanderRadius</c> (read
    /// directly) and the searcher's walk back to its post (the think copies it
    /// into the mind and looks up the walk node under it). Once this tree
    /// believed nothing ever wrote that row, because every write at that offset
    /// 0x80 belongs to another struct — the write is a block copy with no offset in it. But
    /// play in 1.1 says otherwise (guards go back to their posts) and the finalize was where it was.
    ///
    /// Before the finalize it answers the scene's placement, which is what it will be.
    /// </summary>
    public Vector3 SpawnPosition => spawnPosition ?? Scene.Position;
    /// <summary>The spawn transform's turn, in degrees — what a guard turns back to at its post.</summary>
    public Vector3 SpawnOrientation => spawnOrientation ?? Scene.Orientation;
    Vector3? spawnPosition, spawnOrientation;

    /// <summary>
    /// The finalize's once-only bookkeeping: record the spawn transform and mark it
    /// done. Answers false when it had already run.
    /// </summary>
    public bool Finish()
    {
        if (Finalized) return false;
        Finalized = true;
        spawnPosition = Position;
        spawnOrientation = Orientation;
        return true;
    }

    /// <summary>
    /// The finalize as a load does it: the spawn transform a save kept (1.1 writes it
    /// as the thing record's second matrix), rather than where the thing stands now — so a guard
    /// loaded mid-chase still walks back to its post.
    /// </summary>
    public void Rehome(Vector3 home, Vector3 homeOrientation)
    {
        Finalized = true;
        spawnPosition = home;
        spawnOrientation = homeOrientation;
    }

    /// <summary>
    /// What a save calls a thing that was **made** while the game ran — <c>P1_OLDT~12</c> — set
    /// when it is made (<see cref="Save.ThingIds"/>). Null for a thing the scene placed, whose name
    /// comes from its placement instead.
    /// </summary>
    public string? SaveId { get; set; }
    /// <summary>What it is playing, when it has a skeleton and a motion set that can drive it.</summary>
    public ThingAnimation? Animation { get; set; }

    /// <summary>
    /// An action the engine plays at a moment Vitomir has no animation to play it on yet — a mode
    /// and a rate for the action channel, played by whoever makes the animation and then cleared.
    /// The one user is a START HIDDEN character lying down (<see cref="Ai.Minds.Hide"/>): the engine
    /// builds a character's mind after its model, and Vitomir builds minds first.
    /// </summary>
    public (int Mode, float Rate)? Waiting { get; set; }
    /// <summary>
    /// Its character state, when it is one — a THING_TYPE_ACTOR or _PLAYER. This is what every
    /// CodexActor native reads and writes, and what <c>Codex.IsActorGuid</c> answers about.
    /// </summary>
    public Actor? Actor { get; set; }

    /// <summary>
    /// What is currently happening to it — the engine's own list. Everything carries
    /// one, because an item can be enchanted as readily as a character can be cursed.
    /// </summary>
    public Rpg.Effects Effects { get; } = new();

    /// <summary>
    /// How far the frame has faded this thing for being in the way of the camera (ours, presentation only;
    /// <see cref="World.SeeThrough"/>): 0 solid, 1 as clear as it goes. Written once a frame by the decision, read by the
    /// picture and by the pointer, which passes over a thing drawn faded. Always 0 with see-through off.
    /// </summary>
    public float SeeThroughLevel { get; internal set; }

    /// <summary>
    /// The model's alpha: 1 opaque. The MODELALPHA effect writes it
    /// (Cloak of Shadows, Mist Form …), and `SetAlpha` ramps it; a revive puts it back to 1.
    /// Setting it outright is the alpha set with no ramp, which also stops a ramp in progress.
    /// </summary>
    public float ModelAlpha
    {
        get => Fx?.Alpha ?? 1f;
        set { if (value != 1f || Fx is not null) Model3D.SetAlpha(value, 0f); }
    }

    /// <summary>
    /// The model's scale multiplier, from MODELSCALE and `SetScale` — one number when it
    /// is uniform. Setting it is the recompute's call: the scale set with a ramp of 0, which lands on the
    /// next model update.
    /// </summary>
    public float ModelScale
    {
        get => Fx?.Scale.X ?? 1f;
        set { if (value != 1f || Fx is not null) Model3D.SetScale(value, 0f); }
    }

    /// <summary>
    /// The render model's own animated state — alpha, scale, shell and the animation freeze
    /// (`ModelFx`) — made the first time anything asks for it, so the thousands of things that never
    /// fade carry nothing.
    /// </summary>
    public ModelFx? Fx { get; private set; }

    /// <summary><see cref="Fx"/>, made on demand.</summary>
    public ModelFx Model3D => Fx ??= new ModelFx();

    /// <summary>
    /// Another model's state in place of this one's — a shape change shelving the old model object
    /// and giving it back (<see cref="Rpg.ShapeShift"/>); null is a fresh model's.
    /// </summary>
    internal void ResetFx(ModelFx? fx = null) => Fx = fx;

    /// <summary>
    /// Which of its model's named shapes are hidden, on its own instance and each sub-model slot
    /// (<see cref="NamedShapes"/>). Null until its geometry goes in.
    /// </summary>
    public NamedShapes.State? Shapes { get; set; }

    /// <summary>
    /// The mind driving this thing, or null for anything that does not
    /// think. Set when <see cref="Ai.Minds"/> builds one.
    /// </summary>
    public Ai.Mind? Mind { get; set; }

    /// <summary>
    /// The walker a **script** gave this thing, for a character with no AI mind of its own.
    ///
    /// In the engine a walker is part of the thing, not of the AI: `SendActorToPos` does
    /// the guid lookup, checks the type and the health, finds the ground, and then calls the walk
    /// straight on the thing (the walk called on the thing itself) with **no mind anywhere in the path**.
    /// Ours grew the other way round — the walker hung off the mind — so a character the scene
    /// places without an AI file could not be walked at all. The convent's archbishop is one:
    /// `CNVT_3_3` sends him to his second keyframe and he stood where he was.
    ///
    /// <see cref="Location.WalkerFor"/> makes one on demand and the simulation advances it; a thing
    /// with a mind uses the mind's, and the steered character uses the world's.
    /// </summary>
    public Player? Walker { get; set; }

    /// <summary>
    /// **Made at run time, and so standing on no surface yet** (no cached surface). Entering the
    /// world copies the matrix, links the sector and finalises; nothing in it looks for
    /// the ground. The surface is found by the first step or by a move order, which
    /// looks before it walks. A script's walk does not — `SendActorToPos`
    /// calls the walker straight, and the walker with no surface walks **straight at the point**
    /// (the walk skips the search). That is the barrel rat's first dash in 1.1: one
    /// heading from the barrel to where its script sent it, across a gap in the floor. Read when the
    /// thing's walker is made (<see cref="Player.Settle"/>).
    /// </summary>
    public bool Unsurfaced { get; set; }

    /// <summary>
    /// Whichever walker moves this thing now, without making one: its mind's, the steered
    /// character's, or one a script made (<see cref="Walker"/>). What the engine's calls to the
    /// locomotion chooser reach, since there the walker is part of the thing.
    /// </summary>
    public Player? Locomotion =>
        Mind?.Walker ?? Walker ?? (Place?.Player is { } steered && ReferenceEquals(steered.Thing, this) ? steered : null);

    /// <summary>
    /// When the engine throws this thing away, on the millisecond clock; 0 means
    /// never. The thing tick destroys anything whose time has come, which is how a
    /// corpse, a gib, a particle and a spent effect all leave.
    /// </summary>
    public int ExpireAt { get; set; }
    /// <summary>True when scripts may treat it as a character.</summary>
    public bool IsActor => Actor is not null;

    /// <summary>
    /// The transform the scene placed it at, which is the one its geometry was built with. A thing
    /// that has moved is drawn by the difference between this and <see cref="World"/>.
    /// </summary>
    public Matrix4x4 Base => Transform(Scene.Position, Scene.Orientation);
    /// <summary>Where it is now.</summary>
    public Matrix4x4 World => Transform(Position, Orientation);
    /// <summary>
    /// Just the way it is turned — the engine's own thing matrix, which carries no scale: a thing's
    /// MODELSCALE is set on its render model instead, so a shrunken crate
    /// still collides at full size. This is the frame a <see cref="CollideShape.Box"/> is measured
    /// in.
    /// </summary>
    public Matrix4x4 Rotation
    {
        get
        {
            var o = Orientation * (MathF.PI / 180f);
            return Matrix4x4.CreateRotationY(o.Y) * Matrix4x4.CreateRotationX(o.X) * Matrix4x4.CreateRotationZ(o.Z);
        }
    }
    /// <summary>
    /// The Euler degrees that produce a rotation — the inverse of <see cref="Rotation"/>, and so in
    /// the same Y, then X, then Z order.
    ///
    /// **Ours, and only because our shape differs.** The engine keeps a 3 × 4 matrix on every thing
    /// and copies it about wholesale; Vitomir keeps a position and three angles, so anything that
    /// arrives as a matrix — a posed bone, say, which is what the gib spray launches from — has to
    /// be turned back into angles. The matrix that comes back out is the one that went in; what is
    /// lost is only which of several Euler triples was meant, and at the poles (a pitch of ±90°,
    /// where the yaw and the roll become the same turn) the roll is given to the yaw.
    ///
    /// Any scale is divided out first: a thing's MODELSCALE rides along in <see cref="World"/> and
    /// would otherwise be read as rotation.
    /// </summary>
    public static Vector3 OrientationOf(Matrix4x4 m)
    {
        var xa = Vector3.Normalize(new Vector3(m.M11, m.M12, m.M13));
        var ya = Vector3.Normalize(new Vector3(m.M21, m.M22, m.M23));
        var za = Vector3.Normalize(new Vector3(m.M31, m.M32, m.M33));
        if (!float.IsFinite(xa.X) || !float.IsFinite(ya.X) || !float.IsFinite(za.X)) return Vector3.Zero;

        const float r2d = 180f / MathF.PI;
        float sinPitch = Math.Clamp(ya.Z, -1f, 1f);
        float pitch = MathF.Asin(sinPitch);
        // cos(pitch) is what both of the other two angles are read through, so when it vanishes they
        // stop being distinguishable and the roll is folded into the yaw.
        if (MathF.Abs(sinPitch) > 0.999999f)
            return new Vector3(pitch, MathF.Atan2(za.X, xa.X), 0f) * r2d;
        return new Vector3(pitch, MathF.Atan2(-xa.Z, za.Z), MathF.Atan2(-ya.X, ya.Y)) * r2d;
    }

    /// <summary>
    /// True while it is somewhere other than where the scene put it. Worked out from where it is
    /// rather than remembered, because a thing moves for more reasons than its keyframes: the
    /// player walks, and a script may set a position outright.
    /// </summary>
    public bool Moved => Position != Scene.Position || Orientation != Scene.Orientation;

    /// <summary>What it is doing, if it is moving.</summary>
    public ThingMotion? Motion { get; private set; }

    Matrix4x4 Transform(Vector3 position, Vector3 orientationDegrees)
    {
        var o = orientationDegrees * (MathF.PI / 180f);
        return Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateRotationY(o.Y) * Matrix4x4.CreateRotationX(o.X)
               * Matrix4x4.CreateRotationZ(o.Z) * Matrix4x4.CreateTranslation(position);
    }

    /// <summary>
    /// CodexThing.MoveToFrame: travel to a keyframe's pose at <paramref name="speed"/> units a
    /// second. Returns false when there is no such keyframe, as the original's does — and also
    /// when the thing is not <see cref="MoveTrack"/>, which is the gate the worker opens with
    /// move kind 2 or it fails) and which the native reports, because it
    /// answers <c>setne al</c> on the worker's result.
    /// </summary>
    public bool MoveToFrame(int frame, float speed)
    {
        if (MoveType != MoveTrack) return false;
        if (frame < 0 || frame >= Keyframes.Length) return false;
        var key = Keyframes[frame];
        float distance = Vector3.Distance(Position, key.Position);
        // **Already there: nothing happens, and the answer is yes** (a zero length
        // returns 1 before anything is set). No move, no noise — and no `arrived` either, which a
        // script waiting on one does not get.
        if (distance == 0f) return true;
        // **A speed of 0 or less**: the engine keeps the rate, the speed over the distance,
        // and each frame adds the frame's share of it to how far along the thing is,
        // arriving at 1. So at 0 it never arrives — it stands, `moving` for ever
        // — and below 0 it runs backwards, away from the keyframe, for ever. Only a script's mover
        // given no speed can ask it (Vitomir arrived at once).
        bool wasMoving = Motion is not null;
        // The turn is measured from the tracker's own angles (the keyframe's orientation
        // less the tracker's), not from how the thing is placed or turned (<see cref="TrackerAngles"/>).
        Motion = speed > 0
            ? new ThingMotion(frame, Position, key.Position, TrackerAngles, key.Orientation, null, distance / speed)
            : new ThingMotion(frame, Position, key.Position, TrackerAngles, key.Orientation, null, 0f) { Rate = speed / distance };
        // The noises: a thing that was standing still starts its `moving` loop, if it
        // has none going already, and every move — even one that interrupts another — says
        // `startmove`.
        if (!wasMoving && MovingSound == 0) MovingSound = Audio.Noise.Foley(this, Audio.FoleyEvent.Moving);
        Audio.Noise.Foley(this, Audio.FoleyEvent.StartMove);
        return true;
    }

    /// <summary>
    /// The channel a track move's <c>moving</c> loop is on, stopped when the move
    /// arrives. A move that is stopped short (<see cref="StopMoving"/>) leaves it running, as the
    /// engine does: nothing but an arrival stops it.
    /// </summary>
    public int MovingSound { get; set; }

    /// <summary>Whether it was walking last frame — the engine's "moving" bit, as the footfall noises see it.</summary>
    public bool Walking { get; set; }

    /// <summary>
    /// CodexThing.RotatePivot: swing about a keyframe. The keyframe's position is the hinge and its
    /// orientation is how far to turn — a <em>delta</em>, which is why RotateAlways can call this
    /// over and over and keep turning, and why PivotDoor closes itself by passing a negative
    /// duration to turn the same amount back.
    /// </summary>
    public bool RotatePivot(int frame, float duration)
    {
        if (frame < 0 || frame >= Keyframes.Length) return false;
        // **Two ways to answer true having done nothing**, both the original's and both easy to
        // mistake for a bug. A duration of exactly zero returns before the worker is even called,
        // so a script that asks for an instant swing gets no swing and no `arrived`;
        // and the native answers `mov al, 1` unconditionally after the call, so a thing
        // that is not <see cref="MoveTrack"/> is refused by the worker while the script
        // is told it worked. MoveToFrame, sitting right beside it, reports that same refusal.
        if (duration == 0f || MoveType != MoveTrack) return true;
        var key = Keyframes[frame];
        var turn = duration < 0 ? -key.Orientation : key.Orientation;
        var pivot = key.Position;
        var to = Rotate(Position - pivot, turn) + pivot;
        Motion = new ThingMotion(frame, Position, to, Orientation, Orientation + turn, pivot, MathF.Abs(duration));
        // A swing says `startmove` and has no loop.
        Audio.Noise.Foley(this, Audio.FoleyEvent.StartMove);
        return true;
    }

    /// <summary>
    /// Moves it on by one frame of game time. Returns the keyframe it has just reached, or −1.
    /// <paramref name="moved"/> is told once the new pose is set, every frame (where the engine
    /// re-finds the sector, <see cref="TrackMoves"/>), and <paramref name="arrived"/> on the frame the
    /// keyframe is reached — the owning scripts' <c>arrived</c>.
    ///
    /// **The scripts hear the arrival before the noise stops** (for a straight
    /// move and for a swing): the frame is recorded, the moving bits cleared, `arrived` called,
    /// and only then — and only **if nothing has set it moving again** — does the
    /// `moving` loop stop and the thing say `stopmove`. So a mover whose `arrived` sends it straight
    /// on to its next keyframe (the conveyers, the particle movers, the sewer's electric plate) keeps
    /// one loop going the whole way and says only the next leg's `startmove`. Earlier, Vitomir
    /// stopped the loop and said `stopmove` first, and started a new loop for the next leg.
    /// </summary>
    public int Advance(float seconds, Action? moved = null, Action<int>? arrived = null)
    {
        if (Motion is not { } m) return -1;
        m.Elapsed += seconds;
        float t = m.Rate is { } rate ? MathF.Min(m.Elapsed * rate, 1f)             // No floor
                : m.Duration > 0 ? Math.Clamp(m.Elapsed / m.Duration, 0f, 1f) : 1f;
        Orientation = Vector3.Lerp(m.FromOrientation, m.ToOrientation, t);
        Position = m.Pivot is { } pivot
            ? Rotate(m.FromPosition - pivot, (m.ToOrientation - m.FromOrientation) * t) + pivot
            : Vector3.Lerp(m.FromPosition, m.ToPosition, t);
        // The tracker's angles follow: a move's are the angles it has turned to; a swing's
        // are the turn so far alone, not added to anything — 1.1's own, kept.
        TrackerAngles = m.Pivot is null ? Orientation : (m.ToOrientation - m.FromOrientation) * t;
        if (t < 1f) { moved?.Invoke(); return -1; }
        Position = m.Pivot is null ? m.ToPosition : Position;
        Orientation = m.ToOrientation;
        TrackerAngles = m.Pivot is null ? m.ToOrientation : m.ToOrientation - m.FromOrientation;
        Motion = null;
        moved?.Invoke();
        arrived?.Invoke(m.Frame);
        // Arrived: unless `arrived` set it moving again, the loop stops and the
        // thing says `stopmove`.
        if (Motion is not null) return m.Frame;
        if (MovingSound != 0) { Place?.Audio?.Mixer.Stop(MovingSound); MovingSound = 0; }
        Audio.Noise.Foley(this, Audio.FoleyEvent.StopMove);
        return m.Frame;
    }

    /// <summary>
    /// **The tracker's angles**, which a track move turns from and
    /// which the tracker's step builds the thing's frame from while it moves. Every
    /// constructor seeds them from **keyframe 0's orientation** when the thing is
    /// <see cref="MoveTrack"/> and has keyframes — the scene's load, the registration,
    /// the spawns and the money bag
    /// — and nothing else writes them but the tracker itself: not the placed
    /// orientation, not a script's `SetOrientation` — and a load puts back what a save kept (1.1 writes
    /// them with the rest of the tracker's 0x90 bytes, and reads them back;
    /// `Save.TrackSave`). So a tracker placed or turned away from keyframe
    /// 0's orientation snaps to it as its first move begins (558 of the 559 shipped things with
    /// keyframes are placed at keyframe 0's; a stone in `STE1_21_1` is 35 degrees off). Until then
    /// Vitomir turned from the placed orientation (gap 6 of a comparison with 1.1). Seeded lazily here
    /// (ours): unset, it is keyframe 0's, which is what every 1.1 constructor writes; Vitomir's saves
    /// keep them when set, as 1.1's do.
    /// </summary>
    public Vector3 TrackerAngles
    {
        get => trackerAngles ?? (MoveType == MoveTrack && Keyframes.Length > 0 ? Keyframes[0].Orientation : Vector3.Zero);
        set => trackerAngles = value;
    }

    Vector3? trackerAngles;

    /// <summary>The tracker's angles when something has set them — null while they are still keyframe 0's (a save's question).</summary>
    public Vector3? TrackerAnglesSet => trackerAngles;

    /// <summary>
    /// A move a save kept, put back under way (1.1 reads the tracker's 0x90 bytes back, and
    /// the move goes on from where it was). No noise: the `moving` loop is not restarted, as nothing
    /// in the record keeps a channel.
    /// </summary>
    public void ResumeMotion(ThingMotion motion) => Motion = motion;

    /// <summary>
    /// CodexThing.Stop for a <see cref="MoveTrack"/> thing: it stops where it is, and `arrived`
    /// never comes. The engine does not cancel the motion so much as switch it off — the native
    /// clears bit 0 of the physics word, the bit the track move sets when it starts — and
    /// the half-open door stays half open with its destination frame still recorded. Dropping
    /// <see cref="Motion"/> is the same thing seen from outside: no more movement, no keyframe
    /// reached, and <see cref="Advance"/> answers −1 from here on.
    /// </summary>
    public void StopMoving() => Motion = null;

    /// <summary>Rotates a vector by euler degrees, in the order the scene's own transform uses.</summary>
    static Vector3 Rotate(Vector3 v, Vector3 degrees)
    {
        var o = degrees * (MathF.PI / 180f);
        return Vector3.Transform(v, Matrix4x4.CreateRotationY(o.Y) * Matrix4x4.CreateRotationX(o.X)
                                    * Matrix4x4.CreateRotationZ(o.Z));
    }

    /// <summary>Which keyframe it is at, fractional while it moves.</summary>
    public float Frame { get; set; }

    /// <summary>
    /// A per-type state word, whose meaning depends on what the thing is. For a
    /// **container** bit 0 is *already opened* (`Rpg.Container.Opened`); for a **character** the
    /// same word is `ACTOR_FLAGS` and lives on <see cref="Actor.Flags"/> instead. Only the
    /// container reading is used here. It is the first of the type-specific words each save record
    /// writes at this offset: a region's own, its only one (its own writer and reader); the
    /// first of a prop's two; the first of a container's three
    /// (their own writer and reader) — the rest of those words are not split out beyond
    /// <see cref="ContainerHealth"/>.
    /// </summary>
    public uint State { get; set; }

    /// <summary>
    /// The scripts that have taken control of it (`Codex.CaptureThing`), each with the id it
    /// passed — which it gets back as the third argument of every callback this thing fires.
    ///
    /// **A list, because the engine's is one**: more
    /// than one script may capture the same thing, and each of them hears its events separately.
    /// This is how the game's doors are built — `DoorSwitchPivot` sits on the *door* and captures
    /// the *switch*, so clicking a lever with no script of its own reaches the door's script —
    /// and it is not a corner: **177 of the 465 shipped classes call `CaptureThing`**, more than
    /// implement `clicked` themselves. Every capture record is also on its *capturing script's* own
    /// list, so the engine can walk either side; when a thing goes it drops every capture here,
    /// and when a script goes it drops every capture it holds, wherever it holds it
    /// — both by unlinking each record from both lists at once. Vitomir keeps
    /// only this side as a plain list: a destroyed thing's list simply goes with it, and a destroyed
    /// script's holds are dropped by scanning every thing's list instead of walking a list of its own.
    /// </summary>
    public List<Capture> Captures { get; } = [];

    /// <summary>
    /// A script's `CaptureThing` on this thing: a script that already holds it only has
    /// its capture id changed — there is never a second hold — and a new hold goes on the **front**
    /// of the list, so of several scripts holding the same thing the newest hears its
    /// `clicked`, `killed`, `entered` and `arrived` first.
    /// </summary>
    public void TakeHold(int script, int id)
    {
        int held = Captures.FindIndex(c => c.Script == script);
        if (held >= 0) Captures[held] = new Capture(script, id);
        else Captures.Insert(0, new Capture(script, id));
    }

    /// <summary>One script's hold on a thing. See <see cref="Captures"/>.</summary>
    /// <param name="Script">The capturing script's own GUID.</param>
    /// <param name="Id">What that script called this capture, and gets back in the callback.</param>
    public readonly record struct Capture(int Script, int Id);

    public bool Has(uint flag) => (Flags & flag) != 0;

    /// <summary>THING_FLAG_* values the engine and the scripts share (Codex.java).</summary>
    public static class Flag
    {
        public const uint SaveNever = 0x1, SaveSuspend = 0x2, SaveSetModified = 0x4, SaveAlways = 0x8,
                          NoReflection = 0x10, Emitter = 0x20, NoHighlight = 0x40, DynaLight = 0x80,
                          NoNetSync = 0x100, DisableCollide = 0x200, ProximityTrigger = 0x400,
                          StaticShadows = 0x800, NoRegionTrigger = 0x1000, VisBlock = 0x2000,
                          CameraBlock = 0x4000, NoScatter = 0x8000, Secret = 0x10000, TopSecret = 0x20000,
                          Silent = 0x40000, BlockSelect = 0x80000, Teleport = 0x100000, NoStDelete = 0x200000,
                          Animating = 0x800000, Remove = 0x1000000, Targetted = 0x2000000,
                          SoundLink = 0x4000000, NoBlock = 0x8000000, Finalized = 0x10000000,
                          Modified = 0x20000000, FramesModified = 0x40000000, EverModified = 0x80000000;
    }
}

/// <summary>A thing on its way somewhere: where it started, where it is going, and how long it takes.</summary>
/// <param name="Pivot">The hinge to swing about, or null for a straight move.</param>
public sealed record ThingMotion(int Frame, Vector3 FromPosition, Vector3 ToPosition,
                                 Vector3 FromOrientation, Vector3 ToOrientation, Vector3? Pivot, float Duration)
{
    public float Elapsed { get; set; }
    /// <summary>
    /// A straight move's share of the way a second when it was asked at a speed of 0 or less — the
    /// speed over the distance, 0 or negative — in place of <see cref="Duration"/>; null
    /// otherwise. The move then never arrives (<see cref="WorldThing.MoveToFrame"/>).
    /// </summary>
    public float? Rate { get; init; }
}
