// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Serialization;
using Vitomir.Platform.Control;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Host;

/// <summary>
/// How the window stands: <c>windowed</c> (a title bar, at the settings' size), <c>borderless</c> (over the whole
/// display, the picture fitted inside it) or <c>centered</c> (no title bar, the settings' size at the
/// centre of the display's whole bounds, above the task bar while it has the focus; the default).
/// </summary>
public enum WindowMode { Windowed, Borderless, Centered }


/// <summary>
/// The player's settings for the platform's program, kept as JSON in the player's own folder
/// (<see cref="UserData.SettingsPath"/>) and written back when the program closes. A missing or
/// unreadable file gives the defaults; unknown fields are ignored.
/// </summary>
public sealed class PlatformSettings
{
    /// <summary>The level package played last, used when <c>--level</c> is not given.</summary>
    public string LastLevel { get; set; } = "";
    /// <summary>
    /// The view the last session was left in — <c>first</c>, <c>right</c> or <c>left</c> — and the
    /// view the next one starts in (play only; a replay starts as its tape did, a headless run as always). Right by default.
    /// </summary>
    public string LastView { get; set; } = "right";
    /// <summary>The 1.1 install the characters were read from last, used when <c>--game</c> is not given.</summary>
    public string GameFolder { get; set; } = "";

    // ---- ----
    /// <summary>
    /// The models repository's folder the people were read from last (its repository while developing, or a
    /// package's <c>People</c> copy), used when <c>--models</c> is not given; then <c>VITOMIR_MODELS_DIR</c>, a <c>People</c> folder beside
    /// the program's <c>bin</c>, <c>C:\Games\Vitomir\models</c> (<see cref="PeopleBodies.FindFolder"/>). Saved back as
    /// <see cref="GameFolder"/> is.
    /// </summary>
    public string ModelsFolder { get; set; } = "";

    /// <summary>
    /// The player's body in play — <c>models</c> (the models repository's man on its male base, the default),
    /// <c>woman</c> (its woman on the female base) or <c>oneone</c> (1.1's Christof, with his combat); <c>--body</c> for one run. A body
    /// that cannot be built (the files or the loader missing) is Christof, said once. The arena, scenarios, <c>--play-as</c> and the
    /// surveys keep 1.1's bodies.
    /// </summary>
    public string PlayerBody { get; set; } = PeopleBodies.Models;
    // ---- end

    // ---- the people's turn as a chain that catches up (20:38) and the crouch ----
    // Each number has a --turn-… flag for one run (Cli.cs); degrees, degrees a second and seconds. Recorded in a tape (its note turn).
    /// <summary>The fastest a person's head turns after the view (°/s; 300). <c>--turn-head-rate</c>.</summary>
    public float TurnHeadRate { get; set; } = 300f;
    /// <summary>How far the head trails a steady turn of the view (s; 0.03). <c>--turn-head-lag</c>.</summary>
    public float TurnHeadLag { get; set; } = 0.03f;
    /// <summary>The most the head is turned ahead of the chest (°; 25). <c>--turn-neck-most</c>.</summary>
    public float TurnNeckMost { get; set; } = 25f;
    /// <summary>The fastest the chest turns after the head (°/s; 200). <c>--turn-chest-rate</c>.</summary>
    public float TurnChestRate { get; set; } = 200f;
    /// <summary>How far the chest trails the head (s; 0.06). <c>--turn-chest-lag</c>.</summary>
    public float TurnChestLag { get; set; } = 0.06f;
    /// <summary>The most the chest is turned ahead of the hips (°; 30). <c>--turn-spine-most</c>.</summary>
    public float TurnSpineMost { get; set; } = 30f;
    /// <summary>How far the view leaves the standing hips before they turn on the spot (°; 30). <c>--turn-from</c>.</summary>
    public float TurnFrom { get; set; } = 30f;
    /// <summary>At a turn's end, how far off the next turn follows (°; 20). <c>--turn-on</c>.</summary>
    public float TurnOn { get; set; } = 20f;
    /// <summary>The most a turn on the spot is sped up when the hips are far behind (times its rate; 2). <c>--turn-most-speedup</c>.</summary>
    public float TurnMostSpeedup { get; set; } = 2f;
    /// <summary>How far behind the view the legs also pivot on their own (°; 75). <c>--turn-pivot-from</c>.</summary>
    public float TurnPivotFrom { get; set; } = 75f;
    /// <summary>How fast they pivot (°/s; 240). <c>--turn-pivot-rate</c>.</summary>
    public float TurnPivotRate { get; set; } = 240f;
    /// <summary>The most the hips are ever behind the view (°; 90): past it the whole body is carried round with the view. 0 or 180 is off, the earlier build's. <c>--turn-leash</c>.</summary>
    public float TurnLeash { get; set; } = 90f;
    /// <summary>The chain's waist as before (the spine turned evenly and slid on the hips; by default the earlier build's: spread mostly over the chest, the spine left on the pelvis). <c>--turn-waist-even</c>.</summary>
    public bool TurnWaistEven { get; set; }
    /// <summary>The earlier build's turn of a person instead of the chain (the chest on the view at once, the turn on the spot from 45°). <c>--turn-chain-off</c>.</summary>
    public bool TurnChainOff { get; set; }
    /// <summary>A person crouched and still settles into the kneel, as before (off: the crouch held). <c>--crouch-kneel</c>.</summary>
    public bool CrouchKneel { get; set; }

    /// <summary>
    /// A person's turn for a run: the earlier build's with <see cref="TurnChainOff"/> or <c>--turn-chain-off</c>, else the chain's numbers from
    /// these settings; then any <c>--turn-…</c> flag over either. Each number is kept within its sense (a rate 1 to 3600°/s, a limit 0
    /// to 180°, a lag 0 to 1 s, the speed-up 1 to 4): a flag out of it is refused, a setting out of it is the default.
    /// </summary>
    public Animation.LegTurnSettings PersonTurnFor(Cli cli)
    {
        const float D = Core.DetMath.DegToRad;
        var t = TurnChainOff || cli.Flag("--turn-chain-off") ? Animation.LegTurnSettings.NoChain : new Animation.LegTurnSettings
        {
            HeadRate = TurnHeadRate * D, HeadLag = TurnHeadLag, NeckMost = TurnNeckMost * D, ChestRate = TurnChestRate * D, ChestLag = TurnChestLag,
            SpineMost = TurnSpineMost * D, PersonTurnFrom = TurnFrom * D, PersonTurnOn = TurnOn * D, TurnMostSpeedup = TurnMostSpeedup,
            PivotFrom = TurnPivotFrom * D, PivotRate = TurnPivotRate * D, Leash = TurnLeash * D, WaistEven = TurnWaistEven || cli.Flag("--turn-waist-even"),
        };
        var defaults = t.Chain ? new Animation.LegTurnSettings() : Animation.LegTurnSettings.NoChain;
        float Given(string flag, float now, float fallback, float scale, float least, float most)
        {
            if (cli.Arg(flag) is not { } text) return now / scale >= least && now / scale <= most ? now : fallback;   // a setting out of its sense: the default
            if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) || !(v >= least && v <= most))
                throw new SessionException($"{flag} is a number from {least} to {most}, not '{text}'");
            return v * scale;
        }
        return t with
        {
            HeadRate = Given("--turn-head-rate", t.HeadRate, defaults.HeadRate, D, 1f, 3600f), HeadLag = Given("--turn-head-lag", t.HeadLag, defaults.HeadLag, 1f, 0f, 1f),
            NeckMost = Given("--turn-neck-most", t.NeckMost, defaults.NeckMost, D, 0f, 180f), ChestRate = Given("--turn-chest-rate", t.ChestRate, defaults.ChestRate, D, 1f, 3600f),
            ChestLag = Given("--turn-chest-lag", t.ChestLag, defaults.ChestLag, 1f, 0f, 1f), SpineMost = Given("--turn-spine-most", t.SpineMost, defaults.SpineMost, D, 0f, 180f),
            PersonTurnFrom = Given("--turn-from", t.PersonTurnFrom, defaults.PersonTurnFrom, D, 0f, 180f), PersonTurnOn = Given("--turn-on", t.PersonTurnOn, defaults.PersonTurnOn, D, 0f, 180f),
            TurnMostSpeedup = Given("--turn-most-speedup", t.TurnMostSpeedup, defaults.TurnMostSpeedup, 1f, 1f, 4f),
            PivotFrom = Given("--turn-pivot-from", t.PivotFrom, defaults.PivotFrom, D, 0f, 180f), PivotRate = Given("--turn-pivot-rate", t.PivotRate, defaults.PivotRate, D, 1f, 3600f),
            Leash = Given("--turn-leash", t.Leash, defaults.Leash, D, 0f, 180f),
        };
    }
    // ---- end

    // ----
    /// <summary>
    /// The townsfolk who do not fight — <c>models</c> (the default: drawn as the models repository's crowd, each as a person of
    /// its sex and role, walking its own style) or <c>oneone</c> (1.1's stand-ins, as before); <c>--townsfolk-oneone</c> for one run. The
    /// guards, the watch, the foes and the arena keep 1.1's bodies (the only ones with combat for now). Files missing: 1.1's, said once.
    /// <c>models</c> draws each as the person the models repository baked; <c>models-built</c> is the earlier build's crowd, built at
    /// run time on the bases (<c>--crowd-built</c> for one run).
    /// </summary>
    public string TownsfolkBodies { get; set; } = Host.TownsfolkBodies.Models;

    /// <summary>
    /// The people of the models repository are skinned on the CPU; with several about, those out of view (their bounds grown by
    /// 4 m for their shadows) are left out, and those further than <see cref="CharacterSkinNear"/> metres skinned every
    /// <see cref="CharacterSkinMidEvery"/>th frame, further than <see cref="CharacterSkinFar"/> every <see cref="CharacterSkinFarEvery"/>th
    /// (the last skinning drawn again between, carried along). <c>--skin-every-frame</c> for one run is the earlier build's: everyone every frame.
    /// Presentation only (<see cref="Render.Characters.PeopleSkinning"/>).
    /// </summary>
    public bool CharacterSkinCull { get; set; } = true;
    /// <inheritdoc cref="CharacterSkinCull"/>
    public float CharacterSkinNear { get; set; } = 12f;
    /// <inheritdoc cref="CharacterSkinCull"/>
    public float CharacterSkinFar { get; set; } = 30f;
    /// <inheritdoc cref="CharacterSkinCull"/>
    public int CharacterSkinMidEvery { get; set; } = 2;
    /// <inheritdoc cref="CharacterSkinCull"/>
    public int CharacterSkinFarEvery { get; set; } = 4;

    /// <summary>The people's skinning for this run: <c>--skin-every-frame</c> (<paramref name="everyFrame"/>) is the earlier build's, else the settings (each rate 1 to 8, the distances 0 to 500 m).</summary>
    public Render.Characters.PeopleSkinning PeopleSkinningFor(bool everyFrame) => everyFrame ? Render.Characters.PeopleSkinning.EveryFrame : new()
    {
        Cull = CharacterSkinCull,
        NearMetres = Math.Clamp(CharacterSkinNear, 0f, 500f),
        FarMetres = Math.Clamp(Math.Max(CharacterSkinFar, CharacterSkinNear), 0f, 500f),
        MidEvery = Math.Clamp(CharacterSkinMidEvery, 1, 8),
        FarEvery = Math.Clamp(CharacterSkinFarEvery, 1, 8),
    };
    // ---- end

    // ---- a person's planted feet, the heels-up squat and the bend helpers ----
    /// <summary>The earlier build's feet for a person (no foot locks, no heel lift, no soft limit). <c>--feet-off</c>.</summary>
    public bool FeetOff { get; set; }
    /// <summary>How long a planted foot takes to be held or let go (s; 0.1). <c>--foot-lock-seconds</c>.</summary>
    public float FootLockSeconds { get; set; } = 0.1f;
    /// <summary>How far the motion may carry a held foot before it is let go and planted again (m; 0.2). <c>--foot-replant</c>.</summary>
    public float FootReplant { get; set; } = 0.2f;
    /// <summary>How far the crouch held lifts the heels onto the balls of the feet (°; 28, 0 off). <c>--heel-lift</c>.</summary>
    public float HeelLift { get; set; } = 28f;
    /// <summary>The bend helpers as the clips keyed them, the rule not run after the legs' IK. <c>--bend-rule-off</c>.</summary>
    public bool BendRuleOff { get; set; }

    /// <summary>
    /// A person's feet for a run: the earlier build's with <see cref="FeetOff"/> or <c>--feet-off</c> (the heel lift then off too unless
    /// <c>--heel-lift</c> is given), else the locks and the lift from these settings; the bend rule unless <see cref="BendRuleOff"/> or
    /// <c>--bend-rule-off</c>. A flag's number out of its sense (the lock 0.02 to 1 s, the replant 0.05 to 1 m, the lift 0 to 60°) is refused,
    /// a setting's is the default.
    /// </summary>
    public Animation.People.PersonFeetSettings PersonFeetFor(Cli cli)
    {
        const float D = Core.DetMath.DegToRad;
        var defaults = new Animation.People.PersonFeetSettings();
        bool old = FeetOff || cli.Flag("--feet-off");
        float Given(string flag, float now, float fallback, float least, float most)
        {
            if (cli.Arg(flag) is not { } text) return now >= least && now <= most ? now : fallback;
            if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) || !(v >= least && v <= most))
                throw new SessionException($"{flag} is a number from {least} to {most}, not '{text}'");
            return v;
        }
        var f = old ? Animation.People.PersonFeetSettings.Off : defaults;
        return f with
        {
            LockSeconds = Given("--foot-lock-seconds", FootLockSeconds, defaults.LockSeconds, 0.02f, 1f),
            Replant = Given("--foot-replant", FootReplant, defaults.Replant, 0.05f, 1f),
            HeelLift = old && cli.Arg("--heel-lift") is null ? 0f : Given("--heel-lift", HeelLift, defaults.HeelLift * Core.DetMath.RadToDeg, 0f, 60f) * D,
            Bend = !(BendRuleOff || cli.Flag("--bend-rule-off")),
        };
    }
    // ---- end

    // ---- the people on stairs and slopes ----
    /// <summary>The earlier build's people on stairs: the drawn body and the camera on the capsule's steps (no smoothed line). <c>--stair-line-off</c>.</summary>
    public bool StairLineOff { get; set; }
    /// <summary>The earlier build's people on slopes: the flat walk and run on any grade (no slope clips). <c>--slope-clips-off</c>.</summary>
    public bool SlopeClipsOff { get; set; }
    /// <summary>How quickly the drawn body follows the stair's line (s, the spring's half-life; 0.1). <c>--stairs-half-life</c>.</summary>
    public float StairsHalfLife { get; set; } = 0.1f;
    /// <summary>How long a stretch of ground the stair's line averages along the way (m; 0.6). <c>--stairs-window</c>.</summary>
    public float StairsWindow { get; set; } = 0.6f;

    /// <summary>
    /// The people on stairs and slopes for a run: the stairs' line unless <see cref="StairLineOff"/> or <c>--stair-line-off</c>, the slope
    /// clips unless <see cref="SlopeClipsOff"/> or <c>--slope-clips-off</c>, the line's half-life and window from these settings or their flags. A
    /// flag's number out of its sense (the half-life 0 to 0.5 s, the window 0.1 to 2 m) is refused, a setting's is the default.
    /// </summary>
    public Animation.People.PersonGroundSettings PersonGroundFor(Cli cli)
    {
        var defaults = new Animation.People.PersonGroundSettings();
        float Given(string flag, float now, float fallback, float least, float most)
        {
            if (cli.Arg(flag) is not { } text) return now >= least && now <= most ? now : fallback;
            if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) || !(v >= least && v <= most))
                throw new SessionException($"{flag} is a number from {least} to {most}, not '{text}'");
            return v;
        }
        return defaults with
        {
            Stairs = !(StairLineOff || cli.Flag("--stair-line-off")),
            Slopes = !(SlopeClipsOff || cli.Flag("--slope-clips-off")),
            StairClips = !(StairClipsOff || cli.Flag("--stair-clips-off")),   // stair clips
            LineHalfLife = Given("--stairs-half-life", StairsHalfLife, defaults.LineHalfLife, 0f, 0.5f),
            Window = Given("--stairs-window", StairsWindow, defaults.Window, 0.1f, 2f),
        };
    }
    // ---- end

    // ---- the body drawn between ticks on stairs, the third-person camera at walls ----
    /// <summary>
    /// Draw a person between two ticks as before — the capsule's feet interpolated, the stair line's offset the last tick's — which
    /// drops the body by up to a riser for a frame each time the capsule steps one, at any draw rate that is not the tick's (the
    /// jittering and popping seen in play). Presentation only. <c>--stairs-draw-last-tick</c>.
    /// </summary>
    public bool StairsDrawLastTick { get; set; }

    /// <summary>
    /// The third-person camera at walls as before (the shoulder's pivot stopped against a wall, the boom's sphere stopped by any
    /// edge it grazes). <c>--camera-hard-walls</c>. Only the models repository's people have the calm walls (1.1's bodies keep the earlier build's camera).
    /// </summary>
    public bool CameraHardWalls { get; set; }
    // ---- end

    // ---- the skirt hung from the thighs; the stair clips ----
    /// <summary>
    /// The earlier build's people on stairs: the flat walk and run with the feet's IK on the treads, not the models repository's stair clips
    /// (<see cref="Animation.People.PersonGroundSettings.StairClips"/>). <c>--stair-clips-off</c>. A tape carries it in its note <c>ground</c>.
    /// </summary>
    public bool StairClipsOff { get; set; }

    /// <summary>The earlier build's starts and stops: the player's on the spring with the loops' fade, everyone else's on the ramp (no clips). <c>--start-clips-off</c>.</summary>
    public bool StartClipsOff { get; set; }
    /// <summary>The townsfolk on the earlier build's ramp (no natural start and stop clips; their simulation as before). <c>--crowd-start-clips-off</c>.</summary>
    public bool CrowdStartClipsOff { get; set; }
    /// <summary>To try: a stop starts at once (the clip whose phase is nearer), not waiting for its phase. <c>--stop-nearest</c>.</summary>
    public bool StopNearest { get; set; }

    /// <summary>The starts and stops for a run, from these settings and their flags.</summary>
    public Animation.People.PersonStartSettings PersonStartsFor(Cli cli)
    {
        bool off = StartClipsOff || cli.Flag("--start-clips-off");
        return new Animation.People.PersonStartSettings
        {
            Player = !off, Crowd = !off && !(CrowdStartClipsOff || cli.Flag("--crowd-start-clips-off")), WaitForPhase = !(StopNearest || cli.Flag("--stop-nearest")),
        };
    }

    /// <summary>
    /// A person's skirt hung from the two thighs (the townsman's and the watchman's tunic, the guard's hauberk) drawn as before,
    /// each half rigid with its own thigh and the hem opening between them, rather than carried by both (<see cref="Animation.People.SkirtRule"/>).
    /// Presentation only. <c>--skirt-split</c>.
    /// </summary>
    public bool SkirtSplit { get; set; }
    // ---- end

    // ---- the swing bones' simulator ----
    /// <summary>
    /// The people's swing bones (the skirts, the dresses, the habits, the braids) drawn as before — no simulator: a skirt hung from
    /// the thighs by the skirt rule, the rest at their rest — rather than swung by the models repository's simulator
    /// (<see cref="Animation.People.SwingRule"/>). Presentation only. <c>--swing-off</c>.
    /// </summary>
    public bool SwingOff { get; set; }
    /// <summary>Within this of the eye a person's swing is stepped 60 times a second (m; 15). <c>--swing-near</c>.</summary>
    public float SwingNear { get; set; } = Animation.People.PersonSwingSettings.Default.NearMetres;
    /// <summary>To this from the eye 30 times a second, past it not at all (drawn as before) (m; 30). <c>--swing-far</c>.</summary>
    public float SwingFar { get; set; } = Animation.People.PersonSwingSettings.Default.FarMetres;
    /// <summary>
    /// The swing drawn between its steps as before — the tails carried by their heads alone, not kept out of the legs (a running
    /// thigh came through the tunic on those ticks). Presentation only. <c>--swing-keep-out-off</c>.
    /// </summary>
    public bool SwingKeepOutOff { get; set; }

    /// <summary>
    /// The swing for a run: off with <see cref="SwingOff"/> or <c>--swing-off</c>; the distances from these settings or their flags. A
    /// flag's number out of its sense (0 to 200 m, the far not nearer than the near) is refused, a setting's is the default.
    /// </summary>
    public Animation.People.PersonSwingSettings PersonSwingFor(Cli cli)
    {
        var defaults = Animation.People.PersonSwingSettings.Default;
        if (SwingOff || cli.Flag("--swing-off")) return Animation.People.PersonSwingSettings.Off;
        float Given(string flag, float now, float fallback)
        {
            if (cli.Arg(flag) is not { } text) return now is >= 0f and <= 200f ? now : fallback;
            if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) || !(v is >= 0f and <= 200f))
                throw new SessionException($"{flag} is a distance from 0 to 200 m, not '{text}'");
            return v;
        }
        float near = Given("--swing-near", SwingNear, defaults.NearMetres), far = Given("--swing-far", SwingFar, defaults.FarMetres);
        return defaults with { NearMetres = near, FarMetres = MathF.Max(near, far), KeepOutBetweenSteps = !(SwingKeepOutOff || cli.Flag("--swing-keep-out-off")) };
    }
    // ---- end

    // ---- the models repository (the people's sprint, the crouched capsule) ----
    /// <summary>
    /// An earlier build: from this ground speed a person running ahead plays the models repository's sprint, stride-matched
    /// (m/s; 4.25, midway between the run and the sprint; 0 the run at every speed, as before). <c>--sprint-from</c>.
    /// </summary>
    public float SprintFrom { get; set; } = 4.25f;
    /// <summary>A person's sprint as before, the run's blend capped at 1.35× (the feet skate). <c>--sprint-clip-off</c>.</summary>
    public bool SprintClipOff { get; set; }
    /// <summary>
    /// An earlier build: the crouched capsule of the models repository's bodies (m; 1.15, every crouch clip's head at or below
    /// 1.10 m). <c>--person-crouch-height</c>.
    /// </summary>
    public float PersonCrouchHeight { get; set; } = PeopleBodies.CrouchHeight;

    /// <summary>
    /// The people's sprint for a run (<see cref="Animation.AnimationSettings.PersonSprintFrom"/>): 0 with <see cref="SprintClipOff"/>
    /// or <c>--sprint-clip-off</c>, else <c>--sprint-from</c> over the setting. A flag out of its sense (0 to 30 m/s) is refused, a setting's
    /// is the default.
    /// </summary>
    public float SprintFromFor(Cli cli)
    {
        if (SprintClipOff || cli.Flag("--sprint-clip-off")) return 0f;
        float fallback = new Animation.AnimationSettings().PersonSprintFrom;
        if (cli.Arg("--sprint-from") is not { } text) return SprintFrom is >= 0f and <= 30f ? SprintFrom : fallback;
        if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) || !(v is >= 0f and <= 30f))
            throw new SessionException($"--sprint-from is a speed from 0 to 30 m/s (0: the run at every speed), not '{text}'");
        return v;
    }

    /// <summary>
    /// The crouched capsule of the models repository's bodies for a run: <c>--person-crouch-height</c> over the setting. A flag out of
    /// its sense (<see cref="PeopleBodies.LeastCrouchHeight"/> to <see cref="PeopleBodies.MostCrouchHeight"/> m) is refused, a setting's is the default.
    /// </summary>
    public float PersonCrouchHeightFor(Cli cli)
    {
        const float least = PeopleBodies.LeastCrouchHeight, most = PeopleBodies.MostCrouchHeight;
        if (cli.Arg("--person-crouch-height") is not { } text) return PersonCrouchHeight is >= least and <= most ? PersonCrouchHeight : PeopleBodies.CrouchHeight;
        if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) || !(v is >= least and <= most))
            throw new SessionException($"--person-crouch-height is a height from {least} to {most} m, not '{text}'");
        return v;
    }
    // ---- end.1

    /// <summary>The layout of the file (<see cref="Upgrade"/>): 0 earlier, 1 earlier, 2 earlier, 3 earlier, 4 earlier, then <see cref="CurrentVersion"/>.</summary>
    public int SettingsVersion { get; set; }
    public const int CurrentVersion = 5;

    public WindowMode Window { get; set; } = WindowMode.Centered;
    /// <summary>The window's size when centred or windowed (a centred one larger than the display is cut to it).</summary>
    public int WindowWidth { get; set; } = 2560;
    public int WindowHeight { get; set; } = 1440;
    /// <summary>
    /// The picture's size when the window is borderless or windowed (the world is drawn at it and fitted to the
    /// window, centred); 0 for the window's own. A centred window draws at its own size unless <c>--picture</c> says otherwise.
    /// </summary>
    public int PictureWidth { get; set; } = 2560;
    public int PictureHeight { get; set; } = 1440;
    /// <summary>The GPU driver (vulkan, direct3d12) or empty for the renderer's choice.</summary>
    public string Driver { get; set; } = "";
    /// <summary>The renderer's quality preset name, or empty for its default.</summary>
    public string Quality { get; set; } = "";
    public bool VSync { get; set; } = true;

    // ---- the frame cap ----
    /// <summary>
    /// The most pictures a second the window draws (0, the default: no cap), rounded to an even fraction of the display's refresh
    /// (<see cref="FrameCap.Effective"/>: 60 asked is 72 on a 144 Hz display, 60 on 60 or 120 Hz). Presentation only: the simulation's fixed
    /// tick is untouched, and a tape is the same whatever it is. <c>--frame-cap &lt;fps&gt;</c>.
    /// </summary>
    public float FrameCap { get; set; }

    /// <summary>The frame cap asked for a run (pictures a second; 0 none): <c>--frame-cap</c>, else the setting (out of its sense: none).</summary>
    public float FrameCapFor(Cli cli)
    {
        if (cli.Arg("--frame-cap") is not { } text) return FrameCap is > 0f and <= 1000f ? FrameCap : 0f;
        if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) || !(v is >= 0f and <= 1000f))
            throw new SessionException($"--frame-cap is pictures a second from 0 (none) to 1000, not '{text}'");
        return v;
    }
    // ---- end

    // ---- the jumps, the fall and the landings on the models repository's clips ----
    /// <summary>
    /// The earlier build's jumps, fall and landings: the stand-ins in the air and at a hard landing, the rig's dip, the capsule leaving on the press — not the
    /// The models repository's clips (<see cref="Animation.People.PersonAirSettings.Clips"/>). <c>--air-clips-off</c>. A tape carries it in its note <c>air</c>.
    /// </summary>
    public bool AirClipsOff { get; set; }
    /// <summary>
    /// The player's capsule leaves on the press of jump as before, the jump's clip entered at its take-off (no squash drawn), rather than after
    /// the clip's squash (<see cref="Animation.People.PersonAirSettings.Squash"/>). <c>--jump-at-once</c>. A tape carries it.
    /// </summary>
    public bool JumpAtOnce { get; set; }

    /// <summary>The jumps, the fall and the landings for a run, from these settings and their flags.</summary>
    public Animation.People.PersonAirSettings PersonAirFor(Cli cli)
    {
        if (AirClipsOff || cli.Flag("--air-clips-off")) return Animation.People.PersonAirSettings.Off;
        return new Animation.People.PersonAirSettings { Squash = !(JumpAtOnce || cli.Flag("--jump-at-once")) };
    }
    // ---- end

    // ---- getting over and onto things, off edges, on the models repository's clips ----
    /// <summary>
    /// The earlier build's moves over and onto things (the traversal's own paths and stand-ins), off edges and landings moving on — not the models repository's
    /// to 45b clips (<see cref="Animation.People.PersonOverSettings.Clips"/>). <c>--over-clips-off</c>. A tape carries it in its note <c>over</c>.
    /// </summary>
    public bool OverClipsOff { get; set; }

    /// <summary>The moves over and onto things and off edges for a run, from these settings and their flags (off too with the earlier build's air).</summary>
    public Animation.People.PersonOverSettings PersonOverFor(Cli cli)
    {
        if (OverClipsOff || cli.Flag("--over-clips-off") || !PersonAirFor(cli).Any) return Animation.People.PersonOverSettings.Off;
        return new Animation.People.PersonOverSettings { Auto = !(AutoVaultOff || cli.Flag("--auto-vault-off")) };   // the run's own vault
    }
    // ---- end

    // ---- the run's and the sprint's own vault on the clips; the hands on the tops ----
    /// <summary>
    /// The earlier build's vaults at a pace — a sprint's the sprint vault and its stand-in, a run stopped at the thing until a tap — not the run's and the
    /// sprint's own vault on the clips (<see cref="Animation.People.PersonOverSettings.Auto"/>). <c>--auto-vault-off</c>. A tape carries it in its note
    /// <c>over</c> (<c>auto=1</c>).
    /// </summary>
    public bool AutoVaultOff { get; set; }

    /// <summary>
    /// The earlier build's hands in the moves over and onto things — as the clips have them, over or under the real top by the height's stretch — not put on the
    /// real top by the arms' IK (<see cref="Animation.People.PersonHandsSettings.OnTops"/>). <c>--hands-on-tops-off</c>. A tape carries it in its note <c>hands</c>.
    /// </summary>
    public bool HandsOnTopsOff { get; set; }

    /// <summary>The hands for a run, from these settings and their flags (off too with the earlier build's moves or the earlier build's air).</summary>
    public Animation.People.PersonHandsSettings PersonHandsFor(Cli cli)
    {
        if (HandsOnTopsOff || cli.Flag("--hands-on-tops-off") || !PersonOverFor(cli).Any) return Animation.People.PersonHandsSettings.Off;
        return new Animation.People.PersonHandsSettings();
    }

    /// <summary>The orbit camera's double tap (s: a second press within it puts the camera back behind the player).</summary>
    public float OrbitDoubleTap { get; set; } = 0.3f;

    /// <summary>The orbit camera's tilt's limits (degrees: the lowest looking down from over the player, the highest looking up from low behind).</summary>
    public float OrbitLowestPitch { get; set; } = -75f;
    public float OrbitHighestPitch { get; set; } = 35f;

    /// <summary>How near and far the wheel brings the third-person camera (m from its pivot; the default 2.5), and a notch's step (m).</summary>
    public float OrbitNearest { get; set; } = 1.0f;
    public float OrbitFarthest { get; set; } = 6.0f;
    public float OrbitZoomStep { get; set; } = 0.25f;

    /// <summary>How far the mouse swings the orbit camera (a share of how far it turns the player).</summary>
    public float OrbitSpeed { get; set; } = 1.0f;

    /// <summary>The orbit camera's numbers from these settings (each kept within reason).</summary>
    public Cameras.OrbitCameraSettings OrbitCameraFor()
    {
        float nearest = Math.Clamp(OrbitNearest, 0.3f, 20f), farthest = Math.Clamp(OrbitFarthest, nearest, 30f);
        float lowest = Math.Clamp(OrbitLowestPitch, -89f, 0f), highest = Math.Clamp(OrbitHighestPitch, 0f, 89f);
        return new Cameras.OrbitCameraSettings
        {
            DoubleTapSeconds = Math.Clamp(OrbitDoubleTap, 0.05f, 1f), LowestPitchDegrees = lowest, HighestPitchDegrees = highest,
            NearestMetres = nearest, FarthestMetres = farthest, ZoomStepMetres = Math.Clamp(OrbitZoomStep, 0.01f, 5f), Speed = Math.Clamp(OrbitSpeed, 0.05f, 10f),
        };
    }
    // ---- end

    // ---- the ledge on the models repository's clips ----
    /// <summary>
    /// The earlier build's ledge (the hang, shimmy, pull-up, drop and grabs on the motor's own paths and stand-ins) — not the models repository's clips
    /// clips (<see cref="Animation.People.PersonLedgeSettings.Clips"/>). <c>--ledge-clips-off</c>. A tape carries it in its note <c>ledge</c>.
    /// </summary>
    public bool LedgeClipsOff { get; set; }

    /// <summary>The ledge for a run, from these settings and their flags (off too with the earlier build's moves or the earlier build's air).</summary>
    public Animation.People.PersonLedgeSettings PersonLedgeFor(Cli cli)
    {
        if (LedgeClipsOff || cli.Flag("--ledge-clips-off") || !PersonOverFor(cli).Any) return Animation.People.PersonLedgeSettings.Off;
        return new Animation.People.PersonLedgeSettings();
    }
    // ---- end

    // ---- the prelanding on the models repository's clips ----
    /// <summary>
    /// The earlier build's landings from the fall (the contact blend) — not the models repository's prelandings before a standing landing
    /// (<see cref="Animation.People.PersonPrelandingSettings.Clips"/>). <c>--prelanding-off</c>. A tape carries it in its note <c>prelanding</c>.
    /// </summary>
    public bool PrelandingOff { get; set; }

    /// <summary>
    /// The prelanding for a run, from these settings and their flags (off too with the earlier build's moves or the earlier build's air). a short fall's by
    /// <see cref="PrelandingShort"/> (<c>--prelanding-short</c>) and <see cref="PrelandingCutoff"/>, the fade in game time; with <c>commitOff</c>, the earlier build's.
    /// </summary>
    public Animation.People.PersonPrelandingSettings PersonPrelandingFor(Cli cli)
    {
        if (PrelandingOff || cli.Flag("--prelanding-off") || !PersonOverFor(cli).Any) return Animation.People.PersonPrelandingSettings.Off;
        var s = new Animation.People.PersonPrelandingSettings();
        if (CommitOff || cli.Flag("--commit-off")) return s.WithoutCommit;
        string mode = cli.Arg("--prelanding-short") ?? PrelandingShort ?? "cutoff";
        var shortFall = mode.Trim().ToLowerInvariant() switch
        {
            "partway" or "part-way" => Animation.People.ShortFall.Partway,
            "faster" => Animation.People.ShortFall.Faster,
            "cutoff" or "" => Animation.People.ShortFall.Cutoff,
            _ => cli.Arg("--prelanding-short") is not null
                ? throw new SessionException($"--prelanding-short is cutoff, partway or faster, not '{mode}'")
                : Animation.People.ShortFall.Cutoff,
        };
        return s with { Short = shortFall, CutoffSeconds = PrelandingCutoff is >= 0f and <= 1f ? PrelandingCutoff : 0.2f };
    }
    // ---- end

    // ---- the commit point in the air (from play) ----
    /// <summary>
    /// The earlier build's air — the motor's 0.19 s stand-in grab from a jump, a falling grab with jump held, the free hang before the brace, the prelanding
    /// faster to fit a short fall with its fade in its own time — not the commit point (<see cref="Animation.People.PersonCommitSettings"/>) and the fall's own fade.
    /// <c>--commit-off</c>. A tape carries it in its notes <c>commit</c> and <c>prelanding</c>.
    /// </summary>
    public bool CommitOff { get; set; }

    /// <summary>How long before the contact foreseen the body in the air commits (s; "about 0.3 s"). <c>--commit-lead</c>.</summary>
    public float CommitLead { get; set; } = 0.3f;

    /// <summary>A jump taken at a wall catches its lip by itself (else only with use held).</summary>
    public bool AutoCatch { get; set; } = true;

    /// <summary>The cone about the jump's way (degrees each side) in which the lip met is the one the jump was taken at.</summary>
    public float AutoCatchCone { get; set; } = 60f;

    /// <summary>
    /// The earlier build's commit point — no catch on a jump's descent of a lip under its take-off, none at the wall past the arc's top, the catch's lip, room and path
    /// looked at again when it begins, the 30° cone — not the earlier build's (<see cref="Animation.People.PersonCommitSettings.Version"/>). <c>--commit-rules-1</c>. A tape carries
    /// it in its note <c>commit</c> (<c>v</c>).
    /// </summary>
    public bool CommitRules1 { get; set; }

    /// <summary>Of a lip above the arc's reach, how much the arms take up at the contact (m); the capsule is lifted over its arc by the rest.</summary>
    public float CatchArmReach { get; set; } = 0.12f;

    /// <summary>Braced by default: how long before a free-ending grab's end the brace begins where there is a wall under the edge (s; 0: at its end).</summary>
    public float BraceOverlap { get; set; } = 0.45f;

    /// <summary>
    /// The earlier build's climb — a grab waited out (its settle, the brace, the hang) before the pull-up even when the player holds on, the pull-up from its first
    /// frame at its own rate — not the earlier build's straight-on (<see cref="Animation.People.PersonCommitSettings.Climb"/>). <c>--climb-wait-out</c>. A tape carries it in
    /// its note <c>commit</c> (<c>climb</c>, <c>pull</c>).
    /// </summary>
    public bool ClimbWaitOut { get; set; }

    /// <summary>The rate the pull-up plays at when the player goes on from a grab (1 to 2; 1.3: about 1.2 s); from a hang with a fresh press, its own.</summary>
    public float PullUpRate { get; set; } = 1.3f;

    /// <summary>A fall shorter than the prelanding's lead: <c>cutoff</c> (none below <see cref="PrelandingCutoff"/>), <c>partway</c> (the models repository's) or <c>faster</c>. <c>--prelanding-short</c>.</summary>
    public string PrelandingShort { get; set; } = "cutoff";

    /// <summary>With <c>cutoff</c>, below this much air (s) no prelanding.</summary>
    public float PrelandingCutoff { get; set; } = 0.2f;

    /// <summary>The commit point for a run, from these settings and their flags (off with <c>commitOff</c>, and with the ledge's clips off).</summary>
    public Animation.People.PersonCommitSettings PersonCommitFor(Cli cli)
    {
        if (CommitOff || cli.Flag("--commit-off") || !PersonLedgeFor(cli).Any) return Animation.People.PersonCommitSettings.Off;
        float lead = CommitLead is >= 0.05f and <= 1f ? CommitLead : 0.3f;
        if (cli.Arg("--commit-lead") is { } text)
        {
            if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) || !(v is >= 0.05f and <= 1f))
                throw new SessionException($"--commit-lead is a time from 0.05 to 1 s, not '{text}'");
            lead = v;
        }
        var settings = new Animation.People.PersonCommitSettings
        {
            LeadSeconds = lead, AutoCatch = AutoCatch, AutoConeDegrees = AutoCatchCone is >= 0f and <= 90f ? AutoCatchCone : 60f,
            ArmReach = CatchArmReach is >= 0f and <= 0.5f ? CatchArmReach : 0.12f, BraceOverlap = BraceOverlap is >= 0f and <= 1.2f ? BraceOverlap : 0.45f,
            PullUpRate = PullUpRate is >= 1f and <= 2f ? PullUpRate : 1.3f,
        };
        if (ClimbWaitOut || cli.Flag("--climb-wait-out")) settings = settings.WaitOutClimb;   // The earlier build's climb
        return CommitRules1 || cli.Flag("--commit-rules-1") ? settings.AsRules1 : settings;   // The earlier build's rules and cone
    }
    // ---- end

    // ---- tier 1 on the engine, part 1 ----
    /// <summary>
    /// The earlier build's moves — every grab ending as before (the free twin, then the earlier build's brace overlap), the run's step-over and the sprint vault's stand-ins for the
    /// low things, ladders on the earlier build's stand-in — not tier 1's (<see cref="Animation.People.PersonMovesSettings"/>). <c>--moves-off</c>. A tape carries it in its
    /// note <c>moves</c>.
    /// </summary>
    public bool MovesOff { get; set; }

    /// <summary>Sprint held on a ladder plays its climb this much faster (1 to 3; 2.0; the default asked of him). on top of <see cref="LadderClimbRate"/>, at most 3.</summary>
    public float LadderSprintRate { get; set; } = 2f;

    /// <summary>A ladder's climb this much faster than its clips' own (1 to 3; 1.5: 0.87 s a cycle of two rungs, about 0.69 m/s).</summary>
    public float LadderClimbRate { get; set; } = 1.5f;

    /// <summary>A ladder's getting-on, getting-off and the transitions to a landing or a hang this much faster than their clips' own (1 to 3; 1.5).</summary>
    public float LadderMoveRate { get; set; } = 1.5f;

    /// <summary>
    /// How far a level's ladder may lean off a clip set's own (0° upright, 15° leaning) and still be climbed on it (degrees, 0.5 to 7; 2: levels build
    /// 0° or 15° exactly, anything else the stand-in).
    /// </summary>
    public float LadderLeanTolerance { get; set; } = 2f;

    /// <summary>
    /// the most the braced feet's toes bend up against a wall (degrees, 0 to 40; 40 the clips' soft shoes); under 40 the braced foot is moved
    /// back from the wall by the models repository's table. A hook for the outfits' shoes (not there yet). Presentation only.
    /// </summary>
    public float ToeBendLimitDeg { get; set; } = 40f;

    /// <summary>Tier 1's moves for a run, from these settings and their flag (off with the commit point).</summary>
    public Animation.People.PersonMovesSettings PersonMovesFor(Cli cli)
    {
        if (MovesOff || cli.Flag("--moves-off") || !PersonCommitFor(cli).Any) return Animation.People.PersonMovesSettings.Off;
        return new Animation.People.PersonMovesSettings
        {
            LadderSprintRate = LadderSprintRate is >= 1f and <= 3f ? LadderSprintRate : 2f,
            LadderClimbRate = LadderClimbRate is >= 1f and <= 3f ? LadderClimbRate : 1.5f,
            LadderMoveRate = LadderMoveRate is >= 1f and <= 3f ? LadderMoveRate : 1.5f,
            LeanToleranceDegrees = LadderLeanTolerance is >= 0.5f and <= 7f ? LadderLeanTolerance : 2f,
            ToeBendLimitDegrees = ToeBendLimitDeg is >= 0f and <= 40f ? ToeBendLimitDeg : 40f,
        };
    }
    // ---- end

    // ---- tier 1 on the engine, part 2 ----
    /// <summary>
    /// The earlier build's hang — a shimmy stops at a corner, no leaps (jump pulls up, jump with back held pushes off, crouch drops) — not the corners and leaps on the
    /// models repository's clips (<see cref="Animation.People.PersonHangSettings"/>). <c>--hang-moves-off</c>. A tape carries it in its note <c>hang</c>.
    /// </summary>
    public bool HangMovesOff { get; set; }

    /// <summary>How far a leap's target lip may be off the clip's own offset (along the lip, up or down, the leap back's distance) and still be leapt to (m, 0.05 to 0.5; 0.25).</summary>
    public float LeapTolerance { get; set; } = 0.25f;

    /// <summary>How much nearer than its clip's own (outside 0.40 m, inside 0.50 from the hang's middle) a corner may be and still be gone round (m, 0 to 0.3; 0.15).</summary>
    public float CornerSlack { get; set; } = 0.15f;

    /// <summary>How far a corner's turn off a quarter, or a facing ledge's face off square, may be and still be taken (degrees, 1 to 30; 15).</summary>
    public float HangAngleTolerance { get; set; } = 15f;

    /// <summary>The hang's corners and leaps for a run, from these settings and their flag (off with tier 1's moves).</summary>
    public Animation.People.PersonHangSettings PersonHangFor(Cli cli)
    {
        if (HangMovesOff || cli.Flag("--hang-moves-off") || !PersonMovesFor(cli).Any) return Animation.People.PersonHangSettings.Off;
        return new Animation.People.PersonHangSettings
        {
            LeapTolerance = LeapTolerance is >= 0.05f and <= 0.5f ? LeapTolerance : 0.25f,
            CornerSlack = CornerSlack is >= 0f and <= 0.3f ? CornerSlack : 0.15f,
            AngleToleranceDegrees = HangAngleTolerance is >= 1f and <= 30f ? HangAngleTolerance : 15f,
            Breaks = !(WallsOff || cli.Flag("--walls-off")),   // a side leap only where the lip breaks; --walls-off: the earlier build's leap along a lip that goes on
        };
    }
    // ---- end

    // ---- tier 1 on the engine, part 3 ----
    /// <summary>
    /// The earlier build's — no wall climbed (a tap at one jumps), and a side leap along a lip that goes on (the breaks off) — not the walls on the models repository's clips
    /// (<see cref="Animation.People.PersonWallSettings"/>). <c>--walls-off</c>. A tape carries it in its notes <c>walls</c> and <c>hang</c> (<c>breaks=</c>).
    /// </summary>
    public bool WallsOff { get; set; }

    /// <summary>How far a hand or a foot is moved onto a level's own hold, and a move whose end has a limb with none that near is refused (m, 0.05 to 0.4; 0.25).</summary>
    public float WallHoldReach { get; set; } = 0.25f;

    /// <summary>
    /// The wall's moves this much faster than their clips' own (1 to 3). 1.3 (the clips' rate): with the flow a held quick climb makes 2.8
    /// hand moves a second, within a human speed climber's 2.5 to 2.9 (1.5, the brief's, made 3.2); an older file that says the earlier build's 1 is brought to 1.3,
    /// once (<see cref="Load"/>).
    /// </summary>
    public float WallClimbRate { get; set; } = 1.3f;

    /// <summary>
    /// The earlier build's climbing exactly — the wall's clips at their own rates, no flow, no diagonals, the catches as before — not the earlier build's
    /// (<see cref="Animation.People.PersonWallSettings.NoFlow"/>). <c>--walls-no-flow</c>. A tape carries it in its note <c>walls</c> (the earlier build's has no <c>flow=</c>).
    /// </summary>
    public bool WallsNoFlow { get; set; }

    /// <summary>The getting-on and off, the let-go and push-off, over the top and the pull-up after it, and the corners this much faster than their clips' own (1 to 3; 1.5).</summary>
    public float WallMoveRate { get; set; } = 1.5f;

    /// <summary>The flow's blend (s, 0.1 to 0.3; 0.15): a held move out of the getting-on and out of the move before, the capsule's difference and the animation's fade over it.</summary>
    public float WallFlowBlend { get; set; } = 0.15f;

    /// <summary>Forward or back with a side held climbs diagonally (on).</summary>
    public bool WallDiagonals { get; set; } = true;

    /// <summary>How far a splayed hand's wrist may be bent back on a face with nothing to hold (degrees, 30 to 70; 50, the models repository's "about 50"). Presentation only.</summary>
    public float SplayWristLimit { get; set; } = 50f;

    /// <summary>The walls for a run, from these settings and their flag (off with the hang's corners and leaps).</summary>
    public Animation.People.PersonWallSettings PersonWallFor(Cli cli)
    {
        if (WallsOff || cli.Flag("--walls-off") || !PersonHangFor(cli).Any) return Animation.People.PersonWallSettings.Off;
        var walls = new Animation.People.PersonWallSettings
        {
            HoldReach = WallHoldReach is >= 0.05f and <= 0.4f ? WallHoldReach : 0.25f,
            Rate = WallClimbRate is >= 1f and <= 3f ? WallClimbRate : 1.3f,
            WristLimitDegrees = SplayWristLimit is >= 30f and <= 70f ? SplayWristLimit : 50f,
            MoveRate = WallMoveRate is >= 1f and <= 3f ? WallMoveRate : 1.5f,
            FlowBlend = WallFlowBlend is >= 0.1f and <= 0.3f ? WallFlowBlend : 0.15f,
            Diagonals = WallDiagonals,
        };
        // --walls-no-flow: the earlier build's climbing exactly (the clips' own rates, no flow, no diagonals); the reach and the wrist as set.
        return WallsNoFlow || cli.Flag("--walls-no-flow") ? Animation.People.PersonWallSettings.NoFlow with { HoldReach = walls.HoldReach, WristLimitDegrees = walls.WristLimitDegrees } : walls;
    }
    // ---- end

    // ---- tier 1 on the engine, part 4 ----
    /// <summary>
    /// The earlier build's — a gap narrower than the body stops it, the stealth key does nothing, the walk into the crouch walk on the fifth of a second's fade — not
    /// squeezing, stealth with cover and the duck's blends (<see cref="Animation.People.PersonStealthSettings"/>). <c>--stealth-off</c>. A tape carries it in its note <c>stealth</c>.
    /// </summary>
    public bool StealthOff { get; set; }

    /// <summary>Stealth's pace walking or at the run (m/s, 0.3 to 0.8; 0.6, 46% of the 1.3 walk, on the sneak's clip); sprint held, the walk's.</summary>
    public float StealthPace { get; set; } = 0.6f;

    /// <summary>The crouched walk's pace in stealth (m/s, 0.3 to 1; 0.6: its clips played at 0.6 of their rate).</summary>
    public float StealthCrouchPace { get; set; } = 0.6f;

    /// <summary>How far stealth lowers the hips, by a blend toward the crouch's pose (m, 0 to 0.2; 0.11, the cover's).</summary>
    public float StealthLower { get; set; } = 0.11f;

    /// <summary>Walking ahead in stealth on the models repository's sneak (<c>walk_sneak</c>; true); false: the walk's blends, slowed and lowered.</summary>
    public bool StealthSneak { get; set; } = true;

    /// <summary>How far the back may be off a wall behind for the body in stealth to take cover against it (m, 0.1 to 0.5; 0.3).</summary>
    public float CoverReach { get; set; } = 0.3f;

    /// <summary>The walk (or the run) into the crouch walk on a phase-matched blend this long (s, 0.1 to 1; 0.4), and back out (<see cref="DuckOut"/>, 0.6).</summary>
    public float DuckIn { get; set; } = 0.4f;
    public float DuckOut { get; set; } = 0.6f;

    /// <summary>Squeezes quick: the quarter turns into and out of a squeeze this much faster than made (1 to 2; 1.5).</summary>
    public float SqueezeTurnRate { get; set; } = 1.5f;

    /// <summary>No locked-in clip: a side-step's cycle let go of runs to its nearer end this much faster (1 to 3; 2).</summary>
    public float SideStopRate { get; set; } = 2f;

    /// <summary>The keys' way along a squeeze or a cover kept this long after they let go (s, 0 to 0.25; 0.1).</summary>
    public float SideHold { get; set; } = 0.1f;

    /// <summary>Squeezing, stealth, cover and the duck's blends for a run, from these settings and their flag (off with the walls).</summary>
    public Animation.People.PersonStealthSettings PersonStealthFor(Cli cli)
    {
        if (StealthOff || cli.Flag("--stealth-off") || !PersonWallFor(cli).Any) return Animation.People.PersonStealthSettings.Off;
        static float In(float v, float lo, float hi, float d) => v >= lo && v <= hi ? v : d;
        return new Animation.People.PersonStealthSettings
        {
            Pace = In(StealthPace, 0.3f, 0.8f, 0.6f),
            CrouchPace = In(StealthCrouchPace, 0.3f, 1f, 0.6f),
            Lower = In(StealthLower, 0f, 0.2f, 0.11f),
            Sneak = StealthSneak,
            CoverReach = In(CoverReach, 0.1f, 0.5f, 0.3f),
            DuckIn = In(DuckIn, 0.1f, 1f, 0.4f),
            DuckOut = In(DuckOut, 0.1f, 1f, 0.6f),
            TurnRate = In(SqueezeTurnRate, 1f, 2f, 1.5f),
            StopRate = In(SideStopRate, 1f, 3f, 2f),
            Hold = In(SideHold, 0f, 0.25f, 0.1f),
        };
    }
    // ---- end

    // ---- the player's gaits, quicker stops, the lean, the look ----
    /// <summary>
    /// The earlier build's walk, run, sprint, starts and stops for the player (the townsfolk's paces and clips, a stop waiting for its phase) — not the models repository's
    /// newer sets (<see cref="Animation.People.PersonGaitSettings"/>). <c>--player-gaits-off</c>. A tape carries it in its note <c>gaits</c>.
    /// </summary>
    public bool PlayerGaitsOff { get; set; }

    /// <summary>The player's speed (m/s, 2.2 to 3.6; 2.8) from which the run is drawn rather than the walk.</summary>
    public float PlayerRunFrom { get; set; } = 2.8f;

    /// <summary>The player's speed (m/s, 4.2 to 5.6; 4.8) from which the sprint is drawn rather than the run.</summary>
    public float PlayerSprintFrom { get; set; } = 4.8f;

    /// <summary>How long (s, 0.05 to 0.4; 0.2) a stop taken at once blends the loop's pose (at most an eighth of a stride off its way in) into its own.</summary>
    public float StopBlend { get; set; } = 0.2f;

    /// <summary>The player's own gaits for a run, from these settings and their flag (off with the starts).</summary>
    public Animation.People.PersonGaitSettings PersonGaitsFor(Cli cli)
    {
        if (PlayerGaitsOff || cli.Flag("--player-gaits-off") || !PersonStartsFor(cli).Player) return Animation.People.PersonGaitSettings.Off;
        static float In(float v, float lo, float hi, float d) => v >= lo && v <= hi ? v : d;
        return new Animation.People.PersonGaitSettings
        {
            RunFrom = In(PlayerRunFrom, 2.2f, 3.6f, 2.8f), SprintFrom = In(PlayerSprintFrom, 4.2f, 5.6f, 4.8f), StopBlend = In(StopBlend, 0.05f, 0.4f, 0.2f),
        };
    }

    /// <summary>The player leaning into turns at a run and a sprint (presentation; true).</summary>
    public bool Lean { get; set; } = true;

    /// <summary>The lean's most at a sprint and at a run (degrees, 0 to 30; 18 and 12), its share walking (0 to 1; 0.25) and its ease in and out (s, 0.05 to 0.5; 0.15).</summary>
    public float LeanSprintDegrees { get; set; } = 18f;
    public float LeanRunDegrees { get; set; } = 12f;
    public float LeanWalkShare { get; set; } = 0.25f;
    public float LeanEase { get; set; } = 0.15f;

    /// <summary>The head looking where the body is going (presentation; true), at most so far aside and up or down (degrees, 0 to 45; 20 and 15).</summary>
    public bool HeadLook { get; set; } = true;
    public float LookYawDegrees { get; set; } = 20f;
    public float LookPitchDegrees { get; set; } = 15f;

    /// <summary>The head and chest leading the turn toward the way of travel while moving (the turn chain fed the way too; presentation; true).</summary>
    public bool LeadTurns { get; set; } = true;

    /// <summary>The ground states' changes (the gaits both ways, the starts, the stops, the crouch in and out) joined by inertialization (presentation; true), over so long (s, 0.05 to 0.5; 0.2).</summary>
    public bool InertialJoins { get; set; } = true;
    public float InertialJoinSeconds { get; set; } = 0.2f;

    /// <summary>The lean, the look, the chain led by the way and the inertialized joins for a run, from these settings and their flags (<c>--no-lean</c>, <c>--no-head-look</c>, <c>--no-lead-turns</c>, <c>--no-inertial-joins</c>).</summary>
    public Animation.People.PersonPresentSettings PersonPresentFor(Cli cli)
    {
        static float In(float v, float lo, float hi, float d) => v >= lo && v <= hi ? v : d;
        return new Animation.People.PersonPresentSettings
        {
            Lean = Lean && !cli.Flag("--no-lean"),
            LeanSprintDegrees = In(LeanSprintDegrees, 0f, 30f, 18f), LeanRunDegrees = In(LeanRunDegrees, 0f, 30f, 12f),
            LeanWalkShare = In(LeanWalkShare, 0f, 1f, 0.25f), LeanEase = In(LeanEase, 0.05f, 0.5f, 0.15f),
            Look = HeadLook && !cli.Flag("--no-head-look"),
            LookYawDegrees = In(LookYawDegrees, 0f, 45f, 20f), LookPitchDegrees = In(LookPitchDegrees, 0f, 45f, 15f),
            Lead = LeadTurns && !cli.Flag("--no-lead-turns"),
            Joins = InertialJoins && !cli.Flag("--no-inertial-joins"), JoinSeconds = In(InertialJoinSeconds, 0.05f, 0.5f, 0.2f),
        };
    }
    // ---- end of the gaits

    // ---- the characters' textures ----
    /// <summary>
    /// The largest side, in texels, a character's texture is uploaded at: 1024, 2048 (the default) or 4096 (the
    /// files' own); a larger texture is halved by a box filter until it fits (another number is taken as the largest of the three
    /// not above it). The models repository's people carry 4096² maps: the player's take about 395 MB of GPU memory at 4096, 203 MB
    /// at 2048 and 103 MB at 1024 (RGBA8 with mips). <c>--character-texture-limit</c> for one run. Presentation only.
    /// </summary>
    public int CharacterTextureLimit { get; set; } = 2048;

    /// <summary>
    /// The limit for this run: <c>--character-texture-limit</c>'s (<paramref name="flag"/>: 1024, 2048 or 4096;
    /// anything else is refused), else <see cref="CharacterTextureLimit"/> (another number taken as the largest of the three not
    /// above it).
    /// </summary>
    public int CharacterTextureLimitFor(string? flag)
    {
        if (flag is null) return Render.Characters.CharacterTextureSize.Limit(CharacterTextureLimit);
        return int.TryParse(flag, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n)
               && Array.IndexOf(Render.Characters.CharacterTextureSize.Limits, n) >= 0
            ? n : throw new SessionException($"--character-texture-limit is 1024, 2048 or 4096, not '{flag}'");
    }
    // ---- end ----

    /// <summary>Radians the view turns for a pixel of mouse movement.</summary>
    public float MouseSensitivity { get; set; } = 0.0022f;
    public bool InvertY { get; set; }
    /// <summary>
    /// The free-pointer key's setting earlier (<c>held</c> or <c>toggle</c>), read only to bring a file up to the
    /// held and toggled actions (<see cref="Upgrade"/>) and never written again: the freed pointer has <c>freePointer</c>
    /// (held) and <c>freePointerToggle</c> now (<see cref="HoldAndToggle"/>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PointerKey { get; set; }
    /// <summary>
    /// Which way the body faces in third person: <c>view</c> (the default: <i>"I want the player to
    /// always be turning to face the direction the camera is going"</i>; the legs turn toward the move) or <c>move</c> (it
    /// turns to the way it moves). A older file that says <c>move</c> is brought to <c>view</c> once.
    /// </summary>
    public ThirdPersonFacing ThirdPersonFacing { get; set; } = ThirdPersonFacing.View;

    /// <summary>
    /// Whether the legs turn toward the way the body moves when it faces elsewhere (the hips and legs turned, the spine
    /// turned back; <see cref="Animation.LegTurn"/>). On by default; <c>--no-leg-turn</c> turns it off for one run. Recorded
    /// in a tape's head.
    /// </summary>
    public bool LegTurn { get; set; } = true;

    /// <summary>
    /// Double tap to dodge (<i>"a good feature but should default off, I tend not to use them"</i>): a move
    /// key pressed, let go and pressed again within <see cref="DoubleTapSeconds"/> dodges that way. Off by default; on the
    /// Controls page. Recorded in a tape's head.
    /// </summary>
    public bool DoubleTapDodge { get; set; }

    /// <summary>Double tap's window (s; 0.25 by default, 0.1 to 0.6, else the default).</summary>
    public float DoubleTapSeconds { get; set; } = DefaultTapSeconds;

    /// <summary>Double tap's window by default (s) and the ends it is kept within.</summary>
    public const float DefaultTapSeconds = 0.25f, LeastTapSeconds = 0.1f, MostTapSeconds = 0.6f;

    /// <summary>A double tap's window as given, or the default when it is outside its ends.</summary>
    public static float TapWindow(float seconds) => seconds is >= LeastTapSeconds and <= MostTapSeconds ? seconds : DefaultTapSeconds;
    /// <summary>
    /// Always run (on by default): plain movement runs and the walk key walks; off, plain
    /// movement walks and the walk key runs. Recorded in a tape's head.
    /// </summary>
    public bool AlwaysRun { get; set; } = true;
    /// <summary>
    /// How much of the head's motion the first-person view has, in per cent: 0 the
    /// steady eye (the lean and the crouch, no bounce or sway), 100 the head's full motion; 50 by default.
    /// Presentation only: not recorded in a tape, so a replay is drawn with the viewer's own.
    /// </summary>
    public float HeadMotion { get; set; } = 50f;
    /// <summary>
    /// How far the head's own motion through an action — a swing, a block, a flinch, a
    /// dodge's sway — may carry the first-person eye, in centimetres (0 to 100, 4 by default:
    /// <see cref="Cameras.CameraSettings.ActionAllowance"/>; 100 is about the earlier build's eye, which rode the head). Not
    /// presentation: the simulation's eye (the reticle, use, aim) stands there, so a tape records it and a replay uses the
    /// recorded value.
    /// </summary>
    public float FirstPersonActionSway { get; set; } = 4f;
    /// <summary>
    /// The third-person camera's follow of the body's changes of speed, in seconds (0 to 0.3; 0.08 by
    /// default: <see cref="Cameras.CameraSettings.ThirdPersonFollowSeconds"/>; 0 rigid, as before). Recorded in a tape, as above.
    /// </summary>
    public float ThirdPersonFollow { get; set; } = 0.08f;
    /// <summary>
    /// A development control: the third-person <b>melee framing</b> — while a melee weapon or the fists are out
    /// the camera goes up over the head and to the middle, so a foe a metre or so ahead is seen over the player's head
    /// (<see cref="Cameras.CameraSettings.MeleeFraming"/>). Off by default. Recorded in a tape, as above.
    /// </summary>
    public bool ThirdPersonMeleeFraming { get; set; }
    /// <summary>The first-person field of view (vertical degrees, 60 to 110, 80 by default). Presentation only.</summary>
    public float FieldOfView { get; set; } = 80f;
    /// <summary>
    /// Which way the field of view is measured: <c>vertical</c> (the default; <see cref="FieldOfView"/>)
    /// or <c>horizontal</c> (<see cref="FieldOfViewHorizontal"/>, the picture's height then worked out from its shape, so a
    /// wider window keeps the same field across). Each keeps its own value, so switching back and forth does not drift.
    /// </summary>
    public FieldOfViewAxis FieldOfViewAxis { get; set; } = FieldOfViewAxis.Vertical;
    /// <summary>The first-person field of view across (horizontal degrees, 70 to 140, 112 by default: 80° vertical at 16:9). Presentation only.</summary>
    public float FieldOfViewHorizontal { get; set; } = FirstPersonView.DefaultHorizontalField;
    /// <summary>The player's own scale for the interface's screens, 0.75 to 1.5.</summary>
    public float InterfaceScale { get; set; } = 1f;
    /// <summary>
    /// Whether the masquerade switches by itself with the place (on entering a public place,
    /// unbound on entering a free one; the key toggles it in between). On by default; recorded in a tape's head.
    /// </summary>
    public bool MasqueradeAutoSwitch { get; set; } = true;

    /// <summary>
    /// Whether the player gets up a few seconds after a zero-Health stop in play, Health back to its effective maximum —
    /// a sandbox convenience (ENGINE_SANDBOX), not a rule; on by default, <c>--no-recovery</c> turns it off for one run. The
    /// arena never recovers anyone. Recorded in a tape's head.
    /// </summary>
    public bool PlayerRecovers { get; set; } = true;

    /// <summary>How long the player lies stopped before it gets up (s; ENGINE_SANDBOX, 5 by default; 0.5 to 600, else the default).</summary>
    public float PlayerRecoverSeconds { get; set; } = SessionOptions.DefaultRecoverSeconds;

    /// <summary>
    /// Whether the reach highlight is shown: the lips round the
    /// player where a move would start, in the proving ground's colours. Off by default; the action <c>reach</c>
    /// toggles it (and keeps the choice here), <c>--show-reach</c> shows it for one run.
    /// </summary>
    public bool ShowReach { get; set; }

    /// <summary>
    /// Whether the HUD is shown over play (the stand-in HUD, the quick slots and the vitals; the interface design's
    /// real one comes later). On by default; the action <c>hud</c> (no key by default) toggles it and keeps the choice here,
    /// <c>--hide-hud</c> hides it for one run. A <c>--headless</c> picture has no HUD unless <c>--show-hud</c> is given, so
    /// the pictures taken to check levels do not change.
    /// </summary>
    public bool ShowHud { get; set; } = true;

    /// <summary>
    /// Whether the swing's arc is shown (<i>"a kind of faint very translucent
    /// kind of crescent arc visible that shows what the swords path will be"</i>): where the next blow's blade will go, whenever a
    /// weapon or fists can swing, first and third person. On by default; the action <c>swingArc</c> (no key by default) toggles
    /// it and keeps the choice here. A <c>--headless</c> picture draws it only with <c>--swing-arc</c>. Presentation only.
    /// </summary>
    public bool ShowSwingArc { get; set; } = true;

    /// <summary>
    /// The swing's arc's opacity, per cent (<i>"Add some kind of option in the menu to make it lighter or darker so I
    /// can tune it"</i>): 0 (hidden) to 100, 25 by default (ENGINE_SANDBOX, ours: faint over the level's dark stone and still
    /// seen on a lit wall). The Controls page's slider; <c>--swing-arc</c> for one run.
    /// </summary>
    public float SwingArcOpacity { get; set; } = 25f;

    /// <summary>
    /// Third person's blow pitched from the chest to the reticle's point, as to an earlier build (off: from the view's pitch, as
    /// first person's, so the swing's arc moves smoothly with the look). For comparison; <c>--aim-from-reticle</c> for one run.
    /// Recorded in a tape's head.
    /// </summary>
    public bool ThirdPersonAimFromReticle { get; set; }

    /// <summary>
    /// <i>"all of the above as options, like in the M&amp; B series"</i>: how the blow's direction is
    /// chosen — <c>keys</c> (the movement key held at the press, and while the blow is held; the earlier build's default, <i>"in
    /// hindsight, I think I did actually switch to the movement based attack in M&amp;B"</i>), <c>automatic</c> (the side of the foe the
    /// crosshair is on; the earlier build's default) or <c>mouse</c> (the mouse's flick, Bannerlord's). The Controls page's row;
    /// <c>--attack-direction</c> for one run. Recorded in a tape's head (a tape without it replays as automatic).
    /// </summary>
    public string AttackDirection { get; set; } = "keys";

    /// <summary>
    /// The swing's arc the old way, for comparison — the body carried on by its velocity over the blow
    /// (the arc where the blade will be in the world). Off by default: the arc in the body's own frame, drawn on him (it is
    /// always right in front of Christof). <c>--arc-carry-on</c> for one run. Presentation only.
    /// </summary>
    public bool SwingArcCarryOn { get; set; }

    /// <summary>
    /// <i>"we might dim it substantially while the weapon is drawn"</i>: the reticle's opacity,
    /// per cent, while a melee weapon or the fists are out and it has nothing else to say (talk, a power's target): 0 to 100, 20
    /// by default (ENGINE_SANDBOX, ours: <see cref="Presentation.ReticleLook.DefaultMeleePercent"/>; still found at the centre, no
    /// longer pulling the eye from the arc). Full otherwise. The Controls page's slider; <c>--melee-reticle</c> for one run.
    /// Presentation only.
    /// </summary>
    public float MeleeReticleOpacity { get; set; } = ReticleLook.DefaultMeleePercent;

    /// <summary>
    /// The view's zoom at full draw, per cent (0 to 50; 0, off, by default): the field narrows with the
    /// draw's share, by 1 / (1 + zoom × share) — at 10, a tenth narrower at full draw. <c>--bow-zoom</c> for one run. Presentation only
    /// (the arrow goes where the crosshair is whatever the field).
    /// </summary>
    public float BowZoomPercent { get; set; }

    /// <summary>
    /// The player's arrow launched at the angle that carries it to the crosshair's point at the draw reached (its drop allowed
    /// for). <b>Off by default</b> (<i>"Go with the flies like real arrow"</i>): it leaves along the straight line to
    /// that point and drops as a real arrow does; the aids show where it will land. <c>--bow-solve-on</c> for one run; a recording carries it.
    /// </summary>
    public bool BowAimSolve { get; set; }

    /// <summary>The developer's aid marking where the drawn arrow would land if loosed now; the action <c>showImpact</c> (no key by default) toggles it and keeps the choice here. Off by default; presentation only.</summary>
    public bool ShowBowImpact { get; set; }

    /// <summary>The developer's aid drawing the arc the drawn arrow would fly, and each shot's real path for a few seconds beside it; the action <c>showPath</c> (no key by default) toggles it and keeps the choice here. Off by default; presentation only.</summary>
    public bool ShowBowPath { get; set; }

    /// <summary>
    /// <i>"a small running log window that's in one of the corners to show status"</i>: whether
    /// the combat log's window is shown over play — the last lines the session says about the fight (contacts, the guard's verdicts,
    /// harm, dodges, powers, stops). On by default; the action <c>combatLog</c> (no key by default) toggles it and keeps the
    /// choice here; the Controls page's toggle. A <c>--headless</c> picture draws it only with <c>--show-combat-log</c>.
    /// Presentation only.
    /// </summary>
    public bool ShowCombatLog { get; set; } = true;

    /// <summary>The combat log's corner: <c>topRight</c> (the default), <c>topLeft</c>, <c>bottomRight</c> or <c>bottomLeft</c>.</summary>
    public LogCorner CombatLogCorner { get; set; } = LogCorner.TopRight;

    /// <summary>The combat log's rows of text, 3 to 30 (10 by default; ENGINE_SANDBOX, ours): a long line wraps onto two or three.</summary>
    public int CombatLogLines { get; set; } = 10;

    /// <summary>
    /// The fields of the file this build does not know, kept and written back as they were (so a setting of a
    /// later build, or of an agent's not yet merged, survives a save here; earlier they were dropped). Read and set by name
    /// with <see cref="Named"/> and <see cref="SetNamed"/>.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>
    /// A setting by its name in the file (<c>attackDirection</c>), as a word: the property's value when this build
    /// has one by that name (an enumeration in camel case: <c>automatic</c>), else the file's own value kept in
    /// <see cref="Unknown"/>, else null. The Controls page reads the direction work's attack direction so.
    /// </summary>
    public string? Named(string name)
    {
        if (Property(name) is { } p) return p.GetValue(this) is { } v ? Word(v) : null;
        return Unknown is not null && Unknown.TryGetValue(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    }

    /// <summary>Sets a setting by its name in the file: its property when this build has one (a string, or an enumeration by its name), else kept in <see cref="Unknown"/>. Answers whether it took.</summary>
    public bool SetNamed(string name, string value)
    {
        if (Property(name) is { CanWrite: true } p)
        {
            if (p.PropertyType == typeof(string)) { p.SetValue(this, value); return true; }
            if (p.PropertyType.IsEnum && Enum.TryParse(p.PropertyType, value, ignoreCase: true, out var e)) { p.SetValue(this, e); return true; }
            return false;
        }
        if (Property(name) is not null) return false;
        (Unknown ??= new(StringComparer.Ordinal))[name] = JsonSerializer.SerializeToElement(value);
        return true;
    }

    static System.Reflection.PropertyInfo? Property(string name) =>
        string.IsNullOrEmpty(name) || name == nameof(Unknown) ? null
        : typeof(PlatformSettings).GetProperty(char.ToUpperInvariant(name[0]) + name[1..], System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

    static string Word(object v) => v is Enum ? JsonNamingPolicy.CamelCase.ConvertName(v.ToString()!) : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "";

    /// <summary>
    /// Actions to what works them (<see cref="BindingNames"/>): SDL's key names, <c>Mouse Left</c>, <c>Mouse Right</c>,
    /// <c>Mouse Middle</c>, <c>Mouse 4</c>, <c>Mouse 5</c> (or <c>Mouse1</c> to <c>Mouse5</c> numbered as Windows and
    /// Quake number them: 2 the right), <c>Wheel Up</c>, <c>Wheel Down</c>. Changed on the Controls page (Escape, the
    /// pause menu) or with <c>--bind</c>. The action <c>menu</c> opens the pause menu (it was <c>quit</c> earlier;
    /// a file's <c>quit</c> is read as <c>menu</c>).
    /// </summary>
    public Dictionary<string, string[]> Bindings { get; set; } = DefaultBindings();

    public static Dictionary<string, string[]> DefaultBindings() => new(StringComparer.Ordinal)
    {
        ["forward"] = ["W"], ["back"] = ["S"], ["left"] = ["A"], ["right"] = ["D"],
        ["jump"] = ["Space"], ["sprint"] = ["Left Shift"], ["walk"] = ["Left Ctrl"], ["crouch"] = ["C"],
        // No swapShoulder — switchView's V cycles first person, the right shoulder and the left.
        ["use"] = ["E"], ["switchView"] = ["V"], ["freePointer"] = ["Left Alt"],
        // DI: click mode is not in the enhanced game, so switchMode has no key (a file's Tab is kept and does nothing);
        // the freed pointer's click shares attack's Mouse Left (the program sends it to one or the other by the pointer).
        ["switchMode"] = [], ["attack"] = ["Mouse Left"], ["block"] = ["Mouse Right"], ["pointerClick"] = ["Mouse Left"],
        ["screenshot"] = ["F12"], ["menu"] = ["Escape"], ["masquerade"] = ["CapsLock"],
        // Cancel on crouch's key by default — a press with something to cancel cancels, else it crouches.
        ["cancel"] = ["C"],
        ["reach"] = [],   // the reach highlight's toggle: no key until one is given on the Controls page
        ["awareness"] = [],   // the awareness overlay's toggle: no key until one is given on the Controls page
        ["hitVolumes"] = [],  // the hit volumes overlay's toggle: no key until one is given on the Controls page
        ["swingTrail"] = [],  // the swing trail's toggle: no key until one is given on the Controls page
        ["swingArc"] = [],    // the swing's arc's toggle: no key until one is given on the Controls page
        ["combatLog"] = [],   // the combat log's toggle: no key until one is given on the Controls page
        // Each two-state action's toggled half, unbound (HoldAndToggle); the held halves keep their keys.
        ["crouchToggle"] = [], ["walkToggle"] = [], ["freePointerToggle"] = [],
        // The quick slots on the number keys 1 to 8 (SDL's names; the keypad's are left free), and the HUD's
        // toggle, unbound.
        ["slot1"] = ["1"], ["slot2"] = ["2"], ["slot3"] = ["3"], ["slot4"] = ["4"],
        ["slot5"] = ["5"], ["slot6"] = ["6"], ["slot7"] = ["7"], ["slot8"] = ["8"],
        ["hud"] = [],
        // Dodge. The controls work: Mouse 4 or 5 is dodge — Mouse 4 (it was Q, now aim's);
        // an older file gets Mouse 4, else Mouse 5, where nothing of the file has it (GiveDodgeItsKey).
        ["dodge"] = [DodgeKey],
        // Free look, held and toggled, both unbound until given a key.
        ["freeLook"] = [], ["freeLookToggle"] = [],
        // The sword and the bow swapped on F; the bow's aim mode held on Q ("Q is aim").
        ["swapWeapon"] = [SwapWeaponKey], ["aim"] = [AimKey],
        // Aim mode's toggled half ("toggle is empty") and the bow's two developer aids, unbound.
        ["aimToggle"] = [], ["showImpact"] = [], ["showPath"] = [],
        // The camera to watch from, swung round the player while held, on Right Alt (Left Alt frees the pointer).
        [Cameras.OrbitCamera.Action] = [OrbitKey],
        // Stealth's toggle. C where nothing else has it (GiveStealthItsKey: a set with crouch and cancel on X); in these defaults C is crouch's
        // and cancel's, so it waits for a key on the Controls page.
        [StealthAction] = [],
    };

    /// <summary>Stealth's toggle's action and the key a file is given when free there (default key C, unbound in one set).</summary>
    public const string StealthAction = "stealth", StealthKey = "C";

    /// <summary>
    /// Stealth's key for an older file (one with no <c>stealth</c>): <see cref="StealthKey"/> (C) unless another action of the file has it (the
    /// player's own choice wins: stealth then has no key, said in the log, for the Controls page). Answers the key given, or null.
    /// </summary>
    public static string? GiveStealthItsKey(Dictionary<string, string[]> bindings, Action<string>? say = null)
    {
        if (bindings.ContainsKey(StealthAction)) return null;
        if (WillWork(bindings, StealthKey, StealthAction) is { } other)
        {
            bindings[StealthAction] = [];
            say?.Invoke($"settings: {StealthKey} works {other} in your bindings, so Stealth has no key yet; give it one on the Controls page");
            return null;
        }
        bindings[StealthAction] = [StealthKey];
        return StealthKey;
    }

    /// <summary>The orbit camera's key in the defaults (<i>"hold Right Alt and move the mouse"</i>), and the one an older file is given when free there.</summary>
    public const string OrbitKey = "Right Alt";

    /// <summary>
    /// The orbit camera's key for an older file (one with no <c>orbitCamera</c>): <see cref="OrbitKey"/> (Right Alt) unless another action
    /// of the file has it (the player's own choice wins: the camera then has no key, said in the log, for the Controls page). Answers the key given, or null.
    /// </summary>
    public static string? GiveOrbitItsKey(Dictionary<string, string[]> bindings, Action<string>? say = null)
    {
        if (bindings.ContainsKey(Cameras.OrbitCamera.Action)) return null;
        if (WillWork(bindings, OrbitKey, Cameras.OrbitCamera.Action) is { } other)
        {
            bindings[Cameras.OrbitCamera.Action] = [];
            say?.Invoke($"settings: {OrbitKey} works {other} in your bindings, so Orbit Camera has no key yet; give it one on the Controls page");
            return null;
        }
        bindings[Cameras.OrbitCamera.Action] = [OrbitKey];
        return OrbitKey;
    }

    /// <summary>Aim's key in the defaults (<i>"Q is aim"</i>), and the one an older file is given when free there (<see cref="AimTakesQ"/>).</summary>
    public const string AimKey = "Q";

    /// <summary>The swap's key in the defaults, and the one an older file is given when nothing of the file has it.</summary>
    public const string SwapWeaponKey = "F";

    /// <summary>
    /// The swap's key for an older file (one with no <c>swapWeapon</c>): <see cref="SwapWeaponKey"/> (F) unless
    /// another action of the file has it (the player's own choice wins: the swap then has no key, said in the log, for the Controls page).
    /// </summary>
    public static string? GiveSwapWeaponItsKey(Dictionary<string, string[]> bindings, Action<string>? say = null)
    {
        if (bindings.ContainsKey("swapWeapon")) return null;
        foreach (var (action, names) in bindings)
            if (Array.Exists(names, k => string.Equals(k?.Trim(), SwapWeaponKey, StringComparison.OrdinalIgnoreCase)))
            {
                bindings["swapWeapon"] = [];
                say?.Invoke($"settings: {SwapWeaponKey} works {action} in your bindings, so Swap Weapon (the sword and the bow) has no key yet; give it one on the Controls page");
                return null;
            }
        bindings["swapWeapon"] = [SwapWeaponKey];
        return SwapWeaponKey;
    }

    /// <summary>
    /// Dodge's key in the defaults (Q; <c>Mouse 4</c>: Mouse 4 or 5 is
    /// dodge, depending on the game), and <see cref="DodgeKeySecond"/> (<c>Mouse 5</c>) the one an older file is given when
    /// Mouse 4 is not free there (<see cref="GiveDodgeItsKey"/>, <see cref="AimTakesQ"/>).
    /// </summary>
    public const string DodgeKey = "Mouse 4", DodgeKeySecond = "Mouse 5";

    /// <summary>
    /// Dodge's key for an older file (one with no <c>dodge</c>): <see cref="DodgeKey"/> (Mouse 4) unless another action
    /// of the file has it — or will have it by default, an action the file leaves out —, else <see cref="DodgeKeySecond"/> (Mouse 5)
    /// unless taken, else none (the log says so and the Controls page gives it one). A file that names <c>dodge</c> keeps what it
    /// says. Answers the key given, or null. (earlier builds tried C, then Q; Q is aim's, and Dodge may go on
    /// a side button.)
    /// </summary>
    public static string? GiveDodgeItsKey(Dictionary<string, string[]> bindings, Action<string>? say = null)
    {
        if (bindings.ContainsKey("dodge")) return null;
        foreach (var key in new[] { DodgeKey, DodgeKeySecond })
        {
            if (WillWork(bindings, key, "dodge") is not null) continue;
            bindings["dodge"] = [key];
            say?.Invoke($"settings: Dodge is on {key}; change it on the Controls page");
            return key;
        }
        bindings["dodge"] = [];
        say?.Invoke($"settings: {DodgeKey} and {DodgeKeySecond} both work other actions in your bindings, so Dodge has no key yet; give it one on the Controls page");
        return null;
    }

    /// <summary>
    /// Whether two binding names name the same thing — the mouse's and the wheel's by what they name
    /// (<c>Mouse4</c> is <c>Mouse 4</c>, <see cref="BindingNames.Parse"/>), keys by their names, case and spaces round them aside.
    /// </summary>
    public static bool SameInput(string? a, string b)
    {
        if (a is null) return false;
        InputSource? pa = BindingNames.Parse(a, _ => null), pb = BindingNames.Parse(b, _ => null);
        return pa is not null || pb is not null ? pa == pb : string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The action other than <paramref name="except"/> that will work <paramref name="key"/> once the file is
    /// loaded — one of the file's own, or an action the file leaves out whose default has it —, or null when none (the key is free).
    /// </summary>
    static string? WillWork(Dictionary<string, string[]> bindings, string key, string except)
    {
        foreach (var (action, names) in bindings)
            if (action != except && Array.Exists(names, k => SameInput(k, key))) return action;
        foreach (var (action, names) in DefaultBindings())
            if (action != except && !bindings.ContainsKey(action) && Array.Exists(names, k => SameInput(k, key))) return action;
        return null;
    }

    /// <summary>
    /// <c>swapShoulder</c> is gone (<i>"As for swap shoulders, we were going to get rid of
    /// that, and just the "change camera" key would swap between first person, right shoulder, then left shoulder"</i>): a file that
    /// names it loses it, any version (a key it had is said once in the log; the file saved since never names it). Answers the keys it had.
    /// </summary>
    public static string[] DropSwapShoulder(Dictionary<string, string[]> bindings, Action<string>? say = null)
    {
        if (!bindings.Remove(RetiredSwapShoulder, out var keys)) return [];
        keys = [.. keys.Where(k => !string.IsNullOrWhiteSpace(k))];
        if (keys.Length > 0)
            say?.Invoke($"settings: Swap Shoulder is gone, so {string.Join(" and ", keys)} no longer works it; Switch View cycles first person, the right shoulder and the left");
        return keys;
    }

    /// <summary>The retired action's name, which a settings older file may hold (<see cref="DropSwapShoulder"/>).</summary>
    public const string RetiredSwapShoulder = "swapShoulder";

    /// <summary>
    /// Once for an older file (version 4 or less; <see cref="Upgrade"/>), after
    /// <see cref="DropSwapShoulder"/>: <b>Dodge on Q</b> (the earlier build's default) moves to <see cref="DodgeKey"/> (Mouse 4) when nothing
    /// else will have it, else to <see cref="DodgeKeySecond"/> (Mouse 5), else it stays on Q; then <b>aim</b>, when the file gives
    /// it no key (the earlier build's default left it unbound, or the file is older and leaves it out), takes <see cref="AimKey"/> (Q) when
    /// nothing else will have it — a file with Q freed from Swap Shoulder among them —, else it is left with no key (written so,
    /// so the defaults do not put Q on it beside its owner). A key the player gave aim himself, and any other binding, stays. Each
    /// change is said once in the log. Answers what changed (for the tests).
    /// </summary>
    public static IReadOnlyList<string> AimTakesQ(Dictionary<string, string[]> bindings, Action<string>? say = null)
    {
        var changes = new List<string>();
        if (bindings.TryGetValue("dodge", out var dodge) && Array.FindIndex(dodge, k => SameInput(k, AimKey)) is var q && q >= 0)
        {
            if (Array.Find(dodge, k => SameInput(k, DodgeKey) || SameInput(k, DodgeKeySecond)) is { } side)
            {
                // Dodge already has a side button beside Q: Q goes, the side button stays.
                bindings["dodge"] = [.. dodge.Where((_, i) => i != q)];
                changes.Add($"dodge {AimKey} dropped (kept {side.Trim()})");
                say?.Invoke($"settings: Dodge keeps {side.Trim()} and gives up {AimKey} (Q is Aim's now); change it on the Controls page");
            }
            else
                foreach (var key in new[] { DodgeKey, DodgeKeySecond })
                {
                    if (WillWork(bindings, key, "dodge") is not null) continue;
                    var moved = (string[])dodge.Clone();
                    moved[q] = key;
                    bindings["dodge"] = moved;
                    changes.Add($"dodge {AimKey} -> {key}");
                    say?.Invoke($"settings: Dodge moved from {AimKey} to {key} (a side button; Q is Aim's now); change it on the Controls page");
                    break;
                }
        }
        bool aimUnbound = !bindings.TryGetValue("aim", out var aim) || !Array.Exists(aim, k => !string.IsNullOrWhiteSpace(k));
        if (aimUnbound)
        {
            if (WillWork(bindings, AimKey, "aim") is { } owner)
            {
                bindings["aim"] = [];
                changes.Add($"aim none ({AimKey} is {owner}'s)");
                say?.Invoke($"settings: {AimKey} works {owner} in your bindings, so it stays there and Aim (the bow, by default {AimKey}) has no key yet; give it one on the Controls page");
            }
            else
            {
                bindings["aim"] = [AimKey];
                changes.Add($"aim -> {AimKey}");
                say?.Invoke($"settings: Aim (the bow raised, held) is on {AimKey}; change it on the Controls page");
            }
        }
        return changes;
    }

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static PlatformSettings Load(string path, Action<string>? say = null)
    {
        try
        {
            if (!File.Exists(path)) return new PlatformSettings { SettingsVersion = CurrentVersion };
            var text = File.ReadAllText(path);
            var s = JsonSerializer.Deserialize<PlatformSettings>(text, Json) ?? new PlatformSettings();
            // A older file (no wallMoveRate in it) that says the earlier build's default wall climb rate takes the earlier build's, once (a file saved since says both).
            if (!text.Contains("\"wallMoveRate\"", StringComparison.Ordinal) && s.WallClimbRate == 1f)
            {
                s.WallClimbRate = 1.3f;
                say?.Invoke("settings: wallClimbRate is now 1.3 (the default with the flow; the file kept 1, the earlier default)");
            }
            DropSwapShoulder(s.Bindings, say);   // before Upgrade, so Q is free for aim there
            s.Upgrade(say);
            // Escape opened nothing and quit earlier: a file's quit binding is the pause menu's now.
            if (s.Bindings.Remove("quit", out var quit) && !s.Bindings.ContainsKey("menu")) s.Bindings["menu"] = quit;
            // A file from before cancel gets its own crouch's keys for it (X in one scheme), not the default's C.
            if (!s.Bindings.ContainsKey("cancel"))
                s.Bindings["cancel"] = s.Bindings.TryGetValue("crouch", out var crouch) ? [.. crouch] : DefaultBindings()["cancel"];
            // Dodge for a file from before it (Mouse 4 where free, else Mouse 5), before the defaults fill what is left.
            GiveDodgeItsKey(s.Bindings, say);
            GiveSwapWeaponItsKey(s.Bindings, say);
            GiveOrbitItsKey(s.Bindings, say);
            GiveStealthItsKey(s.Bindings, say);
            // A binding the file leaves out keeps its default, so a new action added later is bound.
            foreach (var (action, keys) in DefaultBindings())
                if (!s.Bindings.ContainsKey(action)) s.Bindings[action] = keys;
            return s;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            say?.Invoke($"settings: {path} could not be read ({e.Message}); the defaults are used");
            return new PlatformSettings { SettingsVersion = CurrentVersion };
        }
    }

    /// <summary>
    /// A file of an earlier layout brought up to this one. In version 0 the window was kept as
    /// <c>borderless</c> 1920x1080 only because those were the defaults then (nothing but editing the file chose
    /// them), so such a file takes the newer default, the centred 2560x1440 window. In version 1
    /// the freed pointer's key was held or toggled by the setting <c>pointerKey</c>: a file that said
    /// <c>toggle</c> has its <c>freePointer</c> keys moved to <c>freePointerToggle</c> (<see cref="HoldAndToggle.MovePointerKeysToToggle"/>).
    /// <b>Once</b>: the move is made only for a file of version 1 or less, which then becomes version 2, and the old
    /// setting is dropped (<see cref="PointerKey"/> is never written), so a file saved since says neither again.
    /// Before an earlier build (version 2) there were no quick slots: each slot gets its number key unless the player has already
    /// put that key on another action, which keeps it — the slot then has no key, said in the log, and the Controls page
    /// shows it empty for the player to bind (<see cref="GiveQuickSlotsTheirKeys"/>). Before an earlier build (version 3) third person's
    /// facing was <c>move</c> by default: a file that says so is brought to <c>view</c>, once (<see cref="FaceTheView"/>).
    /// Before an earlier build (version 4) Q was Dodge's and aim had no key: Dodge on Q moves to a side button and aim takes Q where free, once
    /// (<see cref="AimTakesQ"/>; <c>swapShoulder</c> already dropped by <see cref="Load"/>).
    /// </summary>
    void Upgrade(Action<string>? say)
    {
        if (SettingsVersion < 3) GiveQuickSlotsTheirKeys(Bindings, say);
        if (SettingsVersion < 4) FaceTheView(say);
        if (SettingsVersion < 5) AimTakesQ(Bindings, say);
        if (SettingsVersion >= CurrentVersion) { PointerKey = null; return; }
        if (SettingsVersion < 1 && (Window, WindowWidth, WindowHeight) != (WindowMode.Centered, 2560, 1440))
        {
            say?.Invoke($"settings: the window is now centred, 2560x1440 with no title bar (the default; the file kept {Window.ToString().ToLowerInvariant()} {WindowWidth}x{WindowHeight}, the old default)");
            (Window, WindowWidth, WindowHeight) = (WindowMode.Centered, 2560, 1440);
        }
        if (SettingsVersion < 2 && string.Equals(PointerKey?.Trim(), "toggle", StringComparison.OrdinalIgnoreCase))
        {
            var moved = HoldAndToggle.MovePointerKeysToToggle(Bindings);
            say?.Invoke(moved.Count > 0
                ? $"settings: the free-pointer key toggled, so {string.Join(" and ", moved)} now works freePointerToggle and freePointer has no key (a held and a toggled action)"
                : "settings: the free-pointer key toggled, but freePointer had no key to move to freePointerToggle");
        }
        PointerKey = null;
        SettingsVersion = CurrentVersion;
    }

    /// <summary>
    /// Once for a file from before it (version 3 or less): third person's facing <c>move</c> becomes <c>view</c> (in
    /// third person the player always turns to face the direction the camera is
    /// going), the log saying so; the Controls page still offers Move, and a
    /// file saved since keeps what it says. Answers whether it changed anything.
    /// </summary>
    public bool FaceTheView(Action<string>? say = null)
    {
        if (ThirdPersonFacing != ThirdPersonFacing.Move) return false;
        ThirdPersonFacing = ThirdPersonFacing.View;
        say?.Invoke("settings: third person now faces the view (the body turns with the camera, the legs toward the way it moves; the file said move, which the Controls page still offers)");
        return true;
    }

    /// <summary>
    /// The quick slots' keys for an older file (version 2 or less): <c>slot</c><i>n</i> gets the key <i>n</i>
    /// unless another action of the file already has it (the player's own choice wins: that slot is left with no key and
    /// the log says so); a slot the file already names is left as it is. Answers the slots left with no key.
    /// </summary>
    public static IReadOnlyList<string> GiveQuickSlotsTheirKeys(Dictionary<string, string[]> bindings, Action<string>? say = null)
    {
        var unbound = new List<string>();
        for (int n = 1; n <= QuickSlotButtons.Actions.Count; n++)
        {
            var action = QuickSlotButtons.Actions[n - 1];
            if (bindings.ContainsKey(action)) continue;
            var key = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string? owner = null;
            foreach (var (other, names) in bindings)
                if (Array.Exists(names, k => string.Equals(k?.Trim(), key, StringComparison.OrdinalIgnoreCase))) { owner = other; break; }
            if (owner is null) { bindings[action] = [key]; continue; }
            bindings[action] = [];
            unbound.Add(action);
            say?.Invoke($"settings: the key {key} works {owner} in your bindings, so it stays there and quick slot {n} ({action}) has no key yet; give it one on the Controls page");
        }
        return unbound;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
