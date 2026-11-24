// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What each switch of the Fixes page does ticked and unticked, in a player's words (ours).
namespace Vitomir.Engine.Features;

/// <summary>
/// **The Fixes page's hints, said plainly** (ours): for every switch a player can tick, what happens **on** and what happens
/// **off**, as the hover hint shows them (<c>FixesPage.Hint</c>), so that each option is clear about what
/// it does when set. Kept apart from the declarations, whose one line (<see cref="Feature.What"/>) is the console's and the
/// docs' shorthand. Every switch has its pair (<c>FixesPageTests</c>), so a new one cannot reach the page without them.
/// </summary>
public static class FeatureHints
{
    /// <summary>The on and off of a switch by its id (<c>fixes.gunreach</c>), or null when it has none.</summary>
    public static (string On, string Off)? Of(Feature feature) => All.TryGetValue(feature.Id, out var pair) ? pair : null;

    /// <summary>Every switch's pair, by id.</summary>
    public static readonly IReadOnlyDictionary<string, (string On, string Off)> All = new Dictionary<string, (string, string)>
    {
        ["fixes.bodycollision"] = ("a character you steer with the keys stops short of the edge of the ground, so its shoulder can't pass into a wall or a corner.",
                                   "it walks right to the edge, as a click's walk does in the original."),
        ["fixes.shapetorch"] = ("a character who changes shape puts its torch away until the shape ends, so a wolf can bite.",
                                "as the original: the torch stays in hand and the new shape can't attack."),
        ["fixes.netprops"] = ("in a network game, the props the host makes that players' machines don't (another player's torch flame) are sent to everyone.",
                              "as the original: players don't see them."),
        ["fixes.berserker"] = ("the Berserker weapon's strongest frenzy works.",
                               "as the original: its script fails at that level and nothing happens."),
        ["fixes.wakeonhit"] = ("a character a script is holding still fights back once your party hurts it.",
                               "as the original: it keeps standing there."),
        ["fixes.gunreach"] = ("a gun fired at the very edge of its range hits.",
                              "as the original: shots from the last few steps of the range fall short."),
        ["fixes.heldattack"] = ("holding the attack button on an enemy attacks as soon as the character can, after a cast or a stagger too. Needs Auto Repeat Attack.",
                                "holding it only carries on a swing."),
        ["fixes.movespeed"] = ("walk and run animations play at the speed the character is really moving.",
                               "as the original: they play at a fixed rate."),
        ["fixes.matchedgait"] = ("on top of Move Speed, the steps are timed so the feet don't slide on the ground.",
                                 "Move Speed's rate alone."),
        ["fixes.glowcolours"] = ("what the pointer is over glows yellow for a door or chest and red for an enemy, the colours the original asks for.",
                                 "cyan doors and blue enemies, as the original shows them on today's PCs."),
        ["fixes.liverollover"] = ("the name box under the pointer keeps updating while you hold still (blood rising as you feed).",
                                  "as the original: it updates only when the pointer moves."),
        ["fixes.soakedvoid"] = ("Fortitude and Skin of the Adder flash white when they soak a whole blow.",
                                "as the original: no flash."),
        ["qol.quickequip"] = ("Shift + right-click an item in the inventory to put it on or take it off.",
                              "drag items to equip them, as the original."),
        ["qol.driving"] = ("steer your character with the keyboard as well as by clicking.",
                           "clicking only, as the original."),
        ["qol.camera"] = ("the mouse wheel zooms, the arrow keys turn the camera and a middle-button drag swivels it.",
                          "the original's camera controls only."),
        ["qol.freecamera"] = ("Ctrl+P lets the camera fly free of the character, and back.",
                              "no free camera (the original had it only for developers)."),
        ["qol.soakpie"] = ("the soak wedge on a portrait fills evenly from 0 to 100.",
                           "as the original: empty below 15 and full from 86."),
        ["qol.keys"] = ("the game uses the keys you set on the Keys page.",
                        "the original's fixed keys, whatever the Keys page says."),
        ["qol.alttab"] = ("Alt+Tab switches away from the game even while it has the keyboard.",
                          "Alt+Tab may not get out of the game."),
        ["qol.winkey"] = ("the Windows key does nothing while you play, so it can't throw you to the desktop.",
                          "the Windows key opens the Start menu as usual."),
        ["qol.whisperecho"] = ("a whisper you send also shows in your own chat.",
                               "as the original: you don't see what you whispered."),
        ["qol.filmvolume"] = ("films play at the Sound slider's volume.",
                              "as the original: films play at full volume."),
        ["qol.relock"] = ("the pointer lock (Ctrl + middle button) lets go while a menu or window is open, and locks again once they are all closed.",
                          "opening a menu or window turns the lock off."),
        ["qol.skipopening"] = ("the logo and the trailer are skipped when the game starts.",
                               "they play each time, as the original."),
        ["qol.skipstoryfilms"] = ("the story's movies, a new game's opening one too, are skipped as if you pressed Escape. Single player only.",
                                  "every movie plays."),
        ["qol.dropcells"] = ("an item you put down in the inventory lands in the cells it covers most.",
                             "as the original: it shifts up and left."),
        ["qol.heldshift"] = ("holding Shift with the attack button keeps using the second attack.",
                             "as the original: Shift gives the second attack once, then the first."),
        ["qol.spawnwalls"] = ("creatures that appear during play (a rat out of a barrel) stay on the floor's paths.",
                              "as the original: they can run straight through walls."),
        ["qol.partyorder"] = ("drag a companion's portrait along the bar to change the party's order.",
                              "the party stays in the order its members joined."),
    };
}
