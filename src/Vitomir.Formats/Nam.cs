// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text;

namespace Vitomir.Formats;

/// <summary>One explicit render pass of a NAM material: <c>pass &lt;n&gt; [nomip] &lt;texture&gt; &lt;blend&gt;</c>.</summary>
/// <param name="Texture">A TGA name, or <c>$lightmap</c> / <c>$lightadd</c>.</param>
public sealed record NamPass(int Number, bool NoMip, string Texture, string Blend);

/// <summary>
/// NAM material (text, spec: NodSDK/Docs/nam.htm): the texture(s) and render behaviour of a surface
/// material. A TGA without a NAM behaves as <c>texture &lt;name&gt;.tga</c> with defaults.
/// Keywords are case-insensitive and whitespace-separated. The 1.1 engine knows 22 keywords: the
/// SDK's 18 plus <c>nofilter</c>, <c>alpha</c>, <c>constalpha</c> (parsed, no effect) and <c>nosmooth</c>.
/// Lines starting with ';' or '#' are comments.
/// </summary>
public sealed class NamMaterial
{
    /// <summary>Frame textures (several for page-flipped animation).</summary>
    public List<string> Textures { get; } = [];
    public int NumFrames { get; private set; }
    public bool Masked { get; private set; }
    public bool FullyLit { get; private set; }
    public bool AddBlend { get; private set; }
    public bool SubBlend { get; private set; }
    public bool TwoSided { get; private set; }
    public bool Specular { get; private set; }
    public bool EnvMap { get; private set; }
    /// <summary>Accepted by the 1.1 parser but ignored (no effect in the engine).</summary>
    public bool NoFilter { get; private set; }
    /// <summary>Accepted by the 1.1 parser but ignored (no effect in the engine).</summary>
    public bool Alpha { get; private set; }
    /// <summary>Accepted by the 1.1 parser but ignored (no effect in the engine).</summary>
    public bool ConstAlpha { get; private set; }
    public bool NoSmooth { get; private set; }
    /// <summary>Surface sound type (Carpet, Dirt, Metal, Wet, Snow, Stone, Street, Wood, Creaky, Silent); default Stone.</summary>
    public string Sound { get; private set; } = "stone";
    public string? Glow { get; private set; }
    public int? GlowRamp { get; private set; }
    public int? PageFlipMs { get; private set; }
    public int? Flicker { get; private set; }
    public string? Detail { get; private set; }
    public float? DetailScale { get; private set; }
    public List<NamPass> Passes { get; } = [];
    /// <summary>UV slide per pass, in texels per second (see the SDK doc).</summary>
    public Dictionary<int, (float Du, float Dv)> Slides { get; } = [];
    /// <summary>Lines with an unknown keyword, kept for diagnostics.</summary>
    public List<string> Unknown { get; } = [];

    /// <summary>The implied material of a TGA that has no NAM file.</summary>
    public static NamMaterial Implied(string textureName)
    {
        var m = new NamMaterial();
        m.Textures.Add(textureName);
        return m;
    }

    public static NamMaterial Read(byte[] data, string name = "<nam>") => Parse(Encoding.Latin1.GetString(data));

    public static NamMaterial Parse(string text)
    {
        var m = new NamMaterial();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            var t = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            string key = t[0].ToLowerInvariant();
            string Arg(int i) => i < t.Length ? t[i] : "";
            int? IntArg(int i) => int.TryParse(Arg(i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
            switch (key)
            {
                case "texture": if (t.Length > 1) m.Textures.Add(t[1]); break;
                case "numframes": m.NumFrames = IntArg(1) ?? 0; break;
                case "masked": m.Masked = true; break;
                case "fullylit": m.FullyLit = true; break;
                case "addblend": m.AddBlend = true; break;
                case "subblend": m.SubBlend = true; break;
                case "twosided": m.TwoSided = true; break;
                case "specular": m.Specular = true; break;
                case "envmap": m.EnvMap = true; break;
                case "nofilter": m.NoFilter = true; break;
                case "alpha": m.Alpha = true; break;
                case "constalpha": m.ConstAlpha = true; break;
                case "nosmooth": m.NoSmooth = true; break;
                case "sound": m.Sound = Arg(1).ToLowerInvariant(); break;
                case "glow": m.Glow = Arg(1); break;
                case "glowramp": m.GlowRamp = IntArg(1); break;
                case "pageflip": m.PageFlipMs = IntArg(1); break;
                case "flicker": m.Flicker = IntArg(1); break;
                case "detail": m.Detail = Arg(1); break;
                case "detailscale":
                    m.DetailScale = float.TryParse(Arg(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var ds) ? ds : null;
                    break;
                case "pass":
                {
                    int i = 2; bool nomip = false;
                    if (Arg(i).Equals("nomip", StringComparison.OrdinalIgnoreCase)) { nomip = true; i++; }
                    m.Passes.Add(new NamPass(IntArg(1) ?? m.Passes.Count, nomip, Arg(i), Arg(i + 1).ToLowerInvariant()));
                    break;
                }
                case "slide":
                {
                    var rest = string.Join("", t.Skip(2)).Trim('(', ')');
                    var uv = rest.Split(',');
                    if (IntArg(1) is { } pass && uv.Length == 2
                        && float.TryParse(uv[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var du)
                        && float.TryParse(uv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var dv))
                        m.Slides[pass] = (du, dv);
                    else m.Unknown.Add(line);
                    break;
                }
                default: m.Unknown.Add(line); break;
            }
        }
        return m;
    }
}
