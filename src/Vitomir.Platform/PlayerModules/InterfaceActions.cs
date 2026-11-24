// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.PlayerModules;

/// <summary>
/// One of **Vitomir's own interface actions** a player module may bind an element of its interface layouts to: its name in a
/// <c>module.json</c>'s <c>actions</c>, what it does in words, whether it changes the rules (it opens something where 1.1 would
/// not: a rules module), and, when it is what one of 1.1's status bar elements already does, that element (and whether as the
/// right button's: disperse is the regroup button's right click).
/// </summary>
public sealed record InterfaceAction(string Name, string Words, bool Rules = false, string? BarElement = null, bool Right = false);

/// <summary>
/// **The fixed list of the interface actions** a module's element may be bound to (the players' documents list the same; more only
/// by adding them here), the status bars' own elements of 1.1 (a binding of one of them is
/// not used: it keeps 1.1's behaviour), and what a module's bindings come to.
/// <para>A module's <c>actions</c> maps an element's name (as its <c>.nui</c> names it; 1.1 reads names without regard to case) to an
/// action's name: <c>"actions": { "sbVault": "open-vault", "sbLvlUp": "open-advancement" }</c>. In release 1 the bindings take effect
/// on the status bars (<c>statusBarSP.nui</c>, <c>statusBarMP.nui</c>): a click on an element 1.1 does not know runs its action.</para>
/// </summary>
public static class InterfaceActions
{
    /// <summary>The actions, in the order the documents list them.</summary>
    public static readonly InterfaceAction[] All =
    [
        new("open-advancement", "opens the advancement screen for the character steered (experience spent on traits and disciplines)", Rules: true),
        new("open-vault", "opens the vault (the haven's chest) beside the inventory, wherever the character steered is", Rules: true),
        new("open-map", "opens the map, as the map button", BarElement: "sbMapButton"),
        new("open-inventory", "opens or closes the inventory, as its button", BarElement: "sbInventoryButton"),
        new("open-disciplines", "opens or closes the disciplines, as their button", BarElement: "sbDisciplineButton"),
        new("open-quests", "opens or closes the quests, as their button", BarElement: "sbQuestButton"),
        new("open-character-sheet", "opens or closes the character sheet, as its button", BarElement: "sbCharacterButton"),
        new("stance-attack", "the party's attack stance, as its button", BarElement: "sbAttackRadio"),
        new("stance-neutral", "the party's neutral stance, as its button", BarElement: "sbNeutralRadio"),
        new("stance-defense", "the party's defensive stance, as its button", BarElement: "sbDefenseRadio"),
        new("regroup", "the party regroups, as the regroup button", BarElement: "sbRegroupButton"),
        new("disperse", "the party disperses, as the regroup button's right click", BarElement: "sbRegroupButton", Right: true),
        new("quick-use-1", "uses the item in the first quick-use slot, as its key"),
        new("quick-use-2", "uses the item in the second quick-use slot"),
        new("quick-use-3", "uses the item in the third quick-use slot"),
        new("quick-use-4", "uses the item in the fourth quick-use slot"),
        new("quick-use-5", "uses the item in the fifth quick-use slot"),
        new("save", "opens the save screen (single player)"),
        new("load", "opens the load screen (single player)"),
        new("escape-menu", "opens the escape menu"),
    ];

    /// <summary>The action of that name (as a manifest writes it, without regard to case), or null when it is not one of the list.</summary>
    public static InterfaceAction? Find(string? name) =>
        name is null ? null : All.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// **The elements 1.1's own status bars have** (<c>statusBarSP</c>, <c>statusBarMP</c>, <c>statusBarST</c>, <c>statusBarADV</c>; their
    /// names only, lower case, as 1.1 reads them). An element of these keeps 1.1's behaviour whatever a module binds it to.
    /// </summary>
    public static readonly IReadOnlySet<string> OneOneBarElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "advokbutton", "advresetbutton", "advspdummy", "sbactorpanecheck", "sbadvancementbutton", "sbattackradio", "sbcharacterbutton",
        "sbcharmodebutton", "sbchatbox", "sbchatedit", "sbchatscrollbar", "sbchatscrollslider", "sbcontrolspanecheck", "sbcurrentdiscipline",
        "sbcurrenttarget", "sbdefenseradio", "sbdeleteobjectbutton", "sbdiscipline0", "sbdiscipline1", "sbdiscipline2", "sbdiscipline3",
        "sbdiscipline4", "sbdiscipline5", "sbdisciplinebutton", "sbheadhotspot", "sbinventorybutton", "sblocationpanecheck", "sbmapbutton",
        "sbneutralradio", "sbobjectpanecheck", "sbpausebutton", "sbpossessbutton", "sbquestbutton", "sbquickuse", "sbregroupbutton",
        "sbrevivebutton", "sbscenepanecheck", "sbscrollbar", "sbscrollslider", "sbsetghostbutton", "sbstbutton", "sbstcharacterbutton",
        "sbtextbox", "sbtextscrollbar", "sbtextscrollslider",
        "statusblood0", "statusblood1", "statusblood2", "statusblood3", "statusfaith0", "statusfaith1", "statusfaith2", "statusfaith3",
        "statusfrenzy0", "statusfrenzy1", "statusfrenzy2", "statusfrenzy3", "statushealth0", "statushealth1", "statushealth2", "statushealth3",
        "statushotspot0", "statushotspot1", "statushotspot2", "statushotspot3", "statusindicators0", "statusindicators1", "statusindicators2",
        "statusindicators3", "statusmana0", "statusmana1", "statusmana2", "statusmana3", "statusparty0", "statusparty1", "statusparty2", "statusparty3",
    };

    /// <summary>The status bars' layouts a binding takes effect on (1.1 paths).</summary>
    public static readonly string[] BarLayouts = ["ui/statusBarSP.nui", "ui/statusBarMP.nui"];

    /// <summary>
    /// **The bindings a module's <c>actions</c> come to**: each element's action, leaving out an element 1.1's status bars have (it keeps
    /// 1.1's behaviour) and an action that is not one of the list (the checker refuses the module for it).
    /// </summary>
    public static IReadOnlyDictionary<string, InterfaceAction> Effective(IReadOnlyDictionary<string, string> actions)
    {
        var bound = new Dictionary<string, InterfaceAction>(StringComparer.OrdinalIgnoreCase);
        foreach (var (element, name) in actions)
            if (!OneOneBarElements.Contains(element) && Find(name) is { } action) bound[element] = action;
        return bound;
    }

    /// <summary>Whether bindings open something where 1.1 would not (the advancement screen, the vault): they make a module a rules module.</summary>
    public static bool ChangeRules(IReadOnlyDictionary<string, string> actions) => Effective(actions).Values.Any(a => a.Rules);
}

/// <summary>A file of a pack made for a patched 1.1 program that Vitomir recognises by its SHA-256, and the bindings it writes for it on import.</summary>
public sealed record RecognisedFile(string Pack, string Path, string Sha256, IReadOnlyDictionary<string, string> Actions);

/// <summary>
/// **The recognised packs' files**: a small table, by each file's SHA-256, of interface layouts made for a patched 1.1 program
/// whose buttons name actions 1.1 itself does not have. A bare pack imported with one of them gets its bindings written into the
/// <c>module.json</c> Vitomir makes for it. Only the names and the hashes are here, never a file of a pack's.
/// </summary>
public static class RecognisedPacks
{
    public static readonly RecognisedFile[] All =
    [
        // VTMR Crimson Edition's status bar: a level-up button (the advancement screen) and a vault button, which its patched
        // game program answers.
        new("Crimson Edition", "ui/StatusBarSP.nui", "00c0326d0477a75c241143ff00e1af108a2c31bf20ac5c4eac9eb11c4acd6507",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["sbLvlUp"] = "open-advancement", ["sbVault"] = "open-vault" }),
    ];
}
