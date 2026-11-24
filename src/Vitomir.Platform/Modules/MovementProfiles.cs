// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Reflection;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Modules;

/// <summary>
/// One kind of body's movement numbers, as a module's data gives them ("the numbers
/// belong to the body, not the map"; ours): its id (<c>enhanced:movement/elder</c>), a line for people, the
/// numbers themselves, and whether they are research (SANDBOX) values rather than the rules package's.
/// </summary>
public sealed record MovementProfile(AssetId Id, string Title, Locomotion Numbers, bool Sandbox)
{
    /// <summary>
    /// The half-life of the body's starts and stops on the ground (s; the spring, <see cref="BodyMotor.Spring"/>,
    /// in a world on it, <see cref="StartStopSprings"/>); 0, the earlier build's fixed ramp. Not a <see cref="Locomotion"/> number: those are
    /// digested in every world, and a world on the ramp must digest as before. The enhanced module's files give it as
    /// <c>StartStopHalfLife</c>.
    /// </summary>
    public float HalfLife { get; init; }

    /// <summary>The profile's short name, the last part of its id's path (<c>elder</c>), as <c>--profile</c> takes it.</summary>
    public string Name => Id.Path[(Id.Path.LastIndexOf('/') + 1)..];
}

/// <summary>
/// <see cref="Locomotion"/>'s numbers by their names (ours), so a profile file names each field as the struct
/// (and the rules package, where it has the name) does, and a module reads them without knowing the struct's
/// layout: every public <c>float</c> field, in the order the struct declares them. A field added to the struct later
/// is then wanted from every profile file at once (a file without it is refused by name), never silently zero.
/// Reflection runs once, when the type is first used; reading and writing by name is for configuration only, never
/// for a tick.
/// </summary>
public static class LocomotionFields
{
    static readonly FieldInfo[] Fields =
        [.. typeof(Locomotion).GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(float)).OrderBy(f => f.MetadataToken)];

    /// <summary>The names, in the struct's order.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Fields.Select(f => f.Name)];

    /// <summary>Whether <paramref name="name"/> is one of the numbers (case matters).</summary>
    public static bool Has(string name) => Array.FindIndex(Fields, f => f.Name == name) >= 0;

    /// <summary>The number <paramref name="name"/> of <paramref name="numbers"/>.</summary>
    public static float Get(in Locomotion numbers, string name) =>
        (float)(Find(name).GetValue(numbers) ?? 0f);

    /// <summary><paramref name="numbers"/> with <paramref name="name"/> set to <paramref name="value"/>.</summary>
    public static Locomotion With(in Locomotion numbers, string name, float value)
    {
        object boxed = numbers;
        Find(name).SetValue(boxed, value);
        return (Locomotion)boxed;
    }

    static FieldInfo Find(string name) =>
        Array.Find(Fields, f => f.Name == name) ?? throw new ArgumentException($"Locomotion has no number '{name}'");

    /// <summary>A number as a profile's report writes it: <c>∞</c> for an unlimited one.</summary>
    public static string Write(float v) => float.IsPositiveInfinity(v) ? "unlimited" : v.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// The movement profiles of the game (ours): the modules add theirs as
/// they register (in load order), one of them is <b>the human</b> the masquerade holds a body to
/// (<see cref="Human"/>), and the program may name the player's (<see cref="PlayerProfile"/>, its
/// <c>--profile</c>). A body is given its own profile with <see cref="Give"/>: its <see cref="Locomotion"/> and its
/// <see cref="MasqueradeState"/>. For each profile the numbers held to a human's are worked out when profiles or the
/// human change (<see cref="Numbers"/>), never during a tick. A service of every world that registers the
/// masquerade (<see cref="MasqueradeServices.Register"/>); configuration, not state: a body's numbers in force are
/// its <see cref="Locomotion"/>, which the world digests.
/// </summary>
public sealed class MovementProfiles
{
    readonly List<MovementProfile> profiles = [];
    Locomotion[] held = [];
    float[] halfOwn = [], halfHeld = [];   // the spring's half-lives, own and held to the human's
    int human = -1;

    /// <summary>Every profile, in the order added.</summary>
    public IReadOnlyList<MovementProfile> All => profiles;

    /// <summary>The profile the masquerade holds bodies to, or null when no module said one.</summary>
    public MovementProfile? Human => human >= 0 ? profiles[human] : null;

    /// <summary>The index of <see cref="Human"/>, or −1.</summary>
    public int HumanIndex => human;

    /// <summary>The player's own profile by name or id (the program's <c>--profile</c>), or null for the module's choice (the human).</summary>
    public string? PlayerProfile { get; set; }

    /// <summary>Adds a profile (an id added twice is refused); <paramref name="isHuman"/> makes it the masquerade's. Answers its index.</summary>
    public int Add(MovementProfile profile, bool isHuman = false)
    {
        if (profiles.Any(p => p.Id == profile.Id)) throw new InvalidOperationException($"movement profile {profile.Id} is added twice");
        profiles.Add(profile);
        if (isHuman) human = profiles.Count - 1;
        Rework();
        return profiles.Count - 1;
    }

    /// <summary>The index of the profile named <paramref name="nameOrId"/> (its short name or its whole id), or −1.</summary>
    public int IndexOf(string nameOrId)
    {
        for (int i = 0; i < profiles.Count; i++)
            if (profiles[i].Id.ToString() == nameOrId) return i;
        for (int i = 0; i < profiles.Count; i++)
            if (profiles[i].Name == nameOrId) return i;
        return -1;
    }

    /// <summary>The profile named <paramref name="nameOrId"/>, or null.</summary>
    public MovementProfile? Find(string nameOrId) => IndexOf(nameOrId) is var i and >= 0 ? profiles[i] : null;

    /// <summary>The names, for a message: <c>human, neonate, elder</c>.</summary>
    public string Names => profiles.Count == 0 ? "none" : string.Join(", ", profiles.Select(p => p.Name));

    /// <summary>
    /// The numbers in force for a body of profile <paramref name="profile"/> in <paramref name="mode"/>: its own unbound,
    /// held to the human's in the masquerade (<see cref="Masquerade.HeldToHuman"/>; its own when there is no human).
    /// No allocation: worked out beforehand.
    /// </summary>
    public Locomotion Numbers(int profile, MasqueradeMode mode) =>
        mode == MasqueradeMode.Masquerade ? held[profile] : profiles[profile].Numbers;

    /// <summary>
    /// The half-life of the starts and stops of a body of profile <paramref name="profile"/> in <paramref name="mode"/> (s; the
    /// spring): its own unbound; in the masquerade held to the human's, the slower of the two (<see cref="Masquerade.HeldHalfLife"/>).
    /// No allocation: worked out beforehand.
    /// </summary>
    public float HalfLife(int profile, MasqueradeMode mode) =>
        mode == MasqueradeMode.Masquerade ? halfHeld[profile] : halfOwn[profile];

    /// <summary>
    /// Gives body <paramref name="e"/> profile <paramref name="profile"/> (an index of <see cref="All"/>): its
    /// <see cref="MasqueradeState"/>, starting in the masquerade — or unbound when the world switches by place
    /// (<see cref="MasqueradeSettings.AutoSwitch"/>) and the body is controlled and stands in a free place — and its
    /// <see cref="Locomotion"/>, the numbers in force for that mode.
    /// </summary>
    public void Give(World world, EntityId e, int profile)
    {
        if (profile < 0 || profile >= profiles.Count) throw new ArgumentOutOfRangeException(nameof(profile));
        var place = world.TryService<LevelSpace>(out var level) && world.Has<Body>(e) ? level.Places.At(world.Get<Body>(e).Feet) : PlaceKind.Public;
        bool auto = world.TryService<MasqueradeSettings>(out var settings) && settings.AutoSwitch && world.Has<Controlled>(e);
        var mode = auto && place == PlaceKind.Free ? MasqueradeMode.Unbound : MasqueradeMode.Masquerade;
        world.Set(e, new MasqueradeState { Mode = mode, Profile = profile, Place = place, PendingPlace = place });
        world.Set(e, Numbers(profile, mode));
    }

    void Rework()
    {
        // Each profile's worked-out numbers first — its powered jump and reach its own, its tap a human's.
        var humanNumbers = human >= 0 ? profiles[human].Numbers : default;
        for (int i = 0; i < profiles.Count; i++)
            profiles[i] = profiles[i] with { Numbers = human >= 0 ? Masquerade.WithHumanTap(profiles[i].Numbers, humanNumbers) : profiles[i].Numbers.WithOwnPowers() };
        held = new Locomotion[profiles.Count];
        for (int i = 0; i < profiles.Count; i++)
            held[i] = human >= 0 ? Masquerade.HeldToHuman(profiles[i].Numbers, profiles[human].Numbers) : profiles[i].Numbers;
        // The spring's half-lives, held to the human's in the masquerade.
        halfOwn = new float[profiles.Count];
        halfHeld = new float[profiles.Count];
        for (int i = 0; i < profiles.Count; i++)
        {
            halfOwn[i] = profiles[i].HalfLife;
            halfHeld[i] = human >= 0 ? Masquerade.HeldHalfLife(profiles[i].HalfLife, profiles[human].HalfLife) : profiles[i].HalfLife;
        }
    }
}
