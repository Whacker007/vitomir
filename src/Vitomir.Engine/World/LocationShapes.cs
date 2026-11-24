// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A thing given a new body to draw — the shape change's half of the scene (Rpg.ShapeShift).
namespace Vitomir.Engine.World;

public sealed partial class Location
{
    /// <summary>
    /// Whether a model of that name can be loaded here — what the engine's model cache would answer.
    /// With no files to look in (a place built for a test), every name can.
    /// </summary>
    public bool CanLoadModel(string model) => Files is null || Mesh.Model(model, Files) is not null;

    /// <summary>
    /// The thing's geometry made again from its <see cref="WorldThing.Model"/> and
    /// <see cref="WorldThing.MotionSet"/> as they are now — the shape change's swap of the render model
    /// and the motion set. Unlike <see cref="SetModel"/>, **nothing it was
    /// playing carries over**: the engine's shape change puts a freshly loaded model in, whose mixer
    /// is empty, and then chooses the locomotion, which the caller does.
    ///
    /// The mesh is one per place, so the old geometry's slot in <see cref="Drawn"/> is retired and the
    /// new geometry appended, as <see cref="SetModel"/> does (ours: the engine draws each thing on
    /// its own and needs no such step).
    /// </summary>
    public void Reshape(WorldThing thing)
    {
        if (Files is null || !drawnAt.TryGetValue(thing, out int at)) return;
        drawn[at] = Retired;
        drawnAt.Remove(thing);
        Mesh.Unanimate(at);
        thing.Animation = null;
        Show(thing);
    }
}
