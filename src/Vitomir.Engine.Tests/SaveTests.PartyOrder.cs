// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Save;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

public partial class SaveTests
{
    /// <summary>
    /// The party's order after a drag along the bar (ours, <c>partyorder</c>) comes back from a save: the party is
    /// written in its order and read back in it, so nothing new is kept for it.
    /// </summary>
    [Fact]
    public void A_party_put_in_a_new_order_loads_in_that_order()
    {
        using var files = Install.Open();
        var g = Start(files, awaken: false);
        var place = g.Place;
        var lead = place.Player!.Thing;
        lead.Actor!.Flags |= Actor.Flag.Party;
        string[] companions = ["Wilhem", "Serena", "Erik"];
        for (int i = 0; i < companions.Length; i++)
        {
            var c = place.Place(companions[i], lead.Position + new System.Numerics.Vector3(96f * (i + 1), 0f, 0f), lead.Orientation, lead.Sector)!;
            c.Actor!.Flags |= Actor.Flag.Party;
            c.Actor.Flags2 |= Actor.Flag2.InFormation;
        }
        place.Awaken(new AiClasses(files), 1, g.Effects, g.Powers);
        var p = place.PartyMembers().ToArray();
        Assert.Equal(4, p.Length);
        Assert.True(place.ReorderParty([p[0], p[3], p[1], p[2]]));
        int[] names = [.. place.PartyMembers().Select(t => t.Guid)];

        var save = SaveCapture.Take(place, g.Session, now: new DateTime(2026, 11, 20, 12, 0, 0));
        Assert.Equal(names, save.Party.Members.Select(m => m.Guid));
        var loaded = Load(files, save);
        Assert.Equal(names, loaded.Place.PartyMembers().Select(t => t.Guid));
        Assert.Equal(4, names.Distinct().Count());
    }
}
