// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The object kind: an emitter that makes things instead of particles.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Particles;

/// <summary>
/// **Object** (kind 14: <c>firestorm</c>, the Fire Storm discipline's, and <c>objecttest</c>). It
/// makes **things**: every <c>birthrate</c> milliseconds a thing of its <c>template</c>
/// (<c>proj_firestorm</c>) at a point of its emit shape (a disc of 70 round the storm), in the
/// thing's orientation, with <c>lifespan</c> milliseconds to live. It draws nothing itself — what
/// it makes draws itself.
/// </summary>
public sealed class ObjectEmitter : Emitter
{
    public ObjectEmitter(EmitterSettings settings, ParticleSystem system) : base(settings, system) { }

    /// <summary>
    /// Where the things are made. The system sets it to its place; with none (the tests) nothing is
    /// made and <see cref="Made"/> only records the points.
    /// </summary>
    internal Location? Place { get; set; }

    /// <summary>What the last births made, or would have made with no place: the template and where.</summary>
    public List<(string Template, Vector3 At)> Made { get; } = [];

    /// <summary>
    /// The births: with a thing, not dying, a template, and due; a backlog older than a
    /// lifespan forgotten; then while due — a point from the emit shape, the sector there
    /// (**none stops the births** for this frame), the thing made there plainly
    /// in the emitter thing's orientation, its expiry <c>now + lifespan</c>
    /// when there is a lifespan, and the next <c>birthrate</c> on, plus <c>LCG % jitter</c> with a
    /// jitter (the object's is 0).
    /// </summary>
    void Make()
    {
        uint now = Now;
        var s = Settings;
        if (Thing is not { } thing || Dying || s.Template.Length == 0 || NextBirth > now) return;
        if (now - NextBirth > s.Lifespan) NextBirth = now - s.Lifespan;
        while (NextBirth < now)
        {
            var at = EmitPoint();
            if (Place is { } place)
            {
                int sector = place.Solid is { } solid ? solid.SectorAt(at, (int)thing.Sector) : (int)thing.Sector;
                if (sector < 0) return;
                var made = place.Place(s.Template, at, thing.Orientation, sector);
                if (made is null) return;
                if (s.Lifespan != 0) made.ExpireAt = (int)(s.Lifespan + now);
            }
            Made.Add((s.Template, at));
            NextBirth += s.BirthRate;
            if (s.BirthJitter != 0) NextBirth = System.Lcg.Step() % s.BirthJitter + NextBirth;
            // Ours: a birthrate of 0 never ends the loop in 1.1 either; no shipped block has one.
            if (s.BirthRate == 0) break;
        }
    }

    /// <summary>
    /// The update: with no thing, finished; otherwise the births, only while the thing is
    /// in a world and a sector (its world and its sector — the second read as its sector, it
    /// is not in the field map), and never finished.
    /// </summary>
    public override bool Update(float dt)
    {
        if (Thing is null) return false;
        Made.Clear();
        if (Thing.InWorld && Thing.Sector >= 0) Make();
        return true;
    }

    /// <summary>The draw is <c>return 0</c>.</summary>
    public override void Collect(List<ParticleSprite> into) { }
}
