// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.Control;

/// <summary>What a binding names: a key, a mouse button or a turn of the wheel.</summary>
public enum InputSourceKind
{
    Key = 0,
    MouseButton = 1,
    Wheel = 2,
}

/// <summary>
/// One thing a player presses (ours): a key by its scancode, a mouse button by SDL's number (1 the left, 2 the
/// middle, 3 the right, 4 and 5 the side buttons), or the wheel (+1 a turn up, −1 a turn down).
/// </summary>
public readonly record struct InputSource(InputSourceKind Kind, int Code)
{
    public static InputSource Key(int scancode) => new(InputSourceKind.Key, scancode);
    public static InputSource Mouse(int sdlButton) => new(InputSourceKind.MouseButton, sdlButton);
    public static InputSource Wheel(int direction) => new(InputSourceKind.Wheel, Math.Sign(direction));
}

/// <summary>
/// The names a binding may use (ours). Keys by SDL's key names (<c>W</c>, <c>Space</c>, <c>Left Ctrl</c>);
/// the mouse as <c>Mouse Left</c>, <c>Mouse Right</c>, <c>Mouse Middle</c>, <c>Mouse 4</c>, <c>Mouse 5</c>, or
/// numbered the way Windows and Quake number them — <c>Mouse1</c> the left, <c>Mouse2</c> the right,
/// <c>Mouse3</c> the middle (SDL numbers the middle 2 and the right 3; an earlier build read <c>Mouse2</c> as SDL's 2, the
/// middle, which no player means); the wheel as <c>Wheel Up</c> and <c>Wheel Down</c>. Case and spaces do not
/// matter for the mouse's and the wheel's names.
/// </summary>
public static class BindingNames
{
    public const int SdlLeft = 1, SdlMiddle = 2, SdlRight = 3, SdlX1 = 4, SdlX2 = 5;

    /// <summary>
    /// What <paramref name="name"/> names, or null when nothing: <paramref name="keyCode"/> looks a key's name up
    /// (SDL's <c>SDL_GetScancodeFromName</c> in the program), answering null for a name it does not know.
    /// </summary>
    public static InputSource? Parse(string name, Func<string, int?> keyCode)
    {
        var s = name.Trim();
        if (s.Length == 0) return null;
        var squeezed = s.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        switch (squeezed)
        {
            case "mouseleft": case "mouse1": return InputSource.Mouse(SdlLeft);
            case "mouseright": case "mouse2": return InputSource.Mouse(SdlRight);
            case "mousemiddle": case "mouse3": return InputSource.Mouse(SdlMiddle);
            case "mouse4": case "mouseback": case "mousex1": return InputSource.Mouse(SdlX1);
            case "mouse5": case "mouseforward": case "mousex2": return InputSource.Mouse(SdlX2);
            case "wheelup": case "mousewheelup": return InputSource.Wheel(1);
            case "wheeldown": case "mousewheeldown": return InputSource.Wheel(-1);
        }
        return keyCode(s) is { } code ? InputSource.Key(code) : null;
    }

    /// <summary>The name a source is shown by; <paramref name="keyName"/> names a key's scancode (SDL's own name in the program).</summary>
    public static string Name(InputSource source, Func<int, string> keyName) => source.Kind switch
    {
        InputSourceKind.MouseButton => source.Code switch
        {
            SdlLeft => "Mouse Left",
            SdlRight => "Mouse Right",
            SdlMiddle => "Mouse Middle",
            SdlX1 => "Mouse 4",
            SdlX2 => "Mouse 5",
            _ => $"Mouse button {source.Code}",
        },
        InputSourceKind.Wheel => source.Code > 0 ? "Wheel Up" : "Wheel Down",
        _ => keyName(source.Code),
    };
}

/// <summary>
/// The player's bindings made ready for the window's events (ours, keys shared): each key, button or wheel turn
/// to <b>every</b> action it works, in the order given. Any action may share a key with any other (<i>"some
/// commands shouldn't be mutually exclusive to the same key being bound"</i>): a press of a shared key works all of its actions
/// at once — two movement directions on one key, or anything else odd, are the player's choice, not forbidden. Two pairs keep
/// their own rules: <see cref="Cancel"/> and the other action on its key (X both crouch and cancel; one
/// press does one thing, which the simulation decides, <see cref="PlayerControlSystem"/>), and <see cref="PointerClick"/>
/// and the action beside it (the freed pointer's click on attack's Mouse Left; the program sends a press of such a source
/// to the click while the pointer is free and to its other actions while it is captured, so they never mix). A name that names
/// nothing is said and passed over.
/// </summary>
public sealed class BindingTable
{
    /// <summary>The cancel action, which shares a key with another under its own rule: one press, one thing.</summary>
    public const string Cancel = "cancel";

    /// <summary>The freed pointer's click, which shares a key with another under its own rule: the pointer decides.</summary>
    public const string PointerClick = "pointerClick";

    /// <summary>
    /// Whether <paramref name="action"/> is one of the pairs that share a key under their own rule (<see cref="Cancel"/>,
    /// <see cref="PointerClick"/>): the Controls page shares such a key without asking (any other pair is asked).
    /// </summary>
    public static bool Shares(string action) => action == Cancel || action == PointerClick;

    static readonly string[] None = [];
    readonly Dictionary<InputSource, List<string>> actions = [];
    readonly List<InputSource> sources = [];
    readonly List<(string Action, InputSource Source)> order = [];
    readonly List<string> problems = [];

    /// <summary>What was wrong with the bindings given: names of nothing.</summary>
    public IReadOnlyList<string> Problems => problems;

    /// <summary>Every action <paramref name="source"/> works, in the order given (empty when none).</summary>
    public IReadOnlyList<string> ActionsOf(InputSource source) => actions.TryGetValue(source, out var list) ? list : None;

    /// <summary>
    /// The action <paramref name="source"/> works first, if any: the first besides <see cref="Cancel"/> and
    /// <see cref="PointerClick"/>, else one of those (<see cref="ActionsOf"/> gives them all).
    /// </summary>
    public bool TryGet(InputSource source, out string action)
    {
        var list = ActionsOf(source);
        foreach (var a in list) if (!Shares(a)) { action = a; return true; }
        if (list.Count > 0) { action = list[0]; return true; }
        action = "";
        return false;
    }

    /// <summary>Whether <paramref name="source"/> works <see cref="Cancel"/> (alone or besides others).</summary>
    public bool Cancels(InputSource source) => ActionsOf(source) is var l && Contains(l, Cancel);

    /// <summary>Whether <paramref name="source"/> works <see cref="PointerClick"/> (alone or besides others).</summary>
    public bool Clicks(InputSource source) => ActionsOf(source) is var l && Contains(l, PointerClick);

    /// <summary>The sources each action keeps, in the order given.</summary>
    public IEnumerable<(string Action, InputSource Source)> Entries => order;

    /// <summary>The sources that work more than one action, each with its actions, in the order first given.</summary>
    public IEnumerable<(InputSource Source, IReadOnlyList<string> Actions)> Shared
    {
        get
        {
            foreach (var s in sources)
                if (actions[s].Count > 1) yield return (s, actions[s]);
        }
    }

    static bool Contains(IReadOnlyList<string> list, string action)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == action) return true;
        return false;
    }

    public static BindingTable Build(IEnumerable<KeyValuePair<string, string[]>> bindings, Func<string, int?> keyCode, Func<int, string> keyName)
    {
        var t = new BindingTable();
        foreach (var (action, names) in bindings)
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (BindingNames.Parse(name, keyCode) is not { } source)
                {
                    t.problems.Add($"{action} = \"{name}\" names no key, button or wheel; passed over");
                    continue;
                }
                if (!t.actions.TryGetValue(source, out var list)) { t.actions[source] = list = []; t.sources.Add(source); }
                if (list.Contains(action)) continue;
                list.Add(action);
                t.order.Add((action, source));
            }
        return t;
    }

    /// <summary>
    /// Binds <paramref name="action"/> to <paramref name="names"/> in <paramref name="bindings"/> (replacing what it had; each
    /// written by its own name, <c>Mouse2</c> as <c>Mouse Right</c>). Unless <paramref name="share"/>, each of those sources is
    /// taken off any other action that had it, as a key page moves a key — except between <see cref="Cancel"/> or
    /// <see cref="PointerClick"/> and another action, which always share it; with <paramref name="share"/>
    /// the others keep it too and both work it. Answers what was moved, or throws <see cref="ArgumentException"/> naming what is
    /// wrong (an unknown action, a name of nothing); nothing is changed then.
    /// </summary>
    public static IReadOnlyList<string> Rebind(Dictionary<string, string[]> bindings, string action, IReadOnlyList<string> names,
                                               Func<string, int?> keyCode, Func<int, string> keyName, bool share = false)
    {
        if (!bindings.ContainsKey(action))
            throw new ArgumentException($"no action \"{action}\"; the actions are {string.Join(", ", bindings.Keys)}");
        var sources = new List<InputSource>();
        var kept = new List<string>();
        foreach (var raw in names)
        {
            var name = raw.Trim();
            if (name.Length == 0) continue;
            var source = BindingNames.Parse(name, keyCode) ?? throw new ArgumentException($"\"{name}\" names no key, button or wheel");
            if (sources.Contains(source)) continue;
            sources.Add(source);
            kept.Add(BindingNames.Name(source, keyName));     // written by its own name, so the file says one thing
        }
        var moved = new List<string>();
        if (!share)
            foreach (var other in bindings.Keys.ToArray())
            {
                if (other == action || Shares(other) || Shares(action)) continue;
                var left = bindings[other].Where(n => BindingNames.Parse(n, keyCode) is not { } s || !sources.Contains(s)).ToArray();
                if (left.Length != bindings[other].Length)
                {
                    foreach (var n in bindings[other].Except(left)) moved.Add($"{n.Trim()} taken from {other}");
                    bindings[other] = left;
                }
            }
        bindings[action] = [.. kept];
        return moved;
    }

    /// <summary>
    /// The other actions that work <paramref name="source"/> in <paramref name="bindings"/> and would lose it to
    /// <paramref name="action"/> were it moved (the Controls page asks share or move for these): all but
    /// <paramref name="action"/>, leaving out the pairs that share by their own rule (<see cref="Shares"/>).
    /// </summary>
    public static IReadOnlyList<string> Takers(IReadOnlyDictionary<string, string[]> bindings, string action, InputSource source, Func<string, int?> keyCode)
    {
        var takers = new List<string>();
        if (Shares(action)) return takers;
        foreach (var (other, names) in bindings)
            if (other != action && !Shares(other) && names.Any(n => BindingNames.Parse(n, keyCode) == source)) takers.Add(other);
        return takers;
    }
}
