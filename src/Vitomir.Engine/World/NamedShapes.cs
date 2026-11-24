// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Shapes in a model that the engine turns on and off by name: the arrow on a bow, the fists.
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// **A model's meshes can be hidden one at a time, by name, and the choice belongs to the instance.**
/// One routine hides and another shows, both `(instance, const char* name)`; they are the same
/// routine with the one bit written the other way.
/// Each:
/// <list type="number">
/// <item>walks the instance's model's mesh-name table (32 bytes a name, the count in a
/// **byte** of the header) with <c>strcmpi</c> — the whole name, case ignored — and takes the first
/// match;</item>
/// <item>on a miss, asks each of the instance's four sub-model slots in turn (the slot search, which does
/// the same search on the slot's model and then on *its* slots) and takes the first index any of
/// them answers; with no answer anywhere it returns 0 and changes nothing;</item>
/// <item>then, if the index is no more than the instance's **own** mesh count, walks the instance's
/// **own** mesh groups (28 bytes each) and sets (hide) or clears (show) bit 0 of the
/// instance's per-group word (one word a group) for every group whose
/// <c>MeshNum</c> is that index, and returns 1.</item>
/// </list>
///
/// **Step 3 is 1.1's, oddity and all: an index found in a sub-model is applied to the carrier.**
/// The routine keeps the carrier's header throughout and
/// writes into the carrier's own words, so a name that only the sword
/// in the hand has hides whichever of the *body's* meshes happens to share its number, and hides
/// nothing in the sword. No shipped call reaches that arm with a name the body lacks but a slot
/// holds — the arrow is asked of the bow's own instance, and every fist call is asked of a model
/// that has fists or of none — so it is kept as 1.1 has it and pinned by a test rather than seen.
///
/// A fresh instance starts with every word zero (the whole allocation is cleared), so
/// **everything is shown until something hides it**, and filling a sub-model slot
/// always makes a fresh instance: equipping a bow shows its arrow.
///
/// Vitomir keeps the same state as a set of hidden mesh numbers per instance — the body and each of
/// its four slots — on the thing (<see cref="WorldThing.Shapes"/>); since every write flips every
/// group of one mesh number together, that is the same information as the per-group bits. The
/// scene mesh records which model is in which slot as it fills them (<c>Scene.SceneMesh</c>), and
/// the renderer leaves out a batch whose mesh number is hidden on its instance
/// (<see cref="IsHidden"/>).
/// </summary>
public static class NamedShapes
{
    /// <summary>The character's (or thing's) own model rather than one of its sub-model slots.</summary>
    public const int Body = -1;

    /// <summary>
    /// The arrow on a bow or crossbow — a mesh of the held model itself (all 16 bow and crossbow
    /// models have one). The engine holds the string twice.
    /// </summary>
    public const string Arrow = "arrowmeshshape";

    /// <summary>The fists, which a claw or the talking hands replace.</summary>
    public const string FistLeft = "fist_lshape", FistRight = "fist_rshape";

    /// <summary>One model instance's worth: the model in it and which of its mesh numbers are hidden.</summary>
    public sealed class Instance
    {
        /// <summary>The model this instance draws, or null for an empty slot.</summary>
        public NodModel? Model { get; internal set; }
        /// <summary>What the slot was filled for — the weapon, the shield — so a refill is noticed.</summary>
        internal object? Owner { get; set; }
        /// <summary>Mesh numbers whose groups are hidden (bit 0 set).</summary>
        internal HashSet<int> Hidden { get; } = [];

        /// <summary>Whether mesh <paramref name="mesh"/> is hidden on this instance.</summary>
        public bool Hides(int mesh) => Hidden.Count > 0 && Hidden.Contains(mesh);

        /// <summary>A fresh instance of <paramref name="model"/>: every word zero, so everything shown.</summary>
        internal void Reset(NodModel? model, object? owner)
        {
            Model = model;
            Owner = owner;
            Hidden.Clear();
        }
    }

    /// <summary>A thing's instances: its own and its four sub-model slots.</summary>
    public sealed class State
    {
        public Instance Body { get; } = new();
        public Instance[] Slots { get; } = [new(), new(), new(), new()];

        /// <summary>
        /// Bit 8 of the thing's own instance flags: its own geometry is not drawn, because a model in
        /// slot 0 stands in for it — armour, or a headless body (<see cref="BodyModels"/>).
        /// </summary>
        public bool BodyHidden { get; set; }

        /// <summary>The instance a slot number names, <see cref="NamedShapes.Body"/> for the thing's own.</summary>
        public Instance? this[int slot] =>
            slot == NamedShapes.Body ? Body : slot >= 0 && slot < Slots.Length ? Slots[slot] : null;
    }

    /// <summary>
    /// Hide the named shape on the thing's own instance, or on one of its sub-model
    /// slots (<see cref="HeldModel.WeaponSlot"/> for the bow's arrow). False — and nothing changed —
    /// when the instance is empty or no model the search reaches has the name.
    /// </summary>
    public static bool Hide(WorldThing thing, string name, int slot = Body) => Set(thing, name, slot, hidden: true);

    /// <summary>The same, clearing the bit instead — show it.</summary>
    public static bool Show(WorldThing thing, string name, int slot = Body) => Set(thing, name, slot, hidden: false);

    /// <summary>
    /// Whether a batch of mesh <paramref name="mesh"/> on instance <paramref name="slot"/> is left
    /// out of the draw — its shape hidden, or the whole of the thing's own instance hidden behind a
    /// body model (<see cref="State.BodyHidden"/>). A thing nothing has ever hidden anything on costs
    /// one null test.
    /// </summary>
    public static bool IsHidden(WorldThing thing, int slot, int mesh) =>
        thing.Shapes is { } state
        && (slot == Body && state.BodyHidden || mesh >= 0 && state[slot] is { } instance && instance.Hides(mesh));

    /// <summary>
    /// Whether the named shape is hidden on that instance right now — for tests and diagnostics.
    /// Answers by the same search the two writers use, so it is false for a name the search misses.
    /// </summary>
    public static bool IsHidden(WorldThing thing, string name, int slot = Body)
    {
        if (thing.Shapes is not { } state || state[slot] is not { Model: not null } instance) return false;
        int index = Find(instance, slot == Body ? state : null, name);
        return index >= 0 && instance.Hides(index);
    }

    /// <summary>
    /// The thing's own model, as its geometry goes in. A different model is a different instance
    /// (`SetModel`, builds a new one), so what was hidden on the old one is forgotten.
    /// </summary>
    public static void Model(WorldThing thing, NodModel? model)
    {
        var state = thing.Shapes ??= new State();
        if (!ReferenceEquals(state.Body.Model, model)) state.Body.Reset(model, null);
    }

    /// <summary>
    /// Whether slot <paramref name="slot"/> was last filled for <paramref name="owner"/> — the cheap
    /// test a caller makes every frame before building a model name for <see cref="Fill"/>.
    /// </summary>
    public static bool Holds(WorldThing thing, int slot, object? owner) =>
        thing.Shapes is { } state && state[slot] is { } instance && ReferenceEquals(instance.Owner, owner)
        && (owner is not null || instance.Model is null);

    /// <summary>
    /// A sub-model slot filled or emptied for <paramref name="owner"/>.
    /// **Only a change of owner makes a fresh instance** — `SetWeapon` reloads the slot
    /// only when the weapon changes — so hiding a weapon's picture (`EnableActorWeapon`, the flag at
    /// the instance) and showing it again keeps whatever its shapes were.
    /// </summary>
    public static void Fill(WorldThing thing, int slot, object? owner, NodModel? model)
    {
        var state = thing.Shapes ??= new State();
        if (state[slot] is not { } instance || slot == Body) return;
        if (ReferenceEquals(instance.Owner, owner) && ReferenceEquals(instance.Model, model)) return;
        instance.Reset(model, owner);
    }

    /// <summary>
    /// **The fists, on and off** — the same four calls in three places (one hides, one
    /// shows, one does either; and twice more in two routines nothing calls):
    /// <see cref="FistLeft"/> and <see cref="FistRight"/> on the thing's own instance, and then, if
    /// sub-model slot 0 is filled — the armour that stands in for the body — on that too.
    /// </summary>
    public static void Fists(WorldThing thing, bool shown)
    {
        if (thing.Shapes is not { Body.Model: not null } state) return;           // pad9 == 0
        Set(thing, FistLeft, Body, !shown);
        Set(thing, FistRight, Body, !shown);
        if (state.Slots[0].Model is null) return;
        Set(thing, FistLeft, 0, !shown);
        Set(thing, FistRight, 0, !shown);
    }

    static bool Set(WorldThing thing, string name, int slot, bool hidden)
    {
        if (thing.Shapes is not { } state || state[slot] is not { Model: { } model } instance) return false;
        int index = Find(instance, slot == Body ? state : null, name);
        if (index < 0) return false;
        // The index is checked against the carrier's own count — `<=`, not `<` — and a
        // bigger one (only possible from a slot's table) changes nothing but still answers 1.
        if (index > MeshCount(model)) return true;
        if (hidden) instance.Hidden.Add(index); else instance.Hidden.Remove(index);
        return true;
    }

    /// <summary>
    /// The name's index: in the instance's own table first, then in each filled slot in turn
    /// (the slot search — which recurses into the slot's own slots; a slot's slots are always empty here).
    /// </summary>
    static int Find(Instance instance, State? slots, string name)
    {
        if (instance.Model is { } model)
        {
            int count = MeshCount(model);
            for (int i = 0; i < count; i++)
                if (string.Equals(model.MeshNames[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        if (slots is null) return -1;
        foreach (var sub in slots.Slots)
        {
            if (sub.Model is null) continue;
            int found = Find(sub, null, name);
            if (found >= 0) return found;
        }
        return -1;
    }

    /// <summary>The count the engine walks: a byte of the header, so at most 255.</summary>
    static int MeshCount(NodModel model) => Math.Min(model.MeshNames.Length, 0xff);
}
