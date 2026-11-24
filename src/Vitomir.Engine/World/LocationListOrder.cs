// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The order of the world's thing list, which the frame's walk and the scene's two messages follow.
namespace Vitomir.Engine.World;

public sealed partial class Location
{
    /// <summary>
    /// **The world's thing list in 1.1's order: newest first.** A thing enters its world through
    /// The world's add links it in at the **head** of the list (the new thing's next
    /// its old head); leaving unlinks it wherever it is. Everything that walks
    /// the list starts at the head and follows `thing+8` — the frame's thing tick, and
    /// `beginscene` and `endscene`. So the level's
    /// last thing is the first one ticked, and a thing spawned, dropped or carried in ticks before
    /// everything that was there already.
    ///
    /// Vitomir keeps <see cref="Things"/> in the order of entry — the scene's things as it is read
    /// (the scene load reads them in the same order and links each in at the head), then every
    /// <see cref="Enter"/> appended, a thing that left and came back appended again — so the list
    /// is that array reversed. Like <see cref="Snapshot"/> it is a copy, taken when asked: a thing
    /// that enters during a walk over it is not in it, which is 1.1's too — the head has already
    /// been passed.
    /// </summary>
    public WorldThing[] ListOrder
    {
        get
        {
            var order = Snapshot;
            Array.Reverse(order);
            return order;
        }
    }
}
