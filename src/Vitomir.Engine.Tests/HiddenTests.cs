// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// `THING_AF_STARTHIDDEN`, and the thing it is easy to get wrong about it: it is a **starting**
/// state. A corpse lies among the scenery until something rouses it, and the flag is what keeps
/// the pointer off it while it does — so a corpse that never clears the flag can attack you and
/// cannot be attacked back.
/// </summary>
public class HiddenTests
{
    static (Location Where, Minds Minds, GameFiles Files) Enter(string level)
    {
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel(level);
        var loc = Location.Load(files, level, sky: false, scene: where?.Entry?.SceneName, player: "Christof");
        return (loc, loc.Awaken(new AiClasses(files)), files);
    }

    [Fact]
    public void The_monastery_s_awakening_corpses_start_hidden()
    {
        // From the template, not from us: `Corpse_awaken` carries ACTOR_FLAGS 0x9110040, which
        // includes START HIDDEN — and P1_MON1 places several of them.
        var (where, _, files) = Enter("P1_MON1");
        using var _f = files;
        var corpses = where.Things.Where(t => t.Actor is { } a && a.Has(Actor.Flag.StartHidden)).ToArray();
        Assert.NotEmpty(corpses);
        Assert.Contains(corpses, t => t.Template.Contains("corpse", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_hidden_corpse_is_lying_down_from_the_moment_its_mind_is_made()
    {
        // The end of the mind set-up: START HIDDEN plays mode 257, `hidden`, on the
        // action channel at rate 30 — in corpseMinion.nms that is ZombuLieDown.nad, endpause, so it
        // holds. In play they stood until activated and then snapped down to get up again.
        var (where, minds, files) = Enter("P1_MON1");
        using var _f = files;
        var corpses = minds.All.Select(m => m.Thing)
                           .Where(t => t.Actor is { } a && a.Has(Actor.Flag.StartHidden)).ToArray();
        Assert.NotEmpty(corpses);
        foreach (var corpse in corpses)
        {
            Assert.Null(corpse.Waiting);
            Assert.NotNull(corpse.Animation);
            Assert.Equal(Minds.MotionHidden, corpse.Animation!.Action?.Mode.Index);
        }
        // And it is still lying there long after the nine frames have run: endpause holds.
        var one = corpses[0];
        for (int i = 0; i < 60; i++) one.Animation!.Advance(1f / 30f);
        Assert.Equal(Minds.MotionHidden, one.Animation!.Action?.Mode.Index);

        // Rousing it replaces the lying with the getting up.
        minds.Aggravate(minds.All.First(m => ReferenceEquals(m.Thing, one)), where.Player!.Thing);
        Assert.Equal(Minds.MotionAwaken, one.Animation!.Action?.Mode.Index);
    }

    [Fact]
    public void A_hidden_character_with_no_animation_yet_is_left_the_request()
    {
        var (where, _, files) = Enter("P1_MON1");
        using var _f = files;
        var corpse = where.Things.First(t => t.Actor is { } a && a.Has(Actor.Flag.StartHidden));
        corpse.Animation = null;
        Minds.Hide(corpse, where.Motions);
        Assert.Equal((Minds.MotionHidden, Minds.AwakenRate), corpse.Waiting);
    }

    [Fact]
    public void A_hidden_character_cannot_be_pointed_at_and_therefore_cannot_be_attacked()
    {
        // The half that is right, and the reason the other half mattered: the pointer refuses a
        // hidden character, and since the click acts on what the pointer holds, so does
        // the click.
        var (where, _, files) = Enter("P1_MON1");
        using var _f = files;
        var player = where.Player!.Thing;
        var hidden = where.Things.First(t => t.Actor is { } a && a.Has(Actor.Flag.StartHidden));
        Assert.False(Highlight.Offers(player, hidden));
    }

    [Fact]
    public void Rousing_one_clears_the_flag_and_it_becomes_a_target()
    {
        // Inside the react: a character that was START HIDDEN stops being hidden the
        // moment it takes against somebody. In play the awakened
        // zombies could not be attacked, yet they attacked: which means they had reacted, and the flag had
        // stayed on.
        var (where, minds, files) = Enter("P1_MON1");
        using var _f = files;
        var player = where.Player!.Thing;
        var mind = minds.All.First(m => m.Thing.Actor is { } a && a.Has(Actor.Flag.StartHidden));
        var corpse = mind.Thing;

        Assert.False(Highlight.Offers(player, corpse));
        minds.Aggravate(mind, player);
        Assert.False(corpse.Actor!.Has(Actor.Flag.StartHidden));
        Assert.True(Highlight.Offers(player, corpse), "a roused corpse is still unpointable");
        Assert.NotEqual(CursorVerb.None, Cursor.Verb(player, corpse));
    }

    [Fact]
    public void And_it_stands_still_for_at_least_its_own_wakeup_delay()
    {
        // The engine takes the longer of the AI file's wakeupDelay and MOTION_AWAKEN's own length,
        // so the corpse cannot lunge before it has finished getting up.
        var (where, minds, files) = Enter("P1_MON1");
        using var _f = files;
        var mind = minds.All.First(m => m.Thing.Actor is { } a && a.Has(Actor.Flag.StartHidden));
        minds.Aggravate(mind, where.Player!.Thing);
        Assert.True(mind.BusyUntil >= mind.Class.WakeupDelay,
                    $"busy for {mind.BusyUntil} but the AI file asks for {mind.Class.WakeupDelay}");
    }
}
