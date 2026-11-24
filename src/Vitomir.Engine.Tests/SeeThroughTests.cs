// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Scene;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Seeing through what is in the way** (ours, <see cref="SeeThrough"/>): the one decision a frame of what is drawn faded
/// — a wall between the eye and the subject is, one beside it is not, a ceiling over the subject is and a roof beyond it
/// is not, a prop in the way is and a character never — and the pointer reading that same decision: a click through a
/// faded wall lands on the floor behind it, and a faded prop is looked through. With see-through off nothing is faded and
/// the pointer is the base game's. All on a box of a level made here, so no install is needed.
/// </summary>
public class SeeThroughTests
{
    // One sector: a floor, a wall across the way at y = −100, a wall to the side at x = 300, a ceiling over the subject at
    // z = 300 and a roof far beyond it, at y = 600..800.
    const int Floor = 0, WallBetween = 1, WallBeside = 2, Ceiling = 3, RoofBeyond = 4;

    internal static NilLevel Box()
    {
        var vertices = new List<Vector3>();
        var corners = new List<NilSectorVertex>();
        var planes = new List<NilPlane>();
        var surfaces = new List<NilSurface>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, uint flags = 0)
        {
            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            planes.Add(new NilPlane(normal, Vector3.Dot(normal, a)));
            int first = corners.Count;
            foreach (var p in new[] { a, b, c, d })
            {
                corners.Add(new NilSectorVertex((ushort)vertices.Count, 0, 0xffffffff, Vector2.Zero, Vector2.Zero));
                vertices.Add(p);
            }
            surfaces.Add(new NilSurface((ushort)(planes.Count - 1), 4, (flags & NilGroup.FlagWalk) != 0 ? 0 : -1, first, surfaces.Count));
        }
        // Each surface its own group, so the floor alone is walkable. The windings are such that the floor faces up, the
        // wall between faces the eye (−y), the ceiling faces down.
        Quad(new(-1000, -1000, 0), new(1000, -1000, 0), new(1000, 1000, 0), new(-1000, 1000, 0), NilGroup.FlagWalk);
        Quad(new(-200, -100, 0), new(-200, -100, 300), new(200, -100, 300), new(200, -100, 0));
        Quad(new(300, -500, 0), new(300, 500, 0), new(300, 500, 300), new(300, -500, 300));
        Quad(new(-150, -150, 300), new(-150, 150, 300), new(150, 150, 300), new(150, -150, 300));
        Quad(new(-150, 600, 300), new(-150, 800, 300), new(150, 800, 300), new(150, 600, 300));
        var groups = surfaces.Select((s, i) => new NilGroup(1, 0, i == Floor ? NilGroup.FlagWalk : 0u, 4, [], i, i * 4)).ToArray();
        var sector = new NilSector
        {
            Vertices = [.. vertices], SectorVertices = [.. corners], SurfacePlanes = [.. planes], Surfaces = [.. surfaces], Groups = groups,
        };
        return new NilLevel { Sectors = [sector] };
    }

    static readonly Vector3 Eye = new(0, -400, 400);

    static Location PlaceOf(NilLevel level) => new()
    {
        Name = "box", Level = level, Materials = new MaterialCache(null!), Mesh = new SceneMesh(),
    };

    static WorldThing Thing(Location place, Vector3 at, ThingType type, float radius = 30f, float height = 96f)
    {
        var t = new WorldThing
        {
            Index = 0, Guid = 1, Scene = NsdThing.Placed("test", at, Vector3.Zero, 0), Template = "test", Type = type,
            Actor = type is ThingType.Player or ThingType.Actor ? new Actor { Health = 100, MaxHealth = 100 } : null,
            Position = at, Collide = CollideShape.Cylinder, Radius = radius, Height = height, Place = place,
        };
        return t;
    }

    [Fact]
    public void A_wall_between_the_eye_and_the_subject_is_faded_one_beside_it_is_not_and_a_ceiling_over_it_is()
    {
        var level = Box();
        var place = PlaceOf(level);
        var christof = Thing(place, Vector3.Zero, ThingType.Player);
        var seen = new SeeThrough();
        seen.Decide(level, [], Eye, [christof], column: 256f, seconds: 0f);
        Assert.True(seen.Faded(0, WallBetween));
        Assert.False(seen.Faded(0, WallBeside));
        Assert.True(seen.Faded(0, Ceiling));
        Assert.False(seen.Faded(0, RoofBeyond));            // beyond the subject as the eye sees it: kept
        Assert.False(seen.Faded(0, Floor));                 // what the subject stands on: kept
        Assert.Equal(1f, seen.LevelOf(0, WallBetween));     // a frame with no time: at once
        Assert.True(seen.AnySurfaces);
    }

    /// <summary>
    /// **A body drawn away from its point** (<see cref="SeeThrough.PointsOf"/>): 1.1's opening has Christof's point beside the
    /// convent's bed while his waking draws him lying on it under a sheet; framed from the point, the line from the eye to his
    /// feet passed through the bed and the sheet, and both were drawn clear. Framed where the body is drawn, what holds it is
    /// kept solid; a crate truly between the eye and the body is still faded; and a body drawn over its own point, upright,
    /// is framed as ever.
    /// </summary>
    [Fact]
    public void A_sleeper_drawn_on_a_bed_beside_its_point_keeps_the_bed_and_the_sheet_and_still_sees_through_a_crate_in_the_way()
    {
        var level = Box();
        var place = PlaceOf(level);
        var christof = Thing(place, new Vector3(150, 0, 0), ThingType.Player);
        // The bed and the sheet over the body, lying along y at (0, 0); the body drawn on it, 30 to 60 high.
        var bed = Thing(place, Vector3.Zero, ThingType.Prop);
        bed.Collide = CollideShape.Box; bed.BoxMin = new Vector3(-50, -100, 0); bed.BoxMax = new Vector3(50, 100, 40);
        var sheet = Thing(place, Vector3.Zero, ThingType.Prop);
        sheet.Collide = CollideShape.Box; sheet.BoxMin = new Vector3(-50, -90, 30); sheet.BoxMax = new Vector3(50, 90, 70);
        (Vector3, Vector3)? lying = (new Vector3(-25, -90, 30), new Vector3(25, 90, 60));
        // An eye low and to the far side of the point: framed from the point, its lines cross the bed and the sheet.
        var eye = new Vector3(-400, 0, 60);
        var seen = new SeeThrough();
        seen.Decide(level, [bed, sheet], eye, [christof], 256f, 0f);
        Assert.Equal(1f, bed.SeeThroughLevel);                         // as before: framed from the point
        Assert.Equal(1f, sheet.SeeThroughLevel);

        seen.Reset();                                                  // all solid again
        seen = new SeeThrough { Body = t => ReferenceEquals(t, christof) ? lying : null };
        var crate = Thing(place, new Vector3(-250, 0, 0), ThingType.Prop, height: 200f);
        seen.Decide(level, [bed, sheet, crate], eye, [christof], 256f, 0f);
        Assert.Equal(0f, bed.SeeThroughLevel);
        Assert.Equal(0f, sheet.SeeThroughLevel);
        Assert.Equal(1f, crate.SeeThroughLevel);                       // still in the way of where he is drawn

        // A body drawn over its own point, standing: Heights above the point, as ever.
        var points = new List<Vector3>();
        Assert.False(SeeThrough.PointsOf(christof, (new Vector3(130, -20, 0), new Vector3(170, 20, 128)), points));
        Assert.Empty(points);
        Assert.True(SeeThrough.PointsOf(christof, lying.Value, points));
        Assert.Equal(3, points.Count);
        Assert.Equal(new Vector3(0, 0, 45), points[1]);
        Assert.True(SeeThrough.Holds(bed, new Vector3(0, 0, 45)));      // within its box and the slack
        Assert.False(SeeThrough.Holds(bed, new Vector3(0, 0, 60)));
    }

    [Fact]
    public void A_prop_in_the_way_is_faded_and_a_character_never()
    {
        var level = Box();
        var place = PlaceOf(level);
        var christof = Thing(place, Vector3.Zero, ThingType.Player);
        var crate = Thing(place, new Vector3(0, -250, 0), ThingType.Prop, height: 400f);
        var monk = Thing(place, new Vector3(0, -250, 0), ThingType.Actor, height: 400f);
        var aside = Thing(place, new Vector3(250, 0, 0), ThingType.Prop, height: 400f);
        var seen = new SeeThrough();
        seen.Decide(level, [crate, monk, aside], Eye, [christof], column: 256f, seconds: 0f);
        Assert.Equal(1f, crate.SeeThroughLevel);
        Assert.Equal(0f, monk.SeeThroughLevel);
        Assert.Equal(0f, aside.SeeThroughLevel);
        Assert.Equal(0f, christof.SeeThroughLevel);

        // Out of the way again, it eases back over the fifth of a second and is solid.
        crate.Position = new Vector3(-600, 0, 0);
        seen.Decide(level, [crate, monk, aside], Eye, [christof], 256f, SeeThrough.EaseSeconds / 2f);
        Assert.Equal(0.5f, crate.SeeThroughLevel, 3);
        seen.Decide(level, [crate, monk, aside], Eye, [christof], 256f, SeeThrough.EaseSeconds);
        Assert.Equal(0f, crate.SeeThroughLevel);
    }

    [Fact]
    public void Each_level_eases_in_and_out_over_a_fifth_of_a_second()
    {
        var level = Box();
        var christof = Thing(PlaceOf(level), Vector3.Zero, ThingType.Player);
        var seen = new SeeThrough();
        seen.Decide(level, [], Eye, [christof], 256f, 0.05f);
        Assert.Equal(0.25f, seen.LevelOf(0, WallBetween), 3);
        Assert.True(seen.Faded(0, WallBetween));             // drawn faded the moment it starts to fade
        seen.Decide(level, [], Eye, [christof], 256f, 0.2f);
        Assert.Equal(1f, seen.LevelOf(0, WallBetween));
        seen.Decide(level, [], Eye, [], 256f, 0.1f);         // nobody to frame: everything going back
        Assert.Equal(0.5f, seen.LevelOf(0, WallBetween), 3);
        seen.Decide(level, [], Eye, [], 256f, 0.1f);
        Assert.False(seen.Any);
        Assert.Equal(1f, SeeThrough.Opacity(0f));
        Assert.Equal(SeeThrough.Clear, SeeThrough.Opacity(1f), 5);
    }

    [Fact]
    public void A_click_through_a_faded_wall_lands_on_the_floor_behind_it_and_a_faded_prop_is_looked_through()
    {
        var level = Box();
        var place = PlaceOf(level);
        var christof = Thing(place, Vector3.Zero, ThingType.Player);
        var crate = Thing(place, new Vector3(0, -250, 0), ThingType.Prop, height: 400f);
        var direction = Vector3.Normalize(new Vector3(0, 20, 0) - Eye);

        // Nothing faded: the wall takes the click, as the base game's pick does, and the crate takes the pointer.
        Assert.Equal(WallBetween, Pick.Nearest(level, Eye, direction)!.Value.Surface);
        Assert.Same(crate, Pick.NearestThing([crate], Eye, direction)!.Value.Thing);

        place.SeeThrough.Decide(level, [crate], Eye, [christof], 256f, 0f);
        var hit = Pick.NearestShown(level, Eye, direction, place.SeeThrough);
        Assert.NotNull(hit);
        Assert.Equal(Floor, hit.Value.Surface);
        Assert.True(hit.Value.Walkable);
        Assert.Equal(0f, hit.Value.Point.Z, 3);
        // The pointer in the place reads the same decision.
        Assert.Equal(Floor, WorldSweep.Pointer(place, Eye, direction)!.Value.Surface);
        Assert.Null(Pick.NearestThing([crate], Eye, direction));
    }

    [Fact]
    public void With_see_through_off_nothing_is_faded_and_the_camera_is_pulled_in_as_ever()
    {
        var level = Box();
        var place = PlaceOf(level);
        var christof = Thing(place, Vector3.Zero, ThingType.Player);
        var camera = new WorldCamera { HeadOf = null };
        int asked = 0;
        camera.Obstruction = (_, _, _) => { asked++; return 100f; };
        camera.Start(christof);
        camera.Tick(0.05f, 50);
        Assert.Equal(1, asked);
        Assert.Equal(100f, camera.Distance);
        place.SeeThrough.Frame(place, camera, camera.Eye, 0.05f);
        Assert.False(place.SeeThrough.Any);

        // The x-ray: the pull-in is not asked, the eye stays out where it was heading, and the frame fades the wall instead.
        var seeing = new WorldCamera { HeadOf = null, SeeThrough = SeeThroughMode.XRay };
        seeing.Obstruction = (_, _, _) => { asked++; return 100f; };
        seeing.Start(christof);
        seeing.Tick(0.05f, 50);
        Assert.Equal(1, asked);
        Assert.Equal(WorldCamera.StartDistance, seeing.Distance);
        place.SeeThrough.Frame(place, seeing, Eye, 0f);
        Assert.True(place.SeeThrough.Faded(0, WallBetween));
        // And off again: everything solid at once.
        place.SeeThrough.Frame(place, camera, Eye, 0.05f);
        Assert.False(place.SeeThrough.Any);
    }

    [Fact]
    public void The_cutscene_eye_is_not_pulled_onto_the_wall_under_the_xray_and_is_through_the_window()
    {
        var level = Box();
        var place = PlaceOf(level);
        var christof = Thing(place, Vector3.Zero, ThingType.Player);
        var wall = new Vector3(0, -100, 100);
        foreach (var see in new[] { SeeThroughMode.Off, SeeThroughMode.On, SeeThroughMode.XRay })
        {
            var camera = new WorldCamera { HeadOf = null, SeeThrough = see, SightLine = (_, _, _) => wall };
            camera.Start(christof);
            camera.SetupCutscene(christof, null);
            camera.Tick(0.05f, 50);
            if (see == SeeThroughMode.XRay) Assert.NotEqual(wall, camera.Eye);
            else Assert.Equal(wall, camera.Eye);
        }
    }

    [Fact]
    public void The_subjects_are_the_steered_character_in_the_game_camera_and_a_cutscenes_two_in_one()
    {
        var level = Box();
        var place = PlaceOf(level);
        var christof = Thing(place, Vector3.Zero, ThingType.Player);
        var wilhem = Thing(place, new Vector3(100, 0, 0), ThingType.Actor);
        var camera = new WorldCamera { HeadOf = null, SeeThrough = SeeThroughMode.On };
        camera.Start(christof);
        var who = new List<WorldThing>();
        SeeThrough.SubjectsOf(place, camera, who);
        Assert.Equal([christof], who);
        camera.SetupCutscene(christof, wilhem);
        SeeThrough.SubjectsOf(place, camera, who);
        Assert.Equal([christof, wilhem], who);
    }

    [Fact]
    public void In_the_window_the_camera_is_still_pulled_in_and_what_is_still_in_the_way_is_faded()
    {
        var level = Box();
        var place = PlaceOf(level);
        var christof = Thing(place, Vector3.Zero, ThingType.Player);
        int asked = 0;
        var camera = new WorldCamera { HeadOf = null, SeeThrough = SeeThroughMode.On };
        camera.Obstruction = (_, _, _) => { asked++; return 100f; };
        camera.Start(christof);
        camera.Tick(0.05f, 50);
        Assert.Equal(1, asked);                                      // the base game's pull-in, as ever
        Assert.Equal(100f, camera.Distance);
        place.SeeThrough.Frame(place, camera, Eye, 0f);
        Assert.Equal(SeeThroughMode.On, place.SeeThrough.Mode);
        Assert.True(place.SeeThrough.Faded(0, WallBetween));        // what the pull-in did not clear
        Assert.Single(place.SeeThrough.Holes);
    }

    [Fact]
    public void The_window_is_clear_about_the_subject_and_solid_at_its_rim_and_the_pointer_looks_through_it_only_where_it_is_clear()
    {
        var level = Box();
        var place = PlaceOf(level);
        // Far enough beyond the wall that a point well to its side is outside the window's rim.
        var christof = Thing(place, new Vector3(0, 300, 0), ThingType.Player);
        place.SeeThrough.Decide(level, [], Eye, [christof], 256f, 0f, SeeThroughMode.On);
        var seen = place.SeeThrough;
        var hole = Assert.Single(seen.Holes);
        var chest = new Vector3(0, 300, SeeThrough.Heights[1]);
        Assert.Equal(Vector3.Normalize(chest - Eye), hole.Axis);

        // On the wall, where the line to the chest crosses it: clear. Well to the side on the same wall: solid.
        var middle = Eye + (chest - Eye) * (300f / 700f);
        var side = new Vector3(190, -100, middle.Z);
        Assert.Equal(1f, seen.Clearness(middle), 3);
        Assert.Equal(0f, seen.Clearness(side), 3);
        Assert.True(seen.LooksThrough(0, WallBetween, middle));
        Assert.False(seen.LooksThrough(0, WallBetween, side));
        // Beyond the subject, nothing is cleared.
        Assert.Equal(0f, seen.Clearness(chest + (chest - Eye)), 3);

        // A click through the middle lands on the floor behind the wall; one through the solid part takes the wall.
        var through = Vector3.Normalize(new Vector3(0, 150, 0) - Eye);
        Assert.Equal(Floor, Pick.NearestShown(level, Eye, through, seen)!.Value.Surface);
        var atWall = Vector3.Normalize(side - Eye);
        Assert.Equal(WallBetween, Pick.NearestShown(level, Eye, atWall, seen)!.Value.Surface);
        Assert.Equal(WallBetween, WorldSweep.Pointer(place, Eye, atWall)!.Value.Surface);
    }

    [Fact]
    public void The_windows_sum_eases_from_the_rim_to_the_middle_as_the_shaders_smoothstep_does()
    {
        var eye = Vector3.Zero;
        float cosInner = MathF.Cos(0.1f), cosOuter = MathF.Cos(0.3f);
        var hole = new SeeThrough.Hole(Vector3.UnitY, cosInner, cosOuter, 1000f);
        Vector3 At(float angle) => new(MathF.Sin(angle) * 100f, MathF.Cos(angle) * 100f, 0f);
        Assert.Equal(1f, SeeThrough.ClearnessOf(eye, [hole], At(0.05f)), 5);
        Assert.Equal(0f, SeeThrough.ClearnessOf(eye, [hole], At(0.35f)), 5);
        float mid = (cosInner + cosOuter) / 2f;
        Assert.Equal(0.5f, SeeThrough.SmoothStep(cosOuter, cosInner, mid), 4);
        Assert.Equal(0f, SeeThrough.ClearnessOf(eye, [hole with { Far = 50f }], At(0f)), 5);   // past its reach
        Assert.Equal(0f, SeeThrough.ClearnessOf(eye, [hole with { Far = 0f }], At(0f)), 5);    // no window
    }
}
