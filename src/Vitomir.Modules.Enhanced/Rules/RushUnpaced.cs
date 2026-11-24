// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>
/// <b>Rushing creatures keep their own pace</b> (ours, <b>SANDBOX</b>; the szlachta's stalled
/// fights): with the switch <c>rush-unpaced</c>, every fighter whose sheet is one of <c>pace.json</c>'s <c>rush_unpaced.templates</c>
/// (the szlachta's <c>sandbox.szlachta</c>) bears the platform's <see cref="RushUnpaced"/>, so the fighting pace
/// (<see cref="FightingPace"/>, 1.8 m/s while a blow swings or is held ready) does not hold its rush. Marked as the sheet says each tick
/// (a sheet given after the body was made is seen at once; one changed away from the list loses the mark). <c>Act/94</c>, after the
/// brains' pace (93), before the bodies move. Without the switch nothing is registered: the world before, digest for digest.
/// Allocates nothing per tick.
/// </summary>
public sealed class RushUnpacedSystem(IReadOnlyList<string> templates) : ISystem
{
    /// <summary>The system's place: <c>Act/94</c>.</summary>
    public const int Order = 94;

    /// <summary>Registers the platform's <see cref="RushUnpaced"/> and <c>Act/94 enhanced.rush-unpaced</c> for <paramref name="templates"/> (nothing for none).</summary>
    public static WorldBuilder Register(WorldBuilder b, IReadOnlyList<string> templates)
    {
        if (templates.Count == 0) return b;
        FightingPace.RegisterUnpaced(b);
        b.System(Phase.Act, Order, "enhanced.rush-unpaced", new RushUnpacedSystem(templates));
        return b;
    }

    public void Run(World world)
    {
        if (!world.TryStore<RushUnpaced>(out var marks) || !world.TryStore<Platform.Control.Combatant>(out var fighters)) return;
        if (!world.TryService<ActorSheets>(out var sheets)) return;
        var ents = fighters.Entities;
        for (int i = 0; i < ents.Length; i++)
        {
            var e = ents[i];
            bool want = sheets.Has(e) && Listed(sheets.Template(e));
            bool has = marks.Has(e);
            if (want && !has) world.Set(e, new RushUnpaced { Kind = 1 });
            else if (!want && has) world.Remove<RushUnpaced>(e);
        }
    }

    bool Listed(string template)
    {
        for (int k = 0; k < templates.Count; k++) if (string.Equals(templates[k], template, StringComparison.Ordinal)) return true;
        return false;
    }
}
