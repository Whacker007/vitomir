// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Shapeshifting's three natives: Shape of the Beast's and Mask of 1,000
// Faces' effects call the first two, and nothing shipped asks the third.
using Vitomir.Engine.Rpg;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>
    /// `ActorShapeShift`, `ActorEndShapeShift` and `IsActorShapeShifted`, each a thin door onto <see cref="ShapeShift"/>. All three take a character only
    /// (type 0 or 7) and answer false for anything else. None of them relays anything to the other
    /// machines of a multiplayer game.
    /// </summary>
    void RegisterShapes()
    {
        // The name copied into 32 bytes (31 characters and a zero), refused when there is
        // no such template, then the shape change with the flags, not forced — so a second shape
        // over the first answers false and changes nothing.
        Engine.On("CodexActor.ActorShapeShift", (self, a) =>
        {
            if (Character(self) is not { } t || location?.Templates is not { } templates) return CodexHost.Box(false);
            string name = Name31(a, 0);
            if (name.Length == 0 || !templates.Has(name)) return CodexHost.Box(false);
            return CodexHost.Box(ShapeShift.Shift(t, name, unchecked((uint)CodexHost.Int(a, 1)), forced: false));
        });
        // The shape ended; false when no shape was on.
        Engine.On("CodexActor.ActorEndShapeShift", (self, _) =>
            CodexHost.Box(Character(self) is { } t && ShapeShift.End(t)));
        // Whether a shape is on.
        Engine.On("CodexActor.IsActorShapeShifted", (self, _) =>
            CodexHost.Box(Character(self)?.Actor is { } me && ShapeShift.IsShifted(me)));
    }
}
