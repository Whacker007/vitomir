// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The ten repairs' declarations (ours): defects always corrected, with no switch.
namespace Vitomir.Engine.Features;

public static partial class GameFeatures
{
    /// <summary>
    /// The repairs, in the page's order. Always on — a repair has no switch — and declared all the same, so the
    /// registry lists what every game is played with and weighs each switch against them: <c>packcells</c> reads
    /// the cells <c>dropcells</c> changes, and says it tolerates it. The names are <see cref="World.Repairs"/>'s
    /// constants, which the code sites cite.
    /// </summary>
    public static readonly Feature[] Repairs =
    [
        // GroundOrders.Landing: 1.1's drop order computes `delta * 50 + origin` past the fifty-unit reach
        //  where `normalize(delta) * 50 + origin` was meant.
        new(FeatureKind.Repair, World.Repairs.DropLanding, "a space-held drop past arm's reach lands 50 units away, not 50 times as far")
        {
            Title = "Drop Landing",
            Takes = [Points.Landing], Changes = [Data.Position],
        },
        // CodexScenes' SendActorToPos handler: 1.1's walker takes a leg under a tenth of a unit as no leg at all
        // and sends no `arrived`. Here it counts as arriving, on the next step.
        new(FeatureKind.Repair, World.Repairs.ClosedRound, "a patrol whose round ends where it began goes round again instead of stopping")
        {
            Title = "Closed Round",
            Takes = [Points.SendActorToPos], Changes = [Data.ScriptCalls],
        },
        // SpecialMove.Try: 1.1's player's machine replays an ordinary strike as a swing, which rolls the
        // throw again on its own generator. Here a player's machine never rolls.
        new(FeatureKind.Repair, World.Repairs.HostThrows, "a player's machine does not roll its own throw when it replays the host's strike")
        {
            Title = "Host Throws",
            Takes = [Points.SpecialMoveTry], Changes = [Data.ThrowRoll],
        },
        // PartyTravel.Arrival: 1.1's party arrival gives a member with no start of its own the
        // map's origin. Here it arrives where the leader did.
        new(FeatureKind.Repair, World.Repairs.MemberStart, "a party member with no start of its own at the entrance stands on the leader, not at the map's origin")
        {
            Title = "Member Start",
            Takes = [Points.Arrival], Changes = [Data.Position],
        },
        // MainFlagsWord.AtStart: 1.1 folds the Control Options into the AI's word at start-up
        // and on the Controls page's OK only, and every load
        // and quit zeroes it through the teardown. Here the options are folded in again.
        new(FeatureKind.Repair, World.Repairs.KeepControls, "the Control Options' casting and quick-slot boxes hold after a load and after a quit to the menu")
        {
            Title = "Keep Controls",
            Takes = [Points.AtStart], Changes = [Data.AiOptions],
        },
        // LoadedPack.Mend: 1.1 takes a loaded item's cell on trust. Here a used-up consumable is not made again
        // and an item off its grid or on a taken cell goes to the first free one by 1.1's own search.
        // It reads the cells a save gives, which `dropcells` decides at a drop — any cell a drop may choose is one
        // the search accepts, so it tolerates dropcells.
        new(FeatureKind.Repair, World.Repairs.PackCells, "a loaded pack keeps no used-up consumable and puts no two items on one cell")
        {
            Title = "Pack Cells",
            Takes = [Points.Mend], Reads = [Data.ItemCell], Changes = [Data.ItemCell], Tolerates = ["qol.dropcells"],
        },
        // InventoryPass.Draw (and StatusBarPass.Draw): 1.1 loads `<icon>_R` beside every icon and shows
        // a null bitmap where the install has none. Here the plain icon is kept. A picture:
        // each machine's.
        new(FeatureKind.Repair, World.Repairs.HoverPicture, "an item whose icon has no hover picture keeps its plain one under the pointer")
        {
            Title = "Hover Picture",
            Takes = [Points.HoverIcon], Changes = [Data.IconPicture], Whose = FeatureWhose.Machine,
        },
        // CodexScenes.RestoreScripts (CodexScenes.PendulumHome): by reading, 1.1's swing (from where RotatePivot
        // started) begins again after a load (the level start's order) from wherever the save caught the weight.
        // Here the weight is put back at its own place first.
        new(FeatureKind.Repair, World.Repairs.PendulumHome, "the cathedral's swinging weight starts from its own place after a load, so its switch parks it out of the way")
        {
            Title = "Pendulum Home",
            Takes = [Points.RestoreScripts], Changes = [Data.Position],
        },
        // ExplosionDamage.Sees: 1.1's second sight ray  is cast to the
        // explosion's own height and so has no length and always passes. Here it goes to the top of the target.
        new(FeatureKind.Repair, World.Repairs.BlastSight, "a wall stops an explosion's blow: its second sight ray goes to the top of the target, not nowhere")
        {
            Title = "Blast Sight",
            Takes = [Points.Sees], Changes = [Data.BlastSeen],
        },
        // Save.LoadedEffects.Remake: 1.1's load makes an effect's script again and `restore`s it and
        // never tells it `effectstarted`; what the script made as it started is flagged
        // SAVE NEVER (the links' writer passes it over) and a model's shell is not in
        // the file. In 1.1 the looks are gone
        // after a load. Here a load makes them again from what the save recorded, under the GUIDs they had.
        // And a model the script froze (Prison of Ice: `FreezeAnimations`, bit 0x20000 of the flag word that same
        // reader clears) is frozen again in the pose it stood in ("Yes").
        // Presentation: nothing made again can be touched (Data.EffectLooks), a shell is only drawn, and the pose
        // is the picture of a hold the rules keep by themselves (Data.ModelPose). Each machine's own: only the
        // machine that loads a save does it, to its own copy.
        new(FeatureKind.Repair, World.Repairs.LoadedEffects, "an effect's pad, trail, glow and frozen pose are still there after a load, not gone until the effect ends")
        {
            Title = "Loaded Effects",
            Takes = [Points.EffectsRemake], Changes = [Data.EffectLooks, Data.ModelShell, Data.ModelPose], Whose = FeatureWhose.Machine,
        },
    ];
}
