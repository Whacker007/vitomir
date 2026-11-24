// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Modules.Enhanced.Configuration;
using Vitomir.Platform.Core;

namespace Vitomir.Modules.Enhanced.Sheets;

/// <summary>A sheet template: its id, its status, where its numbers come from, and the sheet it gives.</summary>
public sealed record SheetTemplate(string Id, string Status, string Provenance, SheetData Sheet);

/// <summary>
/// Sheet templates. Role templates (townsfolk, guards, the campaign's actors) are open with the rules package
/// (the package: campaign actor profiles are "LATER", and "uniform human purchased 25" is named a
/// research-only choice), so the one template here is a <b>SANDBOX placeholder</b>, so that the townsfolk
/// walking the East Gate have sheets at all. It is refused unless the module runs the research sandbox.
/// </summary>
public static class SheetTemplates
{
    public const string PlaceholderStatus = "SANDBOX_PLACEHOLDER";
    public const string TownspersonId = "sandbox.townsperson";

    /// <summary>
    /// A townsperson: species <c>human</c>, no generation, era not given; every Attribute purchased at 25 and
    /// every Ability at 0; no powers, resources, familiarity or conditions. The 25 is the rules package's
    /// research-only "uniform human purchased 25" (handbook 08, deferred and rejected defaults), used here as
    /// a placeholder and nothing more.
    /// </summary>
    public static SheetTemplate Townsperson(EnhancedConfig config)
    {
        if (!config.IsSandbox)
            throw new InvalidOperationException($"the template {TownspersonId} is a {PlaceholderStatus} and is refused under profile {config.Profile.ProfileId}; it runs only in the research sandbox");
        var layout = config.Layout;
        var purchased = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in layout.Traits)
            purchased[t.Id] = t.Kind == TraitKind.Attribute ? Math.Clamp(25, t.Domain.Min, t.Domain.MaxPurchased) : t.Domain.Min;
        var familiarity = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var f in layout.Families) familiarity[f] = layout.FamiliarityMin;
        var sheet = new SheetData("human", "", "", TownspersonId, purchased, familiarity, [], [], new Dictionary<string, string>(), []);
        return new SheetTemplate(TownspersonId, PlaceholderStatus,
            "ours: placeholder for the townsfolk; Attributes 25 = the package's research-only 'uniform human purchased 25' (handbook 08); role templates open with the rules package",
            sheet);
    }

    /// <summary>Gives an entity the townsperson placeholder sheet (sandbox only).</summary>
    public static void GiveTownsperson(ActorSheets sheets, EnhancedConfig config, EntityId e) =>
        sheets.Import(e, Townsperson(config).Sheet);
}
