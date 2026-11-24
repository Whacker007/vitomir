// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// An emitter's description: one `game.npd` block turned into the numbers the simulation runs on, the
// way 1.1 turns each kind's.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.Particles;

/// <summary>
/// Where an emitter puts a new particle (the `emitshape` key).
/// The shapes are laid out in the emitter's own frame — the thing's matrix — and the numbers that
/// size them are <c>emitradius</c>, <c>emitheight</c>, <c>emitwidth</c> and <c>emitdir</c>.
/// </summary>
public enum EmitShape
{
    /// <summary>The emitter's own position. What every block without an <c>emitshape</c> gets.</summary>
    Point = 0,
    /// <summary>Anywhere along <c>emitdir</c> from the emitter (one draw, 0..1 of the way).</summary>
    Line = 1,
    /// <summary>A disc of <c>emitradius</c> in the thing's X/Y plane, by rejection (two draws a try).</summary>
    Disc = 2,
    /// <summary>A rectangle ±<c>emitwidth</c> by ±<c>emitheight</c> in the thing's X/Y plane.</summary>
    Rectangle = 3,
    /// <summary>
    /// A ball of <c>emitradius</c> — **around the world's origin, not the emitter**: case 4 of
    /// The shape returns the offset it drew and never adds the position or turns it by the frame.
    /// One shipped emitter (<c>flameSphere</c>) uses it, so its flames appear at the map's origin.
    /// </summary>
    Sphere = 4,
    /// <summary>The disc, raised by 0..<c>emitheight</c> along the thing's Z.</summary>
    Cylinder = 5,
    /// <summary>A box ±<c>emitwidth</c>, ±<c>emitheight</c>, ±<c>emitradius</c> along X, Y and Z.</summary>
    Box = 6,
    /// <summary>A ring of <c>emitradius</c> (an angle out of 360°), raised 0..<c>emitheight</c> along Z.</summary>
    Ring = 7,
}

/// <summary>
/// The numbers one emitter runs on, named by where the engine keeps them (the emitter object's
/// offsets are in each summary). A prototype is built once from its `game.npd` block, and every
/// emitter made from it starts as a copy (every field copied). The defaults are the
/// constructors': the base, then the gas one that fire and flame are built on,
/// then the kind's own (fire, flame).
/// </summary>
public sealed class EmitterSettings
{
    public required EmitterKind Kind { get; init; }
    /// <summary>The block's name (31 characters at most).</summary>
    public required string Name { get; init; }
    /// <summary><c>material</c>: the `.nam` every particle is drawn with.</summary>
    public string Material { get; set; } = "";

    /// <summary><c>emitshape</c> (+8).</summary>
    public EmitShape Shape { get; set; }
    /// <summary><c>emitheight</c>, 1 by default.</summary>
    public float EmitHeight { get; set; } = 1f;
    /// <summary><c>emitwidth</c>, 1 by default.</summary>
    public float EmitWidth { get; set; } = 1f;
    /// <summary><c>emitradius</c>, 1 by default.</summary>
    public float EmitRadius { get; set; } = 1f;
    /// <summary><c>emitdir</c>.</summary>
    public Vector3 EmitDir { get; set; }

    /// <summary>
    /// <c>maxparticles</c>: the size of the particle array, and so how many can be alive at
    /// once. Only fire and flame read the key; otherwise, and when it is 0, it is twice the number a
    /// steady emitter keeps alive — <c>lifespan / birthrate × 2</c> — and 2 for a gas whose particles
    /// die before the next is born (the gas parser's own floor; the flame's has none).
    /// </summary>
    public uint MaxParticles { get; set; }
    /// <summary><c>birthrate</c>: **milliseconds between births**, not births per second. 100 by default.</summary>
    public uint BirthRate { get; set; } = 100;
    /// <summary>
    /// Up to this many milliseconds are added at random to every gap between births: 20 for
    /// gas, fire and flame alike, set by their constructors and read from no key.
    /// </summary>
    public uint BirthJitter { get; set; }
    /// <summary><c>lifespan</c>: how long a particle lives, in milliseconds. 1000 by default.</summary>
    public uint Lifespan { get; set; } = 1000;
    /// <summary>
    /// Up to this many milliseconds are added at random to each particle's life: **1000 for gas
    /// and fire, 0 for flame**, from the constructors. It is why a torch whose block says a 35 ms
    /// lifespan still shows flames living up to a second.
    /// </summary>
    public uint LifeJitter { get; set; }
    /// <summary><c>startsize</c>: the particle's size — half the width of its square. 10 by default.</summary>
    public float StartSize { get; set; } = 10f;
    /// <summary><c>endsize</c>: a flame's size as it dies. 10 by default.</summary>
    public float EndSize { get; set; } = 10f;
    /// <summary><c>growthrate</c>: what a gas particle's size gains a second. Only gas reads it.</summary>
    public float GrowthRate { get; set; }
    /// <summary>
    /// How much bigger than <see cref="StartSize"/> a particle may be born: up to a quarter,
    /// set by the gas constructor.
    /// </summary>
    public float SizeVariance { get; set; }
    /// <summary><c>startcolor</c>, ARGB: the colour of a newborn particle. White by default.</summary>
    public uint StartColor { get; set; } = 0xffffffff;
    /// <summary><c>endcolor</c>, ARGB: the colour it fades to. The gas constructor makes it 0.</summary>
    public uint EndColor { get; set; } = 0xffffffff;
    /// <summary><c>mindist</c>, <c>maxdist</c>: how far from the spawn point a particle starts.</summary>
    public float MinDist { get; set; }
    public float MaxDist { get; set; }
    /// <summary>
    /// <c>minspeed</c>, <c>maxspeed</c>. The <c>minspeed</c> line writes **both**, so a
    /// block that gives only a minimum has no spread — which is what the file's header says it
    /// means, and which also means a <c>maxspeed</c> written above its <c>minspeed</c> is lost.
    /// </summary>
    public float MinSpeed { get; set; }
    public float MaxSpeed { get; set; }
    /// <summary><c>velocity</c>: a flame's launch velocity, in the thing's frame.</summary>
    public Vector3 Velocity { get; set; }
    /// <summary>
    /// How far each component of a flame's launch velocity is scaled at random: by
    /// 1 − v/2 up to 1 + v/2. The gas constructor sets 0.5, and no key reaches it.
    /// </summary>
    public float VelocityVariance { get; set; }
    /// <summary><c>turbulence</c>: the size of one random push given to every particle each frame.</summary>
    public float Turbulence { get; set; }
    /// <summary>
    /// <c>drag</c>: each frame the velocity is scaled by <c>1 − dt × drag × k</c>, with k
    /// 0.001 for gas and 0.01 for fire and flame. The fire and flame constructors set 200.
    /// </summary>
    public float Drag { get; set; }
    /// <summary>
    /// <c>gravity</c>: a pull **down** the world's Z of this many units a second, every
    /// second — so the negative values fire uses make it rise. Fire and flame default to −80.
    /// </summary>
    public float Gravity { get; set; }

    /// <summary>
    /// <c>flags</c>: written straight into the emitter's flag word (<c>sscanf("%x")</c>) — the
    /// word <c>DisableEmitter</c> sets 0x10000 in. Only the trail parser reads the key, and the one
    /// bit it means is 1, "always": the trail lays segments whether or not anyone is swinging.
    /// </summary>
    public uint Flags { get; set; }
    /// <summary><c>startpos</c>, <c>endpos</c>: a trail segment's two ends, in the thing's frame.</summary>
    public Vector3 StartPos { get; set; }
    public Vector3 EndPos { get; set; }

    /// <summary><c>minstartsize</c>, <c>maxstartsize</c> (<c>startsize</c> writes both): a spark's size is drawn between them. 10 by default.</summary>
    public float MinStartSize { get; set; } = 10f;
    public float MaxStartSize { get; set; } = 10f;
    /// <summary>
    /// <c>minbirthrate</c>, <c>maxbirthrate</c> (<c>birthrate</c> writes both): the
    /// milliseconds between a spark emitter's bursts, <c>min + LCG % (max − min)</c>. The sparks
    /// constructor makes both 100 — which, left equal, is a division by zero in 1.1; every shipped
    /// block sets them apart.
    /// </summary>
    public uint MinBirthRate { get; set; } = 100;
    public uint MaxBirthRate { get; set; } = 100;
    /// <summary><c>count</c>: a burst is 1 to <c>count + 1</c> sparks.</summary>
    public int Count { get; set; }
    /// <summary><c>startcolormin</c>, <c>startcolormax</c>: a spark's colour, one draw between them.</summary>
    public uint StartColorMin { get; set; }
    public uint StartColorMax { get; set; }

    /// <summary>
    /// <c>minbirthrate</c>, <c>maxbirthrate</c> as a **spurt** reads them (floats;
    /// <c>birthrate</c> writes both): the milliseconds between one group's drops,
    /// <c>trunc(min + rand × (max − min))</c>. The base constructor makes both 100.
    /// </summary>
    public float MinBirthGap { get; set; } = 100f;
    public float MaxBirthGap { get; set; } = 100f;
    /// <summary><c>numgroups</c>: how many jets a spurt has at once.</summary>
    public uint NumGroups { get; set; }
    /// <summary>
    /// <c>mingroup</c>, <c>maxgroup</c>: how many milliseconds one jet keeps its
    /// place and direction before it is aimed afresh — <c>min + rand × (max − min)</c>, and for the
    /// first aim of all <c>rand × max</c>.
    /// </summary>
    public uint MinGroup { get; set; }
    public uint MaxGroup { get; set; }

    /// <summary><c>length</c>: how many seconds of its fall a rain streak spans; 0 draws squares instead.</summary>
    public float Length { get; set; }
    /// <summary><c>width</c>: a rain streak's width, or a snowflake's size.</summary>
    public float Width { get; set; }

    /// <summary>
    /// <c>rotvel</c>: a shock ring's or a pad's spin, degrees a second. The
    /// parser reads <c>(x,y,z)</c> into x, **z, y** — the second and third
    /// swapped — and the update hands the three on in memory order as roll, pitch and yaw,
    /// so the file's <c>(0,90,0)</c> is a turn about the vertical: <see cref="RotVel"/> holds them in
    /// memory order, (roll, pitch, yaw).
    /// </summary>
    public Vector3 RotVel { get; set; }

    /// <summary>
    /// <c>endcolormin</c>, <c>endcolormax</c>: a magic particle's end colour is drawn between the
    /// pair (<c>endcolor</c> writes both; the file has no other key for them), and a
    /// ring band's top edge ends in a field (<c>endcolor</c> writes it with the colour, <c>endcolormax</c>
    /// alone). A ring's top edge starts in <see cref="StartColorMax"/> the same way.
    /// </summary>
    public uint EndColorMin { get; set; }
    public uint EndColorMax { get; set; }
    /// <summary><c>minradius</c>, <c>maxradius</c> (<c>minradius</c> writes both): how far out a magic particle circles. 10 by default.</summary>
    public float MinRadius { get; set; } = 10f;
    public float MaxRadius { get; set; } = 10f;
    /// <summary><c>mingrowth</c>, <c>maxgrowth</c> (<c>mingrowth</c> writes both): what its circle's radius gains a second.</summary>
    public float MinGrowth { get; set; }
    public float MaxGrowth { get; set; }
    /// <summary><c>startheight</c>, <c>endheight</c>: a ring band's height at birth and at death.</summary>
    public float StartHeight { get; set; }
    public float EndHeight { get; set; }
    /// <summary><c>texturerate</c>: how many times the texture wraps round a ring band.</summary>
    public float TextureRate { get; set; }
    /// <summary><c>numsegs</c>: how many sides a ring band has — asserted 1..63 when one is made.</summary>
    public uint NumSegs { get; set; }

    /// <summary><c>subtype</c>: a beam's wobble — <c>sinus</c> a sine wave, anything else noise.</summary>
    public bool Sinus { get; set; }
    /// <summary><c>size</c>: the length of one of a beam's segments.</summary>
    public float SegmentLength { get; set; }
    /// <summary><c>frequency</c>: a sinus beam's wave, degrees of the wave per unit along it.</summary>
    public float Frequency { get; set; }
    /// <summary><c>updaterate</c>: the milliseconds between a beam's new wobbles.</summary>
    public uint UpdateRate { get; set; }
    /// <summary><c>delay</c>: the milliseconds after it is made before a beam's first birth.</summary>
    public uint Delay { get; set; }

    /// <summary><c>cohesion</c>: how much a liquid group's direction keeps to the thing's Y axis — the random part is weighted <c>1 − cohesion</c>.</summary>
    public float Cohesion { get; set; }
    /// <summary><c>groupcohesion</c>: the chance a liquid group, when its turn comes, sheds a drop.</summary>
    public float GroupCohesion { get; set; }

    /// <summary><c>template</c> (a template handle in 1.1): what an object emitter makes.</summary>
    public string Template { get; set; } = "";

    /// <summary>The prototype for a block: the kind's constructor defaults, then its parser over the lines.</summary>
    public static EmitterSettings From(NpdEmitter block)
    {
        var s = Defaults(block.Kind, block.Name);
        s.Apply(block);
        return s;
    }

    /// <summary>What a kind's constructor leaves in a new emitter before any line is read.</summary>
    public static EmitterSettings Defaults(EmitterKind kind, string name)
    {
        var s = new EmitterSettings { Kind = kind, Name = name };
        if (kind is EmitterKind.Gas or EmitterKind.Fire or EmitterKind.Flame)
        {
            // The gas constructor, which the other two call first.
            s.EndColor = 0;
            s.SizeVariance = 0.25f;
            s.VelocityVariance = 0.5f;
            s.LifeJitter = 1000;
            s.BirthJitter = 20;
        }
        if (kind is EmitterKind.Fire or EmitterKind.Flame)
        {
            // The fire's and the flame's own.
            s.Gravity = -80f;
            s.Drag = 200f;
            if (kind == EmitterKind.Flame) s.LifeJitter = 0;
        }
        if (kind == EmitterKind.Sparks)
        {
            // The kind's own.
            s.StartColor = 0xf0ffffff;
            s.EndColor = 0x40ffffff;
            s.Gravity = -80f;
        }
        if (kind == EmitterKind.Spurt)
        {
            // White, no size spread, startsize 10 (the base already has those two).
            s.StartColor = s.EndColor = 0xffffffff;
        }
        if (kind == EmitterKind.Liquid)
        {
            // White, no size spread, startsize 10.
            s.StartColor = s.EndColor = 0xffffffff;
        }
        if (kind == EmitterKind.Rain)
        {
            // Falling at 100 a second, 490 drops in a box 10000 across.
            s.Velocity = new Vector3(0f, 0f, -100f);
            s.MaxParticles = 490;
            s.StartSize = 10000f;
        }
        if (kind == EmitterKind.Magic)
        {
            // 50 particles, a birth every 100 ms, sizes 10, radius 10.
            s.MaxParticles = 50;
            s.StartSize = s.EndSize = 10f;
            s.Gravity = -80f;
            s.BirthJitter = 20;
            s.LifeJitter = 1000;
            s.SizeVariance = 0.25f;
        }
        if (kind is EmitterKind.Shock or EmitterKind.Pad or EmitterKind.Ring)
        {
            // The shock's (and the pad's and the ring's, which call it first): growth
            // 10 a second, one ring.
            s.GrowthRate = 10f;
            s.Count = 1;
        }
        if (kind == EmitterKind.Trail)
        {
            // The kind's own.
            s.StartPos = new Vector3(10f, 0f, 0f);
            s.EndPos = new Vector3(-10f, 0f, 0f);
        }
        return s;
    }

    /// <summary>
    /// The kind's parser (gas, fire, flame): each line it has a case for,
    /// in file order, then the particle count worked out if no line set it. Keys the kind has no
    /// case for are passed over. The other kinds keep only their material here.
    /// </summary>
    void Apply(NpdEmitter block)
    {
        if (Kind == EmitterKind.Trail) { ApplyTrail(block); return; }
        if (Kind == EmitterKind.Sparks) { ApplySparks(block); return; }
        if (Kind == EmitterKind.Spurt) { ApplySpurt(block); return; }
        if (Kind == EmitterKind.Rain) { ApplyRain(block); return; }
        if (Kind is EmitterKind.Shock or EmitterKind.Pad) { ApplyShock(block); return; }
        if (Kind == EmitterKind.Ring) { ApplyRing(block); return; }
        if (Kind == EmitterKind.Magic) { ApplyMagic(block); return; }
        if (Kind == EmitterKind.Beam) { ApplyBeam(block); return; }
        if (Kind == EmitterKind.Object) { ApplyObject(block); return; }
        if (Kind == EmitterKind.Liquid) { ApplyLiquid(block); return; }
        bool gas = Kind == EmitterKind.Gas, fire = Kind == EmitterKind.Fire, flame = Kind == EmitterKind.Flame;
        foreach (var line in block.Lines)
        {
            var v = line.Value;
            if (v is null) continue;
            switch (line.Key)
            {
                case NpdKey.Material: Material = v.Length > 31 ? v[..31] : v; break;
                case NpdKey.Lifespan when gas || fire || flame: Lifespan = (uint)NpdFile.Int(v); break;
                case NpdKey.StartSize when gas || fire || flame: StartSize = NpdFile.Float(v); break;
                case NpdKey.EndSize when flame: EndSize = NpdFile.Float(v); break;
                case NpdKey.BirthRate when gas || fire || flame: BirthRate = (uint)NpdFile.Int(v); break;
                case NpdKey.MaxParticles when fire || flame: MaxParticles = (uint)NpdFile.Int(v); break;
                case NpdKey.GrowthRate when gas: GrowthRate = NpdFile.Float(v); break;
                case NpdKey.StartColor when gas || fire || flame: if (NpdFile.Hex(v, out var sc)) StartColor = sc; break;
                case NpdKey.EndColor when gas || fire || flame: if (NpdFile.Hex(v, out var ec)) EndColor = ec; break;
                case NpdKey.MinDist when gas || fire || flame: MinDist = NpdFile.Float(v); break;
                case NpdKey.MaxDist when gas || fire || flame: MaxDist = NpdFile.Float(v); break;
                case NpdKey.Turbulence when gas || fire || flame: Turbulence = NpdFile.Float(v); break;
                case NpdKey.Gravity when gas || fire || flame: Gravity = NpdFile.Float(v); break;
                case NpdKey.Drag when gas || fire || flame: Drag = NpdFile.Float(v); break;
                case NpdKey.Velocity when flame:
                {
                    var (x, y, z) = (Velocity.X, Velocity.Y, Velocity.Z);
                    NpdFile.Vector(v, ref x, ref y, ref z);
                    Velocity = new Vector3(x, y, z);
                    break;
                }
                case NpdKey.MinSpeed when gas || fire || flame: MinSpeed = MaxSpeed = NpdFile.Float(v); break;
                case NpdKey.MaxSpeed when gas || fire || flame: MaxSpeed = NpdFile.Float(v); break;
                case NpdKey.EmitShape when gas || fire || flame: Shape = (EmitShape)NpdFile.Int(v); break;
                case NpdKey.EmitRadius when gas || fire || flame: EmitRadius = NpdFile.Float(v); break;
                case NpdKey.EmitHeight when gas || fire || flame: EmitHeight = NpdFile.Float(v); break;
                case NpdKey.EmitWidth when gas || fire || flame: EmitWidth = NpdFile.Float(v); break;
                case NpdKey.EmitDir when gas || fire || flame:
                {
                    var (x, y, z) = (EmitDir.X, EmitDir.Y, EmitDir.Z);
                    NpdFile.Vector(v, ref x, ref y, ref z);
                    EmitDir = new Vector3(x, y, z);
                    break;
                }
            }
        }
        if (MaxParticles != 0 || !(gas || fire || flame)) return;
        // Ours: a birthrate of 0 would divide by zero in the engine; no shipped block has one.
        if (BirthRate == 0) { MaxParticles = 2; return; }
        if ((gas || fire) && Lifespan <= BirthRate) MaxParticles = 2;
        else MaxParticles = Lifespan / BirthRate << 1;
    }

    /// <summary>
    /// The trail's parser: material, lifespan, birthrate, the two colours, the two ends and
    /// the flags; then the segment count — twice the segments a lifespan holds, or 2 when a segment
    /// outlives the gap to the next (note: <c>&lt;</c> here where the gas parser has <c>&lt;=</c>) —
    /// **held to 300**.
    /// </summary>
    void ApplyTrail(NpdEmitter block)
    {
        foreach (var line in block.Lines)
        {
            var v = line.Value;
            if (v is null) continue;
            switch (line.Key)
            {
                case NpdKey.Material: Material = v.Length > 31 ? v[..31] : v; break;
                case NpdKey.Lifespan: Lifespan = (uint)NpdFile.Int(v); break;
                case NpdKey.BirthRate: BirthRate = (uint)NpdFile.Int(v); break;
                case NpdKey.StartColor: if (NpdFile.Hex(v, out var sc)) StartColor = sc; break;
                case NpdKey.EndColor: if (NpdFile.Hex(v, out var ec)) EndColor = ec; break;
                case NpdKey.Flags: if (NpdFile.Hex(v, out var f)) Flags = f; break;
                case NpdKey.StartPos: StartPos = Vec(v, StartPos); break;
                case NpdKey.EndPos: EndPos = Vec(v, EndPos); break;
            }
        }
        if (MaxParticles == 0)
            MaxParticles = BirthRate != 0 && BirthRate < Lifespan ? Lifespan / BirthRate << 1 : 2;
        if (MaxParticles > MaxTrailSegments) MaxParticles = MaxTrailSegments;
    }

    /// <summary>
    /// The sparks' parser. Its particle count falls back on the base <c>birthrate</c>,
    /// which no sparks key writes — so it is <c>lifespan / 100 × 2</c>; every shipped block
    /// gives <c>maxparticles</c>.
    /// </summary>
    void ApplySparks(NpdEmitter block)
    {
        foreach (var line in block.Lines)
        {
            var v = line.Value;
            if (v is null) continue;
            switch (line.Key)
            {
                case NpdKey.Material: Material = v.Length > 31 ? v[..31] : v; break;
                case NpdKey.Lifespan: Lifespan = (uint)NpdFile.Int(v); break;
                case NpdKey.StartSize: MinStartSize = MaxStartSize = NpdFile.Float(v); break;
                case NpdKey.MinStartSize: MinStartSize = NpdFile.Float(v); break;
                case NpdKey.MaxStartSize: MaxStartSize = NpdFile.Float(v); break;
                case NpdKey.BirthRate: MinBirthRate = MaxBirthRate = (uint)NpdFile.Int(v); break;
                case NpdKey.MinBirthRate: MinBirthRate = (uint)NpdFile.Int(v); break;
                case NpdKey.MaxBirthRate: MaxBirthRate = (uint)NpdFile.Int(v); break;
                case NpdKey.MaxParticles: MaxParticles = (uint)NpdFile.Int(v); break;
                case NpdKey.Count: Count = NpdFile.Int(v); break;
                case NpdKey.StartColorMin: if (NpdFile.Hex(v, out var lo)) StartColorMin = lo; break;
                case NpdKey.StartColorMax: if (NpdFile.Hex(v, out var hi)) StartColorMax = hi; break;
                case NpdKey.Gravity: Gravity = NpdFile.Float(v); break;
                case NpdKey.Velocity: Velocity = Vec(v, Velocity); break;
                case NpdKey.VelocityVar: VelocityVariance = NpdFile.Float(v); break;
                case NpdKey.MinSpeed: MinSpeed = NpdFile.Float(v); break;
                case NpdKey.MaxSpeed: MaxSpeed = NpdFile.Float(v); break;
                case NpdKey.EmitShape: Shape = (EmitShape)NpdFile.Int(v); break;
                case NpdKey.EmitRadius: EmitRadius = NpdFile.Float(v); break;
                case NpdKey.EmitHeight: EmitHeight = NpdFile.Float(v); break;
                case NpdKey.EmitWidth: EmitWidth = NpdFile.Float(v); break;
                case NpdKey.EmitDir: EmitDir = Vec(v, EmitDir); break;
            }
        }
        if (MaxParticles != 0) return;
        MaxParticles = Lifespan <= BirthRate || BirthRate == 0 ? 2 : Lifespan / BirthRate << 1;
    }

    /// <summary>
    /// The spurt's parser. Its keys differ from the gas's in three ways that matter:
    /// <c>birthrate</c> goes to the float pair rather than the whole number;
    /// <c>minspeed</c> writes **only** the minimum (<c>speed</c> writes both); and <c>startsize</c>
    /// writes the minimum and maximum size. The particle count, when no <c>maxparticles</c> line gives
    /// it, is worked from the untouched base <c>birthrate</c> (100): <c>numgroups × 2</c> when
    /// <c>lifespan ≤ 100</c>, else <c>lifespan / 100 × numgroups × 2</c> — every shipped block gives it.
    /// </summary>
    void ApplySpurt(NpdEmitter block)
    {
        foreach (var line in block.Lines)
        {
            var v = line.Value;
            if (v is null) continue;
            switch (line.Key)
            {
                case NpdKey.Material: Material = v.Length > 31 ? v[..31] : v; break;
                case NpdKey.Lifespan: Lifespan = (uint)NpdFile.Int(v); break;
                case NpdKey.StartSize: MinStartSize = MaxStartSize = NpdFile.Float(v); break;
                case NpdKey.MinStartSize: MinStartSize = NpdFile.Float(v); break;
                case NpdKey.MaxStartSize: MaxStartSize = NpdFile.Float(v); break;
                case NpdKey.BirthRate: MinBirthGap = MaxBirthGap = NpdFile.Float(v); break;
                case NpdKey.MinBirthRate: MinBirthGap = NpdFile.Float(v); break;
                case NpdKey.MaxBirthRate: MaxBirthGap = NpdFile.Float(v); break;
                case NpdKey.MaxParticles: MaxParticles = (uint)NpdFile.Int(v); break;
                case NpdKey.StartColor: if (NpdFile.Hex(v, out var sc)) StartColor = sc; break;
                case NpdKey.Gravity: Gravity = NpdFile.Float(v); break;
                case NpdKey.Velocity: Velocity = Vec(v, Velocity); break;
                case NpdKey.VelocityVar: VelocityVariance = NpdFile.Float(v); break;
                case NpdKey.NumGroups: NumGroups = (uint)NpdFile.Int(v); break;
                case NpdKey.MinGroup: MinGroup = (uint)NpdFile.Int(v); break;
                case NpdKey.MaxGroup: MaxGroup = (uint)NpdFile.Int(v); break;
                case NpdKey.Speed: MinSpeed = MaxSpeed = NpdFile.Float(v); break;
                case NpdKey.MinSpeed: MinSpeed = NpdFile.Float(v); break;
                case NpdKey.MaxSpeed: MaxSpeed = NpdFile.Float(v); break;
                case NpdKey.EmitShape: Shape = (EmitShape)NpdFile.Int(v); break;
                case NpdKey.EmitRadius: EmitRadius = NpdFile.Float(v); break;
                case NpdKey.EmitHeight: EmitHeight = NpdFile.Float(v); break;
                case NpdKey.EmitWidth: EmitWidth = NpdFile.Float(v); break;
                case NpdKey.EmitDir: EmitDir = Vec(v, EmitDir); break;
            }
        }
        if (MaxParticles != 0) return;
        MaxParticles = Lifespan <= BirthRate || BirthRate == 0 ? NumGroups << 1 : Lifespan / BirthRate * NumGroups * 2;
    }

    /// <summary>
    /// The rain's parser: material, <c>startsize</c> (the side of the box the drops fall
    /// in), <c>birthrate</c>, <c>maxparticles</c>, <c>startcolor</c>, <c>turbulence</c>,
    /// <c>velocity</c>, <c>velocityvar</c> (the most a drop may drift sideways), <c>length</c> and
    /// <c>width</c>. No count is worked out: the constructor's 490 stands unless the block says.
    /// </summary>
    void ApplyRain(NpdEmitter block)
    {
        foreach (var line in block.Lines)
        {
            var v = line.Value;
            if (v is null) continue;
            switch (line.Key)
            {
                case NpdKey.Material: Material = v.Length > 31 ? v[..31] : v; break;
                case NpdKey.StartSize: StartSize = NpdFile.Float(v); break;
                case NpdKey.BirthRate: BirthRate = (uint)NpdFile.Int(v); break;
                case NpdKey.MaxParticles: MaxParticles = (uint)NpdFile.Int(v); break;
                case NpdKey.StartColor: if (NpdFile.Hex(v, out var sc)) StartColor = sc; break;
                case NpdKey.Turbulence: Turbulence = NpdFile.Float(v); break;
                case NpdKey.Velocity: Velocity = Vec(v, Velocity); break;
                case NpdKey.VelocityVar: VelocityVariance = NpdFile.Float(v); break;
                case NpdKey.Length: Length = NpdFile.Float(v); break;
                case NpdKey.Width: Width = NpdFile.Float(v); break;
            }
        }
    }

    /// <summary>
    /// The shock's parser, which the pad uses too (it shares the routine):
    /// material, lifespan, <c>startsize</c>, <c>endsize</c>, <c>birthrate</c>, <c>maxparticles</c>,
    /// <c>count</c>, <c>growthrate</c>, the two colours, <c>velocity</c> and <c>rotvel</c> (read with
    /// its y and z swapped, <see cref="RotVel"/>). No <c>maxparticles</c> means 20.
    /// </summary>
    void ApplyShock(NpdEmitter block)
    {
        foreach (var line in block.Lines)
        {
            var v = line.Value;
            if (v is null) continue;
            switch (line.Key)
            {
                case NpdKey.Material: Material = v.Length > 31 ? v[..31] : v; break;
                case NpdKey.Lifespan: Lifespan = (uint)NpdFile.Int(v); break;
                case NpdKey.StartSize: StartSize = NpdFile.Float(v); break;
                case NpdKey.EndSize: EndSize = NpdFile.Float(v); break;
                case NpdKey.BirthRate: BirthRate = (uint)NpdFile.Int(v); break;
                case NpdKey.MaxParticles: MaxParticles = (uint)NpdFile.Int(v); break;
                case NpdKey.Count: Count = NpdFile.Int(v); break;
                case NpdKey.GrowthRate: GrowthRate = NpdFile.Float(v); break;
                case NpdKey.StartColor: if (NpdFile.Hex(v, out var sc)) StartColor = sc; break;
                case NpdKey.EndColor: if (NpdFile.Hex(v, out var ec)) EndColor = ec; break;
                case NpdKey.Velocity: Velocity = Vec(v, Velocity); break;
                case NpdKey.RotVel:
                {
                    // sscanf("(%f,%f,%f)") into x, z, y.
                    var (x, z, y) = (RotVel.X, RotVel.Z, RotVel.Y);
                    NpdFile.Vector(v, ref x, ref z, ref y);
                    RotVel = new Vector3(x, y, z);
                    break;
                }
            }
        }
        if (MaxParticles == 0) MaxParticles = ShockDefaultCount;
    }

    /// <summary>
    /// The ring's parser: the shock's keys, and <c>startcolormax</c>/<c>endcolormax</c>
    /// for the band's top edge (<c>startcolor</c>/<c>endcolor</c> write both edges), the band's
    /// <c>startheight</c>, <c>endheight</c>, <c>texturerate</c> and <c>numsegs</c>, and
    /// <c>flags</c>. No <c>maxparticles</c> means 20; no <c>endsize</c> is read.
    /// </summary>
    void ApplyRing(NpdEmitter block)
    {
        foreach (var line in block.Lines)
        {
            var v = line.Value;
            if (v is null) continue;
            switch (line.Key)
            {
                case NpdKey.Material: Material = v.Length > 31 ? v[..31] : v; break;
                case NpdKey.Lifespan: Lifespan = (uint)NpdFile.Int(v); break;
                case NpdKey.StartSize: StartSize = NpdFile.Float(v); break;
                case NpdKey.BirthRate: BirthRate = (uint)NpdFile.Int(v); break;
                case NpdKey.MaxParticles: MaxParticles = (uint)NpdFile.Int(v); break;
                case NpdKey.Count: Count = NpdFile.Int(v); break;
                case NpdKey.GrowthRate: GrowthRate = NpdFile.Float(v); break;
                case NpdKey.StartColor: if (NpdFile.Hex(v, out var sc)) StartColor = StartColorMax = sc; break;
                case NpdKey.StartColorMax: if (NpdFile.Hex(v, out var sm)) StartColorMax = sm; break;
                case NpdKey.EndColor: if (NpdFile.Hex(v, out var ec)) EndColor = EndColorMax = ec; break;
                case NpdKey.EndColorMax: if (NpdFile.Hex(v, out var em)) EndColorMax = em; break;
                case NpdKey.Velocity: Velocity = Vec(v, Velocity); break;
                case NpdKey.RotVel:
                {
                    var (x, z, y) = (RotVel.X, RotVel.Z, RotVel.Y);
                    NpdFile.Vector(v, ref x, ref z, ref y);
                    RotVel = new Vector3(x, y, z);
                    break;
                }
                case NpdKey.TextureRate: TextureRate = NpdFile.Float(v); break;
                case NpdKey.StartHeight: StartHeight = NpdFile.Float(v); break;
                case NpdKey.EndHeight: EndHeight = NpdFile.Float(v); break;
                case NpdKey.NumSegs: NumSegs = (uint)NpdFile.Int(v); break;
                case NpdKey.Flags: if (NpdFile.Hex(v, out var f)) Flags = f; break;
            }
        }
        if (MaxParticles == 0) MaxParticles = ShockDefaultCount;
    }

    /// <summary>
    /// The magic's parser: material, lifespan, <c>startsize</c> (both sizes),
    /// <c>endsize</c>, <c>birthrate</c> (the float pair), <c>maxparticles</c>,
    /// <c>mingrowth</c>/<c>maxgrowth</c>, <c>startcolor</c> and <c>endcolor</c> (each into its
    /// min and max), <c>velocity</c>, <c>minspeed</c>/<c>maxspeed</c> (the spin, degrees a second),
    /// <c>minradius</c>/<c>maxradius</c> and <c>flags</c>. The first of each pair writes both.
    /// </summary>
    void ApplyMagic(NpdEmitter block)
    {
        foreach (var line in block.Lines)
        {
            var v = line.Value;
            if (v is null) continue;
            switch (line.Key)
            {
                case NpdKey.Material: Material = v.Length > 31 ? v[..31] : v; break;
                case NpdKey.Lifespan: Lifespan = (uint)NpdFile.Int(v); break;
                case NpdKey.StartSize: StartSize = EndSize = NpdFile.Float(v); break;
                case NpdKey.EndSize: EndSize = NpdFile.Float(v); break;
                case NpdKey.BirthRate: MinBirthGap = MaxBirthGap = NpdFile.Float(v); break;
                case NpdKey.MaxParticles: MaxParticles = (uint)NpdFile.Int(v); break;
                case NpdKey.MinGrowth: MinGrowth = MaxGrowth = NpdFile.Float(v); break;
                case NpdKey.MaxGrowth: MaxGrowth = NpdFile.Float(v); break;
                case NpdKey.StartColor: if (NpdFile.Hex(v, out var sc)) StartColorMin = StartColorMax = sc; break;
                case NpdKey.EndColor: if (NpdFile.Hex(v, out var ec)) EndColorMin = EndColorMax = ec; break;
                case NpdKey.Velocity: Velocity = Vec(v, Velocity); break;
                case NpdKey.MinSpeed: MinSpeed = MaxSpeed = NpdFile.Float(v); break;
                case NpdKey.MaxSpeed: MaxSpeed = NpdFile.Float(v); break;
                case NpdKey.MinRadius: MinRadius = MaxRadius = NpdFile.Float(v); break;
                case NpdKey.MaxRadius: MaxRadius = NpdFile.Float(v); break;
                case NpdKey.Flags: if (NpdFile.Hex(v, out var f)) Flags = f; break;
            }
        }
    }

    /// <summary>
    /// The beam's parser: <c>subtype</c>, material, lifespan, <c>startsize</c>,
    /// <c>endsize</c>, <c>birthrate</c>, <c>count</c> (how many beams, 0 for no end),
    /// <c>size</c> (a segment's length), the two colours, <c>turbulence</c> (how far the wobble
    /// goes), <c>updaterate</c>, <c>delay</c>, <c>texturerate</c> and <c>frequency</c>; then the
    /// count of beams alive at once, <c>lifespan / birthrate × 2</c> or 2 (no <c>maxparticles</c>
    /// case).
    /// </summary>
    void ApplyBeam(NpdEmitter block)
    {
        foreach (var line in block.Lines)
        {
            var v = line.Value;
            if (v is null) continue;
            switch (line.Key)
            {
                case NpdKey.Subtype: Sinus = v.Equals("sinus", StringComparison.OrdinalIgnoreCase); break;
                case NpdKey.Material: Material = v.Length > 31 ? v[..31] : v; break;
                case NpdKey.Lifespan: Lifespan = (uint)NpdFile.Int(v); break;
                case NpdKey.StartSize: StartSize = NpdFile.Float(v); break;
                case NpdKey.EndSize: EndSize = NpdFile.Float(v); break;
                case NpdKey.BirthRate: BirthRate = (uint)NpdFile.Int(v); break;
                case NpdKey.Count: Count = NpdFile.Int(v); break;
                case NpdKey.Size: SegmentLength = NpdFile.Float(v); break;
                case NpdKey.StartColor: if (NpdFile.Hex(v, out var sc)) StartColor = sc; break;
                case NpdKey.EndColor: if (NpdFile.Hex(v, out var ec)) EndColor = ec; break;
                case NpdKey.Turbulence: Turbulence = NpdFile.Float(v); break;
                case NpdKey.UpdateRate: UpdateRate = (uint)NpdFile.Int(v); break;
                case NpdKey.Delay: Delay = (uint)NpdFile.Int(v); break;
                case NpdKey.TextureRate: TextureRate = NpdFile.Float(v); break;
                case NpdKey.Frequency: Frequency = NpdFile.Float(v); break;
            }
        }
        if (MaxParticles == 0)
            MaxParticles = BirthRate != 0 && BirthRate < Lifespan ? Lifespan / BirthRate << 1 : 2;
    }

    /// <summary>
    /// The object's parser: <c>lifespan</c>, <c>birthrate</c>, the emit shape's keys and
    /// <c>template</c> — no material. The count is <c>lifespan / birthrate × 2</c>, unused.
    /// </summary>
    void ApplyObject(NpdEmitter block)
    {
        foreach (var line in block.Lines)
        {
            var v = line.Value;
            if (v is null) continue;
            switch (line.Key)
            {
                case NpdKey.Lifespan: Lifespan = (uint)NpdFile.Int(v); break;
                case NpdKey.BirthRate: BirthRate = (uint)NpdFile.Int(v); break;
                case NpdKey.EmitShape: Shape = (EmitShape)NpdFile.Int(v); break;
                case NpdKey.EmitRadius: EmitRadius = NpdFile.Float(v); break;
                case NpdKey.EmitHeight: EmitHeight = NpdFile.Float(v); break;
                case NpdKey.EmitWidth: EmitWidth = NpdFile.Float(v); break;
                case NpdKey.EmitDir: EmitDir = Vec(v, EmitDir); break;
                case NpdKey.Template: Template = v; break;
            }
        }
        if (MaxParticles == 0 && BirthRate != 0) MaxParticles = Lifespan / BirthRate << 1;
    }

    /// <summary>
    /// The liquid's parser: material, lifespan, <c>startsize</c>, <c>birthrate</c>, the two
    /// colours, <c>mindist</c>/<c>maxdist</c>, <c>gravity</c>, <c>numgroups</c>, <c>mingroup</c>,
    /// <c>maxgroup</c>, <c>groupcohesion</c>, <c>cohesion</c> and the speeds — <c>speed</c> and
    /// <c>minspeed</c> alike write both, <c>maxspeed</c> the maximum. No <c>maxparticles</c> case:
    /// <c>lifespan / birthrate × 2</c>, or 2.
    /// </summary>
    void ApplyLiquid(NpdEmitter block)
    {
        foreach (var line in block.Lines)
        {
            var v = line.Value;
            if (v is null) continue;
            switch (line.Key)
            {
                case NpdKey.Material: Material = v.Length > 31 ? v[..31] : v; break;
                case NpdKey.Lifespan: Lifespan = (uint)NpdFile.Int(v); break;
                case NpdKey.StartSize: StartSize = NpdFile.Float(v); break;
                case NpdKey.BirthRate: BirthRate = (uint)NpdFile.Int(v); break;
                case NpdKey.StartColor: if (NpdFile.Hex(v, out var sc)) StartColor = sc; break;
                case NpdKey.EndColor: if (NpdFile.Hex(v, out var ec)) EndColor = ec; break;
                case NpdKey.MinDist: MinDist = NpdFile.Float(v); break;
                case NpdKey.MaxDist: MaxDist = NpdFile.Float(v); break;
                case NpdKey.Gravity: Gravity = NpdFile.Float(v); break;
                case NpdKey.NumGroups: NumGroups = (uint)NpdFile.Int(v); break;
                case NpdKey.MinGroup: MinGroup = (uint)NpdFile.Int(v); break;
                case NpdKey.MaxGroup: MaxGroup = (uint)NpdFile.Int(v); break;
                case NpdKey.Cohesion: Cohesion = NpdFile.Float(v); break;
                case NpdKey.GroupCohesion: GroupCohesion = NpdFile.Float(v); break;
                case NpdKey.Speed: case NpdKey.MinSpeed: MinSpeed = MaxSpeed = NpdFile.Float(v); break;
                case NpdKey.MaxSpeed: MaxSpeed = NpdFile.Float(v); break;
            }
        }
        if (MaxParticles == 0)
            MaxParticles = BirthRate == 0 || Lifespan <= BirthRate ? 2 : Lifespan / BirthRate << 1;
    }

    /// <summary>A shock's or a pad's ring count when the block gives none.</summary>
    public const uint ShockDefaultCount = 20;

    /// <summary>The most segments a trail keeps (<c>299 &lt; n</c> → 300).</summary>
    public const uint MaxTrailSegments = 300;

    static Vector3 Vec(string v, Vector3 was)
    {
        var (x, y, z) = (was.X, was.Y, was.Z);
        NpdFile.Vector(v, ref x, ref y, ref z);
        return new Vector3(x, y, z);
    }

    /// <summary>A copy, which is what making an emitter from a prototype starts with.</summary>
    public EmitterSettings Clone() => (EmitterSettings)MemberwiseClone();
}
