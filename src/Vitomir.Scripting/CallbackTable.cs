// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The engine's own list of the fifty-eight callbacks, as the engine resolves them when
// it first loads a class: a `{name, signature}` table.
namespace Vitomir.Scripting;

/// <summary>
/// 1.1's callback table, in its own order: entry <c>n</c> is resolved with JNI's
/// <c>GetMethodID(class, name, signature)</c> into the class wrapper's slot <c>n</c>, and a
/// class that does not declare that exact name **and signature — return type included** — gets a null
/// slot and is never sent it. Vitomir's lookup matches the same way (<see cref="ScriptRuntime.TryInvoke"/>),
/// and every callback Vitomir sends is sent with its signature from here: a signature that
/// is not the table's is refused rather than silently never found.
/// </summary>
public static class CallbackTable
{
    /// <summary>The fifty-eight entries, slot by slot.</summary>
    public static readonly (string Name, string Signature)[] All =
    [
        ("tick", "(F)V"), ("timer", "(IFFFF)V"), ("startup", "(F)V"), ("shutdown", "(F)V"),
        ("clicked", "(III)V"), ("damaged", "(IIFII)F"), ("pickup", "(III)Z"), ("dropped", "(IFFFI)Z"),
        ("worn", "(III)V"), ("unworn", "(III)Z"), ("used", "(III)Z"), ("cast", "(III)I"),
        ("cast", "(II)I"), ("cast", "(IIFFF)I"), ("arrived", "(III)V"), ("created", "(I)V"),
        ("destroyed", "(II)V"), ("killed", "(III)V"), ("entered", "(III)V"), ("exited", "(III)V"),
        ("convended", "(IZI)V"), ("convreached", "(IIIII)V"), ("beginscene", "(II)V"), ("endscene", "(III)V"),
        ("buysellended", "(Z)V"), ("buysellquery", "(ZI)Z"), ("awaken", "(III)V"), ("save", "(I)V"),
        ("restore", "(I)V"), ("restored", "(I)V"), ("pathended", "(I)V"), ("triggered", "(IIIFFFFI)V"),
        ("advance", "(II)V"), ("effectstarted", "(IIII)V"), ("effectended", "(IIII)V"), ("hit", "(IIFI)V"),
        ("actorcast", "(ILjava/lang/String;III)V"), ("convchosen", "(IIIII)V"), ("actorfeed", "(IIII)V"), ("textended", "(I)V"),
        ("touched", "(III)V"), ("keytag", "(III)V"), ("soaked", "(IIFIFI)F"), ("beginswing", "(IIII)V"),
        ("cancast", "(IIIFFFZ)Z"), ("videoended", "(I)V"), ("actorworn", "(III)V"), ("actorunworn", "(III)Z"),
        ("weatherchanged", "()V"), ("actorstill", "(II)V"), ("actorunstill", "(II)V"), ("stcommand", "(Ljava/lang/String;IFFFF)V"),
        ("canuse", "(III)Z"), ("actorcanuse", "(III)Z"), ("exitstatechanged", "()V"), ("joined", "(I)V"),
        ("disconnected", "(I)V"), ("stcommand2", "(Ljava/lang/String;)V"),
    ];

    static readonly HashSet<(string, string)> entries = [.. All.Select(e => (e.Name, e.Signature))];
    static readonly HashSet<string> names = [.. All.Select(e => e.Name)];

    /// <summary>Whether <paramref name="name"/> with <paramref name="signature"/> is one of the table's entries.</summary>
    public static bool Holds(string name, string signature) => entries.Contains((name, signature));

    /// <summary>Whether <paramref name="name"/> is a callback at all (in any of its shapes).</summary>
    public static bool Names(string name) => names.Contains(name);
}
