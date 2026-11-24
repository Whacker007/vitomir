// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Everyone in the world who is somebody: the state a character carries and the scripts read.
//
// An actor is a thing (THING_TYPE_ACTOR or _PLAYER) with a second set of template tags on it —
// category 10 of the engine's tag table. Every field below is one of those tags; the names, the
// enumerations and the flag bits are the game's own, from Codex/System/Codex.java, which the
// scripts compile against.
//
// The engine's own actor record has a great deal more in it (AI, inventory, disciplines, effects,
// the combat state machine). This is what the shipped scripts actually read and write, plus what
// the templates set that costs nothing to carry.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>What kind of thing a character is (tag 138, ACTOR_TYPE_*).</summary>
public enum ActorType { Vampire = 0, Ghoul = 1, Human = 2, Monster = 3 }

/// <summary>
/// A character's stats, in the order <c>GetActorStat</c> indexes them (ACTOR_STAT_*). The
/// template sets each one with its own tag; the index is what a script asks by.
/// </summary>
public enum ActorStat
{
    Strength = 0, Dexterity = 1, Stamina = 2, Perception = 3, Intelligence = 4, Wits = 5,
    Blood = 6, Humanity = 7, Frenzy = 8, Generation = 9, Faith = 10, Charisma = 11,
    Manipulation = 12, Appearance = 13, FrenzyRating = 14, BloodRating = 15, BloodPool = 16,
    Mana = 17, ManaPool = 18,
}

/// <summary>
/// The kinds of damage, which are also the soak indices: <c>GetActorSoak(DAMAGE_TYPE_FIRE)</c> is
/// how much fire damage the character shrugs off.
/// </summary>
public enum DamageType
{
    Normal = 0, Lethal = 1, Aggravated = 2, Electric = 3, Fire = 4, Sun = 5, Faith = 6,
    Cold = 7, Poison = 8, Disease = 9,
}

/// <summary>
/// A character's state. Built from its template's category-10 tags when the location loads; from
/// then on it is what the CodexActor natives read and write.
/// </summary>
public sealed class Actor
{
    public const int Stats = 19, Soaks = 10;

    /// <summary>
    /// The name a script knows the character by (tag 81). <c>GuidFromCastID("Sorvena")</c> finds
    /// the thing in the current location carrying this, which is how a scene script gets hold of a
    /// named character without knowing where the level builder put it. 136 templates have one.
    /// </summary>
    public string CastId { get; init; } = "";

    /// <summary>
    /// The player-character file this template's numbers really come from (tag 59,
    /// `ACTOR_RPGSTATS`) — `misc/&lt;name&gt;.npc`. The template is the shell; the `.NPC` is the
    /// character, and applying it is what makes `--player` build the Christof the game starts you
    /// with rather than a 20-in-everything vampire. Empty on anything that is not a player.
    /// </summary>
    public string RpgStats { get; init; } = "";
    /// <summary>What it is called on screen, when a script has set one (<c>SetPlayerName</c>).</summary>
    public string Name { get; set; } = "";

    /// <summary>THING_AF_*, from tag 114 and then whatever scripts set (see <see cref="Flag"/>).</summary>
    public uint Flags { get; set; }
    /// <summary>
    /// The second flag word, THING_AF2_* in `Codex.java`. No template sets it; the
    /// engine and the scripts write it. See <see cref="Flag2"/>.
    /// </summary>
    public uint Flags2 { get; set; }

    public ActorType Type { get; set; }
    /// <summary>TEAM_*: who the character sides with. 8 is TEAM_NONE, which is the default.</summary>
    public int Team { get; set; } = Teams.None;

    /// <summary>
    /// How well this character notices others, and how hard it is to notice.
    /// The engine's actor constructor writes **zero into both**, and the
    /// only things that move them are the <c>SENSE</c> and <c>HIDE</c> effects, which write them
    /// directly. No template tag sets either. So every character starts able
    /// to see every other, and stealth is something a discipline or an item does to you.
    /// </summary>
    public int Perception { get; set; } = Rules.GameRules.Current.Sight.PerceptionStealthDefaults[0];

    /// <inheritdoc cref="Perception"/>
    public int Stealth { get; set; } = Rules.GameRules.Current.Sight.PerceptionStealthDefaults[1];
    /// <summary>The team it started on, which <c>ActorUnfrenzy</c> and the AI put it back to.</summary>
    public int OriginalTeam { get; set; } = Teams.None;

    /// <summary>The clan's name as the template writes it (tag 26); empty for the unclanned.</summary>
    public string Clan { get; set; } = "";
    /// <summary>
    /// What mood it reads as under Aura Perception (tag 27, ACTOR_AURA_*). The natives take and
    /// return an aura by name as well as by number, so both are kept.
    /// </summary>
    public uint Aura { get; set; }
    /// <summary>A name a script set with <c>SetActorAura</c>, which overrides the numbered one.</summary>
    public string AuraName { get; set; } = "";

    /// <summary>
    /// Health and its maximum. The engine's actor constructor writes **100** into both before any
    /// template tag is applied, so the six templates that set no ACTOR_HEALTH and the
    /// seven that set no ACTOR_MAXHEALTH get 100, not nothing.
    /// </summary>
    public float Health { get; set; } = Rules.GameRules.Current.Stats.HealthDefault[0];
    /// <inheritdoc cref="Health"/>
    public float MaxHealth { get; set; } = Rules.GameRules.Current.Stats.HealthDefault[1];
    public const float DefaultHealth = 100f;

    /// <summary>
    /// **Lifetime** experience — the figure the rank ladder walks, so
    /// spending cannot demote anyone. The experience award adds to this and to
    /// <see cref="SpendableExperience"/> together and clamps each to 100,000,000.
    /// </summary>
    public long Experience { get; set; }

    /// <summary>
    /// The **unspent** pool, which is what a point costs out of. The character
    /// pane draws it in the box called `charEarnedExp`, whose name reads like the opposite of what
    /// it holds — see `Ui.CharacterPane`.
    /// </summary>
    public long SpendableExperience { get; set; }

    /// <summary>
    /// The character's own money. A party member's cash goes to the party's purse
    /// instead (<c>Session.AddCash</c>), so this is what anybody else is carrying.
    /// </summary>
    public int Cash { get; set; }

    /// <summary>The engine's clamp on both experience figures.</summary>
    public const long MaxExperience = 100_000_000;

    /// <summary>
    /// Which `gib_&lt;n&gt;_&lt;i&gt;` family this character comes apart into (tag 246), and which
    /// `skeleton_&lt;n&gt;` it leaves when it dusts (tag 118). Both default to 0 in the engine's
    /// constructor; 156 templates set a gib class and 687 a skeleton id.
    /// </summary>
    public int GibClass { get; set; }
    /// <inheritdoc cref="GibClass"/>
    public int SkeletonId { get; set; }

    /// <summary>
    /// A multiplier on how fast the character moves and how fast it attacks — effect types
    /// MOVESPEED (17) and ATTACKSPEED (18), which write these directly.
    /// Both start at 1. The move multiplier is read when an **order** is issued rather than every
    /// frame, so Celerity cast mid-walk does not take hold until the next order.
    /// </summary>
    public float MoveSpeedScale { get; set; } = 1f;
    /// <inheritdoc cref="MoveSpeedScale"/>
    public float AttackSpeedScale { get; set; } = 1f;
    /// <summary>
    /// How this one behaves while it is in the player's party (`rpgParty`). It is runtime state, not
    /// a template tag — the status bar sets it — and it scales both the follow leash and the two
    /// thresholds that decide when a member drinks something.
    /// </summary>
    public Ai.PartyStance Stance { get; set; } = Ai.PartyStance.Neutral;

    public float WalkSpeed { get; set; }
    public float RunSpeed { get; set; }
    /// <summary>What killing it is worth (tag 53).</summary>
    public int Xp { get; set; }

    /// <summary>The AI file the template names (tag 56); <see cref="Ai.AiClasses"/> loads it.</summary>
    public string AiFile { get; init; } = "";
    /// <summary>The weapon template it carries (tag 93), the head it wears (57) and that head's motion set (58).</summary>
    public string WeaponTemplate { get; init; } = "";
    /// <summary>
    /// What it is actually holding, read from
    /// <see cref="WeaponTemplate"/>. Null means it cannot attack at all — the first thing an
    /// attack order checks.
    /// </summary>
    public Rpg.Weapon? Weapon { get; set; }

    /// <summary>
    /// The weapon the character falls back to with nothing equipped — the engine's
    /// the actor's attack record, built from <see cref="WeaponTemplate"/> when the tag is read
    /// and kept for the life of the character. <c>SetWeapon(null)</c> restores it, which
    /// is what makes taking a sword off put the fists back rather than leaving the character unable
    /// to fight at all.
    /// </summary>
    public Rpg.Weapon? DefaultWeapon { get; set; }

    /// <summary>
    /// What is in the off hand — a shield, usually (set by
    /// the off-hand wear, which asserts the item is armour). It is here for the same reason
    /// <see cref="Weapon"/> is: something has to say which model hangs on that arm.
    /// </summary>
    public Rpg.Item? Offhand { get; set; }

    /// <summary>
    /// The utility item held in the off hand — the torch, the flashlight, the Gangrel eye — the
    /// engine's `pUtility`, set by the utility's wear (<see cref="Rpg.Utility"/>), which
    /// also puts the item's thing in the world riding the hand bone. Its `0x800` flag is the
    /// utility stance.
    /// </summary>
    public Rpg.Item? Utility { get; set; }

    /// <summary>
    /// What the hand held before a weapon was swapped into it for the moment — the claws — the
    /// engine's own field, and the off hand's the same way.
    /// Both are given back when the swap ends (<see cref="Rpg.WeaponOverride"/>).
    /// </summary>
    public Rpg.Weapon? PrevWeapon { get; set; }
    /// <inheritdoc cref="PrevWeapon"/>
    public Rpg.Item? PrevOffhand { get; set; }

    /// <summary>
    /// The model standing in for its body in sub-model slot 0 — its armour (`christof_plate`), a
    /// script's (`SetActorNoItemArmor`) or its headless body (`Skeleton_nohead`) — or "" for its own
    /// (<see cref="BodyModels"/>).
    /// </summary>
    public string BodyModel { get; set; } = "";

    /// <summary>
    /// The model standing in for a player character's **portrait head** — the head's own sub-model
    /// slot 0, loaded by the same call as <see cref="BodyModel"/> with an `H` on the end
    /// (`christof_plateH`) — or "" for the head's own (<see cref="BodyModels"/>).
    /// </summary>
    public string PortraitModel { get; set; } = "";

    /// <summary>
    /// The open hands a player character talks with — the model in sub-model slot 3 while
    /// <see cref="Flag2.UsingHands"/> is on (<see cref="Rpg.TalkingHands"/>) — or "".
    /// </summary>
    public string TalkingHands { get; set; } = "";

    /// <summary>
    /// The armour it is wearing (set by `SetArmor`). What
    /// it changes about the character's model is not modelled yet; what it changes about the
    /// <b>sound</b> of a blow that did no harm is.
    /// </summary>
    public Rpg.Item? Armor { get; set; }

    /// <summary>
    /// When it last flinched (written by the flinch — <see cref="Rpg.Flinch"/>), and
    /// so how long its hurt noises, and any further flinch, stay away: 3.5 s.
    /// </summary>
    public int FlinchedAt { get; set; }

    /// <summary>
    /// What it is carrying. Everybody has one; it is empty until
    /// something is put in it, and nothing in a shipped scene fills it — the chronicle scripts do,
    /// the same way they assemble the party. Only the ammo path reads it so far.
    /// </summary>
    public Rpg.Inventory Inventory { get; } = new();

    /// <summary>
    /// What is **in his hand** — on the cursor, between one grid and the next — the engine's
    /// the engine's hand. The hold puts an item there (and writes him back into the item as its
    /// owner), the release takes it out, and the hold refuses outright when it is already
    /// full, so a hand holds one item or none.
    ///
    /// It lives on the actor rather than on the pane on purpose: that is where the original keeps
    /// it, and it is why **an item in hand survives a level change** — the UI is rebuilt and the
    /// actor is not, so the first cursor update on the new level finds it still held. The base game
    /// does exactly that.
    /// </summary>
    public Rpg.Item? Held { get; set; }

    /// <summary>
    /// What it has learnt and which one it has selected. The whole
    /// discipline-and-item ladder is gated on this list being non-empty, so a character with none
    /// never reaches for an item either — see <c>Minds.Reach</c>.
    /// </summary>
    public Rpg.DisciplineList Disciplines { get; } = new();

    /// <summary>
    /// The discipline waiting to fire: set by the cast order and the walk into range,
    /// read by the fire at the `Cast` keyframe, cleared by the cancel.
    /// </summary>
    public Rpg.Discipline? PendingDiscipline { get; set; }

    /// <summary>
    /// The shape change in force (built by the shift, reverted by its end), or null.
    /// While one is on, **no discipline can be cast at all** (the cast test's step 3). <see cref="Rpg.ShapeShift"/>.
    /// </summary>
    public Rpg.ShapeChange? ShapeChange { get; set; }

    /// <summary>
    /// What it is doing, who to, and when it is free again — three fields of the
    /// engine's thing. Every AI mode waits on the busy time, and so does every
    /// order. <see cref="ActionRetryAt"/> is the half-second an approach waits
    /// before asking the way again; <see cref="ActionSpeed"/> is the speed the
    /// action moves at, which takes its MOVESPEED multiplier when the order is issued.
    /// </summary>
    public Rpg.ActionMode Action { get; set; }

    /// <summary>
    /// What a human is still asking this character to do, read at the end of every swing. See
    /// <see cref="Rpg.Actions.Asking"/>: it is how the original's hold-to-attack works, and it is
    /// null for everybody the AI drives.
    /// </summary>
    public Rpg.Actions.Asking? Repeating { get; set; }

    /// <summary>
    /// What the pointer is doing — buttons, the thing under it, the ground under it — as the `Repeat`
    /// tag reads it for held automatic fire (<see cref="Rpg.HeldFire"/>). The input layer
    /// leaves it on the steered character each frame; null for everybody the AI drives.
    /// </summary>
    public Rpg.HeldFire.Pointer? Pointing { get; set; }
    /// <inheritdoc cref="Action"/>
    public WorldThing? Target { get; set; }

    /// <summary>
    /// Where the order is aimed, for the ones that carry a point rather than a thing — the
    /// engine's action point, which the action's start fills with the place the player
    /// clicked before it walks them over to drop something there.
    /// </summary>
    public System.Numerics.Vector3 ActionPoint { get; set; }

    /// <summary>
    /// The modifier keys the order was given with. The engine packs them once per click
    /// and hands the same mask to every order handler, which is how **alt** reaches a pick-up that
    /// only happens a second later, after the walk (`Rpg.GroundOrders.ToCursor`).
    /// </summary>
    public InputModifier OrderMods { get; set; }
    /// <inheritdoc cref="Action"/>
    public int BusyUntil { get; set; }
    /// <inheritdoc cref="Action"/>
    public int ActionRetryAt { get; set; }
    /// <summary>
    /// When the action began: the move order writes it and the stake,
    /// whose tick lets the stake out twenty seconds after it.
    /// </summary>
    public int ActionStartedAt { get; set; }
    /// <inheritdoc cref="Action"/>
    public float ActionSpeed { get; set; }
    public string HeadModel { get; set; } = "";

    /// <summary>
    /// The weapon sub-model's hide bit (`EnableActorWeapon`): the weapon is still held and
    /// still swings, it is only not drawn. Kept on the character, so it crosses a level change.
    /// </summary>
    public bool WeaponHidden { get; set; }

    /// <summary>
    /// The same bit on the **off hand's** sub-model: a shield or
    /// a torch is hidden and still held. Nothing but a conversation sets it — `EnableActorWeapon`
    /// reaches only the weapon.
    /// </summary>
    public bool OffhandHidden { get; set; }

    /// <summary>
    /// The suffix `SetActorNoItemArmor` gave for the body shown with no armour item on;
    /// "" is the model's own. Not drawn yet — body-armour models are on the deferred list.
    /// </summary>
    public string NoItemArmor { get; set; } = "";
    public string HeadMotionSet { get; set; } = "";

    /// <summary>
    /// What the portrait head is playing on its **action** channel, when gameplay has put something
    /// there: the bite at a kiss, the feeding loop, the frenzy snarl. Stopping the head's
    /// action channel is setting this back to none; the head then rests on its motion
    /// channel again (<see cref="Ui.PartyHeads.ModeFor"/>).
    /// </summary>
    public Ui.HeadMode HeadAction { get; set; }
    /// <summary>Where a shot leaves it (tag 94).</summary>
    public Vector3 FirePosition { get; init; }

    /// <summary>The current stats, indexed by <see cref="ActorStat"/>.</summary>
    public float[] Stat { get; } = new float[Stats];
    /// <summary>
    /// What the template gave it, which is what <c>GetActorBaseStat</c> answers: the current stat
    /// is the base plus whatever effects and disciplines have done to it since.
    /// </summary>
    public float[] BaseStat { get; } = new float[Stats];
    /// <summary>Damage soak, indexed by <see cref="DamageType"/>, current and as the template set it.</summary>
    public float[] Soak { get; } = new float[Soaks];
    public float[] BaseSoak { get; } = new float[Soaks];

    /// <summary>
    /// The per-stat bounds every base-stat write is clamped into. **The engine clamps and Vitomir
    /// did not**, which is how a frenzy reached −184 (`Simulation.StatDrift` caught it at
    /// −2 every two seconds, which is `ef_decreasefrenzy` doing exactly what it is authored to do).
    ///
    /// The stat set, read out in full:
    ///
    /// <list type="bullet">
    /// <item>a <b>`THING_TYPE_PLAYER`</b> is clamped into its own two rows — a minimum
    /// row and a maximum row, one entry per stat. Those rows are authored: a
    /// `.NPC` file is laid out `// STATS  min  max  current` and gives Christof
    /// <c>frenzy 0 100 0</c>;</item>
    /// <item><b>anyone else</b> is clamped into 0 .. the template's own limit
    /// (one entry per stat). A missing table, or an entry of <b>−1</b>, means no
    /// limit and the global ceiling of <b>10,000</b> applies instead;</item>
    /// <item><b>blood</b> (stat 6) is clamped a second time against the blood <i>pool</i> maximum,
    /// so a character cannot hold more blood than they have room for.</item>
    /// </list>
    ///
    /// Both rows default to the non-player fallback here, which is the right answer for every
    /// character in the game except the player — and the player's come from the `.NPC` reader when
    /// it exists. `-1` in <see cref="StatMax"/> means "no limit" exactly as the engine's sentinel
    /// does.
    /// </summary>
    public float[] StatMin { get; } = new float[Stats];
    /// <inheritdoc cref="StatMin"/>
    public float[] StatMax { get; } = [.. Enumerable.Repeat(Rules.GameRules.Current.Stats.StatCeiling, Stats)];

    /// <summary>The global ceiling a stat with no authored limit is held to (`_DAT_004eb75c`).</summary>
    public const float StatCeiling = 10_000f;
    /// <summary>The floor every non-player stat write is held to (`_DAT_004eb424`).</summary>
    public const float StatFloor = 0f;
    /// <summary>What an authored limit of this value means: no limit, use <see cref="StatCeiling"/>.</summary>
    public const float NoStatLimit = -1f;

    /// <summary>
    /// The clan's own record (`misc/game.ncd`), when the template's clan names one.
    /// Its caps bound a non-player's stats; its groups and weaknesses decide
    /// what the advancement screen charges.
    /// </summary>
    public Rpg.Clan? ClanRecord { get; set; }

    /// <summary>
    /// The bounds one stat write is held to, before blood and mana get their second clamp — the
    /// shared head of the base and the current stat sets. A **player** thing (`THING_TYPE_PLAYER`)
    /// is held to its own two rows, <see cref="StatMin"/> and <see cref="StatMax"/>; **anyone else**
    /// to 0 .. its clan's cap for the stat, or the global ceiling when there is no clan or the cap is
    /// −1. A non-player's rows are never read — the engine has no such rows on a plain actor.
    /// </summary>
    float Bound(int index, float value)
    {
        if (IsPlayerThing)
        {
            if (value < StatMin[index]) return StatMin[index];
            float hi = StatMax[index] == NoStatLimit ? Rules.GameRules.Current.Stats.StatCeiling : StatMax[index];
            return value > hi ? hi : value;
        }
        float floor = Rules.GameRules.Current.Stats.StatFloor;
        if (value < floor) return floor;
        float cap = ClanRecord is { } clan && clan.Caps[index] != NoStatLimit ? clan.Caps[index] : Rules.GameRules.Current.Stats.StatCeiling;
        return value > cap ? cap : value;
    }

    /// <summary>
    /// Blood and mana are clamped a second time, **from the value asked for** rather than from the
    /// first clamp: at least the player's own minimum (or 0) and at most the pool — the *current*
    /// pool, `Stat[BloodPool]` / `Stat[ManaPool]` (the same cases in the current set).
    /// </summary>
    float Pooled(int index, float asked)
    {
        int pool = index == (int)ActorStat.Blood ? (int)ActorStat.BloodPool : (int)ActorStat.ManaPool;
        float lo = IsPlayerThing ? StatMin[index] : Rules.GameRules.Current.Stats.StatFloor;
        if (asked < lo) return lo;
        return asked > Stat[pool] ? Stat[pool] : asked;
    }

    /// <summary>
    /// Write a base stat the way the engine writes one: into <see cref="Bound"/>, and
    /// blood and mana into their pools as well. **Everything that changes a base stat goes through
    /// here**; assigning <see cref="BaseStat"/> directly skips the clamp, which is the bug this exists
    /// to make impossible (a frenzy at −184).
    /// </summary>
    public float SetBaseStat(int index, float value)
    {
        if (index < 0 || index >= Stats) return 0f;
        float v = Bound(index, value);
        if (index is (int)ActorStat.Blood or (int)ActorStat.Mana) v = Pooled(index, value);
        BaseStat[index] = v;
        return v;
    }

    /// <summary>
    /// The clamp half of the current-stat write: the same bounds, and blood and mana held
    /// to their pools. What the write *does* besides — the frenzy check, the low-blood bark, a
    /// human dying of a drained vein — is `Rpg.Stats.Set`, which is what everything calls.
    /// </summary>
    public float ClampStat(int index, float value)
    {
        if (index < 0 || index >= Stats) return 0f;
        float v = Bound(index, value);
        if (index is (int)ActorStat.Blood or (int)ActorStat.Mana) v = Pooled(index, value);
        return v;
    }

    /// <summary>
    /// Take this character's numbers from its player-character file. The template has already been
    /// applied; this replaces what the `.NPC` names and leaves the rest.
    ///
    /// **The file's own comment says** *"bloodpool MUST come before blood!!!"*, as if a blood write were
    /// held to the pool the line before set. It is not: the stat set holds blood to the pool's **current**
    /// value, and the pool line writes only the base, so blood is held to the reset's 100 (see below).
    ///
    /// The disciplines and starting items are read and **not** applied: both need layers this does
    /// not own — the discipline definitions and the template set — so whoever calls this reports
    /// them rather than pretending. Returns how many stats were taken from the file.
    /// </summary>
    public int Apply(Formats.NpcFile npc)
    {
        if (npc.Name.Length > 0) Name = npc.Name;
        if (npc.Clan.Length > 0) Clan = npc.Clan;
        if (npc.MaxHealth > 0f) MaxHealth = npc.MaxHealth;
        if (npc.Health > 0f) Health = npc.Health;
        // Each experience line writes its own figure and nothing else (`totalexperience` the
        // lifetime figure, `experience` the unspent one): a sheet with only the
        // lifetime figure — every companion's — gives nothing to spend (a Prague save: Wilhem 325 of 6,325).
        if (npc.NamesTotalExperience) Experience = npc.TotalExperience;
        if (npc.NamesExperience) SpendableExperience = npc.Experience;

        // Bounds for every authored stat first, so the clamp is in place before any value lands —
        // what 1.1's own two setters do one stat at a time while it reads the file, each lifting or
        // lowering the current value to the new bound on the spot (for the minimum and the maximum alike).
        int taken = 0;
        for (int i = 0; i < Stats && i < npc.Stats.Length; i++)
        {
            if (!npc.Stats[i].Authored) continue;
            StatMin[i] = npc.Stats[i].Min;
            if (Stat[i] < StatMin[i]) Stat[i] = StatMin[i];
            StatMax[i] = npc.Stats[i].Max;
            if (Stat[i] > StatMax[i]) Stat[i] = StatMax[i];
            taken++;
        }
        // Then the values, into the **base** stats only (each case ends in the
        // base stat set). Blood and mana are held to their pools' **current** values,
        // which the sheet's own pool lines do not move — only the recompute after the whole file does.
        // So a sheet whose blood is over the reset's 100 gives 100: Serena's 120 of 120
        // comes in as 100 in 1.1, and so do Wilhem's,
        // Erik's, Lily's, Samuel's and Pink's. The file's "bloodpool MUST come before blood!!!"
        // does not help, whatever its author meant.
        foreach (int i in PoolsFirst)
        {
            if (i >= npc.Stats.Length || !npc.Stats[i].Authored) continue;
            SetBaseStat(i, npc.Stats[i].Current);
        }
        // The recompute sets each current from its base and effects; a body being given its
        // sheet has none, so the base is the current. (A companion's `CharacterSheets.Give` recomputes
        // again after its kit, which is 1.1's own order.)
        for (int i = 0; i < Stats && i < npc.Stats.Length; i++)
            if (npc.Stats[i].Authored) Stat[i] = BaseStat[i];
        return taken;
    }

    /// <summary>
    /// The order a `.NPC`'s values are applied in: both pools, then everything else. It exists for
    /// one line of one shipped comment, and that line is the engine telling on itself.
    /// </summary>
    static readonly int[] PoolsFirst =
        [(int)ActorStat.BloodPool, (int)ActorStat.ManaPool,
         .. Enumerable.Range(0, Stats).Where(i => i != (int)ActorStat.BloodPool
                                               && i != (int)ActorStat.ManaPool)];

    /// <summary>Add to a base stat through the clamp, which is what an over-time effect does.</summary>
    public float AddBaseStat(int index, float delta) =>
        index >= 0 && index < Stats ? SetBaseStat(index, BaseStat[index] + delta) : 0f;

    public bool Has(uint flag) => (Flags & flag) != 0;
    public bool Has2(uint flag) => (Flags2 & flag) != 0;

    /// <summary>
    /// Whether the character has any disciplines at all (the list head). The engine
    /// tests it before the whole discipline-and-item ladder, so a character with a pack full of
    /// vials and no disciplines never reaches for one — see `Minds.Reach`.
    /// </summary>
    public bool HasDisciplines => Disciplines.Count > 0;

    /// <summary>
    /// Whether this is a `THING_TYPE_PLAYER` rather than a plain actor — the test the AI makes
    /// every time a main flag comes in a pair, because "party" and "enemy" in those names mean
    /// exactly this. Set when the thing is built.
    /// </summary>
    public bool IsPlayerThing { get; set; }

    /// <summary>
    /// Where the discipline pane was left for this character (a byte for the tab, a byte for the
    /// tab strip's position) — kept on a `THING_TYPE_PLAYER` only, and put back each time the pane
    /// opens on them (`Ui.DisciplinePane`). The row scroll is not kept.
    /// </summary>
    public byte DisciplineTab { get; set; }
    /// <inheritdoc cref="DisciplineTab"/>
    public byte DisciplineTabOffset { get; set; }

    /// <summary>Is it dead? Set by <see cref="Rpg.Damage.Kill"/> and cleared by a revive.</summary>
    public bool Dead => Has(Flag.Dead);

    /// <summary>
    /// When the corpse starts fading out, on the millisecond clock;
    /// 0 means it is not going to. The actor tick clears it as the fade begins, and a
    /// `TARGET DEAD` discipline can only reach a body while it is still set — which is why the
    /// last five seconds of a corpse are useless.
    /// </summary>
    public int FadeAt { get; set; }

    /// <summary>
    /// When the corpse's fade-out actually began, on the millisecond clock; 0 means it has not.
    /// The engine has no such field — it starts a material fade and forgets — so this is Vitomir's
    /// record of the same thing, and what <see cref="Opacity"/> is measured from.
    /// </summary>
    public int FadingFrom { get; set; }

    /// <summary>
    /// How solid the body still looks: 1 until the fade starts, then down to 0 over
    /// <see cref="Rpg.Damage.Corpse.FadeDurationMs"/> — or over
    /// <see cref="Rpg.Damage.Corpse.DustFadeMs"/> for one that is dusting, which is five times
    /// faster. What is drawn is the model's own alpha (<see cref="WorldThing.ModelAlpha"/>), which the
    /// corpse step and the dust death ramp down over the same times; this is the same curve, kept
    /// for anything that asks about the body rather than the picture.
    /// </summary>
    public float Opacity(int nowMs)
    {
        if (FadingFrom == 0) return 1f;
        float over = Dusted ? Rules.GameRules.Current.Death.CorpseFadeDurationMs[1] : Rules.GameRules.Current.Death.CorpseFadeDurationMs[0];
        float gone = (nowMs - FadingFrom) / over;
        return gone <= 0f ? 1f : gone >= 1f ? 0f : 1f - gone;
    }

    /// <summary>
    /// Whether this one went out as a skeleton and a puff of smoke rather than leaving a body.
    /// The engine has no flag for it — it simply cancels the fade and takes the alpha down in a
    /// second — so this is Vitomir's record of the same thing.
    /// </summary>
    public bool Dusted { get; set; }

    /// <summary>
    /// ACTOR_CLAN_* in the game's own order, lower case. A template writes the clan as a name
    /// (tag 26) and <c>GetActorClanId</c> answers with the number, so this is the map between them.
    /// Thirteen of the sixteen appear in the shipped templates.
    /// </summary>
    public static readonly string[] Clans =
    [
        "none", "brujah", "cappadocian", "gangrel", "nosferatu", "toreador", "assamite", "giovanni",
        "lasombra", "malkavian", "ravnos", "setite", "tremere", "tzimisce", "ventrue", "caitiff",
        "userdefined",
    ];

    /// <summary>ACTOR_AURA_*: what a character reads as under Aura Perception, lower case.</summary>
    public static readonly string[] Auras =
    [
        "none", "afraid", "aggressive", "angry", "bitter", "calm", "compassionate", "confused",
        "conservative", "daydreaming", "depressed", "desirous", "diablerist", "distrustful",
        "envious", "excited", "frenzied", "generous", "happy", "hateful", "idealistic", "innocent",
        "lovestruck", "lustful", "obsessed", "psychotic", "sad", "spiritual", "suspicious",
        "faerie", "ghost", "magicuse", "vampire", "werebeast", "userdefined",
    ];

    /// <summary>
    /// Its clan's number (<see cref="Rpg.Clans.IdOf(string, bool)"/>): one of the fifteen
    /// by name, else 16 for a clan the clan file has (its record is here — `human` in the shipped
    /// file), else 0 (ACTOR_CLAN_NONE).
    /// </summary>
    public int ClanId => Rpg.Clans.IdOf(Clan, ClanRecord is not null);

    /// <summary>What <c>GetActorAura</c> answers: the name a script set, else the numbered one.</summary>
    public string AuraOrName =>
        AuraName.Length > 0 ? AuraName : Aura < Auras.Length ? Auras[Aura] : Auras[0];

    static int Index(string[] table, string name)
    {
        for (int i = 0; i < table.Length; i++)
            if (string.Equals(table[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    /// <summary>Sets the clan by name, as <c>SetActorClan</c> does; false when there is no such clan.</summary>
    public bool SetClan(string name)
    {
        if (Index(Clans, name) == 0 && !string.Equals(name, Clans[0], StringComparison.OrdinalIgnoreCase))
            return false;
        Clan = name;
        return true;
    }

    /// <summary>
    /// TEAM_* (Codex.java). Player teams are 0-7, enemies 12-19. A template that sets no
    /// ACTOR_TEAM leaves the character on <see cref="Teams.None"/>, which is what the engine's own
    /// actor constructor writes into both the team and the original team.
    /// </summary>
    public static class Teams
    {
        public const int PlayerFirst = 0, PlayerLast = 7, None = 8, NotApplicable = 9,
                         Pedestrians = 10, Lawmen = 11, EnemyFirst = 12, EnemyLast = 19;
        public static bool IsPlayer(int team) => team is >= PlayerFirst and <= PlayerLast;
        public static bool IsEnemy(int team) => team is >= EnemyFirst and <= EnemyLast;
    }

    /// <summary>
    /// THING_AF2_* (Codex.java): the second flag word. Bit 1, `Limping`, is set and cleared by the
    /// MOVESPEED effect — below 0.8 the character limps — which is why the animation
    /// layer finds it on the body's `limp` mode and the head's `head_hurt`.
    /// </summary>
    public static class Flag2
    {
        public const uint IsBeheaded = 0x1, Limping = 0x2, WeaponOverride = 0x4, Conversation = 0x8,
                          UsingHands = 0x10, WeaponNoHands = 0x20, Speaking = 0x40, Restrained = 0x80,
                          PermaFrenzy = 0x100, ScrollCast = 0x200;
        /// <summary>
        /// Keeping formation. One of the bits above the ten `Codex.java` names, and the engine is
        /// clear about it: `InParty` is this bit and the member's own party slot pointing
        /// back at it, `SetInParty` is what flips it, and the status bar's `statusParty`
        /// pip is what shows it. It is **not** party membership — that is
        /// <see cref="Flag.Party"/> in the first word — so a companion can be told to stop following
        /// without leaving the party.
        /// </summary>
        public const uint InFormation = 0x1000;
        /// <summary>What MOVESPEED has to fall below for the character to limp.</summary>
        public const float LimpBelow = 0.8f;
    }

    /// <summary>THING_AF_* — the actor flags the scripts and the engine share (Codex.java).</summary>
    public static class Flag
    {
        public const uint Invulnerable = 0x1, InvulnerableToPhysical = 0x2, AiPaused = 0x4,
                          NoFeed = 0x8, NoBleed = 0x10, AlwaysGibKill = 0x20, CanGibKill = 0x40,
                          TalkTo = 0x80, Pickup = 0x100, Staked = 0x200, HurtBySilver = 0x400,
                          FastDrinker = 0x800, Summoned = 0x1000, Animal = 0x2000,
                          HumanityLoss = 0x4000, HumanityTrack = 0x8000, CanBehead = 0x10000,
                          Ethereal = 0x20000, Neutral = 0x40000, AiNoReturnHome = 0x80000,
                          StartHidden = 0x100000, NoCorpseRot = 0x200000, NoDiscipline = 0x400000,
                          NoShapeDiscipline = 0x800000, ValidShape = 0x1000000, Lupine = 0x2000000,
                          NoSkeletonDeath = 0x4000000, NoGibSpray = 0x8000000,
                          Dead = 0x10000000, Party = 0x20000000;
    }

    /// <summary>
    /// The character a template describes, or null when it describes nothing of the sort. The
    /// stats and soaks start at what the template set, and the base copies keep that.
    /// </summary>
    public static Actor From(IReadOnlyDictionary<uint, NodTag> tags)
    {
        var actor = new Actor
        {
            CastId = Str(tags, Tags.CastId),
            RpgStats = Str(tags, Tags.ActorRpgStats),
            Flags = UInt(tags, Tags.ActorFlags),
            Type = tags.ContainsKey(Tags.ActorType) ? (ActorType)UInt(tags, Tags.ActorType) : (ActorType)Rules.GameRules.Current.Stats.ActorTypeDefault,
            Clan = Str(tags, Tags.ActorClan),
            Aura = UInt(tags, Tags.ActorAura),
            AiFile = Str(tags, Tags.ActorAi),
            WeaponTemplate = Str(tags, Tags.ActorWeapon),
            HeadModel = Str(tags, Tags.ActorHeadModel),
            HeadMotionSet = Str(tags, Tags.ActorHeadMotion),
            FirePosition = tags.TryGetValue(Tags.ActorFirePos, out var fp) ? fp.AsVector3() : default,
        };
        // ACTOR_TEAM is an enum tag, and a template that does not set one is on nobody's side.
        actor.Team = actor.OriginalTeam = tags.ContainsKey(Tags.ActorTeam) ? (int)UInt(tags, Tags.ActorTeam) : Rules.GameRules.Current.Stats.TeamDefault;
        // Health and XP are whole numbers on disk (UINT); the speeds, the stats and the soaks are
        // floats. Checked against all 2,619 shipped templates rather than taken from the SDK table.
        if (tags.ContainsKey(Tags.ActorHealth)) actor.Health = UInt(tags, Tags.ActorHealth);
        if (tags.ContainsKey(Tags.ActorMaxHealth)) actor.MaxHealth = UInt(tags, Tags.ActorMaxHealth);
        actor.GibClass = (int)UInt(tags, Tags.GibClass);
        actor.SkeletonId = (int)UInt(tags, Tags.SkeletonId);
        actor.WalkSpeed = Float(tags, Tags.ActorWalkSpeed);
        actor.RunSpeed = Float(tags, Tags.ActorRunSpeed);
        actor.Xp = (int)UInt(tags, Tags.ActorXp);
        // A stat the template does not set keeps the constructor's 20 (as in 1.1).
        float unset = Rules.GameRules.Current.Stats.StatUnsetDefault;
        for (int i = 0; i < Stats; i++)
            actor.Stat[i] = actor.BaseStat[i] = tags.ContainsKey(Tags.Stat[i]) ? Float(tags, Tags.Stat[i]) : unset;
        for (int i = 0; i < Soaks; i++) actor.Soak[i] = actor.BaseSoak[i] = Float(tags, Tags.Soak[i]);
        return actor;
    }

    /// <summary>
    /// What the `gameActor` constructor writes into all nineteen stats, base and current,
    /// before a template's tags are read: **20**. A template that does not set a stat leaves it there —
    /// humanity, frenzy, generation, frenzy rating, mana and mana pool for most of the cast, since the
    /// root `_actor` sets none of them. 1.1 has it on every character (a Monastery monk at
    /// 20 in all six; a rat at 20 but for the humanity its template sets to 0); Vitomir once had 0.
    /// </summary>
    public const float UnsetStat = 20f;

    static string Str(IReadOnlyDictionary<uint, NodTag> tags, uint id) =>
        tags.TryGetValue(id, out var t) ? t.AsString() : "";
    static uint UInt(IReadOnlyDictionary<uint, NodTag> tags, uint id) =>
        tags.TryGetValue(id, out var t) ? t.AsUInt() : 0;

    static float Float(IReadOnlyDictionary<uint, NodTag> tags, uint id) =>
        tags.TryGetValue(id, out var t) ? t.AsFloat() : 0f;
}
