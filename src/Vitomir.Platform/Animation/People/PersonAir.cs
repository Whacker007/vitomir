// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository's clips jumps, the fall and landings, played as its notes say
// (CONVENTIONS, Notes): the clips are the body relative to its capsule; the engine flies the capsule.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// <b>the jumps, the fall and the landings</b> on the models repository's clips (; <see cref="PersonAirClips"/>): whether they play
/// and how (<b>SANDBOX</b>, ours, the fades looked at in stills). Off (<see cref="Off"/>: <c>--air-clips-off</c>) a person leaves the ground, flies and
/// lands as before did (the stand-ins of <see cref="PeopleCompositions"/>, the rig's dip on a hard landing) and the world has no <see cref="PersonAir"/>
/// store. A tape carries them in its note <see cref="TapeNote"/>; a tape without it is the earlier build's.
/// </summary>
public sealed record PersonAirSettings
{
    /// <summary>Whether the models repository's jumps, fall and landings play (on by default; off: the earlier build's, <c>--air-clips-off</c>).</summary>
    public bool Clips { get; init; } = true;

    /// <summary>
    /// Whether the capsule leaves the ground when the jump's clip says (the player's <c>jump</c> after its 0.10 s squash; a running jump
    /// at its <c>takeoff_s</c> after the foot it leaves from came down): the press held back that long. <b>This changes the simulation</b> (the
    /// take-off 12 ticks after the press, standing): the models repository made the player's jump to the engine's numbers with that squash. False
    /// (<c>--jump-at-once</c>): the capsule leaves on the press as before and the clip is entered at its take-off (no squash drawn).
    /// </summary>
    public bool Squash { get; init; } = true;

    /// <summary>The fade into a jump's clip from what was playing (s; 0.08: shorter than the player's squash).</summary>
    public float EnterSeconds { get; init; } = 0.08f;

    /// <summary>The fade into the fall loop: a jump's clip at its end (<c>then</c> <c>fall</c>) or a step off an edge (s; 0.15).</summary>
    public float FallSeconds { get; init; } = 0.15f;

    /// <summary>The contact blend: from the air pose (the fall, a standing jump past its apex) into a landing's first frame (s; 0.06, its heels down).
    /// The air pose's feet are 3 to 4 cm over the capsule's bottom, the landing's on it: the blend takes that up.</summary>
    public float ContactSeconds { get; init; } = 0.06f;

    /// <summary>The fade into a landing from a pose that is not its first frame (s; 0.08): a running jump cut short by a step up (its reaching foot
    /// about 11 cm over the step at contact), or ended in a standing landing, or the fall's pose into a running landing.</summary>
    public float CutSeconds { get; init; } = 0.08f;

    /// <summary>Whether any is on.</summary>
    public bool Any => Clips;

    /// <summary>None.</summary>
    public static PersonAirSettings Off => new() { Clips = false, Squash = false };

    /// <summary>The tape head's note.</summary>
    public const string TapeNote = "air";

    /// <summary>The note: <c>off</c>, else <c>clips=1 squash=1 enter=0.08 fall=0.15 contact=0.06 cut=0.08</c>.</summary>
    public string Note() => !Clips ? "off" : string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"clips=1 squash={(Squash ? 1 : 0)} enter={EnterSeconds:0.####} fall={FallSeconds:0.####} contact={ContactSeconds:0.####} cut={CutSeconds:0.####}");

    /// <summary>These settings with a tape's note laid on; none (an older tape) or <c>off</c> is <see cref="Off"/>. The numbers go through
    /// the note's text, so a replay's are the recording's to the bit.</summary>
    public PersonAirSettings WithNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note) || !note.Contains('=')) return Off;
        var s = this with { Clips = true };
        foreach (var part in note.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || !float.TryParse(part[(eq + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
            s = part[..eq] switch
            {
                "clips" => s with { Clips = v != 0f }, "squash" => s with { Squash = v != 0f }, "enter" => s with { EnterSeconds = v },
                "fall" => s with { FallSeconds = v }, "contact" => s with { ContactSeconds = v }, "cut" => s with { CutSeconds = v },
                _ => s,
            };
        }
        return s.Clips ? s : Off;
    }
}

/// <summary>Where a person is in the air's clips (<see cref="PersonAir.Mode"/>).</summary>
public enum AirMode
{
    /// <summary>None of them: the gaits and the stand (and the earlier build's slots) as before.</summary>
    None,
    /// <summary>A jump's clip on the ground before its take-off (the squash, or a running jump's last step), the press held back.</summary>
    Squash,
    /// <summary>A jump's clip in the air, its time by the capsule's arc.</summary>
    Rise,
    /// <summary>The fall loop.</summary>
    Fall,
    /// <summary>A landing's clip, from the contact.</summary>
    Land,
    /// <summary>A prelanding, its last frame brought to the contact (<see cref="PeoplePrelanding"/>).</summary>
    Pre,
}

/// <summary>
/// <b>a person in the air</b> — the simulation's state (digested): the animation writes it, the body's system reads the jump's held press. Only
/// the people given one by the session have it (the player; the townsfolk on the models repository's crowd), and a world has the store only with the
/// clips on and people who have them, so the earlier builds' digest as before.
/// </summary>
public struct PersonAir
{
    /// <summary>Where it is.</summary>
    public AirMode Mode;

    /// <summary>1: the player's jump (<c>jump</c>); 2: everyone else's (<c>jump_natural</c>, the full countermovement).</summary>
    public int Curve;

    /// <summary>1 while a press of jump made on the ground is held back for the squash (<see cref="PeopleAir.Hold"/>), until it is let go.</summary>
    public int Pending;

    /// <summary>The squash's seconds left before the press is let go and the capsule leaves (the animation sets it as the clip begins).</summary>
    public float Squash;

    /// <summary>The take-off's upward speed (m/s) the drawn body's arc is timed by (√(2g·rise) of the body's jump); 0 for a fall.</summary>
    public float Speed;

    /// <summary>The last landing's drop (m: the highest the feet were in the air above where they landed).</summary>
    public float Drop;

    /// <summary>A fade under way that takes this long (s) in place of the animation's own; 0 none.</summary>
    public float Fade;

    /// <summary>The foot a running landing goes onto: 1 the left, 2 the right; 0 not known (the right: the air pose's forward foot).</summary>
    public int Foot;
}

/// <summary>
/// What the air's clips read of the body (<see cref="PeopleAir.Choose"/>): whether it is in the air from a jump it took, how high that jump
/// rises (m), the gravity it flies under (m/s²) and the drop of its last landing (m); the highest its feet have been in this flight (m, NaN not
/// known: <see cref="Body.HighestZ"/>), for the drop a prelanding foresees.
/// </summary>
public readonly record struct AirBody(bool Jumped, float Rise, float Gravity, float Fell, float Highest = float.NaN, float AirSeconds = 0f);

/// <summary>
/// <b>a person's jumps, fall and landings</b> (the models repository; the running jumps), found by name and by their notes; −1
/// for one the set lacks. Null on <see cref="PersonMotion.Air"/> for a set without them (an older set).
/// </summary>
public sealed class PersonAirClips
{
    /// <summary>The player's standing jump (<c>jump</c>: off after a squash), and everyone else's (<c>jump_natural</c>: the countermovement).</summary>
    public int Jump { get; init; } = -1;
    public int JumpNatural { get; init; } = -1;

    /// <summary>The air pose as a loop (<c>fall</c>).</summary>
    public int Fall { get; init; } = -1;

    /// <summary>The running jumps off the left and the right foot (<c>jump_run_l</c>, <c>_r</c>), −1 for none.</summary>
    public required int[] RunJumps { get; init; }

    /// <summary>The running landings onto the left and the right foot (<c>land_run_l</c>, <c>_r</c>), −1 for none.</summary>
    public required int[] RunLandings { get; init; }

    /// <summary>The standing landings (<c>land_soft</c>, <c>land_high</c> …: every clip with a <c>landing</c> note that does not run on), by their bands, lowest first.</summary>
    public required int[] Landings { get; init; }

    /// <summary>
    /// The prelandings (<c>prelanding_soft</c>, <c>prelanding_high</c>, <c>prelanding_high_player</c>: every clip with an
    /// <c>airborne.prelanding</c> note whose <c>then</c> is a standing landing of the set), in the file's order; empty.
    /// </summary>
    public int[] Prelandings { get; init; } = [];

    /// <summary>The player's standing landing (<c>landing.who</c> <c>player</c>: the <c>land_high_player</c>), −1 for none.</summary>
    public int PlayerLanding { get; init; } = -1;

    /// <summary>Every clip of the set above, for <see cref="Holds"/>.</summary>
    public required int[] All { get; init; }

    /// <summary>Whether <paramref name="clip"/> is one of them.</summary>
    public bool Holds(int clip) => clip >= 0 && Array.IndexOf(All, clip) >= 0;

    /// <summary>Whether <paramref name="clip"/> is a standing landing (the feet planted from its contact: <see cref="PersonFeetSystem"/>).</summary>
    public bool Stands(int clip) => clip >= 0 && Array.IndexOf(Landings, clip) >= 0;

    /// <summary>The standing jump for a curve (1 the player's, 2 everyone else's), the other when the set lacks it.</summary>
    public int JumpFor(int curve) => curve == 2 ? (JumpNatural >= 0 ? JumpNatural : Jump) : (Jump >= 0 ? Jump : JumpNatural);

    /// <summary>The running jump off a foot (1 left, 2 right), or −1.</summary>
    public int RunJump(int foot) => foot == 1 ? RunJumps[0] : foot == 2 ? RunJumps[1] : -1;

    /// <summary>The running landing onto a foot (1 left, 2 right; 0 the right), or the other when the set lacks it.</summary>
    public int RunLanding(int foot)
    {
        int want = foot == 1 ? RunLandings[0] : RunLandings[1], other = foot == 1 ? RunLandings[1] : RunLandings[0];
        return want >= 0 ? want : other;
    }

    /// <summary>
    /// The standing landing for a drop of <paramref name="drop"/> m: the one whose band holds it (the lower of two that share an edge); below every
    /// band the lowest, above every band the highest (beyond the safe fall the hurt group's landings are to come: until then the highest).
    /// </summary>
    public int Landing(CharacterAssets assets, float drop)
    {
        foreach (int c in Landings)
            if (Notes(assets, c)?.Landing is { } l && l.For(drop)) return c;
        if (Landings.Length == 0) return -1;
        var low = Notes(assets, Landings[0])?.Landing;
        return low is not null && drop < low.DropLow ? Landings[0] : Landings[^1];
    }

    /// <summary>
    /// The standing landing for a drop of <paramref name="drop"/> m and who lands (<paramref name="player"/>): <see cref="Landing"/>'s by the bands, then
    /// The earlier build's rule laid over it (<see cref="PeopleOff.After"/>, the same to the clip): the player lands on its own (<see cref="PlayerLanding"/>) from that
    /// clip's band's foot, everyone else never on it (the highest of everyone's whose band's foot the drop has passed).
    /// </summary>
    public int Standing(CharacterAssets assets, float drop, bool player)
    {
        int c = Landing(assets, drop);
        if (PlayerLanding < 0) return c;
        if (player)
        {
            if (c != PlayerLanding && Notes(assets, PlayerLanding)?.Landing is { } l && drop >= l.DropLow - 1e-4f) c = PlayerLanding;
        }
        else if (c == PlayerLanding)
        {
            foreach (int x in Landings)
                if (x != PlayerLanding && Notes(assets, x)?.Landing is { } l && drop >= l.DropLow - 1e-4f) c = x;
        }
        return c;
    }

    /// <summary>The prelanding whose <c>then</c> is <paramref name="landing"/> (by name), or −1.</summary>
    public int PrelandingFor(CharacterAssets assets, int landing)
    {
        if (landing < 0) return -1;
        string name = assets.Clip(landing).Name;
        foreach (int p in Prelandings)
            if (Notes(assets, p)?.Airborne?.Prelanding is { } pre && pre.Then == name) return p;
        return -1;
    }

    /// <summary>A clip's notes, or null.</summary>
    public static ClipNotes? Notes(CharacterAssets assets, int clip) => clip >= 0 && assets.Clip(clip) is INotedClip c ? c.Notes : null;

    /// <summary>The clips from a set (<paramref name="motions"/>, the first of a name the one played), or null when it lacks a jump, the fall or a standing landing.</summary>
    public static PersonAirClips? From(IReadOnlyList<INotedClip> motions, Func<string, INotedClip?> named, Func<INotedClip, int> indexOf)
    {
        int Jump(string name, bool running) => named(name) is { Loops: false, Notes.Airborne: { Jump: true } ab } c && ab.Running == running ? indexOf(c) : -1;
        int Land(string name) => named(name) is { Loops: false, Notes.Landing: { Running: true } } c ? indexOf(c) : -1;
        int fall = named("fall") is { Loops: true, Notes.Airborne: not null } f ? indexOf(f) : -1;
        var landings = motions.Where(c => !c.Loops && c.Notes.Landing is { Running: false } && ReferenceEquals(named(c.Name), c))
                              .OrderBy(c => c.Notes.Landing!.DropLow).ThenBy(c => c.Notes.Landing!.DropHigh)
                              // A clip for the player alone after everyone's in the same band: the files' order put the land_high after
                              // land_high_player, and the band's first is everyone's.
                              .ThenBy(c => c.Notes.Landing!.Who == "player" ? 1 : 0).Select(indexOf).ToArray();
        int jump = Jump("jump", false), natural = Jump("jump_natural", false);
        if (fall < 0 || landings.Length == 0 || (jump < 0 && natural < 0)) return null;
        int[] runJumps = [Jump("jump_run_l", true), Jump("jump_run_r", true)], runLandings = [Land("land_run_l"), Land("land_run_r")];
        // The prelandings, each into a standing landing of the set; the player's own standing landing.
        var standing = landings.Select(i => motions.First(c => indexOf(c) == i).Name).ToHashSet(StringComparer.Ordinal);
        int[] prelandings = [.. motions.Where(c => !c.Loops && c.Notes.Airborne?.Prelanding is { } p && p.LeadS > 0f && standing.Contains(p.Then)
                                                   && ReferenceEquals(named(c.Name), c)).Select(indexOf)];
        int player = motions.FirstOrDefault(c => !c.Loops && c.Notes.Landing is { Running: false, Who: "player" } && ReferenceEquals(named(c.Name), c)) is { } pl ? indexOf(pl) : -1;
        int[] all = [jump, natural, fall, .. runJumps, .. runLandings, .. landings, .. prelandings];
        return new PersonAirClips
        {
            Jump = jump, JumpNatural = natural, Fall = fall, RunJumps = runJumps, RunLandings = runLandings, Landings = landings,
            Prelandings = prelandings, PlayerLanding = player, All = [.. all.Where(c => c >= 0).Distinct()],
        };
    }
}

/// <summary>
/// <b>playing the jumps, the fall and the landings</b> (<see cref="AnimationSystem"/> calls it for a person with a <see cref="PersonAir"/>), the
/// models repository's notes:
/// <list type="bullet">
/// <item><b>A jump</b> from the stand or a walk plays the person's own standing jump (the player's <c>jump</c>, everyone else's <c>jump_natural</c>);
/// out of a run going ahead, the running jump off the foot the run has planted (by the run's phase: the foot that came down within the clip's
/// take-off, entered that far in; else the next to come down, from its first frame). The clip stands on the ground until its <c>takeoff_s</c>:
/// the press is held back that long (<see cref="PersonAirSettings.Squash"/>), so the capsule leaves when the clip's feet do. In the air its time
/// follows the capsule's own arc: <c>takeoff_s</c> + (1 − v/v₀)·(<c>apex_s</c> − <c>takeoff_s</c>), v the capsule's upward speed and v₀ its
/// take-off's — the clip's apex at the capsule's whatever the jump's height (a charged jump's, Potence's), its own rise only timing the drawn
/// body against the engine's arc (Celerity does not change a jump).</item>
/// <item>At its end a standing jump blends into <b>the fall</b> loop (<c>then</c> <c>fall</c>); a running jump holds its last frame until the ground
/// is met (<c>then</c> <c>hold</c>). Leaving the ground any other way (a step off an edge), the fall loop.</item>
/// <item><b>At contact</b>: running on (at the run's speed, going ahead) a running landing — after a running jump the one its notes name, back to
/// back with no blend when it ran its whole flight (level ground) or held its last frame (a drop), cut in when the ground came sooner (a step
/// up); after a fall, onto the foot the run had next. Else the standing landing whose <c>drop_m</c> band holds the drop (the fall's height:
/// <see cref="Body.LastFallHeight"/>), from the air pose with the contact blend (<see cref="PersonAirSettings.ContactSeconds"/>).</item>
/// <item>A running landing hands to the run at its <c>exit_phase</c>, with no fade; a standing one to the stand at its end (or to the gaits past
/// its deepest when the body moves on).</item>
/// </list>
/// Not in a move (a hop up, a grab, a ladder) or a dodge: none, as before.
/// </summary>
public static class PeopleAir
{
    /// <summary>How far past its take-off a jump's clip may stand on the ground before it is given up (the press let go and no jump: a hop up took it, s).</summary>
    public const float GiveUpAfter = 3f * World.TickSeconds;

    /// <summary>
    /// Off the ground without a jump (a step off an edge, a body put down a little above the ground), the clip playing goes on until the body falls
    /// faster than this (m/s: about 5 cm down), then the fall loop; a drop that never got so far lands with no landing (m/s).
    /// </summary>
    public const float FallFrom = 1f;

    /// <summary>How near its end (s) a running jump's clip must be at contact to count as its whole flight (the clip ends a frame's rounding before its level landing).</summary>
    public const float LevelWithin = 2.5f * World.TickSeconds;

    /// <summary>
    /// The body's system's part (<see cref="BodySystem"/>, before the motor): a press of jump on the ground by a body that jumps on the press (not one
    /// that charges) is held back (<see cref="PersonAir.Pending"/>) while the squash runs (<see cref="PersonAir.Squash"/>, set by the animation as
    /// the clip begins), and let go when it has run out or the body has left the ground (a step off an edge: the grace after it takes it).
    /// </summary>
    public static void Hold(ref PersonAir air, ref MoveIntent given, bool onGround, bool canCharge)
    {
        if (air.Pending != 0)
        {
            given.Jump = false;
            air.Squash -= World.TickSeconds;
            if (!onGround || air.Squash <= 1e-4f)
            {
                given.Jump = true;
                air.Pending = 0;
                air.Squash = 0f;
            }
            return;
        }
        if (given.Jump && onGround && !canCharge)
        {
            given.Jump = false;
            air.Pending = 1;
            air.Squash = 0f;
        }
    }

    /// <summary>
    /// One tick of a person's air (<see cref="PeopleAir"/>): true when it chose the clip (<paramref name="a"/> set up, its rate set; the caller only
    /// advances it), false to let the gaits choose as before. <paramref name="stand"/>: the kind's stand (its idle's clip), a standing landing's end.
    /// <paramref name="pre"/> (the prelandings: <see cref="PeoplePrelanding"/>; null) with <paramref name="world"/>, the ground below.
    /// </summary>
    public static bool Choose(CharacterAssets assets, PersonMotion person, ref CharacterAnimation a, ref PersonAir air, in MotionInput input, in AirBody body,
                              PersonAirSettings rules, AnimationSettings animation, float seconds, int stand, PersonPrelandingSettings? pre = null,
                              in OffWorld world = default)
    {
        var clips = person.Air!;
        if (input.Traversal != TraversalKind.None || input.Dodging) { Leave(ref air); return false; }

        // A press held back this tick: the jump's clip begins on the ground, its squash (or a running jump's last step) before the take-off.
        if (air.Pending != 0 && air.Mode != AirMode.Squash && input.Grounded)
        {
            if (Jump(assets, person, clips, ref a, ref air, input, animation, rules, inAir: false)) return true;
            air.Squash = 0f;   // nothing to play: the press goes the next tick
            return false;
        }
        if (air.Mode == AirMode.Squash && input.Grounded)
        {
            if (PersonAirClips.Notes(assets, a.Clip)?.Airborne is not { Jump: true } squash || !clips.Holds(a.Clip) || a.Time > squash.TakeoffS + GiveUpAfter)
            {
                Leave(ref air);   // it did not leave the ground (a hop up took the press, or it was refused): to the gaits, faded
                return false;
            }
            a.Rate = 1f;
            return true;
        }

        if (!input.Grounded)
        {
            if (air.Mode is AirMode.None or AirMode.Land or AirMode.Squash)
            {
                // Just left the ground: a jump's clip on, or the fall loop.
                bool jumped = body.Jumped && body.Rise > 1e-3f && body.Gravity > 0f && (air.Mode == AirMode.Squash || a.WasGrounded);
                if (jumped)
                {
                    air.Speed = MathF.Sqrt(2f * body.Gravity * body.Rise);
                    bool squashed = air.Mode == AirMode.Squash && PersonAirClips.Notes(assets, a.Clip)?.Airborne is { Jump: true };
                    if (!squashed && !Jump(assets, person, clips, ref a, ref air, input, animation, rules, inAir: true)) jumped = false;
                    else air.Mode = AirMode.Rise;
                }
                if (!jumped && input.VerticalSpeed > -FallFrom) return Unchanged(ref a);   // not yet falling: as it was
                if (!jumped)
                {
                    air.Speed = 0f;
                    if (a.Role == MotionRole.Run) air.Foot = NextFoot(assets, a);   // off an edge running: the foot the run had next
                    Begin(ref a, ref air, clips.Fall, MotionRole.Fall, 0f, rules.FallSeconds);
                    air.Mode = AirMode.Fall;
                }
            }
            // The prelanding, before a standing landing: under way, kept to the contact; falling or past a standing jump's apex, begun
            // from its first frame when the contact foreseen is its lead away.
            if (pre is not null && world.Level is not null && clips.Prelandings.Length > 0)
            {
                if (air.Mode == AirMode.Pre) return PeoplePrelanding.Fly(assets, clips, ref a, ref air, world, rules);
                bool standingJump = air.Mode == AirMode.Rise && PersonAirClips.Notes(assets, a.Clip)?.Airborne is { Jump: true, Running: false } && clips.Holds(a.Clip);
                if ((air.Mode == AirMode.Fall || standingJump) && PeoplePrelanding.Start(assets, clips, ref a, ref air, input, body, world, pre, animation)) return true;
            }
            else if (air.Mode == AirMode.Pre)
            {
                Begin(ref a, ref air, clips.Fall, MotionRole.Fall, 0f, rules.FallSeconds);   // (the rules gone under it: the fall)
                air.Mode = AirMode.Fall;
            }
            if (air.Mode == AirMode.Rise)
            {
                var ab = PersonAirClips.Notes(assets, a.Clip)?.Airborne;
                if (ab is not { Jump: true })
                {
                    Begin(ref a, ref air, clips.Fall, MotionRole.Fall, 0f, rules.FallSeconds);
                    air.Mode = AirMode.Fall;
                }
                else
                {
                    float d = assets.Clip(a.Clip).Duration;
                    float t = MathF.Max(a.Time, Arc(ab, air.Speed, input.VerticalSpeed, a.Time + seconds));
                    a.Rate = 0f;
                    a.Role = MotionRole.Jump;
                    if (t < d) a.Time = t;
                    else if (ab.Holds) a.Time = d;   // Held until the ground is met, the leg reaching
                    else
                    {
                        Begin(ref a, ref air, clips.Fall, MotionRole.Fall, 0f, rules.FallSeconds);   // its end: into the fall loop
                        air.Mode = AirMode.Fall;
                    }
                    if (air.Mode == AirMode.Rise) return true;
                }
            }
            a.Role = MotionRole.Fall;
            a.Rate = 1f;
            return true;
        }

        // The contact, this tick.
        if (air.Mode is AirMode.Rise or AirMode.Fall or AirMode.Pre)
        {
            air.Drop = body.Fell;
            bool fromPre = air.Mode == AirMode.Pre;   // after a prelanding
            var ab = PersonAirClips.Notes(assets, a.Clip)?.Airborne;
            bool fromRunningJump = air.Mode == AirMode.Rise && ab is { Running: true };
            if (Running(input, animation, ahead: true))
            {
                int land = -1;
                bool level = false;
                if (fromRunningJump)
                {
                    land = Named(assets, clips.RunLandings, ab!.Land);
                    level = a.Time >= assets.Clip(a.Clip).Duration - LevelWithin;
                }
                if (land < 0) land = clips.RunLanding(air.Foot);
                if (land >= 0)
                {
                    // The running jump's last frame is the landing's first: back to back (no blend) on level ground or a drop; cut in on a step up.
                    Begin(ref a, ref air, land, MotionRole.Run, 0f, fromRunningJump && level ? 0f : rules.CutSeconds);
                    air.Mode = AirMode.Land;
                    a.Rate = 0f;   // its first frame, the moment of contact, this tick
                    return true;
                }
            }
            // After a prelanding the landing by the same rule it was foreseen by (the player's own at once: PeopleOff.After agrees); on its first
            // frame, the prelanding's last, with no blend when the prelanding has come to it (PeoplePrelanding.Seam).
            int landing = fromPre ? clips.Standing(assets, air.Drop, air.Curve == 1) : clips.Landing(assets, air.Drop);
            if (landing >= 0)
            {
                float fade = fromRunningJump ? rules.CutSeconds : fromPre && PeoplePrelanding.Seam(assets, a, landing) ? 0f : rules.ContactSeconds;
                Begin(ref a, ref air, landing, MotionRole.Land, 0f, fade);
                air.Mode = AirMode.Land;
                a.Rate = 0f;   // its first frame, the moment of contact, this tick
                return true;
            }
            Leave(ref air);
            return false;
        }

        if (air.Mode == AirMode.Land)
        {
            var ln = PersonAirClips.Notes(assets, a.Clip)?.Landing;
            if (ln is null || !clips.Holds(a.Clip)) { Leave(ref air); return false; }
            var clip = assets.Clip(a.Clip);
            if (ln.Running)
            {
                if (input.GroundSpeed < animation.IdleBelow) { Leave(ref air); return false; }   // stopped: to the stand, faded
                if (a.Time + seconds >= clip.Duration)
                {
                    // Its last frame: the run takes over at its exit_phase, with no fade (the caller sets the loop's rate this tick).
                    if (person.Run is not { } run) { Leave(ref air); return false; }
                    int loop = run.Clip(input.ForwardSpeed, input.SideSpeed, run.First);
                    a.Clip = loop;
                    a.Role = MotionRole.Run;
                    a.Time = ln.ExitPhase * assets.Clip(loop).Duration;
                    a.PreviousClip = -1;
                    a.Blend = 1f;
                    a.Rate = 0f;
                    Leave(ref air);
                    return false;
                }
                a.Role = MotionRole.Run;
                a.Rate = 1f;
                return true;
            }
            // Standing: to its end, the stand after it; moving on, to the gaits once past its deepest.
            bool moving = input.GroundSpeed >= animation.IdleBelow + 0.5f * animation.Hysteresis;
            float deepest = float.IsNaN(ln.DeepestS) ? ln.HeelsDownS : ln.DeepestS;
            if (a.Time >= clip.Duration - 1e-4f && stand >= 0)
            {
                // Its end: the stand, with the animation's fade (handed here: the caller's choice this tick was made on the landing not yet over).
                Begin(ref a, ref air, stand, MotionRole.Idle, 0f, animation.FadeSeconds);
                Leave(ref air);
                a.Rate = 1f;
                return true;
            }
            if (moving && a.Time >= deepest) { Leave(ref air); return false; }
            a.Role = MotionRole.Land;
            a.Rate = 1f;
            return true;
        }
        return false;
    }

    /// <summary>The time in a jump's clip for the capsule's upward speed <paramref name="v"/> of a take-off at <paramref name="v0"/> (by the clip's
    /// own rise between its take-off and its apex); <paramref name="otherwise"/> without a take-off speed.</summary>
    public static float Arc(ClipAirborne ab, float v0, float v, float otherwise)
    {
        if (v0 <= 1e-3f || !ab.Jump) return otherwise;
        return ab.TakeoffS + (1f - v / v0) * (ab.ApexS - ab.TakeoffS);
    }

    /// <summary>Whether the body goes at a run (the walk-run threshold, less half its band), and (<paramref name="ahead"/>) within 45° of its facing.</summary>
    internal static bool Running(in MotionInput input, AnimationSettings animation, bool ahead)
    {
        float speed = input.GroundSpeed;
        if (speed < animation.RunFrom - 0.5f * animation.Hysteresis) return false;
        return !ahead || input.ForwardSpeed >= 0.7071f * speed;
    }

    /// <summary>
    /// A jump's clip begun: out of a run going ahead the running jump off the foot the run has planted, else the standing jump of the person's
    /// curve. On the ground (the press held back) from the foot's moment, the squash set to what is left to its take-off; in the air (a jump taken
    /// at once) at the arc's time.
    /// </summary>
    static bool Jump(CharacterAssets assets, PersonMotion person, PersonAirClips clips, ref CharacterAnimation a, ref PersonAir air, in MotionInput input,
                     AnimationSettings animation, PersonAirSettings rules, bool inAir)
    {
        int clip = -1, foot = 0;
        float entry = 0f;
        if (a.Role == MotionRole.Run && Running(input, animation, ahead: true) && clips.RunJumps[0] >= 0 && clips.RunJumps[1] >= 0)
        {
            float takeoff = PersonAirClips.Notes(assets, clips.RunJumps[0])?.Airborne?.TakeoffS ?? 0f;
            (foot, entry) = PlantedFoot(assets, a, takeoff);
            clip = clips.RunJump(foot);
        }
        if (clip < 0) { clip = clips.JumpFor(air.Curve); foot = 0; entry = 0f; }
        if (PersonAirClips.Notes(assets, clip)?.Airborne is not { Jump: true } ab) return false;
        float time = inAir ? Arc(ab, air.Speed, input.VerticalSpeed, ab.TakeoffS) : entry;
        Begin(ref a, ref air, clip, MotionRole.Jump, time, rules.EnterSeconds);
        // A running jump lands on the other foot (its notes' running landing); a standing one does not say.
        air.Foot = foot == 1 ? 2 : foot == 2 ? 1 : 0;
        if (inAir) { a.Rate = 0f; return true; }
        a.Rate = 1f;
        air.Mode = AirMode.Squash;
        air.Squash = MathF.Max(0f, ab.TakeoffS - entry);
        return true;
    }

    /// <summary>
    /// The run's feet now: for each foot, how long since it came down (s of the clip as played), by the loop's footfalls. Null when the clip is not
    /// a run with footfalls (a start's, a stair's).
    /// </summary>
    internal static (float Left, float Right)? SinceDown(CharacterAssets assets, in CharacterAnimation a)
    {
        if (a.Clip < 0) return null;
        var clip = assets.Clip(a.Clip);
        INotedClip? made = clip is GaitBlendClip blend ? (blend.Weight > 0.5f ? blend.To : blend.From) : clip as INotedClip;
        if (made is null || !made.Loops || made.Duration <= 0f || clip.Duration <= 0f) return null;
        float rate = MathF.Max(0.5f, MathF.Abs(a.Rate));
        float p = a.Time / clip.Duration;
        p -= MathF.Floor(p);
        float left = float.MaxValue, right = float.MaxValue;
        foreach (var f in made.Notes.Footfalls)
        {
            float since = p - f.At / made.Duration;
            since -= MathF.Floor(since);
            float s = since * clip.Duration / rate;
            if (f.Left) left = MathF.Min(left, s); else right = MathF.Min(right, s);
        }
        return left < float.MaxValue && right < float.MaxValue ? (left, right) : null;
    }

    /// <summary>
    /// The foot a running jump leaves from (1 left, 2 right) and how far into its clip it is entered (s): the foot that came down most lately, that far
    /// in, when within the clip's take-off; else the next to come down, from its first frame. The left from the first frame when the run's feet are not known.
    /// </summary>
    internal static (int Foot, float Entry) PlantedFoot(CharacterAssets assets, in CharacterAnimation a, float takeoff)
    {
        if (SinceDown(assets, a) is not var (l, r)) return (1, 0f);
        bool left = l <= r;
        float since = left ? l : r;
        if (since < takeoff) return (left ? 1 : 2, since);
        return (left ? 2 : 1, 0f);   // the other comes down next
    }

    /// <summary>The foot the run brings down next (1 left, 2 right; 0 not known).</summary>
    static int NextFoot(CharacterAssets assets, in CharacterAnimation a) => SinceDown(assets, a) is var (l, r) ? (l <= r ? 2 : 1) : 0;

    /// <summary>The clip of <paramref name="among"/> named <paramref name="name"/>, or −1.</summary>
    static int Named(CharacterAssets assets, int[] among, string name)
    {
        foreach (int c in among) if (c >= 0 && assets.Clip(c).Name == name) return c;
        return -1;
    }

    /// <summary>The clip playing goes on as it was (its rate kept).</summary>
    static bool Unchanged(ref CharacterAnimation a) => a.Clip >= 0;

    static void Leave(ref PersonAir air)
    {
        air.Mode = AirMode.None;
        air.Fade = 0f;
    }

    /// <summary>A clip begun at <paramref name="time"/>, faded in over <paramref name="fade"/> s (0: at once).</summary>
    internal static void Begin(ref CharacterAnimation a, ref PersonAir air, int clip, MotionRole role, float time, float fade)
    {
        if (fade > 0f && a.Clip >= 0 && a.Clip != clip)
        {
            a.PreviousClip = a.Clip;
            a.PreviousTime = a.Time;
            a.PreviousRate = a.Rate;
            a.Blend = 0f;
            air.Fade = fade;
        }
        else if (a.Clip != clip || fade <= 0f)
        {
            a.PreviousClip = -1;
            a.Blend = 1f;
            air.Fade = 0f;
        }
        a.Clip = clip;
        a.Role = role;
        a.Time = time;
        a.RateScale = 1f;
    }
}
