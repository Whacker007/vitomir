// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository's clips (ladders; the upright ladder into a braced hang and back), played as their notes
// say (CONVENTIONS, Notes, traverse.ladder, top, landing, hang).
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.People;
using Vitomir.Platform.Bodies;

namespace Vitomir.Platform.Animation.People;

/// <summary>
/// <b>a person's ladder on clips</b> — the simulation's state (digested): the motor's (<see cref="Move"/>, <see cref="LadderClipMove"/>) and the
/// animation's last clip. Only the player has one (the session gives it, with the ledge's), and a world has the store only with tier 1's ladders on
/// and people who have the clips (a newer set), so the earlier build's digest as before.
/// </summary>
public struct PersonLadder
{
    /// <summary>The motor's: the ladder clip carrying the capsule, the ladder position, the climb's way.</summary>
    public LadderClipMove Move;

    /// <summary>1 + the index of the ladder clip last played (<see cref="LadderPaths.Clips"/>), 0 none.</summary>
    public int Played;
}

/// <summary>
/// <b>a person's ladder clips</b> (the models repository's clips: <c>ladder_*</c> leaning 15°, <c>ladder_v_*</c> upright), found by their notes
/// (<c>traverse.ladder</c>, <c>top</c>, <c>landing</c>, <c>hang</c>, <c>climb</c>); null on <see cref="PersonMotion.Ladder"/> for a set without a whole set
/// (an idle, a climb up and down, on and off at the foot and at the top).
/// </summary>
public sealed class PersonLadderClips
{
    public required LadderPaths Paths { get; init; }

    /// <summary>Every clip above, for <see cref="Holds"/>.</summary>
    public required int[] All { get; init; }

    public bool Holds(int clip) => clip >= 0 && Array.IndexOf(All, clip) >= 0;

    /// <summary>The clips from a set (the first of a name the one played) at the kind's <paramref name="scale"/>, or null when neither set is whole.</summary>
    public static PersonLadderClips? From(IReadOnlyList<INotedClip> motions, Func<string, INotedClip?> named, Func<INotedClip, int> indexOf, float scale)
    {
        var clips = new List<LadderClip>();
        var notes = new Dictionary<LadderClip, ClipLadder>();
        foreach (var c in motions)
        {
            if (c.Notes.Traverse is not { Ladder: { } ld, Times.Length: >= 2 } tv || !ReferenceEquals(named(c.Name), c)) continue;
            var role = RoleOf(ld, tv);
            if (role == LadderRole.None) continue;
            var clip = Clip(c, tv, role, indexOf(c), scale);
            clips.Add(clip);
            notes[clip] = ld;
        }
        LadderSet? Set(bool vertical)
        {
            bool Of(LadderClip c) => (MathF.Abs(notes[c].TiltDeg) < 1f) == vertical;
            LadderClip? One(LadderRole r, bool rails = false) => clips.FirstOrDefault(c => Of(c) && c.Role == r && c.Rails == rails);
            LadderClip? Side(LadderRole r, int way) => clips.FirstOrDefault(c => Of(c) && c.Role == r && c.Side == way);
            if (One(LadderRole.Idle) is not { } idle || One(LadderRole.Up) is not { } up || One(LadderRole.Down) is not { } down || One(LadderRole.OnBottom) is not { } onB
                || One(LadderRole.OffBottom) is not { } offB || One(LadderRole.OnTop) is not { } onT || One(LadderRole.OffTop) is not { } offT) return null;
            var n = notes[idle];
            return new LadderSet
            {
                Vertical = vertical, Tilt = n.TiltDeg * Core.DetMath.DegToRad, Rung = n.RungM * scale, FirstRung = n.FirstRungM * scale, RungFwd = n.RungFwdM * scale,
                LowerRungUp = n.LowerRungUpM * scale, HandsApart = float.IsNaN(n.HandsApartM) ? 0f : n.HandsApartM * scale,
                Idle = idle, Up = up, Down = down, OnBottom = onB, OffBottom = offB, OnTop = onT, OffTop = offT,
                OnTopRails = One(LadderRole.OnTop, rails: true), OffTopRails = One(LadderRole.OffTop, rails: true),
                OnSide = [Side(LadderRole.OnSide, 1), Side(LadderRole.OnSide, -1)], OffSide = [Side(LadderRole.OffSide, 1), Side(LadderRole.OffSide, -1)],
                ToHang = [Side(LadderRole.ToHang, 1), Side(LadderRole.ToHang, -1)], FromHang = [Side(LadderRole.FromHang, 1), Side(LadderRole.FromHang, -1)],
            };
        }
        var vertical = Set(true);
        var leaning = Set(false);
        if (vertical is null && leaning is null) return null;
        return new PersonLadderClips
        {
            Paths = new LadderPaths { Clips = [.. clips], Vertical = vertical, Leaning = leaning },
            All = [.. clips.Select(c => c.Clip).Distinct()],
        };
    }

    /// <summary>What a ladder clip plays, by its notes' kind (<c>idle</c>, <c>climb</c> by its way, <c>on</c>/<c>off</c> at the bottom, the top, a side, a hang).</summary>
    static LadderRole RoleOf(ClipLadder ld, ClipTraverse tv) => ld.Kind switch
    {
        "idle" => LadderRole.Idle,
        "climb" => tv.Climb?.Way == "down" ? LadderRole.Down : tv.Climb?.Way == "up" ? LadderRole.Up : LadderRole.None,
        "on" or "on bottom" => LadderRole.OnBottom,
        "off" or "off bottom" => LadderRole.OffBottom,
        "on top" => LadderRole.OnTop,
        "off top" => LadderRole.OffTop,
        "on side" => LadderRole.OnSide,
        "off side" => LadderRole.OffSide,
        "to hang" => LadderRole.ToHang,
        "from hang" => LadderRole.FromHang,
        _ => LadderRole.None,
    };

    /// <summary>A ladder clip for the motor: its path and its numbers at the kind's scale.</summary>
    static LadderClip Clip(INotedClip c, ClipTraverse tv, LadderRole role, int index, float scale)
    {
        string to = tv.Landing?.To ?? tv.LadderHang?.To ?? "";
        return new LadderClip
        {
            Clip = index, Name = c.Name, Role = role, Duration = c.Duration, Loops = c.Loops, Side = to == "l" ? 1 : to == "r" ? -1 : 0, Rails = tv.Top?.Rails ?? false,
            T = [.. tv.Times], F = [.. tv.Forward.Select(x => x * scale)], U = [.. tv.Up.Select(x => x * scale)], S = [.. tv.Side.Select(x => x * scale)],
            TopRung = tv.Top?.Rung ?? 0, FloorUp = (tv.Top?.FloorUpM ?? float.NaN) * scale, LipFwd = (tv.Top?.LipFwdM ?? float.NaN) * scale,
            StandFwd = (tv.Top?.StandFwdM ?? float.NaN) * scale,
            LandRung = tv.Landing?.Rung ?? 0, LandUp = (tv.Landing?.FloorUpM ?? float.NaN) * scale, LandLeft = (tv.Landing?.StandLeftM ?? float.NaN) * scale,
            LandFwd = (tv.Landing?.StandFwdM ?? float.NaN) * scale,
            HangRung = tv.LadderHang?.Rung ?? 0, EdgeLeft = (tv.LadderHang?.EdgeLeftM ?? float.NaN) * scale, EdgeFwd = (tv.LadderHang?.EdgeFwdM ?? float.NaN) * scale,
            EdgeUp = (tv.LadderHang?.EdgeUpM ?? float.NaN) * scale, HandsLeft = [.. (tv.LadderHang?.HandsLeftM ?? []).Select(x => x * scale)],
            Hands = [.. tv.Hands.Select(h => new LadderHand(h.Hand == "l", h.ContactS, h.ReleaseS, h.AtForward * scale, h.AtUp * scale, h.Left * scale))],
        };
    }
}

/// <summary>
/// <b>playing the ladder's clips</b> (<see cref="AnimationSystem"/> calls it for a person with a <see cref="PersonLadder"/>, before the ledge): the clip the
/// motor carries the capsule by, at the motor's time (the drawn body and the capsule go together); faded in when the motor says it does not begin on the frame
/// the last one ended on (a climb from the idle part way through its loop, a getting-on from a walk), none between cycles (each starts and ends on the idle's
/// first frame).
/// </summary>
public static class PeopleLadder
{
    /// <summary>The fade into a ladder clip from a pose that is not its first frame (s).</summary>
    public const float Fade = 0.2f;

    /// <summary>The fade into getting on at the top from standing the other way (the clip begins facing away from the drop; s).</summary>
    public const float TurnFade = 0.4f;

    /// <summary>True when it chose the clip (its time set, its rate nought); false to let the others choose.</summary>
    public static bool Choose(CharacterAssets assets, PersonMotion person, ref CharacterAnimation a, ref PersonAir air, ref PersonLadder ladder, in MotionInput input)
    {
        var paths = person.Ladder?.Paths;
        if (paths is null) return false;
        ref var l = ref ladder.Move;
        if (l.Clip > 0 && paths.At(l.Clip - 1) is { } c
            && input.Traversal is TraversalKind.LadderOn or TraversalKind.Ladder or TraversalKind.LadderOff or TraversalKind.PullUp)
        {
            var role = MotionSlots.Of(input.Traversal);
            if (a.Clip != c.Clip)
            {
                float fade = l.Fade switch { 0 => 0f, 2 => TurnFade, _ => Fade };
                PeopleAir.Begin(ref a, ref air, c.Clip, role, l.Time, fade);
            }
            a.Role = role;
            a.Rate = 0f;
            a.Time = l.Time;
            air.Mode = AirMode.None;   // The earlier build's air has nothing to do on a ladder
            ladder.Played = l.Clip;
            return true;
        }
        if (ladder.Played > 0) ladder.Played = 0;
        return false;
    }
}
