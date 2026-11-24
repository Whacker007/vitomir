// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// A shop opened the way the game opens one: the smithy's own `SMTY_JiriShop` hears Christof's
/// click, waits its half second, greets him, and calls `AddBuySell` then `ExecuteBuySell`; and
/// Done tells it `buysellended`.
/// </summary>
[Collection("scripts")]
public class ShopScriptTests(Scripts s)
{
    [Fact]
    public void Jiri_opens_his_shop_from_his_own_script_and_hears_it_close()
    {
        s.Ready();
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel("P1_SMTY");
        var place = Location.Load(files, "P1_SMTY", sky: false, scene: where?.Entry?.SceneName, player: "Christof");
        var session = new Session { Here = place };
        place.Session = session;
        var scenes = new CodexScenes(s.Runtime!) { Chronicles = set };
        scenes.Enter(place);
        var jiri = place.Things.FirstOrDefault(t => t.Script == "SMTY_JiriShop");
        Assert.SkipWhen(jiri is null, "no SMTY_JiriShop in the smithy's scene");
        // Past his first conversation, so a click goes straight to the shop (the script's `else`).
        scenes.Chronicle!.SetFlag(120);                     // PragueChronicle.JIRI_TALKEDONCE
        bool ended = false;
        session.Shops.Opened = _ => true;

        Assert.True(scenes.Clicked(jiri!, place.Player!.Thing));
        for (int i = 0; i < 30 && session.Shops.Active is null; i++) scenes.Step(0.05f);

        var store = session.Shops.Active;
        Assert.NotNull(store);
        Assert.Equal("PragueSmithy.nbs", store.File);
        Assert.Equal(jiri!.Guid, store.OwnerGuid);
        Assert.Equal(0x53, store.SellMask);                  // MUNDANE, TREASURE, ARMOR, WEAPON
        Assert.Equal(Shops.WantFeedback, store.Flags);       // ExecuteBuySell's, over AddBuySell's 0
        Assert.Same(place.Player!.Thing, store.Trader);
        var before = session.Shops.Ended;
        session.Shops.Ended = (st, aborted) => { ended = true; before?.Invoke(st, aborted); };

        session.Shops.End();
        Assert.True(ended);
        Assert.Null(session.Shops.Active);
        scenes.Leave();
        files.Dispose();
    }
}
