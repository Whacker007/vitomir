// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host;

// Ours: the bow's lines for the program's log; the Tell calls BowLines (one line there).
public sealed partial class Session
{
    /// <summary>
    /// This tick's bow events as the log's lines — an arrow nocked, a draw begun, an arrow loosed (its draw, speed,
    /// direction, spread and aim), an arrow spent (what ended its flight) and a draw let down. Nothing in a world without a bow.
    /// </summary>
    public void BowLines(List<string> into)
    {
        if (!world.TryService<EventQueue<ArrowLoosed>>(out var loosed)) return;
        var ic = CultureInfo.InvariantCulture;
        string Who(EntityId e)
        {
            if (e.IsNone) return "nobody";
            string name = NameOf(e.Packed) ?? "someone";
            return name.EndsWith(')') ? string.Create(ic, $"{name[..^1]}, e{e.Index})") : string.Create(ic, $"{name} (e{e.Index})");
        }
        string t = string.Create(ic, $"{world.Seconds:0.00} s: bow: ");
        foreach (var n in world.Events<ArrowNocked>().ThisTick)
            into.Add(string.Create(ic, $"{t}{Who(n.Archer)} nocks arrow #{n.Shot} ({n.ArrowsLeft} left in the quiver)"));
        foreach (var d in world.Events<DrawBegan>().ThisTick)
            into.Add(string.Create(ic, $"{t}{Who(d.Archer)} draws #{d.Shot} to {d.DrawToShare:0.00} of full draw"));
        foreach (var l in loosed.ThisTick)
            into.Add(string.Create(ic, $"{t}{Who(l.Archer)} looses #{l.Shot} at {l.DrawShare:0.00} of full draw: {l.SpeedMetresPerSecond:0.0} m/s, yaw {l.YawDegrees:0.0}°, pitch {l.PitchDegrees:0.00}° (spread {l.SpreadDegrees:0.0}°){(l.Target.IsNone ? "" : $", aimed at {Who(l.Target)}'s {BodyRegions.Name(l.AimedRegion)}")}; {l.ArrowsLeft} left"));
        foreach (var s in world.Events<ArrowSpent>().ThisTick)
        {
            string end = s.End switch
            {
                ArrowEnd.Met => s.Met switch
                {
                    ContactMet.Body => $"met {Who(s.Target)}'s {s.Site} (contact {s.ContactId})",
                    ContactMet.Shield => $"met {Who(s.Target)}'s shield (contact {s.ContactId})",
                    ContactMet.Weapon => $"met {Who(s.Target)}'s blade (contact {s.ContactId})",
                    _ => $"met the level (contact {s.ContactId})",
                },
                ArrowEnd.Fell => "fell out of the level",
                _ => "was given up in flight",
            };
            into.Add(string.Create(ic, $"{t}{Who(s.Archer)}'s arrow #{s.Shot} {end} after {s.FlightSeconds:0.00} s and {s.DistanceMetres:0.0} m, at {s.SpeedMetresPerSecond:0.0} m/s"));
        }
        foreach (var c in world.Events<DrawCancelled>().ThisTick)
            into.Add(string.Create(ic, $"{t}{Who(c.Archer)} lets #{c.Shot} down at {c.DrawShare:0.00} of full draw ({c.Cause switch { DrawCancelCause.LetDown => "let down", DrawCancelCause.AimLetGo => "aim let go", DrawCancelCause.Unequipped => "the bow put away", DrawCancelCause.Hurt => "hurt", DrawCancelCause.TooLittle => "too little a draw to loose", _ => "stopped" }})"));
        // The player's swap, and a shield raised against a drawn bow.
        foreach (var w in world.Events<WeaponSwap>().ThisTick)
            into.Add(w.Ended
                ? string.Create(ic, $"{t}{Who(w.Archer)} has {(w.ToRanged ? "the bow" : "the sword")} in hand")
                : string.Create(ic, $"{t}{Who(w.Archer)} swaps to {(w.ToRanged ? "the bow" : "the sword")} ({w.Seconds:0.##} s)"));
        foreach (var sh in world.Events<ShieldAgainstBow>().ThisTick)
            into.Add(sh.Up
                ? string.Create(ic, $"{t}{Who(sh.Defender)} raises his shield against {Who(sh.Archer)}'s drawn bow ({sh.DistanceMetres:0.0} m)")
                : string.Create(ic, $"{t}{Who(sh.Defender)} lowers his shield (the bow no longer drawn at him)"));
        // An archer brain's aim for each shot (its error, seen in where the bow points) and its sword drawn up close.
        if (world.TryService<EventQueue<ArcherAimed>>(out var aimed))
        {
            foreach (var a in aimed.ThisTick)
                into.Add(string.Create(ic, $"{t}{Who(a.Archer)} aims #{a.Shot} at {Who(a.Foe)}'s chest from {a.DistanceMetres:0.0} m, off by {a.ErrorYawDegrees:+0.00;-0.00}° across and {a.ErrorPitchDegrees:+0.00;-0.00}° up (its Range's spread {a.SpreadDegrees:0.00}°)"));
            foreach (var a in world.Events<ArcherToSword>().ThisTick)
                into.Add(string.Create(ic, $"{t}{Who(a.Archer)} puts the bow away and draws his sword ({(a.OutOfArrows ? "out of arrows" : $"{Who(a.Foe)} within {a.DistanceMetres:0.0} m")})"));
        }
    }
}
