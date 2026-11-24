// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A module's own libraries, found in its bin\.
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Vitomir.Platform.Modules;

/// <summary>
/// **The libraries a module ships in its <c>bin\</c>** (ours). The loader loads a module's assembly from its folder
/// into the default load context (<see cref="ModuleLoader"/>); what that assembly needs and the program does not have —
/// the Classic module's engine, scripting, IKVM's runtime, the converted ecj, NLayer, DotRecast, BCnEncoder — is found
/// here, in the folder of each module loaded so far, in load order. The program's own assemblies are always found first
/// (the runtime asks this resolver only for a name it could not find itself), so a module can never replace the
/// platform's <c>Vitomir.Platform</c> and the <see cref="IModule"/> type stays one. Native libraries (IKVM's
/// <c>libikvm</c>) are found the same way for an assembly that lives in a module's <c>bin\</c>.
/// <para>One version of a library serves the process: the first module's <c>bin\</c> that has it wins, and the log says
/// where each was found. Two modules shipping different versions of one library would need a load context each (the
/// loader's note on mods).</para>
/// </summary>
public static class ModuleDependencies
{
    static readonly Lock gate = new();
    static readonly List<(string Module, string Folder)> folders = [];
    static Action<string> say = _ => { };
    static bool hooked;

    /// <summary>The <c>bin\</c> folders searched, in the order added.</summary>
    public static IReadOnlyList<string> Folders
    {
        get { lock (gate) return [.. folders.Select(f => f.Folder)]; }
    }

    /// <summary>Adds module <paramref name="moduleId"/>'s <c>bin\</c> to the folders searched (once), and hooks the runtime's resolution.</summary>
    public static void Add(string moduleId, string binFolder, Action<string>? log = null)
    {
        var full = Path.GetFullPath(binFolder);
        lock (gate)
        {
            if (log is not null) say = log;
            if (!folders.Any(f => string.Equals(f.Folder, full, StringComparison.Ordinal))) folders.Add((moduleId, full));
            if (hooked) return;
            hooked = true;
        }
        AssemblyLoadContext.Default.Resolving += Resolve;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += ResolveNative;
    }

    /// <summary>The file in a module's <c>bin\</c> holding the assembly <paramref name="name"/>, or null.</summary>
    public static string? Find(AssemblyName name)
    {
        if (name.Name is not { Length: > 0 } simple) return null;
        lock (gate)
            foreach (var (_, folder) in folders)
            {
                var path = Path.Combine(folder, simple + ".dll");
                if (File.Exists(path)) return path;
            }
        return null;
    }

    static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        if (Find(name) is not { } path) return null;
        var found = context.LoadFromAssemblyPath(path);
        // The log of the load that added the folder, which may be gone by the time a library is first asked for (a test's):
        // a closed log never stops the load.
        try { say($"modules: {name.Name} from {path}"); }
        catch (ObjectDisposedException) { say = _ => { }; }
        return found;
    }

    /// <summary>A native library asked for by an assembly in a module's <c>bin\</c>: looked for beside it, by the platform's names for it.</summary>
    static nint ResolveNative(Assembly asking, string library)
    {
        string? from = asking.IsDynamic ? null : Path.GetDirectoryName(asking.Location);
        if (from is null) return 0;
        bool ours;
        lock (gate) ours = folders.Any(f => string.Equals(f.Folder, Path.GetFullPath(from), StringComparison.Ordinal));
        if (!ours) return 0;
        foreach (var file in NativeNames(library))
        {
            var path = Path.Combine(from, file);
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle)) return handle;
        }
        return 0;
    }

    /// <summary>The files a native library <paramref name="library"/> may be on this system (as given, then with the system's prefix and suffix).</summary>
    public static IEnumerable<string> NativeNames(string library)
    {
        yield return library;
        string suffix = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        bool hasSuffix = library.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        if (!hasSuffix) yield return library + suffix;
        if (!OperatingSystem.IsWindows() && !Path.GetFileName(library).StartsWith("lib", StringComparison.Ordinal))
            yield return "lib" + library + (hasSuffix ? "" : suffix);
    }
}
