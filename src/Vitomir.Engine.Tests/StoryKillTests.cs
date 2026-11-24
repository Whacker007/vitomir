// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The Storyteller actor pane's Kill takes the actor's
/// <c>INVULNERABLE</c> flag off before it kills, whose own first test would
/// otherwise refuse. Found in a comparison with 1.1; before, a character a script had made invulnerable
/// could not be killed from the pane.
/// </summary>
public class StoryKillTests
{
    [Fact]
    public void The_Storytellers_kill_takes_an_invulnerable_actors_protection_off_first()
    {
        using var files = Install.Open();
        var inn = ChronicleSet.Load(files)["MP_CurseDarkness"]!;
        var settings = GameSettings.For(inn, "Test", GameFlags.Moderated);
        var place = Location.Load(files, settings.Level, scene: settings.Scene, player: "MP_Serena", entrance: settings.Entrance);
        place.Awaken(new Ai.AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        using var server = new GameServer(settings, new NetPeer(0), new NetCharacter { Name = "Anna", Template = "MP_Serena" });
        server.Enter(place, place.Player?.Thing);
        var st = new StoryPowers(server, place, new Strings(files));

        st.Create("Rat");
        var rat = st.Target!;
        rat.Actor!.Flags |= Actor.Flag.Invulnerable;
        Assert.False(Damage.Kill(rat, null, HitKind.Script, DamageType.Normal, 0));   // the death alone refuses
        st.Kill(rat);
        Assert.True(rat.Actor.Dead);
        Assert.False(rat.Actor.Has(Actor.Flag.Invulnerable));
    }
}
