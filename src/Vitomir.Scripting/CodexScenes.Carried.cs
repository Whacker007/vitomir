// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The scene beginning and ending, told to the scripts of what the characters carry — the torch, the
// flashlight and the Gangrel eye light themselves in `beginscene` and put themselves out in
// `endscene`.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>
    /// **Every thing in the world with a script hears the scene begin** in 1.1: the scene's start walks the
    /// world's thing list (a comparison with 1.1; not every Codex object, as this said
    /// before). A worn utility item is on that list — it is put in the world and attached to the hand
    /// (`Rpg.Utility`) — and a pack's item is not. Vitomir makes an item's
    /// script only when it has something to hear (<see cref="Own"/>), and one that is not riding a
    /// thing in the world is not on the live list — so the worn items' scripts are told here, after
    /// the live ones (1.1's list has them near its head: the order is
    /// gap 1).
    ///
    /// Only the items worn by a character in this place (the equipped bit, `ITEM_FLAG_BEINGWORN`)
    /// are told, and a script the live list already told is not told twice. The three shipped item
    /// classes that answer `beginscene` — `ItemUtilityTorch`, `ItemUtilityFlashlight`,
    /// `ItemGangrelEye` — are utility items and act only when worn, so this tells what 1.1 tells;
    /// a worn weapon's or armour's script, which 1.1 does not tell, answers nothing.
    /// </summary>
    void BeginCarried()
    {
        foreach (var item in Worn())
        {
            if (item.Thing is not { } thing || OwnScripts(thing).Length > 0) continue;
            foreach (var (name, script) in Own(thing))
            {
                try { runtime.Call(script, "beginscene", PlayerGuid, 0); }
                catch (Exception e) { Engine.Log($"{name}.beginscene failed: {ScriptException.From(name, e).JavaType}"); }
            }
        }
    }

    /// <summary>
    /// The scene ending, told to the carried items' scripts that exist and are not on the live list
    /// (the scene's end walks the same global list <see cref="BeginCarried"/> stands in for). The torch
    /// takes its flame away here.
    /// </summary>
    void EndCarried(int exitNumber)
    {
        foreach (var item in Worn())
        {
            if (item.Thing is not { } thing || OwnScripts(thing).Length > 0) continue;
            if (!itemScripts.TryGetValue(thing, out var script)) continue;
            try { runtime.CallEndscene(script, 0, 0, exitNumber); }
            catch (Exception e) { Engine.Log($"{thing.Script}.endscene failed: {ScriptException.From(thing.Script, e).JavaType}"); }
        }
    }

    /// <summary>
    /// **The bodies a journey leaves behind** (ours). In 1.1 an item is one thing with one Codex
    /// object, and it goes with the party: the torch's object hears `endscene` as the place is left
    /// and `beginscene` as the next begins, and lights one flame on the one torch. Vitomir carries the
    /// item and makes it a **new body** in the place arrived at (<c>Campaign.Carry</c>); the old body
    /// stays registered in the place left, out of the world, and its script stayed on that place's
    /// live list — so when the party came back to a place still loaded, the old torch's script heard
    /// `beginscene`, found its item worn (the item is the same object), and hung a flame on the old
    /// body where it was let go: the lone flame seen in mid-air in the monastery after Walk the
    /// Abyss there and back, and after any journey out of a place kept loaded and back.
    /// A body whose item has moved on to another is let go of here, after it has heard `endscene`
    /// and before the place's scripts are put aside, its timers with it.
    /// </summary>
    void ForgetCarriedBodies()
    {
        if (location is not { } here) return;
        var gone = live.Where(l => here.Thing(l.Thing) is { Item: { } item } body && !ReferenceEquals(item.Thing, body)).ToArray();
        foreach (var l in gone)
        {
            live.Remove(l);
            timers.RemoveAll(t => t.Guid == l.Guid);
        }
    }

    /// <summary>What the characters in this place are wearing, with a script of its own.</summary>
    IEnumerable<Item> Worn()
    {
        if (location is null) yield break;
        foreach (var who in location.Things.ToArray())
        {
            if (who.Actor is not { } actor) continue;
            foreach (var item in actor.Inventory.Items.ToArray())
                if (((int)item.Flags & Equipment.EquippedBit) != 0 && item.Thing is { Script.Length: > 0 })
                    yield return item;
        }
    }
}
