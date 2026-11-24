// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game run's interface: its passes built, and the panes, the advancement screen and the shop opened and shut.
using System.Globalization;
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Audio;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Scene;
using Vitomir.Classic;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Engine.Video;
using Vitomir.Formats;
using Vitomir.Platform.Audio;
using Vitomir.Scripting;
using Vitomir;
using Vitomir.Platform.Modules;

namespace Vitomir.Classic;

unsafe sealed partial class ClassicGame
{
    void StartInterface()
    {

        windowed = OpenWindow($"Vitomir — {levelName}");
        // The speakers, when there is a window to go with them and nobody asked for quiet: the platform's audio output,
        // SDL's device behind it, or none (a muted or headless run: only a tape's frames are mixed).
        tape = Arg("--record-sound") is { } tapePath ? new SoundTape(tapePath) : null;
        speakers = headless is null && !Flag("--mute") ? host.Speakers(Mixer.Output) : new AudioOutput();
        speakers.Tap = tape is null ? null : tape.Take;
        // GPU objects are released explicitly, before the window and SDL itself (see Shutdown).
        if (Flag("--no-passes")) foreach (var m in materials.Items) m.DrawPasses = false;
        gpu = OpenGpu();
        // The old renderer's spare light styles (three draws on the lights stream), drawn here, where it made them.
        _ = Vitomir.Engine.World.LightStyles.DrawTheRenderersSpare();
        frame = new PlaceFrame(gpu, location);
        // A loaded game's clock is where the save left it (1.1's tag 9): every deadline a character
        // carries — an effect's end, a discipline's recast, a corpse's fade — is a time on it.
        if (loaded is not null) frame.Sim.Resume(loaded.Header.ClockMs / 1000f);
        // The diagnostic overlay. It costs nothing until it is asked for, and F2 turns it on and off
        // in play, so a run started without it can still answer "why has everything stopped".
        overlay = new Overlay();
        overlayOut = gpu.Overlay();
        watch = new WatchLayer(overlay, overlayOut, location) { Enabled = Flag("--watch") };
        // The game's own status bar, from the player's `ui/statusBarSP.nui`. It starts on in every
        // windowed run with a player (`HudStart`), `--no-hud` starts it off, a headless still
        // has it only with `--hud` (or a game the menus start), and F4 toggles it. It goes down
        // before the diagnostic overlay so a label is never hidden by it.
        // The passes emit into the canvas's draw list from the engine; the 2D backend draws it, and the party heads'
        // model insert through the scene inserts.
        ui = new UiCanvas(new UiAssets(files));
        uiOut = gpu.Interface();                                          // the 2D backend, then its model inserts
        hud = new StatusBarPass(ui, location, net is null ? StatusBarPass.PaneName : "statusBarMP")
        {
            Enabled = HudStart.On(Flag("--hud"), Flag("--no-hud"), playing, headless is not null,
                                  (location.SteeredWalker ?? location.Player) is not null)
        };
        hud.Bar.ShowLog(session.Messages, ui.Assets);                          // laid out in its own font (the bar's, not the pass's)
        // The conversation panes, from the player's own `ui/conversationPane.nui` and `ui/subtitle.nui`.
        // `--talk <name>` starts one of the 310 shipped conversations so it can be watched: space skips
        // a line and a click on a reply picks it, which is what the original's two bindings do.
        // The debug view's items can be turned off: there is a way,
        // and the run has just been told which — the toggles are worth a line of console because
        // nothing on screen says them, and a play-test wants clean frames for screenshots.
        if (watch.Enabled || hud.Enabled)
            Console.WriteLine("keys: Ctrl+F2 diagnostics overlay, Ctrl+F3 its size, F11 status bar, C/Ctrl+F5 character sheet, I inventory, the console's vault (--debug), F6-F9/Tab steer, Q quests"
                            + (watch.Enabled ? "  (the labels and the corner report are --watch; F2 clears them)" : ""));

        // The conversation panes, from the player's own `ui/conversationPane.nui` and `ui/subtitle.nui`,
        // and the game's one running conversation (`Rpg.Conversations`). A script
        // starts one with `ExecuteConversation`; `--talk <name>` starts one by hand, further down, once
        // the string table is up.
        talkPass = new ConversationPass(ui) { Pane = ConversationPass.Load(ui.Assets) };
        // When a `PlayVoice` subtitle comes down again (the subtitle's timer); 0 for none up.
        standaloneUntil = 0;
        // The words live in `strings/*.nvo`; an `.nco` only names them.
        words = new NvoStrings();
        foreach (var f in NvoStrings.Files) if (files.TryRead(f, out var d)) words.Add(d);
        talk = new Conversations
        {
            // The recording is looked up as a stream — `Sounds\Dialog\<id>.mp3`, then the voice
            // `.wav` in Local_Eng.nob — and its length is what the line waits for. 43% of
            // lines have none and take their reading time. `LengthMs: 0` is "no recording", not "missing".
            Voices = id => audio.Bank.Load(id, stream: true) is { } recording
                ? new ConversationVoice(words[id] ?? "", recording.LengthMs, recording)
                : new ConversationVoice(words[id] ?? "", 0),
        };
        // Every conversation, whoever starts it, is hooked onto the one pane here.
        talk.Started += c =>
        {
            audio.Prefetch?.Conversation(c, c.Talk);                       // its voices read ahead (ours, SoundPrefetch.cs)
            var pane = talkPass.Pane!;
            pane.Reset();
            // Say it to the console as well as to the pane. A conversation is over in seconds and then
            // closes itself, so without this a run that worked perfectly looks like one where nothing
            // happened — which is exactly what it looked like the first two times.
            c.Spoke += line =>
            {
                pane.Say(line);
                Console.WriteLine($"  talk: {line}");
                Journal.Current?.Write(frame.Sim.Milliseconds, JournalKind.Story, ("what", "line"),
                    ("conversation", c.Talk.Name), ("event", c.EventIndex), ("step", c.StepIndex), ("said", line));
            };
            c.Chosen += (_, ev, answer, _, _) =>
                Journal.Current?.Write(frame.Sim.Milliseconds, JournalKind.Story, ("what", "reply"),
                    ("conversation", c.Talk.Name), ("event", ev), ("answer", answer));
            // Ending one closes the panes, as `vuiConversation::Close` does:
            // the transcript is the conversation's, not the world's, and does not outlive it.
            c.Closed += () =>
            {
                pane.Reset();
                Console.WriteLine($"  talk: {c.Talk.Name} ended, code {c.ReturnCode}");
                Journal.Current?.Write(frame.Sim.Milliseconds, JournalKind.Story, ("what", "conversation ended"),
                    ("conversation", c.Talk.Name), ("code", c.ReturnCode));
            };
            Console.WriteLine($"  talk: {c.Talk.Name} begins, {c.Actors.Count} of its speakers here"
                            + (c.Missing.Count > 0 ? $" ({string.Join(", ", c.Missing)} are not)" : ""));
            Journal.Current?.Write(frame.Sim.Milliseconds, JournalKind.Story, ("what", "conversation"),
                ("conversation", c.Talk.Name), ("speakers", c.Actors.Count),
                ("missing", c.Missing.Count > 0 ? string.Join(',', c.Missing) : null));
        };
        // The character sheet. `--character` opens it, **F5** toggles it in play, and the status bar's
        // own `sbCharacterButton` does too — which is what a play-test reached for, seven clicks and five
        // dead buttons in. It goes on after the bar and before the conversation,
        // because a conversation is on top of everything in the original.
        // The pane is always built, because the bar's button has to be able to open it; only whether
        // it is *drawn* depends on the flag.
        charPass = new CharacterPass(ui) { Enabled = Flag("--character") };
        charPass.Pane = CharacterPass.Load(ui.Assets);
        DressPanes();
        charPass.Pane.Subject = location.Steered?.Actor;
        // The string table, so the pane says "Human" and "Acolyte" rather than UI_GENERAL_HUMAN and
        // UI_STATUS_HUMAN0. The engine loads system, game, effects and chronicles at start-up; a
        // chronicle's own table goes on top, and cannot overwrite them.
        strings = new Vitomir.Engine.Ui.Strings(files);
        // The chronicle's **metafile** says which tables it adds, and it may name several.
        strings.AddFrom(owner?.Chronicle.Meta);
        charPass.Pane.Strings = key => strings[key];
        // The names in the subtitle brackets are string-table lookups of the cast id.
        talk.Names = key => strings[key];
        // The message log and the quest log say things in the player's language too.
        session.Messages.Text = strings;
        session.Quests.Text = key => strings[key];
        session.Quests.IconLoads = icon => ui.Assets.Texture(icon) is not null;
        if (loaded is not null) session.Quests.ResolveTexts();

        // **The in-game menus**: the escape menu, the save and load screens and
        // their two dialogs — the front end's own machinery, in the game's state. While one is up it
        // has every key and click, and the game is paused — as the Pause key does.
        menus = NewMenus(ui.Assets, strings, audio, FrontEnd.StateGame);
        menus.Posted += line => session.Messages.Add(line);
        session.Paused = () => menus.Paused || net?.Paused == true;   // RPG_CANTDOACTION while paused
        session.Seated = () => net?.Server?.Storyteller == true;      // the seat's stealth fade, 1.0
        OptionsHost.InGame(menus, v => frame.World.Gamma = v, talk, () => minds);
        OptionsHost.AttachMap(menus, strings.Find, line => session.Messages.Add(line));
        menuPass = new MenuPass(ui, menus);

        // The quest pane (`ui/questPane.nui`): **Q**, the status bar's quest button, or `--quests`. It
        // is a left-hand pane like the character sheet and the vault, and opening any one of the three
        // closes the others (`visGroup=1`).
        questPass = new QuestPass(ui) { Enabled = Flag("--quests") };
        questPass.Pane = QuestPass.Load(ui.Assets);
        questPass.Pane.Log = session.Quests;
        // The shop pane (`ui/buySellPane.nui`), opened by a shop script's `ExecuteBuySell` and by
        // nothing else. It has no `visGroup`, so it displaces nothing; the inventory comes up beside it.
        buyPass = new BuySellPass(ui, BuySellPass.Load(ui.Assets));
        buyPass.Pane!.Letter(ui.Assets);                                       // the rows' height, the font's (the pane's, not the pass's)
        buyPass.Pane.Text = strings;
        buyPass.Pane.Effects = effects.File;
        // Whether the shop's slider is being dragged.
        shopDrag = false;
        // The discipline pane: **D**, or the status bar's discipline button.
        discPass = new DisciplinePass(ui);
        discPass.Pane = DisciplinePass.Load(ui.Assets);
        discPass.Pane.Powers = powers;
        discPass.Pane.Text = strings.Find;
        // The advancement in progress, or null.
        advancement = null;
        // Opening the discipline pane puts the status bar back up; the frame's own bar
        // logic takes the request.
        barAsked = false;
        // The advancement screen's own bar (`ui/statusBarADV.nui`).
        advBarPass = new AdvancementBarPass(ui) { Messages = hud };
        // What the left button went down on, for the release that makes it a click (event 1 fires
        // only over the element that was pressed): a discipline pane control, a quick slot, a raise
        // button. And a discipline's drag to a quick slot (`DisciplineDrag`).
        discPress = (Vitomir.Engine.Ui.DisciplinePane.Part.None, -1);
        barPress = -1; raisePress = -1;
        discDrag = new Vitomir.Engine.Ui.DisciplineDrag();
        readOkPressed = false;              // the read pane's OK, pressed and not yet let go (acted on at the release)
        // The advancement screen has closed, and the journey `SetupAdvance` named is due.
        advanceFinished = false;
        // `--quest <name>` loads one the way a script's `CodexQuest.Load` does, so the pane has something
        // to show without playing to the convent — repeatable, a comma between names.
        // A name ending in `!` is completed as well, the way a script's `Complete()` does it.
        foreach (var raw in (Arg("--quest") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string name = raw.Trim().TrimEnd('!');
            if (session.Quests.Load(name) is not { } given) { Console.WriteLine($"quest: no quest file called {name}"); continue; }
            Console.WriteLine($"quest: {given.Name} in slot {given.Slot} -- \"{given.Short}\"");
            if (raw.Trim().EndsWith('!')) session.Quests.SetFlags(given, Vitomir.Engine.Rpg.QuestLog.Complete);
        }

        // `--talk <name>` starts one of the 310 shipped conversations so it can be watched without
        // finding whoever says it: space skips a line and a click on a reply picks it, which is what
        // the original's own two bindings do.
        restart = null;
        if (Arg("--talk") is { } talkName && !talkName.StartsWith("--"))
        {
            if (!files.TryRead($"conversations/{talkName}.nco", out var nco))
                Console.WriteLine($"talk: no conversation called {talkName}");
            else if ((NcoFile.Read(nco, talkName) is var read
                      ? read.Conversation(talkName) ?? read.Conversations.FirstOrDefault()
                      : null) is not { } script)
                Console.WriteLine($"talk: {talkName}.nco holds no conversation");
            else
            {
                restart = at => talk.Run(location, script, ConversationFlag.Shipped, 0, at);
                int steps = script.Events.Sum(e => e.Steps.Count);
                int replies = script.Events.Sum(e => e.Steps.Count(x => x.Kind is NcoStep.AnswerGoto or NcoStep.AnswerReturn));
                Console.WriteLine($"talk: {talkName}, {script.Events.Count} events, {steps} steps"
                                + (replies > 0 ? $", {replies} replies" : ", no replies -- it will play through and close itself"));
                restart(frame.Sim.Milliseconds);
                // A conversation ends and does not loop -- no shipped one does -- so exploring means
                // starting it again. T is a harness key, not the game's: the original re-enters a
                // conversation by clicking the character.
                Console.WriteLine("  press T to start it again; a conversation ends rather than looping,"
                                + " which is the original's own shape");
                if (location.Player is null)
                    Console.WriteLine("  no player character here, so replies are attributed to \"Player\";"
                                    + " with one they take its CASTID, as the engine does in single player");
            }
        }
        // The clan crests, out of the clan list the engine reads at start-up (`misc/game.ncd`).
        if (files.TryRead("misc/game.ncd", out var ncd))
        {
            var crests = Vitomir.Engine.Ui.CharacterPane.ClanIconsFrom(System.Text.Encoding.Latin1.GetString(ncd));
            charPass.Pane.ClanIcons = clan => crests.GetValueOrDefault(clan);
        }
        Console.WriteLine($"strings: {strings.Count} in {string.Join(", ", strings.Loaded)}"
                        + (strings.Missing.Count > 0
                           ? $" ({string.Join(", ", strings.Missing)} not in the install)" : ""));
        if (charPass.Enabled && charPass.Pane.Subject is null)
            Console.WriteLine("character: no player character here, so the sheet is blank -- add --player");
        // The inventory. `--inventory` opens it, **I** toggles it (the game's key), and `sbInventoryButton` does too.
        // It is a **right**-hand pane (`visGroup=2`, quads from x=320), so it and the character sheet
        // occupy opposite halves and can be up together — which is what the base game does, and what
        // a screenshot of the original shows.
        invPass = new InventoryPass(ui) { Enabled = Flag("--inventory") };
        invPass.Pane = InventoryPass.Load(ui.Assets);
        invPass.Pane.Subject = location.Steered?.Actor;
        // The purse comes from the character file's own `cash` line — 50 for Christof — and `--gold`
        // overrides it, which is the same shape as every other harness knob here.
        // The purse is the party's (`Session.Gold`); the pane shows it, and is kept in step each frame.
        if (Arg("--gold") is null && loaded is null) session.Gold += startingKit?.Cash ?? 0;   // beside what the companions' sheets gave (CharacterSheets)
        invPass.Pane.Gold = session.Gold;
        if (invPass.Enabled && invPass.Pane.Subject is null)
            Console.WriteLine("inventory: no player character here, so the pack is empty -- add --player");
        else if (invPass.Enabled)
            Console.WriteLine($"inventory: {invPass.Pane.Placed().Count()} items in "
                            + $"{invPass.Pane.Grids().Count()} grids, {invPass.Pane.Gold} gold");

        // The haven vault. `--vault` opens it, and so does the console's `vault` (F10 no longer); in the game the script native
        // `Vault(guid)` (native 13) opens it and the haven's own `HavenVault` script is what
        // calls that, which is why there is no key for it in the original at all.
        //
        // **It is the same pass over a different file**, because the vault is not a place: an item
        // stored in it keeps hanging off the character's own inventory list with `Location` 0x4000, so
        // it travels wherever they do and only the *pane* is tied to the haven.
        vaultPass = new InventoryPass(ui) { Enabled = Flag("--vault") };
        vaultPass.Pane = InventoryPass.LoadVault(ui.Assets);
        vaultPass.Pane.Subject = location.Steered?.Actor;
        if (vaultPass.Enabled)
            Console.WriteLine($"vault: {vaultPass.Pane.Placed().Count()} items in "
                            + $"{vaultPass.Pane.Grids().Count()} grid for {vaultPass.Pane.SubjectName}");

        // The read dialog, and the documents behind it. A chronicle's `.ntx` sits beside its `.nls`
        // and carries every letter, journal and book in it — `CuthbertLetter` among them, which is
        // what a play-test clicked in the quick-use strip and got "nothing uses that yet" for.
        documents = new Vitomir.Engine.Ui.Documents(files);
        documents.AddFrom(owner?.Chronicle.Meta);
        readPass = new ReadPass(ui, ReadPass.Load(ui.Assets));
        readPass.Pane?.Letter(ui.Assets);                                      // laid out in its own font (the pane's, not the pass's)
        if (readPass.Pane is { } readPane) { readPane.Audio = audio; readPane.Closed = () => Vitomir.Engine.Ui.ReadPaneOpen.Closed(menus); }   // the pause off
        Console.WriteLine($"documents: {documents.Count} in {string.Join(", ", documents.Loaded)}"
                        + (documents.Missing.Count > 0
                           ? $" ({string.Join(", ", documents.Missing)} not in the install)" : ""));

        // Moving items with the mouse. The rules are the engine's; the gesture — one click to lift, one
        // to drop, right-click for the shortcut — is ours and says so in `InventoryHands`.
        hands = (location.Steered ?? location.Player?.Thing) is { } handOwner
            ? new Vitomir.Engine.Ui.InventoryHands(invPass.Pane, handOwner, effects) : null;
        if (vaultPass.Enabled) ShowVault(true);

        // What the engine asks before it decides where a pick-up goes: with the pack on screen it
        // reaches for the cursor instead, which is the base game's own rule and its behaviour
        // of it (`Rpg.GroundOrders.ToCursor`).
        location.InventoryShown = () => invPass.Enabled;

        // The cursor. 1.1 picks one of eight, or draws whatever is in the actor's hand, and
        // draws it after every pane — so this pass goes last.
        cursor = new Vitomir.Engine.Ui.CursorState { Audio = audio };
        // A discipline let go while its icon is on the cursor: the cursor itself again.
        Vitomir.Engine.Rpg.DisciplineBook.LetGo += (_, d) => { if (discDrag.LetGo(d)) cursor.Dragging = null; };
        // The name of whatever the pointer is on, in the game's own small description box.
        // Follows the bar from the first frame; F4 keeps the two in step.
        rolloverPass = new RolloverPass(ui)
        { Text = strings, Effects = effects.File, Enabled = !Flag("--no-rollover") };
        cursorPass = new CursorPass(ui, cursor) { Enabled = !Flag("--no-cursor") };
        // The game draws its own pointer, so the system one goes away — the interface's draw ends every
        // frame with the cursor draw and the original shows no arrow beside it. Two pointers on screen
        // is the tell that this step was skipped.
        if (cursorPass.Enabled) host.ShowCursor(false);
        if (hands is not null) hands.Cursor = cursor;
        hovering = null;
        // The floor under the pointer (`collidePos`), which held automatic fire aims at.
        pointerGround = null;
        // The glow the hovered thing takes on (`Engine.Ui.HoverGlow`), fed from the same pick.
        glow = new Vitomir.Engine.Ui.HoverGlow();
        // Whether the pointer is resting on a `BLOCKSELECT` thing, which swallows the click whole: set only
        // by the game camera's pick, so the free camera keeps the last one.
        swallowed = false;
        // `--pointer-log` (ours): whose each pointer event is, said in the log (`SayPointer`).
        pointerLog = Flag("--pointer-log");
        pointerSaid = default;
        // And the quick-use strip finally does something: the item's own use path.
        // Right-click on an icon **uses** it — the original's own gesture, and the same item
        // path the quick-use strip takes.
        if (hands is not null) hands.Use = item => UseItem(item);

        // Right-click a belt cell to use what is in it; left-click to pick it up or put one down,
        // because `sbQuickUse` is an ordinary icon grid with the ordinary grid handler on binding 0.
        // A belt icon's right click is the same handler as the pack's, deaf while a shop is up.
        hud.Bar.UseQuick = item => buyPass.Enabled ? Vitomir.Engine.Ui.InventoryHands.NotWhileShopping
            : Vitomir.Engine.Rpg.QuickSlots.Use(location.Steered, item,
            GameOptions.Current.Has(GameOptions.BackpackAutoLoad), UseItem);           // the slot's use, and its refill
        // A press there, which in a shop may wait to be a drag (ours, `quickequip`; `InventoryHands.PressIn`).
        hud.Bar.TakeQuick = cell => hands is { } belt
            ? belt.PressIn(new Vitomir.Engine.Ui.InventoryGrid(
                  "sbQuickuse", Vitomir.Engine.Rpg.Slot.QuickUse, 5, 1), cell, 0, leftPressAt.X, leftPressAt.Y).Say
            : "nobody to carry it";

        // The pane buttons are checkboxes with a `Dep` bitmap: they stay down while their pane is up.
        hud.Bar.PaneShown = name =>
            string.Equals(name, "sbCharacterButton", StringComparison.OrdinalIgnoreCase) ? charPass.Enabled
            : string.Equals(name, "sbInventoryButton", StringComparison.OrdinalIgnoreCase) ? invPass.Enabled
            : string.Equals(name, "sbQuestButton", StringComparison.OrdinalIgnoreCase) ? questPass.Enabled
            : string.Equals(name, "sbDisciplineButton", StringComparison.OrdinalIgnoreCase) ? discPass.Enabled
            : false;

        hud.Bar.Panes = name =>
            string.Equals(name, "sbCharacterButton", StringComparison.OrdinalIgnoreCase)
                ? $"character sheet {(ShowCharacterToggle() ? "open" : "closed")}"
            : string.Equals(name, "sbInventoryButton", StringComparison.OrdinalIgnoreCase)
                ? $"inventory {(ShowInventoryToggle() ? "open" : "closed")}"
            : string.Equals(name, "sbQuestButton", StringComparison.OrdinalIgnoreCase)
                ? $"quests {(ShowQuestsToggle() ? "open" : "closed")}"
            : string.Equals(name, "sbDisciplineButton", StringComparison.OrdinalIgnoreCase)
                ? $"disciplines {(ShowDisciplinesToggle() ? "open" : "closed")}"
            : null;
        // The map button, ahead of the four panes.
        var panesBeforeMap = hud.Bar.Panes;
        hud.Bar.Panes = name => name.Equals("sbMapButton", StringComparison.OrdinalIgnoreCase)
            ? OptionsHost.OpenMap(menus, session.Chronicle, files, location.Name) ? "map open" : "no map here"
            : panesBeforeMap?.Invoke(name);
        BindInterfaceActions();

        // The quest log's notice: the button is asked to flash — 500 ms on, 500 off, for
        // up to 1,000 seconds — unless the pane is up and on top. With the pane shut it blinks until
        // the pane is opened, which stops it (`UiControl.StartBlink`, and a video of 1.1).
        session.Quests.Changed += () =>
        {
            if (!(questPass.Enabled && !readPass.Enabled))
                hud.Bar["sbQuestButton"]?.StartBlink(500, 1_000_000, frame.Sim.Milliseconds);
        };

        // The party heads live in the middle of the status bar and are drawn *after* every one of its
        // controls (`uiMenu::Draw` runs the pane's control passes and then the pane's own
        // hook, which is what calls `vuiHeads::Draw`). They come before the description boxes for the
        // same reason the engine's do: `descLargeSP` is a later pane over the identical rectangle, so
        // it paints over them rather than replacing them.
        var headsMotions = location.Motions ?? new MotionSets(files);
        // The heads' renderer draws with the spare light styles it used to make itself: drawn here, where it did.
        var headsStyles = Vitomir.Engine.World.LightStyles.DrawTheRenderersSpare();
        headsPass = new HeadsPass(ui, files, materials, headsMotions, headsStyles)
        {
            // The bar being up is what puts the viewport on screen; `--no-heads` only stops the models,
            // because the engine paints the panel before it tests its own `heads` toggle.
            // Set here for the first frame and kept in step with F4 below: the heads are part of the
            // bar, so hiding the bar hides them. They went on drawing over a hidden bar until a play-test
            // pressed F4 and looked.
            Enabled = hud.Enabled,
            // In a network game `statusBarMP`'s one head goes in its own 80 x 88 box (the heads' mode 1).
            ShowHeads = !Flag("--no-heads"),
            Mode = net is null ? Vitomir.Engine.Ui.PartyHeads.SinglePlayer : Vitomir.Engine.Ui.PartyHeads.Narrow,
            // **The bar's own party list, not the leader alone.** `vuiHeads::Draw` walks `rpgParty`'s
            // array — the same one that lays the status bar's rows out — so the head above a row is the
            // character on that row by construction. Handing the pass only `location.Player` drew one
            // head however many companions were following, which is what a two-head comparison against
            // 1.1 caught. `StatusBar.PartyThings` is rewritten in place each frame, so holding it here
            // once is a live view rather than a snapshot.
            Party = hud.Bar.PartyThings,
        };
        uiOut.MakeInsert(ui.Scene(headsPass.Scene));                              // the heads' renderer made here, as it was

        // **The world is not drawn under the status bar.** Measured off two 1.1 screenshots of one spot,
        // one with the bar and one without: the picture sits 108.9 pixels higher with the bar up, at
        // exactly the same scale — which is a shorter viewport, not a smaller picture. See
        // `StatusBar.WorldBottom`. It is read every frame because F4 toggles the bar in play.
        frame.WorldView = () => hud.Enabled ? Vitomir.Engine.Ui.StatusBar.WorldShare : 1f;
        // The overlay's labels are projected with the matrix the frame was drawn with, or they drift
        // off the characters they name — see `WatchPass.WorldRows`.
        watch.WorldRows = h => frame.WorldRows(h);

        // The read dialog goes on top of everything: it is the one shipped pane that says `dialog=1`.
        // `hud.Rows` is the party rows, which go **over** the heads — see `StatusBarPass.Rows` for
        // the evidence and for the one reading of the original it disagrees with.
        // The camera's fades and flashes go over everything the game draws, the pointer included —
        // 1.1 draws the interface and then the effects after it. The diagnostic overlay is
        // ours and goes on top of them, so a fade to black does not black the labels out too.
        effectsPass = gpu.Effects();
        // The floating captions — the chapter title as a place is entered.
        captions = new Vitomir.Engine.Ui.Captions();
        captionPass = new CaptionPass(ui, captions);
        endText = new Vitomir.Engine.Ui.EndTextHold(captions);              // the end text's hold and what takes it down (EndTextHold)
        last = host.Now();                                          // the frame clock's last frame, which a scene start sets too
        filmClock = 0f;                                                   // game time a film has held the screen for
        uiClock = 0;
        headsPass.Clock = () => uiClock > 0 ? (float)uiClock : null;   // the heads run on the interface's clock
        // The icons of what is on the character being steered.
        effectIconsPass = new EffectIconsPass(ui)
        {
            Steered = () => location.Steered,
            BarShown = () => hud.Enabled,
        };
        // The feed bar in the bottom left corner while the steered character feeds.
        feedBar = new Vitomir.Engine.Ui.FeedBar();
        var feedBarPass = new FeedBarPass(ui, feedBar);
        netPass = new NetPass(ui);                                          // multiplayer's chat and the Storyteller
        // In the seat the description boxes go over the Storyteller's bar and panes, which hid them (`NetRollovers`);
        // elsewhere under the network pass, so a player's load screen and pause menu stay on top.
        // The whole interface is one draw list a frame, drawn by the 2D backend, then the screen's effects and the overlay.
        // First of it the black above a bottom-seated sheet while the escape menu is up (MenuPass.TopBand).
        frame.After = new FrameLayers(uiOut.Of(new UiPasses(menuPass.TopBand, hud, headsPass, hud.Rows, advBarPass, effectIconsPass, feedBarPass, charPass, questPass, buyPass, invPass, discPass, vaultPass, talkPass,
                                 captionPass, readPass, new UiWhen(rolloverPass, () => net?.Story?.On != true), netPass,
                                 new UiWhen(rolloverPass, () => net?.Story?.On == true), menuPass, cursorPass)), new ScreenLayer(effectsPass), watch);

        // Where the pointer is and what it is over. 1.1 does this every frame, before
        // the cursor update and before anything is drawn; doing it on the motion event is the same
        // answer for a fraction of the work, because nothing else moves the mouse.
        // Which panes a click has to try, and therefore which points never reach the world. The hand
        // knows: it holds the open panes, and an icon grid answers for its own rectangle whichever pane
        // it happens to be drawn on.
        // ---- the shop ------------------------------------------------------------------------------
        // What the five pane buttons are, which the shop's show hook hides.
        shopHides = ["sbInventoryButton", "sbCharacterButton", "sbMapButton", "sbQuestButton",
                              "sbDisciplineButton", "sbSTButton"];

        // Whether the camera is one that picks (see `Pointer`); set once the game camera exists.
        Picking = () => true;
    }

    void StartPlayer()
    {
        // A weapon in the party's hands. `--party` spawns the cast at *template* level — a fresh
        // Christof with Stamina 20, no soak of any kind and his fists — which against a chapter-three
        // encounter is not a fight. A real playthrough would have levelled stats, armour, a weapon and
        // Fortitude, and all four come out of a save, which is an open question. Until then this is the
        // honest stand-in: one named weapon, said out loud, rather than quietly fudged numbers.
        if (Arg("--arm") is { } armWith && !armWith.StartsWith("--") && location.Minds is { } armed)
        {
            var armoury = new TemplateSet(files);
            var tags = armoury.Resolve(armWith);
            if (Weapon.From(armWith, tags) is not { } pattern)
                Console.WriteLine($"arm: no weapon template called {armWith}");
            else
            {
                int n = 0;
                foreach (var mind in armed.All)
                    if (mind.Thing.Actor is { } a && (a.Has(Actor.Flag.Party) || ReferenceEquals(mind.Thing, location.Player?.Thing)))
                    {
                        a.Weapon = Weapon.From(armWith, tags);
                        n++;
                    }
                var first = pattern.Attacks[0];
                Console.WriteLine($"arm: {n} of the party carrying {armWith} " +
                                  $"({first.Damage} {first.DamageType}, reach {first.MaxDistance})");
            }
        }

        // Something in the quick-use strip, so the five cells along the bottom of the status bar have
        // anything to show. A real playthrough fills them from the backpack, and a Vitomir save carries
        // them; for a run from nothing this is the same honest stand-in `--arm` is: named templates,
        // said out loud.
        if (Arg("--quick") is { } quickWith && !quickWith.StartsWith("--") && location.Player is { } holder)
        {
            var shelf = new TemplateSet(files);
            var put = new List<string>();
            foreach (var name in quickWith.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var tags = shelf.Resolve(name);
                if (Vitomir.Engine.Rpg.Item.From(name, tags) is not { } made)
                {
                    Console.WriteLine($"quick: no item template called {name}");
                    continue;
                }
                // The strip is five 1x1 cells, and the belt takes only a one-cell icon (the belt gives an
                // item the belt's grid 0x200 only then; `Stow.Place`), so this refuses a bigger one — and
                // says why, which is the point of a diagnostic flag.
                if (made.IconCells is not (1, 1))
                {
                    var (wide, tall) = made.IconCells;
                    Console.WriteLine($"quick: {name} is {wide}x{tall} cells and the quick strip is 1x1");
                    continue;
                }
                // The strip lays its items out by their own cells, so each goes in the first free one.
                if (holder.Thing.Actor is not { } belted || !Vitomir.Engine.Rpg.Stow.Place(belted, made, Vitomir.Engine.Rpg.Slot.QuickUse))
                {
                    Console.WriteLine($"quick: no free quick slot for {name}");
                    continue;
                }
                belted.Inventory.Add(made);
                put.Add($"{name}{(made.Icon.Length > 0 ? "" : " (no ICON)")}");
            }
            Console.WriteLine(put.Count > 0
                ? $"quick: {put.Count} in the quick slots — {string.Join(", ", put)}"
                : "quick: nothing went in");
        }

        // Disciplines, so the six slots along the bottom right have anything in them and the AI's
        // discipline ladder has anything to choose. `name` or `name:level`, and the first six go in the
        // quick slots because that is the only place the bar can show them.
        if (Arg("--disciplines") is { } learnWith && !learnWith.StartsWith("--") && location.Player is { } student)
        {
            var learnt = new List<string>();
            int slot = 0;
            foreach (var one in learnWith.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = one.Split(':');
                if (powers[parts[0]] is not { } definition)
                {
                    Console.WriteLine($"disciplines: no discipline called {parts[0]}");
                    continue;
                }
                int rank = parts.Length > 1 && int.TryParse(parts[1], out int l) ? l : 0;
                if (Vitomir.Engine.Rpg.DisciplineBook.SetLevel(student.Thing, powers, definition.Name, rank) is { } taughtOne
                    && slot < Vitomir.Engine.Rpg.DisciplineList.QuickSlots)
                    taughtOne.QuickSlot = slot++;
                learnt.Add($"{definition.Name} {definition.Type} lvl {rank}, {definition.CostAt(rank)} blood");
            }
            if (student.Thing.Actor is { Disciplines.Count: > 0 } taught)
                Vitomir.Engine.Rpg.DisciplineBook.Select(student.Thing, taught.Disciplines.InSlot(0) ?? taught.Disciplines.All[0]);
            Console.WriteLine(learnt.Count > 0
                ? $"disciplines: {string.Join("; ", learnt)}"
                : "disciplines: nothing learnt");
        }

        // The journal: a machine-readable record of the run, for reading afterwards rather than
        // watching live. It goes in the working directory on purpose — see Diagnostics/Journal.cs.
        if (Flag("--journal") || Arg("--journal") is not null)
        {
            // `--journal` is usable bare or with a path, so a following option is not its value.
            string? where = Arg("--journal") is { } given && !given.StartsWith("--") ? given : null;
            Journal.Current = Journal.Open(where, levelName, location.SceneFile, args);
            Console.WriteLine($"journal: {System.IO.Path.GetFullPath(Journal.Current.Path)}");
            Journal.Current.Write(0, JournalKind.Run,
                ("level", (object?)levelName), ("scene", location.SceneFile),
                ("things", location.Things.Count()), ("minds", location.Minds?.All.Length ?? 0),
                ("player", location.Player is null ? null : Watch.Name(location.Player.Thing)),
                ("fixes", string.Join(',', Fixes.Current.All.Where(f => f.On).Select(f => f.Name)
                                                 .DefaultIfEmpty("none"))),
                ("qoloff", string.Join(',', Qol.Current.All.Where(q => !q.On).Select(q => q.Name)
                                                .DefaultIfEmpty("none"))));
        }
        frame.Glow = glow;
        // `--no-relight`: the things keep the light they were built with, as earlier (a comparison).
        if (Flag("--no-relight")) frame.Poses.Place = null;
        // The click-to-move ring. It borrows the world renderer's pipelines and the location's own
        // material cache, so it costs one quad and no shader.
        markerRenderer = frame.World.NewRing(location.Materials.Get(WalkTarget.Material));
        frame.Marker = markerRenderer;
        if (Flag("--no-fog")) frame.Fog = FogSettings.None;
        frame.World.Gamma = MenuChoices.Session.Brightness(Arg("--gamma") is { } g ? F(g) : null, GameOptions.Current.Gamma);
        frame.SkyEverywhere = Flag("--sky-everywhere");
    }

    bool buyUseDone;

    /// <summary>
    /// `--buy-use <file.nbs>` (harness, with `--settle`): a second into the settle, the shop opened on the nearest other
    /// character as `--shop` opens it, the steered character given the money, every tome on the list bought from the
    /// bottom up (`Shops.Buy`, the purchase's own path), the shop shut, and each bought tome then used from the pack as a
    /// right click uses it (<see cref="UseItem"/>), each line saying what came of it and which disciplines changed.
    /// </summary>
    void BuyUse(string shopFile)
    {
        if (location.Steered is not { Actor: { } me } shopper) { Console.WriteLine("buy-use: nobody steered"); return; }
        var keeper = location.Things.Where(t => t.IsActor && t.InWorld && !ReferenceEquals(t, shopper) && !location.PartyMembers().Contains(t))
                             .OrderBy(t => Vector3.Distance(t.Position, shopper.Position)).FirstOrDefault();
        if (keeper is null || !session.Shops.Add(location, shopFile, keeper, "SHOP", 0, 1.25f, 0.8f, 0x1ff))
        { Console.WriteLine($"buy-use: no shop {shopFile} on anybody"); return; }
        session.Shops.Execute(location, keeper, Vitomir.Engine.Rpg.Shops.WantFeedback, null);
        if (session.Shops.Active is not { } store) { Console.WriteLine("buy-use: the shop did not open"); return; }
        session.AddCash(shopper, 1_000_000);
        Console.WriteLine($"buy-use: {shopper.Template} buys from {keeper.Template}'s {store}");
        var bought = new List<WorldThing>();
        foreach (var line in store.Lines.Where(l => l.Treasure is { } t && t.Template.StartsWith("tome", StringComparison.OrdinalIgnoreCase)).Reverse().ToList())
        {
            var result = session.Shops.Buy(location, store, line.Treasure!.Id, out var thing);
            Console.WriteLine($"  buy {line.Template}: {result}{(thing is null ? "" : $", guid {thing.Guid}, script {thing.Script}")}");
            if (thing is not null) bought.Add(thing);
        }
        session.Shops.End();
        string Of() => string.Join(",", me.Disciplines.All.Select(d => $"{d.Definition.Name}={d.Level}"));
        foreach (var thing in bought)
        {
            var before = Of().Split(',');
            string said = thing.Item is { } item ? UseItem(item) : "no item";
            var gained = Of().Split(',').Except(before);
            Console.WriteLine($"  use {thing.Template} (guid {thing.Guid}): {said}; changed [{string.Join(",", gained)}]; still in the pack: {me.Inventory.Items.Any(i => ReferenceEquals(i, thing.Item))}");
        }
    }

    void StartShops()
    {
        HookShops();
        // `--shop <file.nbs>`: a harness knob that opens a shop on the nearest other character, as a
        // keeper's script would, for a still or a run with nobody to find the shopkeeper. It buys and
        // sells at the shipped scripts' 1.25 and 0.8 and takes everything (mask 0x1ff).
        if (Arg("--shop") is { } shopFile && location.Steered is { } shopper)
        {
            var keeper = location.Things.Where(t => t.IsActor && !ReferenceEquals(t, shopper))
                                 .OrderBy(t => Vector3.Distance(t.Position, shopper.Position)).FirstOrDefault() ?? shopper;
            if (!session.Shops.Add(location, shopFile, keeper, Arg("--shop-name") ?? "SMITHY", 0, 1.25f, 0.8f, 0x1ff))
                Console.WriteLine($"shop: no {Vitomir.Engine.Rpg.Shops.Folder}{shopFile} in the install");
            else session.Shops.Execute(location, keeper, Vitomir.Engine.Rpg.Shops.WantFeedback, null);
        }

        // `--say <recording>`: the steered character speaks a recorded line as a conversation's line
        // is spoken, so its lip-sync can be watched (harness).
        if (Arg("--say") is { } sayName && location.Steered is { } sayer && audio.Bank.Load(sayName, stream: true) is { } said)
        {
            sayer.MouthChannel = audio.Mixer.Play2D(said, 1f, 0.5f, 0);
            sayer.MouthBusyUntil = audio.Mixer.GameMs + said.LengthMs;
            Console.WriteLine($"say: {said.Name}, {said.LengthMs} ms, a lip-sync track of {said.Lips.Length} frames");
        }
        // The developer console. Its table is always there, as 1.1's is (made
        // at start-up), because a script's `CodexConsole.Execute` runs its lines.
        // **The grave key always opens it** (always available, no launch switch) — 1.1 opens it
        // only after `-console`; ours, always on with no switch.
        consoleKey = true;
        ConsoleHost.Attach(menus, new Vitomir.Engine.Ui.ConsoleCommands.Game
            {
                Session = session, Here = () => location, Audio = audio, Menus = menus, Camera = () => worldCam,
                Gamma = v => frame.World.Gamma = v, Fog = f => frame.Fog = f,
                HeadsShown = () => headsPass.ShowHeads, ShowHeads = on => headsPass.ShowHeads = on,
                Clipboard = () => InputTape.Clipboard(host.Clipboard),   // a tape's
                Advance = (scene, sub) => scenes?.AdvanceChronicle(scene, sub) == true, Advancement = OpenAdvancement, Vault = () => { if (Vitomir.Engine.Ui.InventoryPane.VaultMayOpen(location.Steered, vaultPass.Enabled)) ShowVault(true); },
                Read = name => { Vitomir.Engine.Ui.DocumentOpen.Before(captions, worldCam, location.Steered); if (!readPass.Enabled && documents.Find(name) is { } doc) { Vitomir.Engine.Ui.ReadPaneOpen.Accepted(worldCam, location.Steered, menus.CloseGamePanes, menus); readPass.Pane!.Open(doc); } },
                Report = ConsoleHost.Report, Text = strings, Sky = on => gpu.SkyOff = !on, FarClip = f => cam.Far = f,
                Ping = () => net?.Client?.RoundTripMs,                        // `ping` on a player's machine
                EndTextGone = endText.LetGo,                                    // `revive` of a party character
                LogWindow = on => host.LogWindow("classic", on),              // `logwindow` (ours): the host's log window
            }, line => { session.Messages.Add(line); Console.WriteLine($"  console: {line}"); });
        CodexScenes.ConsoleLine = line => menus.DevConsole?.Run(line) == true;
    }

    // **The player modules' interface bindings** (each module's manifest's `actions`, in load order, a later module's binding over an
    // earlier one's; 1.1's own bar elements left out), given to the status bar, and the actions the bar cannot do itself done here:
    // the advancement screen and the vault as the console's `advance` and `vault` open them, the save and load screens as the quick
    // keys' screens (single player), the escape menu.
    void BindInterfaceActions()
    {
        var bound = new Dictionary<string, Vitomir.Platform.PlayerModules.InterfaceAction>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in files.Modules)
            foreach (var (element, action) in Vitomir.Platform.PlayerModules.InterfaceActions.Effective(layer.Actions))
                bound[element] = action;
        hud.Bar.Bindings = bound;
        foreach (var (element, action) in bound) Console.WriteLine($"interface: {element} bound to {action.Name} ({action.Words})");
        hud.Bar.Perform = action => action.Name switch
        {
            "open-advancement" => location.Steered is { } who ? Opened(() => OpenAdvancement(who), () => advancement is not null, "the advancement screen") : "nobody is being steered",
            "open-vault" => Vitomir.Engine.Ui.InventoryPane.VaultMayOpen(location.Steered, vaultPass.Enabled)
                ? Opened(() => ShowVault(true), () => vaultPass.Enabled, "the vault") : vaultPass.Enabled ? "the vault is up already" : "the vault opens only for a player's character",
            "save" => NetPlay.Active ? "not in a network game" : Asked(MenuRequest.SaveMenu, "the save screen"),
            "load" => NetPlay.Active ? "not in a network game" : Asked(MenuRequest.LoadMenu, "the load screen"),
            "escape-menu" => Opened(menus.OpenEscape, () => menus.Up, "the escape menu"),
            _ => null,
        };

        static string Opened(Action open, Func<bool> up, string what)
        {
            open();
            return $"{what} {(up() ? "open" : "not opened")}";
        }

        string Asked(MenuRequest request, string what)
        {
            StopSwivel();
            menus.Ask(request);
            return $"{what} asked for";
        }
    }

    // The panes' modern-day art (at every scene's start): London's and New York's
    // chronicles dress the seven panes and the soak pie in their `…md` pictures.
    void DressPanes()
    {
        ui.Assets.SetModernDay(session.ModernDayArt);
        charPass.Pane!.ModernDay = session.ModernDayArt;
    }


    // Opening the vault is two panes, not one. The vault's show hook puts the
    // **inventory** pane up beside it and its hide hook takes that one down again, so
    // the pair open and close together — which is the only way dragging between them makes sense.
    // The hand is told about the vault's grid only while it is up, because 1.1 destroys the
    // element on close and a click where it used to be has to miss.
    void ShowVault(bool on)
    {
        if (on) { charPass.Enabled = false; questPass.Enabled = false; }   // visGroup 1
        // The bar's pane buttons hidden and the coin greyed, as the shop does;
        // both of the vault's closers undo it.
        if (on || vaultPass.Enabled)
        {
            foreach (var name in Vitomir.Engine.Ui.StatusBar.ShopAndVaultHide) if (hud.Bar[name] is { } b) b.Shown = !on;
            invPass.GoldGreyed = on;
        }
        vaultPass.Enabled = on;
        // Unconditional, because the engine's is: the vault's close hides the inventory pane whether or not
        // the player had it open before the vault. Copied rather than corrected.
        ShowInventory(on);
    }

    // The right-hand panes (`visGroup=2`) take each other down: the inventory and the discipline
    // pane share the right half of the screen. Showing raises the pane, latches the
    // bar's inventory button and re-splits the world view; hiding undoes it
    // (`WorldView.Columns`), both read live from `invPass.Enabled` below.
    void ShowInventory(bool on)
    {
        if (on) discPass.Enabled = false;
        invPass.Enabled = on;
    }

    // Hiding un-latches the bar's discipline button and re-splits the world view, both
    // read live from `discPass.Enabled` rather than pushed by the hide hook itself.
    void ShowDisciplines(bool on)
    {
        if (on)
        {
            invPass.Enabled = false;
            if (advancement is null) barAsked = true;
        }
        discPass.Enabled = on;
        if (on) RebuildDisciplines();
    }

    // The rebuild (and the show hook): the steered character's disciplines, or the
    // one being advanced — and the new-group announcement, when the pane is up to make it.
    void RebuildDisciplines()
    {
        var pane = discPass.Pane!;
        pane.Advancing = advancement is not null;
        foreach (var group in pane.Rebuild(advancement?.Subject ?? location.Steered, discPass.Enabled))
        {
            Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.NewDiscipline);
            session.Messages.Post("RPG_DISCIPLINELEARNED", "%d" + group.Name, Vitomir.Engine.Ui.MessageLog.Yellow);
        }
    }


    // **The advancement screen**: not a pane of its own but the
    // character sheet on the left and the discipline pane on the right, both with their raise and
    // learn buttons up, over a bar of its own — with the game paused under it.
    void OpenAdvancement(Vitomir.Engine.World.WorldThing asked)
    {
        if (advancement is not null) return;                                // already advancing
        advancement = new Vitomir.Engine.Rpg.Advancement(asked, net?.WalkParty(asked) ?? hud.Bar.PartyThings, powers)
                      { Network = net?.AdvanceRules() };
        session.Messages.Clear();
        ShowQuests(false);
        if (vaultPass.Enabled) ShowVault(false);
        BindAdvancement();
        Console.WriteLine($"  advance: {Watch.Name(advancement.Subject)} has "
                        + $"{advancement.Subject.Actor?.SpendableExperience} experience to spend");
    }

    // Both halves shown on whoever is being advanced (the two show hooks).
    void BindAdvancement()
    {
        if (advancement is null) return;
        charPass.Pane!.Subject = advancement.Subject.Actor;
        charPass.Pane.Advancing = true;
        ShowCharacter(true);
        ShowDisciplines(true);
        advBarPass.Enabled = true;
    }

    // "Finished": the next party member, or the end — which sends the party on to the
    // place `SetupAdvance` named.
    void AdvanceDone()
    {
        if (advancement is null) return;
        net?.Advanced(advancement.Subject);         // a network game's: sent to the host, and kept
        if (advancement.Next()) { BindAdvancement(); Console.WriteLine($"  advance: now {Watch.Name(advancement.Subject)}"); return; }
        advancement = null;                          // the advancement screen closes
        charPass.Pane!.Advancing = false;
        charPass.Pane.Subject = location.Steered?.Actor;
        ShowCharacter(false);
        ShowDisciplines(false);
        advBarPass.Enabled = false;
        advBarPass.Pressed = null;
        hud.Bar.Update();
        advanceFinished = true;                     // the sequence step below sends the party on
        Console.WriteLine("  advance: done");
    }

    // "Revert character stats", and Esc.
    void AdvanceRevert()
    {
        if (advancement is null) return;
        advancement.Reset();
        session.Messages.Clear();
        RebuildDisciplines();
        hud.Bar.Update();
        Console.WriteLine($"  advance: {Watch.Name(advancement.Subject)} put back as they were");
    }

    // The other two left-hand panes (`visGroup=1`): showing one takes down whichever of the three
    // was up — its hide hook runs, which for the vault takes the pack down with it.
    // Hiding hides the advancement raise buttons, un-latches the bar's character
    // button and re-splits the world view — all read live from `charPass.Enabled` below rather
    // than pushed by the hide hook itself.
    void ShowCharacter(bool on)
    {
        if (on) { questPass.Enabled = false; if (vaultPass.Enabled) ShowVault(false); }
        charPass.Enabled = on;
    }

    void ShowQuests(bool on)
    {
        if (on)
        {
            charPass.Enabled = false;
            if (vaultPass.Enabled) ShowVault(false);
            // The show hook puts the bar's button down and stops its flash.
            hud.Bar["sbQuestButton"]?.StopBlink();
        }
        questPass.Enabled = on;
    }


    // The hand reaches exactly the panes that are up. The engine gets this for free — 1.1
    // **destroys** the vault's elements when it closes, so a click where the grid used to be finds
    // nothing — and here it is one list, rebuilt on the spot rather than maintained by whoever
    // happened to toggle a pane.
    void SyncPanes()
    {
        if (hands is null) return;
        hands.Panes.Clear();
        if (invPass.Enabled) hands.Panes.Add(invPass.Pane!);
        if (vaultPass.Enabled) hands.Panes.Add(vaultPass.Pane!);
    }


    string UseItem(Vitomir.Engine.Rpg.Item item)
    {
        if (location.Steered is not { } user) return "nobody to use it";
        var result = Vitomir.Engine.Rpg.Items.Use(user, item, frame.Sim.Milliseconds, effects.File);
        string said = result switch
        {
            Vitomir.Engine.Rpg.UseResult.Used => $"used {item.Name}",
            Vitomir.Engine.Rpg.UseResult.UsedUp => $"used the last of {item.Name}",
            Vitomir.Engine.Rpg.UseResult.Held => $"{item.Name} is in hand",
            Vitomir.Engine.Rpg.UseResult.Readable => Read(item),
            Vitomir.Engine.Rpg.UseResult.NotWritten => $"{item.Name} is a kind of item nothing uses yet",
            _ => $"{item.Name} refused",
        };
        return said;
    }

    bool ShowInventoryToggle() { ShowInventory(!invPass.Enabled); return invPass.Enabled; }

    bool ShowDisciplinesToggle() { ShowDisciplines(!discPass.Enabled); return discPass.Enabled; }

    bool ShowCharacterToggle() { ShowCharacter(!charPass.Enabled); return charPass.Enabled; }

    bool ShowQuestsToggle() { ShowQuests(!questPass.Enabled); return questPass.Enabled; }

    // A press on the shop pane. True when the pane took it — anywhere on its artwork.
    // (`x`, `y`, here and at the release: the pointer in the space of the panes above the bar, StatusBar.PaneFromWindow)
    bool ShopPress(float x, float y)
    {
        var bp = buyPass.Pane!;
        if (bp.Over(Vitomir.Engine.Ui.BuySellPane.List, x, y))
        {
            // The pane hook's BUTTONDEPRESS for a list, then the list's press.
            Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.ButtonDepress);
            bp.HoverAt(x, y);
            bp.ClickList();
            return true;
        }
        foreach (var name in new[] { Vitomir.Engine.Ui.BuySellPane.BuyButton, Vitomir.Engine.Ui.BuySellPane.SellButton,
                                     Vitomir.Engine.Ui.BuySellPane.DoneButton })
            if (bp.Over(name, x, y))
            {
                // Pressed even when greyed: the sound plays, the button does not go down.
                Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.ButtonDepress);
                bool live = name == Vitomir.Engine.Ui.BuySellPane.BuyButton ? bp.BuyEnabled
                          : name == Vitomir.Engine.Ui.BuySellPane.SellButton ? bp.SellEnabled : true;
                buyPass.Pressed = live ? name : null;
                return true;
            }
        if (bp.Over(Vitomir.Engine.Ui.BuySellPane.Slider, x, y) || bp.Element(Vitomir.Engine.Ui.BuySellPane.ScrollBar) is { Pos.Length: > 1 } bar
            && x >= bar.Pos[0] && x <= bar.Pos[0] + 8 && y >= bp.SliderY && y <= bp.SliderY + 8)
        {
            shopDrag = bp.CanDrag;
            return true;
        }
        if (bp.Over(Vitomir.Engine.Ui.BuySellPane.ScrollBar, x, y)) { bp.ClickTrack(y); return true; }
        return bp.Covers(x, y);
    }

    // The release: a held button that is still under the pointer fires (event 1 on the same element).
    void ShopRelease(float x, float y)
    {
        shopDrag = false;
        if (buyPass.Pressed is not { } held) return;
        buyPass.Pressed = null;
        Vitomir.Engine.Audio.Noise.Ui(location, Vitomir.Engine.Audio.FoleyEvent.ButtonRelease);
        var bp = buyPass.Pane!;
        if (!bp.Over(held, x, y)) return;
        var shops = session.Shops;
        if (held == Vitomir.Engine.Ui.BuySellPane.BuyButton)
            Console.WriteLine($"  shop: buy {bp.SelectedTreasure?.Template ?? "nothing"} -> {bp.ClickBuy(location, shops)}, {session.Gold} left");
        else if (held == Vitomir.Engine.Ui.BuySellPane.SellButton)
        {
            string what = bp.SelectedItem?.Template ?? "nothing";
            Console.WriteLine($"  shop: sell {what} -> {(bp.ClickSell(location, shops) ? "sold" : "refused")}, {session.Gold} now");
        }
        else if (held == Vitomir.Engine.Ui.BuySellPane.DoneButton)
        {
            shops.End();                             // Done button jumps to the shop's end
            Console.WriteLine("  shop: closed (done)");
        }
    }


    /// <summary>
    /// The left button let go with a shop's press or drag of an item under way (ours, `quickequip`):
    /// over a pane's grid, over a belt cell, or over neither — the shelf and the rest, where a lifted item
    /// goes back (`InventoryHands.Release`). `rx`, `ry` are in the bar's space, `prx`, `pry` in the panes'.
    /// </summary>
    void ShopHandRelease(Vitomir.Engine.Ui.InventoryHands hand, float rx, float ry, int prx, int pry)
    {
        int cell = hud.Enabled ? hud.Bar.QuickCellAt((int)rx, (int)ry) : -1;
        var got = hand.Over(prx, pry) ? hand.Release(prx, pry)
                : cell >= 0 ? hand.ReleaseIn(cell, 0)
                : hand.ReleaseOff();
        Console.WriteLine($"  inv: {got.Say}");
    }

    // (`ux`, `uy`: the pointer in the space of the panes above the bar, StatusBar.PaneFromWindow)
    bool OverPane(int ux, int uy)
    {
        SyncPanes();
        return hands is { } h && h.Over(ux, uy) || questPass.Enabled && questPass.Pane!.Covers(ux, uy)
               || discPass.Enabled && discPass.Pane!.Covers(ux, uy)
               // While a shop is open the game is in state 4, whose frame has no world branch at all:
               // nothing in the world is hovered or clicked, wherever the pointer is.
               || buyPass.Enabled;
    }


    string Read(Vitomir.Engine.Rpg.Item item)
    {
        if (documents.Find(item.TextId) is not { } doc)
            return $"{item.Name} names \"{item.TextId}\", which is in none of the loaded documents";
        readPass.Pane!.Open(doc);
        return $"reading \"{doc.Title}\" — {doc.Paragraphs.Count} paragraphs";
    }


    // A shop is opened by its keeper's script (`ExecuteBuySell`) and by nothing else.
    void HookShops()
    {
        var shops = session.Shops;
        shops.Text = strings.Find;
        // The look camera let go, the vault and the document shut, before the pane.
        shops.CloseModals = () =>
        {
            // The swivel that goes with it is let go by the frame loop (below), which owns it.
            if (worldCam.Mode == CameraMode.Look) worldCam.BackToGame(location.Steered);
            if (vaultPass.Enabled) ShowVault(false);
            if (readPass.Enabled) readPass.Pane!.Open(null);
        };
        shops.Opened = store =>
        {
            buyPass.Pane!.Store = store;
            buyPass.Enabled = true;
            // The show hook: the bar's pane buttons hidden, the pack up beside it.
            foreach (var name in shopHides) if (hud.Bar[name] is { } b) b.Shown = false;
            invPass.GoldGreyed = true;
            ShowInventory(true);
            if (hands is { } h)
            {
                h.Shop = item => item.Thing is { } t && buyPass.Pane.ClickItem(t);
                // A right click sells at once (ours, always on; `BuySellPane.QuickSell`).
                h.QuickSell = item =>
                {
                    bool sold = item.Thing is { } t && buyPass.Pane.QuickSell(t, location, session.Shops);
                    Console.WriteLine($"  shop: sell {item.Name} (right click) -> {(sold ? "sold" : "refused")}, {session.Gold} now");
                    return sold;
                };
            }
            hud.Bar.Shopping = true;   // the shop flag, which a portrait's right click asks
            buyPass.Pane.Refresh();
            Console.WriteLine($"  shop: {store.Name} ({store.File}), {buyPass.Pane.Rows.Count} on the shelf, "
                            + $"trader {(store.Trader is null ? "nobody" : Watch.Name(store.Trader))}");
            return true;
        };
        shops.Changed = _ => buyPass.Pane!.Refresh();
        // The hide hook: the buttons back, the pack down with the pane.
        shops.Closed = _ =>
        {
            buyPass.Enabled = false;
            buyPass.Pressed = null;
            shopDrag = false;
            buyPass.Pane!.Store = null;
            buyPass.Pane.ClearSelection();
            foreach (var name in shopHides) if (hud.Bar[name] is { } b) b.Shown = true;
            invPass.GoldGreyed = false;
            ShowInventory(false);
            // A drag under way goes back where it came from first (ours; `InventoryHands.EndShopDrag`).
            if (hands is { } h) { h.EndShopDrag(); h.Shop = null; h.QuickSell = null; }
            hud.Bar.Shopping = false;
        };
    }
}
