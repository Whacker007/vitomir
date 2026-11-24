// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The scripts' half of a save (`Engine.Save.IScriptPersistence`), and the eight natives a script
// persists itself through.
//
// A script keeps exactly what its own `save(int)` writes with `SaveInt`, `SaveBoolean`, `SaveFloat`
// and `SaveString`, and `restore(int)` reads the same values back in the same order. The words are untyped in 1.1's file — four bytes each, a string a length and
// its bytes — so a `RestoreInt` after a `SaveBoolean` reads the boolean's word as a number; the
// values are kept typed here for the reader's sake and read back through their bits the same way.
//
// On a load the order is 1.1's: every thing's script is constructed from its parameters and
// `restore`d as it is read; the timers (tag 0x19) and captures (tag 0x1a) go back after
// all the things; every script is told `restored`; and only when the place
// is made current does `beginscene` go out. A restored thing is not told `created`, and a
// restored effect's script is not told `effectstarted` — 1.1 does neither.
using Vitomir.Engine.Save;
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes : IScriptPersistence
{
    /// <summary>The values a `save()` is writing, while one is running.</summary>
    List<ScriptValue>? saving;
    /// <summary>The values a `restore()` is reading, while one is running.</summary>
    Queue<ScriptValue>? restoring;

    readonly Dictionary<Location, ScriptRestore> armed = new(ReferenceEqualityComparer.Instance);
    IReadOnlyDictionary<string, ScriptSave>? armedChronicle;

    void RegisterSaving()
    {
        // Each Save* writes one word and answers true — false with no save running.
        Engine.On("CodexSequence.SaveBoolean", (_, a) => Write(ScriptValue.Of(CodexHost.Bool(a, 0))));
        Engine.On("CodexSequence.SaveInt", (_, a) => Write(ScriptValue.Of(CodexHost.Int(a, 0))));
        Engine.On("CodexSequence.SaveFloat", (_, a) => Write(ScriptValue.Of(CodexHost.Float(a, 0))));
        // SaveString answers the length it wrote, or −1 with no save running.
        Engine.On("CodexSequence.SaveString", (_, a) =>
        {
            if (saving is null) return CodexHost.Box(-1);
            string text = CodexHost.Str(a, 0) ?? "";
            saving.Add(ScriptValue.Of(text));
            return CodexHost.Box(text.Length);
        });
        // Each Restore* reads the next word; with none open, the engine's own markers.
        Engine.On("CodexSequence.RestoreBoolean", (_, _) => CodexHost.Box(Next() is { } v && Bits(v) != 0));
        Engine.On("CodexSequence.RestoreInt", (_, _) => CodexHost.Box(Next() is { } v ? Bits(v) : NoStreamInt));
        Engine.On("CodexSequence.RestoreFloat", (_, _) =>
            CodexHost.Box(Next() is { } v ? v.Float ?? BitConverter.Int32BitsToSingle(Bits(v)) : NoStreamFloat));
        Engine.On("CodexSequence.RestoreString", (_, _) => Next() is { } v ? v.String ?? "" : NoStreamString);
    }

    object Write(ScriptValue value)
    {
        if (saving is null) return CodexHost.Box(false);
        saving.Add(value);
        return CodexHost.Box(true);
    }

    ScriptValue? Next() => restoring is { Count: > 0 } q ? q.Dequeue() : null;

    /// <summary>A value as the four bytes 1.1 would have written for it.</summary>
    static int Bits(ScriptValue v) =>
        v.Int ?? (v.Bool is { } b ? (b ? 1 : 0) : v.Float is { } f ? BitConverter.SingleToInt32Bits(f) : v.String?.Length ?? 0);

    // ---- one object ----------------------------------------------------------------------------

    string ClassOf(object script) =>
        looseByObject.TryGetValue(script, out var e) ? e.Name
        : live.FirstOrDefault(l => ReferenceEquals(l.Script, script))?.Name
          ?? kept.Values.SelectMany(k => k.Live).FirstOrDefault(l => ReferenceEquals(l.Script, script))?.Name
          ?? chronicles.FirstOrDefault(c => ReferenceEquals(c.Value, script)).Key
          ?? script.GetType().Name;

    /// <summary>Runs an object's `save(flags)` and answers what it wrote — null when its class declares no `save`.</summary>
    ScriptSave? SaveOf(object script, string className, int flags)
    {
        if (!runtime.Declares(script, "save", "(I)V")) return null;
        var was = saving;
        saving = [];
        try { runtime.TryInvoke(script, "save", "(I)V", [CodexHost.Box(flags)], out _); }
        catch (Exception e) { Engine.Log($"{className}.save failed: {ScriptException.From(className, e).JavaType}"); }
        var wrote = saving;
        saving = was;
        return new ScriptSave { Class = className, Values = wrote };
    }

    /// <summary>Runs an object's `restore(flags)` over the values a save kept.</summary>
    void RestoreOf(object script, string className, ScriptSave saved, int flags)
    {
        if (!string.Equals(saved.Class, className, StringComparison.Ordinal))
        {
            Engine.Log($"a save kept a {saved.Class}'s state where there is now a {className}; not restored");
            return;
        }
        if (!runtime.Declares(script, "restore", "(I)V")) return;
        var was = restoring;
        restoring = new Queue<ScriptValue>(saved.Values);
        try { runtime.TryInvoke(script, "restore", "(I)V", [CodexHost.Box(flags)], out _); }
        catch (Exception e) { Engine.Log($"{className}.restore failed: {ScriptException.From(className, e).JavaType}"); }
        restoring = was;
    }

    void Restored(object script, string className, int flags)
    {
        try { runtime.TryInvoke(script, "restored", "(I)V", [CodexHost.Box(flags)], out _); }
        catch (Exception e) { Engine.Log($"{className}.restored failed: {ScriptException.From(className, e).JavaType}"); }
    }

    // ---- IScriptPersistence --------------------------------------------------------------------

    /// <inheritdoc/>
    public ScriptSave? SaveObject(object script) => SaveOf(script, ClassOf(script), 0);

    /// <inheritdoc/>
    public Dictionary<string, ScriptSave> SaveChronicle()
    {
        var saved = new Dictionary<string, ScriptSave>(StringComparer.Ordinal);
        foreach (var (name, script) in chronicles)
            if (SaveOf(script, name, 1) is { } s) saved[name] = s;
        return saved;
    }

    /// <inheritdoc/>
    public PlaceScripts Save(Location place, Func<int, string?> thingKey, Func<object, string?> looseKey)
    {
        var result = new PlaceScripts();
        List<Live> scripts;
        List<Pending> pending;
        float clock;
        if (ReferenceEquals(place, location)) { scripts = live; pending = timers; clock = now; }
        else if (kept.TryGetValue(place, out var k)) { scripts = k.Live; pending = k.Timers; clock = k.Now; }
        else return result;
        result.ClockMs = (long)Math.Round(clock * 1000.0);

        foreach (var l in scripts)
            if (l.Thing > 0 && SaveOf(l.Script, l.Name, 0) is { } s) result.ByThing[l.Thing] = s;

        // A script is named by what it belongs to: its thing, or its character's effect or discipline.
        string? KeyOf(int scriptGuid)
        {
            if (scripts.FirstOrDefault(l => l.Guid == scriptGuid) is { } l) return l.Thing > 0 ? thingKey(l.Thing) : null;
            if (loose.TryGetValue(scriptGuid, out var e) && e.Script is { } obj) return looseKey(obj);
            return null;
        }
        foreach (var t in pending)
            if (KeyOf(t.Guid) is { } key)
                result.Timers.Add(new TimerSave
                {
                    Script = key, Id = t.Id, InMs = (int)Math.Round((t.At - clock) * 1000.0), Args = [t.A, t.B, t.C, t.D],
                });
        foreach (var thing in place.Things)
            foreach (var hold in thing.Captures)
                if (KeyOf(hold.Script) is { } key) result.Captures.Add((thing.Guid, new CaptureSave { Script = key, Id = hold.Id }));
        return result;
    }

    /// <inheritdoc/>
    public void Arm(Location place, ScriptRestore restore) => armed[place] = restore;

    /// <inheritdoc/>
    public void ArmChronicle(IReadOnlyDictionary<string, ScriptSave> saved) => armedChronicle = saved;

    /// <inheritdoc/>
    public void Reset()
    {
        if (location is not null)
        {
            live.Clear();
            timers.Clear();
            scriptThing.Clear();
            location = null;
        }
        kept.Clear();
        armed.Clear();
        armedChronicle = null;
        loose.Clear();
        looseByObject.Clear();
        carriedTimers.Clear();
        pendingArrivals.Clear();
        chronicles.Clear();
        chronicle = null;
        ChronicleName = null;
    }

    /// <summary>
    /// The restore half of <see cref="Enter"/>, between making the scripts and beginning the scene.
    /// <paramref name="made"/> is every script just made for the place's things.
    /// </summary>
    void RestoreScripts(Location loc, ScriptRestore restore, List<(Live Script, WorldThing Thing)> made)
    {
        const int flags = 0;
        foreach (var (l, thing) in made)
        {
            if (restore.ByThing.TryGetValue(thing.Guid, out var saved)) RestoreOf(l.Script, l.Name, saved, flags);
            if (l.Name == PendulumClass) PendulumHome(thing);                   // the repair `pendulumhome`
        }

        // The engine-made objects the load made for its characters' effects and disciplines.
        var looseMade = new List<Loose>();
        foreach (var e in loose.Values)
            if (e.Script is { } obj && ReferenceEquals(e.Owner.Place, loc) && restore.Loose?.Invoke(obj) is { } saved)
            {
                RestoreOf(obj, e.Name, saved, flags);
                looseMade.Add(e);
            }

        int ScriptGuidOf(string key)
        {
            if (restore.ThingOf?.Invoke(key) is > 0 and var thingGuid)
                return live.FirstOrDefault(l => l.Thing == thingGuid)?.Guid ?? 0;
            if (restore.LooseOf?.Invoke(key) is { } obj && looseByObject.TryGetValue(obj, out var e)) return e.Guid;
            return 0;
        }
        foreach (var t in restore.Timers)
        {
            int guid = ScriptGuidOf(t.Script);
            if (guid == 0) { Engine.Log($"a saved timer belongs to {t.Script}, which has no script now"); continue; }
            var a = t.Args.Length >= 4 ? t.Args : [.. t.Args, .. new float[4 - t.Args.Length]];
            timers.Add(new Pending(now + t.InMs / 1000f, guid, t.Id, a[0], a[1], a[2], a[3]));
        }
        foreach (var (thingGuid, c) in restore.Captures)
        {
            int guid = ScriptGuidOf(c.Script);
            if (guid == 0 || loc.Thing(thingGuid) is not { } thing) continue;
            // 1.1's reader hands each record to CaptureThing itself: a script that
            // already holds the thing — its constructor captured it again — only has its id set, and a new
            // hold goes on the front. Until then a hold was appended unless the same script held
            // it with the same id, so a constructor's hold under another id left the script holding the
            // thing twice (and hearing its callbacks twice), and the holds came back in another order.
            thing.TakeHold(guid, c.Id);
        }

        foreach (var (l, _) in made) Restored(l.Script, l.Name, flags);
        foreach (var e in looseMade) if (e.Script is { } obj) Restored(obj, e.Name, flags);
    }

    /// <summary>The chronicle scripts' saved states, restored once, the first time a place is entered after a load.</summary>
    void RestoreChronicle()
    {
        if (armedChronicle is not { } saved) return;
        armedChronicle = null;
        var restoredNow = new List<(string, object)>();
        foreach (var (name, script) in ChronicleScripts())
            if (saved.TryGetValue(name, out var s)) { RestoreOf(script, name, s, 1); restoredNow.Add((name, script)); }
        foreach (var (name, script) in restoredNow) Restored(script, name, 1);
    }
}
