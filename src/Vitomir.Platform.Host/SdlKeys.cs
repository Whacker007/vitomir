// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using Vitomir.Platform.Control;
using static SDL.SDL3;

namespace Vitomir.Platform.Host;

/// <summary>The platform's binding names (<see cref="BindingNames"/>) with SDL's key names behind them (ours).</summary>
public static class SdlKeys
{
    /// <summary>A key's scancode by SDL's name for it, or null when SDL knows no such key.</summary>
    public static int? Code(string name)
    {
        var code = SDL_GetScancodeFromName(name);
        return code == SDL_Scancode.SDL_SCANCODE_UNKNOWN ? null : (int)code;
    }

    /// <summary>SDL's name for a key's scancode.</summary>
    public static string Name(int scancode) => SDL_GetScancodeName((SDL_Scancode)scancode) is { Length: > 0 } n ? n : $"key {scancode}";

    public static BindingTable Table(IEnumerable<KeyValuePair<string, string[]>> bindings) => BindingTable.Build(bindings, Code, Name);

    /// <summary>The bindings in force, one line: each action with what works it, or "none".</summary>
    public static string Describe(BindingTable table, IEnumerable<string>? actions = null)
    {
        var byAction = table.Entries.GroupBy(e => e.Action).ToDictionary(g => g.Key, g => string.Join(" or ", g.Select(e => BindingNames.Name(e.Source, Name))));
        var names = actions ?? byAction.Keys;
        return string.Join(", ", names.Select(a => $"{a} {(byAction.TryGetValue(a, out var k) ? k : "none")}"));
    }

    /// <summary>
    /// The keys that work more than one action, one line: "C works crouch and cancel; Mouse Left works attack and
    /// pointerClick"; empty when none.
    /// </summary>
    public static string DescribeShared(BindingTable table) =>
        string.Join("; ", table.Shared.Select(s => $"{BindingNames.Name(s.Source, Name)} works {Words(s.Actions)}"));

    static string Words(IReadOnlyList<string> actions) =>
        actions.Count <= 1 ? string.Join("", actions) : string.Join(", ", actions.Take(actions.Count - 1)) + " and " + actions[^1];

    /// <summary>
    /// <c>--bind action=key[,key]</c>, as often as wanted, applied to the settings in order and saved (ours, until
    /// the Controls page): an action given nothing is unbound; a key given to an action is taken off any other — unless it is
    /// written <c>+key</c>: then it is added to what the action has and shared, the others keeping it too. Says
    /// what moved and the bindings in force (<c>--bindings</c> alone only says them). Exit code 0, or 2 when a binding
    /// is wrong (then nothing is saved).
    /// </summary>
    public static int BindCommand(string[] args, PlatformSettings settings, string settingsPath, Action<string> say)
    {
        var bindings = new Dictionary<string, string[]>(settings.Bindings, StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] != "--bind") continue;
            if (i + 1 >= args.Length || args[i + 1].IndexOf('=') is var eq && eq <= 0)
            {
                say("--bind wants action=key[,key], for example --bind \"jump=Mouse Right,Space\" (an empty right side unbinds)");
                return 2;
            }
            var spec = args[++i];
            var action = spec[..eq].Trim();
            if (action == PlatformSettings.RetiredSwapShoulder)
            {
                say("--bind: swapShoulder is gone: switchView cycles first person, the right shoulder and the left; nothing saved");
                return 2;
            }
            var keys = spec[(eq + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            try
            {
                // Keys shared: a key written +key is added to what the action has and shared (the others keep it too);
                // keys written plainly replace what it had and are moved (taken off the others), as before.
                var plain = keys.Where(k => !k.StartsWith('+')).ToList();
                var plus = keys.Where(k => k.StartsWith('+')).Select(k => k[1..].Trim()).Where(k => k.Length > 0).ToList();
                if (plain.Count > 0 || plus.Count == 0)
                    foreach (var moved in BindingTable.Rebind(bindings, action, plain, Code, Name)) say($"bindings: {moved}");
                if (plus.Count > 0)
                {
                    var had = bindings.TryGetValue(action, out var h) ? h : [];
                    BindingTable.Rebind(bindings, action, [.. had, .. plus], Code, Name, share: true);
                    foreach (var k in plus)
                        if (BindingNames.Parse(k, Code) is { } src && Table(bindings).ActionsOf(src) is { Count: > 1 } all)
                            say($"bindings: {BindingNames.Name(src, Name)} shared: it works {Words(all)}");
                }
            }
            catch (ArgumentException e)
            {
                say($"bindings: {e.Message}; nothing saved");
                return 2;
            }
        }
        bool changed = Array.IndexOf(args, "--bind") >= 0;
        if (changed)
        {
            settings.Bindings = bindings;
            settings.Save(settingsPath);
        }
        var table = Table(bindings);
        foreach (var problem in table.Problems) say($"bindings: {problem}");
        say(changed ? $"bindings saved to {settingsPath}:" : $"bindings in {settingsPath}:");
        foreach (var action in bindings.Keys) say($"  {action,-13} {Describe(table, [action])[(action.Length + 1)..]}");
        return 0;
    }
}
