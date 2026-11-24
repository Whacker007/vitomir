// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The vampire verbs, from the scripts' side: the script objects the engine makes for effects and
// disciplines, the callbacks they hear, and the natives the discipline and effect scripts call.
//
// A discipline in this game is two scripts and a row of numbers. `game.ndd` says what it costs and
// whom it may target; `DisciplineCelerity.cast` decides whether it fizzles and puts two effects on
// the caster; `effects.ned` says those effects multiply move speed; and `EffectDiscCelerity`
// spawns the blue trail when the first one starts and takes it away when it ends. The engine half
// of that (`Rpg.Cast`, `Rpg.EffectRuntime`) is in Vitomir.Engine; this is the half that meets Java.
//
// Every native here follows the original's. Where a native's answer is odd — a refused order that
// answers true, a lock that answers 255 when it fails — the oddity is the engine's and is kept.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes : IScriptObjects
{
    // ---- the script objects the engine makes of its own accord -------------------------------

    /// <summary>
    /// A script the engine made for an effect or a discipline: a Codex object
    /// of the named class with a script GUID of its own from the same counter as every other script,
    /// whose `GetClassThing` answers its owner. It can set timers and
    /// capture things like any other script — `EffectDiscCelerity` captures the actor it is on so
    /// that it hears its death — and freeing it drops both.
    /// </summary>
    sealed class Loose(string name, int guid, WorldThing owner)
    {
        public string Name { get; } = name;
        public int Guid { get; } = guid;
        public WorldThing Owner { get; set; } = owner;
        public object? Script { get; set; }
    }

    /// <summary>The engine-made scripts, by script GUID. One set for the whole run: they go where their owner goes.</summary>
    readonly Dictionary<int, Loose> loose = [];
    readonly Dictionary<object, Loose> looseByObject = new(ReferenceEqualityComparer.Instance);

    /// <summary>A party member's engine-made scripts' timers, carried through an exit with the time they had left.</summary>
    readonly List<(Pending Timer, float Left)> carriedTimers = [];

    /// <inheritdoc/>
    public object? Make(string className, WorldThing owner)
    {
        if (className.Length == 0) return null;
        int guid = nextScriptGuid++;
        // Registered before the constructor runs, so a constructor that asks `GetClassThing` is
        // answered — the engine writes the owner into the wrapper before it instantiates.
        var entry = new Loose(className, guid, owner);
        loose[guid] = entry;
        try
        {
            var script = runtime.NewScript(className, guid, []);
            entry.Script = script;
            looseByObject[script] = entry;
            return script;
        }
        catch (ScriptException e)
        {
            loose.Remove(guid);
            Engine.Log($"could not make the {className} for {owner.Template}: {e.JavaType}");
            return null;
        }
    }

    /// <inheritdoc/>
    public void Free(object script)
    {
        if (!looseByObject.Remove(script, out var entry)) return;
        loose.Remove(entry.Guid);
        // Its timers go, and every capture it holds is released.
        timers.RemoveAll(t => t.Guid == entry.Guid);
        carriedTimers.RemoveAll(t => t.Timer.Guid == entry.Guid);
        foreach (var k in kept.Values) k.Timers.RemoveAll(t => t.Guid == entry.Guid);
        foreach (var thing in location?.Things ?? [])
            thing.Captures.RemoveAll(c => c.Script == entry.Guid);
    }

    /// <inheritdoc/>
    public void Rehome(object script, WorldThing owner)
    {
        if (looseByObject.TryGetValue(script, out var entry)) entry.Owner = owner;
    }

    /// <summary>A script by its GUID, whichever kind it is — a scene's own, or one the engine made.</summary>
    (string Name, object Script)? ScriptOf(int guid)
    {
        if (live.FirstOrDefault(l => l.Guid == guid) is { } l) return (l.Name, l.Script);
        if (loose.TryGetValue(guid, out var e) && e.Script is { } s) return (e.Name, s);
        return null;
    }

    /// <summary>
    /// The engine-made scripts' part in entering a place: a character that learnt a discipline or
    /// had an effect put on it before there were scripts to make — which is every character, since
    /// the place wakes before its scripts are started — gets its objects now. Only what is still
    /// missing is made, so a place visited before is left as it was.
    /// </summary>
    void MakeMissing(Location place, bool started = true)
    {
        foreach (var thing in place.Things)
        {
            if (thing.Actor is not { } who) continue;
            foreach (var d in who.Disciplines.All)
                if (d.Script is null && d.Definition.Script.Length > 0) d.Script = Make(d.Definition.Script, thing);
            foreach (var e in thing.Effects.All)
            {
                if (e.Script is not null || e.Spent || e.Definition.Script.Length == 0) continue;
                e.Script = Make(e.Definition.Script, thing);
                // A restored effect's script is made and restored, and does not hear it start again.
                if (started && e.Script is { } s)
                    EffectStarted(s, thing, e, e.Until != 0 ? e.Until - place.Milliseconds : 0);
            }
        }
    }

    // ---- the callbacks -------------------------------------------------------------------------

    void Say(string what, object script, string name, string signature, object?[] args)
    {
        try { runtime.TryInvoke(script, name, signature, args, out _); }
        catch (Exception e) { Engine.Log($"{what}.{name} failed: {ScriptException.From(what, e).JavaType}"); }
    }

    string NameOf(object script) => looseByObject.TryGetValue(script, out var e) ? e.Name : script.GetType().Name;

    /// <inheritdoc/>
    public void EffectStarted(object script, WorldThing actor, Effect effect, int duration)
    {
        using var mark = Vitomir.Engine.Net.MadeHere.ByEffect(actor, effect);             // each player's machine runs it too
        using var made = EffectMade.By(actor, effect);                                    // ours: what it makes, for a save (World/EffectMade.cs)
        Say(NameOf(script), script, "effectstarted", "(IIII)V",
            [CodexHost.Box(actor.Guid), CodexHost.Box(effect.Guid), CodexHost.Box(effect.CreatorGuid), CodexHost.Box(duration)]);
    }

    /// <inheritdoc/>
    public void EffectEnded(object script, WorldThing actor, Effect effect, int reason)
    {
        using var mark = Vitomir.Engine.Net.MadeHere.ByEffect(actor, effect);
        using var made = EffectMade.By(actor, effect);
        Say(NameOf(script), script, "effectended", "(IIII)V",
            [CodexHost.Box(actor.Guid), CodexHost.Box(effect.Guid), CodexHost.Box(effect.CreatorGuid), CodexHost.Box(reason)]);
    }

    /// <inheritdoc/>
    public bool CanCast(object script, int level, WorldThing caster, WorldThing? target, Vector3? at)
    {
        try { return AskCanCast(script, level, caster, target, at); }
        catch (Exception e)
        {
            // The JNI exception is cleared and the call's value is what CallBooleanMethod left: 0.
            Engine.Log($"{NameOf(script)}.cancast failed: {ScriptException.From(NameOf(script), e).JavaType}");
            return false;
        }
    }

    bool AskCanCast(object script, int level, WorldThing caster, WorldThing? target, Vector3? at)
    {
        var p = at ?? Vector3.Zero;
        object?[] args = [CodexHost.Box(level), CodexHost.Box(caster.Guid), CodexHost.Box(target?.Guid ?? 0),
                          CodexHost.Box(p.X), CodexHost.Box(p.Y), CodexHost.Box(p.Z), CodexHost.Box(at is not null)];
        // A class without `cancast` answers yes (as 1.1's does).
        if (!runtime.TryInvoke(script, "cancast", "(IIIFFFZ)Z", args, out var answer)) return true;
        return answer is java.lang.Boolean b && b.booleanValue();
    }

    /// <summary>
    /// **What a player's machine may read for a discipline's <c>cancast</c>** (<see cref="Judge"/>): the natives the shipped
    /// classes' <c>cancast</c> call whose answers that machine has as the host has them — whether a GUID is a character, a player or an
    /// item (the same things, by the same numbers); a character's flags and second flags (her own in her sheet, another's in the flags
    /// message, sent when they change: <see cref="Vitomir.Engine.Net.ThingFlags"/>), its type (her own in her sheet; another's is its template's,
    /// which no multiplayer chronicle's script changes — only two single-player ones call <c>SetActorType</c>), its team (the flags
    /// message), its health (the snapshots, ten a second) and its maximum (her sheet; another's its template's, which a heal does not
    /// move). Anything else read — <c>AIGetNumPets</c> (the minds are the host's), <c>GetActorStat</c> of another (their blood rides
    /// the flags message only when something else changes), <c>GetItemFlags</c> (her pack is the host's), the place's location and
    /// the chronicle's flags (the host's journeys) — makes the judge answer nothing.
    /// </summary>
    static readonly HashSet<string> JudgeReads = new(StringComparer.Ordinal)
    {
        "Codex.IsActorGuid", "Codex.IsPlayerGuid", "Codex.IsItemGuid",
        "CodexActor.GetActorFlags", "CodexActor.GetActorFlags2", "CodexActor.GetActorType", "CodexActor.GetActorTeam",
        "CodexActor.GetActorHealth", "CodexActor.GetActorMaxHealth",
    };

    /// <summary>The lines a <c>cancast</c> may print (a refusal's <c>DisplayNoCast</c>, a bad call's <c>PrintError</c>): the judge says none of them (the host's own <c>cancast</c> sends its own).</summary>
    static readonly HashSet<string> JudgeQuiet = new(StringComparer.Ordinal)
    {
        "CodexConsole.Print", "CodexConsole.PrintNLS", "CodexConsole.PrintFormatNLS", "CodexConsole.PrintError", "CodexConsole.PrintException",
    };

    /// <inheritdoc/>
    public bool? Judge(object script, int level, WorldThing caster, WorldThing? target, Vector3? at)
    {
        bool foreign = false;
        var was = Engine.Judging;
        Engine.Judging = key =>
        {
            if (JudgeReads.Contains(key)) return true;
            if (!JudgeQuiet.Contains(key)) foreign = true;
            return false;
        };
        try
        {
            bool answer = AskCanCast(script, level, caster, target, at);
            return foreign ? null : answer;
        }
        catch (Exception e)
        {
            Engine.LogMissing($"{NameOf(script)}.cancast not judged on this machine: {ScriptException.From(NameOf(script), e).JavaType}");
            return null;
        }
        finally { Engine.Judging = was; }
    }

    /// <inheritdoc/>
    public void Cast(object script, CastForm form, int level, WorldThing caster, WorldThing? target, Vector3 at)
    {
        switch (form)
        {
            case CastForm.Area:
                Say(NameOf(script), script, "cast", "(IIFFF)I",
                    [CodexHost.Box(level), CodexHost.Box(caster.Guid), CodexHost.Box(at.X), CodexHost.Box(at.Y), CodexHost.Box(at.Z)]);
                break;
            case CastForm.Thing:
                Say(NameOf(script), script, "cast", "(III)I",
                    [CodexHost.Box(level), CodexHost.Box(caster.Guid), CodexHost.Box(target?.Guid ?? 0)]);
                break;
            default:
                Say(NameOf(script), script, "cast", "(II)I", [CodexHost.Box(level), CodexHost.Box(caster.Guid)]);
                break;
        }
    }

    /// <summary>The scripts a thing carries itself, which hear its events with a capture id of 0.</summary>
    Live[] OwnScripts(WorldThing thing) => [.. live.Where(l => l.Thing == thing.Guid)];

    /// <inheritdoc/>
    /// <remarks>
    /// **The capture loop has the original game's bug, kept.** For each capture on the caster whose class
    /// declares `actorcast`, 1.1 calls **the caster's own script's** `actorcast` again with that
    /// capture's id, not the capturing script's. With no script of its
    /// own the original dereferences null; that case is logged and skipped. In the shipped content
    /// only `player_christof` declares `actorcast` and it captures nothing, so it never shows.
    /// </remarks>
    public void ActorCast(WorldThing caster, string discipline, int level, int castNum)
    {
        object?[] Args(int capture) =>
            [CodexHost.Box(caster.Guid), discipline, CodexHost.Box(level), CodexHost.Box(castNum), CodexHost.Box(capture)];
        const string sig = "(ILjava/lang/String;III)V";
        var own = OwnScripts(caster).FirstOrDefault();
        if (own is not null) Say(own.Name, own.Script, "actorcast", sig, Args(0));
        foreach (var hold in caster.Captures.ToArray())
        {
            if (ScriptOf(hold.Script) is not { } holder || !runtime.Declares(holder.Script, "actorcast", sig)) continue;
            if (own is null) { Engine.Log($"actorcast: {caster.Template} has a capture that wants it and no script of its own (the original crashes here)"); continue; }
            Say(own.Name, own.Script, "actorcast", sig, Args(hold.Id));
        }
    }

    /// <inheritdoc/>
    public void ActorFeed(WorldThing feeder, WorldThing victim, int status)
    {
        object?[] Args(int capture) =>
            [CodexHost.Box(feeder.Guid), CodexHost.Box(victim.Guid), CodexHost.Box(status), CodexHost.Box(capture)];
        foreach (var own in OwnScripts(feeder)) Say(own.Name, own.Script, "actorfeed", "(IIII)V", Args(0));
        foreach (var hold in feeder.Captures.ToArray())
            if (ScriptOf(hold.Script) is { } s) Say(s.Name, s.Script, "actorfeed", "(IIII)V", Args(hold.Id));
    }

    /// <inheritdoc/>
    public void Stilled(WorldThing actor) => Hold(actor, "actorstill");

    /// <inheritdoc/>
    public void Unstill(WorldThing actor) => Hold(actor, "actorunstill");

    void Hold(WorldThing actor, string callback)
    {
        foreach (var own in OwnScripts(actor))
            Say(own.Name, own.Script, callback, "(II)V", [CodexHost.Box(actor.Guid), CodexHost.Box(0)]);
        foreach (var hold in actor.Captures.ToArray())
            if (ScriptOf(hold.Script) is { } s)
                Say(s.Name, s.Script, callback, "(II)V", [CodexHost.Box(actor.Guid), CodexHost.Box(hold.Id)]);
    }

    // ---- the natives ---------------------------------------------------------------------------

    /// <summary>
    /// The CodexActor guard every one of these starts with: the object's thing is a character —
    /// `THING_TYPE_ACTOR` or `THING_TYPE_PLAYER` (`t+4 ∈ {0, 7}`).
    /// </summary>
    WorldThing? Character(object? self) =>
        Of(self) is { Actor: not null, Type: ThingType.Actor or ThingType.Player } t ? t : null;

    /// <summary>A name argument as every native copies it: 31 characters at most (`strncpy(buf, s, 32); buf[31] = 0`).</summary>
    static string Name31(object?[] a, int i)
    {
        string s = CodexHost.Str(a, i) ?? "";
        return s.Length > 31 ? s[..31] : s;
    }

    /// <summary>What `GetActorEffectIntParam` answers for nothing: −9,999,999.</summary>
    const int NoIntParam = -9_999_999;
    /// <summary>What `GetActorEffectFloatParam` answers for nothing: −1e7.</summary>
    const float NoFloatParam = -1e7f;
    /// <summary>What the level and value lookups answer when the thing is not a character: −2.</summary>
    const int NotACharacter = -2;

    /// <summary>The discipline table, once the place has been given it.</summary>
    DisciplineDefinitions? Powers => location?.Powers;

    int Now => location?.Milliseconds ?? 0;

    void RegisterPowers()
    {
        RegisterEffects();
        RegisterDisciplines();
        RegisterVerbs();
        RegisterMinds();
        RegisterModels();
        RegisterQueries();
        RegisterCollision();
        RegisterShapes();
    }

    /// <summary>The effect natives, each a thin door onto `Rpg.EffectRuntime`.</summary>
    void RegisterEffects()
    {
        // By level (which holds the level to at most 5) and by value.
        // Both answer the new instance's GUID, or 0 — for an unknown name, a
        // negator (which removes instead of adding) and a refused possession alike.
        Engine.On("CodexActor.AddActorEffectByLevel", (self, a) =>
        {
            if (Character(self) is not { } t) return CodexHost.Box(0);
            int creator = CodexHost.Int(a, 3);
            var e = EffectRuntime.Apply(t, Name31(a, 0), CodexHost.Int(a, 2), durationMs: CodexHost.Int(a, 1),
                                        creator: location?.Thing(creator), flags: (uint)CodexHost.Int(a, 4),
                                        intParam: CodexHost.Int(a, 5), floatParam: CodexHost.Float(a, 6),
                                        creatorGuid: creator);
            return CodexHost.Box(e?.Guid ?? 0);
        });
        Engine.On("CodexActor.AddActorEffectByValue", (self, a) =>
        {
            if (Character(self) is not { } t) return CodexHost.Box(0);
            int creator = CodexHost.Int(a, 3);
            var e = EffectRuntime.Apply(t, Name31(a, 0), -1, durationMs: CodexHost.Int(a, 1),
                                        creator: location?.Thing(creator), value: CodexHost.Float(a, 2),
                                        flags: (uint)CodexHost.Int(a, 4), intParam: CodexHost.Int(a, 5),
                                        floatParam: CodexHost.Float(a, 6), creatorGuid: creator);
            return CodexHost.Box(e?.Guid ?? 0);
        });

        // Find — by GUID, by name (case-insensitive), or by kind and subtype
        // — answers the instance's GUID or 0. Only a removed instance is skipped.
        Engine.On("CodexActor.FindActorEffect", (self, a) =>
        {
            if (Character(self) is not { } t) return CodexHost.Box(0);
            return CodexHost.Box(Find(t, a)?.Guid ?? 0);
        });

        // Remove answers whether there was one to remove (with notify).
        Engine.On("CodexActor.RemoveActorEffect", (self, a) =>
        {
            if (Character(self) is not { } t) return CodexHost.Box(false);
            return CodexHost.Box(Find(t, a) is { } e && EffectRuntime.Remove(t, e, notify: true));
        });

        // By kind and subtype (int, int) or by name (String): −2 for none.
        Engine.On("CodexActor.GetActorEffectLevel", (self, a) =>
            CodexHost.Box(Character(self) is { } t ? Find(t, a)?.Level ?? NotACharacter : NotACharacter));
        // Not a character is −2; a character without it is 0 (the lookup's "none"), by name
        // or by kind.
        Engine.On("CodexActor.GetActorEffectValue", (self, a) =>
            CodexHost.Box(Character(self) is { } t ? Find(t, a)?.Value ?? 0f : NotACharacter));
        Engine.On("CodexActor.GetActorEffectIntParam", (self, a) =>
            CodexHost.Box(Character(self) is { } t && Find(t, a) is { } e ? e.IntParam : NoIntParam));
        Engine.On("CodexActor.GetActorEffectFloatParam", (self, a) =>
            CodexHost.Box(Character(self) is { } t && Find(t, a) is { } e ? e.FloatParam : NoFloatParam));

        // `ExpandActorEffect(which, duration, bAppend)`.
        Engine.On("CodexActor.ExpandActorEffect", (self, a) =>
        {
            if (Character(self) is not { } t || Find(t, a) is not { } e) return CodexHost.Box(false);
            EffectRuntime.Expand(e, CodexHost.Int(a, 1), CodexHost.Bool(a, 2), Now);
            return CodexHost.Box(true);
        });

        // The fold itself, marks and all.
        Engine.On("CodexActor.ComputeActorEffectsValue", (self, a) =>
            CodexHost.Box(Character(self) is { } t
                ? t.Effects.Value((EffectKind)CodexHost.Int(a, 0), CodexHost.Int(a, 1)) : 0f));

        // Healing goes through the engine's own heal: nothing for an amount
        // that is not above 0, nothing for the dying, and a vampire's frenzy eased by the amount.
        Engine.On("CodexActor.HealActor", (self, a) =>
        {
            float amount = CodexHost.Float(a, 0);
            if (Character(self) is not { } t || !(amount > 0f)) return CodexHost.Box(false);
            EffectRuntime.Heal(t, amount);
            return CodexHost.Box(true);
        });
    }

    /// <summary>
    /// The three ways the effect natives name an instance, told apart by what they were given: a
    /// string is a name, one int a GUID, two ints a kind and subtype.
    /// </summary>
    static Effect? Find(WorldThing t, object?[] a)
    {
        if (a.Length > 0 && a[0] is string) return t.Effects.Find(Name31(a, 0));
        if (a.Length >= 2 && a[1] is java.lang.Integer && a[0] is java.lang.Integer && a.Length == 2)
            return t.Effects.Find((EffectKind)CodexHost.Int(a, 0), CodexHost.Int(a, 1));
        return t.Effects.Find(CodexHost.Int(a, 0));
    }

    /// <summary>The discipline natives.</summary>
    void RegisterDisciplines()
    {
        // −2 for "not a character" and for "does not have it at all"; −1 is "has it, not learnt".
        Engine.On("CodexActor.GetActorDisciplineLevel", (self, a) =>
            CodexHost.Box(Character(self)?.Actor?.Disciplines[Name31(a, 0)]?.Level ?? NotACharacter));
        // −1..4 only; the discipline found or made and set, which opens the next of the
        // group too. False only for an unknown name.
        Engine.On("CodexActor.SetActorDisciplineLevel", (self, a) =>
        {
            int level = CodexHost.Int(a, 1);
            if (Character(self) is not { } t || level < -1 || level > 4 || Powers is not { } powers)
                return CodexHost.Box(false);
            return CodexHost.Box(DisciplineBook.SetLevel(t, powers, Name31(a, 0), level) is not null);
        });
        // −1..5, and only a discipline it already has. No eviction, no bar refresh.
        Engine.On("CodexActor.SetActorDisciplineSlot", (self, a) =>
        {
            int slot = CodexHost.Int(a, 1);
            if (Character(self)?.Actor?.Disciplines[Name31(a, 0)] is not { } d || slot < -1 || slot > 5)
                return CodexHost.Box(false);
            d.QuickSlot = slot;
            return CodexHost.Box(true);
        });
        // The player-only state bit, which no shipped script reads or writes.
        Engine.On("CodexPlayer.GetPlayerDisciplineState", (self, a) =>
            CodexHost.Box(Of(self) is { Type: ThingType.Player, Actor: { } p } && p.Disciplines[Name31(a, 0)] is { Active: true }));
        Engine.On("CodexPlayer.SetPlayerDisciplineState", (self, a) =>
        {
            if (Of(self) is not { Type: ThingType.Player, Actor: { } p } || p.Disciplines[Name31(a, 0)] is not { } d)
                return CodexHost.Box(false);
            bool was = d.Active;
            d.Active = CodexHost.Bool(a, 1);
            return CodexHost.Box(was);
        });
        // The recast of the **selected** discipline at the level asked, not at its own.
        Engine.On("CodexPlayer.GetPlayerDisciplineReloadTime", (self, a) =>
        {
            int level = CodexHost.Int(a, 0);
            if (level < 0 || level >= NddDiscipline.Levels) return CodexHost.Box(-1);
            if (Of(self) is not { Type: ThingType.Player, Actor.Disciplines.Current: { } d }) return CodexHost.Box(-1);
            return CodexHost.Box(d.Definition.RecastMs[level]);
        });
        // A script's cast order: the player's own order with Space held (mods 8), and **true
        // whatever it answered** — refused, busy or taken.
        Engine.On("CodexActor.ActorActionCast", (self, a) =>
        {
            if (Character(self) is not { Actor: { } me } t || me.Disciplines[Name31(a, 1)] is not { } d)
                return CodexHost.Box(false);
            int guid = CodexHost.Int(a, 0);
            WorldThing? target = null;
            if (guid != 0)
            {
                target = location?.Thing(guid);
                if (target is null || target.Type is not (ThingType.Actor or ThingType.Player)) return CodexHost.Box(false);
            }
            Vitomir.Engine.Rpg.Cast.Order(t, target, null, d, InputModifier.Space);
            return CodexHost.Box(true);
        });
        Engine.On("CodexActor.ActorActionCastPos", (self, a) =>
        {
            if (Character(self) is not { Actor: { } me } t || me.Disciplines[Name31(a, 1)] is not { } d)
                return CodexHost.Box(false);
            Vitomir.Engine.Rpg.Cast.Order(t, null, Vec(a, 0), d, InputModifier.Space);
            return CodexHost.Box(true);
        });
    }

    /// <summary>Feeding, frenzy, holding still, harm and revival — the actor verbs a script can order.</summary>
    void RegisterVerbs()
    {
        // A character to feed on, and **anything but 0 is true** — refused and busy too.
        Engine.On("CodexActor.Feed", (self, a) =>
        {
            if (Character(self) is not { } t) return CodexHost.Box(false);
            if (location?.Thing(CodexHost.Int(a, 0)) is not { Type: ThingType.Actor or ThingType.Player } victim)
                return CodexHost.Box(false);
            return CodexHost.Box(Feeding.Order(t, victim) != 0);
        });
        // Stop, whatever it is doing.
        Engine.On("CodexActor.StopFeeding", (self, _) =>
        {
            if (Character(self) is { } t) Feeding.Stop(t);
        });
        // Both are `ef_increasefrenzy` by value, +100 and −99, with no end.
        Engine.On("CodexActor.ActorFrenzy", (self, _) =>
        {
            if (Character(self) is not { } t) return CodexHost.Box(false);
            Frenzy.Provoke(t);
            return CodexHost.Box(true);
        });
        Engine.On("CodexActor.ActorUnfrenzy", (self, _) =>
        {
            if (Character(self) is not { } t) return CodexHost.Box(false);
            Frenzy.Soothe(t);
            return CodexHost.Box(true);
        });
        Engine.On("CodexActor.CancelActorAction", (self, _) =>
        {
            if (Character(self) is not { } t) return CodexHost.Box(false);
            Actions.Cancel(t);
            return CodexHost.Box(true);
        });
        // True only when the hold was taken (the hold's answer is 4).
        Engine.On("CodexActor.ActorStill", (self, _) =>
            CodexHost.Box(Character(self) is { } t && Actions.Still(t) == OrderResult.Started));
        // Above 0, a type of 0..9, an attacker who may be nobody; hit kind 0; the answer
        // is true once the damage was dealt, whatever it came to.
        Engine.On("CodexActor.DamageActor", (self, a) =>
        {
            float amount = CodexHost.Float(a, 0);
            int type = CodexHost.Int(a, 1);
            if (Character(self) is not { } t || !(amount > 0f) || type < 0 || type >= 10) return CodexHost.Box(false);
            var by = location?.Thing(CodexHost.Int(a, 2));
            Damage.Apply(t, by, HitKind.Script, (DamageType)type, amount,
                         location?.Minds?.Random ?? new Rng(1), Now, motions: location?.Motions);
            return CodexHost.Box(true);
        });
        // No range checks, no dead check, the awakening noise asked for; always true.
        Engine.On("CodexActor.ReviveActor", (self, a) =>
        {
            if (Character(self) is not { } t) return CodexHost.Box(false);
            Damage.Revive(t, CodexHost.Float(a, 0), CodexHost.Float(a, 1), sound: true);
            return CodexHost.Box(true);
        });

        // The clan's summoning and shape templates: the template name in lower
        // case, or one of the engine's error strings.
        Engine.On("CodexActor.GetActorClanBeckoningTemplate", (self, _) => ClanTemplate(self, beckon: true));
        Engine.On("CodexActor.GetActorClanShapeTemplate", (self, _) => ClanTemplate(self, beckon: false));
    }

    string ClanTemplate(object? self, bool beckon)
    {
        if (Character(self)?.Actor is not { } me) return "***ERR:Actor***";
        if (me.ClanRecord is not { } clan) return "***ERR:NoClan***";
        string name = beckon ? clan.Beckon : clan.Shape;
        // `"none"` or a template the table does not have is left NULL by the loader.
        if (name.Length == 0 || location?.Templates is not { } templates || !templates.Has(name))
            return beckon ? "***ERR:NoBeckoningTemplate***" : "***ERR:NoShapeTemplate***";
        return name.ToLowerInvariant();
    }

    /// <summary>
    /// The AI natives — every one of them, since there is no AISetMode or
    /// AIGetPet. Each needs a character with a mind.
    /// </summary>
    void RegisterMinds()
    {
        Mind? MindOf(object? self) => Character(self)?.Mind;
        // "A valid target": a character in the same place — dead is not tested here.
        WorldThing? Valid(Mind m) =>
            m.Target is { Type: ThingType.Actor or ThingType.Player } t && ReferenceEquals(t.Place, m.Thing.Place) ? t : null;
        int InMode(object? self, AiMode mode) =>
            MindOf(self) is not { } m ? -1 : m.Mode == mode && Valid(m) is { } t ? t.Guid : 0;

        Engine.On("CodexActor.AIGetTarget", (self, _) =>
            CodexHost.Box(MindOf(self) is not { } m ? -1 : Valid(m)?.Guid ?? 0));
        // The target must be a character; SetTarget (which keeps nobody for a corpse, and
        // wakes a sleeper anyway), then the mode goes to ENEMY — lock or no lock — and the home
        // mode is left alone. True whatever happened.
        Engine.On("CodexActor.AISetTarget", (self, a) =>
        {
            if (MindOf(self) is not { } m || location?.Minds is not { } minds) return CodexHost.Box(false);
            if (location.Thing(CodexHost.Int(a, 0)) is not { Type: ThingType.Actor or ThingType.Player } t)
                return CodexHost.Box(false);
            minds.SetTarget(m, t);
            m.Enter(AiMode.Enemy, andBase: false);
            return CodexHost.Box(true);
        });
        Engine.On("CodexActor.AIGetMode", (self, _) => CodexHost.Box(MindOf(self) is { } m ? (int)m.Mode : -1));
        // The lock is bit 0x1 of the mind's flags — the same bit that keeps the mind-altering modes
        // out, so a locked mind cannot be mesmerised either.
        Engine.On("CodexActor.AIIsModeLocked", (self, _) =>
            CodexHost.Box(MindOf(self) is { } m && (m.Flags & Mind.FlagUnaffected) != 0));
        // The lock natives answer the old bit — and **255 when they fail** (`al = 0xff`), which a
        // Java `if` reads as true; a boolean here can only say true.
        Engine.On("CodexActor.AILockMode", (self, _) =>
        {
            if (MindOf(self) is not { } m) return CodexHost.Box(true);
            bool was = (m.Flags & Mind.FlagUnaffected) != 0;
            m.Flags |= Mind.FlagUnaffected;
            return CodexHost.Box(was);
        });
        Engine.On("CodexActor.AIUnlockMode", (self, _) =>
        {
            if (MindOf(self) is not { } m) return CodexHost.Box(true);
            bool was = (m.Flags & Mind.FlagUnaffected) != 0;
            m.Flags &= ~Mind.FlagUnaffected;
            return CodexHost.Box(was);
        });
        Engine.On("CodexActor.AIIsFrenzied", (self, _) => CodexHost.Box(MindOf(self) is { Mode: AiMode.Frenzied }));
        // The original's answer is the possessed bit (0x10000000) of a 32-bit word, of which Java's boolean keeps only the
        // low byte: **always false**.
        Engine.On("CodexActor.AIIsPossessed", (_, _) => CodexHost.Box(false));
        Engine.On("CodexActor.AIIsAfraid", (self, _) => CodexHost.Box(InMode(self, AiMode.Afraid)));
        Engine.On("CodexActor.AIIsDazed", (self, _) => CodexHost.Box(InMode(self, AiMode.Dazed)));
        Engine.On("CodexActor.AIIsMesmerized", (self, _) => CodexHost.Box(InMode(self, AiMode.Mesmerized)));
        Engine.On("CodexActor.AIIsPossessing", (self, _) => CodexHost.Box(InMode(self, AiMode.Possessing)));
        // A pet answers its master's GUID and anybody else 0 (the mind's master when the pet bit
        // is set); the count is the filled slots of the master's list, which is
        // what `DisciplineSummonElemental` and its siblings test against AI_MAXPETS before casting.
        Engine.On("CodexActor.AIIsPet", (self, _) =>
            CodexHost.Box(MindOf(self) is not { } m ? -1 : (m.Flags & Mind.FlagPet) != 0 ? m.MasterGuid : 0));
        Engine.On("CodexActor.AIGetNumPets", (self, _) =>
            CodexHost.Box(MindOf(self) is not { } m ? -1 : Minds.PetCount(m)));
        // The class's `mindflags` line, not the mind's own flags.
        Engine.On("CodexActor.AIGetMindFlags", (self, _) =>
            CodexHost.Box(MindOf(self) is { } m ? (int)m.Class.MindFlags : -1));
    }

    /// <summary>A render model's alpha, scale, shell and freeze.</summary>
    void RegisterModels()
    {
        WorldThing? Modelled(object? self) =>
            Of(self) is { } t && (t.Animation is not null || t.Model.Length > 0) ? t : null;

        // `SetAlpha(alpha, rampTime)`: held to 0..1, and the ramp is in **milliseconds**.
        Engine.On("CodexThing.SetAlpha", (self, a) =>
            Modelled(self)?.Model3D.SetAlpha(Math.Clamp(CodexHost.Float(a, 0), 0f, 1f), CodexHost.Float(a, 1)));
        // The scalar form is held twice (0.0001–10000 here, 0.01–50 in the model) and ramps in
        // seconds; the vector form is held once, by the model.
        Engine.On("CodexThing.SetScale", (self, a) =>
        {
            if (Modelled(self) is not { } t) return;
            if (a.Length > 0 && a[0] is float[]) t.Model3D.SetScale(Vec(a, 0), CodexHost.Float(a, 1));
            else t.Model3D.SetScale(Math.Clamp(CodexHost.Float(a, 0), 0.0001f, 10000f), CodexHost.Float(a, 1));
        });
        // `SetShell(material, flags, startAlpha, endAlpha, rampTime, scale)` — the last is ignored. The
        // material is loaded as the engine loads it: its `.nam`, else a texture of that
        // name, else the default — so a shell is turned off only when not even `dflt` is there.
        // Earlier, this asked for the file without its extension, found nothing, and turned every
        // scripted shell off the moment it was set.
        // A shell set inside an effect's own stretch is marked that effect's on the model (ours,
        // `World/EffectMade.cs`): read by a save, and by nothing else.
        Engine.On("CodexThing.SetShell", (self, a) =>
        {
            if (Modelled(self) is not { } t) return;
            t.Model3D.SetShell(Name31(a, 0), (uint)CodexHost.Int(a, 1), CodexHost.Float(a, 2),
                               CodexHost.Float(a, 3), CodexHost.Float(a, 4),
                               name => location?.Materials.Named(name) is not null);
            t.Model3D.ShellBy(EffectMade.Current);
        });
        Engine.On("CodexThing.EndShell", (self, _) => Modelled(self)?.Model3D.EndShell());
        // A freeze set inside an effect's own stretch is marked that effect's on the model (ours, as a shell is:
        // `World/EffectMade.cs`): read by a save, and by nothing else.
        Engine.On("CodexThing.FreezeAnimations", (self, _) =>
        {
            if (Modelled(self) is not { } t) return CodexHost.Box(false);
            t.Model3D.Frozen = true;
            t.Model3D.FrozenBy(EffectMade.Current);
            return CodexHost.Box(true);
        });
        Engine.On("CodexThing.UnfreezeAnimations", (self, _) =>
        {
            if (Modelled(self) is not { } t) return CodexHost.Box(false);
            t.Model3D.Frozen = false;
            return CodexHost.Box(true);
        });
    }

    /// <summary>Which things are which, what they are called, and a few one-liners.</summary>
    void RegisterQueries()
    {
        bool Is(object?[] a, params ThingType[] types) =>
            location?.Thing(CodexHost.Int(a, 0)) is { } t && types.Contains(t.Type);
        // By the thing's type. An item is item, weapon or armour — **not** a
        // container; a prop is a prop and nothing else.
        Engine.On("Codex.IsActorGuid", (_, a) => CodexHost.Box(Is(a, ThingType.Actor, ThingType.Player)));
        Engine.On("Codex.IsItemGuid", (_, a) => CodexHost.Box(Is(a, ThingType.Item, ThingType.Weapon, ThingType.Armor)));
        Engine.On("Codex.IsArmorGuid", (_, a) => CodexHost.Box(Is(a, ThingType.Armor)));
        Engine.On("Codex.IsWeaponGuid", (_, a) => CodexHost.Box(Is(a, ThingType.Weapon)));
        Engine.On("Codex.IsRegionGuid", (_, a) => CodexHost.Box(Is(a, ThingType.Region)));
        Engine.On("Codex.IsPropGuid", (_, a) => CodexHost.Box(Is(a, ThingType.Prop)));
        // Not connected: single player.
        Engine.On("Codex.NetIsConnected", (_, _) => CodexHost.Box(false));

        // What a thing is called: its DESCRIPTIONID's text when it has one — a missing
        // key answering as itself — else its template name. Never the identified description.
        Engine.On("CodexThing.GetName", (self, _) =>
        {
            if (Of(self) is not { } t) return "***ERR:Bad Thing***";
            if (t.DescriptionId.Length > 0) return Nls?.Invoke(t.DescriptionId) ?? t.DescriptionId;
            return t.Template.ToLowerInvariant();
        });
        Engine.On("CodexThing.GetTemplateName", (self, _) =>
            Of(self) is { } t ? t.Template.ToLowerInvariant() : "***ERR:Bad Thing***");
        // The material of the floor the thing stands on.
        Engine.On("CodexThing.GetSurfaceMaterial", (self, _) =>
        {
            if (Of(self) is not { } t) return "***ERR:Bad Thing***";
            if (location?.Walk is not { } walk || walk.Find(t.Position) is var node && node < 0) return "***ERR:No Floor***";
            var level = location.Level;
            var n = walk.Nodes[node];
            if (n.Sector < 0 || n.Sector >= level.Sectors.Length) return "***ERR:No Floor***";
            var sector = level.Sectors[n.Sector];
            if (n.Surface < 0 || n.Surface >= sector.Surfaces.Length) return "***ERR:No Floor***";
            int group = sector.Surfaces[n.Surface].Group;
            if (group < 0 || group >= sector.Groups.Length) return "***ERR:No Material***";
            int m = sector.Groups[group].Material;
            return m >= 0 && m < level.Materials.Length ? level.Materials[m] : "***ERR:No Material***";
        });

        // `SpawnThingNear(template, distance, flags)`: beside the thing, not on it.
        Engine.On("CodexThing.SpawnThingNear", (self, a) =>
        {
            if (location is null || Of(self) is not { } anchor) return CodexHost.Box(0);
            using (Vitomir.Engine.Net.MadeHere.ByScript(location))              // kept back unless `netprops`
                return CodexHost.Box(location.SpawnNear(Name31(a, 0), anchor, CodexHost.Float(a, 1), CodexHost.Int(a, 2) | 3)?.Guid ?? 0);
        });

        // Turning to look: the engine's instant, level look-at, true when both ends exist.
        Engine.On("CodexThing.LookAtThing", (self, a) =>
        {
            if (Of(self) is not { } t || location?.Thing(CodexHost.Int(a, 0)) is not { } at) return CodexHost.Box(false);
            t.Face(at.Position);
            return CodexHost.Box(true);
        });
        Engine.On("CodexThing.LookAtPos", (self, a) =>
        {
            if (Of(self) is not { } t) return CodexHost.Box(false);
            t.Face(Vec(a, 0));
            return CodexHost.Box(true);
        });

        // `Trigger(triggerer, id, p0..p3)`: `triggered` to the thing's own
        // script and its captures — **but only when it has a script of its own**: the capture loop
        // sits inside that test, so a scriptless thing's captures hear nothing.
        Engine.On("CodexThing.Trigger", (self, a) =>
        {
            if (Of(self) is not { } t) return CodexHost.Box(false);
            var own = OwnScripts(t);
            if (own.Length == 0) return CodexHost.Box(true);
            object?[] Args(int capture) =>
                [CodexHost.Box(t.Guid), CodexHost.Box(CodexHost.Int(a, 0)), CodexHost.Box(CodexHost.Int(a, 1)),
                 CodexHost.Box(CodexHost.Float(a, 2)), CodexHost.Box(CodexHost.Float(a, 3)),
                 CodexHost.Box(CodexHost.Float(a, 4)), CodexHost.Box(CodexHost.Float(a, 5)), CodexHost.Box(capture)];
            const string sig = "(IIIFFFFI)V";
            foreach (var l in own) Say(l.Name, l.Script, "triggered", sig, Args(0));
            foreach (var hold in t.Captures.ToArray())
                if (ScriptOf(hold.Script) is { } s) Say(s.Name, s.Script, "triggered", sig, Args(hold.Id));
            return CodexHost.Box(true);
        });

        // Items' flags: an item, weapon or armour; set ORs and answers the old word.
        Engine.On("CodexItem.GetItemFlags", (self, _) =>
            CodexHost.Box(Of(self) is { Type: ThingType.Item or ThingType.Weapon or ThingType.Armor, Item: { } i } ? (int)i.Flags : 0));
        Engine.On("CodexItem.SetItemFlags", (self, a) =>
        {
            if (Of(self) is not { Type: ThingType.Item or ThingType.Weapon or ThingType.Armor, Item: { } i }) return CodexHost.Box(0);
            int was = (int)i.Flags;
            i.Flags |= (ItemFlags)CodexHost.Int(a, 0);
            return CodexHost.Box(was);
        });
        // A projectile's damage: old for new, no clamp.
        Engine.On("CodexProjectile.SetProjectileDamage", (self, a) =>
        {
            if (Of(self) is not { Type: ThingType.Projectile } p) return CodexHost.Box(0);
            int was = p.ProjectileDamage;
            p.ProjectileDamage = CodexHost.Int(a, 0);
            return CodexHost.Box(was);
        });
        // Firing: from the thing's middle plus an offset in its own frame, at a
        // thing's middle or at a point; the new projectile's GUID, or 0.
        Engine.On("CodexThing.FireProjectileAtThing", (self, a) =>
        {
            if (location is null || Of(self) is not { } firer || location.Thing(CodexHost.Int(a, 1)) is not { } target)
                return CodexHost.Box(0);
            return CodexHost.Box(Projectiles.FireAt(location, firer, target, Name31(a, 0), Vec(a, 2),
                                                    location.Minds?.Random, Now, location.Motions)?.Guid ?? 0);
        });
        Engine.On("CodexThing.FireProjectileAtPos", (self, a) =>
        {
            if (location is null || Of(self) is not { } firer) return CodexHost.Box(0);
            return CodexHost.Box(Projectiles.FireAtPoint(location, firer, Name31(a, 0), Vec(a, 2), Vec(a, 1),
                                                         location.Minds?.Random, Now, location.Motions)?.Guid ?? 0);
        });

        // `CodexWorld.GetName`: the current location's world file name.
        Engine.On("CodexWorld.GetName", (self, _) => location?.Name ?? "");

        // `FormatNLS(id, format)`: the id's text with its `%X` slots filled —
        // and an empty format is passed on as none at all, which is the plain lookup.
        Engine.On("Codex.FormatNLS", (_, a) =>
            Vitomir.Engine.Ui.Strings.Format(key => Nls?.Invoke(key), Name31(a, 0),
                                             CodexHost.Str(a, 1) is { Length: > 0 } f ? f : null));

        // `ExecuteText(script, owner, thing, name)`: opens the
        // read pane on a document. False when a pane is already open or there is no such document.
        // The script and the character it was opened for are kept for the close, which tells the
        // script `textended` (`TextClosed`).
        Engine.On("Codex.ExecuteText", (_, a) =>
        {
            if (ReadText?.Invoke(Name31(a, 3)) != true) return CodexHost.Box(false);
            reading = (CodexHost.Int(a, 0), CodexHost.Int(a, 1));
            return CodexHost.Box(true);
        });
    }

    /// <summary>The string tables, for the names and the formatted console lines. Null answers every key as itself.</summary>
    public Func<string, string?>? Nls { get; set; }

    /// <summary>Opens the read pane on a named document; false when it cannot (`Codex.ExecuteText`).</summary>
    public Func<string, bool>? ReadText { get; set; }

    // ---- CodexCollision (table 3) --------------------------------------------------------------

    /// <summary>
    /// One of the engine's 128 query handles: the type mask and the last query's
    /// results. The engine never clears one — its mask is whatever the heap held until `IgnoreAll`
    /// or `AcceptType` sets it — and the shipped users set it first; a fresh one here accepts every
    /// type, which is what zeroed memory would.
    /// </summary>
    sealed class Query
    {
        public uint Refused;
        public readonly List<(int Guid, float Distance)> Results = [];
    }

    readonly Query?[] queries = new Query?[128];

    /// <summary>The most results one query keeps.</summary>
    const int MaxResults = 256;

    /// <summary>What an out-of-range radius becomes, and the limit.</summary>
    const float DefaultRadius = 256f, MaxRadius = 5000f;

    void RegisterCollision()
    {
        Query? Handle(object? self)
        {
            int h = runtime.GuidOf(self);
            return (uint)h < (uint)queries.Length ? queries[h] : null;
        }
        Engine.On("CodexCollision.Create", (_, _) =>
        {
            for (int i = 0; i < queries.Length; i++)
                if (queries[i] is null) { queries[i] = new Query(); return CodexHost.Box(i); }
            return CodexHost.Box(-1);
        });
        Engine.On("CodexCollision.Free", (self, _) =>
        {
            int h = runtime.GuidOf(self);
            if (h >= 0 && h < queries.Length) queries[h] = null;
        });
        Engine.On("CodexCollision.IgnoreAll", (self, _) => { if (Handle(self) is { } q) q.Refused = 0xffffffff; });
        Engine.On("CodexCollision.AcceptType", (self, a) =>
        {
            int type = CodexHost.Int(a, 0);
            if (Handle(self) is { } q && type >= 0 && type < 11) q.Refused &= ~(1u << type);
        });
        Engine.On("CodexCollision.GetNumResults", (self, _) => CodexHost.Box(Handle(self)?.Results.Count ?? 0));
        Engine.On("CodexCollision.GetResult", (self, a) =>
        {
            int i = CodexHost.Int(a, 0);
            return CodexHost.Box(Handle(self) is { } q && i >= 0 && i < q.Results.Count ? q.Results[i].Guid : 0);
        });
        Engine.On("CodexCollision.ThingsInSphere", (self, a) =>
            CodexHost.Box(Handle(self) is { } q ? Sphere(q, Vec(a, 0), CodexHost.Float(a, 1), CodexHost.Int(a, 2)) : -1));
    }

    /// <summary>
    /// `ThingsInSphere`. The reference thing only says which world; it is not
    /// left out of its own answer. Results come in the order the sector flood finds them — not
    /// sorted — and past 256 a new one only gets in by displacing the farthest that is farther.
    /// </summary>
    int Sphere(Query q, Vector3 c, float r, int refGuid, bool clamp = true)
    {
        if (location?.Thing(refGuid) is not { InWorld: true } || location.Solid is not { } solid || location.Props is not { } bodies)
            return -1;
        int start = solid.SectorAt(c);
        if (start < 0) return -1;
        // The native's own guard; the engine's query itself takes the radius as given.
        if (clamp && (!(r > 0f) || r > MaxRadius)) r = DefaultRadius;
        q.Results.Clear();
        var sectors = bodies.SectorsNear(c, r, start);
        var inFlood = new HashSet<int>(sectors);
        foreach (var t in bodies.In(sectors))
        {
            if (!inFlood.Contains((int)t.Sector) || t.Removed) continue;
            if ((q.Refused & (1u << (int)t.Type)) != 0) continue;
            if ((t.Flags & WorldThing.Flag.DisableCollide) != 0) continue;
            float? d = t.Collide switch
            {
                CollideShape.Sphere => Vector3.Distance(c, t.Position) is var s && s <= r + t.Radius ? s : null,
                CollideShape.Cylinder => c.Z > t.Position.Z + r + t.Height || t.Position.Z > c.Z + r ? null
                    : Bodies.Distance2D(c, t.Position) is var h && h <= r + t.Radius ? h : null,
                CollideShape.Box => BoxHit(t, c, r) ? 0f : null,
                _ => null,
            };
            if (d is not { } dist) continue;
            if (q.Results.Count < MaxResults) { q.Results.Add((t.Guid, dist)); continue; }
            int far = -1;
            for (int i = 0; i < q.Results.Count; i++)
                if (q.Results[i].Distance > dist && (far < 0 || q.Results[i].Distance > q.Results[far].Distance)) far = i;
            if (far >= 0) q.Results[far] = (t.Guid, dist);
        }
        return q.Results.Count;
    }

    /// <summary>A sphere against a thing's oriented box, axis by axis.</summary>
    static bool BoxHit(WorldThing t, Vector3 c, float r)
    {
        var m = t.Rotation;
        var rel = c - t.Position;
        Span<Vector3> axes = [new(m.M11, m.M12, m.M13), new(m.M21, m.M22, m.M23), new(m.M31, m.M32, m.M33)];
        Span<float> lo = [t.BoxMin.X, t.BoxMin.Y, t.BoxMin.Z];
        Span<float> hi = [t.BoxMax.X, t.BoxMax.Y, t.BoxMax.Z];
        for (int i = 0; i < 3; i++)
        {
            float p = Vector3.Dot(rel, axes[i]);
            if (p < lo[i] - r || p > hi[i] + r) return false;
        }
        return true;
    }

    /// <summary>
    /// The timers of the engine-made scripts a party member carries, put aside with what they had
    /// left when the party leaves; everyone else's go with the place.
    /// </summary>
    void CarryTimers()
    {
        foreach (var t in timers.ToArray())
        {
            if (!loose.TryGetValue(t.Guid, out var e) || e.Owner.Actor is not { } who || !who.Has(Actor.Flag.Party)) continue;
            carriedTimers.Add((t, t.At - now));
            timers.Remove(t);
        }
    }

    /// <summary>And back on the clock in the place arrived at.</summary>
    void LandTimers()
    {
        foreach (var (t, left) in carriedTimers) timers.Add(t with { At = now + left });
        carriedTimers.Clear();
    }
}
