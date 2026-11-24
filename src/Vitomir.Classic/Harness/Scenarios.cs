// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.

namespace Vitomir;

/// <summary>
/// **Ours**: named test set-ups (`--scenario <name>`; `--scenario list` prints them). Each is no more
/// than the harness flags it stands for — a level of the game's own, a place to stand, who is stood
/// where (`--spawn`, held with `!`) and what is in hand — so that a thing which is hard to arrange
/// in a real play-through can be tried in a minute, by a player at the keyboard or by a headless run.
/// Nothing here is game content: the levels, templates and items are the installed game's, by name.
/// </summary>
static class Scenarios
{
    /// <param name="Name">What `--scenario` is given.</param>
    /// <param name="What">What it sets up, in a line.</param>
    /// <param name="Try">What to do in it, and what should happen.</param>
    /// <param name="Args">The flags it stands for.</param>
    public sealed record Scenario(string Name, string What, string Try, string[] Args);

    // The Society of Leopold's first level, as a fresh arrival: Christof stands at <-330,-2838,-678>
    // in sector 161. The spots were measured: <-510,-2738> is on his side of a wall, in plain
    // sight 206 away; <-430,-2658> is 113 from it in sector 171 — a sector an explosion's search does
    // reach from there (the flood goes through the portals), with the wall between at every height,
    // so 1.1 hurts whoever stands there and Vitomir does not. A spot behind a wall with no way round
    // within the blast's radius proves nothing: the search never gets there in either.
    const string Level = "L1_SOC1";
    const string Near = "-510,-2738,-678", Beyond = "-430,-2658,-678";
    // And <-330,-1938> is 900 straight ahead of him, through the doorway, the line clear at every height.
    const string Far = "-330,-1938,-678";
    // And <-330,-1780> is 1,058 ahead: less his radius (32) and a soldier's (30), a gap of 996.
    const string Edge = "-330,-1780,-678";

    public static readonly Scenario[] All =
    [
        new("blastwall",
            "two held soldiers either side of a wall in the Society's first level, and four grenades",
            "throw a grenade (quick slots) at the soldier you can see: he is hurt, the one behind the wall is not (the repair blastsight); the log says so",
            ["--level", Level, "--player", "--hud", "--no-cast", "--no-scripts", "--buff", "--quick", "fragGrenade,fragGrenade,fragGrenade,fragGrenade",
             "--spawn", $"LeoSoldier@{Near}!;LeoSoldier@{Beyond}!"]),
        new("heldtarget",
            "one held soldier 900 units straight ahead in the Society's first level, and a pistol that never runs dry",
            "shoot him from where you stand: without --fix wakeonhit he stands and takes it (1.1's); with it he is let go and fights, and the log says so",
            ["--level", Level, "--player", "--hud", "--no-cast", "--no-scripts", "--buff", "--give", "pistol", "--free-ammo",
             "--spawn", $"LeoSoldier@{Far}!"]),
        new("gunrange",
            "one held soldier at the very edge of a pistol's reach (the bodies 996 apart of its 1,000), and a pistol that never runs dry",
            "shoot him from where you stand: as 1.1 has it every shot falls short; with --fix gunreach they land (add --fix wakeonhit and he comes for you, which shows it)",
            ["--level", Level, "--player", "--hud", "--no-cast", "--no-scripts", "--buff", "--give", "pistol", "--free-ammo",
             "--spawn", $"LeoSoldier@{Edge}!"]),
    ];

    public static Scenario? Find(string name) =>
        All.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
