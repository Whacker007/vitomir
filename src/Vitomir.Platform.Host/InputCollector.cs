// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using SDL;
using Vitomir.Platform.Control;
using static SDL.SDL3;

namespace Vitomir.Platform.Host;

/// <summary>
/// The window's events turned into the simulation's actions: keys and mouse buttons
/// through the player's bindings into held <see cref="Buttons"/> and a move, the mouse's motion into a
/// look. A frame may hold several ticks or none; the look gathered over a frame goes to the first tick it
/// steps, so nothing is lost or counted twice, and the tape records exactly what each tick was given. The pointer
/// is kept as its place on the picture (<see cref="SetPicture"/>), with the picture's shape; while the pointer
/// is free (<see cref="PointerFree"/>) a source bound to <see cref="BindingTable.PointerClick"/> works the click only,
/// and while it is captured its other action only, so the click and the attack on Mouse Left never mix.
/// </summary>
public sealed unsafe class InputCollector
{
    /// <summary>The stealth toggle's action in the bindings.</summary>
    public const string StealthAction = PlatformSettings.StealthAction;

    BindingTable table;
    // Keys shared: how many sources hold each action down, and what each source down pressed (so letting it go lets go
    // of exactly those, and an action two keys work stays down while either is).
    readonly Dictionary<string, int> down = new(StringComparer.Ordinal);
    readonly Dictionary<InputSource, List<string>> pressedBy = [];
    readonly Stack<List<string>> spare = new();
    readonly HashSet<string> pressedThisFrame = new(StringComparer.Ordinal);
    readonly HashSet<string> pressedSinceTick = new(StringComparer.Ordinal);   // a tap shorter than a tick still reaches one
    float sensitivity;
    bool invertY;
    readonly Action<string>? say;
    Vector2 look;
    Vector2 pointer = new(0.5f, 0.5f);
    // The picture's place in the window (window coordinates) and its shape; the whole window until told.
    float pictureX, pictureY, pictureW, pictureH, aspect;

    /// <summary>Whether the player's pointer is free (the program sets it each frame from the session): the click's sources then work the click.</summary>
    public bool PointerFree { get; set; }

    /// <summary>
    /// Whether the orbit camera may be swung now (the program sets it each frame: third person, the pointer captured). While it may and its key
    /// (<see cref="Cameras.OrbitCamera.Action"/>) is held, the mouse's look goes to the orbit (<see cref="TakeOrbit"/>), not to the player's turn.
    /// </summary>
    public bool OrbitAllowed { get; set; }

    Vector2 orbitLook;
    float wheelTurn;

    /// <summary>Whether the orbit camera's key is held and may swing it.</summary>
    public bool OrbitHeld => OrbitAllowed && down.ContainsKey(Cameras.OrbitCamera.Action);

    /// <summary>
    /// The look gathered for the orbit camera since the last call (the player's turn's axes: x to the left, y up) and the wheel's notches turned
    /// that no binding takes (up positive), both then forgotten.
    /// </summary>
    public (Vector2 Look, float Wheel) TakeOrbit()
    {
        var taken = (orbitLook, wheelTurn);
        orbitLook = Vector2.Zero;
        wheelTurn = 0f;
        return taken;
    }

    /// <summary>The pointer's place on the picture (0 to 1 across and down), as the next tick will be given it.</summary>
    public Vector2 Pointer => pointer;

    /// <summary>
    /// Where the picture is in the window (window coordinates: its left, top, width and height) and its shape (its
    /// pixels' width over their height), for the pointer's place on the picture (the picture is fitted in the
    /// window, centred, so the bars beside it are outside 0 to 1).
    /// </summary>
    public void SetPicture(float x, float y, float width, float height, float pictureAspect)
    {
        (pictureX, pictureY, pictureW, pictureH) = (x, y, width, height);
        aspect = pictureAspect;
    }

    /// <summary>Puts the pointer at the picture's centre, where the reticle is (the program warps the system's pointer there when it is freed).</summary>
    public void CentrePointer() => pointer = new Vector2(0.5f, 0.5f);

    public InputCollector(PlatformSettings settings, Action<string>? say = null)
    {
        this.say = say;
        table = Apply(settings);
    }

    /// <summary>
    /// Takes the player's bindings, the mouse's sensitivity and inverting the view from <paramref name="settings"/>, in
    /// place of those before (the Controls page's OK, at once); nothing stays held from before.
    /// </summary>
    public BindingTable Apply(PlatformSettings settings)
    {
        sensitivity = settings.MouseSensitivity;
        invertY = settings.InvertY;
        table = SdlKeys.Table(settings.Bindings);
        foreach (var problem in table.Problems) say?.Invoke($"bindings: {problem}");
        // Every action named, an unbound one as "none" (the toggled actions are unbound by default).
        say?.Invoke("bindings: " + SdlKeys.Describe(table, settings.Bindings.Keys));
        // Every key that works more than one action, with them all.
        if (SdlKeys.DescribeShared(table) is { Length: > 0 } shared) say?.Invoke("bindings: keys shared: " + shared);
        ReleaseAll();
        return table;
    }

    /// <summary>The bindings in force.</summary>
    public BindingTable Table => table;

    /// <summary>Lets go of everything held and forgets the look gathered (a menu opening or closing: nothing stays held across it).</summary>
    public void ReleaseAll()
    {
        LetGoOfAll();
        pressedSinceTick.Clear();
        look = Vector2.Zero;
        orbitLook = Vector2.Zero;
        wheelTurn = 0f;
    }

    /// <summary>What a key, mouse-button or wheel event names, or null for any other event (or a key's repeat).</summary>
    public static InputSource? SourceOf(in SDL_Event e) => (SDL_EventType)e.type switch
    {
        SDL_EventType.SDL_EVENT_KEY_DOWN when !e.key.repeat => InputSource.Key((int)e.key.scancode),
        SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN => InputSource.Mouse(e.button.button),
        SDL_EventType.SDL_EVENT_MOUSE_WHEEL when WheelTurn(e) != 0f => InputSource.Wheel(WheelTurn(e) > 0f ? 1 : -1),
        _ => null,
    };

    static float WheelTurn(in SDL_Event e) => e.wheel.direction == SDL_MouseWheelDirection.SDL_MOUSEWHEEL_FLIPPED ? -e.wheel.y : e.wheel.y;

    /// <summary>Whether a host-level action (menu, screenshot) went down during this frame's events.</summary>
    public bool Pressed(string action) => pressedThisFrame.Contains(action);

    public bool QuitAsked { get; private set; }

    /// <summary>Starts a frame: forgets last frame's presses.</summary>
    public void BeginFrame() => pressedThisFrame.Clear();

    /// <summary>Takes one event. Mouse motion counts only while <paramref name="looking"/> (the pointer captured).</summary>
    public void Take(in SDL_Event e, bool looking, int windowWidth, int windowHeight)
    {
        switch ((SDL_EventType)e.type)
        {
            case SDL_EventType.SDL_EVENT_QUIT:
            case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                QuitAsked = true;
                break;
            case SDL_EventType.SDL_EVENT_KEY_DOWN:
                if (e.key.repeat) break;
                // Escape always opens the menu when nothing else has it, so a player who unbound "menu" is never shut in.
                if (!Press(InputSource.Key((int)e.key.scancode)) && e.key.scancode == SDL_Scancode.SDL_SCANCODE_ESCAPE) pressedThisFrame.Add("menu");
                break;
            case SDL_EventType.SDL_EVENT_KEY_UP:
                Release(InputSource.Key((int)e.key.scancode));
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
                Press(InputSource.Mouse(e.button.button));
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                Release(InputSource.Mouse(e.button.button));
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
            {
                // A turn of the wheel is a tap: down for the tick it reaches, then up.
                float turn = WheelTurn(e);
                if (turn != 0f && Press(InputSource.Wheel(turn > 0f ? 1 : -1))) Release(InputSource.Wheel(turn > 0f ? 1 : -1));
                else if (turn != 0f) wheelTurn += turn;   // a wheel no binding takes zooms the third-person camera
                break;
            }
            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                // Captured, the pointer waits at the centre (where the reticle is), so a freed pointer starts there.
                if (looking)
                {
                    var moved = new Vector2(-e.motion.xrel, invertY ? e.motion.yrel : -e.motion.yrel) * sensitivity;
                    if (OrbitHeld) orbitLook += moved; else look += moved;   // the orbit camera's key held, the camera swings and the player does not turn
                    pointer = new Vector2(0.5f, 0.5f);
                }
                else if (pictureW > 0 && pictureH > 0) pointer = new Vector2((e.motion.x - pictureX) / pictureW, (e.motion.y - pictureY) / pictureH);
                else if (windowWidth > 0 && windowHeight > 0) pointer = new Vector2(e.motion.x / windowWidth, e.motion.y / windowHeight);
                break;
            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
                LetGoOfAll();                 // nothing stays held across a change of focus (as the old program)
                look = Vector2.Zero;
                break;
        }
    }

    /// <summary>
    /// A source goes down: the freed pointer's click alone when the pointer is free and the source works it, else every
    /// action it works but the click (a shared key works them all; cancel among them). Whether anything was bound to it.
    /// </summary>
    bool Press(InputSource source)
    {
        var all = table.ActionsOf(source);
        if (all.Count == 0) return false;
        if (pressedBy.ContainsKey(source)) return true;     // already down (a second press without a release)
        var list = spare.Count > 0 ? spare.Pop() : [];
        list.Clear();
        if (PointerFree && table.Clicks(source)) list.Add(BindingTable.PointerClick);
        else foreach (var a in all) if (a != BindingTable.PointerClick) list.Add(a);
        pressedBy[source] = list;
        foreach (var a in list) Down(a);
        return true;
    }

    /// <summary>A source goes up: the actions its press worked let go (each stays down while another source holds it).</summary>
    void Release(InputSource source)
    {
        if (!pressedBy.Remove(source, out var list)) return;
        foreach (var a in list)
            if (down.TryGetValue(a, out int n)) { if (n <= 1) down.Remove(a); else down[a] = n - 1; }
        spare.Push(list);
    }

    /// <summary>An action goes down (one more source holds it).</summary>
    void Down(string action)
    {
        down[action] = down.TryGetValue(action, out int n) ? n + 1 : 1;
        pressedThisFrame.Add(action);
        pressedSinceTick.Add(action);
    }

    /// <summary>Lets go of every source and action held.</summary>
    void LetGoOfAll()
    {
        foreach (var (_, list) in pressedBy) spare.Push(list);
        pressedBy.Clear();
        down.Clear();
    }

    /// <summary>The next tick's actions: what is held now, and the look gathered since the last tick that took it.</summary>
    public ActionFrame NextTick()
    {
        var move = new Vector2((Held("right") ? 1 : 0) - (Held("left") ? 1 : 0), (Held("forward") ? 1 : 0) - (Held("back") ? 1 : 0));
        if (move.LengthSquared() > 1) move = Vector2.Normalize(move);
        var b = Buttons.None;
        if (Held("jump")) b |= Buttons.Jump;
        if (Held("sprint")) b |= Buttons.Sprint;
        if (Held("crouch")) b |= Buttons.Crouch;
        if (Held("use")) b |= Buttons.Use;
        if (Held("switchView")) b |= Buttons.SwitchView;
        // No swapShoulder — Buttons.SwapShoulder is never sent (its bit is kept for older tapes).
        if (Held("freePointer")) b |= Buttons.FreePointer;
        if (Held("freeLook")) b |= Buttons.FreeLook;      // free look, held
        if (Held("walk")) b |= Buttons.Walk;
        if (Held("switchMode")) b |= Buttons.SwitchMode;
        if (Held("attack")) b |= Buttons.Attack;
        if (Held("block")) b |= Buttons.Block;
        if (Held("dodge")) b |= Buttons.Dodge;         // the simulation takes its press
        if (Held("swapWeapon")) b |= Buttons.SwapWeapon;   // the sword and the bow swapped (a press)
        if (Held("aim")) b |= Buttons.Aim;                 // the bow raised, undrawn (held)
        if (Held("masquerade")) b |= Buttons.Masquerade;
        if (Held(StealthAction)) b |= Buttons.Stealth;      // stealth's toggle (a press)
        if (Held(BindingTable.Cancel)) b |= Buttons.Cancel;
        if (Held(BindingTable.PointerClick)) b |= Buttons.PointerClick;
        // The toggled halves of crouch, walk and the freed pointer (HoldAndToggle); their latches are the simulation's.
        for (int i = 0; i < HoldAndToggle.Pairs.Count; i++) if (Held(HoldAndToggle.Pairs[i].Toggled)) b |= HoldAndToggle.Pairs[i].ToggleButton;
        // The quick slots' keys (slot1 … slot8); the simulation takes a slot's press, not its holding.
        for (int i = 0; i < QuickSlotButtons.Actions.Count; i++) if (Held(QuickSlotButtons.Actions[i])) b |= QuickSlotButtons.Of(i + 1);
        var frame = new ActionFrame(move, look, b, pointer, aspect);
        look = Vector2.Zero;
        pressedSinceTick.Clear();
        return frame;
    }

    bool Held(string action) => down.ContainsKey(action) || pressedSinceTick.Contains(action);
}
