// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The developer's damage readout, which the console's `combatmessages`
// turns on (bit 1 of the debug word).
using System.Globalization;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **Every blow's numbers in the message box**, while the console's <c>combatmessages</c> is on
/// (called by the melee hit and the shot's, straight after the
/// damage). Nothing for a blow that rolled nothing. A shot or an explosion is put down to whoever
/// made it, and nothing is said when they are gone.
///
/// A character's blow reads <c>RPG_HIT</c> ("%A damages %T"); anything else's <c>RPG_NOCAUSEHIT</c>
/// ("%T got hurt"). Then the sum: with a multiplier, <c>" (%.1f * %.0f) - %.0f = %.0f)"</c> —
/// the rolled damage over the multiplier, the multiplier, what the soak and the scaling took, what
/// was taken; without one, <c>" (%.0f) - %.0f = %.0f)"</c>. The closing bracket with no opening
/// one to match is 1.1's.
/// </summary>
public static class CombatMessages
{
    /// <summary>Bit 1 of the debug word (`combatmessages`). Off unless somebody turns it on.</summary>
    public static bool On { get; set; }

    /// <summary>The string table the two lines and the names are looked up in; the keys themselves without one.</summary>
    public static Ui.Strings? Text { get; set; }

    /// <summary>The line for one blow, or null when 1.1 would say nothing.</summary>
    /// <param name="raw">What was rolled.</param>
    /// <param name="taken">What the damage came to (the damage's answer).</param>
    /// <param name="multiplier">The blow's doublings: 0, 2 or 4 (<see cref="Blow.Multiplier"/>).</param>
    public static string? Line(WorldThing? by, WorldThing victim, float raw, float taken, float multiplier)
    {
        if (raw <= 0f) return null;
        if (by is { Type: ThingType.Projectile or ThingType.Explosion })
            by = by.SpawnedBy != 0 ? by.Place?.Thing(by.SpawnedBy) : null;
        if (by is null) return null;
        string Find(string key) => Text?[key] ?? key;
        string line = by.Type is ThingType.Actor or ThingType.Player
            ? Ui.Strings.Format(Find, "RPG_HIT", "%A" + Ui.Rollover.NameOf(by, Text) + "%T" + Ui.Rollover.NameOf(victim, Text))
            : Ui.Strings.Format(Find, "RPG_NOCAUSEHIT", "%T" + Ui.Rollover.NameOf(victim, Text));
        return line + (multiplier != 0f
            ? string.Create(CultureInfo.InvariantCulture, $" ({raw / multiplier:0.0} * {multiplier:0}) - {raw - taken:0} = {taken:0})")
            : string.Create(CultureInfo.InvariantCulture, $" ({raw:0}) - {raw - taken:0} = {taken:0})"));
    }

    /// <summary>
    /// The colour the line is posted in: **cyan**. 1.1 uses <c>0x0000FFFF</c> — blue
    /// and green full, red and alpha nothing — and the box's text blit builds its 16-bit colour from
    /// the red, green and blue bytes with the alpha bit forced on, so it draws opaque cyan.
    /// Earlier, Vitomir posted it in the box's own grey. Ours: written opaque, since Vitomir's box
    /// honours the alpha byte.
    /// </summary>
    public const uint Colour = 0xFF00FFFFu;

    /// <summary>The blow's line into the message box, when the switch is on.</summary>
    public static void Blow(WorldThing? by, WorldThing victim, float raw, float taken, float multiplier)
    {
        if (!On) return;
        if (Line(by, victim, raw, taken, multiplier) is { } line) victim.Place?.Session?.Messages.Add(line, Colour);
    }
}
