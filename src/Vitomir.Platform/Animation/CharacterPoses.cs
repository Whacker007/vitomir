// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Runtime.CompilerServices;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation;

/// <summary>
/// The characters' poses as the simulation stands now (ours): each one's bone matrices in model space and
/// its skin matrices, worked out from its <see cref="CharacterAnimation"/> the first time they are asked for
/// after the animation last moved, and kept until it moves again. A pose is a pure function of the
/// component (<see cref="PoseEvaluator"/>), so working it out on demand is posing in the simulation:
/// the hit volumes that will ride on it and the picture drawn of it come from the
/// same numbers, and a character nobody looks at costs nothing. A service, not digested (it is derived);
/// the buffers are made once per entity slot and reused, so asking allocates nothing after the first time.
/// <para>
/// Whatever changes a <see cref="CharacterAnimation"/> outside the <see cref="AnimationSystem"/> calls
/// <see cref="Invalidate"/> (the system does, every tick).
/// </para>
/// </summary>
public sealed class CharacterPoses(CharacterAssets assets)
{
    readonly PoseEvaluator evaluator = new();
    Slot?[] slots = [];
    // The drawn poses of the people with the lean, the look, the head's lead or a join on them (People.PersonPresentRule): the renderer's only.
    Slot?[] drawnSlots = [];
    long version = 1;

    sealed class Slot
    {
        public Matrix4x4[] Model = [];
        public Matrix4x4[] Skin = [];
        /// <summary>The bones before the aim offset bent them, and their skin (made when first asked for), when <see cref="Bent"/>.</summary>
        public Matrix4x4[] Unbent = [], UnbentSkin = [];
        public bool Bent;
        public long UnbentSkinVersion;
        public long Version;
        public int Generation;
        public int Bones;
        /// <summary>The parts the pose was made lacking (<see cref="Combat.MissingParts.Bits"/>), collapsed in its skins.</summary>
        public uint Missing;
    }

    // Each model's collapse of lost parts (made the first time it is asked for), and the world's MissingParts store.
    PartCollapse?[] collapses = [];
    bool[] collapsesMade = [];
    World? missingOf;
    Store<Combat.MissingParts>? missingStore;
    // The world's people's swing (People.PersonSwings), when it has the simulator.
    World? swingsOf;
    People.PersonSwings? swings;

    /// <summary>Whether the tails carried between steps are kept out of their colliders (<see cref="People.PersonSwingSettings.KeepOutBetweenSteps"/>).</summary>
    bool KeepOut => swings?.Settings.KeepOutBetweenSteps ?? true;

    /// <summary>The entity's swing when it is simulated now (its swing bones drawn from it), else null.</summary>
    People.SwingState? Swing(World world, EntityId entity)
    {
        if (!ReferenceEquals(swingsOf, world))
        {
            swingsOf = world;
            swings = world.TryService<People.PersonSwings>(out var s) ? s : null;
        }
        return swings?.Live(entity);
    }

    /// <summary>
    /// The entity's swing has stepped, begun or stopped (<see cref="People.PersonSwingSystem"/>): its kept pose's swing bones posed again
    /// from it (the body's bones are as they were), or the pose made again when it next is asked for (no longer simulated: the skirt rule's;
    /// a part lost: the collapse made on the new bones).
    /// </summary>
    internal void Reswing(World world, EntityId entity)
    {
        if (entity.Index < drawnSlots.Length && drawnSlots[entity.Index] is { } drawnSlot) drawnSlot.Version = 0;   // the drawn pose made again
        if (entity.Index >= slots.Length || slots[entity.Index] is not { } slot || slot.Version != version || slot.Generation != entity.Generation) return;
        if (slot.Missing != 0 || Swing(world, entity) is not { } swing) { slot.Version = 0; return; }
        var skeleton = assets.ModelOf(world.Get<CharacterAnimation>(entity).Kind).Skeleton;
        swing.Rule.Pose(swing, skeleton, slot.Model.AsSpan(0, slot.Bones), slot.Skin.AsSpan(0, slot.Bones), Lift(world, entity), KeepOut);
    }

    /// <summary>
    /// How far above the model's origin (its capsule's feet) a person's body is drawn on the stairs' line (<see cref="People.PersonGround.Line"/>,
    /// as the rig lays it on the hips), 0 off it: the level its swing is simulated on (<see cref="People.SwingRule.Step"/>).
    /// </summary>
    internal static float Lift(World world, EntityId entity) => Ground(world, entity) is { Stairs: true } g ? g.Line : 0f;

    /// <summary>Marks every kept pose out of date (after the animation states changed).</summary>
    public void Invalidate() => version++;

    /// <summary>The collapse of lost parts for <paramref name="model"/> (<see cref="PartCollapse"/>), or null when its skeleton has no anatomy table.</summary>
    public PartCollapse? Collapse(int model)
    {
        if (model >= collapsesMade.Length)
        {
            int size = Math.Max(model + 1, collapsesMade.Length * 2);
            Array.Resize(ref collapses, size);
            Array.Resize(ref collapsesMade, size);
        }
        if (!collapsesMade[model])
        {
            collapses[model] = PartCollapse.For(assets.Model(model).Skeleton);
            collapsesMade[model] = true;
        }
        return collapses[model];
    }

    /// <summary>What the entity lacks (<see cref="Combat.MissingParts.Bits"/>; 0 whole, or in a world without the store).</summary>
    public uint Missing(World world, EntityId entity)
    {
        if (!ReferenceEquals(missingOf, world))
        {
            missingOf = world;
            missingStore = world.TryStore<Combat.MissingParts>(out var s) ? s : null;
        }
        if (missingStore is null) return 0;
        ref var m = ref missingStore.Find(entity);
        return Unsafe.IsNullRef(ref m) ? 0 : m.Bits;
    }

    /// <summary>Whether the entity's <paramref name="bone"/> is gone with a part it lacks (<see cref="PartCollapse.Gone"/>).</summary>
    public bool Gone(World world, EntityId entity, int bone)
    {
        uint missing = Missing(world, entity);
        if (missing == 0) return false;
        ref var state = ref world.Store<CharacterAnimation>().Find(entity);
        return !Unsafe.IsNullRef(ref state) && Collapse(assets.Kind(state.Kind).Model) is { } c && c.Gone(missing, bone);
    }

    void Collapsed(World world, EntityId entity, uint missing, ReadOnlySpan<Matrix4x4> bones, Span<Matrix4x4> skin)
    {
        if (missing == 0) return;
        Collapse(assets.Kind(world.Get<CharacterAnimation>(entity).Kind).Model)?.Apply(missing, bones, skin);
    }

    /// <summary>
    /// The entity's skin matrices as they would be without the aim offset's bend: the same as <see cref="Skin"/> when
    /// it is not bent. The first-person eye rides this head (the steady eye), so the view does not tip with a swing.
    /// </summary>
    public ReadOnlySpan<Matrix4x4> UnbentSkin(World world, EntityId entity)
    {
        var slot = Ensure(world, entity);
        if (!slot.Bent) return slot.Skin.AsSpan(0, slot.Bones);
        if (slot.UnbentSkinVersion != slot.Version)
        {
            var skeleton = assets.ModelOf(world.Get<CharacterAnimation>(entity).Kind).Skeleton;
            if (slot.UnbentSkin.Length < slot.Bones) slot.UnbentSkin = new Matrix4x4[slot.Unbent.Length];
            PoseEvaluator.ToSkin(skeleton, slot.Unbent, slot.UnbentSkin);
            Collapsed(world, entity, slot.Missing, slot.Unbent.AsSpan(0, slot.Bones), slot.UnbentSkin.AsSpan(0, slot.Bones));
            slot.UnbentSkinVersion = slot.Version;
        }
        return slot.UnbentSkin.AsSpan(0, slot.Bones);
    }

    /// <summary>
    /// The skin matrices of the entity's held model <paramref name="index"/> (<see cref="CharacterKind.Held"/>) on its pose
    /// (1.1's <c>PoseCarried</c>, <see cref="HeldItems.Skin"/>), into <paramref name="into"/>; answers how many.
    /// Never collapsed: in first person the held weapon is drawn.
    /// </summary>
    public int CopyHeldSkin(World world, EntityId entity, int index, Span<Matrix4x4> into)
    {
        var held = assets.Kind(world.Get<CharacterAnimation>(entity).Kind).Held;
        if ((uint)index >= (uint)held.Count) return 0;
        var carried = held[index].Model;
        int n = Math.Min(into.Length, carried.Skeleton.Count);
        var bones = Bones(world, entity);
        HeldItems.Skin(carried, bones, into[..n]);
        Collapsed(world, entity, Missing(world, entity), bones, into[..n]);   // a thing held in a lost part goes with it (dropped)
        return n;
    }

    /// <summary>
    /// The entity's held model <paramref name="index"/> as the renderer draws it: the held model, the holder's world
    /// matrix, and its skin on the holder's pose; <paramref name="reuse"/> filled in place when it is the same model's.
    /// </summary>
    public Presentation.DrawnCharacter DrawHeld(World world, EntityId entity, int index, Matrix4x4 place, Presentation.DrawnCharacter? reuse = null)
    {
        var model = assets.Kind(world.Get<CharacterAnimation>(entity).Kind).Held[index].Model;
        var drawn = reuse is { } r && ReferenceEquals(r.Model, model) && r.Entity == entity.Packed && r.Skin.Length == model.Skeleton.Count
            ? r
            : new Presentation.DrawnCharacter { Model = model, World = place, Skin = new Matrix4x4[model.Skeleton.Count], Entity = entity.Packed };
        drawn.World = place;
        CopyHeldSkin(world, entity, index, drawn.Skin);
        drawn.ShadowSkin = null;
        return drawn;
    }

    /// <summary>The entity's skin matrices (one per bone of its model: inverse bind × posed bone), for drawing and for hit volumes.</summary>
    public ReadOnlySpan<Matrix4x4> Skin(World world, EntityId entity)
    {
        var slot = Ensure(world, entity);
        return slot.Skin.AsSpan(0, slot.Bones);
    }

    /// <summary>The entity's bones in its model space (metres, Z up), parents first: where a joint is, for an attachment or a hit volume.</summary>
    public ReadOnlySpan<Matrix4x4> Bones(World world, EntityId entity)
    {
        var slot = Ensure(world, entity);
        return slot.Model.AsSpan(0, slot.Bones);
    }

    /// <summary>The entity's bones as drawn (the lean, the look, the head's lead and a join on them, when it has them; else <see cref="Bones"/>).</summary>
    public ReadOnlySpan<Matrix4x4> DrawnBones(World world, EntityId entity)
    {
        var present = People.PersonPresentRule.Of(world, entity);
        if (!present.On && !present.Join.On) return Bones(world, entity);
        var slot = Ensure(world, entity, present);
        return slot.Model.AsSpan(0, slot.Bones);
    }

    /// <summary>
    /// Copies the entity's skin matrices into <paramref name="into"/> with its head collapsed when
    /// <paramref name="firstPerson"/> (the viewer's own body in first person; <see cref="HeadCollapse"/>), and
    /// answers how many were written.
    /// </summary>
    public int CopySkin(World world, EntityId entity, Span<Matrix4x4> into, bool firstPerson = false, bool unbent = false) =>
        CopySkin(world, entity, into, firstPerson, unbent, drawn: false);

    /// <summary>As the overload before; <paramref name="drawn"/>: the pose as drawn (with the lean, the look, the head's lead and a join on it, when it has them).</summary>
    int CopySkin(World world, EntityId entity, Span<Matrix4x4> into, bool firstPerson, bool unbent, bool drawn)
    {
        var present = drawn ? People.PersonPresentRule.Of(world, entity) : default;
        ReadOnlySpan<Matrix4x4> skin;
        if (present.On || present.Join.On) { var d = Ensure(world, entity, present); skin = d.Skin.AsSpan(0, d.Bones); }
        else skin = unbent ? UnbentSkin(world, entity) : Skin(world, entity);
        skin.CopyTo(into);
        if (firstPerson && assets.Kind(world.Get<CharacterAnimation>(entity).Kind).Head is { } head) head.Apply(into[..skin.Length]);
        return skin.Length;
    }

    /// <summary>
    /// The entity as the renderer draws it (<see cref="Presentation.DrawnCharacter"/>): its model, the world
    /// matrix given (<see cref="CharacterKind.Placement"/> makes one from the body's feet and yaw), and its skin
    /// matrices, its head and neck collapsed when <paramref name="firstPerson"/> (and then the whole body's skin for its
    /// shadow, <see cref="Presentation.DrawnCharacter.ShadowSkin"/>). <paramref name="reuse"/>, when it is
    /// this entity's with the same model, is filled in place rather than a new one made.
    /// </summary>
    public Presentation.DrawnCharacter Draw(World world, EntityId entity, Matrix4x4 place, bool firstPerson = false, Presentation.DrawnCharacter? reuse = null)
    {
        var model = assets.ModelOf(world.Get<CharacterAnimation>(entity).Kind);
        var drawn = reuse is { } r && ReferenceEquals(r.Model, model) && r.Entity == entity.Packed && r.Skin.Length == model.Skeleton.Count
            ? r
            : new Presentation.DrawnCharacter { Model = model, World = place, Skin = new Matrix4x4[model.Skeleton.Count], Entity = entity.Packed };
        drawn.World = place;
        CopySkin(world, entity, drawn.Skin, firstPerson, unbent: false, drawn: true);   // as drawn
        // What first person hides is hidden from the picture only; the shadow is cast by the whole body.
        if (firstPerson && assets.Kind(world.Get<CharacterAnimation>(entity).Kind).Head is not null)
        {
            if (drawn.ShadowSkin is not { } whole || whole.Length != drawn.Skin.Length) drawn.ShadowSkin = whole = new Matrix4x4[drawn.Skin.Length];
            CopySkin(world, entity, whole, firstPerson: false, unbent: false, drawn: true);
        }
        else drawn.ShadowSkin = null;
        return drawn;
    }

    /// <summary>A person's turn as a chain (<see cref="People.PersonTurn"/>), or none (a world without the store, a body without it).</summary>
    internal static People.PersonTurn Chain(World world, EntityId entity) =>
        world.TryStore<People.PersonTurn>(out var chains) && chains.Has(entity) ? chains.Get(entity) : default;

    /// <summary>A person's planted feet (<see cref="People.PersonFeet"/>), or none (a world without the store, a body without it).</summary>
    internal static People.PersonFeet Feet(World world, EntityId entity) =>
        world.TryStore<People.PersonFeet>(out var feet) && feet.Has(entity) ? feet.Get(entity) : default;

    /// <summary>A person on uneven ground (<see cref="People.PersonGround"/>), or none (a world without the store, a body without it).</summary>
    internal static People.PersonGround Ground(World world, EntityId entity) =>
        world.TryStore<People.PersonGround>(out var grounds) && grounds.Has(entity) ? grounds.Get(entity) : default;

    /// <summary>A person on a flight of stairs (<see cref="People.PersonFlight"/>), or none (a world without the store, a body without it).</summary>
    internal static People.PersonFlight Flight(World world, EntityId entity) =>
        world.TryStore<People.PersonFlight>(out var flights) && flights.Has(entity) ? flights.Get(entity) : default;

    Slot Ensure(World world, EntityId entity) => Ensure(world, entity, default);

    /// <summary>
    /// The pose with <paramref name="present"/> laid on (the lean, the look, the head's lead, a join: <see cref="People.PersonPresentRule"/>), kept apart from the
    /// simulation's (<see cref="Bones"/>, <see cref="Skin"/>), which is made without them; none, the simulation's itself.
    /// </summary>
    Slot Ensure(World world, EntityId entity, People.PersonPresentPose present)
    {
        ref var state = ref world.Store<CharacterAnimation>().Find(entity);
        if (Unsafe.IsNullRef(ref state)) throw new InvalidOperationException($"{entity} has no CharacterAnimation");
        ref var of = ref present.On || present.Join.On ? ref drawnSlots : ref slots;
        if (entity.Index >= of.Length) Array.Resize(ref of, Math.Max(entity.Index + 1, of.Length * 2));
        var slot = of[entity.Index] ??= new Slot();
        uint missing = Missing(world, entity);   // a part lost since the pose was made makes it again
        if (slot.Version == version && slot.Generation == entity.Generation && slot.Missing == missing) return slot;
        int n = assets.ModelOf(state.Kind).Skeleton.Count;
        if (slot.Model.Length < n) { slot.Model = new Matrix4x4[n]; slot.Skin = new Matrix4x4[n]; }
        ref var rig = ref world.Store<PoseRig>().Find(entity);
        var swing = Swing(world, entity);
        evaluator.Swung = swing is not null;
        evaluator.Hands = People.PersonHandsRule.Of(world, entity, assets, state);   // the hands on the real top
        evaluator.FootBack = People.PersonMovesSettings.FootBackOf(world, entity, assets, state);   // the braced feet for stiffer shoes
        evaluator.StillRoot = People.PersonHangSettings.StillRoot(world, assets, state)    // a corner's or the leap back's turn carried by the facing
                              || People.PersonStealthSettings.StillRoot(world, entity, assets, state);   // a squeeze's quarter turns likewise
        evaluator.Stance = People.PersonStealthRule.Of(world, entity, assets, state);   // stealth's stance
        evaluator.Present = present;   // the lean, the look, the head's lead and the join (the drawn pose's only)
        evaluator.Wall = People.PersonWallRule.Of(world, entity, assets, state);   // the limbs on the wall's holds, the splayed grip
        if (Unsafe.IsNullRef(ref rig)) { evaluator.Evaluate(assets, state, slot.Model, slot.Skin); slot.Bent = false; }
        else
        {
            // A pose bent by the aim offset keeps its unbent bones too (the first-person eye's head).
            if (rig.AimPitch != 0f && slot.Unbent.Length < n) slot.Unbent = new Matrix4x4[n];
            slot.Bent = evaluator.Evaluate(assets, state, rig, slot.Model, slot.Skin, rig.AimPitch != 0f ? slot.Unbent : default, Chain(world, entity), Feet(world, entity), Ground(world, entity), Flight(world, entity));
        }
        evaluator.Swung = false;
        evaluator.Hands = default;
        evaluator.FootBack = 0f;
        evaluator.StillRoot = false;
        evaluator.Wall = default;
        evaluator.Stance = default;
        evaluator.Present = default;
        // A person's swing bones from its simulated swing, in the skirt rule's place (after everything that moves the body).
        if (swing is not null) swing.Rule.Pose(swing, assets.ModelOf(state.Kind).Skeleton, slot.Model.AsSpan(0, n), slot.Skin.AsSpan(0, n), Lift(world, entity), KeepOut);
        // The parts the person lacks drawn collapsed (every view and the shadow: the skin itself); the bones (hit volumes,
        // held things) stay as posed, and what is gone is asked of Gone.
        slot.Missing = missing;
        Collapsed(world, entity, missing, slot.Model.AsSpan(0, n), slot.Skin.AsSpan(0, n));
        slot.UnbentSkinVersion = 0;
        slot.Bones = n;
        slot.Version = version;
        slot.Generation = entity.Generation;
        return slot;
    }
}
