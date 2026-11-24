// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours.
using System.Runtime.CompilerServices;
using Vitomir.Formats.L2;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Combat;

/// <summary>
/// The active window's rule (<b>SANDBOX</b>: the rules package's <c>attack_timing_curves</c>, gate-18, is
/// null, so the window comes from the motion): the frames round a blow's <c>Strike</c> mark where its striking point
/// moves faster than <see cref="ShareOfPeak"/> of the fastest it moves in the clip (for
/// <c>chrisAttackSword</c> about frames 10 to 18 of 29). The module's data gives the numbers
/// (<c>Data/combat/weapons.json</c>); these are the defaults a world without a module has.
/// </summary>
public sealed record ActiveWindowRule
{
    /// <summary>The share of the clip's fastest speed the point must move at to be in the window (0.4).</summary>
    public float ShareOfPeak { get; init; } = 0.4f;

    /// <summary>The clip seconds between the samples the speeds are measured over (1/240 s: eight a 30 fps frame).</summary>
    public float SampleSeconds { get; init; } = 1f / 240f;

    /// <summary>A slow moment no longer than this (clip seconds) between two fast stretches is bridged: a motion's key can brake the point mid-swing (<c>chrisAttackSword</c> at frame 16, for under a frame).</summary>
    public float BridgeSeconds { get; init; } = 0.05f;

    /// <summary>The shortest a window may be (clip seconds), stood round the mark when the motion gives less (a clip whose mark falls where the point is slow).</summary>
    public float LeastSeconds { get; init; } = 0.1f;

    /// <summary>
    /// Where the strike proper begins, by clip (<see cref="StrikeStart"/>): a window does not open before it, so
    /// the blade's move out of a wind-up's ready pose into the blow (the descent from the shoulder, the raise behind the head) is
    /// neither swept for contact nor drawn by the swing's arc. The extraneous upward hook on the sword
    /// path in the drawn arc is no damage path, only the wind-up; the arc across the body is
    /// the actual swing and damage. The module's data (<c>weapons.json</c>'s <c>activeWindow.strikeStarts</c>) gives them; these
    /// are the same (ENGINE_SANDBOX, ours). Empty (the module's switch <c>window-from-windup</c>): the earlier build's windows exactly.
    /// </summary>
    public IReadOnlyList<StrikeStart> StrikeStarts { get; init; } = StrikeStart.Defaults;
}

/// <summary>How a blow's strike proper is found in its motion (<see cref="StrikeStart"/>).</summary>
public enum StrikeStartRule : byte
{
    /// <summary>A side blow: where its blade is first brought level — the tip within <see cref="StrikeStart.Metres"/> of the guard's height (the body's frame before the aim bends it: its sweep).</summary>
    Level = 0,
    /// <summary>An overhead: where the tip, raised, starts down (its highest point).</summary>
    Top = 1,
    /// <summary>A thrust: where the tip starts forward (along the body's front).</summary>
    Forward = 2,
}

/// <summary>
/// Where a family of blows' strike proper begins (ENGINE_SANDBOX, ours): for the clips named
/// (<see cref="Clips"/>, any case), the active window (<see cref="ActiveWindows"/>) begins no earlier than the first moment of
/// the motion's fast run at which <see cref="Rule"/> holds. Found on the same samples as the window, in the kind's model space.
/// </summary>
public sealed record StrikeStart(IReadOnlyList<string> Clips, StrikeStartRule Rule, float Metres = 0.1f)
{
    /// <summary>
    /// The platform's own (the enhanced module's <c>weapons.json</c> gives the same; a test checks): the four directions' wind-ups
    /// (the earlier build's <c>windup:*</c>) — the side blows from where the blade is level, the overhead from its top, the thrust from where
    /// it starts forward. 1.1's own clips (<c>swings-oneone</c>), the earlier build's stitched blows and the earlier build's flattened ones are not named.
    /// </summary>
    public static readonly IReadOnlyList<StrikeStart> Defaults =
    [
        new(["windup:left", "windup:right"], StrikeStartRule.Level, 0.1f),
        new(["windup:overhead"], StrikeStartRule.Top),
        new(["windup:thrust"], StrikeStartRule.Forward),
    ];

    /// <summary>The row naming <paramref name="clip"/> in <paramref name="rows"/>, or null.</summary>
    public static StrikeStart? For(IReadOnlyList<StrikeStart> rows, string clip)
    {
        foreach (var r in rows)
            foreach (var c in r.Clips)
                if (string.Equals(c, clip, StringComparison.OrdinalIgnoreCase)) return r;
        return null;
    }

    /// <summary>The rule's word in the data (<c>level</c>, <c>top</c>, <c>forward</c>).</summary>
    public static readonly IReadOnlyList<string> RuleNames = ["level", "top", "forward"];
}

/// <summary>How a stopped blade is drawn going back to guard (<see cref="BladeStopRule.BringBack"/>).</summary>
public enum BringBackKind : byte
{
    /// <summary>The swing's clip played backwards from where it stopped to its start: it retraces its path, so it never passes through what it stopped in.</summary>
    Reverse = 0,
    /// <summary>The stopped pose blended into the stance (the locomotion) over the bring-back: the blade drawn straight back to guard.</summary>
    Blend = 1,
}

/// <summary>
/// Where a blade stops (ours; <i>"the weapon swing stops where it actually stops"</i>,
/// <i>"I'd prefer the animation not carry through the rest of the "swing" aspect, and start the "bring blade back to ready"
/// animation at the end"</i>; <b>SANDBOX</b> numbers, the module's data <c>Data/combat/weapons.json</c>'s <c>stop</c>). Every
/// contact with a body <b>bites</b>: the swing's clip held at the contact for <see cref="BiteSeconds"/>; then, as the rules
/// say (<see cref="HarmResolved.Continues"/>), it goes on or stops there and is brought back to guard over the rest of the
/// swing's time (<see cref="ReadySecondsLeast"/> to <see cref="ReadySecondsMost"/>). A shield, a parrying blade or the level
/// makes it spring back first (<see cref="ReboundSeconds"/> at <see cref="ReboundRate"/> times its rate, backwards).
/// <see cref="Enabled"/> false is the first contact rule exactly (the blade through bodies, one contact per swing and person, a
/// stop cut short at once): the enhanced module's switch <c>--contact-stop-off</c>.
/// </summary>
public sealed record BladeStopRule
{
    /// <summary>Whether blades stop where they stop; false is the first contact rule.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>How long a blade that met a body is held there before it goes on or is brought back (real s; SANDBOX 0.05).</summary>
    public float BiteSeconds { get; init; } = 0.05f;

    /// <summary>How long a blade that met a shield, a parrying blade or the level springs back first (real s; SANDBOX 0.1).</summary>
    public float ReboundSeconds { get; init; } = 0.1f;

    /// <summary>
    /// How far its tip springs back over <see cref="ReboundSeconds"/> (m; SANDBOX 0.15: a slight rebound of the
    /// weapon): the clip played backwards at the rate that moves the tip this far at the speed it met with
    /// (<see cref="ContactMade.TipSpeed"/>). 0 springs it back at <see cref="ReboundRate"/> alone.
    /// </summary>
    public float ReboundMetres { get; init; } = 0.15f;

    /// <summary>
    /// How fast it springs back at most: this many times the rate the swing played at, backwards (SANDBOX 2). Looked at: at
    /// 2× for 0.1 s the clip's 0.2 s back throws a forehand met at 48 m/s back into its wind-up, so
    /// <see cref="ReboundMetres"/> sets the rate and this only caps it.
    /// </summary>
    public float ReboundRate { get; init; } = 2f;

    /// <summary>The shortest a bring-back to guard may be (real s; SANDBOX 0.15): a blade stopped near its swing's end still takes this long.</summary>
    public float ReadySecondsLeast { get; init; } = 0.15f;

    /// <summary>The longest a bring-back to guard may be (real s; SANDBOX 0.8): a blade stopped early does not drift back for the whole swing.</summary>
    public float ReadySecondsMost { get; init; } = 0.8f;

    /// <summary>How the bring-back is drawn (<see cref="BringBackKind"/>; SANDBOX: the one that reads as pulling the blade back to guard).</summary>
    public BringBackKind BringBack { get; init; } = BringBackKind.Blend;

    /// <summary>
    /// The first stop kept as a choice: a stopped swing (a body's stop or a rebound) is cut short at once —
    /// held where it met and faded over the channel's fade-out — with no bring-back (false by default).
    /// </summary>
    public bool StopFreesOwnerAtOnce { get; init; }

    /// <summary>Kept as a number: a blocked swing's owner is busy this much longer (real s; 0 by default: none).</summary>
    public float BlockedStaggerSeconds { get; init; }

    // ---- the rebound scaled by the blow, the arm's recoil, the attacker's stun ----:
    // "a kind of 'swing and weapon bounces off' animation kind of how it looks in Bannerlord when a
    // swing is blocked ... A kind of light rebound of the weapon like that is what I had in mind." ENGINE_SANDBOX, the module's
    // data (weapons.json's stop); the module's switch rebound-fixed puts back the earlier build's rebound (ReboundMetres for every blow, no
    // recoil, no stun).

    /// <summary>
    /// Whether a rebound's distance comes from the blow — the contact's <see cref="ContactMade.NormalImpulse"/> (the weapon's
    /// mass × the speed into what it met: a cleaner, faster or heavier blow springs back further; a glancing one less) times
    /// <see cref="ReboundMetresPerImpulse"/>, within <see cref="ReboundLeastMetres"/>–<see cref="ReboundMostMetres"/>, over
    /// <see cref="ReboundScaledSeconds"/> — and the arm recoils and the owner is stunned. False: the earlier build's <see cref="ReboundMetres"/>
    /// over <see cref="ReboundSeconds"/> for every blow, no recoil, no stun.
    /// </summary>
    public bool ReboundByImpulse { get; init; } = true;

    /// <summary>The tip's travel back per N·s of the contact's impulse (m; ENGINE_SANDBOX 0.012: a shield met at 28 N·s, 0.34 m).</summary>
    public float ReboundMetresPerImpulse { get; init; } = 0.012f;

    /// <summary>The least a blocked tip springs back (m; ENGINE_SANDBOX 0.15, the earlier build's whole rebound).</summary>
    public float ReboundLeastMetres { get; init; } = 0.15f;

    /// <summary>The most a blocked tip springs back before a parry's scale (m; ENGINE_SANDBOX 0.5).</summary>
    public float ReboundMostMetres { get; init; } = 0.5f;

    /// <summary>How long the scaled rebound takes (real s; ENGINE_SANDBOX 0.12), then the bring-back as before.</summary>
    public float ReboundScaledSeconds { get; init; } = 0.12f;

    /// <summary>
    /// The level's rebound by its material (a factor on the distance; ENGINE_SANDBOX: stone and steel 1, wood 0.8, leather 0.5,
    /// earth 0.4, cloth and flesh 0.3); a shield and a blade are 1 (their guard's verdict scales them).
    /// </summary>
    public MaterialFactors ReboundByMaterial { get; init; } = MaterialFactors.Default;

    /// <summary>The weapon hand's recoil per N·s of impulse (m; ENGINE_SANDBOX 0.003: a shield met at 28 N·s pushes the hand back 8.5 cm).</summary>
    public float RecoilMetresPerImpulse { get; init; } = 0.003f;

    /// <summary>The most the weapon hand recoils before a parry's scale (m; ENGINE_SANDBOX 0.12).</summary>
    public float RecoilMostMetres { get; init; } = 0.12f;

    /// <summary>When the recoil is at its most after the meeting (real s; ENGINE_SANDBOX 0.06): a kick, then a critically damped return.</summary>
    public float RecoilPeakSeconds { get; init; } = 0.06f;

    /// <summary>When the recoil is let go (real s; ENGINE_SANDBOX 0.45: about 1 % of its most is left then).</summary>
    public float RecoilSettleSeconds { get; init; } = 0.45f;

    /// <summary>The upper spine's lean with the recoil, per metre of the hand's (radians; ENGINE_SANDBOX 1.2: 8 cm leans it 5.5°).</summary>
    public float RecoilLeanPerMetre { get; init; } = 1.2f;

    /// <summary>The wrist's turn tipping the blade back with the recoil, per metre of the hand's (radians; ENGINE_SANDBOX 2.5: 8 cm turns it 11°).</summary>
    public float RecoilWristPerMetre { get; init; } = 2.5f;

    /// <summary>
    /// The attacker's stun after a blade held at a guard (real s from the meeting; ENGINE_SANDBOX 0.25, the low end of
    /// Bannerlord's modders' 0.25–0.5 s): no attack begins until it is over (a press in it is refused and buffered); a guard may
    /// be raised and a dodge made. It runs beside the busy time (1.1's <c>RELOADTIME</c>), which still holds the next blow.
    /// </summary>
    public float StunHeldSeconds { get; init; } = 0.25f;

    /// <summary>The stun after a parried (or clash-deflected) blade (real s from the meeting; ENGINE_SANDBOX 0.5, Bannerlord's unmodded figure).</summary>
    public float StunParriedSeconds { get; init; } = 0.5f;

    /// <summary>
    /// Whether a blocked swing's owner may raise the guard once its rebound is over, though its busy time runs on (Bannerlord:
    /// the stunned attacker may block; ENGINE_SANDBOX true). False: the earlier build's — the guard rises when the busy time ends.
    /// </summary>
    public bool GuardAfterRebound { get; init; } = true;

    /// <summary>
    /// Whether the stun counts from the end of the blocked swing's busy time rather than from the meeting — a blocked blow then
    /// costs its owner the stun on top of the whole swing's time (Bannerlord's feel, where the defender gains the initiative; false
    /// by default: at 1.1's timing the busy time outlasts a stun counted from the meeting, so it delays a blow only when the swing is
    /// fast or met late). The switch <c>rebound-stun-after</c>.
    /// </summary>
    public bool StunFromSwingEnd { get; init; }

    /// <summary>The earlier build's rebound for every blow, no recoil, no stun, the guard rising when the busy time ends (the switch <c>rebound-fixed</c>).</summary>
    public BladeStopRule FixedRebound() => this with { ReboundByImpulse = false, GuardAfterRebound = false };

    /// <summary>The scaled rebound's and the recoil's distances times <paramref name="k"/> (the film's small and large: switches <c>rebound-small</c>, <c>rebound-large</c>).</summary>
    public BladeStopRule Sized(float k) => this with
    {
        ReboundMetresPerImpulse = ReboundMetresPerImpulse * k, ReboundLeastMetres = ReboundLeastMetres * k, ReboundMostMetres = ReboundMostMetres * k,
        RecoilMetresPerImpulse = RecoilMetresPerImpulse * k, RecoilMostMetres = RecoilMostMetres * k,
    };
    // ---- end
}

/// <summary>A factor for each <see cref="ContactMaterial"/> (a value, compared by value: the rule stays a plain record).</summary>
public readonly record struct MaterialFactors(float Flesh, float Cloth, float Leather, float Wood, float Steel, float Stone, float Earth)
{
    /// <summary>ENGINE_SANDBOX: a wall's rebound by what it is made of.</summary>
    public static readonly MaterialFactors Default = new(0.3f, 0.3f, 0.5f, 0.8f, 1f, 1f, 0.4f);

    /// <summary>The factor for <paramref name="m"/> (1 for one unknown).</summary>
    public float this[ContactMaterial m] => m switch
    {
        ContactMaterial.Flesh => Flesh, ContactMaterial.Cloth => Cloth, ContactMaterial.Leather => Leather, ContactMaterial.Wood => Wood,
        ContactMaterial.Steel => Steel, ContactMaterial.Stone => Stone, ContactMaterial.Earth => Earth, _ => 1f,
    };

    /// <summary>These with <paramref name="m"/>'s factor <paramref name="f"/>.</summary>
    public MaterialFactors With(ContactMaterial m, float f) => m switch
    {
        ContactMaterial.Flesh => this with { Flesh = f }, ContactMaterial.Cloth => this with { Cloth = f }, ContactMaterial.Leather => this with { Leather = f },
        ContactMaterial.Wood => this with { Wood = f }, ContactMaterial.Steel => this with { Steel = f }, ContactMaterial.Stone => this with { Stone = f },
        ContactMaterial.Earth => this with { Earth = f }, _ => this,
    };
}

/// <summary>Whether a held blade parries a blow it meets (SANDBOX until the rules package's guard direction).</summary>
public enum ParryRule : byte
{
    /// <summary>A blade never stops another: blows pass blades by.</summary>
    Never = 0,
    /// <summary>A blade stops a blow only while its holder holds the block (the guard raised): the default.</summary>
    WhileBlocking = 1,
    /// <summary>Any blade in the way stops a blow (two blows crossing clash).</summary>
    Always = 2,
}

/// <summary>A weapon's matter (SANDBOX until the rules package's materials): what it is made of and its mass (kg), for the impulse.</summary>
public readonly record struct WeaponMatter(ContactMaterial Material, float MassKg);

/// <summary>A rule naming the level's surfaces: a level material whose name holds <see cref="Contains"/> (case aside) is <see cref="Material"/>.</summary>
public readonly record struct SurfaceRule(string Contains, ContactMaterial Material);

/// <summary>
/// What contact needs from the module's data (a service, not digested; SANDBOX until the rules package's anatomy and
/// materials): the active window's rule, each weapon's matter (by 1.1 template, else by its carry class), each shield's
/// material (by its held model's template), the body's material, the level's surfaces (by the names of the level's
/// materials, else <see cref="LevelDefault"/>), whether blades parry, the bare hand's striking ball, and the people
/// protected from weapons (by template or catalogue name). A world with no module keeps these
/// defaults. Filled before the first tick (the modules' <c>Register</c>); <see cref="Prepare"/> resolves each kind of
/// character and the level's materials once, so a tick only indexes arrays.
/// </summary>
public sealed class ContactConfig
{
    /// <summary>Whether the numbers are the engine's research numbers (always, until the rules package's).</summary>
    public bool Sandbox { get; set; } = true;

    public ActiveWindowRule Window { get; set; } = new();

    /// <summary>Each 1.1 carry class's matter (<c>WEAPON_TYPE1</c>: 0 bare hands, 1 one-handed, 2 two-handed blunt, 3 two-handed sword, 4 a pole), SANDBOX masses.</summary>
    public WeaponMatter[] Classes { get; set; } =
    [
        new(ContactMaterial.Flesh, 0.6f), new(ContactMaterial.Steel, 1.4f), new(ContactMaterial.Steel, 3.0f),
        new(ContactMaterial.Steel, 2.5f), new(ContactMaterial.Steel, 3.0f),
    ];

    /// <summary>A weapon's matter by its 1.1 template (<c>BroadSword</c>), over its class's.</summary>
    public Dictionary<string, WeaponMatter> Weapons { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A shield's material by its held model's 1.1 template (<c>weapRapier_shield</c>), else <see cref="ShieldDefault"/>.</summary>
    public Dictionary<string, ContactMaterial> Shields { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ContactMaterial ShieldDefault { get; set; } = ContactMaterial.Wood;

    /// <summary>What a body is (flesh: the regions' armour is the rules' materials).</summary>
    public ContactMaterial Body { get; set; } = ContactMaterial.Flesh;

    /// <summary>The level's surfaces named by their materials' names, the first rule that matches; else <see cref="LevelDefault"/>.</summary>
    public List<SurfaceRule> Surfaces { get; } = [];

    public ContactMaterial LevelDefault { get; set; } = ContactMaterial.Stone;

    public ParryRule Parry { get; set; } = ParryRule.WhileBlocking;

    /// <summary>The ball a bare hand strikes with (m): its radius about the weapon hand's joint.</summary>
    public float HandRadius { get; set; } = 0.06f;

    /// <summary>How far round a fighter's feet its blow can reach at most (m): the near test before any volume is posed.</summary>
    public float ReachAtMost { get; set; } = 3.5f;

    /// <summary>Where a blade stops (<see cref="BladeStopRule"/>: the bite, the follow-through, the bring-back, the rebound).</summary>
    public BladeStopRule Stop { get; set; } = new();

    /// <summary>The people protected from weapons, by the name of their kind (a 1.1 template, <c>Civ03merchant1</c>, or a catalogue name, <c>Merchant</c>).</summary>
    public HashSet<string> Protected { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Resolved by Prepare: per kind of character, its weapon's matter, its shield's material and whether it is protected;
    // per level material, its contact material.
    WeaponMatter[] kindWeapon = [];
    ContactMaterial[] kindShield = [];
    bool[] kindProtected = [];
    int kinds;
    ContactMaterial[] levelMaterials = [];

    /// <summary>The level's material names, as <see cref="Prepare"/> read them (for the docs and the log).</summary>
    public IReadOnlyList<string> LevelMaterialNames { get; private set; } = [];

    /// <summary>How many of the level's materials a rule names (the rest are <see cref="LevelDefault"/>).</summary>
    public int LevelMaterialsKnown { get; private set; }

    /// <summary>
    /// Resolves every kind of character <paramref name="assets"/> holds now (its weapon's matter, its shield's, whether
    /// it is protected) and, when <paramref name="level"/> came from a package, the level's materials by name. At load,
    /// after the modules have filled the config; a kind added later is resolved the first time it is asked about.
    /// </summary>
    public void Prepare(CharacterAssets? assets, LevelSpace? level)
    {
        if (assets is not null) for (int k = 0; k < assets.KindCount; k++) Resolve(assets, k);
        if (level is null) return;
        var names = new List<string>();
        if (level.Source is { Length: > 0 } source && Directory.Exists(source))
        {
            try { names.AddRange(L2Package.Open(source).Manifest.Materials); }
            catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException) { }
        }
        LevelMaterialNames = names;
        levelMaterials = new ContactMaterial[names.Count];
        int known = 0;
        for (int i = 0; i < names.Count; i++)
        {
            levelMaterials[i] = LevelDefault;
            foreach (var rule in Surfaces)
                if (names[i].Contains(rule.Contains, StringComparison.OrdinalIgnoreCase)) { levelMaterials[i] = rule.Material; known++; break; }
        }
        LevelMaterialsKnown = known;
    }

    /// <summary>The level's surface at a triangle (its material by name), or <see cref="LevelDefault"/>.</summary>
    public ContactMaterial LevelSurface(CollisionWorld level, int triangle)
    {
        if ((uint)triangle >= (uint)level.Materials.Length) return LevelDefault;
        int m = level.Materials[triangle];
        return (uint)m < (uint)levelMaterials.Length ? levelMaterials[m] : LevelDefault;
    }

    /// <summary>The matter of the weapon a kind of character carries (its template's, else its class's).</summary>
    public WeaponMatter WeaponOf(CharacterAssets assets, int kind) { Resolve(assets, kind); return kindWeapon[kind]; }

    /// <summary>The material of a kind of character's shield.</summary>
    public ContactMaterial ShieldOf(CharacterAssets assets, int kind) { Resolve(assets, kind); return kindShield[kind]; }

    /// <summary>Whether a kind of character is protected by the data (its name in <see cref="Protected"/>).</summary>
    public bool KindProtected(CharacterAssets assets, int kind) { Resolve(assets, kind); return kindProtected[kind]; }

    /// <summary>The matter of a carry class (bare hands for one unknown).</summary>
    public WeaponMatter ClassOf(int carry) => (uint)carry < (uint)Classes.Length ? Classes[carry] : Classes.Length > 0 ? Classes[0] : new(ContactMaterial.Flesh, 0.6f);

    void Resolve(CharacterAssets assets, int kind)
    {
        if ((uint)kind < (uint)kinds) return;
        if (kind >= kindWeapon.Length)
        {
            int size = Math.Max(kind + 1, Math.Max(8, kindWeapon.Length * 2));
            Array.Resize(ref kindWeapon, size);
            Array.Resize(ref kindShield, size);
            Array.Resize(ref kindProtected, size);
        }
        for (int k = kinds; k <= kind && k < assets.KindCount; k++)
        {
            var c = assets.Kind(k);
            var set = c.Actions;
            var matter = ClassOf(set?.Carry ?? 0);
            if (set is { Weapon.Length: > 0 } && Weapons.TryGetValue(set.Weapon, out var w)) matter = w;
            kindWeapon[k] = matter;
            var shield = ShieldDefault;
            foreach (var held in c.Held)
                if (Shields.TryGetValue(held.Template, out var s)) { shield = s; break; }
            kindShield[k] = shield;
            kindProtected[k] = Protected.Contains(c.Name);
            kinds = k + 1;
        }
    }

    /// <summary>The material's word for the log (<c>steel</c>).</summary>
    public static string Word(ContactMaterial m) => m switch
    {
        ContactMaterial.Flesh => "flesh", ContactMaterial.Cloth => "cloth", ContactMaterial.Leather => "leather", ContactMaterial.Wood => "wood",
        ContactMaterial.Steel => "steel", ContactMaterial.Stone => "stone", ContactMaterial.Earth => "earth", _ => m.ToString().ToLowerInvariant(),
    };

    /// <summary>A material by its word (<c>steel</c>), or null.</summary>
    public static ContactMaterial? Parse(string? word) =>
        Enum.TryParse<ContactMaterial>(word, ignoreCase: true, out var m) && Enum.IsDefined(m) && !int.TryParse(word, out _) ? m : null;
}

/// <summary>
/// A person marked protected: weapons never harm them. A blow at one is found and
/// published with <see cref="ContactMade.Protected"/>, plays no hurt, and harm does not follow; the attempt is an act the
/// world sees, and they protest. Given by the program's <c>--protected &lt;who&gt;</c> (on the tape); a kind of character
/// named in the module's data is protected without it (<see cref="ContactConfig.Protected"/>). Digested.
/// </summary>
public struct ProtectedPerson
{
    public int Marked;
}

/// <summary>Helpers on protected people.</summary>
public static class ProtectedPeople
{
    /// <summary>Whether <paramref name="e"/> is protected: marked so (<see cref="ProtectedPerson"/>), or of a kind the data protects.</summary>
    public static bool Is(World world, EntityId e)
    {
        var stores = world.Stores;
        for (int i = 0; i < stores.Count; i++)
            if (stores[i] is Store<ProtectedPerson> marks && marks.Has(e)) return true;
        if (!world.TryService<ContactConfig>(out var config) || !world.TryService<CharacterAssets>(out var assets)) return false;
        ref var anim = ref world.Store<CharacterAnimation>().Find(e);
        return !Unsafe.IsNullRef(ref anim) && config.KindProtected(assets, anim.Kind);
    }

    /// <summary>Marks <paramref name="e"/> protected (the program's <c>--protected</c>).</summary>
    public static void Mark(World world, EntityId e) => world.Set(e, new ProtectedPerson { Marked = 1 });

    /// <summary>The tape head's note for <c>--protected</c> (the text as given).</summary>
    public const string TapeNote = "protected";
}
