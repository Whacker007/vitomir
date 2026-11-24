// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>Small levels built in memory for the movement's tests: floors, boxes, ramps, walls and pits, as triangles.</summary>
internal sealed class TestLevel
{
    readonly List<Vector3> vertices = [];
    readonly List<int> indices = [];

    public TestLevel Triangle(Vector3 a, Vector3 b, Vector3 c)
    {
        int at = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c);
        indices.Add(at); indices.Add(at + 1); indices.Add(at + 2);
        return this;
    }

    /// <summary>A quad a–b–c–d (two triangles).</summary>
    public TestLevel Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => Triangle(a, b, c).Triangle(a, c, d);

    /// <summary>A level floor at height <paramref name="z"/> over the rectangle, cut into a grid of 2 m squares as a cook's would be.</summary>
    public TestLevel Floor(float x0, float y0, float x1, float y1, float z = 0f, float cell = 2f)
    {
        for (float x = x0; x < x1 - 1e-4f; x += cell)
            for (float y = y0; y < y1 - 1e-4f; y += cell)
            {
                float xe = MathF.Min(x + cell, x1), ye = MathF.Min(y + cell, y1);
                Quad(new(x, y, z), new(xe, y, z), new(xe, ye, z), new(x, ye, z));
            }
        return this;
    }

    /// <summary>A solid box (its six faces).</summary>
    public TestLevel Box(Vector3 min, Vector3 max)
    {
        var (a, b) = (min, max);
        Vector3 P(float x, float y, float z) => new(x, y, z);
        Quad(P(a.X, a.Y, b.Z), P(b.X, a.Y, b.Z), P(b.X, b.Y, b.Z), P(a.X, b.Y, b.Z));   // top
        Quad(P(a.X, a.Y, a.Z), P(a.X, b.Y, a.Z), P(b.X, b.Y, a.Z), P(b.X, a.Y, a.Z));   // bottom
        Quad(P(a.X, a.Y, a.Z), P(b.X, a.Y, a.Z), P(b.X, a.Y, b.Z), P(a.X, a.Y, b.Z));   // −y
        Quad(P(a.X, b.Y, a.Z), P(a.X, b.Y, b.Z), P(b.X, b.Y, b.Z), P(b.X, b.Y, a.Z));   // +y
        Quad(P(a.X, a.Y, a.Z), P(a.X, a.Y, b.Z), P(a.X, b.Y, b.Z), P(a.X, b.Y, a.Z));   // −x
        Quad(P(b.X, a.Y, a.Z), P(b.X, b.Y, a.Z), P(b.X, b.Y, b.Z), P(b.X, a.Y, b.Z));   // +x
        return this;
    }

    /// <summary>A ramp rising along +X from (<paramref name="x0"/>, z 0) at <paramref name="degrees"/> for <paramref name="run"/> metres across, between y0 and y1, with its sides and back.</summary>
    public TestLevel Ramp(float x0, float run, float y0, float y1, float degrees)
    {
        float top = run * DetMath.Tan(degrees * DetMath.DegToRad);
        float x1 = x0 + run;
        Quad(new(x0, y0, 0), new(x1, y0, top), new(x1, y1, top), new(x0, y1, 0));
        Triangle(new(x0, y0, 0), new(x1, y0, 0), new(x1, y0, top));
        Triangle(new(x0, y1, 0), new(x1, y1, top), new(x1, y1, 0));
        Quad(new(x1, y0, 0), new(x1, y1, 0), new(x1, y1, top), new(x1, y0, top));
        return this;
    }

    public LevelSpace Build(string name = "test") =>
        new(name, CollisionWorld.FromTriangles([.. vertices], [.. indices]));
}

/// <summary>Driving one body with the motor directly, tick by tick.</summary>
internal static class Drive
{
    public static readonly Locomotion Numbers = Locomotion.Sandbox;

    /// <summary>Lets a body fall onto what is under it and come to rest (up to two seconds).</summary>
    public static Body Settle(LevelSpace level, Vector3 feet, float yaw = 0f)
    {
        var body = Body.At(feet, yaw);
        for (int i = 0; i < 240 && !(body.State == BodyState.Ground && body.GroundSeconds > 0.1f); i++)
            BodyMotor.Step(ref body, Numbers, new MoveIntent { Facing = yaw, Faces = true }, level.Collision, [], 0, i);
        return body;
    }

    /// <summary>Steps a body for <paramref name="ticks"/> with the intent each tick gives, calling back after each.</summary>
    public static Body Run(LevelSpace level, Body body, int ticks, Func<int, Body, MoveIntent> intent, Action<int, Body, MotorOutcome>? after = null,
                           Locomotion? numbers = null)
    {
        var n = numbers ?? Numbers;
        for (int i = 0; i < ticks; i++)
        {
            var o = BodyMotor.Step(ref body, n, intent(i, body), level.Collision, [], 0, i);
            after?.Invoke(i, body, o);
        }
        return body;
    }

    /// <summary>
    /// Steps a body that makes moves (its <see cref="Traversal"/> kept from tick to tick in <paramref name="move"/>)
    /// for <paramref name="ticks"/> with the intent each tick gives, calling back after each with the body, the move and what happened.
    /// </summary>
    public static Body Moves(LevelSpace level, Body body, ref Traversal move, int ticks, Func<int, Body, MoveIntent> intent,
                             Action<int, Body, Traversal, MotorOutcome>? after = null, Locomotion? numbers = null)
    {
        var n = numbers ?? Numbers;
        for (int i = 0; i < ticks; i++)
        {
            var o = BodyMotor.Step(ref body, ref move, n, intent(i, body), level.Collision, [], 0, i);
            after?.Invoke(i, body, move, o);
        }
        return body;
    }

    /// <summary>Wishing to go along (<paramref name="x"/>, <paramref name="y"/>) at a gait, facing that way.</summary>
    public static MoveIntent Toward(float x, float y, Gait gait = Gait.Run, bool jump = false)
    {
        var d = Vector2.Normalize(new Vector2(x, y));
        return new MoveIntent { Direction = d, Gait = gait, Facing = DetMath.Atan2(d.Y, d.X), Faces = true, Jump = jump };
    }

    /// <summary>How deep the body's capsule is inside the level.</summary>
    public static float Depth(LevelSpace level, in Body body) => level.Collision.Penetration(BodyMotor.CapsuleOf(body.Feet, Numbers));
}
