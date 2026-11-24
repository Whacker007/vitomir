// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Host;

/// <summary>
/// One <c>--armor &lt;who&gt;=&lt;piece&gt;[,&lt;piece&gt;…]</c>: who (as <c>--sheet</c> names people: <c>player</c>,
/// <c>Christof</c>, a shown name, a 1.1 template or <c>e&lt;index&gt;</c>), the pieces of the rules module's armour it wears in
/// place of what it wore (<c>none</c>, or nothing after the <c>=</c>, undresses it), and the text as given. A recording carries
/// the texts (<see cref="SessionOptions.ArmorNote"/>), so a replay dresses the same people the same.
/// </summary>
public sealed record ArmorRequest(string Who, IReadOnlyList<string> Pieces, string Text)
{
    /// <summary>Reads <c>who=piece,piece</c>; false with the reason.</summary>
    public static bool TryParse(string text, out ArmorRequest request, out string error)
    {
        request = new ArmorRequest("", [], text);
        error = "";
        int eq = text.IndexOf('=');
        string who = eq < 0 ? "" : text[..eq].Trim();
        if (eq <= 0 || who.Length == 0 || text.Contains(';'))
        {
            error = $"--armor '{text}': expected <who>=<piece>[,<piece>...] (player=mail_shirt,light_helm, Guard=plate_harness,full_helm, Guard=none)";
            return false;
        }
        var pieces = text[(eq + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pieces.Length == 1 && pieces[0].Equals("none", StringComparison.OrdinalIgnoreCase)) pieces = [];
        request = new ArmorRequest(who, pieces, text);
        return true;
    }

    /// <summary>The requests as a tape's note (the texts, joined by ';').</summary>
    public static string ToNote(IReadOnlyList<ArmorRequest> requests) => string.Join(";", requests.Select(r => r.Text));

    /// <summary>The requests a tape's note gives.</summary>
    public static IReadOnlyList<ArmorRequest> FromNote(string note) =>
        [.. note.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(t => TryParse(t, out var r, out string e) ? r
            : throw new SessionException($"the recording's {SessionOptions.ArmorNote} note is not readable: {e}"))];
}

public sealed partial class Session
{
    /// <summary>
    /// The armour asked for (<c>--armor</c>): each who found (<see cref="Who"/>) dressed in the pieces given through the
    /// rules module's <see cref="IPersonSheets.TryWear"/> (after the sheets and the powers: a sheet given whole replaces what was
    /// worn); a refusal stops the start with the module's reason (the pieces it has named).
    /// </summary>
    void GiveArmor()
    {
        if (options.Armor.Count == 0) return;
        if (!world.TryService<IPersonSheets>(out var route)) throw new SessionException($"--armor: no module of game {modules.Game.Id} keeps sheets");
        foreach (var request in options.Armor)
            foreach (var e in Who("--armor", request.Who))
            {
                if (!route.TryWear(e, request.Pieces, out string error)) throw new SessionException($"--armor {request.Text}: {error}");
                options.Say($"armor: {NameOf(e.Packed) ?? e.ToString()} (e{e.Index}) wears {(request.Pieces.Count == 0 ? "nothing" : string.Join(", ", request.Pieces))}");
            }
    }
}
