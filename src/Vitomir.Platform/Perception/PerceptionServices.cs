// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Navigation;

namespace Vitomir.Platform.Perception;

/// <summary>
/// An observer put where a test wants it (ours, the program's <c>--witness &lt; kind&gt; @x, y, z[,yaw]</c>, carried on
/// a recording's head): the kind's short name or id, the feet (metres) and the way it faces (degrees, counter-clockwise
/// from +X; 0 when not given).
/// </summary>
public readonly record struct WitnessRequest(string Kind, Vector3 Feet, float YawDegrees)
{
    /// <summary>Reads <c>kind@x,y,z[,yaw]</c> (a point for the decimals); false with <paramref name="error"/> saying why.</summary>
    public static bool TryParse(string? text, out WitnessRequest witness, out string error)
    {
        witness = default;
        error = $"--witness wants <kind>@x,y,z[,yaw] (metres, degrees), for example --witness guard@12,-3,0,180; not '{text}'";
        if (string.IsNullOrWhiteSpace(text)) return false;
        int at = text.IndexOf('@');
        if (at <= 0) return false;
        var parts = text[(at + 1)..].Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is < 3 or > 4) return false;
        var v = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || !float.IsFinite(v[i])) return false;
        witness = new WitnessRequest(text[..at].Trim(), new Vector3(v[0], v[1], v[2]), parts.Length == 4 ? v[3] : 0f);
        error = "";
        return true;
    }

    /// <summary>As <see cref="TryParse"/> reads it: <c>guard@12,-3,0,180</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Kind}@{Feet.X:0.###},{Feet.Y:0.###},{Feet.Z:0.###},{YawDegrees:0.###}");

    /// <summary>The tape head's note the witnesses are carried in, each as <see cref="ToString"/> writes it, joined by <c>;</c>.</summary>
    public const string TapeNote = "witnesses";
}

/// <summary>Perception registered in one call (ours), after the masquerade (<see cref="MasqueradeServices.Register"/>).</summary>
public static class PerceptionServices
{
    /// <summary>The systems' places: the reactions just before the routes (Think), perception after the risk check (Perceive).</summary>
    public const int ReactOrder = MovementServices.RoutesOrder - 10, PerceiveOrder = MasqueradeServices.RiskOrder + 10;

    /// <summary>
    /// Registers, in this order (the digest's): the component <see cref="Observer"/>; the events
    /// <see cref="AwarenessChanged"/>, <see cref="ShoutRaised"/>, <see cref="ReactionStarted"/>, <see cref="AreaHeatRose"/>
    /// and <see cref="AreaHeatGone"/>; the services <see cref="PerceptionConfig"/> (empty, for the modules to fill) and
    /// <see cref="PerceptionState"/> (the acts in view and the heat of <paramref name="areas"/>, or of the whole level);
    /// the systems <c>Think/90 platform.reactions</c> (<see cref="ReactionSystem"/>) and <c>Perceive/20
    /// platform.perception</c> (<see cref="PerceptionSystem"/>).
    /// </summary>
    public static WorldBuilder Register(WorldBuilder b, LevelAreas? areas = null)
    {
        b.Component<Observer>();
        b.Event<AwarenessChanged>().Event<ShoutRaised>().Event<ReactionStarted>().Event<AreaHeatRose>().Event<AreaHeatGone>();
        b.Service(new PerceptionConfig()).Service(new PerceptionState(areas ?? LevelAreas.None));
        b.System(Phase.Think, ReactOrder, "platform.reactions", new ReactionSystem());
        b.System(Phase.Perceive, PerceiveOrder, "platform.perception", new PerceptionSystem());
        return b;
    }

    /// <summary>Makes body <paramref name="e"/> an observer of kind <paramref name="kind"/> (an index of <see cref="PerceptionConfig.Kinds"/>), unaware.</summary>
    public static void Observe(World world, EntityId e, int kind) => world.Set(e, Observer.Of(kind));

    /// <summary>
    /// A witness (<see cref="WitnessRequest"/>): a body of <paramref name="numbers"/> standing at the request's feet facing
    /// its way, posted there (<see cref="RouteWalker.AtPost"/>: it stands until a reaction sends it off and walks back
    /// after), an observer of kind <paramref name="kind"/>.
    /// </summary>
    public static EntityId Witness(World world, in WitnessRequest request, int kind, in Locomotion numbers)
    {
        float yaw = DetMath.WrapAngle(request.YawDegrees * DetMath.DegToRad);
        var e = MovementServices.SpawnBody(world, request.Feet, yaw, numbers);
        world.Set(e, RouteWalker.AtPost(request.Feet, yaw));
        Observe(world, e, kind);
        return e;
    }
}
