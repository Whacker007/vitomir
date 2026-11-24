// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Who counts as an enemy, and who can be seen.
//
// Two tests sit in front of everything the AI does about other characters, and neither is the one
// you would guess:
//
//   * **friend or foe** is a team comparison with two special cases and an
//     effect that can override it;
//   * **noticing** is a *stealth against perception* comparison made
//     before any line of sight is traced, and the target's stealth depends on what it is doing.
//
// Both are small, both are 1.1's own, and together they are what 1,150 of the
// game's 1,277 characters spend their time running.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>The engine's friend-or-foe and can-I-see-you tests.</summary>
public static class Sight
{
    /// <summary>
    /// How far up a character the sight line starts, as a share of its own body height
    /// (0.7). Not the eyes and not the feet: seven tenths of
    /// the way up, which is about the chest.
    /// </summary>
    public const float EyeShare = 0.7f;

    /// <summary>
    /// Whether two characters are on friendly terms. In the engine's own order:
    ///
    /// <list type="number">
    /// <item><b>Pedestrians and lawmen never fight.</b> Teams 10 and 11 are friendly to each other
    /// by name, which is why the townspeople and the guards in a square ignore one another.</item>
    /// <item>The same two, reached the other way: a character whose <c>TEAMFRIENDLY</c> effect
    /// names the other of that pair counts as one of them.</item>
    /// <item>The same team is friendly, which is the ordinary case.</item>
    /// <item>Failing all of that, the other's <c>TEAMFRIENDLY</c> effect — the team it *reads* as
    /// being on. Mask of a Thousand Faces is the one discipline that sets it. When there is no
    /// such effect the engine substitutes <see cref="Actor.Teams.None"/> itself rather than asking
    /// the store for a default, so a character on team 8 is friendly to everyone who has no team
    /// of their own.</item>
    /// </list>
    /// </summary>
    public static bool Friendly(WorldThing mine, WorldThing? theirs)
    {
        if (theirs?.Actor is not { } them || mine.Actor is not { } me) return false;
        int team = me.Team;

        if (team == Actor.Teams.Lawmen)
        {
            if (them.Team == Actor.Teams.Pedestrians) return true;
            if (Reads(theirs) is { } as1 && as1 == Actor.Teams.Pedestrians) return true;
        }
        if (team == Actor.Teams.Pedestrians)
        {
            if (them.Team == Actor.Teams.Lawmen) return true;
            if (Reads(theirs) is { } as2 && as2 == Actor.Teams.Lawmen) return true;
        }
        if (team == them.Team) return true;
        return team == (Reads(theirs) ?? Actor.Teams.None);
    }

    /// <summary>The team a character's <c>TEAMFRIENDLY</c> effect makes it read as, or null.</summary>
    static int? Reads(WorldThing thing) =>
        thing.Effects.Has(EffectKind.TeamFriendly)
            ? (int)thing.Effects.Value(EffectKind.TeamFriendly)
            : null;

    /// <summary>
    /// How hard a character is to notice right now. It starts from the character's own
    /// stealth — zero for everybody until a <c>HIDE</c> effect raises it — and then
    /// **comes down for whatever the character is doing**, because doing something is conspicuous.
    /// Read, for a character (type 0 or 7) with any stealth at all, first match wins:
    ///
    /// <list type="bullet">
    /// <item>**feeding** — ACTION 8 or 11, the neck or the wrist — or **in a swing while busy**
    /// (ACTION 5 and busy, dead or held): <b>five</b> easier, held at zero;</item>
    /// <item>**casting while busy** (ACTION 13 and busy, dead or held): <b>three</b> easier, held at
    /// zero;</item>
    /// <item>**walking** (the walker's bit 0, a walk under way — waiting included):
    /// <b>one</b> easier;</item>
    /// <item>anything else: as it is.</item>
    /// </list>
    ///
    /// So a vampire hidden by Obfuscate who bites somebody is five points easier to see for as long
    /// as the feed lasts, and a swing gives it away only while the blow is playing. A swing's
    /// recovery that has run out, or a cast's lingering ACTION 13 once it is free, costs nothing.
    /// <paramref name="moving"/> stands in for the walker when the thing has none (a test's body).
    /// </summary>
    public static int StealthOf(WorldThing thing, bool moving)
    {
        if (thing.Actor is not { } actor) return 0;
        int stealth = actor.Stealth;
        if (thing.Type is not (ThingType.Actor or ThingType.Player) || stealth == 0) return stealth;
        int now = thing.Place?.Milliseconds ?? 0;
        bool held = now < actor.BusyUntil || actor.Has(Actor.Flag.Dead) || actor.Has2(Actor.Flag2.Restrained);
        if (actor.Action is ActionMode.Feeding or ActionMode.WristFeeding || (actor.Action == ActionMode.Swing && held))
            return Math.Max(stealth - Rules.GameRules.Current.Sight.StealthPenaltyFeedOrSwing, 0);
        if (actor.Action == ActionMode.Cast && held) return Math.Max(stealth - Rules.GameRules.Current.Sight.StealthPenaltyCasting, 0);
        bool walking = thing.Locomotion is { } walker ? walker.Walking : moving;
        return walking ? stealth - Rules.GameRules.Current.Sight.StealthPenaltyWalking : stealth;
    }

    /// <summary>What feeding, or a swing still playing, takes off stealth (<c>b − 5</c>).</summary>
    public const int FeedingOrSwinging = 5;
    /// <summary>What a cast still playing takes off stealth (<c>b − 3</c>).</summary>
    public const int Casting = 3;
    /// <summary>What walking takes off stealth (<c>b − 1</c>).</summary>
    public const int Walking = 1;

    /// <summary>
    /// Whether one character can make out another at all, before anything is traced.
    /// The comparison is the looker's perception against the target's stealth, and the default for
    /// both is **zero**, which the engine's own actor constructor writes — so out of the
    /// box everybody can see everybody and it is <c>HIDE</c> that changes it. <c>SENSE</c> is the
    /// other side of the same test: it is what raises a looker's perception.
    /// </summary>
    public static bool Notices(WorldThing looker, WorldThing target, bool targetMoving) =>
        looker.Actor is { } eyes && StealthOf(target, targetMoving) <= eyes.Perception;

    /// <summary>
    /// **How far up a body the sight line attaches, and it is not the same at both ends.**
    /// The sight line computes each end the same way apart from one constant: the size it uses is the
    /// thing's <b>height</b> when its collide shape is a cylinder (shape 2) and
    /// its <b>radius</b> otherwise — a sphere or a box has no separate
    /// height, so its radius is its half-extent. That size is then multiplied by
    /// <see cref="EyeShare"/> = <b>0.7</b> at the <i>looker</i> and by
    /// <see cref="TargetShare"/> = <b>0.5</b> at the <i>target</i>, and added to that
    /// end's Z.
    ///
    /// This tree used 0.7 at both ends until then, which aimed the far end of every sight
    /// line higher than the engine does and so cleared low geometry the original's line hits.
    /// </summary>
    public static Vector3 Eye(WorldThing thing, float share) =>
        thing.Position with
        {
            Z = thing.Position.Z
                + (thing.Collide == World.CollideShape.Cylinder ? thing.Height : thing.Radius) * share,
        };

    /// <summary>Where a character's own sight line starts (0.7 of its body).</summary>
    public static Vector3 Eye(WorldThing thing) => Eye(thing, Rules.GameRules.Current.Sight.SightLineHeights[0]);

    /// <summary>What a sight line aims at on the far end (0.5 of the target's body).</summary>
    public const float TargetShare = 0.5f;

    /// <summary>
    /// **The sight line** — whether the world lets one character see another: the engine's sweep
    /// (`WorldSweep`) with things and surfaces, from the looker's eye (0.7 up) to halfway
    /// up the target (0.5), cast from the sector the eye is in (the looker's own when it holds the
    /// eye; otherwise the first that does). Then its results nearest first:
    /// **a surface blocks unless its group is translucent** (`0x2000` or
    /// `0x4000`, `Trans33`/`Trans66`), and **a thing blocks only when it carries
    /// `THING_FLAG_VISBLOCK`** (`0x2000`, *"does block visibility for line-of-sight queries"*).
    /// Anything else is seen past. A line shorter than a tenth of a unit sees.
    ///
    /// Read. Until then this was ours: a two-sided ray against every surface, with no
    /// thing in the way ever — so a window, a detail brush and the back of a wall blocked sight and
    /// a `VISBLOCK` thing did not. Over every pair of characters within 2,000 units in five
    /// populated scenes (1,706 pairs) the two answer differently once, a back face in the first
    /// monastery; no shipped scene tested places a `VISBLOCK` thing.
    /// </summary>
    public static bool Sees(NilLevel? level, WorldThing looker, WorldThing target)
    {
        if (level is null) return true;
        // Both in the same world: a pair in two places sees nothing.
        if (looker.Place is { } here && target.Place is { } there && !ReferenceEquals(here, there)) return false;
        var from = Eye(looker, Rules.GameRules.Current.Sight.SightLineHeights[0]);
        var span = Eye(target, Rules.GameRules.Current.Sight.SightLineHeights[1]) - from;
        float length = span.Length();
        if (length < Rules.GameRules.Current.Sight.SightAlwaysWithin) return true;
        var solid = looker.Place?.Solid;
        int sector = (int)looker.Sector;
        if (solid?.SectorAt(from, sector) is >= 0 and var holding) sector = holding;
        if (sector < 0 || sector >= level.Sectors.Length) return true;       // ours: nowhere to cast from
        // Only a VISBLOCK thing can stop the line, so only those are offered to the sweep.
        var things = ReferenceEquals(looker.Place?.Level, level)
            ? looker.Place!.Things.Where(t => t.Has(WorldThing.Flag.VisBlock)) : null;
        foreach (var met in WorldSweep.Line(level, things, sector, from, span * (1f / length), length,
                                            new WorldSweep.Asks(Pick.Tests.Both)))
        {
            if (met.Kind == ContactKind.Surface)
            {
                if (Levels.Cells.CellLevel.Of(level) is not null) return false;   // a cooked level's triangle (ours): solid, and none is translucent
                var s = level.Sectors[met.Sector];
                if ((s.Groups[s.Surfaces[met.Surface].Group].SurfaceFlags & SeenThrough) == 0) return false;
            }
            else if (met.Thing is { } thing && thing.Has(WorldThing.Flag.VisBlock)) return false;
        }
        return true;
    }

    /// <summary>A sight line shorter than this always sees.</summary>
    public const float SightMinimum = 0.1f;

    /// <summary>The group flags a sight line passes through: the two translucencies (`0x6000`).</summary>
    public const uint SeenThrough = NilGroup.FlagTrans33 | NilGroup.FlagTrans66;
}
