// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Net;
using Vitomir.Engine.Save;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Every text file Vitomir writes explains itself, and `--save-strict-json` takes the explanations
/// out** (reading is lenient — comments, trailing commas — and writing is
/// strict apart from the comments themselves). A kept network character (`.vplayer`) was the one
/// file that broke the rule — plain JSON, no comments, and a reader that refused them — until then;
/// it is now written and read as the save's parts are (<see cref="Jsonc"/>), and a file written
/// before still reads.
/// </summary>
public class StrictJsonTests
{
    static readonly NetCharacter Kept = new()
    {
        Name = "Wilhem's cousin", Template = "MP_Christof_DA", Type = ActorType.Ghoul, Clan = "brujah",
        Experience = 1500, Cash = NetCharacter.NewCash,
        Saved = new NetSheet { BaseStats = [1, 2, 3], Stats = [1, 2, 3], Health = 40, MaxHealth = 50, Cash = 120 },
    };

    static string Folder() => Directory.CreateTempSubdirectory("vitomir-crypt-").FullName;

    /// <summary>A kept character carries the banner, and comes back as it went in.</summary>
    [Fact]
    public void A_kept_character_explains_itself_and_reads_back()
    {
        var folder = Folder();
        try
        {
            var crypt = new Crypt(folder) { Strict = false };
            crypt.Save(Kept);
            string text = File.ReadAllText(Directory.GetFiles(folder, "*" + Crypt.Extension).Single());
            Assert.StartsWith("// A Vitomir multiplayer character", text);
            var back = Assert.Single(crypt.All());
            Assert.Equal(Kept.Name, back.Name);
            Assert.Equal(Kept.Type, back.Type);
            Assert.Equal(Kept.Clan, back.Clan);
            Assert.Equal(Kept.Experience, back.Experience);
            Assert.Equal(40f, back.Saved!.Health);
            Assert.Equal(new float[] { 1, 2, 3 }, back.Saved.Stats);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    /// <summary>
    /// A file written earlier — the framework's default JSON, capitalised names, no comments — still
    /// reads; and a person's own comment and trailing comma in one do not stop it.
    /// </summary>
    [Fact]
    public void A_kept_character_written_before_the_comments_still_reads()
    {
        string old = JsonSerializer.Serialize(Kept, new JsonSerializerOptions { WriteIndented = true });
        Assert.Contains("\"Name\"", old);
        var back = Crypt.Parse(old)!;
        Assert.Equal(Kept.Name, back.Name);
        Assert.Equal(Kept.Type, back.Type);
        Assert.Equal(120, back.Saved!.Cash);

        var edited = Crypt.Parse("// mine\n{ \"name\": \"Anezka\", \"clan\": \"toreador\", // a note\n }")!;
        Assert.Equal("Anezka", edited.Name);
        Assert.Equal("toreador", edited.Clan);
    }

    /// <summary>`--save-strict-json`: the kept character's text is plain JSON, which a strict reader takes.</summary>
    [Fact]
    public void A_strict_kept_character_is_plain_json()
    {
        string text = new Crypt(Path.GetTempPath()) { Strict = true }.Text(Kept);
        Assert.DoesNotContain("//", text);
        using var doc = JsonDocument.Parse(text);                  // no comment handling: strict
        Assert.Equal(Kept.Name, doc.RootElement.GetProperty("name").GetString());
        Assert.Equal(Kept.Name, Crypt.Parse(text)!.Name);
    }

    /// <summary>
    /// `--save-strict-json` on a save: a player's own save written again, every part strict JSON without a
    /// comment — and the same save written as usual carries them. Reading it back gives the same parts.
    /// </summary>
    [Fact]
    public void A_strict_save_is_plain_json_in_every_part()
    {
        // His own save where the game keeps it, then its copy in the cloud workspace (as GuidTests finds it).
        string path = new[] { Path.Combine(Diag.UserDataFolder, "Saves", "SP3.vsave"),
                              Path.Combine(Environment.GetEnvironmentVariable("VITOMIR_NPF_SAMPLES") ?? "", "Vitomir", "Saves", "SP3.vsave") }.FirstOrDefault(File.Exists) ?? "";
        Assert.SkipUnless(path.Length > 0, "no player's own save here");
        var save = SaveArchive.Read(path);
        var strict = SaveArchive.Parts(save, strict: true);
        var usual = SaveArchive.Parts(save, strict: false);
        Assert.Equal(usual.Select(p => p.Name), strict.Select(p => p.Name));
        foreach (var (name, text) in strict)
        {
            Assert.False(text.Contains("\n//") || text.StartsWith("//") || text.Contains("  //"), $"{name} has a comment");
            using var doc = JsonDocument.Parse(text);             // throws on a comment or a trailing comma
        }
        Assert.StartsWith("// Vitomir save", usual.First(p => p.Name == SaveArchive.HeaderPart).Text);
        var header = Jsonc.Read<SaveHeader>(strict.First(p => p.Name == SaveArchive.HeaderPart).Text);
        Assert.Equal(save.Header.Location, header.Location);
    }
}
