// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// One explosion: a thing of type 10 (`gameExplosion`) running the events of
// its `.nex` file (its event dispatcher) and dealing its damage.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Explosions;

/// <summary>
/// The explosion half of a type-10 thing (the fields after the thing's own). The thing itself is an
/// ordinary <see cref="WorldThing"/>; its owner is <see cref="WorldThing.SpawnedBy"/>.
/// </summary>
public sealed class Explosion
{
    /// <summary>The thing.</summary>
    public required WorldThing Thing { get; init; }
    /// <summary>Its script, or null when the template names none that loads (the engine asserts; ours sits inert).</summary>
    public NexFile? Script { get; set; }
    /// <summary>When it was made — every event's time is from here (the constructor writes the clock).</summary>
    public int StartMs { get; set; }
    /// <summary>When the next event falls due, absolute; 0 at first, so the first tick looks at once.</summary>
    public int NextMs { get; set; }
    /// <summary>The next event to run.</summary>
    public int Index { get; set; }

    /// <summary>`EXPLOSION_FLAGS` (tag 130; the SDK's `FLAG_EXPLOSION_FLAGS`).</summary>
    public uint Flags { get; set; }
    /// <summary>The material its WALLMARK event stamps (the template's `WALLMARK`, tag 244).</summary>
    public string WallMark { get; set; } = "";
    /// <summary>The damage type SETDAMAGE set, a `DAMAGE_TYPE_*` (0 unless set, and 0 for anything outside 0–9).</summary>
    public int DamageType { get; set; }
    /// <summary>The damage at the edge and at the centre.</summary>
    public int MinDamage { get; set; }
    /// <inheritdoc cref="MinDamage"/>
    public int MaxDamage { get; set; }
    /// <summary>How far it reaches — <see cref="DefaultRadius"/> until SETDAMAGE says.</summary>
    public float DamageRadius { get; set; } = DefaultRadius;
    /// <summary>Milliseconds between pulses of damage, 0 for none.</summary>
    public int PulseMs { get; set; }
    /// <summary>When the next pulse is due — the clock at construction, 0 once SETDAMAGE has set no pulse.</summary>
    public int PulseAt { get; set; }

    /// <summary>The constructor's radius (100.0).</summary>
    public const float DefaultRadius = 100f;

    /// <summary>`EXPLOSION_FLAGS`, as the SDK names them (`noteditordata.txt`, `FLAG_EXPLOSION_FLAGS`).</summary>
    public static class Flag
    {
        /// <summary>Its owner is not hurt (the target's GUID against the owner's).</summary>
        public const uint NoDamageToSelf = 0x1;
        /// <summary>A character friendly to its owner is not hurt, when the owner is a character.</summary>
        public const uint NoDamageToTeam = 0x2;
        /// <summary>No party member (`THING_TYPE_PLAYER`) is hurt.</summary>
        public const uint NoDamageToPlayers = 0x4;
        /// <summary>No other character (`THING_TYPE_ACTOR`) is hurt.</summary>
        public const uint NoDamageToActors = 0x8;
        /// <summary>No container is damaged.</summary>
        public const uint NoDamageToContainers = 0x10;
        /// <summary>No projectile is set off.</summary>
        public const uint NoDamageToProjectiles = 0x20;
        /// <summary>No prop is told.</summary>
        public const uint NoDamageToProps = 0x40;
        /// <summary>It is put in the world square to the world's axes, whatever made it (the finalize).</summary>
        public const uint WorldOrient = 0x80;
    }

    /// <summary>A wall mark it asked for (WALLMARK), which <see cref="Decals"/> stamps.</summary>
    public readonly record struct Mark(string Material, Vector3 At, Vector3 Normal, float Size, int Ms);

    /// <summary>What this explosion has asked to be stamped, for tests and the journal.</summary>
    public List<Mark> Marks { get; } = [];

    /// <summary>Whether it has run its last event and gone.</summary>
    public bool Done { get; set; }
}
