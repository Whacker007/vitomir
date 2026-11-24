// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **`dropcash` and `say` act on the party's selected member**
/// (the steered character, except while a possession lasts), not on its first member, which is what
/// Vitomir used until a comparison with 1.1. With Wilhem steered the bag lands beside Wilhem.
/// </summary>
public class ConsoleSelectedTests
{
    [Fact]
    public void Dropcash_puts_the_bag_beside_the_steered_member_not_the_first()
    {
        var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find) { State = FrontEnd.StateGame };
        // The University with Christof and Wilhem both in the party (as `DestroyTalkSteerTests` has it).
        var place = Location.Load(files, "P1_UNIV", things: true, sky: false, scene: "UNIV_7_7", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var christofMind = minds.All.First(m => ReferenceEquals(m.Thing, place.Player!.Thing));
        var wilhemMind = minds.All.First(m => !ReferenceEquals(m, christofMind) && m.Thing.Type == ThingType.Player);
        foreach (var m in new[] { christofMind, wilhemMind })
        {
            m.Thing.Actor!.Flags |= Actor.Flag.Party;
            m.Enter(AiMode.Party);
        }
        var console = new DevConsole(menus);
        menus.DevConsole = console;
        ConsoleCommands.Register(console, new ConsoleCommands.Game
        {
            Session = new Session { Here = place }, Here = () => place, Menus = menus, Text = text,
        });

        var party = place.PartyMembers().ToList();
        Assert.SkipWhen(party.Count < 2, "no second party member in this scene");
        var first = party[0];
        var other = party[1];
        // Far enough apart that "beside" tells them apart.
        other.Position = first.Position + new Vector3(600f, 0f, 0f);
        Assert.True(place.Steer(other));
        Assert.Same(other, place.Selected);

        console.Run("dropcash 77");
        var bag = place.Things.Where(t => t.InWorld && t.Item is { } i && t.Template.Equals("GoldBag", StringComparison.OrdinalIgnoreCase))
                              .OrderBy(t => t.Index).LastOrDefault();
        Assert.NotNull(bag);
        Assert.True(Vector3.Distance(bag!.Position, other.Position) < Vector3.Distance(bag.Position, first.Position),
                    $"the bag at {bag.Position} is nearer {first.Template} than the steered {other.Template}");
    }
}
