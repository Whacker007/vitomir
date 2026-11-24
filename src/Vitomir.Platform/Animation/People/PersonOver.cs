// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository's clips (getting over and onto things; the stride carried on in a longer fall; walking
// and running off an edge; high landings while moving), played as its notes say (CONVENTIONS, Notes).
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.People;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// <b>getting over and onto things, and off edges</b> on the models repository's clips (<see cref="PersonOverClips"/>): whether they
/// play (<b>SANDBOX</b>, ours, the fades looked at in stills). Off (<see cref="Off"/>: <c>--over-clips-off</c>) a person vaults, climbs, mantles and hops
/// up on the traversal's own paths with the earlier build's stand-ins, and walks and runs off edges, jumps running and lands as before, and the world has no
/// <see cref="PersonOver"/> store. They ride on the air's clips: with <c>--air-clips-off</c> they are off too. A tape carries them in its note
/// <see cref="TapeNote"/>; a tape without it is the earlier build's.
/// </summary>
public sealed record PersonOverSettings
{
    /// <summary>Whether the models repository's moves over and onto things, off edges and the fitted air play (on by default; off: the earlier build's, <c>--over-clips-off</c>).</summary>
    public bool Clips { get; init; } = true;

    /// <summary>The fade into a move's clip from a pose that is not its first frame (s; 0.12): a climb from the stand, a vault from a walk, a move from the air.</summary>
    public float EnterSeconds { get; init; } = 0.12f;

    /// <summary>The fade into a walk-off or a run-off from a gait's frame that is not its first (s; 0.08), and into a landing met off its moment.</summary>
    public float CutSeconds { get; init; } = 0.08f;

    /// <summary>A drop below this (m) carries on with the gait off an edge (the run's notes: below about 0.3 m); from it, a run-off or a walk-off.</summary>
    public float OffFrom { get; init; } = 0.3f;

    /// <summary>
    /// <b>the run's and the sprint's own vault</b> — the player running or sprinting at a thin thing in the vault clips'
    /// bands vaults it by itself on the clips (no tap), entered on the run's left footfall as a tapped running vault is, played at the speed met
    /// (<see cref="BodyMotor.VaultRate"/>); a walk still needs a tap. On by default with the clips; off (<c>--auto-vault-off</c>, the setting
    /// <c>autoVaultOff</c>) the earlier build's: the sprint vault's stand-in at a sprint, a stop at a run until a tap. Carried in the note as <c>auto=1</c>.
    /// </summary>
    public bool Auto { get; init; } = true;

    /// <summary>Whether any is on.</summary>
    public bool Any => Clips;

    /// <summary>None.</summary>
    public static PersonOverSettings Off => new() { Clips = false, Auto = false };

    /// <summary>The tape head's note.</summary>
    public const string TapeNote = "over";

    /// <summary>The note: <c>off</c>, else <c>clips=1 enter=0.12 cut=0.08 off=0.3</c> (and <c>auto=1</c> with the run's own vault).</summary>
    public string Note() => !Clips ? "off" : string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"clips=1 enter={EnterSeconds:0.####} cut={CutSeconds:0.####} off={OffFrom:0.####}") + (Auto ? " auto=1" : "");

    /// <summary>
    /// These settings with a tape's note laid on; none (an older tape) or <c>off</c> is <see cref="Off"/>. The numbers go through the
    /// note's text; a note without <c>auto</c> has no run's own vault.
    /// </summary>
    public PersonOverSettings WithNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note) || !note.Contains('=')) return Off;
        var s = this with { Clips = true, Auto = false };
        foreach (var part in note.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || !float.TryParse(part[(eq + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
            s = part[..eq] switch
            {
                "clips" => s with { Clips = v != 0f }, "enter" => s with { EnterSeconds = v }, "cut" => s with { CutSeconds = v }, "off" => s with { OffFrom = v },
                "auto" => s with { Auto = v != 0f },
                _ => s,
            };
        }
        return s.Clips ? s : Off;
    }
}

/// <summary>Where a person is off an edge (<see cref="PersonOver.Off"/>).</summary>
public enum OffMode
{
    /// <summary>None: the gaits, the earlier build's air.</summary>
    None,
    /// <summary>A run-off begun on the ground, on the run's last footfall before the edge, the capsule not yet off.</summary>
    Edge,
    /// <summary>In the air in the clip that left with the stride (a run-off, a walk-off, a running jump), at the fitted rate.</summary>
    Stride,
    /// <summary>In the air in the pedalling loop after it, at the fitted rate.</summary>
    Pedal,
}

/// <summary>
/// <b>a person's moves over and onto things and off edges</b> — the simulation's state (digested): the motor's clip-carried move
/// (<see cref="Move"/>, <see cref="OverMove"/>), and the animation's air fitted to the fall. Only the people given one by the session have it, and a
/// world has the store only with the clips on and people who have them (a newer set), so the earlier builds' digest as before.
/// </summary>
public struct PersonOver
{
    /// <summary>The motor's: the clip-carried move under way, a vault's press held for the run's footfall.</summary>
    public OverMove Move;

    /// <summary>Off an edge: where (<see cref="OffMode"/>).</summary>
    public OffMode Off;

    /// <summary>The air fitted to the fall (the notes' rule): the rate the clip's air and the loop play at, and the loop's half-cycles after the clip.</summary>
    public float Rate;
    public int Half;

    /// <summary>The loop's own seconds still to play before the landing (the rule's n half-cycles; +∞ when the contact is not foreseen).</summary>
    public float Left;

    /// <summary>The foot the clip lands on (1 left, 2 right: its <c>land</c>), its loop entered at that foot's <c>lands</c> phase.</summary>
    public int Foot;

    /// <summary>1 when it left from a walk (the walking landings), 0 a run.</summary>
    public int Walking;

    /// <summary>The time from the take-off to contact foreseen (s; NaN none), and the time since (s).</summary>
    public float Expect;
    public float Flown;

    /// <summary>1 + the index of the clip-carried move's clip last played (<see cref="OverPaths.Clips"/>), for what follows it; 0 none.</summary>
    public int Played;

    /// <summary>1 while the run's pace is changed to bring its footfall to a vault (<see cref="PeopleOver.VaultWait"/>); 2 to an edge (a run-off's take-off).</summary>
    public int Paced;
}

/// <summary>What the edges' clips read of the world: the level, the body's numbers, where its feet are and how it moves (a reference to no level: none).</summary>
public readonly record struct OffWorld(CollisionWorld? Level, Locomotion Loco, Vector3 Feet, Vector3 Velocity);

/// <summary>
/// <b>a person's moves over and onto things and off edges</b> (the models repository's clips), found by name and by their
/// notes; −1 for one the set lacks. Null on <see cref="PersonMotion.Over"/> for a set without any (an older set).
/// </summary>
public sealed class PersonOverClips
{
    /// <summary>The clip-carried moves for the motor (<c>hop_up</c>, <c>vault_*</c>, <c>climb_onto_*</c>, <c>mantle_*</c>), or null for none.</summary>
    public OverPaths? Paths { get; init; }

    /// <summary>Running off and walking off an edge from the left and the right foot (<c>run_off_l</c>, <c>_r</c>; <c>walk_off_l</c>, <c>_r</c>).</summary>
    public required int[] RunOffs { get; init; }
    public required int[] WalkOffs { get; init; }

    /// <summary>The pedalling loops (<c>fall_run</c>, <c>fall_walk</c>).</summary>
    public int FallRun { get; init; } = -1;
    public int FallWalk { get; init; } = -1;

    /// <summary>The walking landings onto the left and the right foot, and the high ones running and walking (<c>land_walk_*</c>, <c>land_run_high_*</c>, <c>land_walk_high_*</c>).</summary>
    public required int[] WalkLandings { get; init; }
    public required int[] RunHighLandings { get; init; }
    public required int[] WalkHighLandings { get; init; }

    /// <summary>The player's standing high landing (<c>land_high_player</c>: <c>landing.who</c> <c>player</c>).</summary>
    public int LandHighPlayer { get; init; } = -1;

    /// <summary>Every clip above (the moves' and the edges'), for <see cref="Holds"/>.</summary>
    public required int[] All { get; init; }

    /// <summary>
    /// Measured from the clips (<see cref="Measure"/>, m ahead of the capsule's centre, at the kind's scale): each run-off's take-off foot's ball at its
    /// <c>takeoff_s</c> (behind the centre: negative), where it leaves the edge.
    /// </summary>
    public float[] OffBall { get; private set; } = [0f, 0f];

    /// <summary>The clips' own numbers measured in the model's space (the model faces +Y): the run-offs' take-off balls (<see cref="OffBall"/>).</summary>
    public void Measure(CharacterAssets assets, Skeleton skeleton, float scale)
    {
        int n = skeleton.Count;
        var local = new BoneTransform[n];
        var model = new System.Numerics.Matrix4x4[n];
        for (int i = 0; i < 2; i++)
        {
            int clip = RunOffs[i];
            if (clip < 0 || PersonAirClips.Notes(assets, clip)?.Airborne is not { } ab) continue;
            int ball = skeleton.Find(ab.EnterFoot == "r" ? "ball_r" : "ball_l");
            if (ball < 0) continue;
            assets.Clip(clip).Sample(float.IsNaN(ab.TakeoffS) ? 0f : ab.TakeoffS, skeleton, local);
            PoseEvaluator.ToModel(skeleton, local, model);
            OffBall[i] = model[ball].Translation.Y * scale;
        }
    }

    /// <summary>
    /// How high over its own top (<see cref="OverClip.TopM"/>) each low vault's hand has its knuckles at its contact, over <see cref="KnuckleOverPalm"/>
    /// (<see cref="OverClip.HandDrop"/>); only the run's low vaults (the earlier build's clips are left as they were). The model faces +Y. (The notes' point,
    /// <c>traverse.hands.at_m</c>, is no help here: <c>vault_speed</c>'s is 4.5 cm over its own top.)
    /// </summary>
    public void MeasureHands(CharacterAssets assets, Skeleton skeleton, float scale, in PersonRig rig)
    {
        if (Paths is null) return;
        int n = skeleton.Count;
        var local = new BoneTransform[n];
        var model = new System.Numerics.Matrix4x4[n];
        foreach (var c in Paths.Clips)
        {
            if (!c.RunOnly || c.Hands.Length == 0 || PersonAirClips.Notes(assets, c.Clip)?.Traverse is not { } tv) continue;
            var drop = new float[c.Hands.Length];
            for (int i = 0; i < c.Hands.Length; i++)
            {
                var h = c.Hands[i];
                var note = tv.Hands.FirstOrDefault(x => (x.Hand == "l") == h.Left && MathF.Abs(x.ContactS - h.ContactS) < 1e-4f);
                int tip = h.Left ? rig.LeftArm.Tip : rig.RightArm.Tip;
                if (note is null || tip < 0) continue;
                assets.Clip(c.Clip).Sample(h.ContactS, skeleton, local);
                PoseEvaluator.ToModel(skeleton, local, model);
                float knuckle = model[tip].Translation.Z * scale, top = c.TopM - c.At(h.ContactS).Up;   // the clip's own top over its capsule at the contact
                float over = knuckle - top - KnuckleOverPalm;
                drop[i] = over > 0.02f ? over : 0f;
            }
            c.HandDrop = drop;
        }
    }

    /// <summary>The knuckles' height over the palm's point on a top as before clips have them (m; <c>vault_low</c>'s 3 cm).</summary>
    public const float KnuckleOverPalm = 0.03f;

    /// <summary>Whether <paramref name="clip"/> is one of them.</summary>
    public bool Holds(int clip) => clip >= 0 && Array.IndexOf(All, clip) >= 0;

    /// <summary>The clip leaving an edge off a foot (1 left, 2 right) from a run or a walk, the other when the set lacks it, or −1.</summary>
    public int Off(bool walking, int foot)
    {
        var two = walking ? WalkOffs : RunOffs;
        int want = foot == 1 ? two[0] : two[1], other = foot == 1 ? two[1] : two[0];
        return want >= 0 ? want : other;
    }

    /// <summary>
    /// The landing moving on onto a foot (1 left, 2 right) for a drop of <paramref name="drop"/> m: walking or running, the high one from its band's
    /// foot (<c>drop_m</c>), else the soft one (running: the earlier build's <c>land_run_*</c>); −1 for none.
    /// </summary>
    public int Landing(CharacterAssets assets, PersonAirClips air, bool walking, int foot, float drop)
    {
        int i = foot == 1 ? 0 : 1;
        int soft = walking ? WalkLandings[i] : air.RunLanding(foot), high = walking ? WalkHighLandings[i] : RunHighLandings[i];
        if (high >= 0 && PersonAirClips.Notes(assets, high)?.Landing is { } l && drop >= l.DropLow - 1e-4f) return high;
        return soft >= 0 ? soft : high;
    }

    /// <summary>The clips from a set (the first of a name the one played) at the kind's <paramref name="scale"/>, or null when it has none of them.</summary>
    public static PersonOverClips? From(IReadOnlyList<INotedClip> motions, Func<string, INotedClip?> named, Func<INotedClip, int> indexOf, float scale)
    {
        int Off(string name) => named(name) is { Loops: false, Notes.Airborne: { Strides: true } } c ? indexOf(c) : -1;
        int Loop(string name) => named(name) is { Loops: true, Notes.Airborne: { Pedals: true } } c ? indexOf(c) : -1;
        int Land(string name) => named(name) is { Loops: false, Notes.Landing: { } } c ? indexOf(c) : -1;
        var moves = new List<OverClip>();
        foreach (var c in motions)
        {
            if (c.Loops || c.Notes.Traverse is not { Times.Length: >= 2 } tv || !ReferenceEquals(named(c.Name), c)) continue;
            // The low vaults the run takes by itself (a vault's notes, in and out of the run; a hurdle's name, or a low deep top's depths).
            bool low = tv.Enter == "run" && tv.ExitInto == "run" && (c.Name.StartsWith("hurdle", StringComparison.Ordinal) || !float.IsNaN(tv.DepthLow));
            var kind = tv.Thin || low ? TraversalKind.Vault : c.Name.StartsWith("hop", StringComparison.Ordinal) ? TraversalKind.HopUp
                     : c.Name.StartsWith("climb", StringComparison.Ordinal) ? TraversalKind.ClimbOnto : c.Name.StartsWith("mantle", StringComparison.Ordinal) ? TraversalKind.Mantle
                     : TraversalKind.None;
            if (kind == TraversalKind.None) continue;
            moves.Add(Move(c, tv, kind, indexOf(c), scale, low));
        }
        int player = motions.FirstOrDefault(c => !c.Loops && c.Notes.Landing is { Who: "player" } && ReferenceEquals(named(c.Name), c)) is { } p ? indexOf(p) : -1;
        int[] runOffs = [Off("run_off_l"), Off("run_off_r")], walkOffs = [Off("walk_off_l"), Off("walk_off_r")];
        int fallRun = Loop("fall_run"), fallWalk = Loop("fall_walk");
        int[] walkLand = [Land("land_walk_l"), Land("land_walk_r")], runHigh = [Land("land_run_high_l"), Land("land_run_high_r")],
              walkHigh = [Land("land_walk_high_l"), Land("land_walk_high_r")];
        int[] all = [.. moves.Select(m => m.Clip), .. runOffs, .. walkOffs, fallRun, fallWalk, .. walkLand, .. runHigh, .. walkHigh, player];
        all = [.. all.Where(c => c >= 0).Distinct()];
        if (all.Length == 0) return null;
        return new PersonOverClips
        {
            Paths = moves.Count > 0 ? new OverPaths { Clips = [.. moves.OrderBy(m => m.Kind).ThenBy(m => m.Low)] } : null,
            RunOffs = runOffs, WalkOffs = walkOffs, FallRun = fallRun, FallWalk = fallWalk, WalkLandings = walkLand, RunHighLandings = runHigh,
            WalkHighLandings = walkHigh, LandHighPlayer = player, All = all,
        };
    }

    /// <summary>A move's clip for the motor: its path and obstacle at the kind's scale; the warp's window (a hop up has none: the rise's first half).</summary>
    static OverClip Move(INotedClip c, ClipTraverse tv, TraversalKind kind, int index, float scale, bool low = false)
    {
        var f = tv.Forward.Select(x => x * scale).ToArray();
        var u = tv.Up.Select(x => x * scale).ToArray();
        bool onTop = !tv.Thin && tv.EndsOnTop;
        float rise = onTop ? u[^1] : u.Max();
        float from = float.IsNaN(tv.WarpFrom) ? 0f : tv.WarpFrom, to = tv.WarpTo;
        if (float.IsNaN(to) && low && !onTop)
        {
            // A hurdle (no hands, no warp): the start's difference out by the stride's top over the rail.
            to = tv.Times[^1] * 0.5f;
            float most = float.NegativeInfinity;
            for (int i = 0; i < u.Length; i++) if (u[i] > most + 1e-6f) { most = u[i]; to = tv.Times[i]; }
        }
        else if (float.IsNaN(to))
        {
            to = tv.Times[^1] * 0.5f;
            for (int i = 0; i < u.Length; i++) if (u[i] >= 0.5f * u[^1]) { to = tv.Times[i]; break; }
        }
        var hand = tv.Hands.Count > 0 ? tv.Hands.MinBy(h => h.ContactS) : null;
        return new OverClip
        {
            Clip = index, Name = c.Name, Kind = kind, Low = tv.Low * scale, High = tv.High * scale, Duration = c.Duration, T = [.. tv.Times], F = f, U = u,
            FaceM = tv.FaceM * scale, TopM = tv.TopM * scale, WarpFrom = from, WarpTo = MathF.Max(from, to), RiseTo = rise, EndsOnTop = onTop,
            ContactS = tv.ContactS, HandForward = (hand?.AtForward ?? 0f) * scale, HandUp = (hand?.AtUp ?? 0f) * scale,
            EnterPhase = tv.EnterPhase, ExitPhase = tv.ExitPhase, ExitSpeed = tv.Speeds.Count >= 3 ? tv.Speeds[2] * scale : 0f,
            SpeedIn = tv.Speeds.Count >= 1 ? tv.Speeds[0] * scale : 0f,
            Hands = [.. tv.Hands.Where(h => h.ReleaseS > h.ContactS).Select(h => new OverHand(h.Hand == "l", h.ContactS, h.ReleaseS))],
            RunOnly = low, Hurdle = low && tv.Thin && tv.Hands.Count == 0, DepthLow = tv.DepthLow * scale, DepthHigh = tv.DepthHigh * scale,
        };
    }
}

/// <summary>
/// <b>playing the clip-carried moves</b> (<see cref="AnimationSystem"/> calls it for a person with a <see cref="PersonOver"/>, before the air):
/// the move's clip at the motor's progress (its time the clip's own: the motor runs the clip's path), faded in from what was playing unless it is
/// its first frame (a vault from the run at its own phase); after a vault the run at its <c>exit_phase</c> with no fade; and a vault's press held
/// for the run's footfall told how long (<see cref="VaultWait"/>).
/// </summary>
public static class PeopleOver
{
    /// <summary>True when it chose the clip (its time set, its rate nought); false to let the air and the gaits choose.</summary>
    public static bool Choose(CharacterAssets assets, PersonMotion person, ref CharacterAnimation a, ref PersonAir air, ref PersonOver over, in MotionInput input,
                              PersonOverSettings rules, AnimationSettings animation)
    {
        var paths = person.Over?.Paths;
        if (paths is null) return false;
        if (input.Traversal != TraversalKind.None && over.Move.Clip > 0 && paths.At(over.Move.Clip - 1) is { } c)
        {
            var role = MotionSlots.Of(input.Traversal);
            if (a.Clip != c.Clip)
            {
                // Into it: a vault from the run at the clip's own phase with no fade (its first frames are the run's there); else faded in.
                float fade = rules.EnterSeconds;
                if (over.Move.Air == 0 && !float.IsNaN(c.EnterPhase) && a.Role == MotionRole.Run && a.Clip >= 0 && assets.Clip(a.Clip).Duration > 0f)
                {
                    float p = a.Time / assets.Clip(a.Clip).Duration - c.EnterPhase;
                    p -= MathF.Round(p);
                    if (MathF.Abs(p) <= 0.04f) fade = 0f;
                    // From the sprint at that phase (its left footfall too, its stride longer than the run's the clip begins from): a short fade.
                    if (fade == 0f && rules.Auto && person.Sprints(a.Clip)) fade = rules.CutSeconds;
                }
                PeopleAir.Begin(ref a, ref air, c.Clip, role, 0f, fade);
            }
            a.Role = role;
            a.Rate = 0f;
            air.Mode = AirMode.None;   // The earlier build's air has nothing to do in a move
            a.Time = over.Move.Entry + Math.Clamp(input.TraversalProgress, 0f, 1f) * (c.Duration - over.Move.Entry);
            over.Played = over.Move.Clip;
            over.Off = OffMode.None;
            return true;
        }
        if (over.Played > 0 && input.Traversal == TraversalKind.None)
        {
            // The move is over: after a vault, running on, the run at its exit_phase with no fade (the clip ends on that frame); else the gaits, faded.
            var done = paths.At(over.Played - 1);
            over.Played = 0;
            if (done is { Kind: TraversalKind.Vault } && a.Clip == done.Clip && input.Grounded && !float.IsNaN(done.ExitPhase) && person.Run is { } run
                && input.GroundSpeed >= animation.RunFrom - 0.5f * animation.Hysteresis)
            {
                int loop = run.Clip(input.ForwardSpeed, input.SideSpeed, run.First);
                a.Clip = loop;
                a.Role = MotionRole.Run;
                a.Time = done.ExitPhase * assets.Clip(loop).Duration;
                a.PreviousClip = -1;
                a.Blend = 1f;
                a.Rate = 0f;
                air.Fade = 0f;
            }
            return false;
        }
        if (over.Move.Pending != 0 && over.Move.Wait < -0.5f)
        {
            // A press held running at a vault: how long, and the run's pace to its footfall then (its strides lengthened or shortened, as a runner's before a fence).
            (over.Move.Wait, float pace) = VaultWait(assets, paths, a, over.Move);
            if (pace != 1f) { a.RateScale = pace; over.Paced = 1; }
        }
        else if (over.Paced == 1 && over.Move.Pending == 0)
        {
            if (a.Role == MotionRole.Run) a.RateScale = 1f;   // the press let go without a vault: the run's own pace again
            over.Paced = 0;
        }
        return false;
    }

    /// <summary>The most the run's pace is changed to bring its footfall to a vault (a share each way).</summary>
    public const float MostPace = 0.3f;

    /// <summary>
    /// How long a press made running at a vault is held (s), and the run's pace to then: the time the body takes to come to the clip's own distance
    /// from the face (<see cref="OverMove.Face"/> less <see cref="OverClip.FaceM"/> at <see cref="OverMove.Speed"/>), in whole ticks, and the run played
    /// at the pace (its own times a share within <see cref="MostPace"/>) that brings it to the footfall the clip begins on (its <c>enter_phase</c>, the
    /// left foot coming down) then — of the next three, the one asking least change. Where none can, the footfall nearest at the run's own pace and the
    /// rest left to the warp. (0, 1) when the run's phase is not known (the press goes at once).
    /// </summary>
    public static (float Wait, float Pace) VaultWait(CharacterAssets assets, OverPaths paths, in CharacterAnimation a, in OverMove move) =>
        paths.Any(TraversalKind.Vault) is not { } c ? (0f, 1f)
        : move.Pending == 2 ? NearestFootfallWait(assets, a, c.EnterPhase, c.FaceM, move.Face, move.Speed)   // the run's own vault
        : FootfallWait(assets, a, c.EnterPhase, c.FaceM, move.Face, move.Speed);

    /// <summary>How far from the clip's own distance from the face (m) the run's own vault may be let go on a footfall: early (further off: the warp
    /// takes it), late (nearer: within the start's reach, <see cref="BodyMotor.VaultNearer"/>).</summary>
    public const float EarlyMost = 0.6f, LateMost = 0.3f;

    /// <summary>
    /// <see cref="FootfallWait"/> for the run's own vault (<see cref="OverMove.Pending"/> 2), looked for from further off (about a second of the
    /// speed): of the run's next four left footfalls, the one the run's pace — changed by at most <see cref="MostPace"/> — brings nearest the moment the
    /// body is the clip's own distance from the face, a footfall a little early (the warp stretching the clip's first strides on) twice as welcome as
    /// one late (the body nearer the face than the clip). The hold is that footfall's time in whole ticks and the pace brings the footfall onto it.
    /// </summary>
    public static (float Wait, float Pace) NearestFootfallWait(CharacterAssets assets, in CharacterAnimation a, float enterPhase, float faceM, float face, float speed)
    {
        // Not in the run's loop yet (a start's clip): not told — asked again next tick (the hold's longest still bounds it).
        if (float.IsNaN(enterPhase) || speed < 0.5f) return (0f, 1f);
        if (a.Role != MotionRole.Run || a.Clip < 0) return (-1f, 1f);
        float d = assets.Clip(a.Clip).Duration, rate = MathF.Abs(a.Rate);
        if (d <= 0f || rate < 1e-3f || !assets.Clip(a.Clip).Loops) return (-1f, 1f);
        const float dt = Core.World.TickSeconds;
        float cycle = d / rate;
        float p = a.Time / d;
        p -= MathF.Floor(p);
        float to = enterPhase - p;
        to -= MathF.Floor(to);
        float ideal = MathF.Max(0f, (face - faceM) / speed);
        float most = BodyMotor.MostHold - 2f * dt;
        float bestOwn = float.NaN, bestWait = 0f, score = float.MaxValue;
        for (int k = 0; k < 4; k++)
        {
            float own = (to + k) * cycle;
            if (own < 2f * dt) continue;
            float pace = ideal > dt ? Math.Clamp(own / ideal, 1f - MostPace, 1f + MostPace) : 1f + MostPace;
            float wait = own / pace;
            if (wait > most + 1e-4f) break;
            // The face then (m off the clip's own distance): early within EarlyMost, late within LateMost (the start's reach either way), else not taken.
            float off = ideal - wait, metres = off * speed;
            if (metres > EarlyMost || metres < -LateMost) continue;
            float e = off >= 0f ? off : -2f * off;
            if (e < score) (bestOwn, bestWait, score) = (own, wait, e);
        }
        // None in reach: let go at the clip's own distance at the run's own pace (the clip faded in from the run's phase then).
        if (float.IsNaN(bestOwn)) return (Math.Max(1, (int)MathF.Round(MathF.Min(ideal, most) / dt)) * dt, 1f);
        int ticks = Math.Max(1, (int)MathF.Round(bestWait / dt));
        return (ticks * dt, bestOwn / (ticks * dt));
    }

    /// <summary>
    /// <see cref="VaultWait"/> for any clip entered from the run at <paramref name="enterPhase"/> with its face <paramref name="faceM"/> ahead (the
    /// running grab's too): the press made with the face <paramref name="face"/> ahead at <paramref name="speed"/>.
    /// </summary>
    public static (float Wait, float Pace) FootfallWait(CharacterAssets assets, in CharacterAnimation a, float enterPhase, float faceM, float face, float speed)
    {
        if (float.IsNaN(enterPhase) || a.Role != MotionRole.Run || a.Clip < 0 || speed < 0.5f) return (0f, 1f);
        float d = assets.Clip(a.Clip).Duration, rate = MathF.Abs(a.Rate);
        if (d <= 0f || rate < 1e-3f || !assets.Clip(a.Clip).Loops) return (0f, 1f);
        float cycle = d / rate;
        float p = a.Time / d;
        p -= MathF.Floor(p);
        float to = enterPhase - p;
        to -= MathF.Floor(to);
        const float dt = Core.World.TickSeconds;
        float ideal = MathF.Min(BodyMotor.MostHold - 2f * dt, MathF.Max(0f, (face - faceM) / speed));
        int ticks = Math.Max(1, (int)MathF.Round(ideal / dt));
        float wait = ticks * dt;
        float best = float.NaN, change = float.MaxValue;
        for (int k = 0; k < 3; k++)
        {
            float own = (to + k) * cycle;   // the run's own time to that footfall
            float pace = own / wait;
            float e = MathF.Abs(DetMath.Log(pace));
            if (e < change) (best, change) = (pace, e);
        }
        if (MathF.Abs(best - 1f) <= MostPace) return (wait, best);
        // None within reach of the pace: the footfall at the run's own, the face's difference left to the warp.
        float near = to * cycle;
        if (near < 2f * dt) near += cycle;
        return (MathF.Min(near, BodyMotor.MostHold - 2f * dt), 1f);
    }

    /// <summary>
    /// <b>the rule</b> (the models repository): the air fitted to the fall. With <paramref name="t"/> the time from the take-off to contact,
    /// <paramref name="a"/> the clip's air part and <paramref name="h"/> the loop's half-cycle, n ≥ 0 half-cycles after the clip, n making
    /// (a + n·h) / t nearest 1 as a ratio; the clip's air and the loop both played at that rate. (0, 1) when the contact is not foreseen.
    /// </summary>
    public static (int Half, float Rate) Rule(float a, float h, float t)
    {
        if (!(t > 1e-3f) || !(h > 0f) || a < 0f) return (0, 1f);
        int best = 0;
        float err = float.MaxValue;
        for (int n = 0; n < 64; n++)
        {
            float r = (a + n * h) / t;
            if (r <= 0f) continue;
            float e = MathF.Abs(DetMath.Log(r));
            if (e < err) (best, err) = (n, e);
            else if (r > 1f) break;
        }
        return (best, (a + best * h) / t);
    }
}

/// <summary>
/// <b>off an edge and the air fitted to the fall</b>, ahead of the older air (<see cref="PeopleAir"/>) for a
/// person with a <see cref="PersonOver"/>:
/// <list type="bullet">
/// <item><b>Running off an edge</b>: on each of the run's footfalls the way ahead is read (<see cref="BodyMotor.LeaveAhead"/>); when the body will leave
/// the ground before the other foot comes down, over a drop of <see cref="PersonOverSettings.OffFrom"/> or more, that footfall is the last before the
/// edge and <c>run_off_&lt;foot&gt;</c> begins on it (entered late enough that its take-off falls where the body leaves). Leaving a run any other way
/// over such a drop, the run-off of the foot planted, at its take-off. Below the drop the run carries on.</item>
/// <item><b>Walking off an edge</b>: leaving the ground at a walk over such a drop, <c>walk_off_&lt;foot&gt;</c> of the foot whose phase the walk is
/// nearest (its <c>enter_phase</c>), from its first frame (the capsule falls from it).</item>
/// <item><b>The rule</b>: as the body leaves (or a running jump's clip is in the air), the time to contact foreseen from the ground below
/// (<see cref="BodyMotor.ContactTime"/>), the clip's air part and the pedalling loop (<c>fall_run</c>, <c>fall_walk</c>) fitted to it
/// (<see cref="PeopleOver.Rule"/>); the loop begun at its <c>lands</c> phase of the clip's landing foot, no blend.</item>
/// <item><b>The landing</b> onto the clip's foot after an even number of half-cycles, the other after an odd (the loop's phase at contact), by the drop's
/// band (<c>land_run_*</c>, <c>land_run_high_*</c>, <c>land_walk_*</c>, <c>land_walk_high_*</c>), from its first frame with no blend (faded when the
/// ground came off its moment); a walking landing into the walk at its <c>exit_phase</c>, a running one into the run.</item>
/// <item>The player's standing high landing is <c>land_high_player</c>; everyone else's <c>land_high</c>.</item>
/// </list>
/// </summary>
public static class PeopleOff
{
    /// <summary>How far past a run-off's take-off it may stand on the ground before it is given up (s: the edge was not met after all).</summary>
    public const float GiveUpAfter = 0.25f;

    /// <summary>How near its moment (s of real time) a landing is met to follow with no blend.</summary>
    public const float OnTime = 2.5f * Core.World.TickSeconds;

    /// <summary>True when it chose the clip (the caller only advances it); false to let the earlier build's air and the gaits choose.</summary>
    public static bool Choose(CharacterAssets assets, PersonMotion person, ref CharacterAnimation a, ref PersonAir air, ref PersonOver over, in MotionInput input,
                              in AirBody body, PersonOverSettings rules, AnimationSettings animation, float seconds, in OffWorld world)
    {
        var clips = person.Over!;
        var airClips = person.Air!;
        if (input.Traversal != TraversalKind.None || input.Dodging) { over.Off = OffMode.None; return false; }
        if (input.Grounded)
        {
            if (over.Off is OffMode.Stride or OffMode.Pedal) return Contact(assets, person, ref a, ref air, ref over, input, body, rules);
            if (over.Off == OffMode.Edge)
            {
                if (!PeopleAir.Running(input, animation, ahead: true) || PersonAirClips.Notes(assets, a.Clip)?.Airborne is not { } ab || a.Time > ab.TakeoffS + GiveUpAfter)
                {
                    over.Off = OffMode.None;   // no edge after all: to the gaits, faded
                    return false;
                }
                a.Role = MotionRole.Run;
                a.Rate = 1f;
                return true;
            }
            if (air.Mode == AirMode.Land && clips.Holds(a.Clip)) return Landed(assets, person, ref a, ref air, input, animation, seconds);
            if (air.Mode == AirMode.None && a.Role == MotionRole.Run && PeopleAir.Running(input, animation, ahead: true)) return LastFootfall(assets, person, ref a, ref air, ref over, input, rules, seconds, world);
            if (air.Mode == AirMode.None && a.Role == MotionRole.Walk && input.ForwardSpeed >= 0.7071f * input.GroundSpeed) PaceWalk(assets, person, ref a, ref over, input, rules, seconds, world);
            else if (over.Paced == 2) { a.RateScale = 1f; over.Paced = 0; }
            return false;
        }

        // In the air.
        if (over.Off == OffMode.Edge)
        {
            // The run-off's capsule off the edge: the air fitted to the fall from here.
            air.Mode = AirMode.Fall;
            air.Speed = 0f;
            Fit(assets, clips, ref a, ref over, world, walking: false);
        }
        else if (over.Off == OffMode.None && (air.Mode is AirMode.None or AirMode.Land) && a.WasGrounded && !body.Jumped)
        {
            // Off an edge not foreseen: a run-off at its take-off or a walk-off from its first frame, over a drop of OffFrom or more; below, as before.
            float t = world.Level is null ? float.NaN : BodyMotor.ContactTime(world.Level, world.Feet, world.Velocity, world.Loco.Gravity);
            float drop = float.IsNaN(t) ? float.MaxValue : 0.5f * world.Loco.Gravity * t * t - world.Velocity.Z * t;
            if (drop < rules.OffFrom) return false;
            bool running = PeopleAir.Running(input, animation, ahead: true);
            bool walking = !running && input.GroundSpeed >= animation.IdleBelow + 0.5f * animation.Hysteresis && input.ForwardSpeed >= 0.7071f * input.GroundSpeed;
            if (!running && !walking) return false;
            int foot = running ? Planted(assets, a) : EdgeFoot(assets, clips, a);
            int clip = clips.Off(!running, foot);
            if (clip < 0 || PersonAirClips.Notes(assets, clip)?.Airborne is not { } ab) return false;
            float entry = running ? ab.TakeoffS : 0f;
            float fade = rules.CutSeconds;
            if (!running && !float.IsNaN(ab.EnterPhase) && a.Clip >= 0 && assets.Clip(a.Clip).Duration > 0f)
            {
                float p = a.Time / assets.Clip(a.Clip).Duration - ab.EnterPhase;
                p -= MathF.Round(p);
                if (MathF.Abs(p) <= 0.04f) fade = 0f;
            }
            PeopleAir.Begin(ref a, ref air, clip, MotionRole.Jump, entry, fade);
            air.Mode = AirMode.Fall;
            air.Speed = 0f;
            Fit(assets, clips, ref a, ref over, world, walking);
        }
        else if (over.Off == OffMode.None && air.Mode == AirMode.Rise && PersonAirClips.Notes(assets, a.Clip)?.Airborne is { Running: true, Strides: true })
        {
            // A running jump's clip in the air (then fall_run): fitted to the fall from here.
            Fit(assets, clips, ref a, ref over, world, walking: false);
        }
        if (over.Off is OffMode.Stride or OffMode.Pedal) return Fly(assets, clips, ref a, ref over, seconds, world);
        return false;
    }

    /// <summary>
    /// The earlier build's choice made this tick laid over (after <see cref="PeopleAir.Choose"/>): the player's standing high landing is <c>land_high_player</c>,
    /// everyone else's <c>land_high</c> (the earlier build's took the set's first in the band, and above every band its last).
    /// </summary>
    public static void After(CharacterAssets assets, PersonMotion person, ref CharacterAnimation a, in PersonAir air)
    {
        var clips = person.Over!;
        if (air.Mode != AirMode.Land || clips.LandHighPlayer < 0 || person.Air is not { } airClips || !airClips.Stands(a.Clip)) return;
        if (air.Curve == 1)
        {
            if (a.Clip != clips.LandHighPlayer && PersonAirClips.Notes(assets, clips.LandHighPlayer)?.Landing is { } l && air.Drop >= l.DropLow - 1e-4f)
                a.Clip = clips.LandHighPlayer;
        }
        else if (a.Clip == clips.LandHighPlayer)
        {
            foreach (int c in airClips.Landings)
                if (c != clips.LandHighPlayer && PersonAirClips.Notes(assets, c)?.Landing is { } l && air.Drop >= l.DropLow - 1e-4f) a.Clip = c;
        }
    }

    /// <summary>
    /// On a footfall of the run with an edge ahead (a drop of <see cref="PersonOverSettings.OffFrom"/> or more where the body would leave the ground,
    /// <see cref="BodyMotor.LeaveAhead"/>): the take-off that falls at the edge — the run-off's take-off foot's ball (<see cref="PersonOverClips.OffBall"/>)
    /// over the edge at its <c>takeoff_s</c>. This foot's when it can: its run-off begins on this footfall (entered later when the edge is nearer than
    /// its stance goes). Else a later footfall's, the run paced to bring it there (its strides a share longer or shorter, within
    /// <see cref="PeopleOver.MostPace"/>, as a runner's before an edge); every footfall looks again.
    /// </summary>
    static bool LastFootfall(CharacterAssets assets, PersonMotion person, ref CharacterAnimation a, ref PersonAir air, ref PersonOver over, in MotionInput input,
                             PersonOverSettings rules, float seconds, in OffWorld world)
    {
        if (world.Level is null || PeopleAir.SinceDown(assets, a) is not var (l, r)) return false;
        int foot = l < seconds - 1e-6f ? 1 : r < seconds - 1e-6f ? 2 : 0;
        if (foot == 0) return false;
        var clips = person.Over!;
        float speed = input.GroundSpeed;
        var v = new Vector2(world.Velocity.X, world.Velocity.Y);
        float d = assets.Clip(a.Clip).Duration, own = MathF.Abs(a.Rate) / MathF.Max(0.1f, a.RateScale);
        bool planned = false;
        if (speed >= 0.5f && v.LengthSquared() > 1e-4f && d > 0f && own > 1e-3f
            && PersonAirClips.Notes(assets, clips.RunOffs[0])?.Airborne is { } first)
        {
            float half = 0.5f * d / own, takeoff = first.TakeoffS;
            float look = speed * (3f * half + takeoff) + world.Loco.Radius + 0.3f;
            if (BodyMotor.LeaveAhead(world.Level, world.Feet, Vector2.Normalize(v), world.Loco, look, out float distance, out float drop) && drop >= rules.OffFrom)
            {
                float edge = distance;   // the edge: a person lets go of it as its middle passes over it (BodyMotor.LeaveAhead, Mover.LeavesAtEdge)
                float since = foot == 1 ? l : r;
                // This foot: its take-off (takeoff_s after its footfall) at the edge, the ball over it.
                int now = clips.Off(walking: false, foot);
                float tau = (edge - clips.OffBall[foot - 1]) / speed;   // from now to the centre where this foot's ball is over the edge
                float natural = takeoff - since;
                if (now >= 0 && tau <= natural + 2.5f * Core.World.TickSeconds)
                {
                    float entry = MathF.Min(takeoff, since + MathF.Max(0f, natural - tau));
                    PeopleAir.Begin(ref a, ref air, now, MotionRole.Run, entry, entry > since + 1.5f * Core.World.TickSeconds ? rules.CutSeconds : 0f);
                    a.Rate = 1f;
                    over.Off = OffMode.Edge;
                    over.Walking = 0;
                    over.Paced = 0;
                    return true;
                }
                // A later footfall's take-off: the run paced so the k-th footfall from now comes when its run-off's take-off then falls at the edge.
                float best = float.NaN, change = float.MaxValue;
                for (int k = 1; k <= 3; k++)
                {
                    int f = k % 2 == 0 ? foot : 3 - foot;
                    float at = (edge - clips.OffBall[f - 1]) / speed - takeoff;   // from now to that footfall
                    if (at <= 2f * Core.World.TickSeconds) continue;
                    float pace = (k * half) / at;
                    float e = MathF.Abs(DetMath.Log(pace));
                    if (e < change) (best, change) = (pace, e);
                }
                if (!float.IsNaN(best) && MathF.Abs(best - 1f) <= PeopleOver.MostPace)
                {
                    a.RateScale = best;
                    over.Paced = 2;
                    planned = true;
                }
            }
        }
        if (!planned && over.Paced == 2) { a.RateScale = 1f; over.Paced = 0; }   // no edge to pace for now: the run's own pace again
        return false;
    }

    /// <summary>
    /// On a footfall of the walk with an edge ahead (a drop of <see cref="PersonOverSettings.OffFrom"/> or more): the walk paced (within
    /// <see cref="PeopleOver.MostPace"/>) so that its middle comes over the edge — where the body leaves the ground and the walk-off begins, the capsule
    /// falling from its first frame — at a walk-off's <c>enter_phase</c> (the body over that foot on the edge), so the walk-off follows the walk with no
    /// fade. Every footfall looks again; none in reach, the walk's own pace.
    /// </summary>
    static void PaceWalk(CharacterAssets assets, PersonMotion person, ref CharacterAnimation a, ref PersonOver over, in MotionInput input, PersonOverSettings rules,
                         float seconds, in OffWorld world)
    {
        if (world.Level is null || a.Clip < 0 || PeopleAir.SinceDown(assets, a) is not var (l, r) || (l >= seconds - 1e-6f && r >= seconds - 1e-6f)) return;
        var clips = person.Over!;
        float speed = input.GroundSpeed;
        var v = new Vector2(world.Velocity.X, world.Velocity.Y);
        float d = assets.Clip(a.Clip).Duration, own = MathF.Abs(a.Rate) / MathF.Max(0.1f, a.RateScale);
        bool planned = false;
        if (speed >= 0.3f && v.LengthSquared() > 1e-4f && d > 0f && own > 1e-3f)
        {
            float cycle = d / own;
            if (BodyMotor.LeaveAhead(world.Level, world.Feet, Vector2.Normalize(v), world.Loco, speed * 1.6f * cycle + 0.3f, out float distance, out float drop) && drop >= rules.OffFrom)
            {
                float tau = distance / speed;
                float p = a.Time / d;
                p -= MathF.Floor(p);
                float best = float.NaN, change = float.MaxValue;
                foreach (int clip in clips.WalkOffs)
                {
                    if (PersonAirClips.Notes(assets, clip)?.Airborne?.EnterPhase is not { } at || float.IsNaN(at)) continue;
                    float to = at - p;
                    to -= MathF.Floor(to);
                    for (int k = 0; k < 3; k++)
                    {
                        if ((to + k) * cycle < 2f * Core.World.TickSeconds) continue;
                        float pace = (to + k) * cycle / tau;
                        float e = MathF.Abs(DetMath.Log(pace));
                        if (e < change) (best, change) = (pace, e);
                    }
                }
                if (!float.IsNaN(best) && MathF.Abs(best - 1f) <= PeopleOver.MostPace)
                {
                    a.RateScale = best;
                    over.Paced = 2;
                    planned = true;
                }
            }
        }
        if (!planned && over.Paced == 2) { a.RateScale = 1f; over.Paced = 0; }
    }

    /// <summary>The air fitted to the fall (<see cref="PeopleOver.Rule"/>) from the clip playing now (a run-off, a walk-off, a running jump).</summary>
    static void Fit(CharacterAssets assets, PersonOverClips clips, ref CharacterAnimation a, ref PersonOver over, in OffWorld world, bool walking)
    {
        var ab = PersonAirClips.Notes(assets, a.Clip)?.Airborne;
        bool walks = walking || ab?.Enter == "walk";
        int loop = walks ? clips.FallWalk : clips.FallRun;
        var lp = PersonAirClips.Notes(assets, loop)?.Airborne;
        float t = world.Level is null ? float.NaN : BodyMotor.ContactTime(world.Level, world.Feet, world.Velocity, world.Loco.Gravity);
        // The air part as the notes give it (air_s may run a frame's rounding past the clip's last frame), from where the clip is now.
        float air = ab is { Strides: true } ? MathF.Max(0f, ab.AirTo - MathF.Max(a.Time, ab.AirFrom)) : MathF.Max(0f, assets.Clip(a.Clip).Duration - a.Time);
        var (n, rate) = lp is { Pedals: true } ? PeopleOver.Rule(air, lp.HalfS, t) : (0, 1f);
        if (float.IsNaN(t)) (n, rate) = (int.MaxValue, 1f);   // no ground foreseen: pedalling until it comes
        over.Off = OffMode.Stride;
        over.Rate = Math.Clamp(rate, 0.4f, 2.5f);
        over.Half = n;
        over.Left = n == int.MaxValue || lp is not { Pedals: true } ? float.PositiveInfinity : n * lp.HalfS;
        over.Foot = ab is not null && ab.Land.Length > 0 ? (ab.Land[^1] == 'l' ? 1 : 2) : 2;
        over.Walking = walks ? 1 : 0;
        over.Expect = t;
        over.Flown = 0f;
    }

    /// <summary>
    /// A tick in the fitted air: the clip's air at the rate to its end, then the loop (from its <c>lands</c> phase of the clip's landing foot, no blend)
    /// for the half-cycles; past them, pedalling on until the ground comes.
    /// </summary>
    static bool Fly(CharacterAssets assets, PersonOverClips clips, ref CharacterAnimation a, ref PersonOver over, float seconds, in OffWorld world)
    {
        over.Flown += seconds;
        var held = over.Off == OffMode.Stride ? PersonAirClips.Notes(assets, a.Clip)?.Airborne : null;
        float end = held is { Strides: true } ? MathF.Min(held.AirTo, assets.Clip(a.Clip).Duration) : assets.Clip(a.Clip).Duration;
        // The fit kept to the fall as it goes (a body rolling off an edge's corner, air control): the rate that brings what is left of the clip's air
        // and the loop's half-cycles to the contact foreseen from here.
        if (world.Level is not null && float.IsFinite(over.Left))
        {
            float left = (over.Off == OffMode.Stride ? MathF.Max(0f, end - a.Time) : 0f) + MathF.Max(0f, over.Left);
            float t = BodyMotor.ContactTime(world.Level, world.Feet, world.Velocity, world.Loco.Gravity, 2f);
            if (left > 1e-3f && t > seconds) over.Rate = Math.Clamp(left / t, 0.4f, 2.5f);
        }
        if (over.Off == OffMode.Stride)
        {
            int loop = over.Walking != 0 ? clips.FallWalk : clips.FallRun;
            bool late = !float.IsNaN(over.Expect) && over.Flown > over.Expect + OnTime;
            if (a.Time + seconds * over.Rate >= end - 1e-5f && loop >= 0 && (over.Half > 0 || late)
                && PersonAirClips.Notes(assets, loop)?.Airborne is { Pedals: true } lp)
            {
                // The loop from where it is ready to land on the clip's foot: that frame is the clip's last.
                float phase = lp.Lands(over.Foot);
                a.Clip = loop;
                a.Time = (float.IsNaN(phase) ? 0f : phase) * assets.Clip(loop).Duration;
                a.PreviousClip = -1;
                a.Blend = 1f;
                a.Role = MotionRole.Fall;
                a.Rate = over.Rate;
                over.Off = OffMode.Pedal;
                return true;
            }
            a.Role = MotionRole.Jump;
            if (a.Time + seconds * over.Rate >= end) { a.Time = end; a.Rate = 0f; }   // n = 0: its last frame, the landing's first, held to the ground
            else a.Rate = over.Rate;
            return true;
        }
        a.Role = MotionRole.Fall;
        a.Rate = over.Rate;
        over.Left -= over.Rate * seconds;
        return true;
    }

    /// <summary>
    /// The contact after the fitted air: onto the clip's foot from the clip, or from the loop onto the foot whose <c>lands</c> phase it is nearest, by the
    /// drop's band and the gait it left from; from the landing's first frame, with no blend when met at its moment.
    /// </summary>
    static bool Contact(CharacterAssets assets, PersonMotion person, ref CharacterAnimation a, ref PersonAir air, ref PersonOver over, in MotionInput input,
                        in AirBody body, PersonOverSettings rules)
    {
        var clips = person.Over!;
        int foot = over.Foot;
        bool onTime;
        if (over.Off == OffMode.Pedal && PersonAirClips.Notes(assets, a.Clip)?.Airborne is { Pedals: true } lp)
        {
            float d = assets.Clip(a.Clip).Duration;
            float p = d > 0f ? a.Time / d : 0f;
            float toL = Cyclic(p - lp.LandsL), toR = Cyclic(p - lp.LandsR);
            foot = MathF.Abs(toL) <= MathF.Abs(toR) ? 1 : 2;
            onTime = MathF.Min(MathF.Abs(toL), MathF.Abs(toR)) * d <= OnTime * over.Rate;
        }
        else
        {
            var ab = PersonAirClips.Notes(assets, a.Clip)?.Airborne;
            float end = ab is { Strides: true } ? MathF.Min(ab.AirTo, assets.Clip(a.Clip).Duration) : assets.Clip(a.Clip).Duration;
            onTime = a.Time >= end - OnTime * over.Rate;
        }
        air.Drop = body.Fell;
        int land = clips.Landing(assets, person.Air!, over.Walking != 0, foot, air.Drop);
        over.Off = OffMode.None;
        if (land < 0) return false;
        PeopleAir.Begin(ref a, ref air, land, over.Walking != 0 ? MotionRole.Walk : MotionRole.Run, 0f, onTime ? 0f : rules.CutSeconds);
        air.Mode = AirMode.Land;
        a.Rate = 0f;   // its first frame, the moment of contact, this tick
        return true;
    }

    /// <summary>A landing moving on (of these clips) to its end: then the walk or the run at its <c>exit_phase</c> with no fade; stopped, to the gaits, faded.</summary>
    static bool Landed(CharacterAssets assets, PersonMotion person, ref CharacterAnimation a, ref PersonAir air, in MotionInput input, AnimationSettings animation, float seconds)
    {
        var ln = PersonAirClips.Notes(assets, a.Clip)?.Landing;
        if (ln is null || !ln.Running) return false;   // a standing one (the player's high landing): the earlier build's
        bool walks = ln.Walking;
        if (input.GroundSpeed < animation.IdleBelow) { air.Mode = AirMode.None; air.Fade = 0f; return false; }
        if (a.Time + seconds >= assets.Clip(a.Clip).Duration)
        {
            var gait = walks ? person.Walk : person.Run;
            air.Mode = AirMode.None;
            air.Fade = 0f;
            if (gait is null) return false;
            int loop = gait.Clip(input.ForwardSpeed, input.SideSpeed, gait.First);
            a.Clip = loop;
            a.Role = walks ? MotionRole.Walk : MotionRole.Run;
            a.Time = ln.ExitPhase * assets.Clip(loop).Duration;
            a.PreviousClip = -1;
            a.Blend = 1f;
            a.Rate = 0f;
            return false;
        }
        a.Role = walks ? MotionRole.Walk : MotionRole.Run;
        a.Rate = 1f;
        return true;
    }

    /// <summary>The foot the run has planted now (1 left, 2 right): the one that came down most lately.</summary>
    static int Planted(CharacterAssets assets, in CharacterAnimation a) => PeopleAir.SinceDown(assets, a) is var (l, r) ? (l <= r ? 1 : 2) : 1;

    /// <summary>The foot on the edge walking off it (1 left, 2 right): the one whose walk-off's <c>enter_phase</c> the walk's phase is nearest.</summary>
    static int EdgeFoot(CharacterAssets assets, PersonOverClips clips, in CharacterAnimation a)
    {
        if (a.Clip < 0 || assets.Clip(a.Clip).Duration <= 0f) return 1;
        float p = a.Time / assets.Clip(a.Clip).Duration;
        float pl = PersonAirClips.Notes(assets, clips.WalkOffs[0])?.Airborne?.EnterPhase ?? float.NaN;
        float pr = PersonAirClips.Notes(assets, clips.WalkOffs[1])?.Airborne?.EnterPhase ?? float.NaN;
        if (float.IsNaN(pl)) return 2;
        if (float.IsNaN(pr)) return 1;
        return MathF.Abs(Cyclic(p - pl)) <= MathF.Abs(Cyclic(p - pr)) ? 1 : 2;
    }

    /// <summary>A share of a cycle brought to −0.5 to 0.5.</summary>
    static float Cyclic(float x) => x - MathF.Round(x);
}
