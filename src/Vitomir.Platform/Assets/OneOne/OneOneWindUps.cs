// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json;
using Vitomir.Formats;
using Vitomir.Platform.Animation;

namespace Vitomir.Platform.Assets.OneOne;

/// <summary>
/// The four directions' wind-ups and ready poses (*"Let's build the wind ups for all 4 directions"*,
/// with Mount &amp; Blade's *"hold attack, and then switch movements to change the attack direction"*; ours, SANDBOX).
/// For each one-handed bearer of 1.1's skeleton 0, as
/// the flattened side blows and the stitched overhead and thrust are made: each blow played as press → its wind-up into
/// its ready pose (held while attack is held) → the strike from it (the blow's own clip from <see cref="WindUpShape.StrikeFrom"/>, its
/// blade's path through the active window the blow's) → the recovery (<see cref="WindUpClip"/>):
/// <list type="bullet">
/// <item><b>left</b> (the flattened backhand): the blade cocked over the left shoulder, ready to cut from the left;</item>
/// <item><b>right</b> (the flattened forehand): cocked at the right shoulder, not in 1.1's loop above the head;</item>
/// <item><b>overhead</b> (the stitched overhead): raised above the head;</item>
/// <item><b>thrust</b> (the stitched thrust): drawn back at the hip, the point forward.</item>
/// </list>
/// The times and the poses are data (<c>Data/combat/windups.json</c> of the enhanced module, read by the session; <see cref="Defaults"/>
/// when no module gives them, the same numbers), so a later pace can change them without the code. Registered in place of the an earlier build
/// and an earlier build blows unless the switch <c>swings-oneone</c> keeps 1.1's side blows (and the earlier build's overhead and thrust).
/// </summary>
public static class OneOneWindUps
{
    /// <summary>The module asset the wind-ups are read from (<c>Data/combat/windups.json</c>).</summary>
    public const string Asset = "combat/windups";

    /// <summary>The clip name a blow with its wind-up is registered under (<c>windup:right</c>): what the blow-quality references and the log name.</summary>
    public static string ClipName(string direction) => "windup:" + direction;

    /// <summary>
    /// The numbers when no module gives them (SANDBOX, ours; equal to the enhanced module's <c>windups.json</c>, which a test checks).
    /// The wind-ups take as long as each blow took to its ready point before (9, 11 and 13 frames), so the pace is
    /// unchanged; the strikes play at the blow's own pace.
    /// </summary>
    public static readonly IReadOnlyList<WindUpShape> Defaults =
    [
        new("left", 7f, 9f, 11f, -1f, new(-0.16f, 0.42f, 1.45f), new(-0.50f, -0.25f, 0.83f), new(0.15f, 0.50f, 1.25f), 0f),
        new("right", 9f, 12f, 13.5f, -1f, new(0.26f, 0.45f, 1.50f), new(0.40f, -0.10f, 0.91f), new(0.55f, 0.35f, 1.25f), 35f),
        new("overhead", 11f, 9f, 11f, -1f, new(0.14f, 0.42f, 1.78f), new(0.12f, -0.60f, 0.79f), new(0.45f, 0.25f, 1.55f), 0f),
        new("thrust", 13f, 13f, 13.5f, -1f, new(0.36f, -0.30f, 1.12f), new(-0.165f, 0.963f, 0.22f), new(0.55f, -0.05f, 1.05f), 0f),
    ];

    /// <summary>Which side each of 1.1's side blows serves (its forehand from the right, its backhand from the left).</summary>
    public static readonly IReadOnlyDictionary<string, string> SideOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["chrisAttackSword"] = "right",
        ["Christof_attackSword_util"] = "right",
        ["chrisAttack2Sword"] = "left",
    };

    /// <summary>
    /// The blows with their wind-ups for a kind (the same rule as the stitched and flattened blows: a one-handed blade on Christof's
    /// skeleton, else none): its flattened side blows (<paramref name="flat"/>, each with 1.1's own rate, <c>Strike</c> mark and busy
    /// time from <paramref name="quick"/>) for the sides it has — the guards keep their one blow, from the right — and its stitched
    /// overhead and thrust.
    /// </summary>
    public static IReadOnlyList<OneOneStitchedBlow> For(Skeleton skeleton, LimbMap? limbs, OneOneWeapon? weapon, IAnimationClip? stand, HeldThing? blade, int spineFirst,
                                                        IReadOnlyList<OneOneAction> quick, IReadOnlyList<(string From, StitchedClip Clip)> flat,
                                                        IReadOnlyList<OneOneStitchedBlow> stitched, IReadOnlyList<WindUpShape>? shapes = null, StitchHips? hips = null)
    {
        if (OneOneStitched.Why(skeleton, limbs, weapon, stand).Length > 0 || blade is not { Kind: HeldKind.Blade } b || b.Length <= 0f) return [];
        shapes ??= Defaults;
        var blows = new List<OneOneStitchedBlow>(4);
        foreach (var side in new[] { "left", "right" })
        {
            if (Shape(shapes, side) is not { } shape) continue;
            foreach (var (from, clip) in flat)
            {
                if (!SideOf.TryGetValue(from, out var s) || s != side) continue;
                var q = quick.FirstOrDefault(a => string.Equals(a.Clip.Name, from, StringComparison.OrdinalIgnoreCase));
                if (q is null) continue;
                blows.Add(Made(skeleton, limbs!, b, spineFirst, side, clip, q.Rate, q.StrikeSeconds, q.BusySeconds, from, shape, hips));
                break;
            }
        }
        foreach (var s in stitched)
            if (Shape(shapes, s.Direction) is { } shape)
                blows.Add(Made(skeleton, limbs!, b, spineFirst, s.Direction, s.Clip, s.Rate, s.StrikeSeconds, s.BusySeconds, s.From, shape, hips));
        return blows;
    }

    static WindUpShape? Shape(IReadOnlyList<WindUpShape> shapes, string direction)
    {
        foreach (var s in shapes) if (string.Equals(s.Direction, direction, StringComparison.OrdinalIgnoreCase)) return s;
        return null;
    }

    static OneOneStitchedBlow Made(Skeleton skeleton, LimbMap limbs, HeldThing b, int spineFirst, string direction, IAnimationClip clip, float rate, float strikeSeconds,
                                   float busy, string from, WindUpShape shape, StitchHips? hips)
    {
        var windUp = new WindUpClip(ClipName(direction), clip, shape, skeleton, limbs.RightArm, b.Bone, b.Guard, b.Tip, spineFirst) { Hips = hips };   // the hips held upright
        float strike = strikeSeconds >= 0f ? windUp.FrameOf(strikeSeconds * WindUpClip.FramesPerSecond) / WindUpClip.FramesPerSecond : -1f;
        return new OneOneStitchedBlow(direction, windUp, rate, strike, busy, windUp.ReadySeconds, from);
    }

    // ---- the guards' left ----

    /// <summary>The module's switch that keeps the guards' and the watchmen's one blow: no left lent, the earlier build's three directions.</summary>
    public const string GuardsOneBlowSwitch = "guards-one-blow";

    /// <summary>What a lent blow's <see cref="OneOneStitchedBlow.From"/> says (<c>lend:chrisAttack2Sword</c>).</summary>
    public static string LentFrom(string clip) => "lend:" + clip;

    /// <summary>
    /// The left for a one-handed blade of Christof's skeleton whose own blows have none: 1.1's guards and watchmen swing
    /// only the forehand, so they struck from the right, overhead or with the thrust and never from the left. Christof's backhand
    /// (<paramref name="backhand"/>, <see cref="OneOneStitched.LentBackhand"/>) flattened as his is, the off hand from the kind's own stand
    /// (<see cref="OneOneStitched.FlatLent"/>), with the left's wind-up into the left ready pose (the blade cocked over the left shoulder) —
    /// Christof's <c>windup:left</c> on the kind's own body — played as 1.1 plays the kind's quick blow: the whole clip in its weapon's first
    /// <c>STRIKETIME</c>, busy its first <c>RELOADTIME</c> (both the forehand's: the two clips are 29 frames), its <c>Strike</c> mark the
    /// backhand's. Null for a kind that gets no wind-ups.
    /// </summary>
    public static OneOneStitchedBlow? LentLeft(Skeleton skeleton, LimbMap? limbs, OneOneWeapon? weapon, IAnimationClip? stand, HeldThing? blade, int spineFirst,
                                               NadClip backhand, IReadOnlyList<WindUpShape>? shapes = null, StitchHips? hips = null)
    {
        if (OneOneStitched.Why(skeleton, limbs, weapon, stand).Length > 0 || blade is not { Kind: HeldKind.Blade } b || b.Length <= 0f) return null;
        if (Shape(shapes ?? Defaults, "left") is not { } shape || OneOneStitched.FlatLent(skeleton, limbs, weapon, stand, blade, backhand, hips) is not { } clip) return null;
        float strikeTime = weapon!.StrikeTime[0], reload = weapon.ReloadTime[0];
        float rate = strikeTime > 0.01f && backhand.Duration > 0f ? backhand.Duration / strikeTime : 1f;
        int i = Array.FindIndex(backhand.Tags, t => t.Type == OneOneActions.StrikeTag);
        float strike = i >= 0 ? backhand.Tags[i].Frame / NadClip.FramesPerSecond : -1f;
        float busy = reload > 0.01f ? reload : backhand.Duration / rate;
        return Made(skeleton, limbs!, b, spineFirst, "left", clip, rate, strike, busy, LentFrom(backhand.Name), shape, hips);
    }

    /// <summary>
    /// The shapes from a <c>windups.json</c> (its <c>blows</c>: per direction <c>wind_up_frames</c>, <c>strike_from_frame</c>,
    /// <c>join_at_frame</c>, <c>strike_frames</c>, <c>guard</c>, <c>blade</c>, <c>via</c> — three numbers each — and
    /// <c>turn_degrees</c>), a direction it does not give keeping <see cref="Defaults"/>'; what is wrong in <paramref name="problems"/>.
    /// </summary>
    public static IReadOnlyList<WindUpShape> Read(string json, string where, List<string> problems)
    {
        var shapes = Defaults.ToList();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!doc.RootElement.TryGetProperty("blows", out var blows) || blows.ValueKind != JsonValueKind.Array)
            {
                problems.Add($"{where}: no \"blows\"");
                return shapes;
            }
            foreach (var e in blows.EnumerateArray())
            {
                string? direction = e.TryGetProperty("direction", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                int i = shapes.FindIndex(s => string.Equals(s.Direction, direction, StringComparison.OrdinalIgnoreCase));
                if (i < 0) { problems.Add($"{where}: a blow's direction '{direction}' is none of left, right, overhead, thrust"); continue; }
                var was = shapes[i];
                float Num(string name, float otherwise)
                {
                    if (!e.TryGetProperty(name, out var v)) return otherwise;
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out float f) && float.IsFinite(f)) return f;
                    problems.Add($"{where}: {direction}: {name} is not a number");
                    return otherwise;
                }
                Vector3 Vec(string name, Vector3 otherwise)
                {
                    if (!e.TryGetProperty(name, out var v)) return otherwise;
                    if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 3 && v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number))
                    {
                        var a = v.EnumerateArray().Select(x => x.GetSingle()).ToArray();
                        return new Vector3(a[0], a[1], a[2]);
                    }
                    problems.Add($"{where}: {direction}: {name} is not three numbers");
                    return otherwise;
                }
                var shape = new WindUpShape(was.Direction, Num("wind_up_frames", was.WindUpFrames), Num("strike_from_frame", was.StrikeFrom), Num("join_at_frame", was.JoinAt),
                                            Num("strike_frames", was.StrikeFrames), Vec("guard", was.Guard), Vec("blade", was.Blade), Vec("via", was.Via), Num("turn_degrees", was.TurnDegrees));
                if (shape.WindUpFrames < 0f || shape.JoinAt < shape.StrikeFrom || shape.Blade.LengthSquared() < 1e-6f)
                {
                    problems.Add($"{where}: {direction}: wind_up_frames below 0, join_at_frame before strike_from_frame, or no blade direction");
                    continue;
                }
                shapes[i] = shape;
            }
        }
        catch (JsonException e) { problems.Add($"{where}: {e.Message}"); }
        return shapes;
    }
}
