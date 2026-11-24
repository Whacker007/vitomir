// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The weapon record's reading is taken from src/Vitomir.Engine/Rpg/Weapon.cs (Weapon.From, AttackMode, CarryMode) and the
// held model's name from src/Vitomir.Engine/World/HeldModel.cs at 6fbcf40, reworked for the platform: only what
// the action channel and the held models need (the carry class, the two attacks' melee types, flags, strike and reload
// times, the model).
using System.Numerics;
using Vitomir.Formats;
using Vitomir.Platform.Animation;

namespace Vitomir.Platform.Assets.OneOne;

/// <summary>
/// A 1.1 weapon as the action channel needs it (1.1's <c>gameWeapon</c>): how it is carried (<c>WEAPON_TYPE1</c>,
/// tag 0x28), each attack's melee type (<c>WEAPON_TYPE2</c> 0x29 and <c>WEAPON_MELEETYPE2</c> 0xe2: the body mode is
/// 65 + it), flags (<c>WEAPON_FLAGS</c> 0x27 for both, <c>WEAPON_FLAGS1</c> 0xee and <c>WEAPON_FLAGS2</c> 0xef, applied in
/// the template's order; <c>0x100</c> on the first makes the second <c>0x80</c>, special), strike time (0x2d, 0x2e) and
/// reload time (0x2f, 0xa2), how many attacks it has (a <c>*2</c> tag makes two), and its model (<c>WEAPON_MODEL</c> 0x33).
/// </summary>
public sealed record OneOneWeapon(string Template, string Model, int Carry, int[] MeleeType, uint[] Flags, float[] StrikeTime, float[] ReloadTime, int Attacks)
{
    public const uint TagType = 16, TypeWeapon = 5;
    public const uint TagFlags = 0x27, TagType1 = 0x28, TagMeleeType = 0x29, TagModel = 0x33, TagMeleeType2 = 0xe2, TagFlags1 = 0xee, TagFlags2 = 0xef;
    public const uint TagStrike1 = 0x2d, TagStrike2 = 0x2e, TagReload1 = 0x2f, TagReload2 = 0xa2;
    /// <summary>The second attack's other tags: any of them makes the weapon two-attacked.</summary>
    static readonly uint[] SecondTags = [0x2c, 0x84, 0x2e, 0xed, 0xa2, 0xa1, 0x9f, 0xe4, 0x86, 0xa5, 0xa4, 0xa3];

    /// <summary>1.1's flag bits read here: ask the motion set for the <c>special</c> variant; make the second attack special; the model named after the character.</summary>
    public const uint SpecialAnimation = 0x80, SecondAttackIsSpecial = 0x100, SpecificModels = 0x4000;

    /// <summary>The first attack mode: 65 (<c>claw</c>) + a melee type.</summary>
    public const int AttackModeBase = 65;

    /// <summary>What 1.1 swings when the set has nothing for the mode: <c>swordslash</c>.</summary>
    public const int FallbackAttackMode = 66;

    /// <summary>The blocks the carry class is added to: walk 1, run 17, stand 33, hurt 221 (<see cref="NmsMotionSet.BodyModes"/>).</summary>
    public const int WalkBase = 1, RunBase = 17, StandBase = 33;

    /// <summary>A weapon template by name (case as 1.1's: the first template of that name), or null when it is not a weapon (tag 16 is not 5).</summary>
    public static OneOneWeapon? From(OneOneTemplates templates, string name)
    {
        if (string.IsNullOrEmpty(name) || templates.Find(name) is not { } found) return null;
        var tags = templates.Resolve(found.Name);
        if (!tags.TryGetValue(TagType, out var type) || type.AsUInt() != TypeWeapon) return null;
        uint a = 0, b = 0;
        int attacks = 1;
        foreach (var (id, tag) in tags)
        {
            if (id == TagFlags) { a = b = tag.AsUInt(); if ((a & SecondAttackIsSpecial) != 0) b |= SpecialAnimation; }
            else if (id == TagFlags1) a = tag.AsUInt();
            else if (id == TagFlags2) { b = tag.AsUInt(); attacks = 2; }
        }
        foreach (var t in SecondTags) if (tags.ContainsKey(t)) attacks = 2;
        int melee1 = tags.TryGetValue(TagMeleeType, out var m1) ? (int)m1.AsUInt() : 0;
        int melee2 = melee1;
        if (tags.TryGetValue(TagMeleeType2, out var m2)) { melee2 = (int)m2.AsUInt(); attacks = 2; }
        float F(uint id, float fallback) => tags.TryGetValue(id, out var v) ? v.AsFloat() : fallback;
        float strike1 = F(TagStrike1, 1f), reload1 = F(TagReload1, 1f);
        return new OneOneWeapon(found.Name, tags.TryGetValue(TagModel, out var model) ? model.AsString() : "",
                                tags.TryGetValue(TagType1, out var carry) ? (int)carry.AsUInt() : 0,
                                [melee1, melee2], [a, b], [strike1, F(TagStrike2, strike1)], [reload1, F(TagReload2, reload1)], attacks);
    }

    /// <summary>
    /// The held model's name (1.1's <c>SetWeapon</c>): <c>WEAPON_MODEL_SKELETONID</c> (<c>broadSword_0</c>), or with
    /// <c>SPECIFIC MODELS</c> the character's own model then the weapon's (<c>christof_claws</c>); empty when it has no model.
    /// </summary>
    public string HeldName(int skeletonId, string actorModel)
    {
        if (Model.Length == 0) return "";
        return (Flags[0] & SpecificModels) != 0 ? $"{Stem(actorModel)}_{Stem(Model)}" : $"{Stem(Model)}_{skeletonId}";
    }

    static string Stem(string name) => name.LastIndexOf('.') is int dot and >= 0 ? name[..dot] : name;
}

/// <summary>One action found in 1.1's set before it is handed to the simulation (<see cref="ActionMotion"/> without the clip's index).</summary>
public sealed record OneOneAction(string Mode, IAnimationClip Clip, float Rate, float StrikeSeconds, float BusySeconds, float HoldSeconds, bool Composed);

/// <summary>What a 1.1 character's action channel plays for the weapon it carries (<see cref="OneOneActions.For"/>).</summary>
public sealed record OneOneActionRoles(IReadOnlyList<OneOneAction> Quick, OneOneAction? Strong, OneOneAction? Block, OneOneAction? Hurt,
                                       OneOneAction? DeathQuick, OneOneAction? DeathSlow, bool[] UpperBody, int[] Spine, int WeaponHand, string Weapon, int Carry);

/// <summary>
/// The action channel's roles from 1.1's motion sets by 1.1's own rules (which animation, and how fast):
/// <list type="bullet">
/// <item><b>The quick blow</b>: the first attack's mode (65 + its melee type; <c>swordslash</c> when the set has none), its
/// variants the run of the mode's entries from the first that are not <c>special</c> (1.1 rolls among them; we
/// take them in turn), or its <c>special</c> when the record asks for it.</item>
/// <item><b>The strong blow</b>: the second attack's mode, its <c>special</c> entry (the broadsword's <c>WEAPON_FLAGS2</c>
/// 0x22c0 asks for it: Christof's whirl); a set with no special plays the mode's first variant.</item>
/// <item>Each attack plays at the rate that makes the whole clip last its <c>STRIKETIME</c>, and the character is busy for
/// its <c>RELOADTIME</c>; its blow lands at the clip's <c>Strike</c> mark (or <c>Fire</c>).</item>
/// <item><b>The block</b>: 1.1 has no block or parry motions (its <c>block1h</c> to <c>parry2h</c> modes are in no set);
/// <c>shield</c> (Christof's <c>chrisShieldBlock</c>) where the set has it, held at its raised frame (where the left hand
/// is highest). Elsewhere <b>a guard composed</b> from the character's own stand, the weapon arm placed by IK, the blade
/// raised up and across the body (<see cref="Guard"/>) — SANDBOX until real ones are made.</item>
/// <item><b>Hurt</b>: <c>hurt</c> + the carry class (<c>hurt_onehand</c>), falling back to <c>hurt</c>. <b>Death</b>:
/// <c>deathquick</c> and <c>deathslow</c>.</item>
/// </list>
/// </summary>
public static class OneOneActions
{
    /// <summary>NAD tag types: <c>Strike</c> (a melee blow) and <c>Fire</c> (a shot).</summary>
    public const uint StrikeTag = 5, FireTag = 4;

    static readonly int HurtBase = Array.IndexOf(NmsMotionSet.BodyModes, "hurt");

    /// <summary>A body mode's name by its number, or empty.</summary>
    public static string ModeName(int index) => (uint)index < (uint)NmsMotionSet.BodyModes.Length ? NmsMotionSet.BodyModes[index] : "";

    /// <summary>The stand, walk and run modes for a carry class, the class's first and the plain one after (1.1's order).</summary>
    public static (string[] Stand, string[] Walk, string[] Run) Locomotion(int carry) =>
        carry <= 0 ? ([NmsMotionSet.Idle, "create"], ["walk"], ["run"])
        : ([ModeName(OneOneWeapon.StandBase + carry), NmsMotionSet.Idle, "create"], [ModeName(OneOneWeapon.WalkBase + carry), "walk"], [ModeName(OneOneWeapon.RunBase + carry), "run"]);

    /// <summary>
    /// The weapon hand of a skeleton whose motion set names neither the right fingers nor a <c>Weapon</c> tag (1.1's
    /// szlachta): by its anatomy table (<see cref="Combat.AnatomyTables"/>), the right hand's first bone and on along its first child in
    /// the same region to the last (the fingers' tip, as the man's <c>RFingers</c>); −1 when the skeleton has no table or no right hand.
    /// </summary>
    static int AnatomyHand(Skeleton skeleton)
    {
        if (Combat.AnatomyTables.For(skeleton) is not { } table) return -1;
        int hand = -1;
        for (int b = 0; b < skeleton.Count && hand < 0; b++)
        {
            int p = skeleton.Bones[b].Parent;
            if (table.Of(b) == Combat.BodyRegion.RightHand && (p < 0 || table.Of(p) != Combat.BodyRegion.RightHand)) hand = b;
        }
        for (bool deeper = hand >= 0; deeper;)
        {
            deeper = false;
            for (int c = hand + 1; c < skeleton.Count; c++)
                if (skeleton.Bones[c].Parent == hand && table.Of(c) == Combat.BodyRegion.RightHand) { hand = c; deeper = true; break; }
        }
        return hand;
    }

    /// <summary>
    /// The roles for a character with this motion set, skeleton, head and limbs carrying <paramref name="weapon"/> (null:
    /// nothing a template names: no blows), <paramref name="clip"/> finding an entry's clip; <paramref name="weaponTag"/> is
    /// the set's <c>Weapon</c> attach point's bone, the weapon hand when the set has no right fingers.
    /// </summary>
    public static OneOneActionRoles For(NmsMotionSet set, Skeleton skeleton, int head, LimbMap? limbs, OneOneWeapon? weapon, Func<NmsMode, IAnimationClip?> clip,
                                        int weaponTag = -1, HeldThing? blade = null)
    {
        int root = limbs?.Root ?? RootOf(skeleton);
        var (upper, spine) = UpperBody(skeleton, root, head);
        // The weapon hand: the right fingers' bone, else the set's Weapon tag (the Teutonic Knight has no RFingers).
        int hand = limbs is { RightArm.Has: true } l ? (l.RightArm.Tip >= 0 ? l.RightArm.Tip : l.RightArm.End) : weaponTag < skeleton.Count ? weaponTag : -1;
        if (hand < 0) hand = AnatomyHand(skeleton);   // else the right hand's last bone by the anatomy table (1.1's szlachta)
        int carry = weapon?.Carry ?? 0;

        var quick = new List<OneOneAction>();
        OneOneAction? strong = null;
        if (weapon is not null)
        {
            int mode1 = OneOneWeapon.AttackModeBase + weapon.MeleeType[0];
            var (variants1, special1) = Entries(set, mode1, clip);
            bool askSpecial1 = (weapon.Flags[0] & OneOneWeapon.SpecialAnimation) != 0;
            if (variants1.Count == 0 && special1 is null) { mode1 = OneOneWeapon.FallbackAttackMode; (variants1, special1) = Entries(set, mode1, clip); }
            List<(NmsMode Mode, IAnimationClip Clip)> firsts = askSpecial1 && special1 is { } sp ? [sp] : variants1;
            foreach (var (m, c) in firsts)
                quick.Add(Attack(m, c, weapon.StrikeTime[0], weapon.ReloadTime[0]));

            int second = weapon.Attacks >= 2 ? 1 : 0;
            int mode2 = OneOneWeapon.AttackModeBase + weapon.MeleeType[second];
            var (variants2, special2) = Entries(set, mode2, clip);
            if (variants2.Count == 0 && special2 is null) (variants2, special2) = Entries(set, OneOneWeapon.FallbackAttackMode, clip);
            if (special2 is { } s2) strong = Attack(s2.Mode, s2.Clip, weapon.StrikeTime[second], weapon.ReloadTime[second]);
            else if (special1 is { } s1) strong = Attack(s1.Mode, s1.Clip, weapon.StrikeTime[second], weapon.ReloadTime[second]);
            else if (variants2.Count > 0) strong = Attack(variants2[0].Mode, variants2[0].Clip, weapon.StrikeTime[second], weapon.ReloadTime[second]);
        }

        OneOneAction? block = null;
        if (First(set, "shield", clip) is { } shield)
            block = new OneOneAction(shield.Mode.Name, shield.Clip, 1f, -1f, 0f, Raised(shield.Clip, skeleton, limbs), false);
        else if (quick.Count > 0)
        {
            // The weapon arm: the right arm of the limbs, else the chain up from the Weapon tag (the Teutonic Knight's).
            var arm = limbs is { RightArm.Has: true } la ? la.RightArm : weaponTag >= 0 ? Limb.FromTip(skeleton, weaponTag) : Limb.None;
            var stand = First(set, ModeName(OneOneWeapon.StandBase + carry), clip) ?? First(set, NmsMotionSet.Idle, clip);
            if (stand is { } st) block = Guard(st.Clip, skeleton, arm, blade, set.Name);
        }

        OneOneAction? Plain(params string[] modes)
        {
            foreach (var name in modes)
                if (name.Length > 0 && First(set, name, clip) is { } f) return new OneOneAction(f.Mode.Name, f.Clip, 1f, -1f, f.Clip.Duration, -1f, false);
            return null;
        }
        var hurt = Plain(carry > 0 ? ModeName(HurtBase + carry) : "", "hurt");
        return new OneOneActionRoles(quick, strong, block, hurt, Plain("deathquick"), Plain("deathslow"), upper, spine, hand, weapon?.Template ?? "", carry);
    }

    static OneOneAction Attack(NmsMode mode, IAnimationClip clip, float strikeTime, float reloadTime)
    {
        // 1.1 stretches the clip to last the strike time (the motion's rate); busy for the reload time.
        float rate = strikeTime > 0.01f && clip.Duration > 0f ? clip.Duration / strikeTime : 1f;
        float strike = -1f;
        if (clip is NadClip nad)
        {
            int i = Array.FindIndex(nad.Tags, t => t.Type == StrikeTag);
            if (i < 0) i = Array.FindIndex(nad.Tags, t => t.Type == FireTag);
            if (i >= 0) strike = nad.Tags[i].Frame / NadClip.FramesPerSecond;
        }
        float busy = reloadTime > 0.01f ? reloadTime : clip.Duration / rate;
        return new OneOneAction(mode.Name, clip, rate, strike, busy, -1f, false);
    }

    /// <summary>A mode's entries whose motion loads: the run of non-special ones from the first (1.1's roll), and the first special.</summary>
    static (List<(NmsMode Mode, IAnimationClip Clip)> Variants, (NmsMode Mode, IAnimationClip Clip)? Special) Entries(NmsMotionSet set, int mode, Func<NmsMode, IAnimationClip?> clip)
    {
        string name = ModeName(mode);
        var variants = new List<(NmsMode, IAnimationClip)>();
        (NmsMode, IAnimationClip)? special = null;
        bool run = true;
        foreach (var m in set.Modes)
        {
            if (m.Kind != NmsModeKind.Body || !m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || clip(m) is not { } c) continue;
            if (m.Has(NmsFlags.Special)) { special ??= (m, c); run = false; }
            else if (run) variants.Add((m, c));
        }
        return (variants, special);
    }

    static (NmsMode Mode, IAnimationClip Clip)? First(NmsMotionSet set, string name, Func<NmsMode, IAnimationClip?> clip)
    {
        foreach (var m in set.Modes)
            if (m.Kind == NmsModeKind.Body && m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && clip(m) is { } c) return (m, c);
        return null;
    }

    /// <summary>The block's raised frame (clip seconds): where the left hand is highest, at 1.1's 30 frames a second.</summary>
    static float Raised(IAnimationClip clip, Skeleton skeleton, LimbMap? limbs)
    {
        int hand = limbs is { LeftArm.Has: true } l ? l.LeftArm.End : -1;
        if (hand < 0) return clip.Duration;
        var local = new BoneTransform[skeleton.Count];
        var model = new Matrix4x4[skeleton.Count];
        float best = 0f, high = float.MinValue;
        int frames = Math.Max(1, (int)MathF.Floor(clip.Duration * NadClip.FramesPerSecond));
        for (int f = 0; f <= frames; f++)
        {
            float t = MathF.Min(f / NadClip.FramesPerSecond, clip.Duration);
            clip.Sample(t, skeleton, local);
            PoseEvaluator.ToModel(skeleton, local, model);
            if (model[hand].Translation.Z > high) { high = model[hand].Translation.Z; best = t; }
        }
        return best;
    }

    /// <summary>
    /// A guard for a set with no <c>shield</c> (ours, SANDBOX until real ones are made), composed from the
    /// character's own stand as the moves' stand-ins were composed: the stand's first frame with the weapon arm placed by IK
    /// (<see cref="LimbIK.Solve"/>) — the wrist in front of the chest and across it, a little below the shoulder — and the
    /// wrist turned so the blade rises up and across the body (<see cref="GuardBlade"/>), held as a one-key composition.
    /// (1.1's swings have no frame that reads as a guard: their wind-ups carry the blade behind the head.)
    /// </summary>
    static OneOneAction? Guard(IAnimationClip stand, Skeleton skeleton, Limb arm, HeldThing? blade, string setName)
    {
        if (!arm.Has) return null;
        int n = skeleton.Count;
        var local = new BoneTransform[n];
        var model = new Matrix4x4[n];
        stand.Sample(0f, skeleton, local);
        PoseEvaluator.ToModel(skeleton, local, model);
        var shoulder = model[arm.Upper].Translation;
        var wrist = new Vector3(GuardWrist.X, shoulder.Y + GuardWrist.Y, shoulder.Z + GuardWrist.Z);
        Quaternion? turn = null;
        if (blade is { Kind: HeldKind.Blade } b && b.Length > 0f && (uint)b.Bone < (uint)n)
        {
            var along = PoseMath.TransformNormal(LimbIK.Unit(b.Tip - b.Guard), model[b.Bone]);
            turn = LimbIK.FromTo(along, GuardBlade);
        }
        LimbIK.Solve(skeleton, local, model, arm, wrist, 1f, new Vector3(0.6f, -0.2f, -0.8f), turnEnd: turn);
        var guard = new ComposedClip($"{setName}:guard({stand.Name})", 1f, false, [new ComposedKey(0f, local)]);
        return new OneOneAction("guard", guard, 1f, -1f, 0f, 0f, true);
    }

    /// <summary>Where the composed guard puts the weapon wrist (model space, m): across the body's middle, in front of the shoulder, below it (SANDBOX).</summary>
    public static readonly Vector3 GuardWrist = new(-0.05f, 0.38f, -0.15f);

    /// <summary>Which way the composed guard's blade points (model space): up, to the left and a little forwards (SANDBOX).</summary>
    public static readonly Vector3 GuardBlade = LimbIK.Unit(new Vector3(-0.45f, 0.3f, 0.84f));

    static int RootOf(Skeleton skeleton)
    {
        for (int b = 0; b < skeleton.Count; b++)
            if (skeleton.Bones[b].Parent < 0 || skeleton.Bones[b].Parent >= b) return b;
        return 0;
    }

    /// <summary>
    /// The upper body and the spine (ours): the bones from the head down to the root; the spine's first bone is the one
    /// whose parent is the root (14 on skeleton 0), the spine the bones from it up to the neck's parent (14, 15, 16), and the
    /// upper body the spine's first bone with every bone under it (the arms, the head, the cape and what the hands hold).
    /// </summary>
    public static (bool[] Upper, int[] Spine) UpperBody(Skeleton skeleton, int root, int head)
    {
        int n = skeleton.Count;
        var upper = new bool[n];
        if ((uint)head >= (uint)n) { Array.Fill(upper, true); return (upper, []); }
        var path = new List<int>();
        for (int b = head; b >= 0 && b < n && b != root; b = skeleton.Bones[b].Parent is int p && p < b ? p : -1) path.Add(b);
        path.Reverse();                  // root's child first, the head last
        if (path.Count == 0) { Array.Fill(upper, true); return (upper, []); }
        int first = path[0];
        for (int b = 0; b < n; b++)
        {
            int p = skeleton.Bones[b].Parent;
            upper[b] = b == first || (p >= 0 && p < b && upper[p]);
        }
        // The spine: up to the neck's parent (the head and the neck are not bent: the eye stays level on its own).
        int spineCount = Math.Max(1, path.Count - 2);
        return (upper, [.. path.Take(spineCount)]);
    }

    /// <summary>
    /// Hands the roles to the simulation's assets, each clip once (<paramref name="added"/> shared with the character's other
    /// clips). the kind's <paramref name="stitched"/> blows too, after every 1.1 clip (so 1.1's clips keep
    /// their indices); <paramref name="stitchedAsQuick"/> (<c>overhead</c>, <c>thrust</c> or <c>both</c>; <c>--stitched-as-quick</c>,
    /// for measuring and filming them before the direction table plays them) makes them the quick blow's variants in place of
    /// 1.1's, for a kind that has them.
    /// </summary>
    public static ActionSet Register(CharacterAssets assets, OneOneActionRoles roles, Dictionary<IAnimationClip, int> added,
                                     IReadOnlyList<OneOneStitchedBlow>? stitched = null, string? stitchedAsQuick = null,
                                     IReadOnlyList<(string From, StitchedClip Clip)>? flat = null)
    {
        ActionMotion Of(OneOneAction? a)
        {
            if (a is null) return ActionMotion.None;
            if (!added.TryGetValue(a.Clip, out int clip)) added[a.Clip] = clip = assets.AddClip(a.Clip);
            return new ActionMotion(clip, a.Rate, a.StrikeSeconds, a.BusySeconds, a.HoldSeconds, a.Composed, a.Mode);
        }
        var set = new ActionSet
        {
            Quick = [.. roles.Quick.Select(Of)], Strong = Of(roles.Strong), Block = Of(roles.Block), Hurt = Of(roles.Hurt),
            DeathQuick = Of(roles.DeathQuick), DeathSlow = Of(roles.DeathSlow), UpperBody = roles.UpperBody, Spine = roles.Spine,
            WeaponHand = roles.WeaponHand, Weapon = roles.Weapon, Carry = roles.Carry,
        };
        // The side blows flattened, in 1.1's blows' places (their rate, Strike mark and busy time), registered after the rest.
        if (flat is { Count: > 0 })
        {
            var places = set.Quick;
            for (int i = 0; i < places.Length; i++)
            {
                string name = roles.Quick[i].Clip.Name;
                foreach (var (from, clip) in flat)
                {
                    if (!string.Equals(from, name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!added.TryGetValue(clip, out int index)) added[clip] = index = assets.AddClip(clip);
                    places[i] = places[i] with { Clip = index, Composed = true };
                    break;
                }
            }
        }
        if (stitched is null || stitched.Count == 0) return set;
        // The stitched blows, each its own clip of the kind (made for its stand), after 1.1's.
        var motions = new StitchedMotion[stitched.Count];
        for (int i = 0; i < stitched.Count; i++)
        {
            var b = stitched[i];
            if (!added.TryGetValue(b.Clip, out int clip)) added[b.Clip] = clip = assets.AddClip(b.Clip);
            motions[i] = new StitchedMotion(b.Direction, new ActionMotion(clip, b.Rate, b.StrikeSeconds, b.BusySeconds, -1f, true, b.Direction), b.ReadySeconds, b.From);
        }
        set.Stitched = motions;
        if (stitchedAsQuick is not { Length: > 0 } asQuick) return set;
        var quick = new List<ActionMotion>(2);
        foreach (var m in motions)
            if (asQuick == "both" || asQuick == m.Direction) quick.Add(m.Motion);
        return quick.Count == 0 ? set : new ActionSet
        {
            Quick = [.. quick], Strong = set.Strong, Block = set.Block, Hurt = set.Hurt, DeathQuick = set.DeathQuick, DeathSlow = set.DeathSlow,
            UpperBody = set.UpperBody, Spine = set.Spine, WeaponHand = set.WeaponHand, Weapon = set.Weapon, Carry = set.Carry, Stitched = motions,
        };
    }
}
