// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Things that arrive after the level has loaded, and whether anyone can see them.
//
// The original bakes nothing about its things. Level geometry is baked per sector at
// load, but every thing is submitted on its own, once a frame, by walking
// the sector's live linked list — no type test, no cached copy, no
// render-side registry. So the whole of putting a new thing on screen is the
// one pointer store that links it into that list, and it draws on the
// very next frame. Its model is read on a cache miss, its material likewise,
// and its bitmaps inside the first draw that wants them.
//
// This tree keeps one vertex buffer per place instead, which is faster to draw and has to be paid
// for here: a thing entering the world appends its geometry to the mesh, and the mesh tells the
// renderer it has grown. These tests are that bargain — that a thing arriving is drawn, that a
// thing leaving is not, and that neither disturbs anything already in the list.
using System.Numerics;
using Vitomir.Engine.Scene;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

public class DrawableTests
{
    static Location Open(GameFiles files) =>
        Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");

    const string Sword = "broadsword";

    /// <summary>
    /// **The headline.** Until now nothing made at run time was drawn at all: gibs, skeletons,
    /// summons and every dropped item were in the world, blocked people, could be picked up again,
    /// and were invisible. A thing that enters the world now gets its geometry at that moment,
    /// which is where the engine links it into the sector's draw list.
    /// </summary>
    [Fact]
    public void A_thing_that_arrives_after_the_level_loaded_is_drawn()
    {
        using var files = Install.Open();
        var place = Open(files);
        int was = place.Drawn.Count, batches = place.Mesh.Batches.Count, revision = place.Mesh.Revision;
        var who = place.Player!.Thing;

        var made = place.Place(Sword, who.Position, Vector3.Zero, who.Sector);

        Assert.NotNull(made);
        Assert.True(made.InWorld);
        Assert.Equal(was + 1, place.Drawn.Count);
        Assert.Same(made, place.Drawn[was]);
        Assert.True(place.Mesh.Batches.Count > batches, "the sword arrived with no geometry");
        Assert.NotEqual(revision, place.Mesh.Revision);
        // Every batch it brought names it, and nothing else.
        Assert.All(place.Mesh.Batches.Skip(batches), b => Assert.Equal(was, b.Thing));
    }

    /// <summary>
    /// Nothing already in the mesh may shift when something arrives. A batch names its thing by
    /// position in <see cref="Location.Drawn"/> and an <see cref="AnimatedThing"/> names its
    /// vertices by offset; the list is appended to and never reordered, so both keep pointing at
    /// what they pointed at.
    /// </summary>
    [Fact]
    public void Everything_already_in_the_mesh_keeps_its_place_when_something_arrives()
    {
        using var files = Install.Open();
        var place = Open(files);
        var named = place.Mesh.Batches.Where(b => b.Thing >= 0).Select(b => place.Drawn[b.Thing]).ToList();
        var posed = place.Mesh.Animated.Select(a => (a.Thing, a.FirstVertex, Who: place.Drawn[a.Thing])).ToList();
        Assert.NotEmpty(named);
        Assert.NotEmpty(posed);
        var who = place.Player!.Thing;

        for (int i = 0; i < 4; i++) place.Place(Sword, who.Position, Vector3.Zero, who.Sector);

        Assert.Equal(named, place.Mesh.Batches.Where(b => b.Thing >= 0).Take(named.Count).Select(b => place.Drawn[b.Thing]));
        Assert.Equal(posed, place.Mesh.Animated.Select(a => (a.Thing, a.FirstVertex, Who: place.Drawn[a.Thing])).Take(posed.Count));
        // And every posed slice still lies inside the vertex list it indexes.
        foreach (var a in place.Mesh.Animated)
            Assert.True(a.FirstVertex + a.Vertices.Length <= place.Mesh.VertexCount,
                        $"thing {a.Thing}'s vertices run past the end of the mesh");
    }

    /// <summary>
    /// A thing goes in and out of the world all the time — that is what picking a sword up and
    /// dropping it is (out of the world). It is the same object each time, so it is given
    /// a body once and keeps it; the second arrival must not build a second one.
    /// </summary>
    [Fact]
    public void A_thing_picked_up_and_put_down_again_is_not_given_a_second_body()
    {
        using var files = Install.Open();
        var place = Open(files);
        var who = place.Player!.Thing;
        var made = place.Place(Sword, who.Position, Vector3.Zero, who.Sector)!;
        int drawn = place.Drawn.Count, batches = place.Mesh.Batches.Count, verts = place.Mesh.VertexCount;

        Assert.True(place.Withdraw(made));
        Assert.False(made.InWorld);
        place.Enter(made);

        Assert.True(made.InWorld);
        Assert.Equal(drawn, place.Drawn.Count);
        Assert.Equal(batches, place.Mesh.Batches.Count);
        Assert.Equal(verts, place.Mesh.VertexCount);
    }

    /// <summary>
    /// The same for a thing that was in the scene to begin with: picking up the letter Cuthbert
    /// gives you and dropping it again costs nothing, because the mesh has had its geometry since
    /// the level opened.
    /// </summary>
    [Fact]
    public void A_scene_thing_put_back_keeps_the_body_the_scene_gave_it()
    {
        using var files = Install.Open();
        var place = Open(files);
        var prop = place.Things.First(t => t.Type == ThingType.Prop);
        int drawn = place.Drawn.Count, batches = place.Mesh.Batches.Count, revision = place.Mesh.Revision;

        Assert.True(place.Withdraw(prop));
        place.Enter(prop);

        Assert.Equal(drawn, place.Drawn.Count);
        Assert.Equal(batches, place.Mesh.Batches.Count);
        Assert.Equal(revision, place.Mesh.Revision);
    }

    /// <summary>
    /// A thing that exists but is in nobody's world — made and registered but never
    /// entered in a world, which is what an item created straight into a pack is — is in no sector's list
    /// and so is drawn by nobody. It gets its body when it is dropped, not when it is made.
    /// </summary>
    [Fact]
    public void A_thing_that_was_made_but_never_placed_is_not_drawn()
    {
        using var files = Install.Open();
        var place = Open(files);
        int drawn = place.Drawn.Count, batches = place.Mesh.Batches.Count;

        var made = place.Make(Sword)!;

        Assert.False(made.InWorld);
        Assert.Equal(drawn, place.Drawn.Count);
        Assert.Equal(batches, place.Mesh.Batches.Count);

        var who = place.Player!.Thing;
        made.Position = who.Position;
        made.Sector = who.Sector;
        place.Enter(made);

        Assert.Equal(drawn + 1, place.Drawn.Count);
        Assert.True(place.Mesh.Batches.Count > batches, "the dropped sword arrived with no geometry");
    }

    /// <summary>
    /// A model and its materials are read when the thing wanting them arrives, not before — the
    /// engine's model cache loads on a miss and so does its material cache.
    /// A level that never used a model has never paid for it, and pays now.
    /// </summary>
    [Fact]
    public void A_model_the_level_never_used_is_read_when_its_thing_arrives()
    {
        using var files = Install.Open();
        var place = Open(files);
        var used = place.Drawn.Select(t => t.Model).Where(m => m.Length > 0)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var who = place.Player!.Thing;

        // The first template in the set whose model this level has never drawn.
        string? stranger = null;
        foreach (var name in place.Templates!.Names.Take(400))
        {
            var tags = place.Templates.Resolve(name);
            if (!tags.TryGetValue(Tags.Model, out var tag)) continue;
            string model = tag.AsString();
            if (model.Length == 0) continue;
            if (used.Contains(model)) continue;
            if (!files.TryRead("3d/models/" + (model.Contains('.') ? model : model + ".nod"), out _)) continue;
            stranger = name;
            break;
        }
        Assert.SkipWhen(stranger is null, "every template in reach uses a model this level already has");

        int materials = place.Materials.Items.Count, skipped = place.Mesh.Skipped.Count;
        var made = place.Place(stranger!, who.Position, Vector3.Zero, who.Sector);

        Assert.NotNull(made);
        Assert.Equal(skipped, place.Mesh.Skipped.Count);
        Assert.True(place.Materials.Items.Count > materials,
                    $"{stranger} brought no material the level did not already have");
    }

    /// <summary>
    /// **A character spawned mid-game now has bones.** The animation is attached by the mesh
    /// builder, so while the builder only ran at load, a character the level placed had one and a
    /// character spawned at run time did not — and that was the reason a gib's
    /// bone pick quietly degrades to the body's transform for every summon. Giving a thing its
    /// geometry when it enters the world gives it its motion set at the same moment, which is what
    /// that note said would close it.
    /// </summary>
    [Fact]
    public void A_character_spawned_mid_game_is_posed_like_one_the_scene_placed()
    {
        using var files = Install.Open();
        var place = Open(files);
        var standing = place.Drawn.First(t => t.Type == ThingType.Actor && t.Animation is not null);
        var who = place.Player!.Thing;

        var made = place.Place(standing.Template, who.Position, Vector3.Zero, who.Sector);

        Assert.NotNull(made);
        Assert.NotNull(made.Animation);
        Assert.Equal(standing.Animation!.Model.Bones.Length, made.Animation.Model.Bones.Length);
        // And it is in the posed list, so the renderer re-poses it every frame like anyone else.
        Assert.Contains(place.Mesh.Animated, a => a.Thing == place.Drawn.Count - 1);
    }

    /// <summary>
    /// **What the shipped scenes actually place, by type** — the measurement behind
    /// <see cref="SceneMesh.AddThing"/>'s rule that a region, and a thing with no type at all, are
    /// the only two things skipped.
    ///
    /// The mesh builder used to skip projectiles and explosions as well, on the reasoning that
    /// they are made at run time. The reasoning was right and the rule was wrong: the engine's draw
    /// loop has no type test in it at all, a projectile and an explosion are ordinary
    /// template-built things with ordinary models, and once things made at run
    /// time are drawn a rule that skips two of the types they come in stops being harmless. This is
    /// the check that dropping it changed nothing about what loads: across all 115 places the
    /// single-player chronicles visit, **no scene places either type**.
    ///
    /// 14,987 placed things, as of this writing: 11,682 props, 1,793 actors, 526 containers, 494
    /// items, 413 regions, 49 weapons, 25 armours and the 5 THING_TYPE_PLAYER things the scenes
    /// place themselves.
    /// </summary>
    [Fact]
    public void What_the_shipped_scenes_place_by_type()
    {
        using var files = Install.Open();
        var counts = new Dictionary<ThingType, int>();
        int places = 0;
        foreach (var path in files.List("").Where(p => p.EndsWith(".nsc", StringComparison.OrdinalIgnoreCase))
                                  .Concat(files.List("chronicles/")).Distinct().Order())
        {
            if (Path.GetFileName(path).StartsWith("MP_", StringComparison.OrdinalIgnoreCase)) continue;
            NscChronicle chronicle;
            try { chronicle = NscChronicle.Read(files.Read(path), Path.GetFileName(path)); } catch { continue; }
            foreach (var loc in chronicle.Locations)
            {
                if (loc.LevelName.Length == 0 || !files.TryRead($"levels/{loc.LevelName}.nil", out _)) continue;
                string? scene = loc.SceneName.Length > 0 && files.TryRead($"scenes/{loc.SceneName}.nsd", out _)
                    ? loc.SceneName : null;
                var place = Location.Load(files, loc.LevelName, things: true, sky: false, scene: scene);
                places++;
                foreach (var t in place.Drawn) counts[t.Type] = counts.GetValueOrDefault(t.Type) + 1;
            }
        }
        string tally = string.Join(", ", counts.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));
        Assert.True(places >= 115, $"only {places} places loaded");
        // The two the builder used to skip, and the one it still does.
        Assert.False(counts.ContainsKey(ThingType.Projectile), tally);
        Assert.False(counts.ContainsKey(ThingType.Explosion), tally);
        Assert.False(counts.ContainsKey(ThingType.None), tally);
        Assert.True(counts.GetValueOrDefault(ThingType.Region) >= 400, tally);
        Assert.True(counts.Values.Sum() >= 14900, tally);
    }
}
