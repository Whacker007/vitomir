// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
/** Minimal script host for the Phase 2 spike (runs identically on a JVM and on IKVM).
 *  Instantiates script classes and invokes the engine callbacks the original engine would call.
 *  JDK 1.1 API only: the engine compiles this together with the game scripts against the
 *  game's own classes.zip.
 */
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;
import java.lang.reflect.InvocationTargetException;

public final class ScriptHost {
    public static Object instantiate(String className) throws Exception {
        Class c = Class.forName(className);
        return c.newInstance();
    }

    /** Invoke `name(int,int)` if the script defines it (beginscene, endscene, ...). */
    public static boolean callIntInt(Object script, String name, int a, int b) throws Exception {
        Method m = find(script.getClass(), name, new Class[]{Integer.TYPE, Integer.TYPE}, Void.TYPE);
        if (m == null) return false;
        m.invoke(script, new Object[]{new Integer(a), new Integer(b)});
        return true;
    }

    /** The engine's script callbacks, by the signatures every shipped script uses:
     *  beginscene(int,int), timer(int,float,float,float,float), arrived(int,int,int),
     *  clicked(int,int,int), endscene(int,int,int) — each found as the engine finds it, by name,
     *  parameters and its `void` return, of any access (`find`; `getMethod` once,
     *  which found public ones only — no shipped class declares one of these non-public). */
    public static boolean callTimer(Object script, int id, float a, float b, float c, float d) throws Exception {
        Method m = find(script.getClass(), "timer", new Class[]{Integer.TYPE, Float.TYPE, Float.TYPE, Float.TYPE, Float.TYPE}, Void.TYPE);
        if (m == null) return false;
        m.invoke(script, new Object[]{new Integer(id), new Float(a), new Float(b), new Float(c), new Float(d)});
        return true;
    }

    public static boolean callArrived(Object script, int thingGuid, int frameNum, int captureId) throws Exception {
        Method m = find(script.getClass(), "arrived", new Class[]{Integer.TYPE, Integer.TYPE, Integer.TYPE}, Void.TYPE);
        if (m == null) return false;
        m.invoke(script, new Object[]{new Integer(thingGuid), new Integer(frameNum), new Integer(captureId)});
        return true;
    }

    /** clicked(thingGuid, clickerGuid, captureId): somebody walked up to this thing and used it.
     *  The engine's click reaches the class's own `clicked` and invokes it with the thing and
     *  whoever clicked. A script without one simply has nothing to say, which is most of them. */
    public static boolean callClicked(Object script, int thingGuid, int clickerGuid, int captureId) throws Exception {
        Method m = find(script.getClass(), "clicked", new Class[]{Integer.TYPE, Integer.TYPE, Integer.TYPE}, Void.TYPE);
        if (m == null) return false;
        m.invoke(script, new Object[]{new Integer(thingGuid), new Integer(clickerGuid), new Integer(captureId)});
        return true;
    }

    /** killed(thingGuid, killerGuid, captureId): this thing has been destroyed, and in this engine
     *  that includes a container being opened (the container's open reaches the callback slot
     *  that the engine's callback table names `killed`). Same shape as clicked. */
    public static boolean callKilled(Object script, int thingGuid, int killerGuid, int captureId) throws Exception {
        Method m = find(script.getClass(), "killed", new Class[]{Integer.TYPE, Integer.TYPE, Integer.TYPE}, Void.TYPE);
        if (m == null) return false;
        m.invoke(script, new Object[]{new Integer(thingGuid), new Integer(killerGuid), new Integer(captureId)});
        return true;
    }

    /** Any of the (int,int,int) callbacks by name: clicked, killed, entered, exited, arrived.
     *  entered(regionGuid, moverGuid, captureId) and exited are the engine's region messages,
     *  the same two-loop dispatch clicked uses. */
    public static boolean callIII(Object script, String name, int a, int b, int c) throws Exception {
        Method m = find(script.getClass(), name, new Class[]{Integer.TYPE, Integer.TYPE, Integer.TYPE}, Void.TYPE);
        if (m == null) return false;
        m.invoke(script, new Object[]{new Integer(a), new Integer(b), new Integer(c)});
        return true;
    }

    /** Any callback, by name, parameter types and return type, answering what it returned — or
     *  null when the class has no such method. The engine resolves its callbacks with JNI's
     *  GetMethodID, which does not care whether a method is public: ClassSpiderSac's `touched` and
     *  every discipline tome's `used` are package-private, and the engine calls them. getDeclaredMethod
     *  up the class chain finds those too, and this class shares the scripts' (default) package, so
     *  invoking a package-private method from here is allowed at the JDK 1.1 API level. */
    public static Object[] invoke(Object script, String name, Class[] types, Class ret, Object[] args) throws Exception {
        Method m = find(script.getClass(), name, types, ret);
        if (m == null) return null;
        return new Object[]{ m.invoke(script, args) };
    }

    /** Whether the script's class (or a superclass) declares `name(types)` returning `ret` — the
     *  per-class method slot the engine tests before a callback (the class's method table). */
    public static boolean declares(Object script, String name, Class[] types, Class ret) {
        return find(script.getClass(), name, types, ret) != null;
    }

    /** Whether a class resolves `name(types)ret` as the engine would — for the scan of every class. */
    public static boolean resolves(Class c, String name, Class[] types, Class ret) {
        return find(c, name, types, ret) != null;
    }

    /** The method the engine's GetMethodID would find (by the {name,
     *  signature} callback table): an instance method of that name whose parameters AND return
     *  type are the signature's, declared by the class or one it extends, of any access. A method
     *  of the name and parameters that returns something else is not it — so the two shipped
     *  `void soaked` are never found for the engine's `(IIFIFI)F`, as 1.1 never finds them — and the
     *  walk goes on up the chain, where the full signature might still be declared. A static one is
     *  not it either (GetMethodID answers instance methods only). */
    static Method find(Class c, String name, Class[] types, Class ret) {
        for (; c != null; c = c.getSuperclass()) {
            try {
                Method m = c.getDeclaredMethod(name, types);
                if (m.getReturnType() == ret && !Modifier.isStatic(m.getModifiers())) return m;
            }
            catch (NoSuchMethodException e) { }
        }
        return null;
    }

    /** convreached(id, event, line, ms, speakerGuid) and convchosen(id, event, answer, ms, who):
     *  the conversation's progress callbacks (the conversation's message, and convchosen beside it). */
    public static boolean callIIIII(Object script, String name, int a, int b, int c, int d, int e) throws Exception {
        Method m = find(script.getClass(), name, new Class[]{Integer.TYPE, Integer.TYPE, Integer.TYPE, Integer.TYPE, Integer.TYPE}, Void.TYPE);
        if (m == null) return false;
        m.invoke(script, new Object[]{new Integer(a), new Integer(b), new Integer(c), new Integer(d), new Integer(e)});
        return true;
    }

    /** convended(id, aborted, returnValue) (the conversation's end). */
    public static boolean callConvEnded(Object script, int id, boolean aborted, int code) throws Exception {
        Method m = find(script.getClass(), "convended", new Class[]{Integer.TYPE, Boolean.TYPE, Integer.TYPE}, Void.TYPE);
        if (m == null) return false;
        m.invoke(script, new Object[]{new Integer(id), new Boolean(aborted), new Integer(code)});
        return true;
    }

    /** pathended(clientGuid): a camera path has run out (from the camera's path player). */
    public static boolean callPathEnded(Object script, int clientGuid) throws Exception {
        Method m = find(script.getClass(), "pathended", new Class[]{Integer.TYPE}, Void.TYPE);
        if (m == null) return false;
        m.invoke(script, new Object[]{new Integer(clientGuid)});
        return true;
    }

    public static boolean callEndscene(Object script, int clientGuid, int captureID, int exitNum) throws Exception {
        Method m = find(script.getClass(), "endscene", new Class[]{Integer.TYPE, Integer.TYPE, Integer.TYPE}, Void.TYPE);
        if (m == null) return false;
        m.invoke(script, new Object[]{new Integer(clientGuid), new Integer(captureID), new Integer(exitNum)});
        return true;
    }

    /** The engine gives each script instance its GUID, which is how the script names itself to
     *  natives like SetTimer. Codex.guid is protected, and this class is in the same (default)
     *  package, so no reflection is needed — and none is available at the JDK 1.1 API level. */
    public static void setGuid(Object script, int guid) {
        if (script instanceof Codex) ((Codex)script).guid = guid;
    }

    public static int getGuid(Object script) {
        return script instanceof Codex ? ((Codex)script).guid : 0;
    }

    /** True if the class looks like a scene script: public no-arg constructor + beginscene(int,int). */
    public static boolean isScene(String className) {
        try {
            Class c = Class.forName(className);
            c.getMethod("beginscene", new Class[]{Integer.TYPE, Integer.TYPE});
            c.getConstructor(new Class[0]);
            return true;
        } catch (Throwable t) { return false; }
    }

    /** Instantiate a scene script and run beginscene(1, 0). Returns "ok" or the failure
     *  (exception class of the underlying cause), never throws. */
    public static String tryScene(String className) {
        try {
            Object s = instantiate(className);
            return callIntInt(s, "beginscene", 1, 0) ? "ok" : "no beginscene";
        } catch (Throwable t) {
            while (t instanceof InvocationTargetException && ((InvocationTargetException)t).getTargetException() != null)
                t = ((InvocationTargetException)t).getTargetException();
            return t.getClass().getName();
        }
    }

    /** Plain-JVM entry point (log mode): ScriptHost <chronicleClass> <sceneClass> */
    public static void main(String[] args) throws Exception {
        String chronicle = args.length > 0 ? args[0] : "PragueChronicle";
        String scene = args.length > 1 ? args[1] : "HAVN_11_1";
        System.out.println("chronicle script: " + chronicle);
        EngineBridge.chronicleScript = instantiate(chronicle);
        System.out.println("scene script: " + scene);
        Object s = instantiate(scene);
        System.out.println("-> beginscene(clientGuid=1, captureID=0)");
        boolean ok = callIntInt(s, "beginscene", 1, 0);
        System.out.println(ok ? "beginscene ran; engine calls logged: " + EngineBridge.log.size() : "no beginscene(int,int) on " + scene);
    }
}
