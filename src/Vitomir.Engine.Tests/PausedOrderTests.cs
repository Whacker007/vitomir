// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **"This character is currently busy, can't issue new orders now."**. The one line an
/// order writes to the message box itself: the attack on a thing, on a point
/// and the cast refuse a busy, dead or held character, and when that character is the one
/// being steered and the game is paused they say so first (<see cref="PausedOrders"/>). Unpaused, or
/// for anyone else, the refusal is silent — which is why an earlier list had it as unbuilt.
/// </summary>
public class PausedOrderTests
{
    static (Location Place, Session Session, WorldThing Christof, WorldThing Monk) Monastery(Vfs.GameFiles files, bool paused)
    {
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files));
        var session = new Session { Here = place, Paused = () => paused };
        session.Messages.Text = new Strings(files);
        place.Session = session;
        var christof = place.Player!.Thing;
        var monk = place.Things.First(t => t.Template.StartsWith("civ", StringComparison.OrdinalIgnoreCase) && t.Actor is not null);
        christof.Actor!.BusyUntil = place.Milliseconds + 5000;      // mid-swing when the Pause key went down
        return (place, session, christof, monk);
    }

    const string Busy = "This character is currently busy, can't issue new orders now.";

    /// <summary>Paused, a busy Christof's click on a monk, on the floor and with a discipline each say it once, in the box's own grey.</summary>
    [Fact]
    public void Paused_a_busy_Christof_is_told_he_cannot_take_orders()
    {
        using var files = Install.Open();
        var (place, session, christof, monk) = Monastery(files, paused: true);
        int now = place.Milliseconds;
        Assert.Equal(OrderResult.Busy, Actions.Attack(christof, monk, christof.Actor!.Weapon, 0, now));
        Assert.Equal(new Message(Busy, MessageLog.Own), session.Messages.Lines[^1]);
        Assert.Equal(OrderResult.Busy, ClickOrders.AttackPoint(christof, monk.Position, 0, now));
        Assert.Equal(OrderResult.Busy, Cast.Order(christof, monk, null, null, InputModifier.None));
        Assert.Equal(3, session.Messages.Lines.Count(l => l.Text == Busy));
    }

    /// <summary>Unpaused the same refusals are silent; paused, a character not being steered is refused silently too.</summary>
    [Fact]
    public void Unpaused_or_for_somebody_else_the_refusal_is_silent()
    {
        using var files = Install.Open();
        var (place, session, christof, monk) = Monastery(files, paused: false);
        Assert.Equal(OrderResult.Busy, Actions.Attack(christof, monk, christof.Actor!.Weapon, 0, place.Milliseconds));
        Assert.Equal(OrderResult.Busy, Cast.Order(christof, monk, null, null, InputModifier.None));
        Assert.Empty(session.Messages.Lines);

        session.Paused = () => true;
        monk.Actor!.BusyUntil = place.Milliseconds + 5000;
        Assert.Equal(OrderResult.Busy, Actions.Attack(monk, christof, monk.Actor.Weapon, 0, place.Milliseconds));
        Assert.Empty(session.Messages.Lines);
    }
}
