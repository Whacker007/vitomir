// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What is currently happening to a character: the effect instances hanging off it, and the
// questions everything else asks of them.
//
// The engine keeps a linked list on the thing, appended to at the tail, and a
// handful of small functions read it: find by type and subtype, by GUID, by
// name, and the fold that turns every matching instance into one number.
// What *changes* the list — applying, removing, the per-frame tick — is `EffectRuntime`.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// One live effect on a character — the engine's 0x34-byte instance,
/// field for field:
///
/// | # | field |
/// |---|---|
/// | 1 | the `effects.ned` row, <see cref="Definition"/> |
/// | 2 | its own script object (<see cref="Script"/>), when the row names a class |
/// | 3 | <see cref="Until"/>, 0 for one that does not end on its own |
/// | 4 | <see cref="NextTick"/> |
/// | 5 | <see cref="Fade"/>, the icon's blink |
/// | 6 | <see cref="Guid"/> |
/// | 7 | <see cref="CreatorGuid"/> |
/// | 8 | <see cref="Flags"/> — the caller's `EI_FLAG_*` and the engine's own bits |
/// | 9 | <see cref="Level"/>, −1 for one applied by value |
/// | 10 | <see cref="Value"/> |
/// | 11 / 12 | <see cref="IntParam"/> / <see cref="FloatParam"/>, the script's own |
/// </summary>
public sealed class Effect
{
    /// <summary>The row of `effects.ned` this came from.</summary>
    public required NedEffect Definition { get; init; }

    /// <summary>
    /// Which of the definition's six levels was applied (0-based), or **−1** for one applied by
    /// value (`AddActorEffectByValue`), which is what `GetActorEffectLevel` answers.
    /// </summary>
    public int Level { get; init; } = 0;

    /// <summary>
    /// What this instance is worth. The engine keeps it on the instance rather than reading the
    /// definition each time, which is what lets a caller apply an effect at a value the
    /// file does not list.
    /// </summary>
    public float Value { get; init; }

    /// <summary>
    /// The engine's millisecond clock when this ends, or **0** for one that does not. An
    /// `ExpandActorEffect` that appends adds to it whatever it holds — so appending to a permanent
    /// effect gives it an end a few seconds into the game, which is long past. That is the engine's.
    /// </summary>
    public int Until { get; set; }

    /// <summary>
    /// Who applied it, by GUID — a caster, a weapon, the item worn. The engine resolves it
    /// each time it needs the thing, so a creator that has gone is simply nobody.
    /// </summary>
    public int CreatorGuid { get; init; }

    /// <summary>The creator as it was when the effect went on, for the in-process callers that have one.</summary>
    public WorldThing? Creator { get; init; }

    /// <summary>
    /// The item this came from, when one did. Worn gear hangs its effects on its wearer for as long
    /// as it is worn, so taking it off has to end exactly those and nothing else. Null for
    /// everything else, which is most of them.
    /// </summary>
    public Item? Source { get; internal set; }

    /// <summary>The instance's GUID: a global counter from 1, saved with the game.</summary>
    public int Guid { get; init; }

    /// <summary>
    /// The instance flags: the caller's <c>EI_FLAG_*</c> in the low bits
    /// (<see cref="Worn"/>, <see cref="Used"/>, <see cref="FromDiscipline"/>) and the engine's own
    /// three high bits — <see cref="FoldedBit"/>, <see cref="CountedBit"/>, <see cref="SpentBit"/>.
    /// </summary>
    public uint Flags { get; set; }

    /// <summary>`EI_FLAG_WORN`: from an item being worn; the creator is the item. Survives a death.</summary>
    public const uint Worn = 0x1;
    /// <summary>`EI_FLAG_USE`: from an item being used.</summary>
    public const uint Used = 0x2;
    /// <summary>`EI_FLAG_DISCIPLINE`: from a discipline; the creator is the caster.</summary>
    public const uint FromDiscipline = 0x4;
    /// <summary>
    /// **0x800000**: an instant kind that has been folded into the base row. It is how
    /// "apply once" works: the recompute runs while the instance still counts, then this is set so
    /// no later fold counts it again, and the next tick removes it.
    /// </summary>
    public const uint FoldedBit = 0x800000;
    /// <summary>**0x1000000**: set by the fold on every instance it looked at. Nothing decoded reads it.</summary>
    public const uint CountedBit = 0x1000000;
    /// <summary>**0x2000000**: removed. Still in the list until the next tick reaps it, and skipped by everything.</summary>
    public const uint SpentBit = 0x2000000;

    /// <summary>The script's own integer and float (`AddActorEffectByLevel`'s last two arguments).</summary>
    public int IntParam { get; init; }
    /// <inheritdoc cref="IntParam"/>
    public float FloatParam { get; init; }

    /// <summary>
    /// The effect's own script object — a Codex class named by the definition (<c>EffectDiscCelerity</c>
    /// …), made fresh for this instance with its owner set. It hears `effectstarted` and
    /// `effectended`, and is freed when the instance is reaped. Null when the row names none.
    /// </summary>
    public object? Script { get; set; }

    /// <summary>
    /// The icon's blink: 200 normally, and a ramp from the clock for the last five seconds
    /// of an effect that ends. Only an effect-icon display reads it.
    /// </summary>
    public float Fade { get; set; } = FullFade;
    /// <summary>What <see cref="Fade"/> holds when the icon is not blinking (0x43480000).</summary>
    public const float FullFade = 200f;

    /// <summary>When this next ticks, for an over-time effect. Zero for one that never does.</summary>
    public int NextTick { get; set; }

    /// <summary>Removed; see <see cref="SpentBit"/>.</summary>
    public bool Spent
    {
        get => (Flags & SpentBit) != 0;
        set => Flags = value ? Flags | SpentBit : Flags & ~SpentBit;
    }

    /// <summary>Folded into the base; see <see cref="FoldedBit"/>.</summary>
    public bool Folded
    {
        get => (Flags & FoldedBit) != 0;
        set => Flags = value ? Flags | FoldedBit : Flags & ~FoldedBit;
    }

    /// <summary>Whether the fold should count it: not spent, and not already in the base.</summary>
    public bool Counts => !Spent && !Folded;

    public EffectKind Kind => Definition.Kind;
    public int Subtype => Definition.Subtype;
    public string Name => Definition.Name;
}

/// <summary>
/// The effects on one character. A list, appended to at the tail and walked from the head, exactly
/// as the engine's own is.
/// </summary>
public sealed class Effects
{
    readonly List<Effect> live = [];

    /// <summary>Every instance, spent ones included, oldest first.</summary>
    public IReadOnlyList<Effect> All => live;

    /// <summary>
    /// The neutral answer when nothing of a type is present. It is **1.0
    /// for the types that multiply** — <c>MOVESPEED</c>, <c>ATTACKSPEED</c>, <c>MODELALPHA</c>,
    /// <c>MODELSCALE</c>, <c>BLOODCOST</c> and the five <c>USERMULT</c> slots — and 0.0 for
    /// everything else, which is the additive/multiplicative split stated by the code rather than
    /// inferred from the names.
    /// </summary>
    public static float Neutral(EffectKind kind) => kind switch
    {
        EffectKind.MoveSpeed or EffectKind.AttackSpeed or EffectKind.ModelAlpha
            or EffectKind.ModelScale or EffectKind.BloodCost
            or EffectKind.UserMult1 or EffectKind.UserMult2 or EffectKind.UserMult3
            or EffectKind.UserMult4 or EffectKind.UserMult5 => 1f,
        _ => 0f,
    };

    /// <summary>
    /// The types the value fold **skips entirely**.
    /// They are the over-time ones, which act by ticking rather than by being added up.
    /// </summary>
    public static bool OverTime(EffectKind kind) =>
        kind is EffectKind.BaseStatOverTime or EffectKind.BaseSoakOverTime
             or EffectKind.DamageOverTime or EffectKind.HealOverTime;

    /// <summary>
    /// The first instance of a type and subtype that has not been removed, or null.
    /// **It does not skip a folded instance** — only the fold does — so an instant effect is still
    /// found for the one frame it lives.
    /// </summary>
    public Effect? Find(EffectKind kind, int subtype = 0)
    {
        foreach (var e in live)
            if (e.Kind == kind && e.Subtype == subtype && !e.Spent) return e;
        return null;
    }

    /// <summary>The instance with this GUID, not removed, or null.</summary>
    public Effect? Find(int guid)
    {
        foreach (var e in live)
            if (e.Guid == guid && !e.Spent) return e;
        return null;
    }

    /// <summary>
    /// The first instance of the effect with this name, not removed, or null. The
    /// compare is case-insensitive (`_strcmpi` against the definition's name).
    /// </summary>
    public Effect? Find(string name)
    {
        foreach (var e in live)
            if (!e.Spent && e.Definition.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return e;
        return null;
    }

    public bool Has(EffectKind kind, int subtype = 0) => Find(kind, subtype) is not null;

    /// <summary>
    /// What a character's effects of one type and subtype come to.
    ///
    /// The fold starts from <see cref="Neutral"/> and takes its mode from the **first matching
    /// instance's** compose column — every instance after that is folded the first one's way,
    /// whatever its own column says, which is what the file's header means by "only the first
    /// effect's compose is consulted".
    ///
    /// Two of the modes stop the walk rather than continuing it, and the first of them is worth
    /// knowing about: **compose `none` returns zero**, not the instance's value. 29 of the 187
    /// shipped effects are composed that way, and they are the ones meant to be read with
    /// <see cref="Find(EffectKind, int)"/> instead — asking this for their value gets nothing on purpose.
    /// </summary>
    public float Value(EffectKind kind, int subtype = 0)
    {
        float value = Neutral(kind);
        if (OverTime(kind)) return value;

        var mode = (EffectCompose)(int)kind;      // replaced by the first match; see above
        bool first = true;
        int counted = 0;
        foreach (var e in live)
        {
            if (e.Kind != kind || e.Subtype != subtype || e.Spent) continue;
            e.Flags |= Effect.CountedBit;         // marked before the folded test
            if (e.Folded) continue;
            if (first) { mode = e.Definition.Compose; first = false; }
            switch (mode)
            {
                case EffectCompose.None: return 0f;
                case EffectCompose.First: return e.Value;
                case EffectCompose.Last: value = e.Value; break;
                case EffectCompose.Min: if (e.Value < value) value = e.Value; break;
                case EffectCompose.Max: if (value < e.Value) value = e.Value; break;
                case EffectCompose.Add: value += e.Value; break;
                case EffectCompose.Multiply: value *= e.Value; break;
                // The engine's own average is not one: `n` is raised first and the running total is
                // scaled by it, so three instances come to a + b/2 + c/3 rather than their mean.
                // Nothing shipped composes this way, so it costs the original nothing — but it is
                // reproduced rather than corrected.
                case EffectCompose.Average:
                    counted++;
                    value = (counted * value + e.Value) / counted;
                    break;
            }
        }
        return value;
    }

    /// <summary>
    /// Puts an instance on the end of the list, which is where the engine puts every new one.
    /// Nothing else happens — no recompute, no script; that is
    /// <see cref="EffectRuntime.Apply"/>, which is what everything outside the runtime calls.
    /// </summary>
    internal void Append(Effect effect) => live.Add(effect);

    /// <summary>
    /// Takes every instance another list holds, in its order, leaving that one empty — a character
    /// carried into the next place keeps what it has on (<see cref="World.Campaign"/>).
    /// </summary>
    internal void TakeAll(Effects from)
    {
        live.AddRange(from.live);
        from.live.Clear();
    }

    /// <summary>
    /// Takes every removed instance out of the list, freeing its script (the first loop of the
    /// tick). The engine does this at the start of the next tick, not when the instance
    /// is removed, so a removed instance is still in the list — and skipped — for the rest of the
    /// frame it went in.
    /// </summary>
    internal void Reap(Action<object>? free)
    {
        for (int i = 0; i < live.Count; i++)
        {
            if (!live[i].Spent) continue;
            if (live[i].Script is { } script) free?.Invoke(script);
            live[i].Script = null;
            live.RemoveAt(i--);
        }
    }

    /// <summary>
    /// Marks one instance removed without any of the removal's consequences. For a test or a
    /// caller that recomputes itself; the engine's removal is <see cref="EffectRuntime.Remove"/>.
    /// </summary>
    public void Remove(Effect effect) => effect.Spent = true;

    /// <summary>Forgets everything at once, spent or not (the reload path, the clear's second loop).</summary>
    internal void Clear() => live.Clear();
}
