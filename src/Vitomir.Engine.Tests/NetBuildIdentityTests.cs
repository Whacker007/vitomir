// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Vitomir.Engine.Net;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Two builds of the same code join each other** (<see cref="NetIdentity.BuildOf"/>, <see cref="NetIdentity.CodeOf(Stream)"/>):
/// the build a machine states is a hash of what the four join assemblies' code is — their metadata and IL — not of how they
/// were built. Pinned on small assemblies made here (<see cref="PersistedAssemblyBuilder"/>, each build with a module version
/// of its own) and written as a compiler writes them, a debug directory naming the PDB in the folder it was built in: two
/// builds of the same code, in two folders, differ byte for byte and in their module versions and state the same code; a
/// method's line, a string or an embedded resource changed states another. And this program's own build is the hash of its
/// loaded join assemblies' code.
/// </summary>
public class NetBuildIdentityTests
{
    /// <summary>The code a tiny assembly is made from: what <c>Tiny.Greeter.Hello</c> returns, what <c>Answer</c> returns, its resource.</summary>
    sealed record Source(string Greeting = "hello", int Answer = 42, string Resource = "the resource");

    /// <summary>
    /// A library built from <paramref name="source"/> as a build in <paramref name="folder"/> would leave it: its own module
    /// version (a fresh one each build), and a debug directory naming <c>folder/obj/Tiny.pdb</c> with an id and a checksum of
    /// that build's.
    /// </summary>
    static byte[] Build(Source source, string folder)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("Tiny") { Version = new Version(1, 2, 3, 4) }, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("Tiny");
        var type = module.DefineType("Tiny.Greeter", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Abstract);
        var hello = type.DefineMethod("Hello", MethodAttributes.Public | MethodAttributes.Static, typeof(string), Type.EmptyTypes).GetILGenerator();
        hello.Emit(OpCodes.Ldstr, source.Greeting);
        hello.Emit(OpCodes.Ret);
        var answer = type.DefineMethod("Answer", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes).GetILGenerator();
        answer.Emit(OpCodes.Ldc_I4, source.Answer);
        answer.Emit(OpCodes.Ret);
        type.CreateType();

        var metadata = assembly.GenerateMetadata(out var il, out var fieldData);
        var resource = System.Text.Encoding.UTF8.GetBytes(source.Resource);
        var resources = new BlobBuilder();
        resources.WriteInt32(resource.Length);
        resources.WriteBytes(resource);
        metadata.AddManifestResource(ManifestResourceAttributes.Public, metadata.GetOrAddString("Tiny.resource.txt"), default, 0);

        // The build's own: the PDB's path in its folder, its id and checksum (made from the folder here, as a compiler makes them from the PDB).
        var buildId = new BlobContentId(Guid.NewGuid(), (uint)folder.GetHashCode(StringComparison.Ordinal));
        var debug = new DebugDirectoryBuilder();
        debug.AddCodeViewEntry(Path.Combine(folder, "obj", "Tiny.pdb"), buildId, portablePdbVersion: 0x0100);
        debug.AddPdbChecksumEntry("SHA256", [.. System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(folder))]);
        debug.AddReproducibleEntry();

        var pe = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(metadata), il,
            mappedFieldData: fieldData, managedResources: resources, debugDirectoryBuilder: debug,
            deterministicIdProvider: content => buildId);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static string Code(byte[] image)
    {
        using var stream = new MemoryStream(image);
        return NetIdentity.CodeOf(stream);
    }

    static Guid Mvid(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image));
        var md = pe.GetMetadataReader();
        return md.GetGuid(md.GetModuleDefinition().Mvid);
    }

    [Fact]
    public void The_same_code_built_twice_in_two_folders_states_the_same_code()
    {
        byte[] a = Build(new Source(), "one/Vitomir/src/Tiny"), b = Build(new Source(), @"elsewhere\Vitomir-public\src\Tiny");
        Assert.NotEqual(a, b);                       // two files: the debug directory, the time stamp and the module version differ
        Assert.NotEqual(Mvid(a), Mvid(b));
        Assert.Equal(64, Code(a).Length);
        Assert.Equal(Code(a), Code(b));
    }

    [Fact]
    public void A_line_of_code_a_string_or_a_resource_changed_states_another_code()
    {
        string same = Code(Build(new Source(), "/build"));
        Assert.NotEqual(same, Code(Build(new Source(Answer: 43), "/build")));
        Assert.NotEqual(same, Code(Build(new Source(Greeting: "hello!"), "/build")));
        Assert.NotEqual(same, Code(Build(new Source(Resource: "the resource, changed"), "/build")));
    }

    static (string, string?)[] Join(params string[] codes) =>
        [.. NetIdentity.JoinAssemblies.Zip(codes, (name, code) => (name, (string?)code))];

    [Fact]
    public void The_same_code_states_the_same_build_and_joins()
    {
        string tiny = Code(Build(new Source(), "/a")), again = Code(Build(new Source(), "/b"));
        string[] four = [tiny, "e1", "e2", "e3"], other = [again, "e1", "e2", "e3"];
        string a = NetIdentity.BuildOf(Join(four)), b = NetIdentity.BuildOf(Join(other));
        Assert.Equal(16, a.Length);
        Assert.Equal(a, b);
        var host = new NetIdentity(a, "Vitomir Platform 0.9.0-dev (dev)", "0123456789abcdef", 4102, "GOG");
        var player = new NetIdentity(b, "Vitomir Platform 0.9.0-dev", "0123456789abcdef", 4102, "GOG");
        Assert.Null(player.Differs(host));   // the version lines differ (a development tag); the build does not
    }

    [Fact]
    public void Other_code_in_one_join_assembly_is_another_build()
    {
        string[] four = [Code(Build(new Source(), "/a")), "e1", "e2", "e3"];
        string[] other = [Code(Build(new Source(Answer: 7), "/a")), "e1", "e2", "e3"];
        string a = NetIdentity.BuildOf(Join(four)), b = NetIdentity.BuildOf(Join(other));
        Assert.NotEqual(a, b);
        var host = new NetIdentity(a, "Vitomir Platform 0.9.0-dev", "0123456789abcdef", 4102, "GOG");
        var player = new NetIdentity(b, "Vitomir Platform 0.9.0-dev", "0123456789abcdef", 4102, "GOG");
        Assert.Contains("another Vitomir build", player.Differs(host));
    }

    [Fact]
    public void This_build_is_the_hash_of_the_loaded_join_assemblies_code()
    {
        var codes = NetIdentity.JoinAssemblies.Select(name =>
        {
            using var file = File.OpenRead(Assembly.Load(name).Location);
            return (name, (string?)NetIdentity.CodeOf(file));
        });
        Assert.Equal(NetIdentity.BuildOf(codes), NetIdentity.ThisBuild);
    }
}
