// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Modules.Enhanced.Configuration;

namespace Vitomir.Modules.Enhanced.Rules;

/// <summary>
/// The contest on contact (ours: <i>"on contact a contested pool (Dexterity + Melee against the
/// defender's Dexterity + Dodge or Melee) adds or takes damage dice — a well-placed blow, never a gate on whether it
/// landed"</i>; the rules package allows it <b>only as an explicitly selectable research comparator</b>, so it runs
/// only with the module's <c>skill-contest</c> switch), a stage of the staged harm on a <b>body</b> contact, after the damage
/// pool is made whole and before it is rolled (<see cref="HarmRules.Resolve"/>):
/// <list type="number">
/// <item>the attack pool <c>φ(E_dexterity) + Melee</c> (Brawl for a bare-handed class: <see cref="SkillData.IsBareHanded"/>);</item>
/// <item>the defence pool <c>φ(E_dexterity)</c>, <b>plus Melee only while the defender was guarding</b> (its block raised)
/// when struck — Brawl when it guards bare-handed — <b>or Dodge only while it was dodging</b> (the rules
/// thread's the platform's dodge <b>in its burst</b> when the blow lands — the body really moving out of the blade's
/// path — not its start, the weight still being shifted, nor its recovery; a dodge lowers the block, so never both). No unperformed
/// defence ("a passive defender cannot claim a chosen Block/Parry/Dodge they never performed"): otherwise Dodge is
/// traced, not used;</item>
/// <item>φ is <see cref="SkillData.DexterityDice"/> (SANDBOX <c>raw/20</c>: the profile has no Dexterity conversion,
/// gate-01 and gate-04 are open); each pool made whole (<see cref="Dice.Pool"/>, from the contest's own pool stream) and
/// rolled at the profile's <c>damage_d10_difficulty</c>, the attack from its stream and the defence from its own — new
/// streams, so the staged harm's own draws are the same whether the contest runs or not;</item>
/// <item>net = attack successes − defence successes; the damage pool gains a positive net (at most
/// <see cref="SkillData.AddMost"/>, null for no cap) and loses a negative one, but never more than
/// <c>floor(share × pool)</c> (<see cref="SkillData.TakeMostShare"/>, SANDBOX ½), and never below nothing.</item>
/// </list>
/// It never decides whether the blow landed: the contact is resolved whatever it says, and a pool that comes to nothing is
/// a contact resisted, as ever. Allocates nothing (the draws are written to the result's list).
/// </summary>
public sealed class SkillContest
{
    SkillContest(SkillData data, int difficulty)
    {
        Data = data;
        Difficulty = difficulty;
    }

    public SkillData Data { get; }

    /// <summary>
    /// (The rules package: <i>"Dodge only counts when an actual eligible Dodge was
    /// executed. Wits/Perception affect seeing/responding, Dexterity/Dodge affect execution, with no free passive evasion roll after
    /// a real hit"</i>): with it, the defence's φ(Dexterity) counts only alongside a performed defence — the guard raised, or a
    /// dodge in its burst — and a defender who performed none has an empty defence pool (the attacker's net unopposed). Off by
    /// default (the module's switch <c>contest-dexterity-performed</c>): the first contest's, φ(Dexterity) always. The rules package decides.
    /// </summary>
    public bool DexterityOnlyPerformed { get; set; }

    /// <summary>The difficulty both pools are rolled at (the profile's <see cref="SkillData.DifficultyProfileValue"/>).</summary>
    public int Difficulty { get; }

    /// <summary>The contest from the settled configuration, or null with the reason added to <paramref name="refusals"/> (the profile's difficulty unreadable).</summary>
    public static SkillContest? Build(EnhancedConfig config, SkillData data, List<string> refusals)
    {
        string key = data.DifficultyProfileValue;
        double v;
        try { v = config.Number(key); }
        catch (Exception e) when (e is UnresolvedValueException or KeyNotFoundException or InvalidOperationException)
        {
            refusals.Add($"the contest on contact: {e.Message}");
            return null;
        }
        if (v != Math.Floor(v) || v < 2 || v > 10) { refusals.Add($"the contest on contact: value '{key}' is {v}: a d10's difficulty is a whole number from 2 to 10"); return null; }
        return new SkillContest(data, (int)v);
    }

    /// <summary>
    /// Rolls the contest of <paramref name="a"/>'s blow with a weapon of <paramref name="cls"/> against <paramref name="t"/>
    /// and applies its net to <paramref name="r"/>'s damage pool (<see cref="HarmResult.DamagePool"/>, already whole), writing
    /// every number to <paramref name="r"/>.
    /// </summary>
    public void Roll(in Striker a, in Struck t, WeaponClass cls, IDeterministicRandom random, HarmResult r)
    {
        var d = Data;
        bool bare = d.IsBareHanded(cls.Id);
        r.Contested = true;
        r.AttackAbilityId = bare ? d.BareAbility : d.ArmedAbility;
        r.AttackAbility = bare ? a.Brawl : a.Melee;
        r.AttackDexterity = a.Dexterity;
        r.AttackDexterityDice = d.DexterityDice.Of(a.Dexterity);
        r.AttackPotential = r.AttackDexterityDice + Math.Max(0, r.AttackAbility);
        r.AttackPool = Dice.Pool(r.AttackPotential, random, d.PoolStream, r.Draws);
        r.AttackSuccesses = Dice.Successes(r.AttackPool, Difficulty, random, d.AttackStream, r.Draws);

        r.DefenceDexterity = t.Dexterity;
        r.DefenceDodge = t.Dodge;                       // traced; counted only while a dodge is performed
        r.DefenceMelee = t.Melee;
        r.DefenceArmed = t.Armed;
        r.DefenceGuarding = t.Guarding;
        // Only a performed defence counts: the guard's Ability while the block is raised (Melee armed, Brawl bare-handed), or
        // Dodge while a dodge is under way (its burst only).
        r.DefenceAbilityId = t.Guarding ? t.Armed ? d.ArmedAbility : d.BareAbility : t.Dodging ? d.DodgeAbility : "";
        r.DefenceAbility = t.Guarding ? t.Armed ? t.Melee : t.Brawl : t.Dodging ? t.Dodge : 0;
        r.DefenceDexterityDice = DexterityOnlyPerformed && !t.Guarding && !t.Dodging ? 0 : d.DexterityDice.Of(t.Dexterity);
        r.DefencePotential = r.DefenceDexterityDice + Math.Max(0, r.DefenceAbility);
        r.DefencePool = Dice.Pool(r.DefencePotential, random, d.PoolStream, r.Draws);
        r.DefenceSuccesses = Dice.Successes(r.DefencePool, Difficulty, random, d.DefenceStream, r.Draws);

        r.ContestNet = r.AttackSuccesses - r.DefenceSuccesses;
        r.PoolBeforeContest = r.DamagePool;
        r.ContestChange = Change(r.ContestNet, r.DamagePool, d.AddMost, d.TakeMostShare);
        r.ContestLimited = r.ContestChange != r.ContestNet;
        r.DamagePool = Math.Max(0, r.DamagePool + r.ContestChange);
    }

    /// <summary>
    /// Whether <paramref name="who"/> is performing a dodge: the platform's <see cref="Platform.Bodies.Dodging"/> in its
    /// <b>burst</b>, the body moving (the rules package's correction: <i>"If Dodge enters any optional
    /// technique contest, its defense applies only when a genuine eligible evasive action is underway with a valid defensive
    /// opportunity, not merely in windup after the body is hit; physical displacement remains primary"</i>). Its start (the
    /// weight shifted before the push: the windup) and its recovery (the feet got under the body again) are no evasion performed.
    /// An earlier build counted the start too.
    /// </summary>
    public static bool Dodging(Platform.Core.World world, Platform.Core.EntityId who)
    {
        if (!world.TryStore<Platform.Bodies.Dodging>(out var dodges)) return false;
        ref var d = ref dodges.Find(who);
        return !System.Runtime.CompilerServices.Unsafe.IsNullRef(ref d) && d.Phase == Platform.Bodies.DodgePhase.Burst;
    }

    /// <summary>
    /// The change a net makes to a whole damage pool: a positive net in full (at most <paramref name="addMost"/>), a
    /// negative one in full but at most <c>floor(takeMostShare × pool)</c> dice; never more than the pool itself.
    /// </summary>
    public static int Change(int net, int pool, int? addMost, double takeMostShare)
    {
        if (net > 0) return addMost is { } m ? Math.Min(net, m) : net;
        if (net < 0)
        {
            int most = (int)Math.Floor(takeMostShare * Math.Max(0, pool));
            return -Math.Min(Math.Min(-net, most), Math.Max(0, pool));
        }
        return 0;
    }
}
