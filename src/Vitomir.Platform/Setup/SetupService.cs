// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The setup and import tool's working half, as one service: what the command line and the launcher's Setup page both call.
using System.Globalization;
using System.Text.RegularExpressions;

namespace Vitomir.Platform.Setup;

/// <summary>One of 1.1's things to import, by its name (<c>SP4</c>), into another slot when <see cref="As"/> names one (<c>SP12</c>).</summary>
public sealed record OriginalPick(string Id, string? As = null)
{
    /// <summary>A pick as the command line writes it: <c>SP4</c>, or <c>SP4=SP12</c> for another slot.</summary>
    public static OriginalPick Parse(string text) =>
        text.IndexOf('=') is var eq and > 0 ? new(text[..eq].Trim(), text[(eq + 1)..].Trim()) : new(text.Trim());
}

/// <summary>
/// **The setup and import tool, its working half**: finding the player's 1.1, checking it against the manifest,
/// importing its data into the user folder, listing and importing 1.1's own saves into the Classic module's folder, and finding FFmpeg.
/// It prints nothing: each step answers a report (and its progress through <see cref="IProgress{T}"/>), refusals among its results,
/// so the command line (<c>vitomir-platform --setup</c>) says them in text and the launcher's Setup page shows them.
/// The two functions are two calls: <see cref="ImportData"/> and <see cref="ImportOriginals"/>; Vitomir's own old user data
/// (<c>%LOCALAPPDATA%\Vitomir</c>) is never read.
/// </summary>
public sealed class SetupService(string userData, DataManifest manifest, ISetupMachine machine, ISaveImporter importer, string madeBy)
{
    /// <summary>The platform's user data (<c>%LOCALAPPDATA%\Vitomir</c>, or <c>VITOMIR_USERDATA</c>).</summary>
    public string UserData { get; } = Path.GetFullPath(userData);

    /// <summary>The same service for another user folder (the launcher's, once the player chose or moved it), its importer kept.</summary>
    public SetupService With(string otherUserData) => new(otherUserData, Manifest, Machine, importer, madeBy);

    /// <summary>The manifest the install is checked against (this program's, <see cref="DataManifest.Builtin"/>, unless a developer's).</summary>
    public DataManifest Manifest { get; } = manifest;

    public ISetupMachine Machine { get; } = machine;

    /// <summary>Where the base game's data is imported: <c>&lt;user data&gt;\Imported\vtmr-1.1</c>.</summary>
    public string DataFolder => ImportedData.Folder(UserData);

    /// <summary>The Classic module's own folder in the user data (<c>Modules\classic</c>, <c>LoopCommand.UserFolderOf</c>).</summary>
    public string ClassicFolder => Path.Combine(UserData, "Modules", "classic");

    /// <summary>Its saves (<c>SP0.vsave</c>, the autosave, to <c>SP29.vsave</c>; the network's <c>MP&lt;n&gt;.vsave</c>).</summary>
    public string SavesFolder => Path.Combine(ClassicFolder, "Saves");

    /// <summary>Its multiplayer characters (<c>&lt;name&gt;.vplayer</c>; <c>NetPlay.CryptFolder</c>, <c>Net.Crypt</c>).</summary>
    public string CryptFolder => Path.Combine(ClassicFolder, "Crypt");

    /// <summary>Where one of 1.1's things would be imported to: its slot in <see cref="SavesFolder"/> or its file in <see cref="CryptFolder"/>; null when it cannot be.</summary>
    public string? TargetOf(OriginalItem item) => item.ImportsAs is not { } file ? null
        : Path.Combine(item.Kind == OriginalKind.Character ? CryptFolder : SavesFolder, file);

    /// <summary>
    /// The character file kept in <see cref="CryptFolder"/> under <paramref name="file"/>'s name, compared without regard to case (the game
    /// finds a character by its name so: <c>NetPlay</c>, and Windows' own folders), or null.
    /// </summary>
    public string? KeptCharacter(string file)
    {
        string exact = Path.Combine(CryptFolder, file);
        if (File.Exists(exact)) return exact;
        if (!Directory.Exists(CryptFolder)) return null;
        return Directory.EnumerateFiles(CryptFolder, "*" + OriginalGames.CharacterExtension)
            .FirstOrDefault(f => Path.GetFileName(f).Equals(file, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Step 1: every install found, with its store and how it was found (<see cref="InstallFinder"/>).</summary>
    public IReadOnlyList<InstallCandidate> Find(string? named = null) => InstallFinder.Find(Machine, named);

    /// <summary>The install to use of those found: the one named, else the first GOG or Steam copy, else the first.</summary>
    public static InstallCandidate? Choose(IReadOnlyList<InstallCandidate> found, string? named = null)
    {
        if (named is not null)
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(named));
            return found.FirstOrDefault(c => c.Folder.Equals(full, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        }
        return found.FirstOrDefault(c => c.Supported) ?? found.FirstOrDefault();
    }

    /// <summary>Step 2: the check of an install (<see cref="DataCheck"/>).</summary>
    public DataCheckReport Check(string install, CheckDepth depth, IProgress<SetupProgress>? progress = null, CancellationToken cancel = default) =>
        DataCheck.Run(install, Manifest, depth, progress, cancel);

    /// <summary>
    /// Step 3: the import of the base game's data (<see cref="DataImport"/>), and then its templates, scenes and camera paths recooked
    /// as text beside it (<see cref="Vitomir.Formats.Recooked"/>). A recooking that fails leaves the import accepted: the game reads
    /// the original files then, and makes the recooked ones at its next start.
    /// </summary>
    public DataImportReport ImportData(string install, IProgress<SetupProgress>? progress = null, CancellationToken cancel = default)
    {
        var report = DataImport.Run(install, DataFolder, Manifest, Machine, madeBy, progress, cancel);
        if (report.Accepted && Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(DataFolder)) is { } imported)
        {
            try { Vitomir.Formats.Recooked.Make(DataFolder, Path.Combine(imported, Vitomir.Formats.Recooked.FolderName), madeBy); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or Vitomir.Formats.NodFormatException) { }
        }
        return report;
    }

    /// <summary>**Verify files**: the imported game files read whole and checked against their manifest, extra files found (<see cref="DataVerify.Run"/>).</summary>
    public DataVerifyReport Verify(IProgress<SetupProgress>? progress = null, CancellationToken cancel = default) => DataVerify.Run(UserData, progress, cancel);

    /// <summary>**Repair**: what a verification found copied again from the copy of the game the files came from (<see cref="DataVerify.Repair"/>).</summary>
    public DataRepairReport Repair(DataVerifyReport verified, IProgress<SetupProgress>? progress = null, CancellationToken cancel = default) =>
        DataVerify.Repair(verified, progress, cancel);

    /// <summary>The imported data as a start finds it (<see cref="ImportedData.Open"/>).</summary>
    public ImportedState Imported(bool developer = false) => ImportedData.Open(UserData, DataManifest.Builtin, developer);

    /// <summary>Step 4: 1.1's own saves, network games and characters in an install (<see cref="OriginalGames"/>).</summary>
    public IReadOnlyList<OriginalItem> Originals(string install) => OriginalGames.List(install);

    static readonly Regex SlotName = new("^(SP|MP)([0-9]{1,2})$", RegexOptions.IgnoreCase);

    /// <summary>
    /// **Step 5: the chosen ones imported** into the Classic module's saves, each through the game's own save import
    /// (<see cref="ISaveImporter"/>), with the game's files from the imported data when it is ready, else the install's own. **Nothing is
    /// written over without <paramref name="replace"/>**: a save already in the slot refuses the import, naming it; with it the old one is
    /// kept beside it as <c>.previous</c>, as a save over a slot keeps one (<c>SaveArchive.Write</c>). A save goes to the slot of its own
    /// number (1.1's <c>SP4</c> to <c>SP4.vsave</c>: 1.1's slot 0 is its autosave, as Vitomir's is), or to the one its pick names, of the
    /// same family (a network game's save to <c>MP&lt;n&gt;</c>). **A character** goes to the Classic module's Crypt as
    /// <c>&lt;its name&gt;.vplayer</c>, or under the name its pick gives (<c>npf000000=Anna</c>), by the same rule: one of that name kept
    /// there already (without regard to case) refuses it, and <paramref name="replace"/> replaces it, keeping it as <c>.previous</c>. One
    /// whose file does not read is refused with why (<see cref="OriginalItem.Why"/>).
    /// </summary>
    public IReadOnlyList<SaveImportResult> ImportOriginals(string install, IEnumerable<OriginalPick> picks, bool replace, CancellationToken cancel = default)
    {
        var items = Originals(install);
        var state = Imported();
        string data = state.Ready ? state.Folder : Path.GetFullPath(install);
        var results = new List<SaveImportResult>();
        foreach (var pick in picks)
        {
            cancel.ThrowIfCancellationRequested();
            var item = items.FirstOrDefault(i => i.Id.Equals(pick.Id, StringComparison.OrdinalIgnoreCase));
            SaveImportResult Refuse(OriginalItem it, string why, string? target = null) => new(it, target, false, true, why, []);
            if (item is null)
            {
                results.Add(Refuse(new(OriginalKind.Save, pick.Id, "", default, "", null),
                    $"{pick.Id}: no such save or character in {Path.Combine(install, "SaveGames")} or {Path.Combine(install, "Crypt")}"));
                continue;
            }
            if (item.ImportsAs is null) { results.Add(Refuse(item, $"{item.Id}: not imported: {item.Why ?? "it cannot be read"}")); continue; }
            if (item.Kind == OriginalKind.Character)
            {
                string? renamed = pick.As is null ? null : OriginalGames.CleanName(pick.As);
                if (renamed is { Length: 0 }) { results.Add(Refuse(item, $"{item.Id}: a character needs a name to be kept by ({item.Id}=<name>)")); continue; }
                string file = renamed is null ? item.ImportsAs : OriginalGames.CharacterFile(renamed);
                string as_ = renamed ?? item.Name ?? item.Id;
                if (KeptCharacter(file) is { } kept && !replace)
                {
                    results.Add(Refuse(item, $"{item.Id}: a character called {as_} is kept already ({kept}): not written over (--replace replaces it, "
                                             + $"keeping it as {Path.GetFileName(kept)}.previous; or import it under another name, {item.Id}=<name>)", kept));
                    continue;
                }
                string target = KeptCharacter(file) ?? Path.Combine(CryptFolder, file);
                results.Add(Place(item, target, CryptFolder, ".tmp", replace, $"imported as {as_}, {target}",
                                  temp => importer.Import(item.Path, temp, data, renamed, cancel)));
                continue;
            }
            string slot = pick.As ?? Path.GetFileNameWithoutExtension(item.ImportsAs);
            var m = SlotName.Match(slot);
            string family = item.Kind == OriginalKind.NetworkSave ? "MP" : "SP";
            if (!m.Success || int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) >= OriginalGames.Slots || !m.Groups[1].Value.Equals(family, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(Refuse(item, $"{item.Id}: {slot} is not a slot for it ({family}0 to {family}{OriginalGames.Slots - 1}" +
                                         (family == "MP" ? ", a network game's" : ", a single-player game's") + ")"));
                continue;
            }
            string saveTarget = Path.Combine(SavesFolder, family + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + ".vsave");
            if (File.Exists(saveTarget) && !replace)
            {
                results.Add(Refuse(item, $"{item.Id}: {Path.GetFileName(saveTarget)} is there already ({saveTarget}): not written over (--replace replaces it, "
                                         + $"keeping it as {Path.GetFileName(saveTarget)}.previous; or import it into another slot, {item.Id}={family}<n>)", saveTarget));
                continue;
            }
            results.Add(Place(item, saveTarget, SavesFolder, ".vsave", replace, $"imported as {saveTarget}", temp => importer.Import(item.Path, temp, data, cancel)));
        }
        return results;
    }

    /// <summary>
    /// One import run into a file of its own beside <paramref name="target"/> and moved over it when it is done: the one there (only with
    /// <paramref name="replace"/>) kept as <c>.previous</c>; one that appeared meanwhile, without it, left alone; nothing left behind.
    /// </summary>
    static SaveImportResult Place(OriginalItem item, string target, string folder, string extension, bool replace, string done, Func<string, (int Code, IReadOnlyList<string> Lines)> run)
    {
        Directory.CreateDirectory(folder);
        string temp = Path.Combine(folder, $".setup-{Guid.NewGuid():N}{extension}");
        try
        {
            var (code, lines) = run(temp);
            if (code != 0 || !File.Exists(temp)) return new(item, target, false, false, $"{item.Id}: the import failed (exit code {code})", lines);
            if (File.Exists(target))
            {
                if (!replace) return new(item, target, false, true, $"{item.Id}: {Path.GetFileName(target)} appeared while it was imported: not written over", lines);
                File.Copy(target, target + ".previous", overwrite: true);
            }
            File.Move(temp, target, overwrite: replace);
            return new(item, target, true, false, $"{item.Id} \"{item.Description}\" {done}", lines);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(item, target, false, false, $"{item.Id}: {e.Message}", []); }
        finally
        {
            foreach (var leftover in new[] { temp, temp + ".writing" })
                try { if (File.Exists(leftover)) File.Delete(leftover); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Step 6: the player's FFmpeg, where the game looks for it (<see cref="FFmpegSearch"/>), or null: the films are then skipped.</summary>
    public static FFmpegFound? FFmpeg(string? told = null) => FFmpegSearch.Find(told);
}
