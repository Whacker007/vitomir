// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A load turns a character's discipline list round** (from the first reading of version 5 saves):
/// 1.1 writes the list head first and reads it back through its own way in — each
/// record found or made on the front, its level set, which opens the group's next on the
/// front. 1.1 holds Christof's list from <c>celerity</c> on after a load where its own save
/// holds it from <c>drawingoutbeast</c>; the order matters because the AI's ladder meets the newest
/// first. Pinned against Wilhem's list in 1.1's Monastery save <c>SP4</c>.
/// </summary>
public class DisciplineLoadOrderTests
{
    static (List<Discipline> Saved, DisciplineDefinitions Defs, WorldThing Owner) Saved(Vitomir.Engine.Vfs.GameFiles files, params (string Name, int Level)[] records)
    {
        var defs = new DisciplineDefinitions(files);
        var owner = new WorldThing { Index = 4, Guid = 5, Scene = new Vitomir.Formats.NsdThing(), Template = "christof", Type = ThingType.Player, Actor = new Actor() };
        return ([.. records.Select(r => new Discipline { Definition = defs[r.Name]!, Owner = owner, Level = r.Level, QuickSlot = 3 })], defs, owner);
    }

    [Fact]
    public void Wilhems_list_in_the_monastery_save_comes_back_turned_round()
    {
        using var files = Install.Open();
        var (saved, defs, owner) = Saved(files, ("feed", 1), ("bloodhealing", 1), ("bloodstrength", -1), ("awe", 0), ("dreadgaze", -1),
                                         ("potence", 1), ("celerity", 0), ("eyesofthebeast", 0), ("feralclaws", 0), ("earthmeld", -1));
        var list = DisciplineLoadOrder.Rebuild(saved, defs, owner);
        Assert.Equal(["earthmeld", "feralclaws", "eyesofthebeast", "celerity", "potence", "dreadgaze", "awe", "bloodstrength", "bloodhealing", "feed"],
                     list.Select(d => d.Name.ToLowerInvariant()));
        Assert.All(list, d => Assert.Contains(d, saved));                  // every instance is the record's, with its fields
    }

    /// <summary>
    /// The proof from 1.1 itself: Christof's 32 disciplines as 1.1's Monastery save <c>SP4</c> holds them,
    /// loaded, saved and loaded again, then three Obfuscate powers given to him, saved and loaded
    /// once more, are the 35 the running game holds — in that exact order, the two places
    /// where a group's next kept the place it was opened in (Majesty ahead of Entrancement, Awaken ahead of
    /// Blood Stamina) included.
    /// </summary>
    [Fact]
    public void Three_loads_of_the_monastery_save_give_the_list_1_1_had()
    {
        using var files = Install.Open();
        var (saved, defs, owner) = Saved(files,
            ("drawingoutbeast", 1), ("subsumethespirit", 0), ("quellbeast", 0), ("beckoning", 0), ("feralwhispers", 0),
            ("summonelemental", 0), ("ignoreflame", 0), ("calllightning", 0), ("mistform", 1), ("shapeofthebeast", 0),
            ("earthmeld", 0), ("possession", 4), ("theforgetfulmind", 0), ("prisonofice", 0), ("heartofstone", 0),
            ("feralclaws", 0), ("eyesofthebeast", 0), ("mesmerize", 0), ("command", 0), ("walktheabyss", 0), ("awaken", 0),
            ("majesty", 1), ("feed", 0), ("bloodhealing", 0), ("bloodstrength", 0), ("bloodstamina", 0), ("blooddexterity", 0),
            ("awe", 1), ("entrancement", 1), ("dreadgaze", 1), ("potence", 1), ("celerity", 1));
        var twice = DisciplineLoadOrder.Rebuild(DisciplineLoadOrder.Rebuild(saved, defs, owner), defs, owner);
        var (obfuscate, _, _) = Saved(files, ("cloakthegathering", 4), ("mask1000faces", 2), ("cloakofshadows", 4));
        var read = DisciplineLoadOrder.Rebuild([.. obfuscate, .. twice], defs, owner);
        Assert.Equal(("celerity potence majesty entrancement dreadgaze awe awaken bloodstamina blooddexterity bloodstrength " +
                      "bloodhealing feed walktheabyss command mesmerize theforgetfulmind eyesofthebeast feralclaws earthmeld " +
                      "heartofstone prisonofice calllightning possession shapeofthebeast mistform ignoreflame summonelemental " +
                      "feralwhispers beckoning quellbeast subsumethespirit drawingoutbeast cloakofshadows mask1000faces cloakthegathering").Split(' '),
                     read.Select(d => d.Name.ToLowerInvariant()));
    }

    /// <summary>
    /// **1.1 itself, a load apart**: A University Haven save (SP3, 02:16:26) loaded
    /// and saved again in West London (SP4, 02:22:51) — Serena's seventeen disciplines in the second file
    /// are the first file's, rebuilt by exactly this rule (Christof's and Wilhem's match too). Summon Soul
    /// opens Plague Wind at the front early on, so Plague Wind's own record, when it comes, keeps that
    /// place near the end rather than going to the front.
    /// </summary>
    [Fact]
    public void Serenas_list_across_a_load_in_1_1_is_this_rule_s()
    {
        using var files = Install.Open();
        string[] first = ["summonsoul", "vigormortis", "shamblinghordes", "fortitude", "spiritstouch", "auraperception", "heightenedsenses",
                          "bloodstrength", "bloodhealing", "feed", "blooddexterity", "bloodstamina", "awaken", "walktheabyss",
                          "psychicprojection", "plaguewind", "blackdeath"];
        var (saved, defs, owner) = Saved(files, [.. first.Select(n => (n, 4))]);
        Assert.Equal(["blackdeath", "walktheabyss", "awaken", "bloodstamina", "feed", "bloodhealing", "blooddexterity", "bloodstrength",
                      "heightenedsenses", "auraperception", "psychicprojection", "spiritstouch", "fortitude", "shamblinghordes", "vigormortis",
                      "plaguewind", "summonsoul"],
                     DisciplineLoadOrder.Rebuild(saved, defs, owner).Select(d => d.Name.ToLowerInvariant()));
    }

    [Fact]
    public void A_groups_next_that_comes_later_in_the_file_keeps_the_place_it_was_opened_in()
    {
        using var files = Install.Open();
        var (saved, defs, owner) = Saved(files, ("feed", 1), ("awe", 0), ("bloodhealing", 0));
        var list = DisciplineLoadOrder.Rebuild(saved, defs, owner);
        Assert.Equal(["bloodstrength", "dreadgaze", "awe", "bloodhealing", "feed"], list.Select(d => d.Name.ToLowerInvariant()));
        Assert.Same(saved[2], list[3]);
        var opened = list[0];
        Assert.Equal((-1, -1, 0u), (opened.Level, opened.QuickSlot, opened.Flags));   // made at −1, announced, no slot
    }
}
