// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ported from the character models' reference simulator for swing bones (swing.py; ours, under this file's licence) and the capsules
// and refit of their swing bones' kit (swingbones.py: capsule_ends, capsule_size, tests, fit_spec). The method, its rules
// and their reasons are the character models'; the frames, the scalar arithmetic and the state's layout are ours.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.People;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// <b>the swing bones' simulator</b> for one kind of person: the character models' <c>swing.py</c> ported (VRM's spring bone
/// made independent of the step's length, with rest-direction gravity, the webs and the capsules; its header states the method in full). Each
/// step, for each bone of each chain, root first and the chains level by level (so the cloth between neighbouring chains is tested as it
/// goes), in the skeleton's own space (a body moving steadily through the world feels nothing):
/// <list type="number">
/// <item>the tail keeps what it was doing, dying away by <c>drag</c> (heavy stuff: <c>fall_drag</c> while it falls back toward where it is
/// pulled, <c>settle</c> near rest), its head's own travel by the <c>carried</c> share given to it outright; pulled by <c>stiffness</c> toward
/// the way its parent leads it and by <c>gravity</c> toward the way it hangs at rest; its length kept;</item>
/// <item>put out of its colliders' capsules (a <c>sided</c> one on the chain's own side of the limb, turned as the limb has turned, along its
/// length only), its middle out of the <c>middles</c>' (the tail moved twice as far), the points <c>along</c> it and <c>across</c> it out of
/// them too (a broad thing only away from the back it hangs on); kept off the ground (12 mm for the cloth's folds);</item>
/// <item>each web's neighbours: the point between them kept out of the capsules, and with <c>stretch</c> no further apart than that many
/// times their rest (each drawn half the way, then put out again);</item>
/// <item>the bone turned the shortest way from where its parent leads it to its tail.</item>
/// </list>
/// The record's capsules are refitted to this person's own rest (the models repository's <c>fit_spec</c>: a capsule that is not <c>fixed</c>
/// shrunk until nothing tested against it touches it at rest; a chain's <c>radii</c> made from its distance to the fixed ones), so a baked
/// crowd person, a base body and a fitted one each get their own. Read-only after it is made; a character's state is a <see cref="SwingState"/>.
/// <para>
/// The frames: the platform's model space (Z up, the person facing +Y, the right hand at +X) where swing.py's is Blender's (Z up, facing −Y,
/// the left hand at +X): a half turn about Z, so the record's <c>side_deg</c> lies at (−cos, −sin, 0) here and its <c>across_dir</c> at
/// (−x, −y, z). A bone's length runs along its own +Z here (glTF's +Y; <see cref="PeopleSpace"/>), its model matrix's third row. Every product
/// is written out in scalar steps (<see cref="PoseMath"/>'s reason), the exponentials and cosines are <see cref="DetMath"/>'s: the same state
/// and body give the same bits on every machine.
/// </para>
/// </summary>
public sealed class SwingRule
{
    /// <summary>A finger's breadth for the cloth's own folds above the ground (m; swing.py's <c>ground_clear</c>).</summary>
    public const float GroundClear = 0.012f;

    /// <summary><c>fit_spec</c>'s margins (m): a refitted capsule this far off what is tested against it; a chain's radii this much further off the fixed ones.</summary>
    public const float FitMargin = 0.004f, FitClear = 0.004f;

    internal sealed class ChainInfo
    {
        public int[] Slots = [];
        public float Gravity, Stiffness, Drag, Carried, Settle, SettleScale;
        public float? FallDrag;
        public float[] Radius = [];   // each bone's, against the colliders (radii, or the chain's radius)
        public int Colliders = -1, Middles = -1;
        public bool HasSide;
        public Vector3 Side;
        public float[]? Along;
        public float[][]? Across;
        public bool HasAcrossDir;
        public Vector3 AcrossDir;
    }

    internal readonly record struct CapsuleSpec(int From, int To, int FromToward, int ToToward, float At, float SpanFrom, float SpanTo, float Radius, float Within, bool Sided);

    internal sealed class WebInfo
    {
        public bool Closed;
        public float Stretch;
        /// <summary>Per level: the web's chains that have a bone there, in order, and the rest distance of each neighbouring pair's tails.</summary>
        public int[][] Ids = [];
        public float[][] Apart = [];
    }

    internal ChainInfo[] Chains = [];
    internal CapsuleSpec[][] Groups = [];
    internal string[] GroupNames = [];
    internal WebInfo[] Webs = [];
    /// <summary>Per level: the chains with a bone there.</summary>
    internal int[][] Levels = [];

    // Per slot (a chain's bone): its bone, chain, level, rest local matrix, the way it hangs at rest (model space), its length.
    internal int[] BoneOf = [], ChainOf = [], LevelOf = [];
    internal Matrix4x4[] RestLocal = [];
    internal Vector3[] Hang = [];
    internal float[] Length = [];
    /// <summary>Each bone's slot, or −1 for a bone of no chain.</summary>
    internal int[] SlotOf = [];
    internal int[] Parent = [];
    /// <summary>Every bone's rest in model space (for the sided capsules' turn and the broad things' directions).</summary>
    internal Matrix4x4[] RestModel = [];
    /// <summary>The slots in the order they are reset and posed: chain by chain, root first.</summary>
    internal int[] Order = [];
    /// <summary>Bones under a chain's bone that are in no chain (posed at their rest on it), parents first.</summary>
    internal int[] Under = [];

    /// <summary>How many chains, and how many bones they have between them.</summary>
    public int ChainCount => Chains.Length;
    public int BoneCount => BoneOf.Length;

    /// <summary>The skeleton's bones that are a chain's (each chain's root first, the chains in the record's order).</summary>
    public IEnumerable<int> Bones => Order.Select(s => BoneOf[s]);

    /// <summary>The chains' bones by chain (each root first).</summary>
    public int[][] ChainBones => [.. Chains.Select(c => c.Slots.Select(s => BoneOf[s]).ToArray())];

    /// <summary>Each capsule's radius after the refit, by group name, in the record's order (for tests and the design notes).</summary>
    public IReadOnlyList<(string Group, float[] Radii)> CapsuleRadii => [.. GroupNames.Select((g, k) => (g, Groups[k].Select(c => c.Radius).ToArray()))];

    /// <summary>Each chain's radii after the refit (one for each bone).</summary>
    public float[][] ChainRadii => [.. Chains.Select(c => c.Radius.ToArray())];

    /// <summary>
    /// The simulator for <paramref name="skeleton"/> wearing what <paramref name="record"/> describes, or null when none of its chains is on the
    /// skeleton. A chain naming a bone the skeleton lacks is left out (its webs close over it), a capsule likewise.
    /// </summary>
    public static SwingRule? For(Skeleton skeleton, SwingRecord? record)
    {
        if (record is null) return null;
        int n = skeleton.Count;
        var bones = skeleton.Bones;
        var rule = new SwingRule { Parent = [.. bones.Select(b => b.Parent)] };
        var local = new BoneTransform[n];
        for (int b = 0; b < n; b++) local[b] = bones[b].Rest;
        rule.RestModel = new Matrix4x4[n];
        PoseEvaluator.ToModel(skeleton, local, rule.RestModel);

        // The chains on this skeleton (each bone the child of the one before, the root the child of a bone of no chain).
        var kept = new List<int>();
        var chainBones = new List<int[]>();
        for (int c = 0; c < record.Chains.Count; c++)
        {
            var names = record.Chains[c].Bones;
            var ids = names.Select(skeleton.Find).ToArray();
            bool ok = ids.Length > 0 && ids.All(i => i >= 0);
            for (int k = 1; ok && k < ids.Length; k++) ok = bones[ids[k]].Parent == ids[k - 1];
            if (ok && bones[ids[0]].Parent < 0) ok = false;
            if (!ok) continue;
            kept.Add(c);
            chainBones.Add(ids);
        }
        if (kept.Count == 0) return null;
        var slotOf = new int[n];
        Array.Fill(slotOf, -1);
        var boneOf = new List<int>();
        foreach (var ids in chainBones)
            foreach (int i in ids)
                if (slotOf[i] < 0) { slotOf[i] = boneOf.Count; boneOf.Add(i); }
        if (boneOf.Count != chainBones.Sum(c => c.Length)) return null;   // a bone in two chains: not a record the models repository writes
        foreach (var ids in chainBones) if (slotOf[bones[ids[0]].Parent] >= 0) return null;   // a chain hung from another's bone: likewise
        int slots = boneOf.Count;
        rule.SlotOf = slotOf;
        rule.BoneOf = [.. boneOf];
        rule.ChainOf = new int[slots];
        rule.LevelOf = new int[slots];
        rule.RestLocal = new Matrix4x4[slots];
        rule.Hang = new Vector3[slots];
        rule.Length = new float[slots];

        // The collider groups (by the record's order), each capsule's bones found.
        var groupNames = record.Colliders.Select(g => g.Key).ToList();
        int Group(string? name) => name is null ? -1 : groupNames.IndexOf(name);
        var groups = new List<CapsuleSpec>[groupNames.Count];
        var specs = new List<SwingCapsule>[groupNames.Count];
        for (int g = 0; g < groupNames.Count; g++)
        {
            groups[g] = [];
            specs[g] = [];
            foreach (var c in record.Colliders[g].Value)
            {
                int from = skeleton.Find(c.From), to = skeleton.Find(c.To);
                int ft = c.FromToward is { } f ? skeleton.Find(f) : -1, tt = c.ToToward is { } t ? skeleton.Find(t) : -1;
                if (from < 0 || to < 0 || (c.FromToward is not null && ft < 0) || (c.ToToward is not null && tt < 0)) continue;
                groups[g].Add(new CapsuleSpec(from, to, ft, tt, c.At, c.SpanFrom, c.SpanTo, c.Radius, c.Within, c.Sided));
                specs[g].Add(c);
            }
        }

        var chains = new ChainInfo[kept.Count];
        for (int k = 0; k < kept.Count; k++)
        {
            var spec = record.Chains[kept[k]];
            var ids = chainBones[k];
            var info = chains[k] = new ChainInfo
            {
                Slots = [.. ids.Select(i => slotOf[i])],
                Gravity = spec.Gravity, Stiffness = spec.Stiffness, Drag = spec.Drag, Carried = spec.Carried,
                FallDrag = spec.FallDrag, Settle = spec.Settle,
                Colliders = Group(spec.Colliders), Middles = Group(spec.Middles),
                Along = spec.Along is { Length: > 0 } along ? along : null,
                Across = spec.Across is { Length: > 0 } across ? across : null,
            };
            // exp(-(1 - c) / (1 - cos(settle_within))): the scale 1 / (1 - cos) once.
            float within = 1f - DetMath.Cos(spec.SettleWithin * DetMath.DegToRad);
            info.SettleScale = within > 1e-9f ? 1f / within : 0f;
            if (spec.SideDeg is { } deg)
            {
                var (s, c) = DetMath.SinCos(deg * DetMath.DegToRad);
                info.HasSide = true;
                info.Side = new Vector3(-c, -s, 0f);   // the record's (cos, sin, 0) in Blender's skeleton space, half a turn about Z
            }
            if (spec.AcrossDir is { } dir) { info.HasAcrossDir = true; info.AcrossDir = new Vector3(-dir.X, -dir.Y, dir.Z); }
            for (int j = 0; j < ids.Length; j++)
            {
                int s = slotOf[ids[j]];
                rule.ChainOf[s] = k;
                rule.LevelOf[s] = j;
                rule.RestLocal[s] = PoseMath.ToMatrix(bones[ids[j]].Rest);
                rule.Hang[s] = Along(rule.RestModel[ids[j]]);
            }
            // Each bone's length: the record's, else the way to the next bone's head, the last as long as the one before (an
            // importer's guess, which a record without lengths was made with).
            for (int j = 0; j < ids.Length; j++)
            {
                float len = spec.Lengths is { } ls && j < ls.Length ? ls[j]
                    : j + 1 < ids.Length ? bones[ids[j + 1]].Rest.Translation.Length()
                    : j > 0 ? rule.Length[slotOf[ids[j - 1]]] : 0.1f;
                rule.Length[slotOf[ids[j]]] = len;
            }
            info.Radius = new float[ids.Length];
            for (int j = 0; j < ids.Length; j++) info.Radius[j] = spec.Radii is { } r && j < r.Length ? r[j] : spec.Radius;
        }
        rule.Chains = chains;

        // The levels: the bones at the same place along their chains, stepped together.
        int depth = chains.Max(c => c.Slots.Length);
        rule.Levels = new int[depth][];
        for (int j = 0; j < depth; j++) rule.Levels[j] = [.. Enumerable.Range(0, chains.Length).Where(c => j < chains[c].Slots.Length)];
        rule.Order = [.. chains.SelectMany(c => c.Slots)];
        var under = new List<int>();
        var below = new bool[n];
        for (int b = 0; b < n; b++)
        {
            int p = bones[b].Parent;
            below[b] = slotOf[b] >= 0 || (p >= 0 && p < b && below[p]);
            if (below[b] && slotOf[b] < 0) under.Add(b);
        }
        rule.Under = [.. under];

        // The refit to this person (fit_spec), then the groups as arrays.
        rule.Fit(skeleton, record, kept, specs);
        rule.Groups = [.. groups.Select((g, k) => g.Select((c, i) => c with { Radius = rule.fitted[k][i] }).ToArray())];
        rule.GroupNames = [.. groupNames];

        // The webs, their chains renumbered to the ones kept, and each pair's rest distance at each level.
        var webs = new List<WebInfo>();
        foreach (var w in record.Webs)
        {
            var ids = w.Chains.Select(c => kept.IndexOf(c)).Where(c => c >= 0).ToArray();
            var web = new WebInfo { Closed = w.Closed, Stretch = w.Stretch, Ids = new int[depth][], Apart = new float[depth][] };
            for (int j = 0; j < depth; j++)
            {
                var at = ids.Where(c => j < chains[c].Slots.Length).ToArray();
                web.Ids[j] = at;
                web.Apart[j] = new float[at.Length];
                for (int k = 0; k < at.Length; k++)
                {
                    int sa = chains[at[k]].Slots[j], sb = chains[at[(k + 1) % at.Length]].Slots[j];
                    web.Apart[j][k] = Norm(rule.RestTail(sa) - rule.RestTail(sb));
                }
            }
            webs.Add(web);
        }
        rule.Webs = [.. webs];
        return rule;
    }

    float[][] fitted = [];

    Vector3 RestHead(int slot) => RestModel[BoneOf[slot]].Translation;
    Vector3 RestTail(int slot) => RestHead(slot) + Hang[slot] * Length[slot];

    /// <summary>
    /// <c>fit_spec</c> on this person's rest: each capsule not <c>fixed</c> shrunk so that no point tested against it (each bone's tail against its
    /// chain's colliders, its middle and its <c>along</c> places and the points <c>across</c> against the middles) is within the chain's radius
    /// and <see cref="FitMargin"/> of it (at least 5 mm, never larger than the record's); each chain that has <c>radii</c> given, for each bone,
    /// its distance at rest to the fixed capsules it is tested against and <see cref="FitClear"/> more (from 4 mm to the chain's radius).
    /// </summary>
    void Fit(Skeleton skeleton, SwingRecord record, List<int> kept, List<SwingCapsule>[] specs)
    {
        int groupCount = specs.Length;
        fitted = new float[groupCount][];
        // The places tested on each bone: (share of the bone, group).
        IEnumerable<(float Share, int Group)> Tests(ChainInfo ch)
        {
            yield return (1f, ch.Colliders);
            if (ch.Middles < 0) yield break;
            yield return (0.5f, ch.Middles);
            if (ch.Along is { } along) foreach (float f in along) yield return (f, ch.Middles);
        }
        for (int g = 0; g < groupCount; g++)
        {
            var points = new List<(Vector3 P, float R)>();
            for (int c = 0; c < Chains.Length; c++)
            {
                var ch = Chains[c];
                foreach (var (share, group) in Tests(ch))
                {
                    if (group != g) continue;
                    float radius = record.Chains[kept[c]].Radius;
                    foreach (int s in ch.Slots)
                    {
                        Vector3 h = RestHead(s), t = RestTail(s);
                        var p = h + (t - h) * share;
                        points.Add((p, radius));
                        if (ch.Across is { } across && ch.HasAcrossDir && ch.Along is { } along && Array.IndexOf(along, share) is var k and >= 0 && k < across.Length)
                            foreach (float a in across[k]) points.Add((p + ch.AcrossDir * a, radius));
                    }
                }
            }
            fitted[g] = new float[specs[g].Count];
            for (int i = 0; i < specs[g].Count; i++)
            {
                var c = specs[g][i];
                float r = c.Radius;
                if (points.Count > 0 && !c.Fixed)
                {
                    var (a, b) = RestEnds(c);
                    float least = float.MaxValue;
                    foreach (var (p, pr) in points) least = MathF.Min(least, SegmentDistance(p, a, b) - pr);
                    r = MathF.Max(0.005f, MathF.Min(r, least - FitMargin));
                }
                fitted[g][i] = (float)Math.Round(r, 4);
            }
        }
        // A chain that carries radii lies close to the fixed capsules: each bone given the room that leaves it where it lies at rest.
        for (int c = 0; c < Chains.Length; c++)
        {
            var spec = record.Chains[kept[c]];
            if (spec.Radii is null) continue;
            var ch = Chains[c];
            for (int j = 0; j < ch.Slots.Length; j++)
            {
                int s = ch.Slots[j];
                Vector3 h = RestHead(s), t = RestTail(s);
                float off = 9f;
                foreach (var (share, group) in Tests(ch))
                {
                    if (group < 0) continue;
                    var p = h + (t - h) * share;
                    for (int i = 0; i < specs[group].Count; i++)
                    {
                        var cap = specs[group][i];
                        if (!cap.Fixed) continue;
                        var (a, b) = RestEnds(cap);
                        off = MathF.Min(off, SegmentDistance(p, a, b) - Size(a, b, fitted[group][i], cap.Within));
                    }
                }
                ch.Radius[j] = (float)Math.Round(Math.Clamp(off + FitClear, 0.004f, spec.Radius), 4);
            }
        }

        (Vector3, Vector3) RestEnds(SwingCapsule c)
        {
            Vector3 Head(string name) => RestModel[skeleton.Find(name)].Translation;
            var a = Head(c.From);
            if (c.FromToward is { } ft) a += (Head(ft) - a) * c.At;
            var b = Head(c.To);
            if (c.ToToward is { } tt) b += (Head(tt) - b) * c.At;
            return (a + (b - a) * c.SpanFrom, a + (b - a) * c.SpanTo);
        }
    }

    /// <summary>A capsule's radius now (capsule_size): one that says <c>within</c> is whole while its ends are nearer than that, gone a quarter further.</summary>
    static float Size(Vector3 a, Vector3 b, float radius, float within)
    {
        if (within <= 0f) return radius;
        return radius * Math.Clamp((1.25f * within - Norm(b - a)) / (0.25f * within), 0f, 1f);
    }

    // ----- a character's state -----

    /// <summary>A state for one character wearing this (sized to the rule; <see cref="Reset"/> before the first step).</summary>
    public SwingState NewState() => new(this);

    /// <summary>
    /// Everything at rest relative to the body's present pose (swing.py's <c>reset</c>): each bone as its parent leads it, its tail where that
    /// puts it, not moving. <paramref name="body"/>: the pose's bones in model space (a chain's own bones in it are not read); <paramref name="lift"/>
    /// as for <see cref="Step"/>.
    /// </summary>
    public void Reset(SwingState state, ReadOnlySpan<Matrix4x4> body, float lift = 0f)
    {
        foreach (int s in Order)
        {
            int p = Parent[BoneOf[s]];
            var f = SlotOf[p] >= 0 ? PoseMath.Multiply(RestLocal[s], state.World[SlotOf[p]]) : Lowered(PoseMath.Multiply(RestLocal[s], body[p]), lift);   // a chain's bone before it set just now
            state.World[s] = f;
            var head = f.Translation;
            state.Tail[s] = head + Along(f) * Length[s];
            state.Prev[s] = state.Tail[s];
            state.HeadBefore[s] = head;
        }
        state.Steps = 0;
        state.Ground = null;
    }

    /// <summary>The frame bone <paramref name="slot"/> has following its parent rigidly: its parent this step's (a chain's bone stepped before it), else the body's.</summary>
    Matrix4x4 Follow(SwingState state, int slot, ReadOnlySpan<Matrix4x4> body, int mark, float lift)
    {
        int p = Parent[BoneOf[slot]];
        int ps = SlotOf[p];
        return ps >= 0 && state.Done[ps] == mark ? PoseMath.Multiply(RestLocal[slot], state.World[ps]) : Lowered(PoseMath.Multiply(RestLocal[slot], body[p]), lift);
    }

    /// <summary>A frame of the body's (model space) in the swing's own (<paramref name="lift"/> lower).</summary>
    static Matrix4x4 Lowered(Matrix4x4 m, float lift)
    {
        m.M43 -= lift;
        return m;
    }

    /// <summary>
    /// One step of <paramref name="dt"/> seconds (swing.py's <c>step</c>) on <paramref name="body"/> (the pose's bones in model space), with the
    /// ground at <paramref name="ground"/> (its height in the swing's space; null: none, in the air). The swing's space is the model's
    /// <paramref name="lift"/> higher (a person on the stairs' line is posed that far above its capsule's feet, the model's origin, which steps
    /// each riser while the body drawn on the line does not: in the model's space the whole body jumps by a riser at each step, and the cloth would
    /// be flung by it; on the line's own level it moves as the body does). The state is kept in it. Allocates nothing.
    /// </summary>
    public void Step(SwingState state, ReadOnlySpan<Matrix4x4> body, float dt, float? ground, float lift = 0f)
    {
        int mark = ++state.Mark;
        state.Ground = ground;
        var up = new Vector3(0f, 0f, lift);
        PlaceCapsules(state, body, up);
        for (int j = 0; j < Levels.Length; j++)
        {
            foreach (int ci in Levels[j])
            {
                var ch = Chains[ci];
                int s = ch.Slots[j];
                var f = Follow(state, s, body, mark, lift);
                var head = f.Translation;
                var follow = Along(f);
                float len = Length[s];
                Vector3 cur = state.Tail[s], prev = state.Prev[s];
                var pull = follow * ch.Stiffness + Hang[s] * ch.Gravity;
                float drag = ch.Drag;
                if (ch.FallDrag is not null || ch.Settle != 0f)
                {
                    // Heavy stuff: fall_drag while moving toward where it is pulled, drag while thrown away; settle near rest.
                    Vector3 to = Unit(pull), at = Unit(cur - head);
                    if (ch.FallDrag is { } fall && Dot(cur - prev, to - at) > 0f) drag = fall;
                    if (ch.Settle != 0f) drag += ch.Settle * DetMath.Exp(-(1f - Dot(at, to)) * ch.SettleScale);
                }
                var went = (head - state.HeadBefore[s]) * ch.Carried;
                var next = cur + went + (cur - prev - went) * DetMath.Exp(-drag * dt) + pull * (dt * dt);
                next = head + Unit(next - head) * len;
                float r = ch.Radius[j];
                next = head + Unit(next + Out(state, next, ch.Colliders, r, ch.HasSide, ch.Side) - head) * len;
                if (ch.Middles >= 0)
                {
                    var pm = Out(state, (head + next) * 0.5f, ch.Middles, r, false, default);
                    if (Any(pm)) next = head + Unit(next + pm * 2f - head) * len;
                    if (ch.Along is { } along) next = Along2(state, ch, s, f, head, next, len, r, along);
                }
                if (ground is { } floor)
                {
                    float low = floor + GroundClear - next.Z;
                    if (low > 0f) next = head + Unit(next + new Vector3(0f, 0f, low) - head) * len;
                }
                state.Follow[s] = f;
                state.Next[s] = next;
            }
            // The cloth between neighbouring chains.
            foreach (var web in Webs) WebStep(state, web, j, ground);
            foreach (int ci in Levels[j])
            {
                int s = Chains[ci].Slots[j];
                var f = state.Follow[s];
                var head = f.Translation;
                state.Prev[s] = state.Tail[s];
                state.Tail[s] = state.Next[s];
                state.HeadBefore[s] = head;
                state.World[s] = Turned(f, Unit(state.Next[s] - head));
                state.Done[s] = mark;
            }
        }
        state.Steps++;
    }

    /// <summary>The capsules where <paramref name="body"/> (model space) has them, in the swing's space (<paramref name="up"/> lower): each group's into the state.</summary>
    void PlaceCapsules(SwingState state, ReadOnlySpan<Matrix4x4> body, Vector3 up)
    {
        for (int g = 0; g < Groups.Length; g++)
        {
            var specs = Groups[g];
            var caps = state.Caps[g];
            int count = 0;
            for (int i = 0; i < specs.Length; i++)
            {
                ref readonly var c = ref specs[i];
                var a = body[c.From].Translation - up;
                if (c.FromToward >= 0) a += (body[c.FromToward].Translation - up - a) * c.At;
                var b = body[c.To].Translation - up;
                if (c.ToToward >= 0) b += (body[c.ToToward].Translation - up - b) * c.At;
                Vector3 e0 = a + (b - a) * c.SpanFrom, e1 = a + (b - a) * c.SpanTo;
                float r = Size(e0, e1, c.Radius, c.Within);
                if (r <= 0f) continue;
                // A sided capsule's turn since rest: the rest frame's inverse, then the posed (row vectors: v × restᵀ × posed).
                var turned = c.Sided ? PoseMath.Multiply(Transpose3(RestModel[c.From]), Rotation3(body[c.From])) : default;
                caps[count++] = new Capsule(e0, e1, r, c.Sided, turned);
            }
            state.CapCount[g] = count;
        }
    }

    /// <summary>The points along a stiff thing (and across a broad one) put out of its middles' capsules, twice over.</summary>
    Vector3 Along2(SwingState state, ChainInfo ch, int s, in Matrix4x4 f, Vector3 head, Vector3 next, float len, float r, float[] along)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            bool broad = false;
            Vector3 sn = default;
            if (ch.Across is not null && ch.HasAcrossDir)
            {
                // The direction across, as the bone has turned: into its rest frame, out of the frame it turns to.
                var rn = Turned(f, Unit(next - head));
                sn = ToFrame(ch.AcrossDir, RestModel[BoneOf[s]], rn);
                broad = true;
            }
            for (int k = 0; k < along.Length; k++)
            {
                float share = along[k];
                int across = broad && ch.Across is { } acr && k < acr.Length ? acr[k].Length : 0;
                for (int x = -1; x < across; x++)
                {
                    float a = x < 0 ? 0f : ch.Across![k][x];
                    if (a != 0f && !broad) continue;
                    var pt = head + (next - head) * share + (a != 0f ? sn * a : Vector3.Zero);
                    var pm = Out(state, pt, ch.Middles, r, false, default);
                    if (Any(pm) && broad)
                    {
                        // A broad thing is pushed only away from the back it hangs on, along its own normal, the shortest way out.
                        var on = Unit(Cross(sn, Unit(next - head)));
                        var back = ToFrame(new Vector3(0f, -1f, 0f), RestModel[BoneOf[s]], f);   // swing.py's [0, 1, 0]: behind, in Blender's space
                        if (Dot(on, back) < 0f) on = -on;
                        float step = 0f;
                        while (step < 0.4f && Any(Out(state, pt + on * step, ch.Middles, r, false, default))) step += 0.005f;
                        pm = on * step;
                    }
                    if (Any(pm)) next = head + Unit(next + pm / share - head) * len;
                }
            }
        }
        return next;
    }

    void WebStep(SwingState state, WebInfo web, int j, float? ground)
    {
        var ids = web.Ids[j];
        int n = ids.Length;
        if (n < 2) return;
        float stretch = web.Stretch;
        var moved = state.Moved;
        Array.Clear(moved);
        int passes = stretch != 0f ? 3 : 2;
        int pairs = web.Closed ? n : n - 1;
        for (int pass = 0; pass < passes; pass++)
            for (int k = 0; k < pairs; k++)
            {
                int ca = ids[k], cb = ids[(k + 1) % n];
                ChainInfo cha = Chains[ca], chb = Chains[cb];
                int sa = cha.Slots[j], sb = chb.Slots[j];
                float r = MathF.Min(cha.Radius[j], chb.Radius[j]);
                bool sided = cha.HasSide && chb.HasSide;
                var push = Out(state, (state.Next[sa] + state.Next[sb]) * 0.5f, cha.Colliders, r, sided, sided ? Unit(cha.Side + chb.Side) : default);
                if (Any(push))
                {
                    var ha = state.Follow[sa].Translation;
                    state.Next[sa] = ha + Unit(state.Next[sa] + push - ha) * Length[sa];
                    var hb = state.Follow[sb].Translation;
                    state.Next[sb] = hb + Unit(state.Next[sb] + push - hb) * Length[sb];
                }
                if (stretch != 0f)
                {
                    // Cloth does not stretch: neighbours no further apart than they are at rest (and a little), each drawn half the way.
                    float most = stretch * web.Apart[j][k];
                    var d = state.Next[sb] - state.Next[sa];
                    float dist = Norm(d);
                    if (dist > most)
                    {
                        var corr = d * (0.5f * (dist - most) / dist);
                        var ha = state.Follow[sa].Translation;
                        var hb = state.Follow[sb].Translation;
                        state.Next[sa] = ha + Unit(state.Next[sa] + corr - ha) * Length[sa];
                        state.Next[sb] = hb + Unit(state.Next[sb] - corr - hb) * Length[sb];
                        moved[ca] = true;
                        moved[cb] = true;
                    }
                }
            }
        // What was drawn along is kept out of the limbs and off the ground again.
        for (int c = 0; c < moved.Length; c++)
        {
            if (!moved[c]) continue;
            var ch = Chains[c];
            int s = ch.Slots[j];
            var head = state.Follow[s].Translation;
            float len = Length[s];
            state.Next[s] = head + Unit(state.Next[s] + Out(state, state.Next[s], ch.Colliders, ch.Radius[j], ch.HasSide, ch.Side) - head) * len;
            if (ground is { } floor)
            {
                float low = floor + GroundClear - state.Next[s].Z;
                if (low > 0f) state.Next[s] = head + Unit(state.Next[s] + new Vector3(0f, 0f, low) - head) * len;
            }
        }
    }

    /// <summary>Between steps, each web's neighbouring tails at a level moved with the point between them out of the colliders (<see cref="Pose"/>).</summary>
    void WebOut(SwingState state, WebInfo web, int j, Vector3 up)
    {
        var ids = web.Ids[j];
        int n = ids.Length;
        if (n < 2) return;
        int pairs = web.Closed ? n : n - 1;
        for (int pass = 0; pass < 2; pass++)
            for (int k = 0; k < pairs; k++)
            {
                ChainInfo cha = Chains[ids[k]], chb = Chains[ids[(k + 1) % n]];
                int sa = cha.Slots[j], sb = chb.Slots[j];
                bool sided = cha.HasSide && chb.HasSide;
                var push = Out(state, (state.Next[sa] + state.Next[sb]) * 0.5f, cha.Colliders, MathF.Min(cha.Radius[j], chb.Radius[j]), sided, sided ? Unit(cha.Side + chb.Side) : default);
                if (!Any(push)) continue;
                var ha = state.Follow[sa].Translation - up;
                state.Next[sa] = ha + Unit(state.Next[sa] + push - ha) * Length[sa];
                var hb = state.Follow[sb].Translation - up;
                state.Next[sb] = hb + Unit(state.Next[sb] + push - hb) * Length[sb];
            }
    }

    /// <summary>
    /// How far a point must move to be clear of a group's capsules (swing.py's <c>_out</c>; zero when it is clear, or for no group): each in
    /// turn, the point already moved by the ones before. A sided capsule puts it out on <paramref name="side"/> of the limb, turned as the limb has
    /// turned since rest, along its length only; round its ends, and an unsided one, the nearest way.
    /// </summary>
    static Vector3 Out(SwingState state, Vector3 pt, int group, float extra, bool hasSide, Vector3 side)
    {
        var push = Vector3.Zero;
        if (group < 0) return push;
        var caps = state.Caps[group];
        int count = state.CapCount[group];
        for (int i = 0; i < count; i++)
        {
            ref readonly var c = ref caps[i];
            var q0 = pt + push;
            var ab = c.B - c.A;
            float abab = Dot(ab, ab);
            float t = abab > 1e-12f ? Math.Clamp(Dot(q0 - c.A, ab) / abab, 0f, 1f) : 0f;
            var q = c.A + ab * t;
            var d = q0 - q;
            float dist = Norm(d), radius = c.R + extra;
            if (dist >= radius) continue;
            var u = dist > 1e-6f ? d / dist : Unit(Cross(ab, Vector3.UnitZ));
            if (c.Sided && hasSide && t > 0f && t < 1f)
            {
                var o = Rotate(side, c.Turned);
                var axis = Unit(ab);
                o -= axis * Dot(o, axis);
                if (Norm(o) > 0.2f)
                {
                    o = Unit(o);
                    float s = Dot(u, o);
                    if (s < 0f) u = Unit(u - o * (2f * s));
                }
            }
            push += q + u * radius - q0;
        }
        return push;
    }

    /// <summary>
    /// Poses the chains' bones into <paramref name="model"/> (the pose's bones in model space, the body's as drawn) from the state: each bone
    /// turned the shortest way from where its parent leads it to its tail — on the tick of a step exactly the step's frames; between steps the
    /// last step's tails, each carried by the chain's <c>carried</c> share of its head's travel since — and the bones under a chain's at their
    /// rest on it (a tail carried down kept off the last step's ground). <paramref name="keepOut"/>, each tail kept out of its chain's colliders
    /// where the body has them now (between steps a fast limb moves into the cloth the last step left; the earlier build's without). <paramref name="lift"/>:
    /// the swing's space above the model's now (Step's).
    /// The skin matrices of every bone it moved go into <paramref name="skin"/> when it is long enough. Allocates nothing.
    /// </summary>
    public void Pose(SwingState state, Skeleton skeleton, Span<Matrix4x4> model, Span<Matrix4x4> skin, float lift = 0f, bool keepOut = true)
    {
        var up = new Vector3(0f, 0f, lift);
        // The colliders where the body has them now (between steps the state's are where it had them at the last step). Level by level
        // (as a step goes), so the cloth between neighbouring chains is put out with them.
        if (keepOut) PlaceCapsules(state, model, up);
        for (int j = 0; j < Levels.Length; j++)
        {
            foreach (int ci in Levels[j])
            {
                var ch = Chains[ci];
                int s = ch.Slots[j];
                var f = PoseMath.Multiply(RestLocal[s], model[Parent[BoneOf[s]]]);
                var head = f.Translation - up;   // in the swing's space (lift: Step's)
                var tail = state.Tail[s] + (head - state.HeadBefore[s]) * ch.Carried;
                // The tail kept out of its colliders as the body has them now, as the step keeps it (its chain's radius, its side; in play,
                // at full run, the left thigh clipped through the bottom of the tunic): between the 60 a second steps a running
                // thigh moves on a tick's worth into the cloth the last step left. On a step's own tick the step has put its tails out already
                // (nothing moves: the step's frames, swing.py's). Off: --swing-keep-out-off.
                if (keepOut && ch.Colliders >= 0)
                {
                    var push = Out(state, tail, ch.Colliders, ch.Radius[j], ch.HasSide, ch.Side);
                    if (Any(push)) tail = head + Unit(tail + push - head) * Length[s];
                }
                state.Follow[s] = f;
                state.Next[s] = tail;
            }
            // The point between neighbouring chains' tails put out of the colliders too, both tails with it (the step's web pass, without
            // its stretch: the step keeps that).
            if (keepOut) foreach (var web in Webs) WebOut(state, web, j, up);
            foreach (int ci in Levels[j])
            {
                int s = Chains[ci].Slots[j];
                var f = state.Follow[s];
                var head = f.Translation - up;
                var tail = state.Next[s];
                // Carried down between steps (a body crouching, kneeling) no further than the ground the last step had (nor lower than the step left it).
                if (state.Ground is { } ground && tail.Z < ground + GroundClear) tail.Z = MathF.Min(ground + GroundClear, MathF.Max(tail.Z, state.Tail[s].Z));
                model[BoneOf[s]] = Turned(f, Unit(tail - head));
            }
        }
        var bones = skeleton.Bones;
        foreach (int b in Under) model[b] = PoseMath.Multiply(PoseMath.ToMatrix(bones[b].Rest), model[bones[b].Parent]);
        if (skin.Length < model.Length) return;
        var inverse = skeleton.InverseBind;
        foreach (int s in Order) skin[BoneOf[s]] = PoseMath.Multiply(inverse[BoneOf[s]], model[BoneOf[s]]);
        foreach (int b in Under) skin[b] = PoseMath.Multiply(inverse[b], model[b]);
    }

    /// <summary>Each chain's bones' tails as the state has them (model space), slot by slot in <see cref="Bones"/>' order.</summary>
    public void Tails(SwingState state, Span<Vector3> into)
    {
        for (int k = 0; k < Order.Length && k < into.Length; k++) into[k] = state.Tail[Order[k]];
    }

    // ----- the arithmetic, in scalar steps -----

    internal readonly record struct Capsule(Vector3 A, Vector3 B, float R, bool Sided, Matrix4x4 Turned);

    /// <summary>A bone's length's way: its frame's +Z (the third row).</summary>
    internal static Vector3 Along(in Matrix4x4 m) => new(m.M31, m.M32, m.M33);

    /// <summary>
    /// <paramref name="f"/> turned the shortest way (swing.py's <c>_minrot</c>) so its +Z is <paramref name="to"/>, about its own origin: each
    /// axis turned by the rotation taking its +Z to <paramref name="to"/> (half a turn about any square axis when they are opposite).
    /// </summary>
    internal static Matrix4x4 Turned(in Matrix4x4 f, Vector3 to)
    {
        var a = Along(f);
        var v = Cross(a, to);
        float c = Dot(a, to), sn = Norm(v);
        Vector3 x = new(f.M11, f.M12, f.M13), y = new(f.M21, f.M22, f.M23), z = a;
        if (sn < 1e-9f)
        {
            if (c > 0f) return f;
            var k = Unit(MathF.Abs(a.X) < 0.9f ? Cross(a, Vector3.UnitX) : Cross(a, Vector3.UnitY));
            (x, y, z) = (Half(x, k), Half(y, k), Half(z, k));
        }
        else
        {
            var k = v / sn;
            (x, y, z) = (Rodrigues(x, k, sn, c), Rodrigues(y, k, sn, c), Rodrigues(z, k, sn, c));
        }
        return new Matrix4x4(x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, f.M41, f.M42, f.M43, 1f);

        static Vector3 Half(Vector3 p, Vector3 k) => k * (2f * Dot(k, p)) - p;
        static Vector3 Rodrigues(Vector3 p, Vector3 k, float s, float c)
        {
            var kp = Cross(k, p);
            return p + kp * s + Cross(k, kp) * (1f - c);
        }
    }

    /// <summary>A model-space direction <paramref name="v"/> taken into the frame <paramref name="rest"/> and out of <paramref name="now"/> (row vectors: v × restᵀ × now).</summary>
    static Vector3 ToFrame(Vector3 v, in Matrix4x4 rest, in Matrix4x4 now)
    {
        float lx = v.X * rest.M11 + v.Y * rest.M12 + v.Z * rest.M13;
        float ly = v.X * rest.M21 + v.Y * rest.M22 + v.Z * rest.M23;
        float lz = v.X * rest.M31 + v.Y * rest.M32 + v.Z * rest.M33;
        return new Vector3(lx * now.M11 + ly * now.M21 + lz * now.M31, lx * now.M12 + ly * now.M22 + lz * now.M32, lx * now.M13 + ly * now.M23 + lz * now.M33);
    }

    static Vector3 Rotate(Vector3 v, in Matrix4x4 m) => PoseMath.TransformNormal(v, m);

    static Matrix4x4 Rotation3(in Matrix4x4 m) => new(m.M11, m.M12, m.M13, 0f, m.M21, m.M22, m.M23, 0f, m.M31, m.M32, m.M33, 0f, 0f, 0f, 0f, 1f);

    static Matrix4x4 Transpose3(in Matrix4x4 m) => new(m.M11, m.M21, m.M31, 0f, m.M12, m.M22, m.M32, 0f, m.M13, m.M23, m.M33, 0f, 0f, 0f, 0f, 1f);

    internal static float Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    internal static Vector3 Cross(Vector3 a, Vector3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    internal static float Norm(Vector3 v) => MathF.Sqrt(Dot(v, v));

    /// <summary>swing.py's <c>_unit</c>: v over its length, or over 1e-12 when it has none.</summary>
    internal static Vector3 Unit(Vector3 v) => v / MathF.Max(Norm(v), 1e-12f);

    static bool Any(Vector3 v) => v.X != 0f || v.Y != 0f || v.Z != 0f;

    static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float l = Dot(ab, ab);
        float t = l > 1e-12f ? Math.Clamp(Dot(p - a, ab) / l, 0f, 1f) : 0f;
        return Norm(p - (a + ab * t));
    }
}

/// <summary>
/// One character's swing (<see cref="SwingRule"/>): each chain bone's tail and where it was a step before, its head after the last step,
/// its frame; the capsules and the step's scratch. Presentation only: kept outside anything digested (<see cref="PersonSwings"/>).
/// </summary>
public sealed class SwingState
{
    internal readonly Vector3[] Tail, Prev, HeadBefore, Next;
    internal readonly Matrix4x4[] World, Follow;
    internal readonly int[] Done;
    internal readonly SwingRule.Capsule[][] Caps;
    internal readonly int[] CapCount;
    internal readonly bool[] Moved;
    internal int Mark;
    /// <summary>The ground the last step had (model space), or null.</summary>
    internal float? Ground;

    /// <summary>The rule it is for.</summary>
    public SwingRule Rule { get; }

    /// <summary>Steps taken since the last reset.</summary>
    public int Steps { get; internal set; }

    internal SwingState(SwingRule rule)
    {
        Rule = rule;
        int n = rule.BoneCount;
        Tail = new Vector3[n];
        Prev = new Vector3[n];
        HeadBefore = new Vector3[n];
        Next = new Vector3[n];
        World = new Matrix4x4[n];
        Follow = new Matrix4x4[n];
        Done = new int[n];
        Caps = [.. rule.Groups.Select(g => new SwingRule.Capsule[g.Length])];
        CapCount = new int[rule.Groups.Length];
        Moved = new bool[rule.ChainCount];
    }

    /// <summary>The tail of each chain bone (model space), in <see cref="SwingRule.Bones"/>' order.</summary>
    public Vector3[] Tails()
    {
        var t = new Vector3[Rule.BoneCount];
        Rule.Tails(this, t);
        return t;
    }

    /// <summary>The tail where a step before it was, likewise.</summary>
    public Vector3[] PreviousTails() => [.. Rule.Order.Select(s => Prev[s])];
}
