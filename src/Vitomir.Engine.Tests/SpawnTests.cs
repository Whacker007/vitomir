// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Making a thing while the game runs, and unmaking one. Up to
/// now every thing in a level came out of a scene file at load, so the thing list could be an array
/// and a GUID could be an index into it. Neither is true any more, and these tests are mostly about
/// that: a spawned thing is a real member of the place, findable by the GUID a script would use,
/// standing in a sector, and a removed one is gone from all three.
/// </summary>
public class SpawnTests
{
    static Location Open(GameFiles files) =>
        Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");

    /// <summary>A template every install has, so a test never invents one.</summary>
    const string Rat = "petRat";

    [Fact]
    public void A_spawned_thing_stands_exactly_where_its_spawner_does()
    {
        using var files = Install.Open();
        var place = Open(files);
        var from = place.Player!.Thing;
        int before = place.Things.Count;

        var made = place.Spawn(Rat, from);

        Assert.NotNull(made);
        // The engine copies the spawner's whole transform — no offset, no drop to the ground.
        Assert.Equal(from.Position, made.Position);
        Assert.Equal(from.Orientation, made.Orientation);
        Assert.Equal(from.Sector, made.Sector);
        Assert.Equal(Rat, made.Template);
        // And it is a member of the place, by the GUID a script would be handed.
        Assert.Equal(before + 1, place.Things.Count);
        Assert.Same(made, place.Thing(made.Guid));
        Assert.Contains(made, place.Things);
    }

    /// <summary>
    /// A GUID is no longer an index. The scene's things are numbered as they are read and the
    /// counter carries on from there, never yielding 0 (which is "no thing" to a script) and never
    /// reusing one that is still in the table.
    /// </summary>
    [Fact]
    public void A_spawned_things_guid_carries_on_past_the_scenes_own()
    {
        using var files = Install.Open();
        var place = Open(files);
        int scene = place.Things.Max(t => t.Guid);
        var from = place.Player!.Thing;

        // One number a rat, or two when its template gives it a fallback weapon (the tag reader's case 0x5d
        // makes that a thing first; LocationGuids.cs).
        var first = place.Spawn(Rat, from)!;
        int each = first.Guid - scene;
        var second = place.Spawn(Rat, from)!;

        Assert.InRange(each, 1, 2);
        Assert.Equal(scene + 2 * each, second.Guid);
        Assert.NotEqual(0, first.Guid);
        // Removing one does not hand its number to the next: the counter only goes up.
        Assert.True(place.Remove(first));
        var third = place.Spawn(Rat, from)!;
        Assert.Equal(scene + 3 * each, third.Guid);
        Assert.Null(place.Thing(first.Guid));
    }

    [Fact]
    public void A_removed_thing_leaves_the_list_the_guid_table_and_its_sector()
    {
        using var files = Install.Open();
        var place = Open(files);
        var made = place.Spawn(Rat, place.Player!.Thing)!;
        int bodies = place.Props!.Count;
        int count = place.Things.Count;

        Assert.True(place.Remove(made));

        Assert.True(made.Removed);
        Assert.Equal(count - 1, place.Things.Count);
        Assert.Null(place.Thing(made.Guid));
        Assert.DoesNotContain(made, place.Things);
        if (made.Collide != CollideShape.None) Assert.Equal(bodies - 1, place.Props.Count);

        // The engine's destroy is guarded by a flag it sets first, so asking twice does nothing.
        Assert.False(place.Remove(made));
        Assert.Equal(count - 1, place.Things.Count);
    }

    /// <summary>
    /// A spawner that is not in the world spawns nothing — the engine needs both a world and a
    /// sector off it before it will build anything.
    /// </summary>
    [Fact]
    public void Nothing_is_spawned_from_a_thing_that_has_left_the_world()
    {
        using var files = Install.Open();
        var place = Open(files);
        var from = place.Spawn(Rat, place.Player!.Thing)!;
        place.Remove(from);

        Assert.Null(place.Spawn(Rat, from));
        // And a template the table has not got is refused rather than made empty.
        Assert.Null(place.Spawn("noSuchTemplateAnywhere", place.Player!.Thing));
    }

    /// <summary>
    /// A projectile and an explosion are the only two types given a maker — the spawner's GUID, at
    /// two different offsets. It is the whole of kill attribution in the original, so a test that
    /// nothing else gets one is as much the point as the two that do.
    /// </summary>
    [Fact]
    public void Only_a_projectile_or_an_explosion_remembers_who_made_it()
    {
        using var files = Install.Open();
        var place = Open(files);
        var from = place.Player!.Thing;
        var templates = place.Templates!;

        // Take real templates of each type out of the install rather than naming ones.
        var byType = new Dictionary<ThingType, string>();
        foreach (var name in templates.Names)
        {
            var tags = templates.Resolve(name);
            if (!tags.TryGetValue(Tags.Type, out var t)) continue;
            byType.TryAdd((ThingType)t.AsUInt(), name);
        }
        Assert.SkipUnless(byType.ContainsKey(ThingType.Projectile), "no projectile template in the install");

        var bolt = place.Spawn(byType[ThingType.Projectile], from)!;
        Assert.Equal(from.Guid, bolt.SpawnedBy);

        if (byType.TryGetValue(ThingType.Explosion, out var boom))
            Assert.Equal(from.Guid, place.Spawn(boom, from)!.SpawnedBy);

        // Everything else is left at zero.
        foreach (var (type, template) in byType)
        {
            if (type is ThingType.Projectile or ThingType.Explosion) continue;
            Assert.Equal(0, place.Spawn(template, from)!.SpawnedBy);
        }
    }

    /// <summary>
    /// **A destroy drops nothing but its DESTROY_TEMPLATE.** A DROPITEM is a death's or an opening's
    /// (`LootTests`), so a character a script removes takes its item with it — the teardown's
    /// `drop` flag is the destroy template's, and suppressing it leaves nothing at all.
    /// </summary>
    [Fact]
    public void A_thing_with_a_dropitem_drops_nothing_when_it_is_removed()
    {
        using var files = Install.Open();
        var place = Open(files);
        var templates = place.Templates!;
        string? dropper = templates.Names.FirstOrDefault(n =>
            templates.Resolve(n) is var tags
            && tags.TryGetValue(Tags.DropItem, out var d) && d.AsString().Length > 0 && templates.Has(d.AsString())
            && !(tags.TryGetValue(Tags.DestroyTemplate, out var dt) && dt.AsString().Length > 0));
        Assert.SkipWhen(dropper is null, "no template in the install names a DROPITEM and no destroy template");

        var thing = place.Spawn(dropper!, place.Player!.Thing)!;
        Assert.NotEqual("", thing.DropItem);
        int count = place.Things.Count;

        place.Remove(thing);

        Assert.Equal(count - 1, place.Things.Count);
        Assert.DoesNotContain(place.Things, t => t.Template.Equals(thing.DropItem, StringComparison.OrdinalIgnoreCase)
                                                 && t.Guid > thing.Guid);
    }

    /// <summary>
    /// `THING_FLAG_REMOVE` now has a consumer. The generic thing tick raises it when a thing's
    /// expiry passes and then destroys it — which is what finally takes a corpse, a gib
    /// or a container's dropped item out of the world, and once nothing did.
    /// </summary>
    [Fact]
    public void An_expired_thing_is_taken_out_of_the_world_by_the_tick()
    {
        using var files = Install.Open();
        var place = Open(files);
        var made = place.Spawn(Rat, place.Player!.Thing)!;
        var sim = new Simulation(place);

        made.ExpireAt = 500;
        sim.Advance(0.2f);
        Assert.False(made.Removed);          // not yet

        sim.Advance(0.5f);
        Assert.True(made.Removed);
        Assert.Null(place.Thing(made.Guid));
        Assert.DoesNotContain(made, place.Things);
    }

    /// <summary>
    /// A character that has left the world stops thinking. Its mind is still in the array — the AI
    /// is built once and does not grow yet — so the gate is on the thing rather than on the list.
    /// </summary>
    [Fact]
    public void A_removed_character_stops_thinking()
    {
        using var files = Install.Open();
        var place = Open(files);
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var mind = minds.All.First(m => !ReferenceEquals(m.Thing, minds.Steered) && m.Thing.Actor is not null);

        mind.NextThink = 0;
        minds.Tick(0.1f);
        int thought = mind.NextThink;

        place.Remove(mind.Thing);
        mind.NextThink = 0;
        minds.Tick(0.1f);
        Assert.Equal(0, mind.NextThink);      // the gate stopped it before anything scheduled
        _ = thought;
    }

    /// <summary>
    /// The effects a character is born with: a vampire's frenzy falls on its own, a
    /// mortal's mana and blood come back on their own, and neither is in any template — the engine
    /// applies them by name as the thing is placed. Nothing in Vitomir did this before, so a
    /// frenzied vampire stayed frenzied for ever.
    /// </summary>
    [Fact]
    public void A_vampire_is_born_losing_frenzy_and_a_mortal_gaining_mana()
    {
        using var files = Install.Open();
        var place = Open(files);
        var effects = new EffectDefinitions(files);
        place.Awaken(new AiClasses(files), 1, effects);

        var christof = place.Player!.Thing;
        Assert.Equal(ActorType.Vampire, christof.Actor!.Type);
        Assert.Contains(christof.Effects.All, e => e.Definition.Name.Equals("ef_decreasefrenzy", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(christof.Effects.All, e => e.Definition.Name.Equals("ef_increasemanaot", StringComparison.OrdinalIgnoreCase));

        // And they are real: the frenzy effect ticks every two seconds and takes two off.
        var frenzy = effects.File!["ef_decreasefrenzy"]!;
        Assert.Equal(2000, frenzy.TickMs);
        Assert.Equal(-2f, frenzy.At(0));
        Assert.Equal((int)ActorStat.Frenzy, frenzy.Subtype);

        // A mortal in the same scene gets the other two instead.
        var mortal = place.Things.FirstOrDefault(t => t.Actor?.Type == ActorType.Human);
        if (mortal is null) return;
        Assert.Contains(mortal.Effects.All, e => e.Definition.Name.Equals("ef_increasemanaot", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(mortal.Effects.All, e => e.Definition.Name.Equals("ef_increasebloodot", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(mortal.Effects.All, e => e.Definition.Name.Equals("ef_decreasefrenzy", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A thing spawned after the AI woke up is born with its effects too.</summary>
    [Fact]
    public void A_character_spawned_later_is_born_with_the_same_effects()
    {
        using var files = Install.Open();
        var place = Open(files);
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));

        var rat = place.Spawn(Rat, place.Player!.Thing)!;
        Assert.SkipWhen(rat.Actor is null, $"{Rat} is not a character in this install");
        var wanted = rat.Actor!.Type == ActorType.Human ? "ef_increasemanaot" : "ef_decreasefrenzy";
        if (rat.Actor.Type is ActorType.Vampire or ActorType.Human)
            Assert.Contains(rat.Effects.All, e => e.Definition.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// `CodexThing.SpawnThing` and `CodexThing.Remove`, through the script layer rather than
    /// through the engine's own API — which is how 100 shipped scripts reach them.
    /// </summary>
    [Fact]
    public void The_place_is_reachable_by_the_guid_a_script_would_be_handed()
    {
        using var files = Install.Open();
        var place = Open(files);
        var made = place.Spawn(Rat, place.Player!.Thing)!;

        // The GUID round-trips, which is the contract every CodexThing native depends on.
        Assert.Same(made, place.Thing(made.Guid));
        Assert.Null(place.Thing(0));
        Assert.Null(place.Thing(place.NextGuid));
    }

    /// <summary>
    /// The flag verbs answer with the word **as it was**, not as it is — the same shape
    /// `SetActorFlags` has, and the reason a script can put a flag back where it found it.
    /// </summary>
    [Fact]
    public void The_render_flag_verbs_or_and_and_not_and_answer_with_the_old_word()
    {
        using var files = Install.Open();
        var place = Open(files);
        var thing = place.Player!.Thing;
        thing.RenderFlags = 0b0101;

        // Set ORs: it never replaces the word.
        uint was = thing.RenderFlags;
        thing.RenderFlags |= 0b0010;
        Assert.Equal(0b0111u, thing.RenderFlags);
        Assert.Equal(0b0101u, was);

        // Clear ANDs with the complement, so bits it does not name survive.
        thing.RenderFlags &= ~0b0100u;
        Assert.Equal(0b0011u, thing.RenderFlags);
    }

    /// <summary>
    /// **Only a `MOVETYPE` 3 thing has physics.** Every physics native tests the thing's move type before
    /// anything else and answers zero or false otherwise — so a script that has been pushing a door
    /// about with `SetVelocity` has never moved it, and that is the original's behaviour rather
    /// than a gap in ours.
    /// </summary>
    [Fact]
    public void Only_a_physics_thing_has_a_velocity_or_physics_flags()
    {
        using var files = Install.Open();
        var place = Open(files);
        var templates = place.Templates!;
        var physical = place.Things.FirstOrDefault(t => t.MoveType == WorldThing.MovePhysics);
        var inert = place.Things.First(t => t.MoveType != WorldThing.MovePhysics);

        Assert.Equal(0u, inert.PhysicsFlags);
        Assert.Equal(default, inert.Velocity);

        // At least one shipped template somewhere is a physics thing, or the gate is untested.
        int kinds = templates.Names.Count(n =>
            templates.Resolve(n).TryGetValue(Tags.MoveType, out var mt) && mt.AsUInt() == WorldThing.MovePhysics);
        Assert.True(kinds > 0, "no shipped template has MOVETYPE 3 — the physics gate is untestable");
        _ = physical;
    }

    /// <summary>
    /// **Moving a thing is not a write to its position.** `simThing::SetPosition` re-finds the
    /// sector the new point falls in and moves the thing between the sectors' thing lists, and
    /// once `CodexThing.SetPosition` did neither — a teleported thing kept its old
    /// sector and went on blocking people where it used to be. The homework item this closes has
    /// been open since the script layer went in.
    /// </summary>
    [Fact]
    public void Moving_a_thing_takes_it_to_the_sector_the_point_is_actually_in()
    {
        using var files = Install.Open();
        var place = Open(files);
        Assert.SkipWhen(place.Solid is null, "no collision loaded for this level");
        var thing = place.Spawn(Rat, place.Player!.Thing)!;

        // Somewhere in a different sector: take another thing's position rather than inventing one.
        var elsewhere = place.Things.FirstOrDefault(t => t.Sector >= 0 && t.Sector != thing.Sector);
        Assert.SkipWhen(elsewhere is null, "this scene is all one sector");

        long was = thing.Sector;
        place.Move(thing, elsewhere!.Position);

        Assert.Equal(elsewhere.Position, thing.Position);
        Assert.NotEqual(was, thing.Sector);
        Assert.Equal(place.At(elsewhere.Position, was), thing.Sector);
        // And it is in the new sector's body list rather than the old one's, which is the half
        // that was actually broken: the bodies near it are found from the sector.
        if (thing.Collide != CollideShape.None)
            Assert.Contains(thing, place.Props!.In([(int)thing.Sector]));
    }

    /// <summary>
    /// A spawn does not take the caller's sector on trust either — it finds the one the point is
    /// in, which is what keeps a thing spawned beside a portal on the right side of it.
    /// </summary>
    [Fact]
    public void A_spawn_finds_the_sector_rather_than_inheriting_it()
    {
        using var files = Install.Open();
        var place = Open(files);
        Assert.SkipWhen(place.Solid is null, "no collision loaded for this level");
        var from = place.Player!.Thing;

        var made = place.Place(Rat, from.Position, from.Orientation, sector: 0);
        Assert.NotNull(made);
        Assert.Equal(place.At(from.Position, 0), made.Sector);
    }
    // ---- beside, not on (SpawnThingNear) -----------------------------------------------

    static float Across(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Y - b.Y).Length();

    /// <summary>
    /// `SpawnThingNear` puts the new thing the distance asked out along the first of eight directions
    /// with ground under it, on that ground, facing out along it.
    /// </summary>
    [Fact]
    public void Spawning_near_puts_it_the_distance_out_facing_away_on_the_ground()
    {
        using var files = Install.Open();
        var place = Open(files);
        var from = place.Player!.Thing;
        var made = place.SpawnNear(Rat, from, 80f, 8 | 3);

        Assert.NotNull(made);
        Assert.Equal(80f, Across(made!.Position, from.Position), 2);
        float yaw = made.Orientation.Z * MathF.PI / 180f;
        var facing = new Vector2(-MathF.Sin(yaw), MathF.Cos(yaw));
        var outward = Vector2.Normalize(new Vector2(made.Position.X - from.Position.X, made.Position.Y - from.Position.Y));
        Assert.Equal(1f, Vector2.Dot(facing, outward), 3);
        Assert.Same(made, place.Thing(made.Guid));
        Assert.True(made.Sector >= 0);
    }

    [Fact]
    public void An_anchor_that_does_not_scatter_takes_it_exactly_where_it_stands()
    {
        using var files = Install.Open();
        var place = Open(files);
        var from = place.Player!.Thing;
        from.Flags |= WorldThing.Flag.NoScatter;
        var made = place.SpawnNear(Rat, from, 80f, 3)!;
        Assert.Equal(from.Position, made.Position);
        Assert.Equal(from.Orientation, made.Orientation);
    }

    /// <summary>A distance of none is "just clear": both radii, when each has a shape, and a tenth more.</summary>
    [Fact]
    public void No_distance_means_just_clear_of_each_other()
    {
        using var files = Install.Open();
        var place = Open(files);
        var from = place.Player!.Thing;
        var made = place.SpawnNear(Rat, from, 0f, 8 | 3)!;
        float expected = ((made.Collide != CollideShape.None ? made.Radius : 0f)
                          + (from.Collide != CollideShape.None ? from.Radius : 0f)) * 1.1f;
        Assert.True(expected > 0f);
        Assert.Equal(expected, Across(made.Position, from.Position), 2);
        Assert.Null(place.SpawnNear("noSuchTemplate", from, 80f, 3));
    }
}
