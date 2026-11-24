// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Every word on a menu in our own faces (ours;:
// "let's go ahead and replace all of the menu items with our new font so it looks consistent").
using System.Text;
using Vitomir.Formats;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **The text of a pane in our lettering** (ours): what was left in 1.1's bitmap faces — a list's rows, a text
/// box's lines, a key's caption — drawn in our own faces instead. Two kinds of text (*"Option B"*):
/// <list type="bullet">
/// <item>**a name** (a row, a heading, a key, a value, a one-line box) in **Vitomir Uncial**, a capital in Vitomir
/// Capitals (<see cref="MenuLettering.Runs"/>), as 1.1's own menu words are written;</item>
/// <item>**a sentence** (a box that wraps its lines: a hint, a description) in **Vitomir Book**, our text face, as 1.1
/// wrote its sentences in a plainer face than its menu words.</item>
/// </list>
/// A control can say which it is with <c>face=name|sentence</c> (a key of ours, which no file of 1.1's has); otherwise a
/// box that wraps is a sentence and everything else a name. The layout a click is tested against is 1.1's and is not
/// changed: only how the words are drawn in it.
/// </summary>
public static class MenuText
{
    public enum Kind { Name, Sentence }

    /// <summary>The key that says which a control's text is, and its two words.</summary>
    public const string FaceKey = "face", NameWord = "name", SentenceWord = "sentence";

    /// <summary>
    /// The ems against 1.1's <c>defaultFont10</c>, its lists' and boxes' face (a control in a larger face of 1.1's is
    /// scaled by its line height): a name's uncial x-height is 5.75 units, as 1.1's panel labels are
    /// (<see cref="MenuLettering.PanelEm"/>); a sentence's book x-height a little under 5, its lines an em apart.
    /// </summary>
    public const float NameEm = 11.5f, SentenceEm = 10.5f;

    /// <summary>How far apart a sentence's lines are, in its ems.</summary>
    public const float SentencePitch = 1.0f;

    /// <summary>
    /// Where a line's middle is, in ems above its baseline, for setting a one-line box's text in the middle of it: half
    /// way between the baseline and a little over the x-height, so a row of capitals and lower case sits level with the
    /// check box beside it and a tail hangs below (<i>"The text isn't lined up with the checkbox"</i>).
    /// </summary>
    public const float NameMiddle = 0.28f, SentenceMiddle = 0.27f;

    public static Kind KindOf(NuiElement e) => e.Get(FaceKey)?.Trim().ToLowerInvariant() switch
    {
        NameWord => Kind.Name,
        SentenceWord => Kind.Sentence,
        _ => e.Type == "textbox" && NuiPane.TextBox(e).HasFlag(NuiPane.TextBoxFlags.WordWrap) ? Kind.Sentence : Kind.Name,
    };

    public static float EmOf(Kind kind) => kind == Kind.Name ? NameEm : SentenceEm;

    public static float MiddleOf(Kind kind) => kind == Kind.Name ? NameMiddle : SentenceMiddle;

    /// <summary>A run of a line, its face and its em.</summary>
    public readonly record struct Run(string Text, Lettering Face, float Em);

    /// <summary>A line cut into what each face draws: a name's lower case and capitals; a sentence whole, in the book face.</summary>
    public static IEnumerable<Run> Runs(Kind kind, string text, float em)
    {
        if (kind == Kind.Sentence)
        {
            var book = Lettering.Load(Lettering.Book);
            yield return new Run(Spelled(book, text), book, em);
            yield break;
        }
        var uncial = Lettering.Load(Lettering.Uncial);
        var capitals = Lettering.Load(Lettering.Capitals);
        foreach (var (run, caps) in MenuLettering.Runs(Spelled(uncial, capitals, text)))
            yield return caps ? new Run(run, capitals, em * MenuLettering.CapitalScale) : new Run(run, uncial, em);
    }

    public static float Measure(Kind kind, string text, float em) => Runs(kind, text, em).Sum(r => r.Face.Measure(r.Text, r.Em));

    /// <summary>How far one character moves the pen (for breaking a sentence's lines).</summary>
    public static float Advance(Kind kind, char c, float em) => Measure(kind, c.ToString(), em);

    /// <summary>A sentence's lines: 1.1's breaking (<see cref="TextBoxText"/>), measured in our face (in hundredths of a unit).</summary>
    public static List<string> Lines(Kind kind, string text, bool wrap, float width, float em) =>
        TextBoxText.Lines(text, wrap, (int)(width * 100f), c => (int)MathF.Round(Advance(kind, c, em) * 100f));

    // ---- what a face does not have ----------------------------------------------------------------------------------

    /// <summary>Letters no face draws as one, written as their plain letters.</summary>
    static readonly Dictionary<char, string> Spelt = new()
    {
        ['Æ'] = "AE", ['Œ'] = "OE", ['Ð'] = "D", ['Þ'] = "TH", ['ß'] = "ss", ['æ'] = "ae", ['œ'] = "oe",
        ['‘'] = "'", ['’'] = "'", ['“'] = "\"", ['”'] = "\"", ['–'] = "-", ['—'] = "-", ['…'] = "...",
    };

    /// <summary>Text as a face can draw it: a character it lacks as its plain letters, an accented one as its letter.</summary>
    public static string Spelled(Lettering face, string text) => Spelled(face, null, text);

    static string Spelled(Lettering face, Lettering? capitals, string text)
    {
        bool Has(char c) => char.IsUpper(c) && capitals is not null ? capitals[c] is not null : face[c] is not null;
        if (text.All(Has)) return text;
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (Has(c)) { sb.Append(c); continue; }
            if (Spelt.TryGetValue(c, out var plain) && plain.All(Has)) { sb.Append(plain); continue; }
            string bare = c.ToString().Normalize(NormalizationForm.FormD);
            if (bare.Length > 0 && Has(bare[0])) { sb.Append(bare[0]); continue; }
            if (char.IsUpper(c) && capitals is not null && face[char.ToLowerInvariant(c)] is not null) { sb.Append(char.ToLowerInvariant(c)); continue; }
            sb.Append(Has('?') ? '?' : ' ');
        }
        return sb.ToString();
    }
}
