// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Rpg;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// The game's clock. Everything that moves on its own moves here: the scripts' timers, the light
/// styles, the animating materials, the AI, every character's animation, and the blows their
/// animations call for.
///
/// Drawing is not part of it. A renderer reads what the simulation has arrived at and puts it on
/// the screen; it never advances anything, which is what lets a fight be run with no window open.
/// The one exception is the sky, because the original only advances the sky's own animation while
/// the sky is on screen, and whether it is on screen is a fact about the frame — so that stays
/// with whoever is drawing.
/// </summary>
public sealed partial class Simulation(Location location)
{
    /// <summary>The place being simulated.</summary>
    public Location Location { get; } = location;

    /// <summary>
    /// The scene's scripts. Defaults to none; the game installs the real script layer, which is
    /// what switches light styles, sets material frames and runs the scene's own timers.
    /// </summary>
    public ISceneScripts Scripts { get; set; } = NoScripts.Instance;

    /// <summary>
    /// The game's camera, or null for a run with none. It is ticked **after the things**: the
    /// engine's world tick ticks every thing of the place and only then the camera,
    /// so the camera frames where the steered character stands this frame.
    /// Earlier, Vitomir ticked it first, a frame behind.
    /// </summary>
    public WorldCamera? Camera { get; set; }

    /// <summary>
    /// The game's one running conversation, ticked after the script timers. Kept on the
    /// place (<see cref="Location.Talk"/>), where the destroy reaches it.
    /// </summary>
    public Conversations? Talk { get => Location.Talk; set => Location.Talk = value; }

    /// <summary>What the combat rolls come out of: the launch's stream in a game, carried on across places and loads (<see cref="Ai.Chance"/>).</summary>
    public Rng Random { get; set; } = Ai.Chance.Game?.Combat ?? new();

    /// <summary>Time since the location was entered: what sliding textures and light styles run on.</summary>
    public float Seconds { get; private set; }

    /// <summary>
    /// The click-to-move ring. It lives here rather than on the renderer because the engine clears
    /// it from the walker and the order layer rather than from the draw — see
    /// <see cref="WalkTarget.Clear"/> for the three places.
    /// </summary>
    public WalkTarget WalkTarget { get; } = new();

    bool wasWalking;

    /// <summary>The same clock in milliseconds, which is the unit every action deadline is in.</summary>
    public int Milliseconds { get; private set; }

    /// <summary>Every blow that has landed, newest last: what a headless fight is read out of.</summary>
    public List<Landed> Blows { get; } = [];

    /// <summary>A blow that came out of an animation's own keyframe tag.</summary>
    public readonly record struct Landed(int AtMs, WorldThing Attacker, WorldThing Target, Blow Blow, Harm Harm);

    /// <summary>
    /// Every shot loosed, newest last: a ranged weapon's tag fires a projectile, and
    /// whether it lands is decided later by what it meets. A shot that lands is also a blow in
    /// <see cref="Blows"/>, credited to its owner.
    /// </summary>
    public List<Loosed> Shots { get; } = [];

    /// <summary>
    /// A shot a weapon loosed: who, at whom, the projectile (gone again by now for a hitscan), where
    /// it left from and how fast it set off.
    /// </summary>
    public readonly record struct Loosed(int AtMs, WorldThing Attacker, WorldThing? Target, WorldThing Projectile,
                                         System.Numerics.Vector3 From, System.Numerics.Vector3 Velocity);

    /// <summary>
    /// A shot reaching somebody: the blow for the tally and the journal, as a swing's is. The
    /// margin kept is the accuracy roll's, which in 1.1 only decides a critical.
    /// </summary>
    void ShotLanded(Rpg.Projectiles.Struck s)
    {
        var by = s.Owner ?? s.Shot;
        var blow = new Blow(true, s.Margin, s.Damage, s.Critical ? Combat.MultiplierStep : 0f, s.Critical);
        Blows.Add(new Landed(Milliseconds, by, s.Victim, blow, s.Harm));
        Diagnostics.Journal.Current?.Write(Milliseconds, Diagnostics.JournalKind.Blow,
            ("by", Name(by)), ("at", Name(s.Victim)),
            ("weapon", s.Shot.Template), ("ranged", true),
            ("hit", true), ("crit", s.Critical), ("margin", s.Margin),
            ("dmg", s.Damage), ("taken", s.Harm.Taken), ("soaked", s.Damage - s.Harm.Taken),
            ("killed", s.Harm.Killed), ("refused", s.Harm.Refused),
            ("gap", Bodies.Gap(by, s.Victim)),
            ("hpLeft", (object?)s.Victim.Actor?.Health));
    }

    readonly List<NadTag> crossed = [];

    /// <summary>
    /// Carries the game's clock into a new place. The engine has one clock and it
    /// never goes back; every deadline a character carries — an effect's end, a busy timer, a
    /// discipline's recast — is a time on it. A simulation is made per place here, so on arrival it
    /// is started where the last one stopped, and the place's AI clock is put to the same time.
    /// Without it a character who walked through an exit carried deadlines from a clock the new
    /// place had never heard of, and the journal's time went back to zero at every door.
    /// </summary>
    public void Resume(float seconds)
    {
        Seconds = seconds;
        Milliseconds = (int)(Seconds * 1000f);
        Location.Milliseconds = Milliseconds;
        Location.Minds?.Resume(Milliseconds);
    }

    /// <summary>
    /// One tick of the world, in 1.1's order. The frame
    /// runs the world tick and then the draw; the world tick ticks
    /// every thing of the place and only then the camera, the
    /// dynamic lights' ramps, the script timers, the conversation
    /// and the messages. The draw's own clocks — the light styles, the lights' intensities
    /// and blobs, the decals, the emitters, the materials — come after all of that, and the frame's
    /// requests (a film) last.
    ///
    /// The things are ticked as 1.1 ticks them: the world's list newest first, each thing's whole
    /// tick — its move, its think, its effects and action, its animation, whose keyframe tags are
    /// what land a blow — before the next thing's (`World/ThingTick.cs`).
    /// </summary>
    public void Step(float seconds)
    {
        Seconds += seconds;
        Milliseconds = (int)(Seconds * 1000f);
        // The place keeps the clock too, so anything that happens to a thing — a spawn, a drop, an
        // expiry — can date itself without being handed it.
        Location.Milliseconds = Milliseconds;
        // A streamed cell level's loaded cells, taken in at the top of the frame (ours, Phase 3).
        // And its exits, the party's area and the area's sound (ours; CellLevel.Play.cs).
        var cells = Levels.Cells.CellLevel.Of(Location.Level);
        cells?.Step(Location, seconds);

        Scripts.StepFilms(seconds);                             // before the world tick, as 1.1's frame does
        StepThings(seconds);
        cells?.Ease.Step(Location.Things, seconds);             // ours: a walker's shown height over a cell level's steps (CellStepEase)

        Camera?.Tick(seconds, Milliseconds);
        Location.StepLightRamps(Milliseconds);
        Scripts.StepTimers(seconds);
        Talk?.Tick(Milliseconds);
        Location.Session?.Notices.Tick(Milliseconds);

        StepDrawClocks(seconds);
        Scripts.EndFrame();
        Census();
    }

    /// <summary>
    /// The clocks 1.1 keeps in its world draw, after the world tick: the dynamic-light
    /// pass steps the light styles first, then works
    /// out every dynamic light's intensity and blob and fades the decals; then the emitters are
    /// updated and handed over; then the animating materials, whose flicker
    /// frames read the byte the styles wrote. Vitomir runs them
    /// whether or not anything draws (ours: the sky alone stays with the renderer).
    /// </summary>
    void StepDrawClocks(float seconds)
    {
        Location.Styles.Step(seconds * 1000f);
        Location.StepLightPass();
        Location.Particles.Step(seconds, Milliseconds);
        Location.Materials.Animate(DrawClocks.Interface(this, seconds) * 1000f, Location.Styles);
    }

    /// <summary>
    /// The interface clock's frame for this step, when the host keeps it apart from the
    /// game's: what the materials' animation runs on (<see cref="DrawClocks"/>). Taken by the next
    /// <see cref="Step"/>; null there, the step's own seconds.
    /// </summary>
    public float? InterfaceSeconds { get; set; }

    readonly Ai.Rng shared = new(1);

    /// <summary>How often the journal takes a census of the level (ms).</summary>
    public const int CensusMs = 2_000;
    int censusAt;

    /// <summary>
    /// A periodic count into the journal, so a stretch where nothing happened can be told apart
    /// from a stretch where everything had stopped — which is the distinction that cost two
    /// sessions before the overlay existed.
    /// </summary>
    void Census()
    {
        if (Diagnostics.Journal.Current is not { } journal || Milliseconds < censusAt) return;
        censusAt = Milliseconds + CensusMs;
        if (Location.Minds is not { } minds) return;

        int live = 0, dead = 0, aimed = 0, moving = 0, fighting = 0;
        foreach (var m in minds.All)
        {
            if (m.Thing.Actor is not { } a) continue;
            if (a.Dead) dead++; else live++;
            if (m.Target is not null) aimed++;
            if (m.Walker.Speed > 0f) moving++;
            if (a.Action is ActionMode.Attack or ActionMode.Swing) fighting++;
        }
        var me = Location.Player?.Thing.Actor;
        journal.Write(Milliseconds, Diagnostics.JournalKind.Tick,
            ("live", live), ("dead", dead), ("aimed", aimed), ("moving", moving),
            ("fighting", fighting), ("blows", Blows.Count),
            ("playerHp", (object?)me?.Health), ("playerDead", (object?)me?.Dead));

        // **The player's stats, but only the ones that have moved.** Added because
        // a character sheet showed frenzy at −184 and falling by one a second, and reading
        // every write to `Actor.Stat` in the tree found nobody who could be doing it. A value
        // nobody can account for needs the journal rather than another read: this says which stats
        // move, when, and by how much, and it costs a line only on the census where something
        // changed.
        //
        // It also catches the thing the original would never allow. The engine's stat set
        // clamps **every** stat write against a per-stat minimum and maximum,
        // and those bounds are authored — `Christof.NPC` opens with
        // `// STATS min max current` and gives frenzy `0 100 0`. Vitomir clamps nowhere, so any
        // stat can wander anywhere; until the clamp is built, this is what notices.
        if (me is not null) StatDrift(journal, me);
    }

    float[]? lastStats;

    /// <summary>One journal line per census naming the stats that changed and by how much.</summary>
    void StatDrift(Diagnostics.Journal journal, Actor me)
    {
        if (lastStats is null)
        {
            lastStats = [.. me.Stat];
            return;
        }
        var moved = new List<(string, object?)>();
        for (int i = 0; i < Actor.Stats && i < lastStats.Length; i++)
        {
            float delta = me.Stat[i] - lastStats[i];
            if (MathF.Abs(delta) < 0.0005f) continue;
            moved.Add(($"{(ActorStat)i}", $"{me.Stat[i]:0.###} ({delta:+0.###;-0.###})"));
            lastStats[i] = me.Stat[i];
        }
        if (moved.Count > 0) journal.Write(Milliseconds, "stats", [.. moved]);
    }

    /// <summary>What the journal calls a thing.</summary>
    internal static string Name(WorldThing thing) => Diagnostics.Watch.Name(thing);

    /// <summary>Runs the clock forward in fixed slices, so a still can be asked for a later moment.</summary>
    public void Advance(float seconds, float step = 1f / 60f)
    {
        for (float t = 0; t < seconds; t += step) Step(MathF.Min(step, seconds - t));
    }

    /// <summary>
    /// The blow the animation just called for. Rolling it and applying it are separate jobs, so
    /// both are asked in turn and the result is kept for anyone watching.
    /// </summary>
    void Land(WorldThing attacker)
    {
        if (attacker.Actor is not { Weapon: { } weapon } actor) return;
        // Out of ammunition and nothing to reload with — `outofammo`, and no blow at all.
        if (!Rpg.Projectiles.Loaded(attacker, weapon)) return;
        // A ranged attack looses its projectile instead of rolling, and a
        // thrown one then costs a charge of the item.
        if (weapon.Current.Is(WeaponFlag.Ranged))
        {
            var held = weapon;
            var aimed = actor.Target;
            if (Rpg.Projectiles.FireWeapon(Location, attacker, Random, Milliseconds, Location.Motions, ShotLanded) is { } shot)
            {
                Shots.Add(new Loosed(Milliseconds, attacker, aimed, shot, shot.Position, shot.Velocity));
                Diagnostics.Journal.Current?.Write(Milliseconds, Diagnostics.JournalKind.Shot,
                    ("by", Name(attacker)), ("at", aimed is null ? null : Name(aimed)),
                    ("weapon", held.Name), ("projectile", shot.Template),
                    ("from", shot.Position), ("heading", shot.Orientation));
            }
            // Every thrown weapon is also ranged (all 17 are `0x21`), so this is the only place it
            // can happen, though the engine asks after either path.
            Rpg.Projectiles.Thrown(attacker, held);
            return;
        }
        // The target gone, or a swing's target out of reach now — a whiff.
        if (Rpg.StrikeReach.Whiffs(attacker, actor, weapon) || actor.Target is not { } target) return;
        // (`ThrowBlow`): a throw goes straight to its damage, and any action but a swing
        // or a throw resolves nothing at all.
        var resolve = Rpg.ThrowBlow.For(actor.Action);
        if (resolve == Rpg.ThrowBlow.Resolve.Nothing) return;
        bool throwing = resolve == Rpg.ThrowBlow.Resolve.Throw;
        // A container is struck without a to-hit roll — the damage roll plus one, straight into its
        // health. This is what makes a barrel break at the first swing.
        if (!throwing && target.Type == ThingType.Container)
        {
            float struck = Combat.RollDamage(attacker, weapon.Current, Random) + Rules.GameRules.Current.Combat.ContainerMeleeBonus;
            Rpg.Container.Damage(target, struck, attacker, Location.Motions);
            return;
        }
        if (target.Actor is null) return;

        var attack = weapon.Current;
        var blow = throwing ? Rpg.ThrowBlow.Throw(attacker, target, attack, Random)
                 : Combat.Swing(attacker, target, attack, Random,
                                targetHelpless: Helpless(target),
                                attackerIsPlayer: attacker.Type == ThingType.Player,
                                facing: Facing(attacker, target));
        if (Rpg.Stake.Melee(attacker, target, weapon, blow, Random)) return;      // Staked, no harm
        var harm = blow.Hit
            ? Damage.Apply(target, attacker, HitKind.Melee,
                           attack.DamageType, blow.Damage, Random, Milliseconds,
                           motions: Location.Motions)
            : new Harm(0f, false, false);
        if (blow.Hit) Rpg.CombatMessages.Blow(attacker, target, blow.Damage, harm.Taken, blow.Multiplier);
        // A miss is still an attack as far as the one swung at is concerned.
        if (!blow.Hit && target.Mind is { } missed && Location.Minds is { } minds) minds.AttackedBy(missed, attacker);
        // The weapon's noise, from its own set and placed on the attacker: a miss is
        // another `swing`, a hit is `hit…` for whatever it struck. A blow that did no harm rings off
        // the shield, else the armour, else the target itself; one that hurt sounds like the target.
        Audio.Noise.Weapon(weapon, attacker, blow.Hit ? Audio.Noise.Struck(target, harm.Taken) : Audio.FoleyEvent.Swing);
        // A blow that landed tells the weapon's and the wielder's scripts (`hit`).
        if (blow.Hit) Location.ItemScripts?.Hit?.Invoke(attacker, weapon, target, harm.Taken);
        Blows.Add(new Landed(Milliseconds, attacker, target, blow, harm));
        Diagnostics.Journal.Current?.Write(Milliseconds, Diagnostics.JournalKind.Blow,
            ("by", Name(attacker)), ("at", Name(target)),
            ("weapon", weapon.Name), ("attack", weapon.CurrentAttack), ("ranged", false),
            ("hit", blow.Hit), ("crit", blow.Critical), ("margin", blow.Margin),
            ("dmg", blow.Damage), ("taken", harm.Taken), ("soaked", blow.Damage - harm.Taken),
            ("killed", harm.Killed), ("refused", harm.Refused),
            ("gap", Bodies.Gap(attacker, target)),
            ("hpLeft", (object?)target.Actor?.Health));
    }

    /// <summary>
    /// A defender who cannot get out of the way: the engine's thirty-point penalty is for
    /// someone restrained — which a neck victim is — or **feeding**, action 8 or 11: a vampire with
    /// its teeth in somebody is the one that cannot duck.
    /// </summary>
    static bool Helpless(WorldThing target) =>
        target.Actor is { } a
        && (a.Has2(Actor.Flag2.Restrained) || a.Action is ActionMode.Feeding or ActionMode.WristFeeding);

    /// <summary>
    /// How much the two of them are facing the same way, which is what the backstab bonus is scored
    /// on: both turned alike means the attacker is behind. Yaw is <c>Orientation.Z</c>, so the dot
    /// of the two forward vectors is the cosine of the difference whichever local axis is forward.
    /// </summary>
    static float Facing(WorldThing attacker, WorldThing target) =>
        MathF.Cos((attacker.Orientation.Z - target.Orientation.Z) * (MathF.PI / 180f));
}
