// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The world's walk and one thing's tick, in 1.1's order.
//
// 1.1's world tick walks the world's thing list from its head, and the world's add links every
// thing in at the head — so the newest thing ticks first — and it runs one thing's whole tick
// before it reads the next. Everything a thing does in a frame is in that one call:
// its expiry, its move, its light, its touches, its character's tick (think, lips, effects,
// action, fade) or its explosion's, its model's animation and ramps, and its riders. So who acts
// first in a frame, and the order the rolls come off the shared streams, is the list's.
//
// `TickThing` is that one call, and it is the seam: the per-thing order lives here and nowhere else.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Scene;

namespace Vitomir.Engine.World;

public sealed partial class Simulation
{
    /// <summary>
    /// The world's walk: every thing of the place in the list's order,
    /// **newest first** (<see cref="Location.ListOrder"/>), each thing's whole tick
    /// (<see cref="TickThing"/>) before the next's.
    ///
    /// The walk is over the list as it stood when it began. The tick reads a thing's next before
    /// ticking it, and anything a tick makes goes in at the head, behind the walk: **a thing made
    /// during the walk first ticks on the next frame** — a skeleton a blow spawns, the explosion a
    /// grenade becomes, an item a death drops. A thing destroyed by an earlier thing's tick is
    /// deleted when the walk reaches it, as 1.1 deletes it there; one taken out of the
    /// world (picked up) is passed over (ours: 1.1 would tick one it had already read as the next,
    /// out of the world, through its move alone).
    /// </summary>
    void StepThings(float seconds)
    {
        BeginThings(seconds);
        foreach (var thing in Location.ListOrder)
        {
            if (thing.Removed) { Scripts.DeleteThing(thing); continue; }
            if (!ReferenceEquals(thing.Place, Location)) continue;
            TickThing(thing, seconds);
        }
        EndThings();
    }

    /// <summary>Whether the AI thinks this frame (the minds' switch), set as the walk begins.</summary>
    bool thinking;

    /// <summary>The minds of this place's AI, to tell a thing's own from one another place made (ours).</summary>
    readonly HashSet<Mind> ours = [];
    Mind[]? oursFrom;

    /// <summary>The projectiles' dice, clock and tally for this frame's physics (made once a walk).</summary>
    Physics.Shots? shots;

    /// <summary>
    /// The frame's work before the walk: the AI's clock (and the minds that joined), the scripts'
    /// clock and their leftovers (<see cref="ISceneScripts.BeginThings"/>), the explosions' clock,
    /// and the animated things indexed by thing.
    /// </summary>
    void BeginThings(float seconds)
    {
        thinking = false;
        if (Location.Minds is { } minds)
        {
            thinking = minds.Begin(seconds);
            if (!ReferenceEquals(oursFrom, minds.All))
            {
                ours.Clear();
                foreach (var m in minds.All) ours.Add(m);
                oursFrom = minds.All;
            }
        }
        Scripts.BeginThings(seconds);
        Location.BeginExplosions(Milliseconds);
        shots = new Physics.Shots(Random, Milliseconds, Location.Motions, ShotLanded);
        IndexAnimated();
    }

    /// <summary>
    /// The frame's work after the walk: the AI's oscillation check, the steered walker's ring, the
    /// explosions let go and the lights put on their things, and a conversation's stop-the-walkers
    /// bit lowered (cleared as the walk ends).
    /// </summary>
    void EndThings()
    {
        if (thinking) Location.Minds?.Finish();
        // **The ring goes when the walker stops**, arrived or given up, which is the walker's stop clearing
        // it — and it is why a click across a room leaves a ring you only half see, while a
        // click at your feet leaves one that barely appears. Watched as an edge here rather than
        // hooked onto `Player.Stop`, because `Stop` is also how a *new* route begins and hooking it
        // would take the ring away on the frame it was placed.
        bool walking = Location.SteeredWalker is { Walking: true };
        if (wasWalking && !walking) WalkTarget.Clear();
        wasWalking = walking;
        Location.EndExplosions();
        Rpg.ConversationStop.Lower(Location);                          // the stop bit cleared
    }

    /// <summary>
    /// **One thing's whole tick**, in 1.1's order:
    /// <list type="number">
    /// <item>its expiry: a projectile's burst, then the destroy
    /// — and a thing flagged <c>THING_FLAG_REMOVE</c> is deleted and ticks no
    /// further;</item>
    /// <item>its move, unless it rides something: the walker
    /// with its footfalls, the track, the physics;</item>
    /// <item>while it is still in the world: its proximity test, then a
    /// character's tick or an explosion's, then its model — the animation,
    /// whose keyframe tags land the blows, and the ramps — then its riders put back on
    /// it.</item>
    /// </list>
    /// Its light is put on it after the walk (<see cref="Location.EndExplosions"/>, ours).
    /// </summary>
    void TickThing(WorldThing thing, float seconds)
    {
        if (Expire(thing)) return;

        if (thing.AttachedTo is null) Move(thing, seconds);
        // Vitomir's destroy takes a thing out at once, so a thing its own move destroyed (a
        // projectile that struck) stops here; 1.1's only flags it and finishes the tick, and
        // deletes it at the next (ours).
        if (!thing.InWorld) return;

        if ((thing.Flags & WorldThing.Flag.ProximityTrigger) != 0) Scripts.TouchThing(thing);
        if (thing.Actor is { } actor) ActorTick(thing, actor, seconds);                          // the character's tick (types 0, 7)
        else if (thing.Type == ThingType.Explosion) Location.TickExplosion(thing, Milliseconds, Random);
        if (!thing.InWorld) return;                                                     // ours, as above

        StepModel(thing, seconds);
        if (!thing.InWorld) return;
        // Everything riding it put back where its bone has got to, after its
        // animation and its move, so it follows this frame's pose.
        if (thing.Attachments.Count > 0 && thing.AttachedTo is null) Attachments.Update(thing);
    }

    /// <summary>
    /// The top of the thing's tick: an expiry that has passed destroys it
    /// — a projectile with <c>PROJ_EXPLODE_ONTIMER</c> bursting as it goes,
    /// which is how a thrown grenade goes off in the air; the steered
    /// character never expires, which Vitomir does not ask, since nothing
    /// gives it an expiry — and a thing flagged
    /// <c>THING_FLAG_REMOVE</c> is deleted there and then (the deleting destructor) and
    /// does nothing else this frame. True when the thing is gone.
    ///
    /// The destroy drops what it drops like any other: the engine's expiry passes the same second
    /// argument a script's `Remove` does (0), so a crate that rots away leaves its
    /// contents exactly as a smashed one does. Seventeen templates set the burst and no arrow, bolt
    /// or bullet does.
    /// </summary>
    bool Expire(WorldThing thing)
    {
        if (thing.ExpireAt != 0 && thing.ExpireAt <= Milliseconds)
        {
            thing.ExpireAt = 0;
            if (Rpg.Projectiles.BurstsOnTimer(thing)) Rpg.Projectiles.Expired(thing);
            else thing.Flags |= WorldThing.Flag.Remove;
        }
        if (!thing.Removed && (thing.Flags & WorldThing.Flag.Remove) == 0) return false;
        Location.Remove(thing);
        Scripts.DeleteThing(thing);
        return true;
    }

    /// <summary>
    /// The thing's move (by <c>MOVETYPE</c>): its walker — the AI's for a
    /// character with a mind, the one a script gave a thing with none (`Location.WalkerFor`), the
    /// steered character's route (<see cref="SteeredWalk"/>; the player's input only orders a
    /// walk) — and the footfalls the walker says; a script's track
    /// move; a <c>MOVETYPE 3</c> thing's physics. Vitomir asks each by its own
    /// state rather than by <c>MOVETYPE</c> (ours), and a thing has one of them.
    /// </summary>
    void Move(WorldThing thing, float seconds)
    {
        if (thing.Mind is { } mind && ours.Contains(mind)) Location.Minds!.FollowOne(mind, seconds);
        foreach (var walker in Location.ScriptWalkers)
            if (ReferenceEquals(walker.Thing, thing)) { walker.Follow(seconds); break; }
        if (Location.SteeredWalker is { Walking: true } steered && ReferenceEquals(steered.Thing, thing)) steered.Follow(seconds);
        if (thing.Actor is not null) Footfall(thing);

        if (thing.Motion is not null) Scripts.MoveThing(thing, seconds);
        if (thing.MoveType == WorldThing.MovePhysics && !thing.Removed)
            Physics.Step(Location, thing, seconds, shots);
    }

    /// <summary>
    /// A character's own tick, in its order: the think (not on a player's
    /// machine, which runs no AI); the mouth and a chat line's lips; the
    /// effects; the action — which is what closes the distance, times a swing
    /// out and puts the character back to standing; its carried things' own ticks, where they have
    /// anything to do (<see cref="CarriedTick"/>); then the
    /// fade of a corpse.
    /// </summary>
    void ActorTick(WorldThing thing, Actor actor, float seconds)
    {
        if (thinking && thing.Mind is { } mind && ours.Contains(mind)) Location.Minds!.ThinkOne(mind);
        if (!thing.InWorld) return;
        if (thing.Animation is { } animation) animation.Mouth = Anim.LipSync.For(thing);
        Anim.ChatLips.Tick(thing, Milliseconds);
        // Everything live on it: reap what has ended, pay out what is due (the effects'
        // tick). Every character gets it whether or not anyone is looking, and so does a corpse
        // — an effect does not stop because its host died, it stops because it was told to.
        if (thing.Effects.All.Count > 0)
            Rpg.EffectRuntime.Tick(thing, Milliseconds, Location.Minds?.Random ?? shared, Location.Motions);
        if (!thing.InWorld) return;
        // A character doing nothing is ticked too while its player holds an attack on something (Actions.HeldAgain, the Fix
        // heldattack): with the Fix off that tick does nothing.
        if (actor.Action != Rpg.ActionMode.None || actor.Repeating is not null)
            Rpg.Actions.Tick(thing, Milliseconds, moving: thing.Mind?.Moving ?? false, Location.Motions);
        CarriedTick(actor, seconds);
        Fade(actor, thing);
    }

    /// <summary>
    /// A corpse's fade: when <see cref="Actor.FadeAt"/> passes the actor tick **clears
    /// it** — which is what closes the window on a `TARGET DEAD` discipline — and fades the model's
    /// alpha to nothing over 4.8 seconds (the alpha set to 0 over 4800 ms). The removal that follows is
    /// the thing's expiry, <see cref="WorldThing.ExpireAt"/>, at the top of its tick.
    ///
    /// A dusting body's fade began with the dust — the skeleton death took the alpha down over a
    /// second of its own — so <see cref="Actor.FadingFrom"/> is started here at once; its
    /// <see cref="Actor.FadeAt"/> is the kill's thirty seconds all the same, and when it
    /// comes round the fade it starts is from nothing to nothing (Rpg.Damage.Kill).
    /// </summary>
    void Fade(Actor actor, WorldThing thing)
    {
        if (actor.Dusted && actor.FadingFrom == 0 && actor.Dead) actor.FadingFrom = Milliseconds;
        else if (actor.FadeAt != 0 && actor.FadeAt <= Milliseconds)
        {
            actor.FadeAt = 0;
            if (actor.FadingFrom == 0) actor.FadingFrom = Milliseconds;       // a dusted body's began with the dust
            thing.Model3D.SetAlpha(0f, Rules.GameRules.Current.Death.CorpseFadeDurationMs[0]);
        }
    }

    /// <summary>
    /// A character setting off or coming to a halt: <c>startmove</c> as it
    /// sets off, and as it stops <c>stopmove</c> and a <c>scuff_&lt;floor&gt;</c> — the scrape of a
    /// foot planted. The engine says these from inside the walker, on the bit it keeps for "moving"
    /// (the physics word's bit 1); here that is <see cref="WorldThing.Walking"/>, compared frame to
    /// frame, right after the thing's walker.
    /// </summary>
    void Footfall(WorldThing thing)
    {
        if (Location.Audio is null) return;
        bool now = Location.Player is { } p && ReferenceEquals(p.Thing, thing) ? p.Walking : thing.Mind?.Moving ?? false;
        bool held = HeldUp.Heard(Location, thing) && now && thing.Walking;
        if (now == thing.Walking && !held) return;
        thing.Walking = now;
        if (now && !held) { Audio.Noise.Foley(thing, Audio.FoleyEvent.StartMove); return; }
        Audio.Noise.Foley(thing, Audio.FoleyEvent.StopMove);
        if (Audio.KeyframeSounds.FloorOrNone(thing) is var floor and >= 0) Audio.Noise.Foley(thing, Audio.FoleyEvent.Scuff + floor);
    }

    /// <summary>
    /// The thing's model: its animation moves on — whether or not it is on screen — and
    /// then its own ramps, alpha, scale and shell, which keep running while the animation is
    /// frozen. A character's crossed keyframe tags are collected as it goes: a <c>Strike</c> or
    /// <c>Fire</c> tag is the moment its blow lands (<see cref="Land"/>), there and then, in its own
    /// tick.
    /// </summary>
    void StepModel(WorldThing thing, float seconds)
    {
        if (animatedOf.TryGetValue(thing, out var a)) Animate(thing, a, seconds);
        if (thing.InWorld && thing.Fx is { Busy: true } fx) fx.Tick(seconds);
    }

    /// <summary>
    /// The animated things by thing, for each one's own tick. <see cref="SceneMesh.Animated"/> is
    /// append-only and each entry names its thing by the position in <see cref="Location.Drawn"/>
    /// the mesh builder recorded, which never shifts; a thing is drawn once (`drawnAt`). So the
    /// entries added since the last walk are looked up before this one begins, and a thing given
    /// geometry during the walk — a skeleton a blow spawned — is not in the walk and first animates
    /// on the next frame, which is what the engine does too.
    /// </summary>
    void IndexAnimated()
    {
        var animated = Location.Mesh.Animated;
        // A removal (a thing given a new body: a shape taken or ended, a model set) moves the later entries
        // down, so the count alone no longer says which are new: the index is made again. Earlier, it was
        // not, and a thing reshaped while others had been added before it kept its old body's entry — its
        // new body was never animated (Orsi, back from the wolf as he died, stood through his own toss,
        // seen in play).
        int removals = Location.Mesh.AnimatedRemovals;
        if (!ReferenceEquals(animatedFrom, animated) || removals != animatedRemovals)
        { animatedOf.Clear(); animatedIndexed = 0; animatedFrom = animated; animatedRemovals = removals; }
        for (; animatedIndexed < animated.Count; animatedIndexed++)
            animatedOf[Location.Drawn[animated[animatedIndexed].Thing]] = animated[animatedIndexed];
    }

    readonly Dictionary<WorldThing, AnimatedThing> animatedOf = new(ReferenceEqualityComparer.Instance);
    int animatedIndexed, animatedRemovals;
    List<AnimatedThing>? animatedFrom;

    /// <summary>One animated thing's advance and the tags it crossed.</summary>
    void Animate(WorldThing thing, AnimatedThing a, float seconds)
    {
        // `FreezeAnimations`: the model's mixer is not advanced (the model's update tests bit 0x20000).
        if (thing.Fx is { Frozen: true }) return;
        if (!thing.IsActor)
        {
            crossed.Clear();
            a.Animation.Advance(seconds, crossed);
            foreach (var tag in crossed)
                if (!Anim.CodexTags.Crossed(thing, tag)) Audio.KeyframeSounds.Sound(thing, tag);   // 13..15: `keytag`
            return;
        }

        // What it is holding, if that has changed since the last frame. The engine calls
        // SetWeapon from wherever the weapon is set and the model slot follows immediately;
        // here the check rides the animation, because this is the one place that
        // already has both the character and the geometry drawn for it.
        if (Location.Files is { } files) Location.Mesh.Rearm(a, thing, files, Location.Materials);

        crossed.Clear();
        a.Animation.Advance(seconds, crossed);
        foreach (var tag in crossed)
        {
            if (Rpg.Actions.IsStrikeTag(tag)) Land(thing);
            // 16, `Repeat`: held automatic fire goes round again; when the
            // play head went back, the tags after it are behind it now and do not fire.
            else if (tag.Type == Rpg.HeldFire.RepeatTag) { if (Rpg.HeldFire.Repeat(thing, Milliseconds)) break; }
            // 18, `Throwdeath`: a thrown body has landed.
            else if (tag.Type == Rpg.SpecialMove.ThrowdeathTag) Rpg.SpecialMove.Throwdeath(thing);
            // 6, `Cast`: the discipline fires (a character only).
            else if (tag.Type == Rpg.Cast.CastTag) { if (thing.IsActor) Rpg.Cast.Fire(thing); }
            // 8, `Project`: the arrow is nocked — shown on the bow.
            else if (tag.Type == Rpg.Projectiles.ProjectTag) Rpg.Projectiles.Nock(thing);
            // 19, the synthetic `endtag` of a `noloop endtag` mode: the feed's loops start.
            else if (tag.Type == Rpg.Feeding.EndTag) { if (thing.IsActor) Rpg.Feeding.Landed(thing); }
            // 20, the synthetic end of a `noloop fidget` mode: an idle flourish has run out, and
            // the locomotion is chosen again, which stands it back up.
            else if (tag.Type == Anim.ThingAnimation.FidgetTagType) thing.Locomotion?.Reanimate();
            // 13..15, `Codex1`..`Codex3`: the scripts hear `keytag`.
            else if (Anim.CodexTags.Crossed(thing, tag)) { }
            // A noise of her spell animation's, guessed on her machine, is kept to be stopped with it if taken back.
            else if (Audio.KeyframeSounds.Sound(thing, tag) is var noise and not 0) Net.NetOrders.Casts(thing)?.Sounded(noise);
        }
    }
}
