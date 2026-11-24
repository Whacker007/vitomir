// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The quality-of-life switches' declarations (ours): things 1.1 never had.
namespace Vitomir.Engine.Features;

public static partial class GameFeatures
{
    /// <summary>
    /// The quality-of-life switches, in the page's order (which the settings line's wording and two tests rest on:
    /// <c>off: freecamera, winkey</c>). On by default, but <c>winkey</c> and <c>freecamera</c>.
    /// </summary>
    public static readonly Feature[] Qol =
    [
        // InventoryHands.RightClick: 1.1's right-click on an icon uses the item  and has no
        // gesture that equips it. On, shift + right-click puts an item on or takes it off, and the host replays
        // the gesture (Net.HostPack): what is worn changes, so it is the host's.
        new(FeatureKind.Qol, "quickequip", "shift + right-click an inventory icon to put an item on or take it off")
        {
            Title = "Quick Equip",
            Default = true, Takes = [Points.RightClick], Changes = [Data.Worn],
        },
        // Program.Run's steer block, and GameServer.Steer for a player's machine's push: 1.1 is click-to-move and
        // nothing else (driving asks what a click asks; it takes over the A and D of the game's own key
        // handler). It moves a character, so it is the rules' and the host's.
        new(FeatureKind.Qol, "driving", "steer a character with the keyboard as well as by clicking")
        {
            Title = "Driving",
            Default = true, Takes = [Points.Steer], Changes = [Data.Steered],
        },
        // Program.Run's camera input (the wheel, the arrows, the middle-button drag) with KeyBindings.Pressed and
        // HeldClick: more ways onto the same WorldCamera entry points as 1.1's keypad and
        // zoom keys. No rule reads the camera.
        new(FeatureKind.Qol, "camera", "the wheel zooms, the arrows turn and a middle-button drag swivels the game camera")
        {
            Title = "Camera",
            Default = true, Takes = [Points.CameraInput], Changes = [Data.CameraView], Whose = FeatureWhose.Machine,
        },
        // FreeCameraGate.Opens: 1.1 reads Ctrl+P only behind the options word's bit 0x80 (`-devmode`).
        // On, the switch stands where the developer bit stood; the rest of the test is kept.
        new(FeatureKind.Qol, "freecamera", "Ctrl+P takes the camera free and back (1.1's only with -devmode)")
        {
            Title = "Free Camera",
            Takes = [Points.FreeCamera], Changes = [Data.CameraView], Whose = FeatureWhose.Machine,
        },
        // CharacterPane.Slices: 1.1's frame is clamp(soak, 0, 100) x 0.01 x 7 truncated. On, one frame
        // for every 20 of the number shown. A picture and nothing else.
        new(FeatureKind.Qol, "soakpie", "a soak wedge fills in even steps from 0 and is full at 100, not empty below 15 and full at 86")
        {
            Title = "Soak Pie",
            Default = true, Takes = [Points.Slices], Changes = [Data.SoakWedge], Whose = FeatureWhose.Machine,
        },
        // KeyInput.Bindings: 1.1's keys are fixed (the key state). On, the key page's and the
        // [keys] section's bindings; off, 1.1's own whatever the file says.
        new(FeatureKind.Qol, "keys", "the keys and buttons of the key page and [keys]; off, 1.1's own keys whatever they say")
        {
            Title = "Keys",
            Default = true, Takes = [Points.Bindings], Changes = [Data.KeyMap], Whose = FeatureWhose.Machine,
        },
        // Program.Run's system-keys block (SDL_HINT_ALLOW_ALT_TAB_WHILE_GRABBED): ours, 1.1 has nothing like it.
        new(FeatureKind.Qol, "alttab", "Alt+Tab leaves the game even while it holds the keyboard")
        {
            Title = "Alt+Tab",
            Default = true, Takes = [Points.AltTab], Changes = [Data.SystemKeys], Whose = FeatureWhose.Machine,
        },
        // The same block (SDL_SetWindowKeyboardGrab): the window holds the keyboard while it has the focus.
        new(FeatureKind.Qol, "winkey", "the Windows key does nothing while the game has the keyboard")
        {
            Title = "Windows Key",
            Takes = [Points.KeyboardHold], Changes = [Data.SystemKeys], Whose = FeatureWhose.Machine,
        },
        // GameClient.Receive's chat arm (the host always sends the copy, GameServer.Say): 1.1 sends a whisper to
        // the one whispered to and shows its sender nothing. One residue is left as it is:
        // on a host that whispers, the shown line moves the speaker's lips on the host's copy.
        new(FeatureKind.Qol, "whisperecho", "a whisper you send is shown to you too; 1.1 shows its sender nothing")
        {
            Title = "Whisper Echo",
            Default = true, Takes = [Points.WhisperShown], Changes = [Data.ChatShown], Whose = FeatureWhose.Machine,
        },
        // FilmSound.Flags: 1.1 gives a film's sound to Bink and no slider reaches it (the master
        // volume's setter is never called). On, the Sound slider's level.
        new(FeatureKind.Qol, "filmvolume", "films follow the Sound slider; 1.1 plays them at full volume")
        {
            Title = "Film Volume",
            Default = true, Takes = [Points.FilmFlags], Changes = [Data.FilmVolume], Whose = FeatureWhose.Machine,
        },
        // SwivelHold.SetAside (CentreLook): the swivel lock and its centred look are ours; opening a pane or a menu
        // over the game camera ended the lock. On, it is set aside and comes back on once everything opened is closed
        // (Whacker007, 2026-11-22). The player's own controls: each machine's.
        new(FeatureKind.Qol, "relock", "the swivel lock comes back on when the panes and menus that opened over it are closed")
        {
            Title = "Relock After Menus",
            Default = true, Takes = [Points.LockAside], Changes = [Data.PointerLock], Whose = FeatureWhose.Machine,
        },
        // ClassicFrontEnd.TitleAndMenus: 1.1 plays the logo and the trailer before the title screen every time it starts
        // (Escape or Space skips each). On, they are not played. A film and nothing else: each machine's own.
        new(FeatureKind.Qol, "skipopening", "the logo and the trailer are not played before the title screen")
        {
            Title = "Skip Opening Films",
            Takes = [Points.OpeningFilms], Changes = [Data.OpeningFilms], Whose = FeatureWhose.Machine,
        },
        // FilmScreen.Start (and the new game's film, ClassicFrontEnd): a story film ends at once, its script told
        // `videoended` as when the player presses Escape on its first frame — 1.1's own path for a skipped film, and its
        // `-nocutscenes`. Single player only: in a network game the story's films are as they always were.
        new(FeatureKind.Qol, "skipstoryfilms", "the story's films are skipped (a new game's opening one too), as if Escape were pressed at once; single player only")
        {
            Title = "Skip Story Films",
            Takes = [Points.StoryFilmStart], Changes = [Data.StoryFilmShown], Whose = FeatureWhose.Machine,
        },
        // DropCell.Of: 1.1 rounds the drawn top-left to a cell by truncating (half a cell),
        // so an item hanging off the top or left is pulled in. On, the floor: refused as at the other edges. It
        // decides the cell an item is saved in, and the host works it out for a player's machine: the host's.
        new(FeatureKind.Qol, "dropcells", "an item put down goes into the cells it covers most at every edge; 1.1 pulls one in at the top and left")
        {
            Title = "Drop Cells",
            Default = true, Takes = [Points.DropCellOf], Changes = [Data.ItemCell],
        },
        // SwingRepeat.Ask: 1.1 means a held Shift+click to keep swinging the second attack, but tests VK_LSHIFT and
        // VK_RSHIFT against an array indexed by scan code (the Mute and Calculator keys), so the second attack comes
        // once and the first after it. On, a held Shift goes on asking for the second. A QoL by Whacker007's word
        // (2026-11-20: *"They shouldn't change the gameplay so I don't think modded should be part of those"*): the
        // player could ask for the second attack with each click in 1.1, or hold the middle button.
        new(FeatureKind.Qol, "heldshift", "holding Shift with the button keeps swinging the second attack (1.1 swings it once, then the first)")
        {
            Title = "Held Shift",
            Default = true, Takes = [Points.SwingAsk], Changes = [Data.AttackChoice],
        },
        // Player.Advance: a creature made at run time (a feeder rat out of a barrel) has no ground under it, a script's
        // walk for it is one straight leg with no route, and 1.1 walks the whole leg through walls even once it has
        // landed on the floor. On, it looks for a route by the walk map the moment it finds its feet. A QoL by
        // Whacker007's word (2026-11-20), as heldshift.
        new(FeatureKind.Qol, "spawnwalls", "a creature made during play (a feeder rat out of a barrel) keeps to the walk map once it finds the floor (1.1 runs on straight, through walls)")
        {
            Title = "Spawn Walls",
            Default = true, Takes = [Points.FoundFeet], Changes = [Data.Position],
        },
        // Location.ReorderParty: 1.1's roster keeps the members in the order they joined and nothing changes it. On, a
        // companion's portrait dragged along the bar swaps or goes in between; the leader stays first. A QoL by
        // Whacker007's word (2026-11-20), as heldshift.
        new(FeatureKind.Qol, "partyorder", "a companion's portrait dragged along the bar puts the party in a new order (1.1 keeps the order they joined in)")
        {
            Title = "Party Order",
            Default = true, Takes = [Points.ReorderParty], Changes = [Data.PartyOrder],
        },
    ];
}
