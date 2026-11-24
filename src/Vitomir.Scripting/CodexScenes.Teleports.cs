// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The two teleporter natives: Walk the Abyss's `CreateTeleporter` and the portal's `ActivateTeleporter`.
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>
    /// `CodexSequence.CreateTeleporter` and `ActivateTeleporter`,
    /// thin doors onto the teleporters (<see cref="Teleports"/>, the session's table).
    /// </summary>
    void RegisterTeleports()
    {
        // `CreateTeleporter(creatorGuid, inTemplate, outTemplate, expireTime, flags)`: the
        // creator must be a character (type 0 or 7), and both template names are copied to
        // 32 characters and must name templates — else false. Then the teleporter is made with the
        // arguments in its own order (creator, in, out, **flags, expire** — the native swaps the last
        // two), and **true whatever it answered**. Walk the Abyss is the only caller:
        // `("havenPortal", "havenPortal", 0, 0)`.
        Engine.On("CodexSequence.CreateTeleporter", (_, a) =>
        {
            if (location?.Thing(CodexHost.Int(a, 0)) is not { } creator
                || creator.Type is not (ThingType.Actor or ThingType.Player)) return CodexHost.Box(false);
            string inTemplate = Clip(CodexHost.Str(a, 1)), outTemplate = Clip(CodexHost.Str(a, 2));
            if (location.Templates is not { } templates || !templates.Has(inTemplate) || !templates.Has(outTemplate))
                return CodexHost.Box(false);
            bool made = location.Session?.Teleports.Create(creator, inTemplate, outTemplate,
                                                          (uint)CodexHost.Int(a, 4), CodexHost.Int(a, 3)) ?? false;
            if (!made) Diag.For("Vitomir.Scripting").LogInformation("CreateTeleporter: no pair for {Who}", creator.Template);
            return CodexHost.Box(true);
        });

        // `ActivateTeleporter(teleporterGuid, playerGuid)`: the player must be a
        // `THING_TYPE_PLAYER` (7) — else false — and then the teleporter is activated, and true. The `Teleporter` script
        // on both ends of a pair calls it from `clicked`.
        Engine.On("CodexSequence.ActivateTeleporter", (_, a) =>
        {
            if (location?.Thing(CodexHost.Int(a, 1)) is not { Type: ThingType.Player } player) return CodexHost.Box(false);
            location.Session?.Teleports.Activate(CodexHost.Int(a, 0), player);
            return CodexHost.Box(true);
        });
    }

    /// <summary>The native's 32-byte copy of a template name (`strncpy(…, 0x20)` then a terminator at 31).</summary>
    static string Clip(string? name) => name is null ? "" : name.Length > 31 ? name[..31] : name;
}
