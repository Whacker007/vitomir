// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// **The join checks the build and the data** (ours; for an internet-ready network game): a friend's machine with
// another Vitomir build, or other 1.1 data, plays a different game from the host's — a mirror that does not match the
// world it is sent — so the Hello says which build and which data it runs, and the host refuses a difference with a line
// that says which differs.
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace Vitomir.Engine.Net;

/// <summary>
/// **Which Vitomir and which 1.1 data a machine plays** (ours). <see cref="Build"/>: the build of the code that plays
/// the network game — SHA-256 over what the engine's, the classic game's, the scripts' and the formats' assemblies' code is
/// (<see cref="CodeOf(Stream)"/>: their metadata and IL, not how they were built), sixteen hex digits — so two machines
/// running the same code agree whichever program they run it under (<c>vitomir-platform --game classic</c>, or the
/// old program while there was one) and wherever it was built, and other code (or the same code built as Release
/// rather than Debug, whose IL differs) differs; <see cref="BuildName"/> the program's version line, for the reader.
/// <see cref="Data"/>: the data's fingerprint (<see cref="Vfs.GameFiles.Fingerprint"/>), with its count of files and the
/// store the install came from (<see cref="Vfs.GameFiles.Store"/>), for the reader. <see cref="Modules"/>: the rules modules on
/// (<see cref="Vfs.GameFiles.RulesModules"/>, in load order, <c>id version hash</c>), which must be the same on every machine; the look
/// modules (textures, sounds, text, films) are each player's own and are neither in the fingerprint nor compared.
/// </summary>
public sealed record NetIdentity(string Build, string BuildName, string Data, int DataFiles, string Store)
{
    /// <summary>
    /// The rules modules on, in load order (<c>id version hash</c>); empty with none. On the wire they follow the data's fingerprint in
    /// its field (<c>fingerprint|a 1.0.0 hash;b …</c>), so a Hello of a machine with none is as it always was.
    /// </summary>
    public IReadOnlyList<string> Modules { get; init; } = [];

    /// <summary>What separates the fingerprint from the modules, and the modules from each other, in the Hello's data field.</summary>
    const char ModulesMark = '|', ModuleMark = ';';

    /// <summary>
    /// **The rules this machine resolved** (ours: the rules files' numbers, <see cref="Engine.Rules.GameRules"/>): empty when they are 1.1's,
    /// else their short hash, a colon and the keys that differ from 1.1's, comma separated (<c>3f2a…:Combat.CriticalMargin</c>). On the
    /// wire they follow the data and the modules in its field after <see cref="RulesMark"/>, so a Hello with 1.1's rules is as it was.
    /// The rules modules' check covers a module's own <c>rules.cfg</c> (its files are in its hash); this covers the Classic game's
    /// own copy, which a player may have edited.
    /// </summary>
    public string Rules { get; init; } = "";

    /// <summary>What comes before the rules in the Hello's data field.</summary>
    const char RulesMark = '^';

    /// <summary>The identity's <see cref="Rules"/> of a set of rules: empty for 1.1's.</summary>
    public static string RulesOf(Engine.Rules.GameRules rules) =>
        rules.IsDefault ? "" : rules.ShortHash() + ":" + string.Join(',', rules.Changed().Select(k => k.Id));

    /// <summary>
    /// What a test's host and player both are when nobody says (every field empty): the check passes between two such,
    /// as it does between two machines of one build and one install.
    /// </summary>
    public static readonly NetIdentity Unstated = new("", "", "", 0, "");

    /// <summary>The program's version line, as the program sets it (the platform's, or <c>vitomir-tools</c>'): for the join's line.</summary>
    public static string ProgramName { get; set; } = EntryVersion();

    /// <summary>The running program's version line: the platform's (<c>PlatformVersion.Text</c>) under it, else the engine's own (<c>FrontEnd.Version</c>).</summary>
    static string EntryVersion()
    {
        try
        {
            if (Assembly.GetEntryAssembly()?.GetType("Vitomir.Platform.Host.PlatformVersion")?.GetField("Text")?.GetValue(null) is string line) return line;
        }
        catch (Exception e) when (e is TypeLoadException or FileNotFoundException or BadImageFormatException) { }
        return Ui.FrontEnd.Version;
    }

    /// <summary>This build (<see cref="Build"/>), worked out once.</summary>
    public static string ThisBuild => thisBuild ??= MakeBuild();
    static string? thisBuild;

    /// <summary>The four assemblies whose code makes <see cref="Build"/>, in the order they are hashed.</summary>
    public static readonly string[] JoinAssemblies = ["Vitomir.Engine", "Vitomir.Classic", "Vitomir.Scripting", "Vitomir.Formats"];

    static string MakeBuild()
    {
        var codes = new List<(string Name, string? Code)>();
        foreach (var name in JoinAssemblies)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
            if (assembly is null)
                try { assembly = Assembly.Load(name); }
                catch (Exception e) when (e is FileNotFoundException or FileLoadException or BadImageFormatException) { }
            codes.Add((name, assembly is null ? null : CodeOf(assembly)));
        }
        return BuildOf(codes);
    }

    /// <summary>
    /// The code of a loaded assembly (<see cref="CodeOf(Stream)"/>, read from its file); an assembly with no file (loaded from
    /// bytes) by its module version instead, which only a build of the same bytes shares.
    /// </summary>
    static string CodeOf(Assembly assembly)
    {
        if (assembly.Location is { Length: > 0 } path && File.Exists(path))
            try
            {
                using var file = File.OpenRead(path);
                return CodeOf(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException) { }
        return "mvid:" + assembly.ManifestModule.ModuleVersionId.ToString("N");
    }

    /// <summary>
    /// <see cref="Build"/> from the join assemblies' names and code hashes (<see cref="CodeOf(Stream)"/>; null: absent):
    /// SHA-256 over <c>name=code</c> lines, sixteen hex digits.
    /// </summary>
    public static string BuildOf(IEnumerable<(string Name, string? Code)> codes)
    {
        var text = new StringBuilder();
        foreach (var (name, code) in codes) text.Append(name).Append('=').Append(code ?? "absent").Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    /// <summary>
    /// **What an assembly's code is, not how it was built** (ours): SHA-256, as hex, over its metadata and its IL as
    /// <c>System.Reflection.Metadata</c> reads them from the file <paramref name="image"/> — the assembly's name,
    /// version, culture and public key; its module's name; each assembly it references by name, version, culture and public
    /// key or token; every other metadata table row by row (a method's and a field's RVA left out); the string, blob and
    /// user-string heaps whole and the GUID heap but the module's own ids; each method body (its header, IL and exception
    /// clauses); each field's initial data; and each embedded resource by its name and content. Left out, as what the build
    /// rather than the code decides: the module version id (an MVID, which a deterministic compiler derives from the whole
    /// image, the debug information with it, so a referenced assembly's new MVID gives the referencing one a new MVID), the
    /// PE and COFF headers (the time stamp, the checksum), the debug directory (the PDB's id, checksum and path), the
    /// strong-name signature and the Win32 resources (made from the assembly's attributes, which are in). So two builds of
    /// the same source agree in any folder, from the private tree or from its public copy, however their references were
    /// made (a fresh IKVM conversion of the script compiler gives Vitomir.Scripting a new MVID and the same code), and a
    /// change to a line of code, a string or a resource gives another hash.
    /// </summary>
    public static string CodeOf(Stream image)
    {
        using var pe = new PEReader(image,
            PEStreamOptions.LeaveOpen | PEStreamOptions.PrefetchEntireImage);
        var md = pe.GetMetadataReader();
        var meta = pe.GetMetadata().GetContent().AsSpan();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Text(string s) { hash.AppendData(Encoding.UTF8.GetBytes(s)); hash.AppendData([0]); }
        void Bytes(BlobHandle b) { var bytes = md.GetBlobBytes(b); Text(bytes.Length.ToString(CultureInfo.InvariantCulture)); hash.AppendData(bytes); }

        if (md.IsAssembly)
        {
            var a = md.GetAssemblyDefinition();
            Text("assembly"); Text(md.GetString(a.Name)); Text(a.Version.ToString()); Text(md.GetString(a.Culture)); Bytes(a.PublicKey);
            Text(((int)a.Flags).ToString(CultureInfo.InvariantCulture));
        }
        var module = md.GetModuleDefinition();
        Text("module"); Text(md.GetString(module.Name)); Text(module.Generation.ToString(CultureInfo.InvariantCulture));
        foreach (var handle in md.AssemblyReferences)
        {
            var r = md.GetAssemblyReference(handle);
            Text("reference"); Text(md.GetString(r.Name)); Text(r.Version.ToString()); Text(md.GetString(r.Culture)); Bytes(r.PublicKeyOrToken);
        }

        // Every other table, row by row: the rows hold indices into the heaps (hashed below) and into other tables.
        for (int t = 0; t < MetadataTokens.TableCount; t++)
        {
            var table = (TableIndex)t;
            if (table is TableIndex.Module or TableIndex.AssemblyRef) continue;
            int rows = md.GetTableRowCount(table);
            if (rows == 0) continue;
            int size = md.GetTableRowSize(table), at = md.GetTableMetadataOffset(table);
            int skip = table is TableIndex.MethodDef or TableIndex.FieldRva ? 4 : 0;   // the RVA, first in both
            Text("table " + t.ToString(CultureInfo.InvariantCulture) + " " + rows.ToString(CultureInfo.InvariantCulture));
            for (int row = 0; row < rows; row++) hash.AppendData(meta.Slice(at + row * size + skip, size - skip));
        }

        foreach (var heap in new[] { HeapIndex.String, HeapIndex.Blob, HeapIndex.UserString })
        {
            Text("heap " + ((int)heap).ToString(CultureInfo.InvariantCulture));
            hash.AppendData(meta.Slice(md.GetHeapMetadataOffset(heap), md.GetHeapSize(heap)));
        }
        // The GUID heap but the module's own (its MVID and the edit-and-continue ids).
        var own = new HashSet<int>();
        foreach (var g in new[] { module.Mvid, module.GenerationId, module.BaseGenerationId })
            if (!g.IsNil) own.Add(MetadataTokens.GetHeapOffset(g));
        int guids = md.GetHeapSize(HeapIndex.Guid) / 16, guidAt = md.GetHeapMetadataOffset(HeapIndex.Guid);
        Text("guids");
        for (int g = 1; g <= guids; g++)
            if (!own.Contains(g)) hash.AppendData(meta.Slice(guidAt + (g - 1) * 16, 16));

        Text("bodies");
        foreach (var handle in md.MethodDefinitions)
        {
            int rva = md.GetMethodDefinition(handle).RelativeVirtualAddress;
            if (rva == 0) continue;
            int size = pe.GetMethodBody(rva).Size;
            hash.AppendData(pe.GetSectionData(rva).GetContent(0, size).AsSpan());
        }
        Text("field data");
        foreach (var handle in md.FieldDefinitions)
        {
            var field = md.GetFieldDefinition(handle);
            int rva = field.GetRelativeVirtualAddress();
            if (rva == 0) continue;
            int size = FieldDataSize(md, field);
            Text(size.ToString(CultureInfo.InvariantCulture));
            if (size > 0) hash.AppendData(pe.GetSectionData(rva).GetContent(0, size).AsSpan());
        }
        Text("resources");
        int resourcesAt = pe.PEHeaders.CorHeader?.ResourcesDirectory.RelativeVirtualAddress ?? 0;
        foreach (var handle in md.ManifestResources)
        {
            var resource = md.GetManifestResource(handle);
            Text(md.GetString(resource.Name));
            if (!resource.Implementation.IsNil || resourcesAt == 0) continue;   // another file's or another assembly's: named only
            var data = pe.GetSectionData(resourcesAt + (int)resource.Offset);
            var reader = data.GetReader();
            int length = reader.ReadInt32();
            hash.AppendData(data.GetContent(4, length).AsSpan());
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>The size of a field's initial data (a field with an RVA): its value type's layout size, or a primitive's.</summary>
    static int FieldDataSize(MetadataReader md, FieldDefinition field)
    {
        var sig = md.GetBlobReader(field.Signature);
        sig.ReadSignatureHeader();
        var code = sig.ReadSignatureTypeCode();
        return code switch
        {
            SignatureTypeCode.Boolean or SignatureTypeCode.SByte or SignatureTypeCode.Byte => 1,
            SignatureTypeCode.Char or SignatureTypeCode.Int16 or SignatureTypeCode.UInt16 => 2,
            SignatureTypeCode.Int32 or SignatureTypeCode.UInt32 or SignatureTypeCode.Single => 4,
            SignatureTypeCode.Int64 or SignatureTypeCode.UInt64 or SignatureTypeCode.Double => 8,
            SignatureTypeCode.TypeHandle when sig.ReadTypeHandle() is { Kind: HandleKind.TypeDefinition } type
                => md.GetTypeDefinition((TypeDefinitionHandle)type).GetLayout().Size,
            _ => 0,
        };
    }

    /// <summary>This machine: this build, and the data <paramref name="files"/> reads (worked out once a set of files).</summary>
    public static NetIdentity Of(Vfs.GameFiles files)
    {
        lock (known)
        {
            if (known.TryGetValue(files, out var had)) return had;
            var (id, count, _) = files.Fingerprint();
            var made = new NetIdentity(ThisBuild, ProgramName, id, count, files.Store())
                { Modules = files.RulesModules, Rules = RulesOf(Engine.Rules.GameRules.Current) };
            known.Add(files, made);
            return made;
        }
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Vfs.GameFiles, NetIdentity> known = [];

    /// <summary>Written in the Hello, after the proof.</summary>
    public void Write(NetWriter w) =>
        w.Str(Build).Str(BuildName)
         .Str((Modules.Count == 0 ? Data : Data + ModulesMark + string.Join(ModuleMark, Modules)) + (Rules.Length == 0 ? "" : RulesMark + Rules))
         .I32(DataFiles).Str(Store);

    public static NetIdentity Read(NetReader r)
    {
        string build = r.Str(), name = r.Str(), data = r.Str();
        int files = r.I32();
        string store = r.Str();
        string rules = "";
        if (data.IndexOf(RulesMark) is var at and >= 0) (data, rules) = (data[..at], data[(at + 1)..]);
        int mark = data.IndexOf(ModulesMark);
        return mark < 0 ? new(build, name, data, files, store) { Rules = rules }
            : new(build, name, data[..mark], files, store) { Modules = data[(mark + 1)..].Split(ModuleMark, StringSplitOptions.RemoveEmptyEntries), Rules = rules };
    }

    /// <summary>The data as the line names it: its fingerprint, its files and its store.</summary>
    string DataLine => $"{(Data.Length > 0 ? Data : "unknown")} ({DataFiles:N0} files{(Store.Length > 0 ? ", a " + Store + " install" : "")})";

    /// <summary>
    /// **What differs between the host (<paramref name="host"/>) and a joining machine (this)**: null when they
    /// play the same game, else the line both ends show — the build first, then the data. A side that does not state one
    /// (<see cref="Unstated"/>: a test's host or player made without the program) is not checked on it.
    /// </summary>
    public string? Differs(NetIdentity host)
    {
        if (Build.Length > 0 && host.Build.Length > 0 && Build != host.Build)
            return $"The host runs another Vitomir build: {Name(host)} (build {Or(host.Build)}) against this machine's {Name(this)} " +
                   $"(build {Or(Build)}). Both must run the same build.";
        if (Data.Length > 0 && host.Data.Length > 0 && Data != host.Data)
            return $"The host reads other VTMR data: its fingerprint {host.DataLine} against this machine's {DataLine}. " +
                   "Both must read the same 1.1 data (the same install, no other mods).";
        if ((Build.Length > 0 || Data.Length > 0) && (host.Build.Length > 0 || host.Data.Length > 0) && ModulesDiffer(host.Modules, Modules) is { } modules)
            return $"The host plays with other rules modules: {modules}. Both must have the same rules modules on, in the same order " +
                   "(look modules - textures, sounds, text, films - may differ).";
        if ((Build.Length > 0 || Data.Length > 0) && (host.Build.Length > 0 || host.Data.Length > 0) && RulesDiffer(host.Rules, Rules) is { } rules)
            return $"The host plays by other rules: {rules}. Both must resolve the same rules (the same rules.cfg in the Classic game's folder " +
                   "and the same rules modules).";
        return null;
    }

    /// <summary>
    /// **What differs between the host's rules and this machine's**, in words, or null when they are the same: each side's rules as
    /// 1.1's or by their hash and the keys they change.
    /// </summary>
    public static string? RulesDiffer(string host, string here)
    {
        if (host == here) return null;
        static string Said(string rules) => rules.Length == 0 ? "1.1's" : rules.Split(':') is [var hash, var keys]
            ? $"{hash} (changing {(keys.Length == 0 ? "nothing named" : keys.Replace(",", ", "))})" : rules;
        return $"the host's are {Said(host)}, this machine's {Said(here)}";
    }

    /// <summary>
    /// **What differs between the host's rules modules and this machine's**, in words, or null when they are the same list: each module
    /// only one side has, each of another version or other files, then (when both have the same modules) the order.
    /// </summary>
    public static string? ModulesDiffer(IReadOnlyList<string> host, IReadOnlyList<string> here)
    {
        if (host.SequenceEqual(here)) return null;
        static (string Id, string Version, string Hash) Split(string stamp) =>
            stamp.Split(' ') is [var id, var version, var hash] ? (id, version, hash) : (stamp, "", "");
        var h = host.Select(Split).ToList();
        var m = here.Select(Split).ToList();
        var said = new List<string>();
        foreach (var x in h.Where(x => !m.Any(y => y.Id == x.Id))) said.Add($"{x.Id} {x.Version} is on at the host and not here");
        foreach (var y in m.Where(y => !h.Any(x => x.Id == y.Id))) said.Add($"{y.Id} {y.Version} is on here and not at the host");
        foreach (var x in h)
            if (m.FirstOrDefault(y => y.Id == x.Id) is { Id.Length: > 0 } y)
            {
                if (x.Version != y.Version) said.Add($"{x.Id} is {x.Version} at the host and {y.Version} here");
                else if (x.Hash != y.Hash) said.Add($"{x.Id} {x.Version} has other files or interface bindings at the host than here");
            }
        if (said.Count == 0)
            said.Add($"the order differs (the host's {string.Join(", ", h.Select(x => x.Id))}; here {string.Join(", ", m.Select(y => y.Id))})");
        return string.Join("; ", said);
    }

    static string Name(NetIdentity side) => side.BuildName.Length > 0 ? side.BuildName : "a Vitomir";
    static string Or(string s) => s.Length > 0 ? s : "unknown";

    /// <summary>The console's line for this machine: the build and the data, as the log bundle writes them too.</summary>
    public override string ToString() => $"{Name(this)}, build {Or(Build)}; VTMR data {DataLine}" +
        (Modules.Count > 0 ? $"; rules modules {string.Join(", ", Modules)}" : "") + (Rules.Length > 0 ? $"; rules {Rules}" : "");
}
