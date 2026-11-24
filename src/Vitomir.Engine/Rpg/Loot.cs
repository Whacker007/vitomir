// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a death or an opening leaves on the floor.
using Diag = Vitomir.Engine.Diagnostics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **Exactly one thing per event.** A character killed (`rpgActor::Kill`, its loot
/// block) and a container opened or broken do the same three steps, in the same
/// order:
/// <list type="number">
/// <item>its <see cref="WorldThing.DropItem"/>, if it names a template, is made — **else** one roll
/// of its <see cref="WorldThing.TreasureClass"/> (<see cref="Treasure.Roll"/>);</item>
/// <item>whatever was made is put **on the floor beside it** (the `SpawnThingNear`
/// placer, distance 0, flags 0xb — so the first direction with ground wins, and in practice the
/// item lands just in front of the chest or the body);</item>
/// <item>if it could not be placed it is destroyed.</item>
/// </list>
/// There is no container pane and no looting a corpse: the player picks the item up off the floor
/// like any other. In single player a dead character's own inventory and what it wore **stay on
/// the body and go with it** — the spill of the whole pack is the multiplayer host's alone
/// (on a host).
/// </summary>
public static class Loot
{
    /// <summary>The placer's flags: 1 enter the world, 2 no out-parameters, 8 no room test.</summary>
    public const int PlaceFlags = 0xb;

    /// <summary>
    /// Makes and places what <paramref name="source"/> leaves, or nothing. <paramref name="source"/>
    /// must still be in the world, which is why the death calls this before a gib takes the body.
    /// </summary>
    public static WorldThing? Drop(WorldThing source)
    {
        if (source.Place is not { } place || source.Removed) return null;
        WorldThing? made = null;
        string from;
        // DROPITEM is a template pointer by the time the engine reads it (the setter resolved the
        // name), so a name that is not a template is no DROPITEM at all and the class is rolled.
        if (source.DropItem.Length > 0 && place.Templates?.Has(source.DropItem) == true)
        {
            made = place.Make(source.DropItem);
            from = "dropitem";
        }
        else if (source.TreasureClass != 0 && place.Treasure is { } table)
        {
            made = table.Roll(source.TreasureClass) is { } record ? Treasure.Make(place, record) : null;
            from = $"class {source.TreasureClass}";
        }
        else return null;

        Diag.Journal.Current?.Write(place.Milliseconds, Diag.JournalKind.Loot,
            ("from", Diag.Watch.Name(source)), ("by", from),
            ("made", made is null ? "nothing" : made.Template),
            ("amount", made?.Item?.Amount));
        if (made is null) return null;
        place.PlaceNear(made, source, 0f, PlaceFlags);
        return made;
    }
}
