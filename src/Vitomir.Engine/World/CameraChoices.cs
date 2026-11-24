// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using Vitomir.Engine.Diagnostics;

namespace Vitomir.Engine.World;

/// <summary>
/// **How the game camera looks, beyond the base game's own limits** (ours; presentation only, so neither a Fix nor
/// anything that makes a game modded).
///
/// <list type="bullet">
/// <item><b>How far up it turns</b> (<see cref="Up"/>): the base game holds the game camera between 80° and 20° below
/// level in every way it is turned (the keys, the screen's edges, the swivel and its lock). Vitomir lets it come up to
/// <see cref="Up"/>, by default level with the horizon. How far back the eye sits is still worked out over the base
/// game's range: above 20° below level it sits at the near distance, as it does at 20°.</item>
/// <item><b>Smoothing on steps</b> (<see cref="Steps"/>, on by default): a walker on the base game's stairs rises a
/// whole riser in the one tick it crosses the riser's line, and the camera, which looks at the walker's head, jumps
/// with it. With smoothing on, the camera's own point of interest takes the step out and pays it back over the ground
/// covered and a short while (<see cref="Levels.Cells.CellStepEase.Next"/>'s rule); the walker is drawn and stands
/// exactly where the base game puts it.</item>
/// <item><b>How far out it zooms</b> (<see cref="Out"/>): the base game's zoom only brings the camera in (a scale of 0.1 to 1
/// on its distance, 1 the furthest). Vitomir lets the scale go out to <see cref="Out"/> times the base game's distance, by
/// default three. The camera still starts at the base game's distance: going further out is the player's own doing.</item>
/// <item><b>Seeing through what is in the way</b> (<see cref="SeeThrough"/>, <see cref="SeeThroughMode"/>; always on for players,
/// with no setting to turn it off, so a cutscene never shows a wall's inside): the
/// camera still pulls in in front of a wall as the base game's does, and whatever is still between the eye and whom the camera
/// frames is drawn clear in a soft round window about each of them, the pointer looking through it where it is clear
/// (<see cref="World.SeeThrough"/>). The developer's x-ray (<c>--camera-xray</c>, which needs <c>--developer</c>) instead
/// leaves the camera where it is put, behind walls and outside the level, and draws everything in the way nearly clear
/// with a faint outline of its edges.</item>
/// </list>
///
/// All of them change where the eye is or what a click meets, so where a click lands on the world: a recording carries them
/// (its <c>camera</c> line, <see cref="TapeFields"/>), and one made before the line or a word of it existed replays with the
/// base game's camera for what it lacks (<see cref="FromTape"/>).
/// </summary>
public static class CameraChoices
{
    /// <summary>The settings file's section and keys: <c>[camera] up = &lt;degrees&gt;</c>, <c>[camera] steps = on | off</c>.</summary>
    public const string Section = "camera", UpKey = "up", StepsKey = "steps", OutKey = "out", SeeThroughKey = "seethrough";

    /// <summary>The base game's own limit: 20° below level.</summary>
    public const float BaseUp = -20f;

    /// <summary>The highest the setting may ask for: level with the horizon.</summary>
    public const float MostUp = 0f;

    /// <summary>The limit when nothing says otherwise: level with the horizon.</summary>
    public const float DefaultUp = MostUp;

    /// <summary>The smoothing when nothing says otherwise: on.</summary>
    public const bool DefaultSteps = true;

    /// <summary>The base game's furthest zoom: its own distance, once.</summary>
    public const float BaseOut = 1f;

    /// <summary>The furthest the setting may ask for: three times the base game's distance.</summary>
    public const float MostOut = 3f;

    /// <summary>How far out the zoom goes when nothing says otherwise: three times the base game's distance.</summary>
    public const float DefaultOut = MostOut;

    /// <summary>Seeing through what is in the way: the soft window, always (the camera still pulls in). Off is only a recording's from before it.</summary>
    public const SeeThroughMode DefaultSeeThrough = SeeThroughMode.On;

    /// <summary>The developer's switch for the x-ray (<see cref="SeeThroughMode.XRay"/>): not a player's switch, so it needs <c>--developer</c>.</summary>
    public const string XRayFlag = "--camera-xray";

    /// <summary>
    /// The developer's switch for no seeing through at all (<see cref="SeeThroughMode.Off"/>, the base game's), to compare a shot
    /// with what 1.1 shows: not a player's switch, so it needs <c>--developer</c>.
    /// </summary>
    public const string PlainFlag = "--camera-no-seethrough";

    /// <summary>How far out the zoom goes, as a multiple of the base game's distance, between <see cref="BaseOut"/> and <see cref="MostOut"/>.</summary>
    public static float Out { get; set; } = DefaultOut;

    /// <summary>How what is between the eye and whom the camera frames is seen through (<see cref="World.SeeThrough"/>).</summary>
    public static SeeThroughMode SeeThrough { get; set; } = DefaultSeeThrough;

    /// <summary>How far up the game camera turns, in degrees from level (negative is below), between <see cref="BaseUp"/> and <see cref="MostUp"/>.</summary>
    public static float Up { get; set; } = DefaultUp;

    /// <summary>Whether the game camera is smoothed on steps.</summary>
    public static bool Steps { get; set; } = DefaultSteps;

    /// <summary>All back to the base game's camera: what a recording from before the <c>camera</c> line was made with.</summary>
    public static void BaseGame()
    {
        Up = BaseUp;
        Steps = false;
        Out = BaseOut;
        SeeThrough = SeeThroughMode.Off;
    }

    /// <summary>A zoom limit held to the range the setting allows.</summary>
    public static float HeldOut(float times) => Math.Clamp(times, BaseOut, MostOut);

    /// <summary>A multiple of the base game's distance, or null if the word is not a number.</summary>
    public static float? ParseOut(string? word) =>
        float.TryParse(word?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && float.IsFinite(d) ? HeldOut(d) : null;

    /// <summary>A limit held to the range the setting allows.</summary>
    public static float Held(float degrees) => Math.Clamp(degrees, BaseUp, MostUp);

    /// <summary>A degrees word, or null if it is not a number.</summary>
    public static float? ParseUp(string? word) =>
        float.TryParse(word?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && float.IsFinite(d) ? Held(d) : null;

    /// <summary>An on/off word, or null if it is neither (any of the on/off settings).</summary>
    public static bool? ParseSteps(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "on" or "1" or "true" or "yes" => true,
        "off" or "0" or "false" or "no" => false,
        _ => null,
    };

    /// <summary>
    /// A recording's word for the see-through: <c>off</c>, <c>window</c> (<see cref="SeeThroughMode.On"/>) or <c>xray</c>. A
    /// recording that says <c>on</c> was made when on meant what the x-ray does now, and reads as the x-ray.
    /// </summary>
    public static SeeThroughMode? ParseTapeSeeThrough(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "off" => SeeThroughMode.Off,
        "window" => SeeThroughMode.On,
        "xray" or "on" => SeeThroughMode.XRay,
        _ => null,
    };

    /// <summary>A recording's word for a see-through (<see cref="ParseTapeSeeThrough"/>).</summary>
    public static string TapeWord(SeeThroughMode mode) => mode switch
    {
        SeeThroughMode.On => "window",
        SeeThroughMode.XRay => "xray",
        _ => "off",
    };

    /// <summary>
    /// Reads what the player asked for and puts it in force: <c>--camera-up</c>, <c>--camera-steps</c> and <c>--camera-out</c> on
    /// the command line, else <c>[camera]</c> of the player's settings file, else the defaults. The see-through is the window
    /// unless the developer's <see cref="XRayFlag"/> (<paramref name="xray"/>) asks for the x-ray or their
    /// <see cref="PlainFlag"/> (<paramref name="plain"/>) for none; a settings file's old
    /// <c>seethrough</c> key is complained about and passed over.
    /// **Only the program calls this.** A word that does not read is complained about and passed over.
    /// </summary>
    public static void Configure(string? upFlag, string? stepsFlag, Action<string>? complain = null, string? settingsPath = null,
                                 string? outFlag = null, bool xray = false, bool plain = false)
    {
        float up = DefaultUp, far = DefaultOut;
        bool steps = DefaultSteps;
        var see = DefaultSeeThrough;
        string path = settingsPath ?? Diag.UserSettingsPath;
        if (File.Exists(path))
        {
            var ini = IniFile.Read(path);
            if (ini[Section + "." + UpKey] is { Length: > 0 } u)
            {
                if (ParseUp(u) is { } d) up = d;
                else complain?.Invoke($"[{Section}] {UpKey} = {u} is not a number of degrees; left at {up:0.#}");
            }
            if (ini[Section + "." + StepsKey] is { Length: > 0 } s)
            {
                if (ParseSteps(s) is { } b) steps = b;
                else complain?.Invoke($"[{Section}] {StepsKey} = {s} is neither on nor off; left {(steps ? "on" : "off")}");
            }
            if (ini[Section + "." + OutKey] is { Length: > 0 } o)
            {
                if (ParseOut(o) is { } d) far = d;
                else complain?.Invoke($"[{Section}] {OutKey} = {o} is not a number (1 to 3 times the base game's distance); left at {far:0.##}");
            }
            if (ini[Section + "." + SeeThroughKey] is { Length: > 0 } t)
                complain?.Invoke($"[{Section}] {SeeThroughKey} = {t} is no longer a setting: seeing through what is in the way is always on");
        }
        if (upFlag is not null)
        {
            if (ParseUp(upFlag) is { } d) up = d;
            else complain?.Invoke($"--camera-up {upFlag} is not a number of degrees; left at {up:0.#}");
        }
        if (stepsFlag is not null)
        {
            if (ParseSteps(stepsFlag) is { } b) steps = b;
            else complain?.Invoke($"--camera-steps {stepsFlag} is neither on nor off; left {(steps ? "on" : "off")}");
        }
        if (outFlag is not null)
        {
            if (ParseOut(outFlag) is { } d) far = d;
            else complain?.Invoke($"--camera-out {outFlag} is not a number (1 to 3 times the base game's distance); left at {far:0.##}");
        }
        if (plain) see = SeeThroughMode.Off;
        if (xray) see = SeeThroughMode.XRay;
        Up = up;
        Steps = steps;
        Out = far;
        SeeThrough = see;
    }

    /// <summary>
    /// A recording's words for its <c>camera</c> line: <c>up=&lt;degrees&gt;</c>, <c>steps=on|off</c>, <c>out=&lt;times&gt;</c>
    /// and <c>seethrough=off|window|xray</c>.
    /// </summary>
    public static string[] TapeFields() =>
        [$"{UpKey}={Up.ToString("0.###", CultureInfo.InvariantCulture)}", $"{StepsKey}={(Steps ? "on" : "off")}",
         $"{OutKey}={Out.ToString("0.###", CultureInfo.InvariantCulture)}", $"{SeeThroughKey}={TapeWord(SeeThrough)}"];

    /// <summary>
    /// The camera a recording was made with, from its <c>camera</c> line's words, put in force: a word it lacks is the
    /// base game's (a recording from before the line was written was made with the base game's camera).
    /// </summary>
    public static void FromTape(IEnumerable<string>? fields)
    {
        BaseGame();
        if (fields is null) return;
        foreach (var field in fields)
        {
            int eq = field.IndexOf('=');
            if (eq < 0) continue;
            string name = field[..eq], value = field[(eq + 1)..];
            if (name == UpKey && ParseUp(value) is { } d) Up = d;
            if (name == StepsKey && ParseSteps(value) is { } b) Steps = b;
            if (name == OutKey && ParseOut(value) is { } o) Out = o;
            if (name == SeeThroughKey && ParseTapeSeeThrough(value) is { } t) SeeThrough = t;
        }
    }
}

/// <summary>How what stands between the camera and whom it frames is seen through (ours, <see cref="CameraChoices.SeeThrough"/>).</summary>
public enum SeeThroughMode
{
    /// <summary>The base game's: the camera pulls in in front of a wall, and nothing is faded.</summary>
    Off,
    /// <summary>
    /// The players' (the default): the camera still pulls in, and what is still in the way is drawn clear in a soft round
    /// window about each subject, the pointer looking through it where it is clear (<see cref="World.SeeThrough"/>).
    /// </summary>
    On,
    /// <summary>
    /// The developer's: the camera is not pulled in, the whole level is drawn wherever the eye is, and everything in the way is
    /// drawn nearly clear, whole, with a faint outline of its edges.
    /// </summary>
    XRay,
}
