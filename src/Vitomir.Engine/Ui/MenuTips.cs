// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A tooltip for each option on 1.1's own menu screens, which have none (ours).
namespace Vitomir.Engine.Ui;

/// <summary>
/// **What each option on the menu screens does, under the pointer** (ours). 1.1's Options screens, its multiplayer game
/// setup and its character creation have no hints at all; here every option a player can tick, set or
/// pick say clearly what it does. A control listed here, rested on for <see cref="DelayMs"/>, shows its line in a small
/// box by the pointer (<see cref="FrontEnd.Tip"/>, drawn by <c>MenuPass</c>); a check box's tip answers over its words
/// on its row too (<see cref="At"/>). Our own pages (Fixes, Keys) have their own hint boxes and are not
/// here. Each line says what Vitomir does, which is not always what 1.1 did: the Advanced screen's settings are kept and
/// changed nothing in Vitomir, and they say so.
/// </summary>
public static class MenuTips
{
    /// <summary>How long the pointer rests on a control before its tip shows: half a second.</summary>
    public const int DelayMs = 500;

    /// <summary>How far right of a check box its tip still answers, over the words of one of ours beside it.</summary>
    public const float LabelReach = 150f;

    /// <summary>The tip box's widest text, and the room round it.</summary>
    public const int Width = 210, Pad = 4;

    const string Inert = " Kept from the original; Vitomir always draws at full detail, so this changes nothing.";

    /// <summary>Every tip, by the pane's file and the control's name (both as the files spell them, matched without case).</summary>
    public static readonly IReadOnlyDictionary<(string Pane, string Control), string> All =
        new Dictionary<(string, string), string>(new Ci())
        {
            // ---- Options ----------------------------------------------------------------------------------------
            [("Options", "opgameplaybutton")] = "Blood, subtitles, always run and the quick load question.",
            [("Options", "opcontrolsbutton")] = "Camera and attack controls, the quick slots, your companions' use of disciplines, and the keys.",
            [("Options", "opdisplaybutton")] = "Screen size and brightness.",
            [("Options", "opsoundbutton")] = "Music and sound volumes.",
            [("Options", OptionsMenu.FixesButton)] = "Fixes and conveniences of Vitomir's own, each switched on or off. Those that change the rules mark a game as modded.",
            // ---- Gameplay -----------------------------------------------------------------------------------------
            [("OptionsGameplay", "opgpbloodon")] = "On: no blood or gore is shown. Off: blood as in the original.",
            [("OptionsGameplay", "opgpsubtitling")] = "On: every spoken line is also shown as text on screen. Off: only lines with no recorded voice are shown.",
            [("OptionsGameplay", AlwaysRunOption.Box)] = "On: your character runs when steered with the keys, and holding Shift walks. Off: it walks, and Shift runs. Clicks are unchanged.",
            [("OptionsGameplay", QuickLoadConfirm.Box)] = "On: the quick load key asks before loading, so a stray key press can't lose your progress. Off: it loads at once.",
            // ---- Controls -----------------------------------------------------------------------------------------
            [("OptionsControls", "opconmousepitch")] = "On: moving the pointer to the top or bottom edge tilts the camera up or down. Off: the edges don't tilt it.",
            [("OptionsControls", "opconcameraslew")] = "On: the camera swings round behind the character you lead as it moves. Off: it keeps its angle.",
            [("OptionsControls", "opconrepeatattack")] = "On: holding the attack button keeps your character attacking. Off: one attack per click.",
            [("OptionsControls", "opconautoquickslot")] = "On: when a quick slot item is used up, the next one of the same kind is moved in from your pack. Off: the slot stays empty.",
            [("OptionsControls", "opconquickdisc")] = "On: your companions only use the disciplines in their quick slots. Off: they may use any they know.",
            [("OptionsControls", "opconquickitems")] = "On: your companions only use the items in their quick slots, never their packs. Off: their packs too.",
            [("OptionsControls", "opconhealing")] = "On: your companions may use healing disciplines by themselves. Off: only when you order it.",
            [("OptionsControls", "opconbuff")] = "On: your companions may use disciplines that strengthen them by themselves. Off: only when you order it.",
            [("OptionsControls", "opcondamage")] = "On: your companions may use damaging disciplines by themselves. Off: only when you order it.",
            [("OptionsControls", "opconspecial")] = "On: your companions may use their other disciplines by themselves. Off: only when you order it.",
            [("OptionsControls", OptionsMenu.KeysButton)] = "Choose the keys and buttons for each action.",
            // ---- Display ------------------------------------------------------------------------------------------
            [("OptionsDisplay", "opdisdevicelist")] = "The graphics card Vitomir draws with. There is only the one it uses.",
            [("OptionsDisplay", "opdisvideomodelist")] = "The screen size: the display's own modes in full screen, the window's size in a window.",
            [("OptionsDisplay", "opdisdetailless")] = "The detail level." + Inert,
            [("OptionsDisplay", "opdisdetailmore")] = "The detail level." + Inert,
            [("OptionsDisplay", "opdisgammascroll")] = "Brightness. Changes as you drag; Cancel puts it back.",
            [("OptionsDisplay", "opdisadvancedoptionsbutton")] = "The original's detailed graphics settings." + Inert,
            // ---- Advanced -----------------------------------------------------------------------------------------
            [("optionsDisplayAdv", "opdadetailless")] = "The detail level." + Inert,
            [("optionsDisplayAdv", "opdadetailmore")] = "The detail level." + Inert,
            [("optionsDisplayAdv", "opdaslvertex")] = "Vertex lighting." + Inert,
            [("optionsDisplayAdv", "opdasllightmap")] = "Lightmaps." + Inert,
            [("optionsDisplayAdv", "opdastyledlights")] = "Flickering and pulsing lights." + Inert,
            [("optionsDisplayAdv", "opdalightblobs")] = "Pools of light under lamps and torches." + Inert,
            [("optionsDisplayAdv", "opdabit16")] = "16-bit textures." + Inert,
            [("optionsDisplayAdv", "opdabit32")] = "32-bit textures." + Inert,
            [("optionsDisplayAdv", "opdabilinear")] = "Bilinear texture filtering." + Inert,
            [("optionsDisplayAdv", "opdatrilinear")] = "Trilinear texture filtering." + Inert,
            [("optionsDisplayAdv", "opdareflections")] = "Reflections in mirrors and water." + Inert,
            [("optionsDisplayAdv", "opdahwtrans")] = "The sky." + Inert,
            [("optionsDisplayAdv", "opda3dheads")] = "The 3D heads on the party bar." + Inert,
            [("optionsDisplayAdv", "opdamodellodscroll")] = "How detailed far-off characters are." + Inert,
            [("optionsDisplayAdv", "opdaheadlodscroll")] = "How detailed the party bar's heads are." + Inert,
            [("optionsDisplayAdv", "opdaparticledetailscroll")] = "How many sparks, smoke and blood particles are drawn." + Inert,
            [("optionsDisplayAdv", "opdadecalsscroll")] = "How many marks (blood, scorch) stay on the ground." + Inert,
            [("optionsDisplayAdv", "opdanummipsscroll")] = "How many texture sizes are kept for distance." + Inert,
            [("optionsDisplayAdv", "opdamipselectionscroll")] = "The largest texture size." + Inert,
            [("optionsDisplayAdv", "opdanumdlscroll")] = "How many lights shine on one character." + Inert,
            [("optionsDisplayAdv", "opdanumshadowsscroll")] = "How many characters cast shadows." + Inert,
            [("optionsDisplayAdv", "opdashaddetscroll")] = "How detailed the shadows are." + Inert,
            // ---- Sound --------------------------------------------------------------------------------------------
            [("OptionsSound", "opso3dproviderlist")] = "The sound system. Vitomir has one, so there is nothing to choose.",
            [("OptionsSound", "opsomusicscroll")] = "Music volume. Changes as you drag.",
            [("OptionsSound", "opsosoundscroll")] = "Sound effects and voice volume, and the films' too with Film Volume on (Fixes page). Changes as you drag.",
            [("OptionsSound", "opso2speakers")] = "Two speakers. Kept from the original; Vitomir always plays in stereo.",
            [("OptionsSound", "opso4speakers")] = "Four speakers. Kept from the original; Vitomir always plays in stereo.",
            [("OptionsSound", "opsoheadphones")] = "Headphones. Kept from the original; Vitomir always plays in stereo.",
            [("OptionsSound", "opsosurround")] = "Surround sound. Kept from the original; Vitomir always plays in stereo.",
            // ---- Multiplayer: Create Game ------------------------------------------------------------------------
            [("mpCreateGame", "mpcgnameedit")] = "The game's name, as other players see it in the list of games.",
            [("mpCreateGame", "mpcgpwprotectcheck")] = "On: players need the password below to join. Off: anyone can join.",
            [("mpCreateGame", "mpcgpw")] = "The password players must type to join.",
            [("mpCreateGame", "mpcgpwconfirm")] = "The password again, to be sure it is typed right.",
            [("mpCreateGame", MostPlayersOption.Edit)] = "How many players the game takes, from 2 to 8.",
            [("mpCreateGame", "mpcgchroniclelist")] = "The chronicle (story) the game plays.",
            [("mpCreateGame", "mpcgcoopcombative")] = "On: co-op, every player on one side. Off: combative, each player on their own side and able to fight the others.",
            [("mpCreateGame", "mpcgtrackhumanity")] = "On: a kill costs the killer Humanity, as in the single player story. Off: Humanity is not touched.",
            [("mpCreateGame", "mpcgstmoderation")] = "On: you are the Storyteller and can run the game live (the Storyteller button). Off: no Storyteller tools.",
            [("mpCreateGame", "mpcgautostoryteller")] = "On: the chronicle's own scripts run conversations and move the story on by themselves. Off: the Storyteller does it.",
            [("mpCreateGame", "mpcgloadsavedbutton")] = "Carry on a network game saved before.",
            // ---- Multiplayer: characters --------------------------------------------------------------------------
            [("mpPlayerCreate", "mppcname")] = "Your character's name.",
            [("mpPlayerCreate", "mppckindredcheck")] = "A vampire: Kindred of a clan, with its disciplines.",
            [("mpPlayerCreate", "mppcghoulcheck")] = "A ghoul: a mortal kept strong by a vampire's blood.",
            [("mpPlayerCreate", "mppchumancheck")] = "A mortal human.",
            [("mpPlayerCreate", "mppcmodellist")] = "How your character looks.",
            [("mpPlayerCreate", "mppcclanlist")] = "Your character's clan, which sets its disciplines.",
            [("mpPlayerSelect", "mppsdeleteplayerbutton")] = "Delete the chosen character for good.",
        };

    /// <summary>A control's tip on a pane, or null.</summary>
    public static string? Of(string pane, string control) => All.TryGetValue((pane, control), out var tip) ? tip : null;

    /// <summary>
    /// The tipped control under a point on a pane, and its tip: a control the point is on; else the check box whose row it is
    /// on — 1.1 paints a box's words to its left, so the nearest box to the point's right within <see cref="LabelLeft"/>, or
    /// failing one the nearest to its left within <see cref="LabelReach"/> (our own boxes' words are to their right). Hidden
    /// controls have none.
    /// </summary>
    public static (string Control, string Tip)? At(MenuPane pane, float x, float y)
    {
        (string, string)? left = null, right = null;
        float leftGap = float.MaxValue, rightGap = float.MaxValue;
        foreach (var e in pane.Nui.Elements)
        {
            if (e.Pos.Length < 2 || e.Size.Length < 2 || pane.Hidden.Contains(e.Name) || Of(pane.File, e.Name) is not { } tip) continue;
            var (l, bottom) = pane.At(e);
            float r = l + e.Size[0], top = bottom + e.Size[1];
            if (y < bottom || y > top) continue;
            if (x >= l && x <= r) return (e.Name, tip);
            if (e.Type != "checkbox") continue;
            if (x < l && l - x <= LabelLeft && l - x < leftGap) { leftGap = l - x; left = (e.Name, tip); }
            if (x > r && x - r <= LabelReach && x - r < rightGap) { rightGap = x - r; right = (e.Name, tip); }
        }
        return left ?? right;
    }

    /// <summary>How far left of a check box its tip still answers, over the words 1.1 paints before it.</summary>
    public const float LabelLeft = 250f;

    sealed class Ci : IEqualityComparer<(string Pane, string Control)>
    {
        public bool Equals((string Pane, string Control) a, (string Pane, string Control) b) =>
            string.Equals(a.Pane, b.Pane, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Control, b.Control, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Pane, string Control) k) =>
            HashCode.Combine(k.Pane.ToLowerInvariant(), k.Control.ToLowerInvariant());
    }
}
