// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The chronicle's metafile: the extra files and templates a chronicle needs (NodSDK\Docs\nmf.htm).
namespace Vitomir.Formats;

/// <summary>
/// A <c>.nmf</c>: one keyword per line and its arguments. The game loads <c>game.nls</c>,
/// <c>system.nls</c> and the four default <c>.not</c> files itself, so these are the additions.
/// </summary>
public sealed class NmfFile
{
    /// <summary>The template the player starts as (<c>Christof</c>).</summary>
    public string Player { get; private set; } = "";
    /// <summary>The default speaker in conversations.</summary>
    public string Speaker { get; private set; } = "";
    /// <summary>String tables, voice-over, subtitle and speech files this chronicle adds.</summary>
    public List<string> Nls { get; } = [];
    public List<string> Nvo { get; } = [];
    public List<string> Nss { get; } = [];
    public List<string> Ntx { get; } = [];
    public List<string> Not { get; } = [];
    /// <summary>Templates dropped as cash when an enemy dies.</summary>
    public List<string> Cash { get; } = [];
    public List<string> Ignored { get; } = [];

    public static NmfFile Read(ReadOnlySpan<byte> data, string fileName)
    {
        var f = new NmfFile();
        foreach (var raw in System.Text.Encoding.Latin1.GetString(data).Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
            var words = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2) continue;
            var rest = words[1..];
            switch (words[0].ToUpperInvariant())
            {
                case "PLAYER": f.Player = rest[0]; break;
                case "SPEAKER": f.Speaker = rest[0]; break;
                case "NLS": f.Nls.AddRange(rest); break;
                case "NVO": f.Nvo.AddRange(rest); break;
                case "NSS": f.Nss.AddRange(rest); break;
                case "NTX": f.Ntx.AddRange(rest); break;
                case "NOT": f.Not.AddRange(rest); break;
                case "CASH": f.Cash.AddRange(rest); break;
                default: f.Ignored.Add(line.Trim()); break;
            }
        }
        return f;
    }
}
