// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// The module's folder as the build lays it out beside the tests (Modules/Enhanced, as beside the program), a
/// scratch copy of it to change, contexts that read it through the platform's registry by id, and a world with
/// the module in the sandbox.
/// </summary>
internal static class TestData
{
    /// <summary>The module's folder in the tests' output.</summary>
    public static string ModuleFolder => Path.Combine(AppContext.BaseDirectory, "Modules", "Enhanced");

    /// <summary>Its Data/ folder.</summary>
    public static string Folder => Path.Combine(ModuleFolder, "Data");

    /// <summary>The files under Data/ the tests read or change.</summary>
    public const string RegistryFile = "vampire_trait_registry.json";
    public const string GateRegisterFile = "NUMERIC_GATE_REGISTER.json";
    public const string GateMappingFile = "profile_value_gates.json";
    public const string SandboxFile = "profiles/prototype_sandbox.json";
    public const string ShippingFile = "profiles/enhanced_unapproved_template.json";

    /// <summary>A file of the module's Data/ in the tests' output.</summary>
    public static string File(string relative) => Path.Combine(Folder, relative);

    /// <summary>A context for the module in <paramref name="moduleFolder"/> (the built one by default), read through a registry of it alone.</summary>
    public static ModuleConfigContext Context(string? profile = null, string? moduleFolder = null, params string[] switches) => new()
    {
        Assets = AssetRegistry.ForModule(moduleFolder ?? ModuleFolder).For("enhanced"),
        Profile = profile,
        Switches = new HashSet<string>(switches, StringComparer.Ordinal),
    };

    public static EnhancedModule Sandbox(string? moduleFolder = null, params string[] switches)
    {
        var m = new EnhancedModule();
        m.Configure(Context(null, moduleFolder, [EnhancedModule.SandboxSwitch, .. switches]));
        return m;
    }

    /// <summary>The sandbox with both of the Abilities' channels off (the tempo turned off, the contest not asked for): the module as first made.</summary>
    public static EnhancedModule SandboxTempoOff(string? moduleFolder = null) => Sandbox(moduleFolder, EnhancedModule.TempoOffSwitch);

    /// <summary>A world with the module registered (sandbox) and its sheets.</summary>
    public static (World World, ActorSheets Sheets, EnhancedModule Module) World(ulong seed = 1)
    {
        var m = Sandbox();
        var b = new WorldBuilder(seed);
        m.Register(b);
        var w = b.Build();
        return (w, w.Service<ActorSheets>(), m);
    }

    /// <summary>A copy of the module's folder in a new scratch folder (its data to change), removed when disposed.</summary>
    public sealed class Copy : IDisposable
    {
        readonly string scratch;

        public Copy()
        {
            scratch = Directory.CreateTempSubdirectory("vitomir-enhanced-").FullName;
            Folder = Path.Combine(scratch, "Enhanced");
            foreach (var f in Directory.GetFiles(ModuleFolder, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(Folder, Path.GetRelativePath(ModuleFolder, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                System.IO.File.Copy(f, to);
            }
        }

        /// <summary>The copy's module folder (give it to <see cref="Context"/> or <see cref="Sandbox"/>).</summary>
        public string Folder { get; }

        /// <summary>A file of the copy's Data/.</summary>
        public string Path_(string file) => Path.Combine(Folder, "Data", file);

        public void Edit(string file, Func<string, string> change)
        {
            string p = Path_(file);
            string before = System.IO.File.ReadAllText(p);
            string after = change(before);
            if (after == before) throw new InvalidOperationException($"the edit of {file} changed nothing");
            System.IO.File.WriteAllText(p, after);
        }

        public void Dispose()
        {
            try { Directory.Delete(scratch, recursive: true); } catch (IOException) { }
        }
    }
}
