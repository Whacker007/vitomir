// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Navigation;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Host;

/// <summary>
/// <c>--snagcheck &lt;file&gt;</c>: drives bodies with the real motor along routes between seeded pairs of
/// reachable places on the level and writes where they snag, one JSON line a finding and a summary
/// (<see cref="SnagCheck"/>), headless. For checks of the new levels.
/// The body is the player's movement profile as play gives
/// it: the game's modules loaded with the command line's switches, their profiles registered in a world of the
/// level, the one <c>--profile</c> names (else the human), in the masquerade as the player starts — or its own
/// numbers with <c>--unbound</c>. When the modules refuse to start (fail closed: no <c>--sandbox</c>) and no
/// profile was asked for, the body is the platform's sandbox numbers, and a note says so.
/// </summary>
internal static class SnagCheckCommand
{
    public static int Run(Cli cli, PlatformSettings settings, HostLog log, string output)
    {
        var options = Program.Options(cli, settings, log);
        var level = LevelSpace.Open(options.Level);
        var gait = (cli.Arg("--gait") ?? "run").ToLowerInvariant() switch { "walk" => Gait.Walk, "sprint" => Gait.Sprint, _ => Gait.Run };
        bool unbound = cli.Flag("--unbound");
        var (profile, numbers, mode, why, halfLife, landingSlide) = Body(options, level, unbound, log, cli.Flag("--snag-spring"));
        var report = SnagCheck.Run(level, new SnagCheckOptions
        {
            Pairs = cli.Int("--pairs", 50), Seed = (ulong)cli.Int("--seed", 1), Gait = gait,
            Numbers = numbers, Profile = profile, Mode = mode, Notes = why is null ? [] : [why],
            HalfLife = halfLife,   // the spring, as play gives the player (0 with accel-ramp)
            LandingSlide = landingSlide,   // its landings as play gives them (the earlier build's with landing-slide)
        });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllLines(output, report.Lines);
        string agent = report.Agent is { } a ? $"; its navigation baked for {a.Radius:0.###} m round, {a.Height:0.###} m tall, climbing {a.Climb:0.###} m, slopes to {a.MaxSlope:0.#}°" : "; its navigation says no agent";
        log.Say($"snagcheck: {level.Name}, {report.Pairs.Count} routes at a {SnagCheck.PaceWord(gait)}, the {profile} body{(mode == "masquerade" ? " in the masquerade" : "")}{(halfLife > 0f ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $" starting and stopping on the spring ({halfLife:0.###} s)") : " on the fixed ramp")}, {report.Arrived} arrived, {report.Findings.Count} findings{agent}; {Path.GetFullPath(output)}");
        foreach (var note in report.Notes) log.Say($"snagcheck: note: {note}");
        return report.Findings.Count == 0 ? 0 : 3;
    }

    /// <summary>
    /// The body to drive: the profile's name, its numbers, and a note when they are not a profile's. The modules are
    /// loaded as <see cref="Session.Create"/> loads them; a refusal stops the check when <c>--profile</c> was given.
    /// </summary>
    static (string Profile, Locomotion Numbers, string Mode, string? Note, float HalfLife, bool LandingSlide) Body(SessionOptions options, LevelSpace level, bool unbound, HostLog log, bool snagSpring)
    {
        using var install = GameInstall.TryOpen(options.Game);
        LoadedGame modules;
        try { modules = ModulesCommand.Load(options, install); }
        catch (Exception e) when (e is ModuleRefusedException or ModuleCheckException && options.BodyProfile is null)
        {
            string why = e is ModuleRefusedException r ? $"the rules module {r.ModuleId} refused to start (fail closed; --sandbox starts its research profile)" : "the game's modules were refused";
            log.Say($"snagcheck: {why}; the body is the platform's sandbox numbers");
            return ("sandbox", Locomotion.Sandbox, "unbound", $"{why}, so the body is the platform's sandbox numbers, not a movement profile", 0f, false);
        }
        var b = new WorldBuilder(options.Seed);
        MovementServices.Register(b, level);
        MasqueradeServices.Register(b, new MasqueradeSettings { AutoSwitch = options.AutoMasquerade });
        modules.Register(b);
        var world = b.Build();
        var profiles = world.Service<MovementProfiles>();
        if (profiles.All.Count == 0)
        {
            if (options.BodyProfile is { } none) throw new SessionException($"There is no movement profile '{none}' in game {modules.Game.Id} (it gives none).");
            return ("sandbox", Locomotion.Sandbox, "unbound", $"no module of game {modules.Game.Id} gives movement profiles, so the body is the platform's sandbox numbers", 0f, false);
        }
        if (SnagCheck.BodyOf(profiles, options.BodyProfile, unbound) is not { } body)
            throw new SessionException($"There is no movement profile '{options.BodyProfile}' in game {modules.Game.Id} (its profiles: {profiles.Names}).");
        // The ramp, the earlier build's, by default (the levels' acceptance test of the geometry; townsfolk walk so);
        // --snag-spring drives the player's spring as play gives it (--start-stop-half-life its half-life for the run; 0 the ramp).
        float halfLife = snagSpring && world.TryService<StartStopSprings>(out _) ? options.StartStopHalfLife ?? body.HalfLife : 0f;
        bool landingSlide = world.TryService<StartStopSprings>(out var springs) && springs.LandingSlide;
        return (body.Profile, body.Numbers, body.Mode, null, halfLife, landingSlide);
    }
}
