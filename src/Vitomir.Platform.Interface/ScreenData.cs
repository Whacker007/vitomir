// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text;

namespace Vitomir.Platform.Interface;

/// <summary>
/// The data a screen is bound to (bound to the game's data, not written into the
/// code): named values — words, numbers, yes or no — and named lists of items, each item data of its own. A piece
/// names what it shows (<c>"{label}"</c> in its words, <c>"bind": "sensitivity"</c>); inside a list, a name is looked
/// up in the item first and then in the screen's data. The host fills it and reads back what the player changed.
/// </summary>
public sealed class ScreenData
{
    readonly Dictionary<string, object?> values = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<ScreenData>> lists = new(StringComparer.Ordinal);

    public ScreenData(ScreenData? parent = null) { Parent = parent; }

    /// <summary>The data a list's item looks in when it does not have a name itself.</summary>
    public ScreenData? Parent { get; }

    /// <summary>Counts every change, so a screen knows when to lay itself out again.</summary>
    public int Version => version + (Parent?.Version ?? 0);
    int version;

    public ScreenData Set(string name, object? value)
    {
        if (!values.TryGetValue(name, out var was) || !Equals(was, value))
        {
            values[name] = value;
            version++;
        }
        return this;
    }

    /// <summary>
    /// Sets a name in the data that holds it (this item's, or the screen's above it), or here when none does: what a worked piece
    /// in a list's row writes, so a name the row's item has stays the item's and one the screen has stays the screen's.
    /// </summary>
    public ScreenData SetWhere(string name, object? value)
    {
        for (var d = this; d is not null; d = d.Parent)
            if (d.values.ContainsKey(name)) return d.Set(name, value);
        return Set(name, value);
    }

    public bool TryGet(string name, out object? value)
    {
        for (var d = this; d is not null; d = d.Parent)
            if (d.values.TryGetValue(name, out value)) return true;
        value = null;
        return false;
    }

    public string Text(string name) => TryGet(name, out var v) ? Format(v) : "";

    public float Number(string name) => TryGet(name, out var v) ? v switch
    {
        float f => f,
        double d => (float)d,
        int i => i,
        string s when float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) => p,
        _ => 0f,
    } : 0f;

    /// <summary>Whether a name is set and true (a word that is not empty counts as true).</summary>
    public bool Flag(string name) => TryGet(name, out var v) && v switch
    {
        bool b => b,
        string s => s.Length > 0,
        null => false,
        _ => true,
    };

    /// <summary>A list's items (made empty the first time it is asked for).</summary>
    public List<ScreenData> Items(string name)
    {
        for (var d = this; d is not null; d = d.Parent)
            if (d.lists.TryGetValue(name, out var found)) return found;
        var made = new List<ScreenData>();
        lists[name] = made;
        return made;
    }

    /// <summary>A new item at the end of a list, its names looked up here when it has not got them.</summary>
    public ScreenData Add(string list)
    {
        var item = new ScreenData(this);
        Items(list).Add(item);
        version++;
        return item;
    }

    /// <summary>Empties a list.</summary>
    public void Clear(string list)
    {
        Items(list).Clear();
        version++;
    }

    /// <summary>
    /// <paramref name="template"/> with each <c>{name}</c> replaced by that value's words (<c>{{</c> for a brace);
    /// a name not set gives nothing.
    /// </summary>
    public string Fill(string template)
    {
        int open = template.IndexOf('{');
        if (open < 0) return template;
        var sb = new StringBuilder(template.Length + 16);
        int i = 0;
        while (i < template.Length)
        {
            char c = template[i];
            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{') { sb.Append('{'); i += 2; continue; }
            if (c == '{')
            {
                int close = template.IndexOf('}', i + 1);
                if (close < 0) { sb.Append(template, i, template.Length - i); break; }
                sb.Append(Text(template[(i + 1)..close].Trim()));
                i = close + 1;
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    static string Format(object? v) => v switch
    {
        null => "",
        string s => s,
        float f => f.ToString("0.##", CultureInfo.InvariantCulture),
        bool b => b ? "On" : "Off",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };
}
