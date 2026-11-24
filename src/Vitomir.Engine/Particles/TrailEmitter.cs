// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The trail kind: the ribbon an arrow or a crossbow bolt draws behind it.
using System.Numerics;

namespace Vitomir.Engine.Particles;


/// <summary>
/// **Trail** (kind 5: <c>arrowTrail</c> on every arrow and bolt, and the blade trails). Every
/// <c>birthrate</c> milliseconds the emitter lays one **segment** — its <c>startpos</c> and
/// <c>endpos</c> turned into the thing's frame, so a line across the thing where it is now — and
/// each segment fades from 1 to 0 over <c>lifespan</c>. Drawn oldest first, consecutive segments
/// are joined into a ribbon of quads, coloured from <c>endcolor</c> to <c>startcolor</c> by life and
/// textured across from start (u 0) to end (u 1) and along by life (v).
///
/// Unlike the particle kinds it lays **at most one segment a frame** and catches up on nothing: the
/// next is due <c>birthrate × particleLOD</c> after now, not after the last one was due.
/// It lays only while it has a thing and, unless its flags have bit 1, only while the thing is
/// attached to a character swinging an attack marked <c>STRIKETRAIL</c> (<see cref="Swinging"/>).
/// Every arrow, bolt, throwing-knife and Celerity block sets the bit; the three without it
/// (<c>bastard</c>, <c>flameTrail</c>, <c>bloodTrail</c>) are made by nothing a shipped script
/// or template does — a weapon's own blade trail (<c>WEAPON_TRAIL</c>, <c>WEAPON_BLADESTART</c>,
/// <c>WEAPON_BLADEEND</c>) is not a particle emitter and is drawn elsewhere (not read).
/// </summary>
public sealed class TrailEmitter : Emitter
{
    /// <summary><c>flags</c> bit 1: lay segments always, not only in a swing.</summary>
    public const uint FlagAlways = 0x1;

    /// <summary>The segments: each one's two ends and its life (0x44 bytes each).</summary>
    public Segment[] Segments { get; }

    /// <summary>One segment: <see cref="Start"/> and <see cref="End"/> in the world, and how much life is left.</summary>
    public struct Segment
    {
        public float Life;
        public Vector3 Start;
        public Vector3 End;
        /// <summary>Set once the slot has been laid.</summary>
        public bool Laid;
    }

    public TrailEmitter(EmitterSettings settings, ParticleSystem system) : base(settings, system) =>
        Segments = new Segment[settings.MaxParticles];

    /// <summary>
    /// The segment due now, if one is: not while dying, not before it is due, not
    /// without a thing, and — without the always bit — not at all here (see the class summary).
    /// </summary>
    void Lay()
    {
        uint now = Now;
        if (Dying || NextBirth > now || Thing is null) return;
        if ((Flags & FlagAlways) == 0 && !Swinging(Thing, now)) return;
        if (!(NextBirth < now)) return;
        ref var seg = ref Segments[NextSlot];
        seg.Start = Settings.StartPos.X * AxisX + Settings.StartPos.Y * AxisY + Settings.StartPos.Z * AxisZ + Origin;
        seg.End = Settings.EndPos.X * AxisX + Settings.EndPos.Y * AxisY + Settings.EndPos.Z * AxisZ + Origin;
        seg.Laid = true;
        seg.Life = 1f;
        NextBirth = (uint)(long)(Settings.BirthRate * ParticleSystem.Lod) + now;
        NextSlot++;
        if (Settings.MaxParticles <= NextSlot) NextSlot = 0;
    }

    /// <summary>
    /// The swing test for a trail without the always bit: its thing is attached to
    /// an actor or a player (type 0 or 7) whose action is a swing
    /// (action 5), who is busy with it (<c>now &lt) — or dead
    /// (flag <c>0x10000000</c>), or restrained (second flag word <c>0x80</c>) — and whose weapon's
    /// current attack (the weapon's selected attack
    /// record) is <c>STRIKETRAIL</c> (flag 0x2000).
    /// </summary>
    public static bool Swinging(World.WorldThing thing, uint now)
    {
        if (thing.AttachedTo is not { Type: World.ThingType.Actor or World.ThingType.Player, Actor: { } a }) return false;
        if (a.Action != Rpg.ActionMode.Swing) return false;
        if (!(now < (uint)a.BusyUntil || a.Dead || a.Has2(World.Actor.Flag2.Restrained))) return false;
        return a.Weapon is { } weapon && weapon.Current.Is(Rpg.WeaponFlag.StrikeTrail);
    }

    /// <summary>The update: the segment due, then every segment faded by dt / lifespan, never below 0.</summary>
    public override bool Update(float dt)
    {
        float span = Settings.Lifespan * 0.001f;
        Lay();
        bool alive = false;
        for (int i = 0; i < Segments.Length; i++)
        {
            ref var s = ref Segments[i];
            if (!(s.Life > 0f)) continue;
            alive = true;
            s.Life -= dt / span;
            if (s.Life < 0f) s.Life = 0f;
        }
        return !Dying || alive;
    }

    /// <summary>A trail has no sprites; see <see cref="CollectRibbon"/>.</summary>
    public override void Collect(List<ParticleSprite> into) { }

    /// <summary>
    /// The draw (the renderer's trail entry): the live segments oldest first — from the
    /// next slot to the end, then from the start — each two consecutive ones a quad.
    /// </summary>
    public override void CollectQuads(List<ParticleQuad> into) => CollectRibbon(into);

    /// <inheritdoc cref="CollectQuads"/>
    public void CollectRibbon(List<ParticleQuad> into)
    {
        Segment? previous = null;
        int n = Segments.Length;
        for (int k = 0; k < n; k++)
        {
            var s = Segments[(int)((NextSlot + k) % n)];
            if (!(s.Life > 0f)) continue;
            if (previous is { } p)
                into.Add(new ParticleQuad(p.Start, p.End, s.End, s.Start, p.Life, s.Life,
                                          Blend(Settings.StartColor, Settings.EndColor, p.Life),
                                          Blend(Settings.StartColor, Settings.EndColor, s.Life)));
            previous = s;
        }
    }
}
