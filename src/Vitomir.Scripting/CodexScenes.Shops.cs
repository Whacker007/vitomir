// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The shop and treasure natives. The rules are `Vitomir.Engine.Rpg.Shops` and `Rpg.Treasure`; this is
// only the script's way in, and the two callbacks the engine makes back into a shop's script.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    void RegisterShops()
    {
        // `AddBuySell(scriptGuid, file, ownerGuid, descriptionId, flags, buyRate, sellRate,
        // sellFlags)`: the calling script has to be a live one — the native tests it and
        // then never uses it — and the owner a thing. Every shop script calls it on every visit.
        Engine.On("Codex.AddBuySell", (_, a) =>
        {
            string? file = CodexHost.Str(a, 1), desc = CodexHost.Str(a, 3);
            if (location is not { Session: { } session } place || ScriptOf(CodexHost.Int(a, 0)) is null
                || place.Thing(CodexHost.Int(a, 2)) is not { } owner || file is null || desc is null)
                return CodexHost.Box(false);
            Hook(session.Shops);
            return CodexHost.Box(session.Shops.Add(place, file, owner, desc, (uint)CodexHost.Int(a, 4),
                                                   CodexHost.Float(a, 5), CodexHost.Float(a, 6), CodexHost.Int(a, 7)));
        });
        // `ExecuteBuySell(scriptGuid, ownerGuid, shopperGuid, xflags)`: open the pane on
        // the owner's store. The shopper is a multiplayer argument; the trader is whoever is steered.
        Engine.On("Codex.ExecuteBuySell", (_, a) =>
        {
            if (location is not { Session: { } session } place || ScriptOf(CodexHost.Int(a, 0)) is not { } script
                || place.Thing(CodexHost.Int(a, 1)) is not { } owner)
                return CodexHost.Box(false);
            Hook(session.Shops);
            return CodexHost.Box(session.Shops.Execute(place, owner, (uint)CodexHost.Int(a, 3), script.Script));
        });
        // `RefillAllBuySells()`: every sold-out or empty line stocked again.
        // No shipped script calls it.
        Engine.On("Codex.RefillAllBuySells", (_, _) =>
        {
            if (location is { Session: { } session, Treasure: { } table }) session.Shops.Refill(table);
            return null;
        });
        // `BeginBuySell` / `EndBuySell`: they check the script and answer false.
        Engine.On("Codex.BeginBuySell", (_, _) => CodexHost.Box(false));
        Engine.On("Codex.EndBuySell", (_, _) => CodexHost.Box(false));

        // `CodexThing.SpawnTreasure(treasureClass, bForceSuccess)`: one roll, made and put
        // into the world **exactly** where this thing is — no scatter, no drop noise, no deferring to
        // an owner. A class outside 0..255 is 0; so is a roll of nothing. No shipped script calls it.
        Engine.On("CodexThing.SpawnTreasure", (self, a) =>
        {
            int cls = CodexHost.Int(a, 0);
            if (location is not { } place || Of(self) is not { InWorld: true } from || cls is < 0 or > 255
                || place.Treasure?.Roll(cls, CodexHost.Bool(a, 1)) is not { } record
                || Treasure.Make(place, record) is not { } made)
                return CodexHost.Box(0);
            made.Position = from.Position;
            made.Orientation = from.Orientation;
            made.Sector = from.Sector;
            place.Enter(made);
            return CodexHost.Box(made.Guid);
        });
        // `CodexWorld.CreateTreasure` builds its transform the way `CreateThing` does
        // (built in CodexScenes.Natives.cs); it is not built and stays on the log-once
        // default. Nothing shipped calls it.
    }

    /// <summary>The two calls the shop makes back into its script.</summary>
    void Hook(Shops shops)
    {
        // `buysellended(boolean bAborted)`, before the pane goes.
        shops.Ended ??= (store, aborted) =>
        {
            if (store.Script is { } s) Say(NameOf(s), s, "buysellended", "(Z)V", [CodexHost.Box(aborted)]);
        };
        // `buysellquery(boolean, int itemGuid)` before a sale, answering whether it may go
        // ahead. A script without one — every shipped script — says yes.
        shops.Query ??= (store, item) =>
        {
            if (store.Script is not { } s) return true;
            try
            {
                if (!runtime.TryInvoke(s, "buysellquery", "(ZI)Z", [CodexHost.Box(false), CodexHost.Box(item.Guid)], out var r))
                    return true;
                return r is java.lang.Boolean b && b.booleanValue();
            }
            catch (Exception e)
            {
                // As with `cancast`: the exception is cleared and the call answers what it left, 0.
                Engine.Log($"{NameOf(s)}.buysellquery failed: {ScriptException.From(NameOf(s), e).JavaType}");
                return false;
            }
        };
    }
}
