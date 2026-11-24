// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `damaged`, the damage message: what a prop's scripts are told
// when an explosion catches it, and a character's before any blow lands
// (sent).
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>
    /// `damaged(thingGuid, attackerGuid, damage, damageType, captureId) → float`. The thing's own script hears it first with a capture id of 0,
    /// and **its answer is the damage from then on**; then each script holding a capture on the thing,
    /// with that capture's id, **while the damage is not below 0** — a negative answer stops the
    /// walk. A class that does not declare it is passed over and the damage stands. No shipped class
    /// declares `damaged`, so in the shipped game this is silent; a mod's prop can hear an explosion,
    /// and a mod's character script every blow it takes, its answer the blow (Rpg.DamagedMessage).
    /// The lookup is by the whole signature, return type included, as 1.1's.
    /// </summary>
    /// <returns>The damage after every script that answered.</returns>
    public float Damaged(WorldThing thing, WorldThing? attacker, float damage, int type)
    {
        const string sig = "(IIFII)F";
        foreach (var (name, script) in Own(thing).Take(1))
            damage = DamagedOne(name, script, thing, attacker, damage, type, 0);
        foreach (var hold in thing.Captures.ToArray())
        {
            if (damage < 0f) return 0f;                                  // the walk stops
            if (ScriptOf(hold.Script) is { } s)
                damage = DamagedOne(s.Name, s.Script, thing, attacker, damage, type, hold.Id);
        }
        return damage;

        float DamagedOne(string name, object script, WorldThing thing, WorldThing? attacker, float damage, int type, int capture)
        {
            try
            {
                if (!runtime.TryInvoke(script, "damaged", sig,
                        [CodexHost.Box(thing.Guid), CodexHost.Box(attacker?.Guid ?? 0), CodexHost.Box(damage),
                         CodexHost.Box(type), CodexHost.Box(capture)], out var answer))
                    return damage;                                       // the class has no such method
                return answer is java.lang.Float f ? f.floatValue() : damage;
            }
            catch (Exception e)
            {
                Engine.Log($"{name}.damaged failed: {ScriptException.From(name, e).JavaType}");
                return damage;
            }
        }
    }
}
