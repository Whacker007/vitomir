// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The special-move throw**: a strike with a `THROW` attack against a character can become
/// a grab on a roll of the weapon's chance — the two face each other, the
/// victim is put the throw distance in front of the thrower and restrained, and both play their
/// throw motions, busy for exactly their length; the victim's tick, the thrower's,
/// the `Throwdeath` tag and a thrower's death dropping its victim
/// finish it. Earlier, Vitomir had the two action numbers and the tags read, and threw nobody.
/// The fights are real ones: a werewolf made out of a character in the Old Town, throwing Christof.
/// </summary>
public class SpecialMoveTests
{
    static (Location Place, Simulation Sim, WorldThing Christof, WorldThing Wolf) Open(GameFiles files)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1", player: "Christof");
        var minds = place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), new DisciplineDefinitions(files));
        var christof = place.Steered!;
        var wolf = minds.All.First(m => !ReferenceEquals(m.Thing, christof) && m.Thing.Actor is not null).Thing;
        wolf.Actor!.Flags |= Actor.Flag.AiPaused;
        // The werewolf's own body and motion set, which is what has `throw2` in it.
        wolf.Model = "Werewolf.nod";
        wolf.MotionSet = "werewolf.nms";
        place.Reshape(wolf);
        wolf.Actor.Weapon = Weapon.From("Claw_werewolfBig", place.Templates!.Resolve("Claw_werewolfBig"));
        // Near him and looking the other way: the grab turns them both.
        place.Move(wolf, christof.Position + new Vector3(90f, 0f, 0f));
        wolf.Orientation = new Vector3(0f, 0f, 0f);
        christof.Actor!.MaxHealth = christof.Actor.Health = 10_000f;
        return (place, new Simulation(place), christof, wolf);
    }

    static float Yaw(WorldThing from, WorldThing to) =>
        Player.Wrap(MathF.Atan2(-(to.Position.X - from.Position.X), to.Position.Y - from.Position.Y) * (180f / MathF.PI));

    static void Run(Simulation sim, int ms)
    {
        for (int t = 0; t < ms; t += 33) sim.Step(1f / 30f);
    }

    /// <summary>
    /// The eight templates and their numbers, read as the engine reads them (the reader's cases
    /// 0xf5, 0xf7, 0xf8): the chance clamped to 1, and the flag on whichever attack the template
    /// put it on — the second for the Vozhd, the advanced war ghoul and the lesser werewolf.
    /// </summary>
    [Fact]
    public void Eight_weapons_carry_the_throw()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var found = new Dictionary<string, (int Attack, int Type, float Distance, float Chance)>();
        foreach (var name in templates.Names)
        {
            if (Weapon.From(name, templates.Resolve(name)) is not { } w) continue;
            for (int i = 0; i < w.AttackCount; i++)
                if (w.Attacks[i].Is(WeaponFlag.CanSpecialMove)) found[name] = (i, w.ThrowType, w.ThrowDistance, w.ThrowChance);
        }
        Assert.Equal(8, found.Count);
        Assert.Equal((1, 0, 200f, 1f), found["Claw_vozhd"]);
        Assert.Equal(0, found["Claw_vozhdLesser"].Attack);
        Assert.Equal(0.25f, found["Claw_vozhdLesser"].Chance);
        Assert.Equal(1, found["Claw_warGhoulADV"].Attack);
        Assert.Equal(1, found["Claw_werewolf"].Attack);
        Assert.Equal(0.3f, found["Claw_werewolf"].Chance, 3);
        Assert.Equal(1f, found["Claw_werewolfBig"].Chance);             // 5 in the template
        Assert.Equal(0, found["Punch_Golem"].Attack);
        Assert.Equal(3, found["bite_Zulo"].Type);                        // `throw4` / `thrown4`
        Assert.Equal(2, found["bite_ghoulAlligator"].Type);
    }

    /// <summary>
    /// The grab: both turned to each other, the victim put the throw distance in front
    /// of the thrower (121.26 for the big werewolf) and restrained, 17 and 18, and each busy for its
    /// own motion — `WerewolfToss2` and `ChrisWarGhoulDamage`, 99 frames each at thirty a second.
    /// No swing: the thrower is not in the attack and not busy for the claw's 1,333 ms reload.
    /// </summary>
    [Fact]
    public void A_werewolfs_claw_grabs_Christof_and_throws_him()
    {
        using var files = Install.Open();
        var (place, _, christof, wolf) = Open(files);
        int now = place.Milliseconds;
        var claw = wolf.Actor!.Weapon!;

        Actions.Strike(wolf, christof, claw, now, place.Motions);

        Assert.Equal(ActionMode.Throwing, wolf.Actor.Action);
        Assert.Equal(ActionMode.Thrown, christof.Actor!.Action);
        Assert.Equal(now + 3300, wolf.Actor.BusyUntil);
        Assert.Equal(now + 3300, christof.Actor.BusyUntil);
        Assert.True(christof.Actor.Has2(Actor.Flag2.Restrained));
        Assert.Equal(SpecialMove.ThrowMode + 1, wolf.Animation!.Mode!.Index);        // throw2
        Assert.Equal(SpecialMove.ThrownMode + 1, christof.Animation!.Mode!.Index);   // thrown2

        // Put, not walked: the thrower's position plus the distance along its facing, level.
        var gap = christof.Position - wolf.Position;
        Assert.Equal(claw.ThrowDistance, new Vector2(gap.X, gap.Y).Length(), 1);
        Assert.Equal(Yaw(wolf, christof), wolf.Orientation.Z, 1);
        Assert.Equal(Yaw(christof, wolf), christof.Orientation.Z, 1);
    }

    /// <summary>
    /// The rest of it, run: the toss's own `Strike` tag (frame 16) lands a blow on Christof — a
    /// restrained target, so helpless; the `Throwdeath` tag (frame 54) ends his throw and lets him go
    /// while he is still busy getting up; and at 3.3 s both ticks put them back to nothing.
    /// </summary>
    [Fact]
    public void The_throw_strikes_lands_and_lets_go()
    {
        using var files = Install.Open();
        var (place, sim, christof, wolf) = Open(files);
        Actions.Strike(wolf, christof, wolf.Actor!.Weapon!, place.Milliseconds, place.Motions);
        int start = place.Milliseconds;

        Run(sim, 1000);
        Assert.Contains(sim.Blows, b => ReferenceEquals(b.Attacker, wolf) && ReferenceEquals(b.Target, christof) && b.Blow.Hit);
        Assert.Equal(ActionMode.Thrown, christof.Actor!.Action);

        Run(sim, 1000);                                                   // past frame 54, 1.8 s
        Assert.Equal(ActionMode.None, christof.Actor.Action);
        Assert.False(christof.Actor.Has2(Actor.Flag2.Restrained));
        Assert.True(christof.Actor.BusyUntil > place.Milliseconds);
        Assert.Equal(ActionMode.Throwing, wolf.Actor.Action);

        Run(sim, start + 3300 + 100 - place.Milliseconds);
        Assert.Equal(ActionMode.None, wolf.Actor.Action);
        Assert.Null(wolf.Actor.Target);
    }

    /// <summary>
    /// A victim the throw kills plays no death: the thrown motion plays on and the
    /// `Throwdeath` tag holds it where the body came down, so it lies as it landed rather than
    /// standing back up when the motion ends.
    /// </summary>
    [Fact]
    public void A_victim_killed_in_the_throw_lies_where_it_landed()
    {
        using var files = Install.Open();
        var (place, sim, christof, wolf) = Open(files);
        var victim = place.Minds!.All.Select(m => m.Thing).First(t => !ReferenceEquals(t, christof) && !ReferenceEquals(t, wolf)
            && t.Actor is not null && t.Animation is { } a && place.Motions!.Find(a.Set, SpecialMove.ThrownMode + 1) is not null);
        victim.Actor!.Flags |= Actor.Flag.AiPaused;
        victim.Actor.Type = ActorType.Human;
        victim.Actor.Health = 1f;
        place.Move(victim, wolf.Position + new Vector3(0f, 60f, 0f));

        Actions.Strike(wolf, victim, wolf.Actor!.Weapon!, place.Milliseconds, place.Motions);
        Assert.Equal(ActionMode.Thrown, victim.Actor.Action);
        Run(sim, 4000);

        Assert.True(victim.Actor.Dead);
        var channel = victim.Animation!.Action;
        Assert.NotNull(channel);
        Assert.True(channel!.Held);
        float landed = HeldFire.TagFrame(channel.Player.Animation, SpecialMove.ThrowdeathTag);
        Assert.InRange(channel.Player.Frame, landed, landed + 1.5f);
    }

    /// <summary>
    /// A thrower killed in the throw drops its victim: the victim's thrown
    /// motion is stopped, it is cancelled forced and `ef_restrain` comes off — back on its feet where
    /// it was put, at once.
    /// </summary>
    [Fact]
    public void A_thrower_killed_in_the_throw_drops_its_victim()
    {
        using var files = Install.Open();
        var (place, _, christof, wolf) = Open(files);
        Actions.Strike(wolf, christof, wolf.Actor!.Weapon!, place.Milliseconds, place.Motions);
        Assert.Equal(ActionMode.Thrown, christof.Actor!.Action);

        Damage.Fall(wolf, ActionMode.Throwing, 50f, place.Motions);

        Assert.Equal(ActionMode.None, christof.Actor.Action);
        Assert.False(christof.Actor.Has2(Actor.Flag2.Restrained));
        Assert.Null(christof.Animation!.Action);
    }

    /// <summary>
    /// What does not throw: a roll at or over the chance (the one draw), an attack
    /// without the flag (the lesser werewolf's first), and a target that is not a character.
    /// Each leaves the strike an ordinary swing.
    /// </summary>
    [Fact]
    public void Only_a_THROW_attack_that_rolls_under_its_chance_throws()
    {
        using var files = Install.Open();
        var (place, _, christof, wolf) = Open(files);
        var werewolf = Weapon.From("Claw_werewolf", place.Templates!.Resolve("Claw_werewolf"))!;
        int now = place.Milliseconds;

        // The second attack carries the flag and a chance of 0.3: a seed rolling over it does not throw.
        werewolf.CurrentAttack = 1;
        int over = Enumerable.Range(1, 100_000).First(s => new Rng(s).Unit() >= 0.3f);
        int under = Enumerable.Range(1, 100_000).First(s => new Rng(s).Unit() < 0.3f);
        Assert.False(SpecialMove.Try(wolf, christof, werewolf, now, new Rng(over)));
        Assert.Equal(ActionMode.None, christof.Actor!.Action);
        // The first has no flag at all, whatever the roll.
        werewolf.CurrentAttack = 0;
        Assert.False(SpecialMove.Try(wolf, christof, werewolf, now, new Rng(under)));
        // Not a character: nothing to throw.
        var box = new WorldThing { Index = -1, Guid = 5, Scene = new NsdThing(), Template = "box", Type = ThingType.Container };
        werewolf.CurrentAttack = 1;
        Assert.False(SpecialMove.Try(wolf, box, werewolf, now, new Rng(under)));
        // And the roll under it does.
        Assert.True(SpecialMove.Try(wolf, christof, werewolf, now, new Rng(under)));
        Assert.Equal(ActionMode.Thrown, christof.Actor.Action);
    }
}
