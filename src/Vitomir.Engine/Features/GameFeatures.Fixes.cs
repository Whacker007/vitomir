// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The eleven Fixes' declarations (ours): faults of 1.1's the player may switch off.
namespace Vitomir.Engine.Features;

public static partial class GameFeatures
{
    /// <summary>
    /// The install's script classes that declare the soak message's handler returning <c>void</c> — the ones 1.1
    /// never calls and the Fix <c>soakedvoid</c> does. Each does nothing but set a shell on its
    /// character, which is what lets the Fix be declared presentation; <c>SoakedHandlerTests</c> reads the
    /// install's sources and fails if the classes are others or a body does more.
    /// </summary>
    public static readonly string[] SoakedVoidHandlers = ["EffectDiscFortitude", "EffectDiscSkinOfTheAdder"];

    /// <summary>
    /// The Fixes, in the registry's order (the page lists the ones that change the rules first), every one off by default. Each line of a declaration is a claim a test
    /// holds: the axis is computed from what it takes and changes, and a feature that is each machine's must come
    /// out presentation (<c>FeatureDeclarationTests</c>).
    /// </summary>
    public static readonly Feature[] Fixes =
    [
        // Player.RateFor: 1.1 plays every locomotion cycle at a flat 30 (the animation update; the
        // motion channel's play) and parses the `movespeed` mode flag without ever reading it. On, a
        // flagged mode's rate follows the character's current speed against its template's. The rate is each
        // machine's picture — the character moves exactly as fast either way — and that holds only because no
        // walk or run cycle carries a tag the rules act on (Data.LocomotionRate). Two residues are left as they
        // are: the host's rate is sent to the players' machines, and the `sounds` random
        // stream moves with the footfalls.
        new(FeatureKind.Fix, "movespeed", "a locomotion cycle's rate follows the character's current speed")
        {
            Title = "Move Speed",
            Takes = [Points.RateFor], Changes = [Data.LocomotionRate], Whose = FeatureWhose.Machine,
        },
        // Player.RateFor again, and so it refines movespeed: where a stride can be measured from the cycle's own
        // footfall tags (Anim.Stride) it has the say, and movespeed keeps the rest — which is what the method
        // does when both are on. Ours, and the more invented of the two.
        new(FeatureKind.Fix, "matchedgait", "a locomotion cycle's rate makes its feet match the ground")
        {
            Title = "Matched Gait",
            Takes = [Points.RateFor], Changes = [Data.LocomotionRate], Whose = FeatureWhose.Machine,
            Refines = "fixes.movespeed",
        },
        // Player.Clear: 1.1's walker asks the free-spot test and keeps only the things of its answer (and
        // the ground ray), trusting the walk graph. On, a step pushed from the keyboard must
        // also fit the body against the level (DriveBody.Fits). It reads which character is being pushed, which
        // `driving` changes — and it exists only for a driven step, so it tolerates driving: rule 2's own case.
        new(FeatureKind.Fix, "bodycollision", "a driven character has to fit its whole body against the level")
        {
            Title = "Body Collision",
            Takes = [Points.Clear], Reads = [Data.Steered], Changes = [Data.Position], Tolerates = ["qol.driving"],
        },
        // HoverGlow.Shown: 1.1's hover writes the colour (the fade, then the draw)
        // and the reference build's DirectX wrapper shows its outer bytes exchanged. The stored colour is the same
        // either way; only the tint handed to the renderer differs.
        new(FeatureKind.Fix, "glowcolours", "the hover glow in the colours 1.1 asks for, not the reference build's")
        {
            Title = "Glow Colours",
            Takes = [Points.GlowShown], Changes = [Data.GlowTint], Whose = FeatureWhose.Machine,
        },
        // ShapeShift.Shift (Rpg.ShapeTorchFix.LetGo and GiveBack): 1.1's shape change keeps the off-hand
        // item, so a wolf wears the torch and cannot bite. On, the item leaves the
        // hand as setting no utility item does and stays in its slot until the shape ends.
        new(FeatureKind.Fix, "shapetorch", "a character taking another shape holds its torch out of sight (still worn) until the shape ends, so a wolf can bite")
        {
            Title = "Shape Torch",
            Takes = [Points.Shift], Changes = [Data.OffHand],
        },
        // LiveRollover.Keep (and WorldHover.For beside it): 1.1 writes a rollover when the hover changes (a
        // control's or a world thing's) and on a few events. On, every frame.
        new(FeatureKind.Fix, "liverollover", "a rollover's lines kept current while the pointer rests (a portrait's blood climbs as you feed)")
        {
            Title = "Live Rollover",
            Takes = [Points.RolloverKeep], Changes = [Data.RolloverLines], Whose = FeatureWhose.Machine,
        },
        // CodexScenes.Soaked: the soak message (sent from the soak roll) looks its handler up by
        // name and full signature (the callback table has `soaked(IIFIFI)F`), so the two shipped
        // `void` handlers are never called. On, they are — script methods 1.1 never runs.
        // **Presentation, by what the install's scripts do** (the "color flashing" is purely
        // visual): the classes that declare a `void soaked` of that signature are EffectDiscFortitude and
        // EffectDiscSkinOfTheAdder, and each body does nothing but set the model's shell white when the soak
        // covers the blow — which `SoakedHandlerTests` reads from the install's own sources, as
        // `LocomotionTagTests` guards the gait Fixes. A mod's class with such a handler would be run too, and
        // could do anything: the test reads the install it is given. **The host's all the same**: the host is the
        // machine that runs the script, so its switch is the one that decides (a presentation feature may be the
        // host's; what the registry's own check asks is that a rules feature is, and a machine's is presentation).
        new(FeatureKind.Fix, "soakedvoid", "Fortitude and Skin of the Adder flash white when a blow is soaked away whole, as their scripts were written (1.1 never calls them)")
        {
            Title = "Soaked Void",
            Takes = [Points.Soaked], Changes = [Data.ModelShell],
        },
        // NetSynch.Kept: 1.1's host sends nothing flagged NO NET SYNCH and a player's
        // machine skips a worn item's scripts. On, the host sends the props a script made so flagged.
        new(FeatureKind.Fix, "netprops", "in a network game the host sends the flagged props no player's machine makes (another character's torch flame is seen)")
        {
            Title = "Network Props",
            Takes = [Points.NetKept], Changes = [Data.Announced],
        },
        // CodexScenes.SayHit (its catch arm, CodexScenes.Berserker): the fault is the shipped script's —
        // ItemWeaponBerserker.hit indexes FRENZY_DURATIONS one past its end at level 5 — and 1.1 reports the throw
        // and applies nothing (an effect's level is held to 5). On, the level-5 frenzy takes hold.
        new(FeatureKind.Fix, "berserker", "the Berserker weapon's strongest frenzy takes hold (1.1's script is one entry short and throws on it)")
        {
            Title = "Berserker",
            Takes = [Points.SayHit], Changes = [Data.Effects],
        },
        // Minds.Damaged (Minds.WakeOnHit): 1.1's damage event hands a held character its attacker, but
        // its think is gated on THING_AF_AIPAUSED and nothing clears it. On, a party member's blow does.
        new(FeatureKind.Fix, "wakeonhit", "a character a script holds still is let go when a party member hurts it (1.1 leaves it standing)")
        {
            Title = "Wake on Hit",
            Takes = [Points.Damaged], Changes = [Data.AiHeld],
        },
        // Projectiles.Hitscan: 1.1 lets a shot be fired within the weapon's MAXDISTANCE of the target's body
        // and then casts the ray from the muzzle for the projectile's own range,
        // which falls short at the edge. On, the ray is longer by the shooter's radius and a little.
        new(FeatureKind.Fix, "gunreach", "a gun fired from the very edge of its reach hits (1.1's shot falls just short)")
        {
            Title = "Gun Reach",
            Takes = [Points.Hitscan], Changes = [Data.ShotReach],
        },
        // Actions.HeldAgain: 1.1 asks whether the button is still held only at the end of a swing, so an attack held
        // through a cast, a flinch or anything else that keeps the character busy is never made. On, it starts the moment the
        // character is free (Whacker007, 2026-11-22).
        new(FeatureKind.Fix, "heldattack", "a held attack starts the moment the character is free, after a cast or a flinch too (1.1 starts one only after a swing)")
        {
            Title = "Held Attack",
            Takes = [Points.HeldAgain], Changes = [Data.ActionOrder],
        },
    ];
}
