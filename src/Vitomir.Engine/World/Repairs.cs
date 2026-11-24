// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The third category, settled: defects of the original that Vitomir **always** corrects,
// with no switch to turn the correction off.
//
// It exists because the two categories the project already had could not hold one. A **Fix**
// (`Fixes.cs`) is faithful by default and optional, so that
// whoever plays can recognise the game and call out what looks off against it.
// That argument is about **recognising** the game, and it is
// strongest for behaviour you can see and weakest for a defect that quietly destroys something you
// owned. A **quality-of-life** switch is the opposite default and is about convenience, which this
// is not.
//
// The rule for admission: things that outright break the game,
// like causing item loss, are kept track of here with no option to turn them off. And
// the constraint that comes with it: what goes in is chosen with care, and it
// is kept minimal. Widened with the second entry: behaviour that is
// *"clearly ... overlooked/broken"* in the original, which nobody would want to turn back on.
//
// **A repair is a feature of the registry like any other** (`Features.GameFeatures.Repairs`),
// always on: its one line and where it acts are declared there, its name is a
// constant here for the code site to cite. A repair is not
// invisible just because it has no switch: it is declared and explained, so a later decision to make one
// optional after all has something to act on.
using Vitomir.Engine.Features;

namespace Vitomir.Engine.World;

/// <summary>Defects the engine always corrects. See the file header for the admission rule.</summary>
public static class Repairs
{
    /// <summary>
    /// **droplanding** — a space-held drop past arm's reach lands fifty units away, not fifty
    /// times as far.
    ///
    /// The throw order measures the vector from the character to the clicked point and,
    /// beyond the fifty-unit reach, computes the landing point as `delta * 50 + origin` — the
    /// **same constant** (50.0) serving as the threshold it just compared against
    /// *and* as the multiplier. The author meant `normalize(delta) * 50 + origin`; the
    /// code is three multiplications by that constant with no division anywhere.
    ///
    /// A click 100 units away throws the item 5,000. The order then asks the ground ray for the walk
    /// surface under that point and refuses when there is none, so most long throws are merely
    /// rejected — but the ones that find a surface put the item in another room, still saved,
    /// owned by nobody, and gone. A sword was lost in a real 1.1 save this way.
    ///
    /// **The first entry, and the one the category was created for.**
    /// </summary>
    public const string DropLanding = "droplanding";

    /// <summary>
    /// **closedround** — a patrol whose round ends where it began goes round again instead of
    /// stopping.
    ///
    /// Every patrol in the game is a script (`WalkCycle`, `WalkCycleLawmen`) that sends its
    /// character to the next keyframe from `arrived` and wraps from the last to the first. 30 of the
    /// 143 shipped rounds end **on** their first keyframe, so the order after the first lap is a walk
    /// to where the character already stands — and the walker takes a leg under a tenth of a unit as
    /// no leg at all (`StartWalk`: length 0, answer 0), sends no `arrived`, and nothing ever asks
    /// again. The round stops for good on its first keyframe; London's police are the case seen in play.
    ///
    /// The repair: a script's `SendActorToPos` to where the character already stands **counts as
    /// arriving there** — `arrived` goes out on the next step, as it would at the end of any walk.
    /// Everything else about the order is the engine's.
    ///
    /// Admitted on a wider reading of the bar than the first entry's:
    /// it is clearly overlooked or broken behaviour in the original game, with no
    /// reason to turn it off: guards and police should patrol instead of just standing
    /// there.
    /// </summary>
    public const string ClosedRound = "closedround";

    /// <summary>
    /// **hostthrows** — a player's machine does not roll its own throw when it replays the host's
    /// strike.
    ///
    /// In a network game the host decides every strike: its roll turns a `THROW` attack into a grab,
    /// and it tells the players' machines which it was (message 0xc,
    /// its last byte 1 for a throw); the damage is its too (the damage applies nothing on a client but a
    /// blow that came from the net). A player's machine strikes only what the host sends (the strike
    /// returns at once on a client unless the strike is replayed) and replays a throw with the roll
    /// skipped (the replay with 1, 1) — but an **ordinary** strike it replays with
    /// 1, 0, and the strike then rolls the throw chance again on that machine's own
    /// generator. A win there grabs the victim and throws it on that one screen (the grab
    /// sends nothing) while on the host it was a swing: a throw that never happened, until the
    /// snapshots put the victim back.
    ///
    /// The repair: a player's machine never rolls it; it throws only when the host says so. Admitted:
    /// it is dropped where the host is the one that calculates.
    /// </summary>
    public const string HostThrows = "hostthrows";

    /// <summary>
    /// **memberstart** — a party member with no start of its own at the entrance stands on the
    /// leader, not at the map's origin.
    ///
    /// Arriving in a place, each member in turn asks the entrance for the
    /// start numbered one past its slot (by group with <c>i + 1</c>); when there is none
    /// it is given the identity matrix with position (0, 0, 0), entered into the world at the
    /// entrance's sector and its ground looked for from there — the **map's origin**, which is
    /// usually inside a wall, under the floor or in another room. An entrance with fewer starts than
    /// the party has members is a data slip, and the member it strands is lost to the player.
    ///
    /// Vitomir keeps its own fallback: such a member arrives **where the leader arrived**. Everything
    /// else about the arrival is the engine's. Admitted when a comparison with 1.1
    /// showed 1.1's: the fallback in case there is no spawn point is a good fail-closed catch.
    /// </summary>
    public const string MemberStart = "memberstart";

    /// <summary>
    /// **keepcontrols** — the Control Options' four casting boxes and two quick-slot boxes hold after a
    /// load and after a quit to the menu.
    ///
    /// The six boxes are bits of one AI word. 1.1 folds them in at start-up
    /// and when the Controls page is applied — and nowhere
    /// else — while every load and every quit to the menu tears the game down
    /// through the load's reset, which writes the word to **zero**. So after any load the party casts every
    /// kind of discipline and draws on the whole pack whatever the boxes say, until the player happens
    /// to open the Controls page and press OK: the long-known "companions ignore the Control Options"
    /// bug.
    ///
    /// The repair folds the options into the word the teardown leaves (`Ai.MainFlagsWord.AtStart`).
    /// Admitted as it is: fixed with no flag. It is a
    /// real bug that was never fixed in the game, and there is no reason to leave it in.
    /// </summary>
    public const string KeepControls = "keepcontrols";

    /// <summary>
    /// **packcells** — a loaded pack keeps no used-up consumable and puts no two items on one cell.
    ///
    /// 1.1 takes a loaded item's cell on trust and does not resolve a collision: two records on one
    /// cell are drawn one over the other and a click finds whichever its list meets first. 1.1 never
    /// writes such a save, but Vitomir did until then — a consumable's last charge stayed in the
    /// list (<c>Rpg.UsedUp</c>), was saved, and came back from a load at 0 or below, usable again and
    /// on a cell something else had taken since (seen in play). On a load, a consumable
    /// destroyed at zero with no charge left is not made again, and an item off its grid or on a taken
    /// cell goes to the first free one by 1.1's own search (<c>Save.LoadedPack</c>).
    ///
    /// On the first half of the bar: a pack that hides one item under another, or offers a potion
    /// that was drunk, is a broken save, and nobody would want it back.
    /// </summary>
    public const string PackCells = "packcells";

    /// <summary>
    /// **hoverpicture** — an item whose icon has no red-outlined picture keeps its plain one while the
    /// pointer is on it. 1.1 loads <c>&lt;icon&gt;_R</c> beside every icon and, where the install
    /// has none, keeps a null bitmap that the hover then shows: the item vanishes
    /// under the pointer. Only quest items lack one (Ecaterina's vitae, the locket of St Jude, Lucretia's
    /// heart, Luther's shackles, the Stephansdom and Tower keys, the Tremere amulet, the transmitter, the
    /// arcanulums, the golem's shem). A good fix to admit.
    /// </summary>
    public const string HoverPicture = "hoverpicture";

    /// <summary>
    /// **pendulumhome** — the cathedral's swinging weight (`STE1_Pendulum`) is put back at its own place
    /// before its script starts again after a load, so the swing does not begin from wherever the save
    /// caught it and its switch parks it out of the way (<c>Scripting.CodexScenes.PendulumHome</c>;
    /// `RotatePivot`'s start, the load's order). By reading: not observed in 1.1.
    /// </summary>
    public const string PendulumHome = "pendulumhome";

    /// <summary>
    /// **blastsight** — a wall stops an explosion's blow.
    ///
    /// An explosion hurts what it can see: a sweep toward the body, and
    /// failing that a second one meant for the top of the body. The second was written with the
    /// explosion's own point and height where the target's were meant, and no explosion has a
    /// height, so it is a ray of no length that meets nothing and passes — and a grenade, a
    /// molotov or a fireball's burst hurt everyone in reach through walls and floors. The repair:
    /// the second ray goes to the target's point raised by the target's height (a cylinder's
    /// `HEIGHT`, else its `RADIUS`), as the code around it plainly meant.
    ///
    /// Logged as a Fix candidate (*"clearly a bug"*); made a repair:
    /// *"Let's turn it off, no switch. That's just a straight up bug in my view."*
    /// </summary>
    public const string BlastSight = "blastsight";

    /// <summary>
    /// **loadedeffects** — an effect's pad, trail, glow and frozen pose are still there after a load.
    ///
    /// A discipline's effect script makes its looks as the effect starts (`effectstarted`): a pad or a trail
    /// spawned and attached to the character, a shell set on its model. Every such thing is flagged `SAVE
    /// NEVER` in 1.1's own data, so no save keeps it (the links' writer passes it over);
    /// a save keeps no model's shell either (the model's writer writes none, and its reader throws
    /// away even the flag word); and a load makes the effect's script again and
    /// `restore`s it without telling it `effectstarted`. So in 1.1 a character loaded with
    /// Heightened Senses on has the effect and its icon and no rosette at its feet until the effect ends and is
    /// cast again.
    ///
    /// The repair: every save records, with each effect, what its script made that is still there, and a load
    /// makes that again — each thing under the GUID it had, so that the script, which kept that number, still
    /// removes it when the effect ends, and each shell as it stood (<c>Save.LoadedEffects</c>). The script is
    /// not run again. A save from before the record loads as it did.
    ///
    /// And a model the script froze is frozen again in the pose it stood in (asked
    /// whether Prison of Ice's pose should come back too: *"Yes."*). `FreezeAnimations` sets the model's bit
    /// 0x20000, which is in the flag word that same reader throws away, so in 1.1 the prisoner is held by the
    /// rules after a load (the discipline's second effect is IMMOBILIZE, which is saved) and stands at ease
    /// under the ice. The pose is the picture only: no animation is put back with it.
    ///
    /// Play showed it (Christof's heightened senses still active, but the visual effect at
    /// his feet gone); it was a Fix at first, and became a repair once a look at
    /// 1.1 showed the same: in the base game the visual effects are not preserved on save
    /// and load. That is a plain bug, so it is fixed with no option to turn it off.
    /// Each machine's own in a network game: it changes what the machine that loads
    /// a save shows.
    /// </summary>
    public const string LoadedEffects = "loadedeffects";

    /// <summary>
    /// Every repair, by name, with its one line — read from the
    /// registry's declarations, in its order.
    /// </summary>
    public static readonly (string Name, string What)[] All =
        [.. GameFeatures.Registry.Of(FeatureKind.Repair).Select(f => (f.Name, f.What))];
}
