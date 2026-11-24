// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Making a thing while the game is running, and unmaking one. Everything in a level up to now has
// come out of a scene file at load; this is the other way in, and it is what the summons, the gibs,
// the skeleton a vampire collapses into, a thrown knife, a dropped sword and the beckoning pets all
// need. 100 shipped scripts call it.
//
// The engine's path is four functions and this file follows them:
//
// * **`SpawnThing`** — the whole of it: pick whose position to use, make the thing,
//   give it a GUID, put it in the world, and record who made it.
// * **the thing's making** — construct from a template record and copy the template's name onto the thing.
// * **the GUID's taking** — allocate the GUID from the global counter and register it in the hashtable.
// * **the world's enter** — put it in the world at a transform: copy the matrix, find the sector the point
//   is in, and link the thing into that sector's list.
//
// Where a thing goes is the part worth knowing: **a spawned thing appears exactly where the thing
// that spawned it is**, with its transform copied wholesale. There is no offset, no drop to the
// ground and no search for room. `SpawnThingNear` is the one that searches, and it is a different
// native with a different placement call.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

public sealed partial class Location
{
    /// <summary>
    /// `CodexThing.SpawnThing`: makes the named template at
    /// <paramref name="parent"/>'s own position and sector, and returns it — or null when the
    /// template is not in the table, or the spawner is not itself in the world.
    ///
    /// Three details are the engine's and none of them is obvious:
    ///
    /// <list type="bullet">
    /// <item>**An item, weapon or armour spawns from its owner instead.** If the spawner is a
    /// <see cref="ThingType.Item"/>, <see cref="ThingType.Weapon"/> or <see cref="ThingType.Armor"/>
    /// and something is carrying it (`pItem->pOwner`), the *owner* is used as the
    /// spawner. A carried sword's own position is wherever it was picked up from, so without this a
    /// weapon that spawns something would put it somewhere in the last room.</item>
    /// <item>**A projectile or an explosion remembers who made it** — the spawner's GUID, at
    /// the owner field of a projectile or of an explosion. Nothing else is given it, so kill
    /// attribution exists for exactly those two types.</item>
    /// <item>**The spawner must be in the world**: it needs both a world and a sector, so a thing
    /// that has been removed, or is in a bag, spawns nothing.</item>
    /// </list>
    /// </summary>
    public WorldThing? Spawn(string template, WorldThing parent)
    {
        if (Templates is null || template.Length == 0) return null;

        // An item, weapon or armour defers to whoever is carrying it.
        if (parent.Type is ThingType.Item or ThingType.Weapon or ThingType.Armor
            && parent.Owner is { } owner) parent = owner;
        if (parent.Removed || parent.Sector < 0) return null;

        var made = Place(template, parent.Position, parent.Orientation, parent.Sector);
        if (made is null) return null;

        // The two types that are given a maker.
        if (made.Type is ThingType.Projectile or ThingType.Explosion) made.SpawnedBy = parent.Guid;
        return made;
    }

    /// <summary>
    /// `CodexThing.SpawnThingNear`: makes the template **beside**
    /// <paramref name="anchor"/> rather than on top of it, and answers it — or null when the template
    /// is not in the table or the anchor is not in the world.
    ///
    /// The thing is made first, so its own size is known, and then a place is looked for:
    /// <list type="bullet">
    /// <item>an anchor flagged <see cref="WorldThing.Flag.NoScatter"/> puts it exactly where the
    /// anchor stands;</item>
    /// <item>otherwise a <paramref name="distance"/> of 0 or less means "just clear of each other" —
    /// the two radii, each counted only when the thing has a collide shape, times 1.1;</item>
    /// <item>eight directions are tried in order: the anchor's forward, then turned by +45, −45,
    /// +90, −90, +135, −135 and 180 degrees. Each is a point that far out and 60 up, and the floor
    /// under it is looked for **from what the anchor stands on** — its own surface, that surface's
    /// sector, and the sector the point is in (<see cref="FloorUnder"/>); no floor, next
    /// direction. A thing made at run time stands on no surface, and round one of those in the
    /// monastery no direction finds a floor at all;</item>
    /// <item>unless <paramref name="flags"/> has 8, a point where the new thing would touch any
    /// **thing** but the anchor is refused too — walls are not tested, and neither are
    /// the dead (the query's bit 4);</item>
    /// <item>the first that passes is used, the new thing facing out along it; if none does, it goes
    /// where the anchor is, facing its way.</item>
    /// </list>
    /// It enters the world making its <c>drop</c> noise (foley 0x12). The native always
    /// passes flags 1 and 2 on top of the script's.
    /// </summary>
    public WorldThing? SpawnNear(string template, WorldThing anchor, float distance, int flags)
    {
        if (Templates is null || !Templates.Has(template) || anchor.Removed || anchor.Sector < 0) return null;
        if (Make(template) is not { } made) return null;
        PlaceNear(made, anchor, distance, flags);
        return made;
    }

    /// <summary>
    /// The placing half of <see cref="SpawnNear"/>, on a thing that is
    /// already made — which is how the engine uses it for loot: the loot drop hands it the item a death
    /// or an opening has just made, with distance 0 and flags 0xb (enter the world, no room test).
    /// </summary>
    public void PlaceNear(WorldThing made, WorldThing anchor, float distance, int flags)
    {
        var at = anchor.Position;
        var facing = anchor.Orientation;
        if ((anchor.Flags & WorldThing.Flag.NoScatter) == 0)
        {
            float d = distance;
            if (d <= 0f)
                d = ((made.Collide != CollideShape.None ? made.Radius : 0f)
                     + (anchor.Collide != CollideShape.None ? anchor.Radius : 0f)) * NearSlack;
            float yaw = anchor.Orientation.Z * (MathF.PI / 180f);
            var forward = new Vector2(-MathF.Sin(yaw), MathF.Cos(yaw));
            foreach (float turn in NearTurns)
            {
                float r = turn * (MathF.PI / 180f), c = MathF.Cos(r), s = MathF.Sin(r);
                var dir = Vector2.Normalize(new Vector2(forward.X * c - forward.Y * s, forward.X * s + forward.Y * c));
                var probe = new Vector3(anchor.Position.X + dir.X * d, anchor.Position.Y + dir.Y * d,
                                        anchor.Position.Z + NearLift);
                // The floor probe from the anchor's own surface and sector (FloorProbe.cs); no floor, next direction.
                if (FloorUnder(probe, anchor) is not { } floor) continue;
                if ((flags & NearNoRoomTest) == 0 && Blocked(made, floor, anchor)) continue;
                at = floor;
                facing = new Vector3(0f, 0f, Player.Wrap(MathF.Atan2(-dir.X, dir.Y) * (180f / MathF.PI)));
                break;
            }
        }
        made.Position = at;
        made.Orientation = facing;
        made.Sector = At(at, anchor.Sector);
        made.Unsurfaced = true;                        // nothing here looks for the ground
        byGuid.Remove(made.Guid);                      // Enter registers it again, with the world
        Enter(made);
        Vitomir.Engine.Audio.Noise.Foley(made, Vitomir.Engine.Audio.FoleyEvent.Drop);
        Minds?.Adopt(made);
    }

    /// <summary>The eight directions <see cref="SpawnNear"/> tries, in the engine's order (its switch).</summary>
    static readonly float[] NearTurns = [0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f];

    /// <summary>How far above the ground the probe starts and how far down it looks.</summary>
    const float NearLift = 60f;

    /// <summary>The margin on "just clear".</summary>
    const float NearSlack = 1.1f;

    /// <summary>`SpawnThingNear`'s flag that skips the room test.</summary>
    public const int NearNoRoomTest = 8;

    /// <summary>
    /// Whether the room test refuses the spot: a new thing standing at
    /// <paramref name="feet"/>, its own shape — a sphere or a cylinder; any other shape meets nothing —
    /// against the things there, sphere, cylinder or model (<see cref="Bodies.Hit"/>).
    /// **Only the nearest hit is looked at** (the query's hits
    /// sorted nearest first by <see cref="Bodies.Hit"/>'s distance): the spot is taken when there is
    /// none or it is the anchor, and refused otherwise — so something else touching the new thing
    /// further off than the anchor does not refuse it (earlier, Vitomir refused a spot anything but the
    /// anchor touched). **Things only**: no wall of the level is looked at, and until then Vitomir's
    /// refused a spot a wall came within the body's radius, which the engine never does. That the point
    /// has a floor is the probe's part (<see cref="FloorUnder"/>).
    /// </summary>
    bool Blocked(WorldThing made, Vector3 feet, WorldThing anchor)
    {
        if (made.Collide is not (CollideShape.Sphere or CollideShape.Cylinder)) return false;
        WorldThing? nearest = null;
        float best = float.MaxValue;
        foreach (var other in things)
        {
            if (ReferenceEquals(other, made) || other.Collide == CollideShape.None || other.Removed) continue;
            if ((other.Flags & WorldThing.Flag.DisableCollide) != 0) continue;
            // The query's bit 4 is set, so the filter passes over a dead
            // character or party member — a thing can be put down overlapping a corpse (the
            // A comparison with 1.1; Vitomir refused the spot until then).
            if (other.Type is ThingType.Actor or ThingType.Player && other.Actor is { } body && body.Has(Actor.Flag.Dead)) continue;
            if (!Bodies.Hit(made.Collide, feet, made.Radius, made.Height, other, out float distance) || distance >= best) continue;
            best = distance;
            nearest = other;
        }
        return nearest is not null && !ReferenceEquals(nearest, anchor);
    }

    /// <summary>
    /// The rest of the spawn, with the transform decided: construct from the template, take a GUID,
    /// and enter the world. Separate from <see cref="Spawn"/> because everything else that makes a
    /// thing at run time — a dropped item, a corpse's gibs, a `SpawnThingNear` — differs only in
    /// how it chose the place.
    /// </summary>
    /// <param name="guid">
    /// The GUID a party member arriving by a journey already has (<see cref="Claim"/>), or 0 for a
    /// new thing, which takes the counter's (<see cref="TakeGuidFor"/>).
    /// </param>
    public WorldThing? Place(string template, Vector3 position, Vector3 orientation, long sector, int guid = 0)
    {
        if (Templates is null || !Templates.Has(template)) return null;
        var sheet = guid == 0 ? CharacterSheets.HoldFor(this, template) : null;   // a player-type thing's kit first
        // The placement does not take the sector on trust — it finds the one the point
        // is actually in, which is what keeps a thing spawned beside a portal on the right side
        // of it. The caller's sector is only the hint the search starts from.
        var thing = Build(NsdThing.Placed(template, position, orientation, At(position, sector)),
                          Templates, index: -1, guid: Claim(guid) ? guid : TakeGuidFor(template), now: Milliseconds);
        thing.SaveId = Save.ThingIds.Made(this, ++Made);
        thing.Unsurfaced = true;                       // nor here: see WorldThing.Unsurfaced
        Enter(thing);
        // The thing's first entry into a world finalises it, and the first thing that
        // does is say `create` — which only the explosions' foley files fill.
        Vitomir.Engine.Audio.Noise.Foley(thing, Vitomir.Engine.Audio.FoleyEvent.Create);
        CharacterSheets.Made(this, thing, sheet);                                  // its sheet (CharacterSheets.cs)
        // A character made after the place woke up needs a mind like everybody else's.
        Minds?.Adopt(thing);
        return thing;
    }

    /// <summary>
    /// A thing that is **constructed and registered but never enters the world** — the engine's
    /// Building from the template and taking a GUID and registering it, without
    /// linking it into a sector. That split is the engine's own, and this is the half an
    /// item created straight into somebody's pack needs: it exists, it has a GUID a save can write
    /// and a script can resolve, and it is nowhere.
    /// </summary>
    /// <param name="guid">The GUID a carried item arriving by a journey already has, or 0 for a new one.</param>
    public WorldThing? Make(string template, int guid = 0)
    {
        if (Templates is null || template.Length == 0 || !Templates.Has(template)) return null;
        var thing = Build(NsdThing.Placed(template, Vector3.Zero, Vector3.Zero, 0),
                          Templates, index: -1, guid: Claim(guid) ? guid : TakeGuidFor(template), now: Milliseconds);
        thing.SaveId = Save.ThingIds.Made(this, ++Made);
        byGuid[thing.Guid] = thing;
        return thing;
    }

    /// <summary>
    /// How many things have been made here — the number in a made thing's save id
    /// (<see cref="Save.ThingIds.Made"/>). It only counts up, and a save keeps it, so a thing made
    /// after a load never takes the name of one made before it.
    /// </summary>
    public int Made { get; set; }

    /// <summary>
    /// A thing brought back from a save: built from its template like <see cref="Make"/>, registered
    /// under **the GUID it had** — a script's saved state may hold that number — and not yet in the
    /// world. A GUID already taken here gets a new one, and the caller is told by the answer's.
    /// </summary>
    public WorldThing? Restore(string template, int guid, string? saveId)
    {
        if (Templates is null || template.Length == 0 || !Templates.Has(template)) return null;
        if (guid <= 0 || byGuid.ContainsKey(guid)) guid = Guids.Fresh(byGuid.ContainsKey);   // a real number, even mid-load (ThingGuids.Loading)
        var thing = Build(NsdThing.Placed(template, Vector3.Zero, Vector3.Zero, 0),
                          Templates, index: -1, guid: guid, now: Milliseconds);
        thing.SaveId = saveId;
        byGuid[thing.Guid] = thing;
        Guids.Past(guid);
        return thing;
    }

    /// <summary>Puts the GUID counter where a save left it; it never goes backwards past a GUID in use.</summary>
    public void ResumeGuids(int next)
    {
        if (next > NextGuid) NextGuid = next;
    }

    /// <summary>
    /// The sector a point is in, starting the search from a hint. Falls back to
    /// the hint when the level has no collision loaded, which is what a geometry-less test sees.
    /// </summary>
    public long At(Vector3 position, long hint) =>
        Solid is { } solid && solid.SectorAt(position, (int)hint) is var found && found >= 0
            ? found : hint;

    /// <summary>
    /// `simThing::SetPosition` — moving a thing that is already in the
    /// world, as opposed to putting one there. **It is not just a write to the position**: the
    /// engine re-finds the sector the new point falls in and, when it has changed, moves the thing
    /// between the sectors' thing lists.
    ///
    /// That is the whole of a bug this tree has carried since the script layer went in.
    /// `CodexThing.SetPosition` wrote the position and nothing else, so a teleported thing kept the
    /// sector it started in — it went on blocking people where it used to be and blocked nobody
    /// where it now was. Everything that *walks* was always fine, because the walker calls
    /// <see cref="Bodies.Moved"/> itself; it was only the script teleport that was wrong.
    /// </summary>
    public void Move(WorldThing thing, Vector3 position)
    {
        long from = thing.Sector, to = At(position, from);
        thing.Position = position;
        if (to != from)
        {
            thing.Sector = to;
            Props?.Moved(thing, from, to);
        }
        // And the regions, which SetPosition tests every time.
        Regions?.Moved(thing, from);
    }

    /// <summary>
    /// Puts a thing in a sector outright, without looking — what a load does to leave a thing in the
    /// sector the save says, which is not always the one its point is in (a region the scene
    /// placed in no sector at all is in sector −1).
    /// </summary>
    internal void Resector(WorldThing thing, long sector)
    {
        long from = thing.Sector;
        if (from == sector) return;
        thing.Sector = sector;
        if (thing.InWorld) Props?.Moved(thing, from, sector);
    }

    /// <summary>
    /// A thing comes into the world: its box is measured, it joins the GUID table and the thing
    /// list, and it is linked into its sector. A thing that is already in
    /// is left alone.
    ///
    /// The engine calls the second of those two the **finalize**, guards it with a flag of its own
    /// (flag `0x10000000`) so it runs exactly once, and does six things in it. Four are
    /// here: the **spawn transform** (<see cref="WorldThing.Finish"/> — the live matrix copied to
    /// the start transform, whose translation is the home every AI measures from), the born-with effects
    /// (<see cref="Born"/>), the box, and <see cref="LaunchVelocity"/>. The other two are not: it
    /// hands the render model the thing's GUID, and it asks the motion set for mode **127,
    /// `create`** on the action channel at 30 fps — which answers a question
    /// `Anim.ThingAnimation` had left open about which mode a newly made thing starts on. Ten
    /// shipped motion sets contain nothing but `create`, and this is why: it is the mode the engine
    /// fires at birth.
    /// </summary>
    public void Enter(WorldThing thing)
    {
        // **By the world pointer, not by the GUID table.** A thing that has been picked up is out
        // of the world and still in the table — the engine keeps its GUID registration, which is
        // how the unavailable pass can look a carried item up — so asking the table would refuse to put a
        // dropped sword back. Its world is the field the engine itself tests.
        if (thing.InWorld) return;
        if (Files is not null) LoadBox(thing, Files, []);
        thing.Removed = false;
        thing.Place = this;                            // pWorld
        things.Add(thing);
        byGuid[thing.Guid] = thing;
        Props?.Enter(thing);
        // The sector link is what makes a thing drawable, in both engines. Ours has to
        // build the geometry to link; the original only stores a pointer.
        Show(thing);
        // THING_TYPE_PLAYER is what the AI's paired main flags mean by "party".
        if (thing.Actor is { } who) { who.IsPlayerThing = thing.Type == ThingType.Player; if (Files is { } f) who.ClanRecord = Rpg.Clans.For(f)[who.Clan]; }
        if (!thing.Finish()) return;                    // and the spawn transform with it
        LaunchVelocity(thing);
        PlayCreate(thing);
        Born(thing);
        Entered(thing);                                 // an explosion begins (Explosions/Location.Explosions.cs)
        // The finalize's last act: a thing with a script is told it exists.
        if (thing.Script.Length > 0) Created?.Invoke(thing);
    }

    /// <summary>
    /// **`PHYSICS_VELOCITY` is in the thing's own frame**, and the finalize rotates it into the
    /// world exactly once, as the thing enters (`if (moveType == 3)
    /// RotateVector`, the rotate being the matrix's 3×3 with no
    /// translation, written back in place).
    ///
    /// Without this the tag is meaningless, and that is not a hypothetical: it is the shape of the
    /// shipped data. Every projectile template carries a `PHYSICS_VELOCITY`, and every *fired* one
    /// is `0, s, 0` — nothing but a speed along its own **+Y**, which is forward — so an arrow flies
    /// where it was aimed only because of this one line. The other 101 of the 156 are gibs and
    /// severed heads with a deliberate X or Z (`gib_2_0` is `-80,0,250`), which is what makes the
    /// rotate load-bearing rather than incidental. An early build read the tag and wrote it straight to the
    /// velocity, which sent every gib chunk along a world axis instead of away from the bone it came
    /// off; this is the fix.
    ///
    /// Only the linear velocity is rotated. `PHYSICS_ROTVEL` is a spin about the thing's own axes
    /// and the engine leaves it alone.
    /// </summary>
    public static void LaunchVelocity(WorldThing thing)
    {
        if (thing.MoveType != WorldThing.MovePhysics || thing.Velocity == Vector3.Zero) return;
        thing.Velocity = Vector3.TransformNormal(thing.Velocity, thing.Rotation);
    }

    /// <summary>
    /// The effects a character is **born with**, applied as the last step of coming into the world
    /// (the tail of the placement). They are not in any template and nothing in the data
    /// names them — the engine simply puts them on by name, at level 0, by `ACTOR_TYPE`:
    ///
    /// <list type="bullet">
    /// <item>a **vampire** (type 0) gets `ef_decreasefrenzy` — frenzy falls by **2 every 2
    /// seconds**, which is why the frenzy bar drains on its own between fights;</item>
    /// <item>a **human** (type 2) gets `ef_increasemanaot` and `ef_increasebloodot` — mana **+1
    /// every 4 seconds** and blood **+1 every 6 seconds**. A mortal recovers on its own; a vampire
    /// does not, and has to feed.</item>
    /// </list>
    ///
    /// The engine tests the human case twice in a row for the second effect, which changes nothing
    /// and is copied here as one test. Nothing happens at all until the location has been given its
    /// effect definitions, which is <see cref="Awaken"/>.
    /// </summary>
    public void Born(WorldThing thing)
    {
        if (Effects?.File is not { } file || thing.Actor is not { } actor) return;
        foreach (var name in actor.Type switch
        {
            ActorType.Vampire => Rules.GameRules.Current.Healing.BornEffectsVampire,
            ActorType.Human => Rules.GameRules.Current.Healing.BornEffectsHuman,
            _ => [],
        })
            if (file[name] is { } definition)
                Rpg.EffectRuntime.Apply(thing, definition, 0, Milliseconds);
    }

    /// <summary>
    /// The finalize's `create` (mode 127, 0, 30.0): mode **127** played on the
    /// **action** channel at 30 frames a second, for every thing made — the level's own as they are
    /// placed and anything made while the game runs. Only ten shipped sets have the mode (a flag,
    /// banners, a buoy, signs, a curtain, the spider sac, a barrel, a muzzle flash), so for
    /// everything else the motion set refuses it and nothing changes. A thing whose render flags
    /// carry **`RECREATE`** (0x100) is also sent it from the re-create — which the finalize runs and a
    /// load runs again for every thing it brings back — so such a thing starts
    /// its `create` over when a game is loaded (<see cref="Recreate"/>).
    /// </summary>
    public void PlayCreate(WorldThing thing)
    {
        if (Motions is { } motions) thing.Animation?.Play(CreateMode, motions, onMotion: false);
    }

    /// <summary>A loaded thing's re-create: `create` again when its render flags say `RECREATE`.</summary>
    public void Recreate(WorldThing thing)
    {
        if ((thing.RenderFlags & RecreateFlag) != 0) PlayCreate(thing);
    }

    /// <summary>Mode 127, `create`, in the engine's mode table (0x7f).</summary>
    public const int CreateMode = 127;

    /// <summary>`THING_RENDERFLAG_RECREATE` (render flag <c>0x100</c>).</summary>
    public const uint RecreateFlag = 0x100;

    /// <inheritdoc cref="Born"/>
    public static readonly string[] VampireBorn = ["ef_decreasefrenzy"];
    /// <inheritdoc cref="Born"/>
    public static readonly string[] HumanBorn = ["ef_increasemanaot", "ef_increasebloodot"];

    /// <summary>
    /// `CodexThing.Remove`: the thing leaves the world.
    ///
    /// Three things about the engine's own destroy are worth having. It is **guarded by a flag**
    /// (bit `0x1000000`, set before anything else), so asking twice does nothing the
    /// second time rather than doing it twice. It **does not refuse the player** — there is no type
    /// test anywhere in it, so a script that removes the player removes the player. And it
    /// **spawns the thing's `DESTROY_TEMPLATE`** on the way out, at the dying thing's own position —
    /// which is how a barrel becomes its debris and a volatile one becomes an explosion; a
    /// <see cref="ThingType.Container"/>'s is given a ten-second expiry, everything else's lasts.
    ///
    /// Nothing here is freed, so anything still holding the thing can see
    /// <see cref="WorldThing.Removed"/> and stop — which is what a mind does.
    /// </summary>
    /// <param name="drop">
    /// False suppresses the `DESTROY_TEMPLATE`. The engine's own second argument, and `Remove`
    /// passes 0 — so a script's remove **does** leave its debris. A `DROPITEM` is never dropped
    /// here: it is the death's and the opening's (`Rpg.Loot`).
    /// </param>
    public bool Remove(WorldThing thing, bool drop = true)
    {
        if (thing.Removed) return false;               // The bit is the whole guard
        thing.Removed = true;
        // Whatever it rides lets it go, and whatever rides it is let go — destroyed, if it
        // was attached to go with it.
        if (thing.AttachedTo is { } parent) Attachments.Detach(parent, thing);
        Attachments.DetachAll(thing);
        // A character's effects come off with it, all but worn gear's — while it can
        // still be found by its GUID, so an effect's script ending with it can still ask about it.
        if (thing.IsActor) Rpg.EffectRuntime.RemoveAll(thing);
        // The actor's destructor lets its pets go before it frees their list.
        Minds?.Destroyed(thing);
        // The conversation with it ends and the steering passes on, while it
        // still holds its party slot (`Location.Destroyed.cs`).
        Destroying(thing);
        Doomed?.Invoke(thing);                         // its scripts, at its deletion (LocationDoomed.cs)
        things.Remove(thing);
        byGuid.Remove(thing.Guid);
        Props?.Leave(thing);
        // A thing with sounds on it lets them go — they stay where it was.
        if ((thing.Flags & WorldThing.Flag.SoundLink) != 0) Audio?.Mixer.Forget(thing);
        // Whoever was aiming at it stops — the unavailable pass's loop, inlined in the
        // destroy (until then only a pick-up did it, so a second attacker went on swinging at
        // a barrel another had broken).
        Untarget(thing);

        // What it turns into — the DESTROY_TEMPLATE, for any thing that has one —
        // and when the thing going is a container and what it turns into has a model, that is
        // short-lived debris (ten seconds, and a 9,000 ms fade on the model that is not
        // drawn yet). Until then this spawned the DROPITEM instead, which is a different field
        // for a different moment.
        if (drop && thing.DestroyTemplate.Length > 0
            && Place(thing.DestroyTemplate, thing.Position, thing.Orientation, thing.Sector) is { } debris
            && thing.Type == ThingType.Container && (debris.Model.Length > 0 || debris.Scene.Model is not null))
            debris.ExpireAt = Milliseconds + Rules.GameRules.Current.Items.ContainerDropLifetimeMs;
        // Nothing else comes out. A character's DROPITEM (or its treasure roll) was put on the
        // floor at the moment of death, beside the body (`Rpg.Loot`); the
        // body going drops nothing. Until then this put the DROPITEM down here, as a stopgap.
        return true;
    }

    /// <summary>
    /// `simThing::RemoveFromWorld`  — the thing leaves the world and goes
    /// on existing. This is **not** <see cref="Remove"/>: that one destroys, this one is what
    /// happens to a sword when somebody picks it up.
    ///
    /// The engine does four things and the order is its own: unlink from the **sector's** thing
    /// list, unlink from the **world's** thing list, clear the thing's world and
    /// its cached walk surface, and release the slot it holds in the world's
    /// thing-by-index array. Then it **recurses over the attached children**, so a
    /// thing taken out of the world takes whatever hangs off it with it.
    ///
    /// Two of those matter here. The world pointer is the engine's one-bit answer to "is it really
    /// there", which <see cref="WorldThing.InWorld"/> is. And the **GUID registration is not
    /// touched** — the hashtable is global and set at construction, which is why a
    /// carried item still resolves by GUID, why a save can write one, and why <see cref="Enter"/>
    /// cannot use the table as its guard.
    ///
    /// The children stay attached: they leave the world with their parent and are still riding it
    /// when it comes back.
    /// </summary>
    /// <returns>False when it was not in the world to begin with, which is the engine's own 0.</returns>
    public bool Withdraw(WorldThing thing)
    {
        if (!thing.InWorld) return false;
        things.Remove(thing);
        Props?.Leave(thing);
        thing.Place = null;
        foreach (var a in thing.Attachments.ToArray()) Withdraw(a.Child);
        return true;
    }

    /// <summary>
    /// **The unavailable pass** — what the engine runs on a thing that has stopped being available,
    /// and it is called at the end of **every** pick-up (both branches).
    ///
    /// It is gated on `THING_FLAG_TARGETTED` (`0x2000000`), whose own comment in `Codex.java` says
    /// *"Thing is currently targeted (for combat, pickup, activation, etc.)"*. When the flag is
    /// set, it walks the **world's** thing list, and for every `THING_TYPE_ACTOR` or
    /// `THING_TYPE_PLAYER` it resolves that actor's action target through the GUID
    /// table and, where it is this thing, **cancels its order — unforced, the feed's stop told to
    /// release**, so one busy, dead or held keeps it. Then it clears the
    /// flag. (Earlier, Vitomir cleared the action outright; a comparison with 1.1 flagged it.)
    ///
    /// In play: two people walk towards the same dropped sword, one of them gets there first, and
    /// **the other stops rather than walking to an empty floor**. Nothing else in the engine would
    /// have told them.
    /// </summary>
    public void Untarget(WorldThing thing)
    {
        if (!thing.Has(WorldThing.Flag.Targetted)) return;
        foreach (var other in things.ToArray())
            if (other.Type is ThingType.Actor or ThingType.Player && other.Actor is { } who && ReferenceEquals(who.Target, thing))
                Rpg.Actions.Cancel(other, release: true);
        thing.Flags &= ~WorldThing.Flag.Targetted;
    }

    /// <summary>
    /// How long a <see cref="ThingType.Container"/>'s dropped item lasts before the thing tick
    /// takes it away: **ten seconds** (`now + 10000` is written into the thing's expiry, which is the
    /// same expiry field a corpse uses).
    /// </summary>
    public const int ContainerDropLifetime = 10_000;
}
