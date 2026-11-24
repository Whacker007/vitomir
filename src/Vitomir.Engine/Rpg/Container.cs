// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Opening a container, which in this engine is a kind of dying.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// Chests, barrels, crates — `THING_TYPE_CONTAINER`, and the branch the use order takes for them
/// instead of clicking their script.
///
/// **Opening one is a death.** That is not a metaphor: the open marks the container spent, stops
/// it highlighting, plays <b>`MOTION_DEATHQUICK`</b> (mode 121, at rate 30 — `Codex.java`'s own
/// name for that number), and calls the script method in slot 17, which the engine's callback
/// table says is **`killed`**, not `clicked`. A barrel you smash and a chest you open
/// are the same event to this engine.
/// </summary>
public static class Container
{
    /// <summary>
    /// `THING_FLAGS`-adjacent: the container's own state word, bit 0 meaning
    /// **already opened**. The open tests it first and returns 0 — so a container cannot be opened
    /// twice, and there is no re-looting anything in this game.
    /// </summary>
    public const uint Opened = 0x1;

    /// <summary>
    /// Bit 1 of the same word: **you break this one rather than open it**. Set by the container's
    /// own setter when its `CONTAINER_HEALTH` is above zero, and read by the cursor
    /// classifier, which offers *attack* instead of *use* for it — so a barrel
    /// glows red and draws the blade, and a chest glows yellow.
    /// </summary>
    public const uint Breakable = 0x2;

    /// <summary>
    /// A blow landing on a container. An open container and an
    /// unbreakable one take nothing — both answer 0 before they look at the damage — and a
    /// breakable one loses health and **is opened when it reaches zero**, with no opener: the
    /// engine calls the open with a null second argument. Returns what health is left.
    ///
    /// So its script hears `killed(container, 0)` whoever broke it — a swing, a shot, a blast.
    /// <paramref name="by"/> is who struck it, which 1.1
    /// does not pass on; earlier, Vitomir opened it for them, and the script heard their GUID (the
    /// A comparison with 1.1).
    /// </summary>
    public static float Damage(WorldThing container, float amount, WorldThing? by = null,
                               Anim.MotionSets? motions = null)
    {
        if ((container.State & Opened) != 0 || (container.State & Breakable) == 0) return 0f;
        container.ContainerHealth -= amount;
        if (container.ContainerHealth <= 0f) Open(container, opener: null, motions);
        return container.ContainerHealth;
    }

    /// <summary>
    /// What a melee blow does to a container (in the resolve): **no to-hit roll** — a crate
    /// does not dodge — and the damage roll **plus one**. That extra point is why a barrel with a
    /// `CONTAINER_HEALTH` of 1 always breaks at the first swing, whatever the weapon rolled.
    /// </summary>
    public const float MeleeBonus = 1f;

    /// <summary><c>MOTION_DEATHQUICK</c>, which is what a container plays when it is opened.</summary>
    public const int MotionOpen = 121;
    /// <inheritdoc cref="MotionOpen"/>
    public const float OpenRate = 30f;

    /// <summary>
    /// Opens <paramref name="container"/> for <paramref name="opener"/> — or, broken open, for
    /// nobody. False when it was already open, which is the original's own first test: **one fill
    /// per container, ever**.
    ///
    /// The order is the original's: what is inside comes out first —
    /// its DROPITEM, else one roll of its class, put on the floor beside it (<see cref="Loot"/>) —
    /// so the loot is lying there before the lid moves or the barrel turns into debris. Then it is
    /// marked spent, stops glowing and makes its noise; then one of two endings; and **then its
    /// script hears `killed`, always** — with killer 0 when it was broken rather than opened
    /// (the message reads the opener only when there is one).
    /// </summary>
    public static bool Open(WorldThing container, WorldThing? opener, Anim.MotionSets? motions = null)
    {
        if ((container.State & Opened) != 0) return false;
        // The contents. `container_barrel2` names a rat as its DROPITEM, so every one of
        // the 48 placed never gives treasure, whatever class its scene gave it.
        Loot.Drop(container);
        container.State |= Opened;
        // It stops glowing, for good — the engine sets NO HIGHLIGHT on the thing itself,
        // which is how a looted barrel stops asking to be looted again.
        container.Flags |= WorldThing.Flag.NoHighlight;
        // And it makes its noise — `activate`, the barrel breaking or the lid creaking — before
        // either ending, so a barrel that is about to be destroyed is still there to be heard.
        Audio.Noise.Foley(container, Audio.FoleyEvent.Activate);
        // **Then one of two endings**: a container with a DESTROY_TEMPLATE is destroyed
        // outright — the destroy, which is what turns a barrel into its debris and a
        // volatile barrel into an explosion — and one without plays its quick death and stays.
        // The engine's destroy only marks the thing for removal at the start of the next frame, so
        // its script still finds it when `killed` runs after; ours removes at once, so the script
        // is told first. Nothing a script can see differs.
        if (container.DestroyTemplate.Length > 0 && container.Place is { } here)
        {
            here.Killed?.Invoke(container, opener);
            here.Remove(container);
            return true;
        }
        if (motions is not null && container.Animation is { } opening)
            opening.Play(MotionOpen, motions, onMotion: true, rate: OpenRate);
        container.Place?.Killed?.Invoke(container, opener);
        return true;
    }
}
