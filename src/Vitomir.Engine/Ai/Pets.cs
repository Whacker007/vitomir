// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A master's pets as a list: all of them let go when the party leaves a place
// or the master is destroyed, and all of them handed his new target.
// The single pet's way in and way out are in MindEffects.cs.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ai;

public sealed partial class Minds
{
    /// <summary>
    /// **Every pet let go** — the list walk 1.1 has twice, byte for byte: once in the party's scene end
    /// for each member after taking him out of the world,
    /// and once in the actor's destructor before it frees the list.
    /// For each slot with a GUID in it, the pet is looked up; when it is
    /// still a character (type 0 or 7) with a mind, still marked a pet (bit 0x4) and still
    /// this master's, it is released as a pet whose effect has ended is
    /// (<see cref="ReleasePet"/>): a **summoned** one runs from its master, afraid, fades
    /// out over five seconds and is gone; a **beckoned** animal loses the mark and goes back to its
    /// home mode. The slot is emptied whether or not the pet was found.
    /// </summary>
    /// <remarks>
    /// A released summoned pet's five seconds run on the clock of the place it was left in — so a
    /// place Vitomir keeps loaded (ours: 1.1 writes the place away) lets it fade when the party
    /// comes back to it, if its clock has not run on while away.
    /// </remarks>
    public void ReleasePets(Mind master)
    {
        if (master.Pets is not { } list) return;
        for (int i = 0; i < list.Length; i++)
        {
            int guid = list[i];
            if (guid == 0) continue;
            if (where?.Thing(guid) is { Type: ThingType.Actor or ThingType.Player, Mind: { } pet } thing
                && (pet.Flags & Mind.FlagPet) != 0 && pet.MasterGuid == master.Thing.Guid)
                ReleasePet(master, thing);
            list[i] = 0;
        }
    }

    /// <summary>
    /// **The party leaves a place**: each member's pets are let go (the walk over the
    /// master's list, when the member has a list) — the leader's and every companion's. Nothing
    /// follows its master through an exit.
    /// </summary>
    public void PartyLeft(IEnumerable<WorldThing> members)
    {
        foreach (var member in members)
            if (member.Mind is { Pets: not null } mind) ReleasePets(mind);
    }

    /// <summary>
    /// **A master's destroyed** (the actor's destructor): his pets are let go before the
    /// list is freed — an enemy summoner's elemental is released when his body is taken
    /// away, not when he dies. The list goes with him.
    /// </summary>
    public void Destroyed(WorldThing thing)
    {
        if (thing.Mind is not { Pets: not null } mind) return;
        ReleasePets(mind);
        mind.Pets = null;
    }

    /// <summary>
    /// **The pets take their master's new target** (from <c>SetTarget</c>
    /// whenever the master has a list). Each pet found that has a mind and is
    /// not afraid (mode 12), dazed (11), mesmerised (10), frenzied
    /// (9) or possessed (<see cref="Mind.FlagPossessed"/>) is given it through its
    /// own <c>SetTarget</c> (<see cref="SetTarget"/>): checked there, and reacted to by a pet that has
    /// not yet noticed anybody. A <c>null</c> is handed on too, so a master who lets his target go
    /// calls his pets off. Nothing checks that the pet is still his or still a pet — the list is
    /// the whole test.
    /// </summary>
    /// <remarks>
    /// The set-target's third argument skips this; no call read passes anything but 0 for it. A pet that is
    /// somebody's master hands the target on again; the depth is capped here (ours), where 1.1 would
    /// recurse without end round a circle of masters that nothing makes.
    /// </remarks>
    internal void PetsFollow(Mind master, WorldThing? asked)
    {
        if (master.Pets is not { } list || following >= MaxPets) return;
        following++;
        try
        {
            foreach (int guid in list.ToArray())
            {
                if (guid == 0 || where?.Thing(guid) is not { Mind: { } pet }) continue;
                if (pet.Mode is AiMode.Afraid or AiMode.Dazed or AiMode.Mesmerized or AiMode.Frenzied
                    || (pet.Flags & Mind.FlagPossessed) != 0) continue;
                SetTarget(pet, asked);
            }
        }
        finally { following--; }
    }

    /// <summary>How deep <see cref="PetsFollow"/> has gone (ours: a guard against a circle of masters).</summary>
    int following;
}
