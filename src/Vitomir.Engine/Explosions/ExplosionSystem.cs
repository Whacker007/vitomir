// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The explosions of one place: the `.nex` scripts they run (loaded once by name), the
// explosions alive now, and their tick (the thing's tick → the explosion's step
// → its event dispatcher).
//
// **The public API**:
//
//   var fx = location.Explosions;             // made on first use, one per place
//   fx.Begin(thing)                            // a type-10 thing has entered the world (Location.Enter)
//   fx.Of(thing)                               // its explosion state
//   fx.TickThing(thing, now, rng)              // its tick, from its thing's own tick
//   fx.Script("fireball.nex")                  // a script as the engine read it
//   fx.MuzzleFlash(place, firer, weapon, at)   // the shot's flash
//
// Nothing needs to be told about a projectile's detonation: `Projectiles.Detonate` places the
// template the projectile names, the placement is a thing entering the world, and an explosion
// entering the world begins.
using System.Numerics;
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Explosions;

/// <summary>
/// One place's explosions. Stepped once a frame from <see cref="Simulation"/>; the engine ticks each
/// explosion as part of its thing's tick, so an explosion made this frame is
/// looked at this frame.
/// </summary>
public sealed class ExplosionSystem
{
    readonly ILogger log = Diag.For<ExplosionSystem>();
    readonly Location? location;
    readonly Dictionary<string, NexFile?> scripts = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<WorldThing, Explosion> byThing = new(ReferenceEqualityComparer.Instance);
    readonly List<Explosion> order = [];

    /// <summary>
    /// The engine's <c>rand()</c> as the explosions draw it — a CREATE's chance, random offsets and
    /// distance. **Ours: a stream of its own**, as the particles have, so a
    /// blast's smoke never changes a fight's rolls in a test.
    /// </summary>
    public Ai.Rng Rand { get; } = Ai.Chance.Game?.Explosions ?? new(1);   // a game's: the launch's, carried on

    /// <summary>The explosion scripts the engine keeps at once (the original asserts <c>numExplosions &lt; MAX_EXPLOSIONS</c>, 128).</summary>
    public const int MaxScripts = 128;
    /// <summary>`EXPLOSION_NAME` (129) and `EXPLOSION_FLAGS` (130), which the explosion's own setter takes (modifiers 0x81, 0x82).</summary>
    public const uint TagName = 129, TagFlags = 130;
    /// <summary>`WALLMARK` (244): the material an explosion's WALLMARK event stamps.</summary>
    public const uint TagWallMark = 244;

    /// <summary>The live explosions, oldest first.</summary>
    public IReadOnlyList<Explosion> Live => order;

    /// <summary>The engine's clock at the last step.</summary>
    public int Now { get; private set; }

    /// <summary>The system for a place.</summary>
    public ExplosionSystem(Location location) => this.location = location;

    /// <summary>A system with no place, for tests that hand it scripts.</summary>
    public ExplosionSystem() { }

    /// <summary>An explosion's state, or null when the thing is not a running explosion.</summary>
    public Explosion? Of(WorldThing thing) => byThing.GetValueOrDefault(thing);

    /// <summary>
    /// A script by the name a template gives it: read the first time — from
    /// `misc/Explosions/` (the path the loader registers as kind 0x11, "Misc_Explosions") — and kept,
    /// under the name cut to 31 characters and without regard to case. A file that does
    /// not open, or keeps no events, is not kept and answers null each time it is asked for.
    /// </summary>
    public NexFile? Script(string name)
    {
        string key = name.Length > 31 ? name[..31] : name;
        if (scripts.TryGetValue(key, out var have)) return have;
        if (location?.Files is not { } files) return null;
        if (!files.TryRead("misc/Explosions/" + name, out var data)) { log.LogDebug("no explosion file '{Name}'", name); return null; }
        var templates = location.Templates;
        var nex = NexFile.Read(data, name, templates is null ? null : templates.Has);
        foreach (var d in nex.Dropped) log.LogTrace("{Name}: {Dropped}", name, d);
        if (nex.Events.Count == 0) return null;
        if (scripts.Count >= MaxScripts) log.LogWarning("more than {Max} explosion files (the engine asserts)", MaxScripts);
        scripts[key] = nex;
        return nex;
    }

    /// <summary>Puts a script in the cache under a name, for tests that make their own.</summary>
    public void Define(string name, NexFile nex) => scripts[name] = nex;

    /// <summary>
    /// An explosion thing entering the world. The constructor starts its clock
    /// (its start and its pulse clock both the time now) and the template's two explosion
    /// tags load its script and its flags; the finalize then squares a `WORLD ORIENT`
    /// explosion to the world (the rotation set to the identity). Its template's light is
    /// made now as well, since the engine applies the `DYNA_*` tags as it builds the thing.
    /// </summary>
    public Explosion Begin(WorldThing thing, int? now = null)
    {
        if (byThing.TryGetValue(thing, out var have)) return have;
        int at = now ?? location?.Milliseconds ?? Now;
        var e = new Explosion { Thing = thing, StartMs = at, PulseAt = at };
        if (location?.Templates is { } templates)
        {
            var tags = templates.Resolve(thing.Scene);
            if (tags.TryGetValue(TagName, out var name) && name.AsString() is { Length: > 0 } file) e.Script = Script(file);
            if (tags.TryGetValue(TagFlags, out var flags)) e.Flags = flags.AsUInt();
            if (tags.TryGetValue(TagWallMark, out var mark)) e.WallMark = mark.AsString();
        }
        if ((e.Flags & Explosion.Flag.WorldOrient) != 0) thing.Orientation = Vector3.Zero;
        location?.Lights.Notice(thing);
        byThing[thing] = e;
        order.Add(e);
        if (e.Script is null) log.LogDebug("explosion {Template} #{Guid} has no script and will sit inert", thing.Template, thing.Guid);
        return e;
    }

    /// <summary>Starts an explosion with a script given here, for tests.</summary>
    public Explosion Begin(WorldThing thing, NexFile script, int now, uint flags = 0)
    {
        var e = Begin(thing, now);
        e.Script = script;
        e.Flags = flags;
        return e;
    }

    /// <summary>
    /// Every live explosion ticked at once, oldest first, one made during the walk
    /// included — for a caller with no simulation. <see cref="World.Simulation"/> ticks each from
    /// its own thing's tick instead (<see cref="TickThing"/>), in the world list's order, and a
    /// thing made during that walk first ticks on the next frame.
    /// </summary>
    public void Step(int now, Ai.Rng damageRng)
    {
        Now = now;
        for (int i = 0; i < order.Count; i++)
        {
            var e = order[i];
            if (e.Done) continue;
            if (e.Thing.Removed) { e.Done = true; continue; }
            Tick(e, now, damageRng);
        }
        Prune();
    }

    /// <summary>The clock the explosions' ticks read this frame, set before the world's walk.</summary>
    public void Clock(int now) => Now = now;

    /// <summary>
    /// One explosion thing's own tick (a `THING_TYPE_EXPLOSION`'s), from the
    /// world's walk: nothing for a thing that is not a live explosion.
    /// </summary>
    public void TickThing(WorldThing thing, int now, Ai.Rng damageRng)
    {
        if (!byThing.TryGetValue(thing, out var e) || e.Done) return;
        if (thing.Removed) { e.Done = true; return; }
        Tick(e, now, damageRng);
    }

    /// <summary>The ended explosions let go, and those whose thing has gone, after the walk.</summary>
    public void Prune()
    {
        foreach (var e in order) if (e.Thing.Removed) e.Done = true;
        order.RemoveAll(e => e.Done);
        foreach (var gone in byThing.Where(p => p.Value.Done).Select(p => p.Key).ToArray()) byThing.Remove(gone);
    }

    /// <summary>
    /// A pulse of damage when one is due (a max damage, a pulse clock, and the clock past
    /// it — **strictly**), the clock moved on by the pulse; then, if the next event is due, the
    /// events.
    /// </summary>
    public void Tick(Explosion e, int now, Ai.Rng damageRng)
    {
        if (e.MaxDamage != 0 && e.PulseAt != 0 && (uint)e.PulseAt < (uint)now)
        {
            ExplosionDamage.Deal(e, damageRng, now);
            e.PulseAt += e.PulseMs;
        }
        if ((uint)e.NextMs <= (uint)now) RunDue(e, now, damageRng);
    }

    /// <summary>
    /// Every event whose time from the start has come, in file order; past the last, the
    /// explosion flags itself `NO NET SYNCH` (0x100) and destroys itself — so it lives as
    /// long as its last event's time. Otherwise the next event's time is noted.
    /// </summary>
    void RunDue(Explosion e, int now, Ai.Rng damageRng)
    {
        if (e.Script is not { } script) return;                  // the engine asserts; ours waits
        while (true)
        {
            if (e.Index >= script.Events.Count)
            {
                e.Done = true;
                e.Thing.Flags |= WorldThing.Flag.NoNetSync;
                location?.Remove(e.Thing);
                location?.Lights.Ramps.Forget(e.Thing);
                return;
            }
            var ev = script.Events[e.Index];
            if ((uint)now < (uint)(ev.Time + e.StartMs)) break;
            Dispatch(e, ev, now, damageRng);
            e.Index++;
        }
        e.NextMs = script.Events[e.Index].Time + e.StartMs;
    }

    /// <summary>
    /// The dispatcher, the switch on the event's kind. The light cases
    /// do nothing to a thing with no light, and GAUSS has no case anywhere.
    /// </summary>
    public void Dispatch(Explosion e, NexEvent ev, int now, Ai.Rng damageRng)
    {
        var thing = e.Thing;
        var light = location?.Lights.Of(thing);
        var ramps = location?.Lights.Ramps;
        if (ramps is not null) ramps.Now = now;
        switch (ev.Command)
        {
            case NexCommand.Create:
                Create(e, ev, now);
                break;
            case NexCommand.DynaOn:
                if (light is not null) thing.Flags |= WorldThing.Flag.DynaLight;
                break;
            case NexCommand.DynaOff:
                thing.Flags &= ~WorldThing.Flag.DynaLight;
                break;
            case NexCommand.DynaIntensity:
            case NexCommand.DynaRadius:
                if (light is null) break;
                var field = ev.Command == NexCommand.DynaIntensity ? RampField.Intensity : RampField.Radius;
                if (ev.Mode == NexRamp.Instant)
                {
                    if (field == RampField.Intensity) light.Current = ev.Value; else light.Radius = ev.Value;
                }
                else if (ev.Mode == NexRamp.Ramp) ramps?.Start(thing, light, field, ev.Value, ev.RampMs);
                break;
            case NexCommand.DynaColor:
                if (light is null) break;
                var (r, g, b) = ev.Color;
                if (ev.Mode == NexRamp.Instant) light.Color = new Vector3(r, g, b);
                else if (ev.Mode == NexRamp.Ramp) ramps?.StartColor(thing, light, new Vector3(r, g, b), ev.RampMs);
                break;
            case NexCommand.DynaStyle:
                if (light is not null) light.Style = ev.Style;
                break;
            case NexCommand.DynaMaterial:
                // Only a material that loads replaces the one it has.
                if (light is not null && (location is null || location.Materials.Named(ev.Name) is not null)) light.Material = ev.Name;
                break;
            case NexCommand.SetDamage:
                e.MinDamage = ev.MinDamage;
                e.MaxDamage = ev.MaxDamage;
                e.PulseAt = 0;
                if ((ev.Flags & 1) != 0)
                {
                    e.PulseMs = ev.PulseMs;
                    if (ev.PulseMs != 0) e.PulseAt = now;
                }
                if ((ev.Flags & 2) != 0) e.DamageType = ev.DamageType is >= 0 and <= 9 ? ev.DamageType : 0;
                if ((ev.Flags & 4) != 0) e.DamageRadius = ev.Radius;
                break;
            case NexCommand.ApplyDamage:
                ExplosionDamage.Deal(e, damageRng, now);
                break;
            case NexCommand.ModelAlpha:
                // The render model's alpha, the ramp in milliseconds.
                if (HasModel(thing))
                {
                    if (ev.Mode == NexRamp.Instant) thing.Model3D.SetAlpha(ev.Value, 0f);
                    else if (ev.Mode == NexRamp.Ramp) thing.Model3D.SetAlpha(ev.Value, (uint)ev.RampMs);
                }
                break;
            case NexCommand.ModelScale:
                // The render model's scale, the ramp in seconds (× 0.001).
                if (HasModel(thing))
                {
                    if (ev.Mode == NexRamp.Instant) thing.Model3D.SetScale(ev.Value, 0f);
                    else if (ev.Mode == NexRamp.Ramp) thing.Model3D.SetScale(ev.Value, (uint)ev.RampMs * 0.001f);
                }
                break;
            case NexCommand.WallMark:
                // A decal of the template's WALLMARK material, of this size,
                // for this long, on what lies along the thing's own Z axis from its
                // position. Asked for only with a material, a size and a time — and stamped
                // (`Decals`).
                if (e.WallMark.Length > 0 && ev.MarkSize != 0f && ev.MarkMs != 0)
                {
                    e.Marks.Add(new Explosion.Mark(e.WallMark, thing.Position,
                        Vector3.TransformNormal(Vector3.UnitZ, thing.Rotation), ev.MarkSize, ev.MarkMs));
                    location?.Decals.WallMark(thing, e.WallMark, ev.MarkSize, ev.MarkMs, now);
                }
                break;
        }
    }

    /// <summary>Whether a thing has a render model, which the model events need.</summary>
    static bool HasModel(WorldThing thing) => thing.Model.Length > 0 || thing.Scene.Model is not null;

    /// <summary>
    /// CREATE: <c>repeat</c> times, each passing its chance (no roll at all for
    /// 1.0; otherwise <c>rand × (1/32767) ≤ chance</c>), a thing made from the template:
    /// <list type="number">
    /// <item>the offset, each flagged axis scaled by <c>1 − 2 × rand01</c> (x, y, z in that order,
    /// one draw each), then the distance, if flagged, by <c>rand01</c>;</item>
    /// <item>the offset normalised (a zero offset stays zero), turned by the explosion's matrix
    /// and taken out that distance from its position;</item>
    /// <item>made there with the explosion's own orientation. An explosion with an owner **fires**
    /// it — owned, so a fireball's spray is still the caster's — and a template that is
    /// not a projectile is made, unmade and then made plainly; one with no owner makes it
    /// plainly;</item>
    /// <item>a non-zero <c>timer</c> is its life: its end = now + timer, over whatever its
    /// template's `TIMER` said.</item>
    /// </list>
    /// </summary>
    void Create(Explosion e, NexEvent ev, int now)
    {
        if (location is null) return;
        var thing = e.Thing;
        for (int i = 0; i < ev.Repeat; i++)
        {
            if (ev.Chance != 1f && !(Rand.Unit() <= ev.Chance)) continue;
            var (x, y, z) = ev.Offset;
            float distance = ev.Distance;
            if (ev.RandomAxis(0)) { float u = Rand.Unit(); x = (1f - (u + u)) * x; }
            if (ev.RandomAxis(1)) { float u = Rand.Unit(); y = (1f - (u + u)) * y; }
            if (ev.RandomAxis(2)) { float u = Rand.Unit(); z = (1f - (u + u)) * z; }
            if (ev.RandomDistance) distance = Rand.Unit() * distance;
            var offset = new Vector3(x, y, z);
            float length = offset.Length();
            if (length != 0f) offset *= 1f / length;
            var at = thing.Position + Vector3.TransformNormal(offset, thing.Rotation) * distance;

            WorldThing? made = null;
            if (thing.SpawnedBy != 0)
                made = Rpg.Projectiles.Fire(location, thing, ev.Name, at, thing.Orientation, thing.SpawnedBy, nowMs: now);
            made ??= location.Place(ev.Name, at, thing.Orientation, thing.Sector);
            if (made is not null && ev.Timer != 0) made.ExpireAt = now + ev.Timer;
        }
    }

    /// <summary>
    /// A shot's muzzle flash (from `FireWeapon` after the use
    /// noise): the attack's `WEAPON_MUZZLEFLASH` made at the muzzle with **the shooter's**
    /// orientation (the owner's matrix is copied and puts the shot's translation in it), not
    /// owned by anybody, and flagged `NO NET SYNCH`. A weapon nobody holds, or whose holder is out of
    /// the world, makes none. Every firearm names one and they are all explosions
    /// (`exp_muzzleMagnum` and its kin): a light and a flash particle for a quarter of a second.
    /// </summary>
    public static WorldThing? MuzzleFlash(Location place, WorldThing holder, string template, Vector3 muzzle)
    {
        if (template.Length == 0 || !holder.InWorld || holder.Sector < 0) return null;
        var flash = place.Place(template, muzzle, holder.Orientation, holder.Sector);
        if (flash is not null) flash.Flags |= WorldThing.Flag.NoNetSync;
        return flash;
    }
}
