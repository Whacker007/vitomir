// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game's own registry: the switch points, the data and the registry of the thirty-five declarations (ours).
// The declarations themselves are in GameFeatures.Fixes.cs, .Qol.cs and .Repairs.cs.
namespace Vitomir.Engine.Features;

/// <summary>
/// **Every feature Vitomir has** (ours; 1.1 has no switches): eleven Fixes, fourteen quality-of-life switches and
/// ten repairs, one declaration each, and the registry that holds them. <see cref="World.Fixes"/>,
/// <see cref="World.Qol"/> and <see cref="World.Repairs"/> read their lists from here; so do the Fixes page, the
/// host's switches (<see cref="Net.HostSwitches"/>) and a save's header. There is no other list of names.
///
/// **Adding one**: a declaration here, a property on <see cref="World.Fixes"/> or <see cref="World.Qol"/> tied to
/// it by name (<see cref="SwitchAttribute"/>) — or, for a repair, a constant on <see cref="World.Repairs"/>.
/// Tests hold them together.
/// </summary>
public static partial class GameFeatures
{
    /// <summary>
    /// The switch points: each a method where the game's behaviour branches, what is decided there with 1.1's
    /// address where it has one, and whether that is the rules or the presentation.
    /// </summary>
    public static class Points
    {
        const bool Rules = true, Presentation = false;

        public static readonly SwitchPoint RateFor = new("Player.RateFor",
            "how fast a locomotion cycle plays (1.1: a flat 30)", Presentation);
        public static readonly SwitchPoint Clear = new("Player.Clear",
            "whether a step's destination is clear to stand on (1.1 keeps only the things its sweep answers)", Rules);
        public static readonly SwitchPoint GlowShown = new("HoverGlow.Shown",
            "the colour the hover glow is drawn in (the reference build shows its outer bytes exchanged)", Presentation);
        public static readonly SwitchPoint Shift = new("ShapeShift.Shift",
            "what a character taking another shape does with the item in its off hand", Rules);
        public static readonly SwitchPoint RolloverKeep = new("LiveRollover.Keep",
            "when a rollover's lines are written again (1.1: when the hover changes)", Presentation);
        /// <summary>
        /// **Presentation, and a test holds that** (<c>SoakedHandlerTests</c>): what is decided here is which script
        /// methods are called, and a script could do anything — but the only methods the other version calls are
        /// the install's two <c>void soaked</c> handlers, and each does nothing but set a shell on the model
        /// (<see cref="Data.ModelShell"/>). A mod's class with such a handler would be run too, whatever it does.
        /// </summary>
        public static readonly SwitchPoint Soaked = new("CodexScenes.Soaked",
            "which script methods the soak message calls (looked up by full signature)", Presentation);
        public static readonly SwitchPoint NetKept = new("NetSynch.Kept",
            "which things the host announces to the players' machines", Rules);
        public static readonly SwitchPoint SayHit = new("CodexScenes.SayHit",
            "what becomes of a weapon script's hit that throws", Rules);
        public static readonly SwitchPoint Damaged = new("Minds.Damaged",
            "what a blow does to a character a script holds still", Rules);
        public static readonly SwitchPoint HeldAgain = new("Actions.HeldAgain",
            "whether a held attack starts when the character is free again after other than a swing", Rules);
        public static readonly SwitchPoint Hitscan = new("Projectiles.Hitscan",
            "how far a shot's ray reaches", Rules);
        public static readonly SwitchPoint SwingAsk = new("SwingRepeat.Ask",
            "which attack a held click asks for at the end of a swing (1.1 tests Shift against the wrong keys)", Rules);
        public static readonly SwitchPoint ReorderParty = new("Location.ReorderParty",
            "whether the party's order can be changed (1.1: the order they joined in, always)", Rules);
        public static readonly SwitchPoint FoundFeet = new("Player.Advance (found its feet)",
            "what a walker made with no ground under it does once it lands on the floor (1.1: walks on along its straight leg)", Rules);
        public static readonly SwitchPoint EffectsRemake = new("LoadedEffects.Remake",
            "what a load does about the things and the shell an effect's script had made (1.1 makes the script again and nothing it made)", Presentation);
        public static readonly SwitchPoint RightClick = new("InventoryHands.RightClick",
            "what a right-click on an inventory icon does", Rules);
        public static readonly SwitchPoint Steer = new("GameServer.Steer",
            "whether the keyboard pushes the steered character (1.1 moves by clicks alone)", Rules);
        public static readonly SwitchPoint CameraInput = new("Program.Run (camera)",
            "which inputs reach the game camera's turn, tilt and zoom (1.1: the keypad, - and =, X and Z)", Presentation);
        public static readonly SwitchPoint FreeCamera = new("FreeCameraGate.Opens",
            "whether Ctrl+P takes the camera free", Presentation);
        public static readonly SwitchPoint Slices = new("CharacterPane.Slices",
            "which frame a soak wedge on the character sheet shows", Presentation);
        public static readonly SwitchPoint Bindings = new("KeyInput.Bindings",
            "which keys and buttons give which command (1.1's are fixed)", Presentation);
        public static readonly SwitchPoint AltTab = new("Program.Run (Alt+Tab)",
            "whether Alt+Tab leaves the game while it holds the keyboard", Presentation);
        public static readonly SwitchPoint KeyboardHold = new("Program.Run (keyboard hold)",
            "whether the window holds the keyboard, system keys and all, while it has the focus", Presentation);
        public static readonly SwitchPoint WhisperShown = new("GameClient.Receive (chat)",
            "the chat line a whisper's sender is shown (1.1 shows none)", Presentation);
        public static readonly SwitchPoint FilmFlags = new("FilmSound.Flags",
            "which volume a film's sound plays at (1.1: full)", Presentation);
        public static readonly SwitchPoint LockAside = new("SwivelHold.SetAside",
            "whether a pane or a menu opening ends the swivel lock or sets it aside to come back", Presentation);
        public static readonly SwitchPoint OpeningFilms = new("ClassicFrontEnd.TitleAndMenus (the opening films)",
            "whether the logo and the trailer play before the title screen", Presentation);
        public static readonly SwitchPoint StoryFilmStart = new("FilmScreen.Start",
            "whether a story film plays or ends at once, as Escape on its first frame ends it", Presentation);
        public static readonly SwitchPoint DropCellOf = new("DropCell.Of",
            "which cell of a grid an item put down goes into", Rules);
        public static readonly SwitchPoint Landing = new("GroundOrders.Landing",
            "where an item dropped past arm's reach lands", Rules);
        public static readonly SwitchPoint SendActorToPos = new("CodexScenes.SendActorToPos",
            "what a script's walk to where the character already stands does", Rules);
        public static readonly SwitchPoint SpecialMoveTry = new("SpecialMove.Try",
            "whether a strike rolls for a throw", Rules);
        public static readonly SwitchPoint Arrival = new("PartyTravel.Arrival",
            "where a party member arrives in a place", Rules);
        public static readonly SwitchPoint AtStart = new("MainFlagsWord.AtStart",
            "what the AI's options word holds when a game starts (1.1 leaves it zero)", Rules);
        public static readonly SwitchPoint Mend = new("LoadedPack.Mend",
            "what a load does with the cells a save gives a pack's items", Rules);
        public static readonly SwitchPoint HoverIcon = new("InventoryPass.Draw",
            "which picture an inventory icon shows under the pointer", Presentation);
        public static readonly SwitchPoint RestoreScripts = new("CodexScenes.RestoreScripts",
            "where the cathedral's swinging weight is when its script starts again after a load", Rules);
        public static readonly SwitchPoint Sees = new("ExplosionDamage.Sees",
            "whether an explosion sees what it would hurt", Rules);
    }

    /// <summary>
    /// The data the declarations name: what each is, and whether 1.1's rules read it. Only two are **read** by a
    /// declaration (<see cref="Steered"/>, <see cref="ItemCell"/>) — a declared read is data whose 1.1 meaning a
    /// feature relies on, not everything its code touches.
    /// </summary>
    public static class Data
    {
        const bool RulesRead = true, NotRead = false;

        /// <summary>
        /// **Not read by the rules, and a test holds that**: a cycle's rate moves the moment of its keyframe tags,
        /// which would be the rules' business if a walk or run cycle carried a tag the rules act on. None does
        /// (<c>LocomotionTagTests</c>, over every motion set of the install).
        /// </summary>
        public static readonly Datum LocomotionRate = new("locomotion.rate", "a locomotion cycle's playback rate", NotRead);
        public static readonly Datum Steered = new("actor.steered", "which character the keyboard pushes, and where", RulesRead);
        public static readonly Datum Position = new("thing.position", "where a thing stands", RulesRead);
        public static readonly Datum PartyOrder = new("party.order", "the order of the party's members", RulesRead);
        public static readonly Datum OffHand = new("actor.offhand", "what a character holds in its off hand", RulesRead);
        public static readonly Datum Worn = new("actor.worn", "what a character wears and carries", RulesRead);
        public static readonly Datum ItemCell = new("item.cell", "the cell an item lies in", RulesRead);
        public static readonly Datum ScriptCalls = new("script.calls", "which of a script's methods the engine calls", RulesRead);
        public static readonly Datum Announced = new("net.announced", "which things the host tells the players' machines of", RulesRead);
        public static readonly Datum Effects = new("actor.effects", "the effects on a character", RulesRead);
        public static readonly Datum ActionOrder = new("actor.action", "what a character is doing (its action) and at whom", RulesRead);
        public static readonly Datum AiHeld = new("actor.aiheld", "whether a script holds a character's AI still", RulesRead);
        public static readonly Datum ShotReach = new("shot.reach", "how far a shot's ray reaches", RulesRead);
        public static readonly Datum AttackChoice = new("attack.choice", "which of a weapon's two attacks a character swings", RulesRead);
        public static readonly Datum BlastSeen = new("blast.seen", "what an explosion sees", RulesRead);
        public static readonly Datum ThrowRoll = new("strike.throwroll", "whether a strike rolls for a throw on a player's machine", RulesRead);
        public static readonly Datum AiOptions = new("ai.options", "the AI's options word", RulesRead);
        /// <summary>
        /// **Not read by the rules**: the shell a script lays over a model (<see cref="World.ModelFx"/>)
        /// is drawn and faded and nothing else — no rule, no script and no save of 1.1's asks whether a model has one.
        /// </summary>
        public static readonly Datum ModelShell = new("model.shell", "the shell a script lays over a model", NotRead);
        /// <summary>
        /// **Not read by the rules, and a test holds that** (<c>EffectLooksTests</c>, over the install's effect
        /// scripts and templates): what an effect's script makes and a save never keeps is a prop with no
        /// collision shape and no script of its own — an emitter, a dome — riding the character. Nothing can touch
        /// it, and <c>Save.LoadedEffects</c> makes again nothing that is not such a prop.
        /// </summary>
        public static readonly Datum EffectLooks = new("effect.looks", "the props an effect's script made that no save keeps", NotRead);
        /// <summary>
        /// **Not read by the rules**: the pose a model frozen by an effect's script is held in
        /// (<c>FreezeAnimations</c>, the model's bit 0x20000; its mixer is not advanced). What holds the
        /// character is another thing — Prison of Ice's second effect is IMMOBILIZE, <c>THING_AF2_RESTRAINED</c>,
        /// which every action's door tests and a save keeps — so the freeze is the picture of it.
        /// <c>Save.LoadedEffects</c> lays the pose in with the mixer's own fade and puts no channel back: what the
        /// model plays, and so every tag it will cross, is what the load gave it. One residue, left as it is:
        /// while it is frozen again its idle is not advanced, as before the save, where
        /// 1.1's load lets it run — so the motion sets' own dice, which only choose between animations, are asked
        /// less often for those seconds.
        /// </summary>
        public static readonly Datum ModelPose = new("model.pose", "the pose a model frozen by an effect's script is held in", NotRead);
        public static readonly Datum GlowTint = new("glow.tint", "the hover glow's drawn colour", NotRead);
        public static readonly Datum RolloverLines = new("rollover.lines", "a rollover's lines", NotRead);
        public static readonly Datum CameraView = new("camera.view", "the camera's view", NotRead);
        public static readonly Datum SoakWedge = new("sheet.soakwedge", "the frame a soak wedge shows", NotRead);
        public static readonly Datum KeyMap = new("input.bindings", "which input gives which command", NotRead);
        public static readonly Datum SystemKeys = new("window.systemkeys", "what the system keys do while the game has the keyboard", NotRead);
        public static readonly Datum ChatShown = new("chat.shown", "the chat lines one machine shows", NotRead);
        public static readonly Datum FilmVolume = new("film.volume", "a film's volume", NotRead);
        public static readonly Datum PointerLock = new("pointer.lock", "whether the swivel lock holds the pointer", NotRead);
        public static readonly Datum OpeningFilms = new("film.opening", "the films before the title screen", NotRead);
        public static readonly Datum StoryFilmShown = new("film.story", "whether a story film is shown", NotRead);
        public static readonly Datum IconPicture = new("icon.picture", "the picture an inventory icon shows", NotRead);
    }

    /// <summary>The game's registry: the Fixes, the quality-of-life switches and the repairs, in the Fixes page's order.</summary>
    public static FeatureRegistry Registry => registry.Value;

    // Made on first use, not with the class: the three lists are in other files of it, and the order statics
    // in different files are set is not ours to rely on.
    static readonly Lazy<FeatureRegistry> registry = new(() => new FeatureRegistry([.. Fixes!, .. Qol!, .. Repairs!]));
}
