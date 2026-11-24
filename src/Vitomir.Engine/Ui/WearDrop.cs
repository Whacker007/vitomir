// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A drop on a worn slot of the pack (the drop's swap arm, the weargrid's overlap count
// and the placing), read and built — a comparison with 1.1's first gap.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **What a drop on a worn slot comes to**, read to the end.
///
/// <para>**The count** (the weargrid's overlap count). A weargrid holds
/// the **item thing** it shows, and its
/// placing writes a two-handed item (item flag <c>0x8000</c>) into its linked
/// grid's too when that is empty — so with a two-hander worn **both weapon grids hold
/// it**. For a carried item that is not two-handed the count is only whether the target grid holds
/// anything (0 or 1); the link is never asked. For a two-handed one it is 0 with both
/// grids empty, 1 with one of them holding something or both holding the same thing, and **2** when
/// they hold two different things. <see cref="InventoryHands.Displaced"/> answers the
/// same.</para>
///
/// <para>**The swap takes the displaced item off first** (the release with no
/// noise, which rebuilds the pack), and only then places and equips the carried one.
/// So **a torch or a shield dropped on the off hand while a
/// two-handed weapon is held is a swap** — the off-hand grid holds the two-hander, the count is 1,
/// the two-hander comes off onto the cursor and the torch goes on — and so is a two-hander dropped
/// on the weapon hand while only a shield or a torch is held (the grid's own is empty, so the link's
/// is taken). Vitomir equipped first and so refused both with the red cross
/// (<see cref="Utility.HandsFree"/>, asked before the occupants came off). A displaced item that
/// will not come off (its `unworn` says no) is the red cross — twice.</para>
///
/// <para>**"I can't wear that"**. When the equip still refuses — the wear
/// answering 1 — the pane's character says `vox_cannotwear` (event 0x95) when it may speak,
/// and the item stays on the cursor. The drop has already asked the use test — a
/// character who may not use the item is refused with the red cross and no word — and the count has
/// already taken the two-hand rule's cases off the weapon grids, so with the grids as the pack draws
/// them the line is met rarely: a two-hander refused for a shield or a torch the pack does not show
/// in the off hand, or a shield or a torch refused for a two-handed weapon in the hand that the weapon
/// grid does not show.</para>
/// </summary>
public static class WearDrop
{
    /// <summary>What a refusal's report adds when the line was asked for (ours: the journal's words).</summary>
    public const string Said = " — \"I can't wear that\"";

    /// <summary>
    /// `vox_cannotwear` (voice line 0x95) from the pane's character, when it may start a
    /// spoken line.
    /// </summary>
    public static void CannotWear(WorldThing who)
    {
        if (Audio.Noise.CanSpeak(who)) Audio.Noise.Foley(who, Audio.FoleyEvent.VoxCannotWear);
    }

    /// <summary>
    /// Whether a refused equip is the wear's own **1** — the answer the line is played for: the item
    /// may not be used or the two-hand rule (the use test's pair of tests) — rather than a
    /// refusal of Vitomir's own making (a wrong slot, a missing actor, the item already there).
    /// </summary>
    public static bool EquipRefused(EquipResult got) => got == EquipResult.Restricted;
}
