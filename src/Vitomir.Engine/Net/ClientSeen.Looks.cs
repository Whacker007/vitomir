// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a player's machine makes for itself from the host's blows, deaths, gib kills and beheadings.
// The host's side is `MadeHere`.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

public sealed partial class ClientSeen
{
    /// <summary>
    /// This machine's own no-gore setting (1.1's detail option bit 2), which decides what it makes of the host's
    /// blood and gore: null is the process's own <see cref="Gore.NoBlood"/>, which on a real player's machine it
    /// is. **Ours**, for one process playing several machines (the tests): each machine's is set here, and put in
    /// <see cref="Gore.NoBlood"/> while this machine makes its things.
    /// </summary>
    public bool? NoBlood { get; set; }

    /// <summary>A thing this machine made for itself from the host's blow, death, gib kill or beheading.</summary>
    public event Action<WorldThing>? Made;

    /// <summary>
    /// What a blow or a death makes on this machine. A blow that leaves the character standing is dealt here
    /// again for its looks (the blow dealt with its looks-only argument, which skips the rules): the amount off
    /// its health — which the host's next snapshot sets to the host's, the host deciding every blow — and the hit
    /// reaction (<see cref="HitReaction.Shown"/>): the hurt noises, the loose chunk, the sparks, the flames, the
    /// frost, the blood. The frenzy the host adds (`ef_increasefrenzy`) is refused on a player's
    /// machine (not from the network), as is anything the wound ladder puts on; this machine
    /// has no minds to tell. A death (0xf, the death forced) — whose noises and message line
    /// <see cref="Blow"/> gives — makes the death effects' looks: the killing blow's chunks
    /// and, for a body left whole, not dust and not gibbed, the killing blow's wound effects; the
    /// animation comes with the snapshot and the dust's bones and smoke are sent. A gibbed body is gone by now
    /// (<see cref="Gibbed"/> came before its removal), so, as on 1.1's player's machine, whose 0xf then finds no
    /// thing, it cries nothing and shows nothing more. Before the looks, what the death does first on a player's
    /// machine: the scripts that captured the body hear `killed` (an effect's script here, its wearer's
    /// death) and the weapon goes out of sight (unless `WEAPONOVERRIDE` or `WEAPONNOHANDS`, and again at
    /// the head of the death effects, unconditionally), until a revive shows it again (<see cref="Revived"/>).
    /// </summary>
    void Looks(SeenBlow b, WorldThing? victim, WorldThing? attacker)
    {
        if (victim is not { Actor: { } actor } || !victim.InWorld) return;
        if (!b.Killed && b.Amount <= 0f) return;                                     // a blow's test; a death's message has no such test
        int now = Place.Milliseconds;
        bool was = Gore.NoBlood;
        if (NoBlood is { } own) Gore.NoBlood = own;
        try
        {
            int before = Place.NextGuid;
            if (!b.Killed)
            {
                actor.Health -= b.Amount;
                HitReaction.Shown(victim, actor, attacker, b.Kind, b.Type, b.Amount, dice, now);
            }
            else
            {
                Place.Killed?.Invoke(victim, attacker);                              // The captures
                actor.WeaponHidden = true;                                           // the death hides it
                Damage.SprayOnDeath(victim, actor, b.Kind, b.Amount, dice);
                if (b.Whole && !b.Dusted && !Damage.Gibs(actor, b.Amount))
                    WoundEffects.Show(victim, attacker, b.Type, b.Amount, dice, now, death: true);
            }
            Tell(before);
        }
        finally { Gore.NoBlood = was; }
    }

    /// <summary>
    /// The host's gib kill (message 0x1f with the action 0x1b, on a player's machine): the
    /// sixteen chunks this machine throws itself — `gib_&lt;class&gt;_0` … `_7`, each twice, off a random bone
    /// (<see cref="Gore.Spray"/> with no tag) — or **none under this machine's own no-gore setting**,
    /// whatever the host's; and the body taken out — here as the host's removal takes a thing out
    /// (<see cref="WorldMirror.Removed"/>), which then finds it gone. Its scatter is this machine's own, so the
    /// chunks lie differently on every machine, as in 1.1.
    /// </summary>
    void Gibbed(int guid)
    {
        if (mirror!.Find(guid) is not { Actor: { } actor } body || !body.InWorld) return;
        bool was = Gore.NoBlood;
        if (NoBlood is { } own) Gore.NoBlood = own;
        try
        {
            int before = Place.NextGuid;
            if (!Gore.NoBlood)
                for (int piece = 0; piece < Rules.GameRules.Current.Stake.GibPieces; piece++)
                    for (int again = 0; again < 2; again++)
                        Gore.Spray(body, Damage.GibTemplate(actor.GibClass, piece), Gore.NoTag, dice);
            Tell(before);
        }
        finally { Gore.NoBlood = was; }
        mirror.Removed(guid);
        Log?.Invoke($"net: thing {guid} gibbed here");
    }

    /// <summary>
    /// The host's beheading (0x1f with the action 0x19, on a player's machine): this
    /// machine strikes the head off itself — the head made at its own copy's head bone with its own jitter, the
    /// body headless, `THING_AF2_ISBEHEADED`, the neck's blood — under its own no-gore setting, with which the
    /// head stays on (<see cref="Beheading.Behead"/> told).
    /// </summary>
    void Beheaded(int guid, int attackerGuid)
    {
        if (mirror!.Find(guid) is not { Actor: not null } victim) return;
        var attacker = attackerGuid != 0 ? mirror.Find(attackerGuid) : null;
        bool was = Gore.NoBlood;
        if (NoBlood is { } own) Gore.NoBlood = own;
        try
        {
            int before = Place.NextGuid;
            Beheading.Behead(victim, attacker, dice, Place.Milliseconds, told: true);
            Tell(before);
        }
        finally { Gore.NoBlood = was; }
    }

    /// <summary>
    /// The host's revive (message 0x20; the revive forced, with the noise): this machine revives its own copy
    /// (<see cref="SeenRevive.Shown"/>) — the weapon the death hid shown again.
    /// </summary>
    void Revived(int guid, float health, float blood)
    {
        if (mirror!.Find(guid) is not { Actor: not null } body) return;
        SeenRevive.Shown(body, health, blood);
        Log?.Invoke($"net: thing {guid} revived here");
    }

    /// <summary>
    /// Tells <see cref="Made"/> of every thing this machine made since its counter stood at <paramref name="before"/>:
    /// numbered by this machine's own counter meanwhile (the host's things here carry the host's numbers, 0x40000000 set).
    /// </summary>
    void Tell(int before)
    {
        int after = Place.NextGuid;
        if (after > before) Log?.Invoke($"net: {after - before} made here ({string.Join(", ", Place.Things.Where(t => t.Guid >= before && t.Guid < after).Select(t => t.Template).Distinct())})");
        if (Made is null) return;
        foreach (var t in Place.Things.ToArray())
            if (t.Guid >= before && t.Guid < after) Made(t);
    }
}
