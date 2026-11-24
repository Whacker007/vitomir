// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a character begins a game holding, out of the `item` lines of their `.NPC`.
//
// Read early and deliberately parked there — *"half a feature that says what it would have
// done beats half a feature that quietly does the wrong thing"* — because applying it needs the
// template set, which the actor does not own. The inventory pane arriving is what made it worth
// doing: without somewhere to draw them, starting items were invisible either way.
//
// **Three placements, and only one of them needs a decision.** `main` and `belt` name a container
// outright, so the item goes in the first cell that will take it. `equip` names no slot, so the
// slot comes from the item's own **type** — `Item.SlotsFor`, which is the engine's own mapping and
// was settled from the save corpus — and where that is a *pair* (the two arms, the two hands) the
// choice between them is `Equipment.Equip`'s, already written and already handling the two-handed
// case that makes `leathergloves` occupy both arms.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>What happened to one `item` line.</summary>
/// <param name="Template">The name the file gave.</param>
/// <param name="Where">The placement it asked for.</param>
/// <param name="Slot">Where it actually went, or <see cref="Rpg.Slot.None"/> if nowhere.</param>
/// <param name="Note">Why, when it did not go where it was asked.</param>
public readonly record struct KitLine(string Template, Formats.NpcPlacement Where, int Slot, string? Note)
{
    public bool Placed => Slot != Rpg.Slot.None;
}

/// <summary>Gives a character the items their `.NPC` starts them with.</summary>
public static class StartingKit
{
    /// <summary>
    /// Applies every `item` line, in the file's own order, and says what became of each. Nothing
    /// here throws: a template the install has not got, or an item that will not fit anywhere, is
    /// **reported** — a mod will hit both, and a silent drop is the failure that looks like nothing.
    /// </summary>
    /// <param name="numbers">
    /// The number each made item takes, in order — a companion's, held for it as it was made
    /// (<see cref="World.CharacterSheets"/>). Without it the player's held numbers are used
    /// (<see cref="World.ThingGuids.GivingKit"/>).
    /// </param>
    public static List<KitLine> Give(WorldThing thing, Formats.NpcFile npc, TemplateSet templates,
                                     EffectDefinitions effects, int nowMs = 0, Func<int>? numbers = null)
    {
        var report = new List<KitLine>();
        if (thing.Actor is not { } actor) return report;
        if (numbers is not null) { Give(thing, actor, npc, templates, effects, nowMs, report, numbers); return report; }
        // The numbers the player's making held for its kit (World.ThingGuids.Hold) go to these things.
        var guids = thing.Place?.Guids;
        if (guids is not null) guids.GivingKit = true;
        try { Give(thing, actor, npc, templates, effects, nowMs, report, null); }
        finally { guids?.KitGiven(); }
        return report;
    }

    static void Give(WorldThing thing, Actor actor, Formats.NpcFile npc, TemplateSet templates,
                     EffectDefinitions effects, int nowMs, List<KitLine> report, Func<int>? numbers)
    {

        foreach (var line in npc.Items)
        {
            var tags = templates.Has(line.Template) ? templates.Resolve(line.Template) : null;
            if (tags is null) { report.Add(new(line.Template, line.Where, Slot.None, "no such template")); continue; }

            // **A kit item is a thing**, made and registered but never entering the world — which
            // is what the engine does for anything created straight into a pack, and what gives it
            // the GUID a save writes and a script resolves (`Rpg.Ground`). Without a location to
            // make it in — a test fixture, a character built outside a level — the item is built
            // on its own and simply has no thing, which everything downstream checks for.
            var made = thing.Place?.Make(line.Template, numbers?.Invoke() ?? 0);
            if ((made?.Item ?? Item.From(line.Template, tags)) is not { } item)
            {
                report.Add(new(line.Template, line.Where, Slot.None, "not an item template"));
                continue;
            }
            if (made is not null) made.Owner = thing;

            switch (line.Where)
            {
                case Formats.NpcPlacement.Equip:
                    actor.Inventory.Add(item);
                    var got = Equipment.Equip(thing, item, Slot.None, effects, nowMs);
                    if (got == EquipResult.Equipped) report.Add(new(line.Template, line.Where, item.Location, null));
                    else
                    {
                        // It stays carried rather than vanishing, which is what the pack is for.
                        Stow.Place(actor, item, Slot.Inventory);
                        report.Add(new(line.Template, line.Where, item.Location, got.ToString()));
                    }
                    break;

                case Formats.NpcPlacement.Belt:
                    actor.Inventory.Add(item);
                    if (!Stow.Place(actor, item, Slot.QuickUse))
                    {
                        Stow.Place(actor, item, Slot.Inventory);
                        report.Add(new(line.Template, line.Where, item.Location, "the belt would not take it"));
                    }
                    else report.Add(new(line.Template, line.Where, item.Location, null));
                    break;

                default:
                    actor.Inventory.Add(item);
                    if (!Stow.Place(actor, item, Slot.Inventory))
                        report.Add(new(line.Template, line.Where, Slot.None, "the pack is full"));
                    else report.Add(new(line.Template, line.Where, item.Location, null));
                    break;
            }
        }
    }
}
