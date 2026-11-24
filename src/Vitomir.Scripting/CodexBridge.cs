// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Generates the engine bridge from the player's own Codex sources, into the script cache.
//
// The game's 18 `Codex*` classes declare 447 methods `native`, implemented in C++ inside
// the original engine. We rewrite each declaration into a plain Java method that forwards to
// `EngineBridge.call(key, self, args)`, keeping every signature byte for byte so the scripts
// compile and run against them unchanged. Everything else in each file — constants, helpers,
// constructors, comments — is copied verbatim.
//
// This runs at load time, from the player's install, and writes into the cache. Nothing generated
// here is checked in, and the player's files are never modified.
using System.Text;
using System.Text.RegularExpressions;

namespace Vitomir.Scripting;

public static class CodexBridge
{
    /// <summary>`public static native void Foo(int a);` in any modifier order.</summary>
    static readonly Regex Native = new(
        @"^(?<indent>[ \t]*)(?<mods>(?:(?:public|private|protected|static|final|synchronized)\s+)*)native\s+" +
        @"(?<mods2>(?:static\s+)?)(?<ret>[\w\[\]\.]+)\s+(?<name>\w+)\s*\((?<params>[^)]*)\)\s*;",
        RegexOptions.Multiline);

    static readonly Dictionary<string, string> Defaults = new()
    {
        ["boolean"] = "false", ["int"] = "0", ["float"] = "0.0f", ["double"] = "0.0", ["long"] = "0L",
        ["short"] = "(short)0", ["byte"] = "(byte)0", ["char"] = "'\\0'", ["String"] = "\"\"", ["Object"] = "null",
    };

    /// <summary>The class the engine never bridges: the original's copy-protection hook.</summary>
    public const string Excluded = "CodexSecurity.java";

    /// <summary>
    /// Writes the bridged `Codex*.java` plus `EngineBridge.java` into <paramref name="outDir"/>,
    /// reading from the player's `Codex/System`. Returns how many natives were rewritten.
    /// </summary>
    public static int Generate(string codexSystemDir, string outDir)
    {
        Directory.CreateDirectory(outDir);
        int total = 0;
        foreach (var path in Directory.GetFiles(codexSystemDir, "Codex*.java").OrderBy(p => p, StringComparer.Ordinal))
        {
            if (Path.GetFileName(path).Equals(Excluded, StringComparison.OrdinalIgnoreCase)) continue;
            total += BridgeOne(path, outDir);
        }
        File.WriteAllText(Path.Combine(outDir, "EngineBridge.java"), EngineBridgeJava, Encoding.Latin1);
        return total;
    }

    /// <summary>
    /// One `Codex*.java` bridged into <paramref name="outDir"/> under its own name: what <see cref="Generate"/> does for each, and
    /// what the shipped classes' overlay does for a mod's changed `Codex*.java`. Returns how many natives it rewrote.
    /// </summary>
    public static int BridgeOne(string path, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var file = Path.GetFileName(path);
        var text = File.ReadAllText(path, Encoding.Latin1);
        var cls = Path.GetFileNameWithoutExtension(file);
        var rewritten = Rewrite(text, cls);
        if (cls == "Codex") rewritten = AddGuidHook(rewritten);
        File.WriteAllText(Path.Combine(outDir, file), rewritten, Encoding.Latin1);
        return Native.Matches(text).Count;
    }

    /// <summary>The names of the classes this replaces, so the caller can leave the originals out.</summary>
    public static HashSet<string> ReplacedFiles(string codexSystemDir) =>
        new(Directory.GetFiles(codexSystemDir, "Codex*.java").Select(Path.GetFileName)!,
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The original allocates a script object, writes its GUID field, and only then calls the
    /// constructor, so a script's constructor can already use its own GUID — 55 of the 300 shipped
    /// ones do, <c>Door</c> among them. Java reflection cannot construct in that order, so instead
    /// we give <c>Codex</c> the constructor it never declared: every script's <c>super()</c> chain
    /// reaches it, and it runs before the script's own constructor body.
    /// </summary>
    static string AddGuidHook(string codexJava)
    {
        const string field = "protected int guid = 0;";
        int at = codexJava.IndexOf(field, StringComparison.Ordinal);
        if (at < 0) throw new InvalidOperationException("Codex.java has no `protected int guid` field to hook");
        const string ctor = """

            	/** Vitomir: the engine gives a script its GUID before its constructor body runs. */
            	public Codex() { guid = EngineBridge.pendingGuid; }
            """;
        return codexJava[..(at + field.Length)] + ctor + codexJava[(at + field.Length)..];
    }

    static string Rewrite(string source, string cls) => Native.Replace(source, m =>
    {
        string ret = m.Groups["ret"].Value, name = m.Groups["name"].Value, indent = m.Groups["indent"].Value;
        var mods = (m.Groups["mods"].Value + m.Groups["mods2"].Value).Trim();
        if (mods.Length > 0) mods += " ";
        var pars = Regex.Replace(m.Groups["params"].Value.Replace('\t', ' '), @"\s+", " ").Trim();
        var names = ParamNames(pars);
        var self = mods.Contains("static") ? "null" : "this";
        var boxed = string.Join(", ", names.Select(n => Box(n, pars)));
        var call = $"EngineBridge.call(\"{cls}.{name}\", {self}, new Object[]{{{boxed}}})";
        var dflt = Defaults.TryGetValue(ret, out var d) ? d : "null";
        var body = ret == "void" ? $"{call};" : $"return {Unbox(ret, call, dflt)};";
        return $"{indent}{mods}{ret} {name}({pars}) {{ {body} }}";
    });

    static List<string> ParamNames(string pars) =>
        [.. pars.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => p.Split(' ')[^1].Replace("[]", ""))];   // C-style `float origin[]` → origin

    /// <summary>Boxes a primitive argument. JDK 1.1 has constructors, not valueOf.</summary>
    static string Box(string name, string pars)
    {
        foreach (var p in pars.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bits = p.Split(' ');
            string type = bits[^2], n = bits[^1];
            if (n.EndsWith("[]")) { type += "[]"; n = n[..^2]; }
            if (n != name) continue;
            return type switch
            {
                "int" or "short" or "byte" => $"new Integer({name})",
                "float" => $"new Float({name})",
                "double" => $"new Double({name})",
                "long" => $"new Long({name})",
                "boolean" => $"new Boolean({name})",
                "char" => $"new Character({name})",
                _ => name,
            };
        }
        return name;
    }

    static string Unbox(string ret, string call, string dflt) => ret switch
    {
        "boolean" => $"EngineBridge.asBoolean({call}, {dflt})",
        "int" => $"EngineBridge.asInt({call}, {dflt})",
        "float" => $"EngineBridge.asFloat({call}, {dflt})",
        "String" => $"EngineBridge.asString({call}, {dflt})",
        _ => $"({ret}){call}",
    };

    /// <summary>
    /// The dispatcher every bridged method calls. Its `host` field is a `java.util.Dictionary`
    /// on purpose: runtime-compiled Java cannot see .NET types, but it can call a C# object
    /// through a Java base class, so <see cref="CodexHost"/> subclasses that.
    /// </summary>
    /// <summary>The dispatcher's source, for the shipped classes' build too (probe-classes).</summary>
    public static string EngineBridgeSource => EngineBridgeJava;

    const string EngineBridgeJava = """
        /** Engine bridge dispatcher. Generated by Vitomir from the player's Codex sources — do not edit.
         *  Every former-native Codex method calls EngineBridge.call(key, self, args), which forwards to
         *  the host the engine installs (a java.util.Dictionary whose get() receives {key, self, args}).
         *  With no host installed the call is logged instead, which is what the script tests use. */
        import java.util.Dictionary;
        import java.util.Vector;
        public final class EngineBridge {
            public static Dictionary host = null;            // installed by the engine at startup
            public static int pendingGuid = 0;               // the GUID of the script being constructed
            public static final Vector log = new Vector();   // used when no host is installed
            public static Object chronicleScript = null;     // set before scripts run, in log mode
            public static boolean echo = false;
            public static Object call(String key, Object self, Object[] args) {
                if (host != null) return host.get(new Object[]{key, self, args});
                StringBuffer sb = new StringBuffer(key);
                sb.append("(");
                for (int i = 0; i < args.length; i++) { if (i > 0) sb.append(", "); sb.append(String.valueOf(args[i])); }
                sb.append(")");
                log.addElement(sb.toString());
                if (echo) System.out.println("  [engine] " + sb.toString());
                if (key.equals("Codex.GetChronicleScript")) return chronicleScript;
                if (key.equals("CodexThing.GuidFromCastID")) return new Integer(Math.abs(String.valueOf(args[0]).hashCode()) % 100000 + 1);
                return null;
            }
            public static boolean asBoolean(Object o, boolean d) { return o instanceof Boolean ? ((Boolean)o).booleanValue() : d; }
            public static int asInt(Object o, int d) { return o instanceof Integer ? ((Integer)o).intValue() : d; }
            public static float asFloat(Object o, float d) { return o instanceof Float ? ((Float)o).floatValue() : d; }
            public static String asString(Object o, String d) { return o instanceof String ? (String)o : d; }
        }
        """;
}
