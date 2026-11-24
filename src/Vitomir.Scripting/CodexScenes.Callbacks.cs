// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The callbacks the single-player scripts answer that nothing sent at first: an item picked
// up, used, worn and taken off, asked whether it may be; a blow landed; a thing come close to; a
// document closed and a film over; an exit opened or shut; the weather changed; the story advanced.
// Each is sent from where the original engine sends it, the way it sends it.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>
    /// The item callbacks, handed to the place for the RPG layer to ask (the same seam `pickup` was
    /// built with: the engine half must not know about Java). Called from <see cref="Enter"/>.
    /// </summary>
    void HookCallbacks(Location loc)
    {
        loc.ItemScripts = new Ground.Scripts(
            Pickup: (item, picker) => Ask("pickup", item, picker),
            CanUse: (item, user) => Ask("canuse", item, user),
            Worn: (item, wearer) => Tell3("worn", item, wearer),
            Unworn: (item, wearer) => Ask("unworn", item, wearer),
            Used: (item, owner) => Ask("used", item, owner),
            Hit: Hit,
            Damaged: Damaged,                                         // CodexScenes.Damaged.cs
            ActorCanUse: ActorCanUse, ActorWorn: ActorWorn, ActorUnworn: ActorUnworn,
            BeginSwing: BeginSwing, KeyTag: KeyTag,                   // the mod-only five, CodexScenes.ModCallbacks.cs
            Soaked: Soaked);                                          // CodexScenes.Soaked.cs
    }

    // ---- the item's own script ------------------------------------------------------------------

    /// <summary>
    /// The item scripts Vitomir made on demand, by item: an item that was never in a world — a
    /// starting kit's, a shop's — has no script on the live list. **Ours**: 1.1 makes a thing's Codex
    /// object as the thing is registered, whether or not it ever enters a
    /// world; Vitomir makes an item's the first time the item has something to hear, as one of the
    /// engine-made scripts (<see cref="Make"/>), whose `GetClassThing` answers the item. It hears no
    /// `created`, as a pack's item never does.
    /// </summary>
    readonly Dictionary<WorldThing, object> itemScripts = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The scripts a thing carries, which hear its events with a capture id of 0 —
    /// made now for an item that has a `script` and has none yet.
    /// </summary>
    (string Name, object Script)[] Own(WorldThing thing)
    {
        var own = OwnScripts(thing);
        if (own.Length > 0) return [.. own.Select(l => (l.Name, l.Script))];
        if (thing.Item is null || thing.Script.Length == 0) return [];
        if (!itemScripts.TryGetValue(thing, out var made))
        {
            if (Make(thing.Script, thing) is not { } fresh) return [];
            itemScripts[thing] = made = fresh;
        }
        return [(thing.Script, made)];
    }

    /// <summary>
    /// A **boolean** callback through the shape `pickup`, `used`, `canuse` and `unworn` share:
    /// the answer starts **true**; the thing's own script
    /// is asked `(thingGuid, otherGuid, 0)` when its class declares the method, and a false ends it
    /// there; then each capture on the thing whose class declares it, in the list's order, with its
    /// own capture id — the first false ends the loop and is the answer. A script that throws
    /// answers false, which is what the JNI call leaves behind once the exception is cleared.
    /// </summary>
    bool Ask(string callback, WorldThing thing, WorldThing other)
    {
        const string sig = "(III)Z";
        foreach (var (name, script) in Own(thing).Take(1))
            if (!AskOne(name, script, callback, sig, thing, other, 0)) return false;
        foreach (var hold in thing.Captures.ToArray())
            if (ScriptOf(hold.Script) is { } s && !AskOne(s.Name, s.Script, callback, sig, thing, other, hold.Id))
                return false;
        return true;
    }

    bool AskOne(string name, object script, string callback, string sig, WorldThing thing, WorldThing other, int capture)
    {
        try
        {
            if (!runtime.TryInvoke(script, callback, sig,
                                   [CodexHost.Box(thing.Guid), CodexHost.Box(other.Guid), CodexHost.Box(capture)], out var answer))
                return true;                                              // the class has no such method
            return answer is java.lang.Boolean b && b.booleanValue();
        }
        catch (Exception e)
        {
            Engine.Log($"{name}.{callback} failed: {ScriptException.From(name, e).JavaType}");
            return false;
        }
    }

    /// <summary>
    /// A `(III)V` callback through the plain two loops — `worn`, `touched`:
    /// the thing's own script with capture 0, then every capture with its id.
    /// </summary>
    void Tell3(string callback, WorldThing thing, WorldThing? other)
    {
        object?[] Args(int capture) => [CodexHost.Box(thing.Guid), CodexHost.Box(other?.Guid ?? 0), CodexHost.Box(capture)];
        foreach (var (name, script) in Own(thing).Take(1)) Say(name, script, callback, "(III)V", Args(0));
        foreach (var hold in thing.Captures.ToArray())
            if (ScriptOf(hold.Script) is { } s) Say(s.Name, s.Script, callback, "(III)V", Args(hold.Id));
    }

    // ---- hit ------------------------------------------------------------------------------------

    /// <summary>
    /// `hit(wielderGuid, victimGuid, damage, captureId)` — sent at the
    /// end of a melee blow that landed (after the blow's noise), with the damage it did.
    /// Four loops, and **two of them carry the original game's bug**, which we
    /// reproduce: the weapon's own script; for each capture on the weapon whose class declares
    /// `hit`, **the weapon's own script again**, with that capture's id; the wielder's own script; and
    /// for each capture on the wielder whose class declares it, the weapon's own script once more.
    /// With no script on the weapon the original dereferences null in the capture loops; that is
    /// logged and skipped.
    ///
    /// The weapon's script is the item's when the hand's weapon came from one. A character's own
    /// weapon — its `ACTOR_WEAPON`, the bite of a ghoul spider — and a swapped-in claw are things of
    /// their template in 1.1 (`ACTOR_WEAPON` in the tag reader, and the claw's swap), made with the template's script: `bite_ghoulSpider`
    /// carries `ClassPoisonous` as the spider itself does, so a spider's bite is offered the poison
    /// twice, once by each. Vitomir has no thing for those weapons, so their script is made the
    /// first time one lands a blow and kept with the weapon (ours; its `GetClassThing` answers the
    /// wielder rather than the weapon).
    /// </summary>
    public void Hit(WorldThing wielder, Weapon weapon, WorldThing victim, float damage)
    {
        var weaponThing = WeaponThing(wielder, weapon);
        (string Name, object Script)? own = weaponThing is not null
            ? Own(weaponThing) is { Length: > 0 } o ? o[0] : null
            : TemplateScript(wielder, weapon);
        object?[] Args(int capture) =>
            [CodexHost.Box(wielder.Guid), CodexHost.Box(victim.Guid), CodexHost.Box(damage), CodexHost.Box(capture)];
        const string sig = "(IIFI)V";
        void Captures(WorldThing on)
        {
            foreach (var hold in on.Captures.ToArray())
            {
                if (ScriptOf(hold.Script) is not { } holder || !runtime.Declares(holder.Script, "hit", sig)) continue;
                if (own is not { } mine) { Engine.Log($"hit: a capture on {on.Template} wants it and the weapon has no script (the original crashes here)"); continue; }
                SayHit(mine.Name, mine.Script, Args(hold.Id), wielder, victim);
            }
        }
        if (own is { } first) SayHit(first.Name, first.Script, Args(0), wielder, victim);
        if (weaponThing is not null) Captures(weaponThing);
        foreach (var l in OwnScripts(wielder).Take(1)) SayHit(l.Name, l.Script, Args(0), wielder, victim);
        Captures(wielder);
    }

    /// <summary>The scripts of weapons that are no item, by weapon (see <see cref="Hit"/>).</summary>
    readonly Dictionary<Weapon, (string Name, object Script)?> weaponScripts = new(ReferenceEqualityComparer.Instance);

    (string Name, object Script)? TemplateScript(WorldThing wielder, Weapon weapon)
    {
        if (weaponScripts.TryGetValue(weapon, out var known)) return known;
        (string, object)? made = null;
        if (location?.Templates is { } templates && templates.Has(weapon.Name)
            && templates.Resolve(weapon.Name).TryGetValue(Tags.Script, out var tag) && tag.AsString() is { Length: > 0 } name
            && Make(name, wielder) is { } script)
            made = (name, script);
        weaponScripts[weapon] = made;
        return made;
    }

    /// <summary>The item thing a weapon in the hand came from — in the pack or on the cursor — or null.</summary>
    static WorldThing? WeaponThing(WorldThing wielder, Weapon weapon)
    {
        if (wielder.Actor is not { } actor) return null;
        if (actor.Held is { } held && ReferenceEquals(held.Weapon, weapon)) return held.Thing;
        foreach (var item in actor.Inventory.Items)
            if (ReferenceEquals(item.Weapon, weapon)) return item.Thing;
        return null;
    }

    // ---- touched --------------------------------------------------------------------------------

    /// <summary>The frames stepped, which the proximity test staggers its things by.</summary>
    int frames;

    // ---- textended ------------------------------------------------------------------------------

    /// <summary>The script that opened the read pane and the character it opened it for.</summary>
    (int Script, int Reader)? reading;

    /// <summary>
    /// The read pane has closed: when a script opened it for a
    /// character — `ExecuteText`'s first two arguments — that script hears `textended(readerGuid)`,
    /// and then every script holding a capture on the reader but that one. A book read
    /// from the pack has no script behind it and nobody hears anything. The host calls this when
    /// the pane closes, whoever opened it.
    /// </summary>
    public void TextClosed()
    {
        if (reading is not { } r) return;
        reading = null;
        if (location?.Thing(r.Reader) is not { Type: ThingType.Actor or ThingType.Player } reader) return;
        if (ScriptOf(r.Script) is not { } opener) return;
        object?[] args = [CodexHost.Box(reader.Guid)];
        Say(opener.Name, opener.Script, "textended", "(I)V", args);
        foreach (var hold in reader.Captures.ToArray())
            if (ScriptOf(hold.Script) is { } s && !ReferenceEquals(s.Script, opener.Script))
                Say(s.Name, s.Script, "textended", "(I)V", args);
    }

    // ---- videoended -----------------------------------------------------------------------------

    /// <summary>The film a script asked for (the main loop's request 14): its name, the script, and the id it gave.</summary>
    (string Name, int Script, int Id)? film;

    /// <summary>The film showing: the script that hears `videoended` and the id it gave.</summary>
    (int Script, int Id)? showing;

    /// <summary>
    /// The end of the frame, where 1.1's main loop takes request 14: the film is
    /// handed to the host, <see cref="PlayFilm"/>, which **starts** it and holds the game while it
    /// shows (state 1); when it ends or is skipped the host calls <see cref="FilmEnded"/>, and the
    /// script that asked hears `videoended(id)` then, as the film
    /// closes. **A film that cannot be played ends at once**: with films turned off or the file not
    /// opening, the script is told straight away, so the story goes on. With no
    /// host, that is every film.
    /// </summary>
    void RunFilm()
    {
        if (film is not { } f) return;
        film = null;
        if (showing is not null) FilmEnded();                    // an open film is closed first
        showing = (f.Script, f.Id);
        if (PlayFilm?.Invoke(f.Name) != true) FilmEnded();
    }

    /// <summary>
    /// The film the host was showing has ended or been skipped: the script that asked hears
    /// `videoended(id)`. Nothing when no film is showing.
    /// </summary>
    public void FilmEnded()
    {
        if (showing is not { } f) return;
        showing = null;
        if (ScriptOf(f.Script) is { } s) Say(s.Name, s.Script, "videoended", "(I)V", [CodexHost.Box(f.Id)]);
    }

    // ---- exitstatechanged -----------------------------------------------------------------------

    /// <summary>
    /// `OpenExit` / `CloseExit`: the exit's closed bit, and then — when the
    /// named location is valid, the exit is one it has, and that location's world is loaded — every
    /// thing's own script in that world hears `exitstatechanged()`. No captures and no
    /// chronicle scripts: it is how each `ClickExit` in a place re-reads whether its sign should
    /// say locked. A place the party has left and Vitomir keeps counts as loaded, as one of 1.1's
    /// six kept places does.
    /// </summary>
    void ExitState(string name, int exit, bool closed)
    {
        if (Chronicle is not { } chronicle) return;
        chronicle.SetExitClosed(name, exit, closed);
        if (chronicle.At(name) is not { } where || exit < 0 || exit >= where.ExitCount) return;
        int index = chronicle.Find(name);
        if (location is not null && chronicle.Current == index)
            foreach (var l in InListOrder().Where(l => l.Thing > 0))                        // the world's list, newest first
                Say(l.Name, l.Script, "exitstatechanged", "()V", []);
        foreach (var (place, k) in kept.ToArray())
            if (Chronicles?.For(place.SceneFile, place.Name) is { } at && ReferenceEquals(at.Chronicle, chronicle) && at.Location == index)
                foreach (var l in k.Live.Where(l => l.Thing > 0))
                    Say(l.Name, l.Script, "exitstatechanged", "()V", []);
    }

    // ---- weatherchanged -------------------------------------------------------------------------

    /// <summary>
    /// `weatherchanged()`: every thing's own script in the world whose class declares it,
    /// then the chronicle's scripts. The `Weather` script answers it by taking its old weather thing
    /// away and spawning one of the new name.
    /// </summary>
    void WeatherChanged()
    {
        foreach (var l in InListOrder().Where(l => l.Thing > 0))                            // the world's list, newest first
            Say(l.Name, l.Script, "weatherchanged", "()V", []);
        foreach (var (name, script) in ChronicleScripts())
            Say(name, script, "weatherchanged", "()V", []);
    }
}
