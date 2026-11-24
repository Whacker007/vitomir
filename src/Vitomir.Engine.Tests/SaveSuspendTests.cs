// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Net;
using Vitomir.Engine.Save;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Whether a thing is saved: never without a GUID or with THING_FLAG_SAVE_NEVER, and with
/// THING_FLAG_SAVE_SUSPEND only in single player — a network game's save leaves it out
/// (<c>* != 0</c>). Pinned by the pass of a comparison with 1.1, which found a
/// host's save keeping it.
/// </summary>
public class SaveSuspendTests
{
    [Fact]
    public void A_suspended_thing_is_saved_in_single_player_and_left_out_of_a_network_game_s_save()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var saved = SaveCapture.OfPlace(place).Things.Values.Select(s => s.Guid).ToHashSet();
        var thing = place.Things.First(t => t.Guid != 0 && t.Actor is null && saved.Contains(t.Guid));
        thing.Flags |= WorldThing.Flag.SaveSuspend;

        Assert.Contains(SaveCapture.OfPlace(place).Things.Values, s => s.Guid == thing.Guid);
        NetOrders.Attach(place, NetMode.Server);
        try
        {
            Assert.DoesNotContain(SaveCapture.OfPlace(place).Things.Values, s => s.Guid == thing.Guid);
        }
        finally { NetOrders.Detach(place); }
    }
}
