// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game's scripts, loaded and running, with the engine installed underneath them.
using System.Diagnostics;

namespace Vitomir.Scripting;

/// <summary>
/// Loads the compiled Codex classes and runs them. One per session: the class loader, the bridge
/// host and every live script object hang off it.
/// </summary>
public sealed class ScriptRuntime
{
    readonly java.net.URLClassLoader loader;
    readonly java.lang.Class hostClass;
    readonly java.lang.reflect.Method instantiate, callIntInt, callTimer, callEndscene, callArrived, callClicked, callKilled, setGuid, getGuid, isScene;
    readonly java.lang.reflect.Method callIII, callIIIII, callConvEnded, callPathEnded, invoke, declares, resolves;
    readonly CodexCompiler compiler;

    public CodexHost Engine { get; }
    /// <summary>The chronicle script the scenes belong to (Prague, Vienna, London, New York…).</summary>
    public object? Chronicle { get; private set; }
    public CodexBuild Build { get; }
    /// <summary>How long loading the classes and installing the engine took.</summary>
    public long LoadMs { get; }

    ScriptRuntime(CodexCompiler compiler, CodexBuild build, CodexHost engine)
    {
        this.compiler = compiler;
        Build = build;
        Engine = engine;
        var sw = Stopwatch.StartNew();
        // The overlay first, then the shipped classes (1.1's search path, the loose folders before Codex.nob), or the source
        // path's one build.
        loader = new java.net.URLClassLoader([.. build.SearchPath.Select(d => new java.io.File(d).toURI().toURL())], null);
        hostClass = java.lang.Class.forName("ScriptHost", true, loader);
        var bridge = java.lang.Class.forName("EngineBridge", true, loader);
        bridge.getField("host").set(null, engine);              // every script call now lands in C#
        var str = java.lang.Class.forName("java.lang.String");
        instantiate = hostClass.getMethod("instantiate", [str]);
        isScene = hostClass.getMethod("isScene", [str]);
        var obj = java.lang.Class.forName("java.lang.Object");
        var i32 = java.lang.Integer.TYPE; var f32 = java.lang.Float.TYPE;
        callIntInt = hostClass.getMethod("callIntInt", [obj, str, i32, i32]);
        callTimer = hostClass.getMethod("callTimer", [obj, i32, f32, f32, f32, f32]);
        callEndscene = hostClass.getMethod("callEndscene", [obj, i32, i32, i32]);
        callArrived = hostClass.getMethod("callArrived", [obj, i32, i32, i32]);
        callClicked = hostClass.getMethod("callClicked", [obj, i32, i32, i32]);
        callKilled = hostClass.getMethod("callKilled", [obj, i32, i32, i32]);
        callIII = hostClass.getMethod("callIII", [obj, str, i32, i32, i32]);
        callIIIII = hostClass.getMethod("callIIIII", [obj, str, i32, i32, i32, i32, i32]);
        callConvEnded = hostClass.getMethod("callConvEnded", [obj, i32, java.lang.Boolean.TYPE, i32]);
        callPathEnded = hostClass.getMethod("callPathEnded", [obj, i32]);
        var classArray = java.lang.Class.forName("[Ljava.lang.Class;");
        var objArray = java.lang.Class.forName("[Ljava.lang.Object;");
        var cls = java.lang.Class.forName("java.lang.Class");
        invoke = hostClass.getMethod("invoke", [obj, str, classArray, cls, objArray]);
        declares = hostClass.getMethod("declares", [obj, str, classArray, cls]);
        resolves = hostClass.getMethod("resolves", [cls, str, classArray, cls]);
        setGuid = hostClass.getMethod("setGuid", [obj, i32]);
        getGuid = hostClass.getMethod("getGuid", [obj]);
        LoadMs = sw.ElapsedMilliseconds;
    }

    /// <summary>Compiles the player's scripts if needed, then loads them with <paramref name="engine"/> underneath.</summary>
    public static ScriptRuntime Start(CodexCompiler compiler, CodexHost engine, bool force = false) =>
        new(compiler, compiler.Build(force), engine);

    /// <summary>
    /// Creates a script object by class name. Throws <see cref="ScriptException"/> carrying the
    /// Java exception's own type name, because the caller has to tell a wrong chronicle
    /// (ClassCastException, try another) from a script that is actually broken.
    /// </summary>
    public object Instantiate(string className)
    {
        try { return instantiate.invoke(null, [className]); }
        catch (Exception e) { throw ScriptException.From(className, e); }
    }

    /// <summary>Creates a script object, or false with the reason when it won't.</summary>
    public bool TryInstantiate(string className, out object? script, out string? why)
    {
        try { script = Instantiate(className); why = null; return true; }
        catch (ScriptException e) { script = null; why = e.JavaType; return false; }
    }

    /// <summary>Sets the chronicle every scene script reads through <c>Codex.GetChronicleScript</c>.</summary>
    public bool SetChronicle(string className) => TryInstantiate(className, out var c, out _) && (Chronicle = c) is not null;

    /// <summary>Whether a class is a scene script: a public no-arg constructor and beginscene(int,int).</summary>
    public bool IsScene(string className) =>
        isScene.invoke(null, [className]) is java.lang.Boolean b && b.booleanValue();

    /// <summary>
    /// Calls one of the engine's script callbacks — <c>beginscene</c>, <c>endscene</c> — returning
    /// false when the script doesn't define it. Exceptions inside the script are the caller's.
    /// </summary>
    public bool Call(object script, string name, int a, int b) =>
        callIntInt.invoke(null, [script, name, java.lang.Integer.valueOf(a), java.lang.Integer.valueOf(b)])
            is java.lang.Boolean r && r.booleanValue();

    /// <summary>
    /// Loads a scene script by name, recompiling its source first if the player has edited it since
    /// the cache was built. Returns the script object, or null when there is no such script.
    /// </summary>
    public object? LoadScene(string className, out long recompiledMs)
    {
        recompiledMs = 0;
        var source = compiler.SourceOf(className);
        if (source is not null && compiler.NeedsRecompile(source)) recompiledMs = compiler.Recompile(source);
        return IsScene(className) ? Instantiate(className) : null;
    }

    /// <summary>
    /// Gives a script its GUID — the number it passes to natives like SetTimer to name itself.
    /// The original does this when it creates the script; nothing works without it.
    /// </summary>
    public void SetGuid(object script, int guid) => setGuid.invoke(null, [script, java.lang.Integer.valueOf(guid)]);

    /// <summary>
    /// The GUID a Codex object carries. The CodexThing natives are instance methods with no GUID
    /// parameter — the object itself says which thing it is — so this is how a handler knows.
    /// </summary>
    public int GuidOf(object? self) =>
        self is null ? 0 : getGuid.invoke(null, [self]) is java.lang.Integer i ? i.intValue() : 0;

    /// <summary>
    /// The parameter types of a script's first public constructor, as Java names (<c>int</c>,
    /// <c>float</c>, <c>java.lang.String</c>, <c>CodexThing</c>…). The original takes the
    /// first constructor `getConstructors()` returns and caps it at sixteen parameters, matching each
    /// parameter's type name against its own fixed list of the twelve it knows to get the conversion;
    /// a script class declares exactly one constructor, so
    /// "first" is unambiguous in practice.
    /// </summary>
    public string[]? ConstructorTypes(string className)
    {
        var ctor = FirstConstructor(className);
        return ctor is null ? null : [.. ctor.getParameterTypes().Take(MaxConstructorParams).Select(t => t.getName())];
    }

    /// <summary>The original caps a script constructor at this many parameters.</summary>
    public const int MaxConstructorParams = 16;

    java.lang.reflect.Constructor? FirstConstructor(string className)
    {
        try
        {
            var ctors = java.lang.Class.forName(className, true, loader).getConstructors();
            return ctors.Length > 0 ? ctors[0] : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Creates a script with its constructor arguments, writing <paramref name="guid"/> where the
    /// original writes it: before the constructor body runs (see <see cref="CodexBridge"/>).
    /// </summary>
    public object NewScript(string className, int guid, object?[] args)
    {
        var ctor = FirstConstructor(className) ?? throw new ScriptException(className, "no public constructor", new MissingMethodException(className));
        var bridge = java.lang.Class.forName("EngineBridge", true, loader).getField("pendingGuid");
        bridge.setInt(null, guid);
        try { return ctor.newInstance(args); }
        catch (Exception e) { throw ScriptException.From(className, e); }
        finally { bridge.setInt(null, 0); }
    }

    /// <summary>
    /// A <c>CodexThing</c> (or one of its subclasses) wrapping a thing's GUID — what a script
    /// receives for a constructor parameter that names a thing in the scene.
    /// </summary>
    public object? NewThingRef(string codexClass, int guid)
    {
        try
        {
            var c = java.lang.Class.forName(codexClass, true, loader);
            return c.getConstructor([java.lang.Integer.TYPE]).newInstance([java.lang.Integer.valueOf(guid)]);
        }
        catch (Exception) { return null; }
    }

    /// <summary>The engine's timer callback: <c>timer(id, a, b, c, d)</c>.</summary>
    public bool CallTimer(object script, int id, float a, float b, float c, float d) =>
        callTimer.invoke(null, [script, java.lang.Integer.valueOf(id), java.lang.Float.valueOf(a),
                                java.lang.Float.valueOf(b), java.lang.Float.valueOf(c), java.lang.Float.valueOf(d)])
            is java.lang.Boolean r && r.booleanValue();

    /// <summary>The engine's arrival callback: <c>arrived(thingGuid, frameNum, captureId)</c>.</summary>
    public bool CallArrived(object script, int thingGuid, int frame, int captureId) =>
        callArrived.invoke(null, [script, java.lang.Integer.valueOf(thingGuid),
                                  java.lang.Integer.valueOf(frame), java.lang.Integer.valueOf(captureId)])
            is java.lang.Boolean r && r.booleanValue();

    /// <summary>
    /// The engine's use callback: <c>clicked(thingGuid, clickerGuid, captureId)</c>. False means
    /// the script has no such method, which is not a failure — most of them do not.
    /// </summary>
    public bool CallClicked(object script, int thingGuid, int clickerGuid, int captureId) =>
        callClicked.invoke(null, [script, java.lang.Integer.valueOf(thingGuid),
                                  java.lang.Integer.valueOf(clickerGuid), java.lang.Integer.valueOf(captureId)])
            is java.lang.Boolean r && r.booleanValue();

    /// <summary>
    /// The engine's destroy callback: <c>killed(thingGuid, killerGuid, captureId)</c>. **Opening a
    /// container is one of these** — see `Rpg.Container`.
    /// </summary>
    public bool CallKilled(object script, int thingGuid, int killerGuid, int captureId) =>
        callKilled.invoke(null, [script, java.lang.Integer.valueOf(thingGuid),
                                 java.lang.Integer.valueOf(killerGuid), java.lang.Integer.valueOf(captureId)])
            is java.lang.Boolean r && r.booleanValue();

    /// <summary>
    /// Any <c>(int, int, int)</c> callback by name — <c>entered(regionGuid, moverGuid, captureId)</c>
    /// and <c>exited</c> are the ones that need this; the older ones have their own.
    /// </summary>
    public bool CallIII(object script, string name, int a, int b, int c) =>
        callIII.invoke(null, [script, name, java.lang.Integer.valueOf(a), java.lang.Integer.valueOf(b),
                              java.lang.Integer.valueOf(c)]) is java.lang.Boolean r && r.booleanValue();

    /// <summary>
    /// <c>convreached(id, event, line, ms, speakerGuid)</c> or <c>convchosen(id, event, answer, ms,
    /// who)</c>.
    /// </summary>
    public bool CallIIIII(object script, string name, int a, int b, int c, int d, int e) =>
        callIIIII.invoke(null, [script, name, java.lang.Integer.valueOf(a), java.lang.Integer.valueOf(b),
                                java.lang.Integer.valueOf(c), java.lang.Integer.valueOf(d), java.lang.Integer.valueOf(e)])
            is java.lang.Boolean r && r.booleanValue();

    /// <summary><c>convended(id, aborted, returnValue)</c>.</summary>
    public bool CallConvEnded(object script, int id, bool aborted, int code) =>
        callConvEnded.invoke(null, [script, java.lang.Integer.valueOf(id), java.lang.Boolean.valueOf(aborted),
                                    java.lang.Integer.valueOf(code)]) is java.lang.Boolean r && r.booleanValue();

    /// <summary><c>pathended(clientGuid)</c>.</summary>
    public bool CallPathEnded(object script, int clientGuid) =>
        callPathEnded.invoke(null, [script, java.lang.Integer.valueOf(clientGuid)])
            is java.lang.Boolean r && r.booleanValue();

    /// <summary>The engine's leave-the-scene callback: <c>endscene(clientGuid, captureID, exitNum)</c>.</summary>
    public bool CallEndscene(object script, int clientGuid, int captureId, int exitNum) =>
        callEndscene.invoke(null, [script, java.lang.Integer.valueOf(clientGuid),
                                   java.lang.Integer.valueOf(captureId), java.lang.Integer.valueOf(exitNum)])
            is java.lang.Boolean r && r.booleanValue();

    /// <summary>
    /// Any callback by name and JNI signature — <c>created(I)V</c>, <c>used(III)Z</c>,
    /// <c>cast(II)I</c>, <c>effectstarted(IIII)V</c> — with what it returned. False when the class
    /// has no such method, which is not a failure: most scripts implement a handful of the
    /// fifty-eight. Exceptions inside the script are the caller's.
    /// <para>**Found as 1.1 finds it**: by the name, the parameter types **and the return type**
    /// (`GetMethodID` with the table's full signature), so a class whose method
    /// has the name and parameters but returns something else does not answer — the two shipped
    /// <c>void soaked</c> are not the engine's <c>soaked(IIFIFI)F</c>. A callback's signature must be
    /// the table's (<see cref="CallbackTable"/>): any other is refused with an exception, because a
    /// wrong one would now be silently never found. <paramref name="offTable"/> lets one through on
    /// purpose — the Fix <c>soakedvoid</c>'s <c>(IIFIFI)V</c>, the only one.</para>
    /// </summary>
    public bool TryInvoke(object script, string name, string signature, object?[] args, out object? result, bool offTable = false)
    {
        Check(name, signature, offTable);
        var answer = invoke.invoke(null, [script, name, TypesOf(signature), ReturnOf(signature), args]) as object[];
        result = answer is { Length: > 0 } ? answer[0] : null;
        return answer is not null;
    }

    /// <summary>
    /// Whether a script's class has a callback, without calling it — the engine's own test of the
    /// class's method slot before it sends one (`actorcast`'s capture loop reads exactly this). Found
    /// as <see cref="TryInvoke"/> finds it: name, parameters and return type.
    /// </summary>
    public bool Declares(object script, string name, string signature, bool offTable = false)
    {
        Check(name, signature, offTable);
        return declares.invoke(null, [script, name, TypesOf(signature), ReturnOf(signature)]) is java.lang.Boolean b && b.booleanValue();
    }

    /// <summary>
    /// Whether a class, by name, resolves a callback as 1.1's `GetMethodID` would — without
    /// making one, so without running its constructor. For the scan of every class
    /// (<c>CallbackLookupTests</c>).
    /// </summary>
    public bool Resolves(string className, string name, string signature) =>
        ClassOf(className) is { } c
        && resolves.invoke(null, [c, name, TypesOf(signature), ReturnOf(signature)]) is java.lang.Boolean b && b.booleanValue();

    /// <summary>A class of the build, loaded but not initialised, or null when there is none.</summary>
    public java.lang.Class? ClassOf(string className)
    {
        try { return java.lang.Class.forName(className, false, loader); }
        catch (Exception) { return null; }
    }

    /// <summary>A callback's name with a signature that is not the table's is a mistake in Vitomir: refused.</summary>
    static void Check(string name, string signature, bool offTable)
    {
        if (!offTable && CallbackTable.Names(name) && !CallbackTable.Holds(name, signature))
            throw new ArgumentException($"{name}{signature} is not in the engine's callback table", nameof(signature));
    }

    /// <summary>The return class a JNI signature names: <c>V</c>, <c>Z</c>, <c>I</c> or <c>F</c> after the <c>)</c>.</summary>
    public static java.lang.Class ReturnOf(string signature) => signature[(signature.IndexOf(')') + 1)..] switch
    {
        "V" => java.lang.Void.TYPE,
        "Z" => java.lang.Boolean.TYPE,
        "I" => java.lang.Integer.TYPE,
        "F" => java.lang.Float.TYPE,
        var other => throw new ArgumentException($"signature {signature} returns {other}"),
    };

    /// <summary>The parameter classes a JNI signature names (<c>(IZF[F)</c> and <c>Ljava/lang/String;</c>).</summary>
    public static java.lang.Class[] TypesOf(string signature)
    {
        var list = new List<java.lang.Class>();
        for (int i = signature.IndexOf('(') + 1; i < signature.Length && signature[i] != ')'; i++)
        {
            if (signature[i] == '[')
            {
                // An array's class is named by its own descriptor: `[F`, `[Ljava.lang.String;`.
                int end = signature[i + 1] == 'L' ? signature.IndexOf(';', i) : i + 1;
                list.Add(java.lang.Class.forName(signature[i..(end + 1)].Replace('/', '.')));
                i = end;
                continue;
            }
            java.lang.Class t = signature[i] switch
            {
                'I' => java.lang.Integer.TYPE,
                'F' => java.lang.Float.TYPE,
                'Z' => java.lang.Boolean.TYPE,
                'L' => java.lang.Class.forName(signature[(i + 1)..signature.IndexOf(';', i)].Replace('/', '.')),
                _ => throw new ArgumentException($"signature {signature}"),
            };
            if (signature[i] == 'L') i = signature.IndexOf(';', i);
            list.Add(t);
        }
        return [.. list];
    }

    /// <summary>Every class in the build (the overlay's and the shipped ones', once each), which is how the tests find the scene scripts.</summary>
    public IEnumerable<string> ClassNames() =>
        Build.SearchPath.SelectMany(d => Directory.GetFiles(d, "*.class"))
                 .Select(Path.GetFileNameWithoutExtension)
                 .Where(n => n is not null && !n.Contains('$'))
                 .Distinct(StringComparer.Ordinal)
                 .OrderBy(n => n, StringComparer.Ordinal)!;
}

/// <summary>A script threw. <see cref="JavaType"/> is the Java exception's own class name.</summary>
public sealed class ScriptException(string className, string javaType, Exception inner)
    : Exception($"{className}: {javaType}", inner)
{
    public string ClassName { get; } = className;
    public string JavaType { get; } = javaType;

    /// <summary>
    /// Unwraps reflection's layers to whatever the script actually threw. Java's own
    /// InvocationTargetException keeps its cause in a field rather than as a .NET inner exception,
    /// so both kinds of nesting have to be peeled.
    /// </summary>
    public static ScriptException From(string className, Exception e)
    {
        var root = e;
        for (int guard = 0; guard < 16; guard++)
        {
            if (root is java.lang.reflect.InvocationTargetException ite && ite.getTargetException() is { } target)
            { root = target; continue; }
            if (root.InnerException is { } inner) { root = inner; continue; }
            break;
        }
        var name = root is java.lang.Throwable t ? t.getClass().getName() : root.GetType().FullName ?? root.GetType().Name;
        var made = new ScriptException(className, name, e);
        Made?.Invoke(made, root);
        return made;
    }

    /// <summary>
    /// **Ours, a diagnostic hook**: told of every script failure as it is wrapped, with the exception the
    /// script actually threw (its Java stack included), which the one-line log message leaves out. The
    /// story sweep listens (<c>StorySweep</c>, vitomir-tools'); nothing in the game does.
    /// </summary>
    public static Action<ScriptException, Exception>? Made { get; set; }
}
