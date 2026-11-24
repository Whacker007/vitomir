// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository (52b to 52e; the prelanding), played as its notes say (airborne.prelanding).
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// <b>the prelanding</b> on the models repository's clips (<c>prelanding_soft</c>, <c>prelanding_high</c>, <c>prelanding_high_player</c>): whether
/// they play. Off (<see cref="Off"/>: <c>--prelanding-off</c>) a fall goes into its standing landing as before did (the contact blend from the fall). They
/// ride on the moves over and onto things (the ground below foreseen) and the air's clips: with <c>--over-clips-off</c> or <c>--air-clips-off</c> they are
/// off too. The capsule's flight is untouched; the drawn body (the animation's state, digested, and the hit volumes on it) is not, so a tape carries them
/// in its note <see cref="TapeNote"/>; a tape without it is the earlier build's.
/// </summary>
public sealed record PersonPrelandingSettings
{
    /// <summary>Whether the models repository's prelandings play before a standing landing (on by default; off: the earlier build's, <c>--prelanding-off</c>).</summary>
    public bool Clips { get; init; } = true;

    /// <summary>
    /// The fade into the prelanding's first frame from the <c>fall</c> loop at whatever phase (or from a standing jump's clip past its apex, or a ledge's
    /// drop), in the clip's own seconds (0.13: the models repository's "about 0.13 s"; played faster, as much shorter in real time).
    /// </summary>
    public float FromSeconds { get; init; } = 0.13f;

    /// <summary>
    /// A fall shorter than the prelanding's lead — <see cref="ShortFall.Cutoff"/> (the default): none below
    /// <see cref="CutoffSeconds"/> of air (the earlier build's contact blend from the fall), faster to fit above it as before; <see cref="ShortFall.Partway"/> (the models
    /// stream's note): begun part way in, at <c>lead_s</c> less the time to the contact, never later than <see cref="PartwayLatest"/>
    /// (under <see cref="PartwayLeast"/> left: from there, faster); <see cref="ShortFall.Faster"/>: the earlier build's, from its first frame faster to fit.
    /// </summary>
    public ShortFall Short { get; init; } = ShortFall.Cutoff;

    /// <summary>Below this much air (s: the flight so far and the time to the contact) no prelanding, with <see cref="ShortFall.Cutoff"/>.</summary>
    public float CutoffSeconds { get; init; } = 0.2f;

    /// <summary>
    /// Whether <see cref="FromSeconds"/> is game time whatever the prelanding's rate (the models repository: "real time"); false, the earlier build's: the clip's own
    /// time (a prelanding at 6× faded in over 0.02 s).
    /// </summary>
    public bool RealFade { get; init; } = true;

    /// <summary>Part way: the latest a prelanding is begun at (s of it), and the least time to the contact that begins it there at its own rate (s).</summary>
    public const float PartwayLatest = 0.17f, PartwayLeast = 0.13f;

    /// <summary>Whether any is on.</summary>
    public bool Any => Clips;

    /// <summary>None.</summary>
    public static PersonPrelandingSettings Off => new() { Clips = false };

    /// <summary>The tape head's note.</summary>
    public const string TapeNote = "prelanding";

    /// <summary>
    /// These settings as before played them (faster to fit a short fall, the fade in the clip's own time): <c>--commit-off</c> and a tape without
    /// the note's an earlier build keys.
    /// </summary>
    public PersonPrelandingSettings WithoutCommit => this with { Short = ShortFall.Faster, RealFade = false };

    /// <summary>The note: <c>off</c>, else <c>clips=1 from=0.13</c> and, <c>short=1 cutoff=0.2 real=1</c> (none of them: the earlier build's).</summary>
    public string Note() => !Clips ? "off" : Short == ShortFall.Faster && !RealFade
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"clips=1 from={FromSeconds:0.####}")
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"clips=1 from={FromSeconds:0.####} short={(int)Short} cutoff={CutoffSeconds:0.####} real={(RealFade ? 1 : 0)}");

    /// <summary>These settings with a tape's note laid on; none (an older tape) or <c>off</c> is <see cref="Off"/>. The numbers go through the note's text.</summary>
    public PersonPrelandingSettings WithNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note) || !note.Contains('=')) return Off;
        var s = this with { Clips = true, Short = ShortFall.Faster, RealFade = false };   // a note without its keys is the earlier build's
        foreach (var part in note.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || !float.TryParse(part[(eq + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
            s = part[..eq] switch
            {
                "clips" => s with { Clips = v != 0f }, "from" => s with { FromSeconds = v },
                "short" => s with { Short = (ShortFall)(int)v }, "cutoff" => s with { CutoffSeconds = v }, "real" => s with { RealFade = v != 0f }, _ => s,
            };
        }
        return s.Clips ? s : Off;
    }
}

/// <summary>What a fall shorter than the prelanding's lead plays (<see cref="PersonPrelandingSettings.Short"/>).</summary>
public enum ShortFall
{
    /// <summary>The prelanding from its first frame, faster to fit.</summary>
    Faster = 0,
    /// <summary>By design: none below the cutoff's air, faster to fit above it.</summary>
    Cutoff = 1,
    /// <summary>The models repository's: begun part way in.</summary>
    Partway = 2,
}

/// <summary>
/// <b>playing the prelanding</b> (the models repository; <see cref="PeopleAir.Choose"/> calls it in the air for a person with the clips and the
/// ground below foreseen, <see cref="OffWorld"/>):
/// <list type="bullet">
/// <item>Falling (the <c>fall</c> loop: off an edge with no stride, a ledge's drop, a falling grab's miss, a charged jump past its clip), or in a standing jump's
/// clip past its apex, and not running ahead (a running landing comes then): each tick the contact is foreseen from the ground below
/// (<see cref="BodyMotor.ContactTime"/>, as the stride rule's) and counted in ticks (n: the tick of the contact is n from now). When n ticks are
/// <c>lead_s</c> or less, the landing is chosen as the contact will choose it (the drop's band from the highest the feet have been to the ground foreseen,
/// the player's own: <see cref="PersonAirClips.Standing"/>) and its prelanding (<c>then</c>) begins <b>from its first frame</b>, drawn this tick, faded in
/// from what plays over <see cref="PersonPrelandingSettings.FromSeconds"/> of its own time.</item>
/// <item>Its rate brings its last frame to the contact's tick: <c>lead_s</c> (its length) over n ticks — 1 when begun at its lead, faster when the fall was
/// shorter — kept to the contact foreseen tick by tick (what is left of it over the ticks left). A contact that recedes past its lead (a ledge passes
/// from under, the ground falls away) by more than <see cref="Recede"/> goes back to the fall; it begins again from its first frame when the ground comes.</item>
/// <item>At the contact the standing landing (<see cref="PeopleAir.Choose"/>) begins on that tick from its first frame — the prelanding's last, which is
/// not drawn — with no blend when the prelanding has reached its end within <see cref="SeamTicks"/> of its play and its fade in is done and the
/// landing is its <c>then</c>; else the contact blend (a contact earlier than foreseen, or a band other than foreseen).</item>
/// <item>A contact the next tick when the fall begins (under one tick to go): none; the landing takes the earlier build's contact blend from the fall.</item>
/// </list>
/// </summary>
public static class PeoplePrelanding
{
    /// <summary>How far past its lead (s) the contact foreseen may recede under a prelanding before it goes back to the fall.</summary>
    public const float Recede = 0.1f;

    /// <summary>The fewest ticks to the contact for a prelanding to begin (2: its first frame this tick, a tick of it between, the landing on the contact's).</summary>
    public const int Fewest = 2;

    /// <summary>
    /// How far over the ground the motor's capsule is met by it (m): it lands when its feet come within this of the ground's (five skins: the probe's four over
    /// the skin it rests at), a tick sooner than the ground's own height at a slow fall's 1 cm a tick.
    /// </summary>
    public const float Meets = 5f * BodyMotor.Skin;

    /// <summary>The ticks from now to the tick the body meets the ground (1: the next), and the time to it (s); (0, NaN) when not within <paramref name="most"/> s.</summary>
    public static (int Ticks, float Seconds) ToContact(in OffWorld world, float most)
    {
        if (world.Level is null) return (0, float.NaN);
        float t = BodyMotor.ContactTime(world.Level, world.Feet - new System.Numerics.Vector3(0f, 0f, Meets), world.Velocity, world.Loco.Gravity, most);
        if (float.IsNaN(t)) return (0, float.NaN);
        return (Math.Max(1, (int)MathF.Ceiling(t / World.TickSeconds - 1e-3f)), t);
    }

    /// <summary>The longest lead of the set's prelandings (s).</summary>
    static float Lead(CharacterAssets assets, PersonAirClips clips)
    {
        float lead = 0f;
        foreach (int p in clips.Prelandings) lead = MathF.Max(lead, LeadOf(assets, p));
        return lead;
    }

    /// <summary>A prelanding's lead (s): its notes' <c>lead_s</c>, its length when the notes do not say.</summary>
    static float LeadOf(CharacterAssets assets, int clip) =>
        PersonAirClips.Notes(assets, clip)?.Airborne?.Prelanding is { LeadS: > 0f } p ? p.LeadS : assets.Clip(clip).Duration;

    /// <summary>
    /// The drop the contact foreseen <paramref name="seconds"/> from now will count (<see cref="Body.LastFallHeight"/>): from the highest the feet have been in
    /// this flight (<paramref name="highest"/>; or the arc's top still to come) to where they meet the ground.
    /// </summary>
    public static float Drop(in OffWorld world, float highest, float seconds)
    {
        float g = world.Loco.Gravity, vz = world.Velocity.Z, z = world.Feet.Z;
        float top = float.IsNaN(highest) ? z : MathF.Max(highest, z);
        if (vz > 0f && g > 0f) top = MathF.Max(top, z + vz * vz / (2f * g));
        return top - (z + vz * seconds - 0.5f * g * seconds * seconds) + Meets - BodyMotor.Skin;   // the feet then rest a skin over the ground
    }

    /// <summary>
    /// In the air (falling, or a standing jump's clip past its apex), not yet in a prelanding: true when one begins this tick (its first frame drawn, the
    /// air's mode <see cref="AirMode.Pre"/>).
    /// </summary>
    public static bool Start(CharacterAssets assets, PersonAirClips clips, ref CharacterAnimation a, ref PersonAir air, in MotionInput input, in AirBody body,
                             in OffWorld world, PersonPrelandingSettings rules, AnimationSettings animation)
    {
        if (clips.Prelandings.Length == 0 || input.VerticalSpeed >= 0f || PeopleAir.Running(input, animation, ahead: true)) return false;
        float lead = Lead(assets, clips);
        var (n, t) = ToContact(world, lead + 2f * World.TickSeconds);
        if (n == 0 || n * World.TickSeconds > lead + 1e-4f) return false;
        if (n < Fewest) return false;   // under one tick to go: the earlier build's contact blend from the fall
        int landing = clips.Standing(assets, Drop(world, body.Highest, t), air.Curve == 1);
        int pre = clips.PrelandingFor(assets, landing);
        if (pre < 0 || n * World.TickSeconds > LeadOf(assets, pre) + 1e-4f) return false;
        float d = assets.Clip(pre).Duration;
        if (d <= 0f) return false;
        float toContact = n * World.TickSeconds, entry = 0f, preLead = LeadOf(assets, pre);
        // A fall shorter than its lead — none below the cutoff's air (the contact blend from the fall), or begun part way in.
        if (toContact < preLead - 1e-4f)
        {
            if (rules.Short == ShortFall.Cutoff && body.AirSeconds + toContact < rules.CutoffSeconds - 1e-4f) return false;
            if (rules.Short == ShortFall.Partway) entry = MathF.Min(PersonPrelandingSettings.PartwayLatest, MathF.Max(0f, preLead - toContact));
        }
        float rate = (d - entry) / toContact;
        // The fade in game time whatever the rate (the clip's own time).
        float fade = rules.RealFade ? rules.FromSeconds : rules.FromSeconds / MathF.Max(1f, rate);
        // The fall begun this very tick (a drop's clip or a step off just ended): faded from what played before it, not from the fall's first frame.
        if (a.Clip == clips.Fall && a.PreviousClip >= 0 && a.Blend <= 0f)
        {
            a.Clip = a.PreviousClip;
            a.Time = a.PreviousTime;
            a.Rate = a.PreviousRate;
            a.PreviousClip = -1;
            a.Blend = 1f;
        }
        if (a.PreviousClip >= 0 && a.Blend < 1f)
        {
            // A fade into the fall still under way (from the stand, a walk, a jump's clip): it goes on, into the prelanding in the fall's place (its first
            // frame is the fall's air pose), over what is left of it or the prelanding's own fade if that is sooner; the clip it came from is not dropped.
            float left = (1f - a.Blend) * (air.Fade > 0f ? air.Fade : animation.FadeSeconds);
            a.Clip = pre;
            a.Role = MotionRole.Fall;
            a.Time = entry;
            a.RateScale = 1f;
            air.Fade = MathF.Max(World.TickSeconds, MathF.Min(left, fade)) / MathF.Max(1e-3f, 1f - a.Blend);
        }
        else PeopleAir.Begin(ref a, ref air, pre, MotionRole.Fall, entry, fade);
        // From a standing jump's clip past its apex (its time the capsule's arc's, its rate nought): it goes on fading at the arc's pace, not stopped.
        if (a.PreviousClip >= 0 && a.PreviousRate == 0f && PersonAirClips.Notes(assets, a.PreviousClip)?.Airborne is { Jump: true } jump && air.Speed > 1e-3f && body.Gravity > 0f)
            a.PreviousRate = (jump.ApexS - jump.TakeoffS) * body.Gravity / air.Speed;
        air.Mode = AirMode.Pre;
        a.Rate = 0f;   // its first frame this tick
        return true;
    }

    /// <summary>
    /// A tick of a prelanding in the air: its rate to bring its last frame to the contact's tick (held there if the contact is late); back to the fall
    /// when the contact recedes past its lead by more than <see cref="Recede"/> or goes.
    /// </summary>
    public static bool Fly(CharacterAssets assets, PersonAirClips clips, ref CharacterAnimation a, ref PersonAir air, in OffWorld world, PersonAirSettings airSettings)
    {
        float lead = LeadOf(assets, a.Clip);
        var (n, _) = ToContact(world, lead + Recede + 2f * World.TickSeconds);
        if (n == 0 || n * World.TickSeconds > lead + Recede)
        {
            PeopleAir.Begin(ref a, ref air, clips.Fall, MotionRole.Fall, 0f, airSettings.FallSeconds);
            air.Mode = AirMode.Fall;
            a.Rate = 1f;
            return true;
        }
        float d = assets.Clip(a.Clip).Duration, left = d - a.Time;
        a.Role = MotionRole.Fall;
        a.Rate = left <= 1e-5f ? 0f : left / ((n + 1) * World.TickSeconds);
        return true;
    }

    /// <summary>
    /// Whether the standing landing <paramref name="landing"/> follows the prelanding playing with no blend: it is the prelanding's <c>then</c>, the fade into
    /// the prelanding is done and it has reached its last frame within <see cref="SeamTicks"/> ticks of its play (a contact a tick sooner than foreseen: the contact blend).
    /// </summary>
    public static bool Seam(CharacterAssets assets, in CharacterAnimation a, int landing)
    {
        if (landing < 0 || a.PreviousClip >= 0 || PersonAirClips.Notes(assets, a.Clip)?.Airborne?.Prelanding is not { } pre) return false;
        if (pre.Then != assets.Clip(landing).Name) return false;
        float left = assets.Clip(a.Clip).Duration - a.Time;
        return left <= SeamTicks * World.TickSeconds * MathF.Max(1f, a.Rate) + 1e-4f;
    }

    /// <summary>How near its end (ticks of its play) a prelanding is at the contact to go into its landing with no blend (1.5: met on its tick).</summary>
    public const float SeamTicks = 1.5f;
}
