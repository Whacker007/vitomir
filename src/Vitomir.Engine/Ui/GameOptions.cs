// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The settings the Options screen edits: 1.1's two option words, its two volumes, its brightness and
// the display and sound choices. Read from the main sequence (which reads the `[gameOptions]`
// section of masquerade.ini and writes it back) and the mixer (which writes `[mixer]`).
using System.Globalization;
using Vitomir.Engine.Diagnostics;

namespace Vitomir.Engine.Ui;

/// <summary>
/// What the Options screen edits, and where it lives between runs.
///
/// **1.1 keeps these in `masquerade.ini`** beside the game and writes them back when it shuts down
/// (the mixer's too). **Vitomir never writes to the game's folder**,
/// so its copy lives in an <c>[options]</c> section of the player's own <c>vitomir.ini</c>, under
/// the same key names, and it is written the moment the player presses OK rather than at exit (ours).
/// Reading goes: the install's `masquerade.ini` first — so a 1.1 player's own choices carry over —
/// then the <c>vitomir.ini</c> beside the executable, then the player's own, each overriding the
/// last. Two differences from reading `masquerade.ini` alone, both ours:
///
/// - **Brightness does not come from `masquerade.ini`.** 1.1's own is 1.4 (`[render] gamma`), and
///   Vitomir's default is 1.0, the frame as drawn, chosen when the presenter was written
///   (`Render.Presenter`). A gamma chosen on the Display screen is kept in <c>[options]</c>.
/// - Values are written plainly (<c>gamma = 1.2</c>), not in the hex-and-checksum form Windows'
///   `WritePrivateProfileStruct` gives 1.1's floats (<see cref="IniFile.ProfileFloat"/> still reads
///   those in `masquerade.ini`).
/// </summary>
public sealed class GameOptions
{
    // ---- the options word (six of its bits are read from the ini) ----------

    /// <summary>`ForceSubtitles`: subtitles on every spoken line. The Gameplay screen's second box.</summary>
    public const uint ForceSubtitles = 0x1;
    /// <summary>`NoBlood`: no blood and no gore (<see cref="Rpg.Gore.NoBlood"/>). The Gameplay screen's first box.</summary>
    public const uint NoBlood = 0x2;
    /// <summary>`RepeatAttack`: "Auto Repeat Attack" — holding the button keeps swinging.</summary>
    public const uint RepeatAttack = 0x4;
    /// <summary>`AutoSlew`: "Auto Camera Slewing".</summary>
    public const uint AutoSlew = 0x8;
    /// <summary>`MousePitch`: "Enable Mouse Pitch" — the top and bottom edges tilt the camera.</summary>
    public const uint MousePitch = 0x10;
    /// <summary>`BackpackAutoLoad`: "Autoload Quick Slot From Backpack".</summary>
    public const uint BackpackAutoLoad = 0x20;
    /// <summary>
    /// Not in the ini: `-console` on the command line  — the developer console's
    /// key works only with it (tested before opening `console.nui`).
    /// </summary>
    public const uint ConsoleAllowed = 0x40;
    /// <summary>Not in the ini: `-devmode` on the command line, the developer keys.</summary>
    public const uint DevMode = 0x80;
    /// <summary>
    /// Not in the ini: set with <see cref="NoBlood"/> by a <c>NoBlood</c> **registry** value
    /// — a locked build. The Gameplay screen **hides** its blood box when it is set
    /// (the screen hides the control), so the player cannot turn it off.
    /// </summary>
    public const uint BloodLocked = 0x100;

    // ---- the party AI word, and what it does to the AI flags ----------

    /// <summary>`UseQuickDisc`: "Use Quick Disciplines Only".</summary>
    public const uint UseQuickDisc = 0x1;
    /// <summary>`UseQuickItems`: "Use Quick Items Only".</summary>
    public const uint UseQuickItems = 0x2;
    /// <summary>`CastHealing`: "Auto-use Healing".</summary>
    public const uint CastHealing = 0x4;
    /// <summary>`CastBuff`: "Auto-use Buff".</summary>
    public const uint CastBuff = 0x8;
    /// <summary>`CastDamage`: "Auto-use Damage".</summary>
    public const uint CastDamage = 0x10;
    /// <summary>`CastSpecial`: "Auto-use Special".</summary>
    public const uint CastSpecial = 0x20;

    /// <summary>What is in force this run. Installed once by the program; a test builds its own.</summary>
    public static GameOptions Current { get; set; } = new();

    /// <summary>The options word. Zero until the ini says otherwise — the engine's own start.</summary>
    public uint Flags { get; set; }
    /// <summary>The party AI word.</summary>
    public uint Ai { get; set; }

    /// <summary>
    /// **Always run** (ours): driving runs without the run key, and the run key held
    /// walks instead (<see cref="AlwaysRunOption"/>). The Gameplay screen's third box; off by
    /// default; <c>alwaysRun</c> in <c>[options]</c>. 1.1 has no such setting — it has no driving.
    /// </summary>
    public bool AlwaysRun { get; set; }

    /// <summary>
    /// **Confirm quick load** (ours): the quick load asks OK or Cancel in
    /// the message box before it loads (<see cref="QuickLoadConfirm"/>). The Gameplay screen's fourth box;
    /// off by default, so the quick load asks nothing; <c>confirmQuickLoad</c> in <c>[options]</c>.
    /// </summary>
    public bool ConfirmQuickLoad { get; set; }

    /// <summary>Brightness, held to 0.5..2 wherever it is set.</summary>
    public float Gamma { get; set; } = DefaultGamma;
    /// <summary>`[mixer] effectsVolume`, 0..1; 0.75 when the file does not say.</summary>
    public float EffectsVolume { get; set; } = 0.75f;
    /// <summary>`[mixer] musicVolume`, 0..1; 0.75 when the file does not say.</summary>
    public float MusicVolume { get; set; } = 0.75f;
    /// <summary>`[mixer] speakerType`: 0 two speakers, 1 four, 2 headphones, 3 surround (the radio order).</summary>
    public int SpeakerType { get; set; }
    /// <summary>`[render] curDetailLevel`: 0 very low … 3 high, 4 custom.</summary>
    public int DetailLevel { get; set; } = 3;
    /// <summary>
    /// The Advanced Display screen's controls, by control name — its checkboxes as 0/1 and its bars
    /// as their stops. Kept as the screen left them, which is not the engine's detail table.
    /// </summary>
    public Dictionary<string, int> Advanced { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Vitomir's brightness default: the frame as drawn (<c>Render.Presenter.DefaultGamma</c>).</summary>
    public const float DefaultGamma = 1.0f;
    /// <summary>The bounds every gamma is held to (0.5 and 2.0).</summary>
    public const float MinGamma = 0.5f, MaxGamma = 2.0f;

    public bool Has(uint bit) => (Flags & bit) != 0;
    public bool HasAi(uint bit) => (Ai & bit) != 0;

    /// <summary>Sets or clears one bit of the options word.</summary>
    public void Set(uint bit, bool on) => Flags = on ? Flags | bit : Flags & ~bit;
    /// <summary>Sets or clears one bit of the party AI word.</summary>
    public void SetAi(uint bit, bool on) => Ai = on ? Ai | bit : Ai & ~bit;

    /// <summary>
    /// The renderer's custom detail preset as `masquerade.ini`'s `[renderCustom]` has it
    /// (<see cref="DetailPresets.Custom"/>), which the Advanced screen shows for Custom until the
    /// player saves one of their own; null when the file has none.
    /// </summary>
    public DetailPreset? RenderCustom { get; set; }

    /// <summary>A copy the screens can edit and throw away on Cancel.</summary>
    public GameOptions Clone()
    {
        var copy = (GameOptions)MemberwiseClone();
        copy.Advanced = new Dictionary<string, int>(Advanced, StringComparer.OrdinalIgnoreCase);
        return copy;
    }

    /// <summary>
    /// The party AI flags with the AI word folded in: the two
    /// quick-slot filters are set when the boxes are, and the four "no party casting" bits are set
    /// when the boxes are **clear** — the boxes say what the party may do, the flags what it may not.
    /// Every other bit is kept.
    /// </summary>
    public uint FoldInto(uint aiFlags)
    {
        uint f = aiFlags;
        f = HasAi(UseQuickDisc) ? f | Vitomir.Engine.Ai.AiMainFlag.QuickDisciplinesOnly : f & ~Vitomir.Engine.Ai.AiMainFlag.QuickDisciplinesOnly;
        f = HasAi(UseQuickItems) ? f | Vitomir.Engine.Ai.AiMainFlag.QuickItemsOnly : f & ~Vitomir.Engine.Ai.AiMainFlag.QuickItemsOnly;
        f = HasAi(CastHealing) ? f & ~Vitomir.Engine.Ai.AiMainFlag.NoPartyCastHeal : f | Vitomir.Engine.Ai.AiMainFlag.NoPartyCastHeal;
        f = HasAi(CastBuff) ? f & ~Vitomir.Engine.Ai.AiMainFlag.NoPartyCastBuff : f | Vitomir.Engine.Ai.AiMainFlag.NoPartyCastBuff;
        f = HasAi(CastDamage) ? f & ~Vitomir.Engine.Ai.AiMainFlag.NoPartyCastDamage : f | Vitomir.Engine.Ai.AiMainFlag.NoPartyCastDamage;
        f = HasAi(CastSpecial) ? f & ~Vitomir.Engine.Ai.AiMainFlag.NoPartyCastSpecial : f | Vitomir.Engine.Ai.AiMainFlag.NoPartyCastSpecial;
        return f;
    }

    /// <summary>The <c>vitomir.ini</c> section Vitomir keeps these in.</summary>
    public const string Section = "options";

    /// <summary>The twelve `[gameOptions]` keys in the reader's order, with their word and bit.</summary>
    public static readonly (string Key, bool AiWord, uint Bit)[] Keys =
    [
        ("ForceSubtitles", false, ForceSubtitles), ("NoBlood", false, NoBlood), ("RepeatAttack", false, RepeatAttack),
        ("AutoSlew", false, AutoSlew), ("MousePitch", false, MousePitch), ("BackpackAutoLoad", false, BackpackAutoLoad),
        ("UseQuickDisc", true, UseQuickDisc), ("UseQuickItems", true, UseQuickItems), ("CastHealing", true, CastHealing),
        ("CastBuff", true, CastBuff), ("CastDamage", true, CastDamage), ("CastSpecial", true, CastSpecial),
    ];

    /// <summary>
    /// Reads the chain: the game's `masquerade.ini` (its `[gameOptions]`, `[mixer]` and `[render]`
    /// sections, the way the game reads them), then each <c>vitomir.ini</c>'s
    /// <c>[options]</c>. **No side effect**; <see cref="Configure"/> installs.
    /// </summary>
    public static GameOptions Parse(string? gameDir, string? installedDir = null, string? userPath = null)
    {
        var options = new GameOptions();
        if (gameDir is not null)
        {
            var ini = IniFile.Read(Vfs.GameData.OptionsIni(gameDir));   // an install's own, or the game folder's settings\
            foreach (var (key, ai, bit) in Keys)
                if (ini["gameOptions." + key] is { } v)                    // GetPrivateProfileIntA with the bit as its default
                {
                    bool had = ai ? options.HasAi(bit) : options.Has(bit);
                    bool on = Diagnostics.IniFile.ProfileInt(v, had ? 1 : 0) != 0;
                    if (ai) options.SetAi(bit, on); else options.Set(bit, on);
                }
            options.EffectsVolume = Math.Clamp(IniFile.ProfileFloat(ini["mixer.effectsVolume"], 0.75f), 0f, 1f);
            options.MusicVolume = Math.Clamp(IniFile.ProfileFloat(ini["mixer.musicVolume"], 0.75f), 0f, 1f);
            if (ini["mixer.speakerType"] is { } sp && int.TryParse(sp, out int speaker)) options.SpeakerType = Math.Clamp(speaker, 0, 3);
            if (ini["render.curDetailLevel"] is { } dl && int.TryParse(dl, out int detail)) options.DetailLevel = Math.Clamp(detail, 0, 4);
            options.RenderCustom = DetailPresets.Custom(ini);
        }
        var installed = Path.Combine(installedDir ?? Diag.InstalledFolder, Diag.FileName);
        options.Apply(IniFile.Read(installed));
        var user = userPath ?? Diag.UserSettingsPath;
        if (!string.Equals(user, installed, StringComparison.OrdinalIgnoreCase)) options.Apply(IniFile.Read(user));
        return options;
    }

    /// <summary>Reads the chain and installs it as <see cref="Current"/>. **Only the program calls this.**</summary>
    public static GameOptions Configure(string? gameDir, string? installedDir = null) =>
        Current = Parse(gameDir, installedDir);

    /// <summary>One <c>[options]</c> section over what is already read.</summary>
    public void Apply(IniFile ini)
    {
        foreach (var (key, value) in ini.Section(Section))
        {
            var k = Keys.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (k.Key is not null)
            {
                bool on = IniFile.IsTrue(value);
                if (k.AiWord) SetAi(k.Bit, on); else Set(k.Bit, on);
                continue;
            }
            float f = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : float.NaN;
            switch (key.ToLowerInvariant())
            {
                case "gamma" when !float.IsNaN(f): Gamma = Math.Clamp(f, MinGamma, MaxGamma); break;
                case "effectsvolume" when !float.IsNaN(f): EffectsVolume = Math.Clamp(f, 0f, 1f); break;
                case "musicvolume" when !float.IsNaN(f): MusicVolume = Math.Clamp(f, 0f, 1f); break;
                case "speakertype" when !float.IsNaN(f): SpeakerType = Math.Clamp((int)f, 0, 3); break;
                case "curdetaillevel" when !float.IsNaN(f): DetailLevel = Math.Clamp((int)f, 0, 4); break;
                case "alwaysrun": AlwaysRun = IniFile.IsTrue(value); break;
                case "confirmquickload": ConfirmQuickLoad = IniFile.IsTrue(value); break;
                default:
                    if (key.StartsWith("adv.", StringComparison.OrdinalIgnoreCase) && !float.IsNaN(f)) Advanced[key[4..]] = (int)f;
                    break;
            }
        }
    }

    /// <summary>What goes into <c>[options]</c>: every key, so the file says what is in force.</summary>
    public IEnumerable<KeyValuePair<string, string>> Values()
    {
        foreach (var (key, ai, bit) in Keys)
            yield return new(key, (ai ? HasAi(bit) : Has(bit)) ? "1" : "0");
        yield return new("gamma", Gamma.ToString("0.0##", CultureInfo.InvariantCulture));
        yield return new("effectsVolume", EffectsVolume.ToString("0.0##", CultureInfo.InvariantCulture));
        yield return new("musicVolume", MusicVolume.ToString("0.0##", CultureInfo.InvariantCulture));
        yield return new("speakerType", SpeakerType.ToString(CultureInfo.InvariantCulture));
        yield return new("curDetailLevel", DetailLevel.ToString(CultureInfo.InvariantCulture));
        yield return new("alwaysRun", AlwaysRun ? "1" : "0");
        yield return new("confirmQuickLoad", ConfirmQuickLoad ? "1" : "0");
        foreach (var (name, value) in Advanced.OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase))
            yield return new("adv." + name, value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Writes <see cref="Values"/> into the player's own <c>vitomir.ini</c> (or <paramref name="path"/>).</summary>
    public void Save(string? path = null) => SettingsFile.Set(path ?? Diag.UserSettingsPath, Section, Values());
}
