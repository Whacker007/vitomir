// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The model that stands in for a character's own body: its armour, or its body with the head off.
namespace Vitomir.Engine.World;

/// <summary>
/// **A character's body can be replaced by another model** — the body swap, the one
/// routine behind three different things:
/// <list type="bullet">
/// <item>**armour** — `SetArmor` passes the armour item's <c>ARMOR_MODEL</c> when it
/// changes, or nothing when it comes off: `christof` in `plate` is drawn as `christof_plate`;</item>
/// <item>**a script's own body** — `SetActorNoItemArmor(suffix)` passes its string
/// straight through;</item>
/// <item>**a beheading** — the beheading passes `nohead`: `Skeleton_nohead`, `zombu_nohead`
/// (<see cref="Rpg.Beheading"/>).</item>
/// </list>
/// With a suffix it builds <c>&lt;model&gt;_&lt;suffix&gt;</c> from the name of the model the character
/// is drawn with (the header's own name, in a 512-byte buffer — no 32-byte cut
/// here), loads it into **sub-model slot 0** and, if that worked, sets bit 8 of the
/// character's **own** instance flags — the bit that stops an instance drawing its own geometry, the
/// same bit that hides a weapon — and clears it if it did not. With no suffix it empties
/// slot 0 and clears the bit. So the new model stands in the old one's place, posed by the old one's
/// skeleton, and a name that is not there leaves the character as it was.
///
/// **A player's portrait head is done the same way** (type 7 only): the name is built from the
/// **body's** model name again, with `"_%sH"` — `christof` in `plate` is
/// `christof_plateH` — and loaded into the portrait head's own slot 0,
/// whose own geometry is hidden while it holds one; no suffix empties it. **37** such heads ship —
/// chain, leather, plate and the modern `upgrade` jackets for Christof, Wilhem, Serena, Erik, Samuel
/// and the generic bodies (`Christof_PlateH`, `wilhem_chainH`, `christof_upgradeH`, …) — on the
/// head's fifteen-bone skeleton; where there is none, and for `nohead`, the name does not load and the
/// portrait keeps its own face. Built as <see cref="Actor.PortraitModel"/>, drawn by the
/// status bar's heads (<c>Render.HeadsPass</c>) in the plain head's place.
///
/// Vitomir keeps the name on the actor (<see cref="Actor.BodyModel"/>); the scene mesh loads it into
/// slot 0 when it next looks (<c>Scene.SceneMesh.Rearm</c>, every frame) and marks the body's own
/// instance hidden while slot 0 holds a model (<see cref="NamedShapes.State.BodyHidden"/>).
/// </summary>
public static class BodyModels
{
    /// <summary>What a beheading asks for.</summary>
    public const string NoHead = "nohead";

    /// <summary>
    /// The body as <c>&lt;model&gt;_&lt;suffix&gt;</c>, or the character's own with an empty
    /// or null <paramref name="suffix"/>.
    /// </summary>
    public static void Set(WorldThing thing, string? suffix)
    {
        if (thing.Actor is not { } actor) return;
        actor.BodyModel = string.IsNullOrEmpty(suffix) ? "" : Name(thing.Model, suffix);
        if (thing.Type == ThingType.Player)
            actor.PortraitModel = string.IsNullOrEmpty(suffix) ? "" : Portrait(thing.Model, suffix);
    }

    /// <summary>The portrait head's stand-in: the body model's name, `"_%sH"`.</summary>
    public static string Portrait(string model, string suffix) => Name(model, suffix) + "H";

    /// <summary>The model's own name, an underscore and the suffix (`"_%s"`).</summary>
    public static string Name(string model, string suffix) => HeldModel.Stem(model) + "_" + HeldModel.Stem(suffix);
}
