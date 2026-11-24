// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.People;

namespace Vitomir.Platform.Host;

/// <summary>
/// The player's body chosen and built (<i>the player's body is the models repository's man on its male
/// base</i>, not 1.1's Christof; the models repository read from disk while developing, a package's copy of what is
/// used): the models repository's folder found (<see cref="FindFolder"/>), opened by the contract's <see cref="PeopleLibrary"/>,
/// the person built and made a kind by the contract's <see cref="PeopleKinds"/>, its textures given to the session's. When
/// the folder, the files or the loader are missing (while the people's loader and animation are the contract's stubs, they throw
/// <see cref="NotImplementedException"/>) the player is 1.1's Christof, as before, and one line says why.
/// </summary>
public static class PeopleBodies
{
    /// <summary>The words <c>--body</c> and the setting <c>playerBody</c> take.</summary>
    public const string Models = "models", Woman = "woman", OneOne = "oneone";

    /// <summary>The default repository (the loader's <see cref="PeopleLibrary.DefaultFolder"/>).</summary>
    public const string DefaultRepository = PeopleLibrary.DefaultFolder;

    /// <summary>The environment variable naming the models repository's folder (the tests' too; the loader's <see cref="PeopleLibrary.FolderVariable"/>).</summary>
    public const string EnvironmentVariable = PeopleLibrary.FolderVariable;

    /// <summary>The folder beside a package's <c>bin</c> the package copies the people into (<c>tools/package-platform.ps1</c>).</summary>
    public const string PackageFolder = "People";

    /// <summary>
    /// The enhanced module's switches a recording's missing notes stand for (the program knows a module only through its
    /// interface, so the names are repeated here): <c>accel-ramp</c> (every body on the earlier build's ramp: a tape without the note
    /// <c>accel</c>) and <c>arrows-heavy</c> (the earlier build's war arrow: a tape without the note <see cref="ArrowsNote"/>).
    /// </summary>
    public const string AccelRampSwitch = "accel-ramp", ArrowsHeavySwitch = "arrows-heavy";

    /// <summary>The rules module's switch for the earlier build's landings on the spring (a tape on the spring without the note <c>landing</c>).</summary>
    public const string LandingSlideSwitch = "landing-slide";

    /// <summary>The tape head's note of the arrows a world with a bow flew (<c>items</c>, the items set's; <c>eh1</c>).</summary>
    public const string ArrowsNote = "arrows";

    /// <summary>
    /// <b>SANDBOX</b>: the crouched capsule's height of the models repository's bodies
    /// (m): a later set brought every crouch clip's head to 1.10 m or below on both bodies (the squat on the balls of the feet, the crouched
    /// walk every way, the turns, the ways down and up), so 1.15 — 1.1's own — covers every crouched pose, and the proving ground's 1.16 m
    /// tunnel is passed crouched. An older models repository (the head to 1.21 m crouch-walking to a side) clips the head
    /// through a ceiling this low. The setting <c>personCrouchHeight</c>, <c>--person-crouch-height</c>; a recording carries it.
    /// </summary>
    public const float CrouchHeight = 1.15f;

    /// <summary>The earlier builds' crouched capsule (the crouch, the head to 1.21 m): a tape without the note <see cref="CrouchNote"/>.</summary>
    public const float CrouchHeightTall = 1.25f;

    /// <summary>The crouched capsule's sense: at least the capsule's width (2 × 0.3 m), at most the standing height (1.8 m).</summary>
    public const float LeastCrouchHeight = 0.6f, MostCrouchHeight = 1.8f;

    /// <summary>The tape head's note of the crouched capsule (m), written when the player's body is the models repository's.</summary>
    public const string CrouchNote = "crouchHeight";

    /// <summary>
    /// The movement profile the player's new body moves by: profile <paramref name="index"/> of <paramref name="profiles"/>
    /// with its crouched capsule <paramref name="height"/> (<see cref="CrouchHeight"/> by default) — a profile of its own
    /// (<c>…/human-models</c>), added once and kept, so the masquerade's switches give the body its numbers with that crouch in either mode
    /// (the crouch is a number the masquerade keeps the body's own of). Answers its index. Configuration, before the first tick.
    /// </summary>
    public static int Crouched(Modules.MovementProfiles profiles, int index, string body, float height = CrouchHeight)
    {
        var own = profiles.All[index];
        var id = Modules.AssetId.Of(own.Id.Module, own.Id.Path + "-" + body);
        int found = profiles.IndexOf(id.ToString());
        if (found >= 0) return found;
        return profiles.Add(new Modules.MovementProfile(id, $"{own.Title}, crouched to {height.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} m (the models repository's {body})",
                                                        own.Numbers with { CrouchHeight = height }, own.Sandbox) { HalfLife = own.HalfLife });
    }

    /// <summary>The recipe of a body word, or null for 1.1's Christof (<c>oneone</c>, or none).</summary>
    public static PersonRecipe? RecipeOf(string? body) => body switch
    {
        Models => PersonRecipe.Player,
        Woman => PersonRecipe.Woman,
        _ => null,
    };

    /// <summary>
    /// The player's body: <paramref name="given"/> (<c>--body</c>), else <c>oneone</c> with <c>--play-as</c> (<paramref name="playAs"/>)
    /// or for a tool measuring the player's blows (<paramref name="combat"/>: the surveys), else <paramref name="setting"/> (the setting
    /// <c>playerBody</c>; a word that is no body is taken as models). A <c>--body</c> that is no body stops the start.
    /// </summary>
    public static string Choose(string? given, bool playAs, bool combat, string? setting)
    {
        if (given is not null) return IsBody(given) ? given : throw new SessionException($"--body is models, woman or oneone, not '{given}'");
        if (combat || playAs) return OneOne;
        return IsBody(setting) ? setting! : Models;
    }

    /// <summary>Whether <paramref name="word"/> is a body (<c>models</c>, <c>woman</c>, <c>oneone</c>).</summary>
    public static bool IsBody(string? word) => word is Models or Woman or OneOne;

    /// <summary>
    /// The models repository's folder: <paramref name="given"/> (<c>--models</c>), else <paramref name="setting"/> (the setting
    /// <c>modelsFolder</c>, the one used last), else <c>VITOMIR_MODELS_DIR</c>, else a <c>People</c> folder beside the program's
    /// <c>bin</c> (a package's copy), else <see cref="DefaultRepository"/> — the first that is a folder, as a full path; null for none.
    /// The loader's own <see cref="PeopleLibrary.Locate"/> looks at the folder named, the variable and the default only (and takes the
    /// variable even when it names no folder); this order (the setting and a package's copy between), and the folder
    /// chosen is then given to it.
    /// </summary>
    public static string? FindFolder(string? given, string? setting)
    {
        string? beside = null;
        try { beside = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", PackageFolder)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { }
        foreach (var candidate in new[] { given, setting, Environment.GetEnvironmentVariable(EnvironmentVariable), beside, DefaultRepository })
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate)) return Path.GetFullPath(candidate);
        return null;
    }

    /// <summary>The player's body as built: the body word, its kind among the session's, the person, the folder and commit, the load's time.</summary>
    public sealed record Built(string Body, int Kind, PersonModel Person, string Folder, string? Commit, double Milliseconds)
    {
        /// <summary>The library it was built with (the townsfolk are built with it too, so the maps they share are decoded once).</summary>
        public PeopleLibrary? Library { get; init; }
    }

    /// <summary>
    /// Builds the player's body <paramref name="body"/> (<c>models</c>, <c>woman</c>) from <paramref name="folder"/> into
    /// <paramref name="assets"/> (a kind named <c>people:&lt;recipe&gt;</c>, after every 1.1 kind, so theirs keep their indices) and its
    /// textures into <paramref name="textures"/>; null with the reason in <paramref name="why"/> when it cannot (no folder, not the
    /// models repository's, the loader or the kinds not built yet, a file that will not read). Never throws for those. Its images are decoded
    /// only with <paramref name="decodeImages"/> (play in a window: a person's maps are about 300 MB; <see cref="PeopleLibrary.DecodeImages"/>).
    /// </summary>
    public static Built? TryBuild(string body, string? folder, CharacterAssets assets, IDictionary<string, TextureImage> textures, out string why,
                                  bool decodeImages = false, int maxTextureSide = 0)
    {
        why = "";
        if (RecipeOf(body) is not { } recipe) { why = $"'{body}' is no body of the models repository's (models, woman)"; return null; }
        if (folder is null)
        {
            why = $"no models folder found (--models, the setting modelsFolder, {EnvironmentVariable}, {PackageFolder} beside the program, {DefaultRepository})";
            return null;
        }
        var clock = Stopwatch.StartNew();
        PeopleLibrary? library;
        try
        {
            library = PeopleLibrary.Locate(folder, out string notOpen, decodeImages, maxTextureSide);   // The folder named; its maps capped
            if (library is null) { why = $"{folder} is not the models repository's ({notOpen})"; return null; }
        }
        catch (NotImplementedException e) { why = $"the models repository's loader is not built yet ({e.Message})"; return null; }
        catch (Exception e) when (Reading(e)) { why = $"{folder} could not be opened: {e.Message}"; return null; }
        PersonModel person;
        int kind;
        try
        {
            person = library.Person(recipe);
            var motions = library.Motions(recipe.Sex);
            kind = PeopleKinds.Register(assets, person, motions, "people:" + recipe.Name);
        }
        catch (NotImplementedException e) { why = $"the models repository's {recipe.Name} could not be built yet ({e.Message})"; return null; }
        catch (Exception e) when (Reading(e)) { why = $"the models repository's {recipe.Name} could not be built from {folder}: {e.Message}"; return null; }
        foreach (var t in person.Textures) textures.TryAdd(t.Name, t);
        return new Built(body, kind, person, library.Folder, library.Commit, clock.Elapsed.TotalMilliseconds) { Library = library };
    }

    /// <summary>The exceptions a file that will not read or a person that will not build throws (not the program's own faults).</summary>
    static bool Reading(Exception e) =>
        e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or System.Text.Json.JsonException
            or KeyNotFoundException or ArgumentException or InvalidOperationException;
}
