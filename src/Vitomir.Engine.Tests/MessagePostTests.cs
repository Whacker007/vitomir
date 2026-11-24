// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// What the engine itself says in the message box, beyond what the scripts print: the
/// posts 1.1 makes to the message box that Vitomir had not been making.
/// </summary>
public class MessagePostTests
{
    static (Location Place, Session Session, WorldThing Christof) Monastery(Vfs.GameFiles files)
    {
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var session = new Session { Here = place };
        session.Messages.Text = new Strings(files);
        place.Session = session;
        var christof = place.Player!.Thing;
        christof.Actor!.Flags |= Actor.Flag.Party | Actor.Flag.HumanityTrack;
        christof.Actor.Type = ActorType.Vampire;
        christof.Actor.BaseStat[(int)ActorStat.Humanity] = christof.Actor.Stat[(int)ActorStat.Humanity] = 70;
        return (place, session, christof);
    }

    /// <summary>
    /// **A kill that costs Humanity says so**: "%P lost some Humanity!" in the box's own
    /// colour, when the killer is the one being steered — the 1.1 line after a monk dies at
    /// Christof's hand. A kill by somebody not steered takes the Humanity just the same and says
    /// nothing.
    /// </summary>
    [Fact]
    public void Killing_a_monk_says_Christof_lost_some_Humanity()
    {
        using var files = Install.Open();
        var (place, session, christof) = Monastery(files);
        var monks = place.Things.Where(t => t.Template.StartsWith("civ", StringComparison.OrdinalIgnoreCase)
                                            && t.Actor is { } a && a.Has(Actor.Flag.HumanityLoss)).ToList();
        Assert.True(monks.Count >= 2);

        Damage.Kill(monks[0], christof, HitKind.Melee, DamageType.Lethal, place.Milliseconds);
        Assert.Equal(70 - Damage.HumanityPerKill, christof.Actor!.Stat[(int)ActorStat.Humanity]);
        Assert.Equal(new Message("Christof lost some Humanity!", MessageLog.Own), session.Messages.Lines[^1]);

        // Steering somebody else, the kill is charged in silence.
        int said = session.Messages.Lines.Count;
        place.Minds!.Steered = place.Things.First(t => t.Mind is not null && !ReferenceEquals(t, christof) && !monks.Contains(t));
        Damage.Kill(monks[1], christof, HitKind.Melee, DamageType.Lethal, place.Milliseconds);
        Assert.Equal(70 - 2 * Damage.HumanityPerKill, christof.Actor.Stat[(int)ActorStat.Humanity]);
        Assert.Equal(said, session.Messages.Lines.Count);
    }
}
