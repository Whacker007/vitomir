// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// An effect's looks through a save and a load (ours, the repair `loadedeffects`): what every save records of
// them, and what a load makes again. The record kept while the game runs is
// `World/EffectMade.cs`.
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Save;

/// <summary>
/// **An effect's glow, pad, trail and frozen pose, kept through a save** (ours; the repair <c>loadedeffects</c>,
/// <see cref="Repairs.LoadedEffects"/> — always done, with no switch: it is a
/// pure bug, so it is fixed with no option to turn it off).
///
/// What 1.1 does: an effect's script makes its looks in <c>effectstarted</c> — it spawns a pad or a
/// trail and attaches it, and sets a shell on the model. Each such thing is flagged <c>SAVE NEVER</c> in the
/// templates, so the save's filter leaves it out and the links' writer passes over it on its parent's
/// list (the test of thing flag bit 1 — and writes a party member's list empty whatever is on
/// it). The model's writer writes the model's flag word, motion set, name and animation and nothing
/// of a shell — not its material, not its alphas — and the reader
/// reads the flag word of an ordinary model and puts 0 in its place. On a load
/// the effects' reader makes each instance and its script, gives the script its GUID
/// and <c>restore</c>s it, and links the instance straight onto the character:
/// it does not go through the apply, so the script is never told
/// <c>effectstarted</c> — it hears <c>restored</c>, which no shipped effect
/// script answers. The one thing the load does about effects' looks is the opposite: the post-load pass
/// **takes off** Earth Meld and Mist Form, the two whose looks a character could not be left without.
/// So in 1.1 a load leaves every other effect on with none of what its script made. **That is what
/// 1.1 does**: in the base game the visual effects are not preserved on save and
/// load; the rest of this explains why.
///
/// **Every save writes what there is to make again** (<see cref="Capture"/>), as an extra 1.1's record lacks and
/// nothing but this file reads: with each effect, the things its script made that are still there
/// (<see cref="EffectSave.Made"/>) and the shells it set that are still on (<see cref="EffectSave.Shells"/>).
/// **A load makes them again** (<see cref="Remake"/>): each thing from its template **under the
/// GUID it had** — the script kept that number and removes the thing by it when the effect ends — where it was
/// and riding what it rode; each shell as it stood, its ramp where it was. The script is not run, and nothing
/// is made for an effect that is no longer on. A save with no record — every one from before the record was
/// written — loads exactly as it did.
///
/// **What a load made again goes when its effect goes** (<see cref="Ended"/>). The script's own
/// <c>effectended</c> is told first and takes what it can — it removes its pad by the number it kept and ends its
/// shell. But not every script keeps its number: <c>EffectDiscTheftOfVitae</c> and
/// <c>EffectDiscTongueOfTheAsp</c> hold their beam's GUID in a field and declare no <c>save</c>, so after a load
/// the field is 0 and their <c>EndEffect</c> removes nothing. So once the script has been told, whatever the
/// load made again for that effect and is still there is taken away by the engine, and a shell it put back that
/// is still the effect's and would stay (<see cref="ModelFx.ShellEndsByItself"/>: a flash is left to fade) is ended. Only what a load made again: in a game that was never loaded nothing here
/// runs, and what a script leaves behind is left.
///
/// **A model the script froze is frozen again in its pose** (of Prison of Ice's pose:
/// <i>"Yes."</i>). <c>FreezeAnimations</c> sets the model's bit 0x20000 — in the flag word the reader throws away —
/// so in 1.1 a load leaves the prisoner held by the rules (<c>ef_disc_prisonofice2</c> is IMMOBILIZE, which is
/// saved) and standing at ease under the ice. The freeze is marked the effect's as the script sets it
/// (<see cref="ModelFx.FrozenEffect"/>), a save writes the pose the model stands in
/// (<see cref="EffectSave.Frozen"/>, <c>ThingAnimation.KeepPose</c>), and <see cref="Remake"/> freezes the model
/// again in it (<c>ThingAnimation.HoldPose</c>): the pose laid in with the mixer's own fade, **no channel put
/// back**, so what the character plays and every tag it will cross are what the load gave it. The script's
/// <c>EndEffect</c> unfreezes it; if a script did not, <see cref="Ended"/> does.
///
/// **Only what nothing can touch** (<see cref="Looks"/>): a prop no save keeps, with no collision shape, no
/// script of its own and no time to live. That is every lasting thing the install's effect scripts make
/// (<c>EffectLooksTests</c>), and it is what lets the repair be declared presentation. A burst that ends by itself
/// (<c>blueMagic</c>, a shockwave: a template with a <c>TIMER</c>) is not kept: made again it would start over.
/// </summary>
public static class LoadedEffects
{
    static readonly ILogger log = Diag.For(typeof(LoadedEffects).FullName!);

    /// <summary>
    /// Whether <paramref name="thing"/> is one of an effect's looks that a save records: in the world, never
    /// saved (<see cref="WorldThing.Flag.SaveNever"/>), a prop with no collision shape and no script, and not
    /// on a timer of its own (<see cref="WorldThing.ExpireAt"/>).
    /// </summary>
    public static bool Looks(WorldThing thing) => thing.InWorld && Makes(thing);

    /// <summary>
    /// What <paramref name="effect"/>'s script made in <paramref name="place"/> that is still there, written
    /// onto its record: the things marked its (<see cref="WorldThing.MadeByEffect"/>) that are
    /// <see cref="Looks"/>, in the place's order, the shells marked its (<see cref="ModelFx.ShellEffect"/>), and
    /// the models it froze that are still frozen (<see cref="ModelFx.FrozenEffect"/>), each with its pose.
    /// An effect with no script made nothing.
    /// </summary>
    public static void Capture(Location place, Effect effect, EffectSave save)
    {
        if (effect.Guid == 0 || effect.Definition.Script.Length == 0) return;
        foreach (var t in place.Things)
        {
            if (t.MadeByEffect == effect.Guid && Looks(t))
            {
                var made = new EffectThingSave
                {
                    Template = t.Template, Guid = t.Guid, Position = t.Position, Orientation = t.Orientation, Sector = t.Sector,
                };
                if (t.AttachedTo is { } parent && parent.Attachments.FirstOrDefault(a => ReferenceEquals(a.Child, t)) is { } link)
                {
                    made.Rides = parent.Guid;
                    made.Bone = link.Bone;
                    made.AttachFlags = link.Flags;
                    var m = link.Relative;
                    made.Relative = [m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33, m.M41, m.M42, m.M43];
                }
                if (!ReferenceEquals(t.Keyframes, t.Scene.Keyframes) && !t.Keyframes.SequenceEqual(t.Scene.Keyframes))
                    made.Keyframes = [.. t.Keyframes.Select(k => new KeyframeSave { Position = k.Position, Orientation = k.Orientation })];
                if (t.Fx is { } looks)
                {
                    if (looks.AlphaHeading != 1f) made.Alpha = looks.AlphaHeading;
                    if (looks.ScaleHeading != 1f) made.Scale = looks.ScaleHeading;
                }
                (save.Made ??= []).Add(made);
            }
            if (t.Fx is { } fx && fx.ShellEffect == effect.Guid && fx.ShellNow is { } shell && !t.Removed)
                (save.Shells ??= []).Add(new EffectShellSave
                {
                    On = t.Guid, Material = shell.Material, Flags = shell.Flags, Alpha = shell.Alpha, Ramping = shell.Ramping,
                    From = shell.From, To = shell.To, Rate = shell.Rate, Progress = shell.Progress,
                });
            if (t.Fx is { Frozen: true } held && held.FrozenEffect == effect.Guid && !t.Removed && t.Animation?.KeepPose() is { } pose)
                (save.Frozen ??= []).Add(new EffectFrozenSave { On = t.Guid, Rotation = pose.Rotation, Translation = pose.Translation });
        }
    }

    /// <summary>
    /// What a load has still to make again in a place: an effect put back, on whom, and what its record names
    /// — its own lists, so the save that was read is left as it was read.
    /// </summary>
    sealed record Waiting(WorldThing On, Effect Effect, List<EffectThingSave> Made, List<EffectShellSave> Shells, List<EffectFrozenSave> Frozen);

    static readonly ConditionalWeakTable<Location, List<Waiting>> waiting = new();

    /// <summary>
    /// A load has put <paramref name="effect"/> back on <paramref name="on"/> from <paramref name="save"/>: when
    /// the record says its script had made something, that is kept for <see cref="Remake"/>. Nothing for a
    /// record that names nothing, which is every effect of a save from before the record was written.
    /// </summary>
    public static void Loaded(Location place, WorldThing on, Effect effect, EffectSave save)
    {
        if (save.Made is not { Count: > 0 } && save.Shells is not { Count: > 0 } && save.Frozen is not { Count: > 0 }) return;
        waiting.GetOrCreateValue(place).Add(new Waiting(on, effect, [.. save.Made ?? []], [.. save.Shells ?? []], [.. save.Frozen ?? []]));
    }

    /// <summary>What a load made again in a place, by the effect it was made for; and how many of them the engine had to take away itself.</summary>
    sealed class Again
    {
        public readonly List<(int Effect, WorldThing Thing)> Things = [];
        public readonly List<(int Effect, WorldThing Wearer)> Shells = [];
        public readonly List<(int Effect, WorldThing Held)> Frozen = [];
        public int Swept;
    }

    static readonly ConditionalWeakTable<Location, Again> again = new();

    /// <summary>
    /// <paramref name="effect"/> has ended on <paramref name="on"/> and its script has been told
    /// (Anything a load made again for it that the script did not take is taken now — the thing
    /// removed, the shell ended if it is still that effect's and would not end by itself, the model unfrozen if
    /// it is still frozen by that effect. Nothing in a place no load made anything in.
    /// </summary>
    public static void Ended(WorldThing on, Effect effect)
    {
        if (on.Place is not { } place || !again.TryGetValue(place, out var made)) return;
        for (int i = made.Things.Count - 1; i >= 0; i--)
        {
            var (guid, thing) = made.Things[i];
            if (guid != effect.Guid) continue;
            made.Things.RemoveAt(i);
            if (!thing.Removed && place.Remove(thing)) made.Swept++;
        }
        for (int i = made.Shells.Count - 1; i >= 0; i--)
        {
            var (guid, wearer) = made.Shells[i];
            if (guid != effect.Guid) continue;
            made.Shells.RemoveAt(i);
            // A flash still fading is left to fade, as it would have been: only a shell that would stay is ended.
            if (wearer.Fx is { Shell: not null, ShellEndsByItself: false } fx && fx.ShellEffect == effect.Guid) { fx.EndShell(); made.Swept++; }
        }
        for (int i = made.Frozen.Count - 1; i >= 0; i--)
        {
            var (guid, held) = made.Frozen[i];
            if (guid != effect.Guid) continue;
            made.Frozen.RemoveAt(i);
            // Still frozen, and still by this effect: its script did not let it go, so the engine does.
            if (held.Fx is { Frozen: true } fx && fx.FrozenEffect == effect.Guid) { fx.Frozen = false; made.Swept++; }
        }
    }

    /// <summary>
    /// How many things and shells a load made again in <paramref name="place"/> that the engine, not their
    /// script, took away when their effect ended (<see cref="Ended"/>): 0 where every script kept its number.
    /// </summary>
    public static int Swept(Location place) => again.TryGetValue(place, out var made) ? made.Swept : 0;

    /// <summary>
    /// **The switch point** (ours). After a load's things, links and effects are back and 1.1's post-load pass
    /// has run (<see cref="PostLoad.FixUp"/>, which takes Earth Meld and Mist Form off): for every effect still
    /// on that <see cref="Loaded"/> kept, what its record names is made again. A thing whose parent, or a shell
    /// whose model, is not back yet — the party comes after the place — waits for the next call. Answers how
    /// many things, shells and frozen poses it put back.
    /// </summary>
    public static int Remake(Location place)
    {
        if (!waiting.TryGetValue(place, out var list) || list.Count == 0) return 0;
        int made = 0;
        foreach (var w in list.ToArray())
        {
            // Nothing for an effect no longer there: one the post-load pass took off, or its owner gone.
            if (w.On.Removed || !w.On.Effects.All.Contains(w.Effect) || w.Effect.Spent) { list.Remove(w); continue; }
            foreach (var s in w.Made.ToArray())
                if (Thing(place, w, s) is { } done) { w.Made.Remove(s); if (done) made++; }
            foreach (var s in w.Shells.ToArray())
            {
                if (place.Thing(s.On) is not { } wearer) continue;                       // not back yet
                wearer.Model3D.PutShell(new ModelFx.ShellState(s.Material, s.Flags, s.Alpha, s.Ramping, s.From, s.To, s.Rate, s.Progress), w.Effect.Guid);
                again.GetOrCreateValue(place).Shells.Add((w.Effect.Guid, wearer));
                w.Shells.Remove(s);
                made++;
            }
            foreach (var s in w.Frozen.ToArray())
            {
                if (place.Thing(s.On) is not { } held) continue;                         // not back yet
                w.Frozen.Remove(s);
                if (held.Animation is not { } mixer || !mixer.HoldPose(s.Rotation, s.Translation)) continue;   // nothing to pose, or not this model's pose
                held.Model3D.Frozen = true;
                held.Model3D.FrozenBy(w.Effect.Guid);
                again.GetOrCreateValue(place).Frozen.Add((w.Effect.Guid, held));
                made++;
            }
            if (w.Made.Count == 0 && w.Shells.Count == 0 && w.Frozen.Count == 0) list.Remove(w);
        }
        return made;
    }

    /// <summary>
    /// One thing made again: true when it was, false when it cannot be (said in the log), null when what it
    /// rides is not back yet.
    /// </summary>
    static bool? Thing(Location place, Waiting w, EffectThingSave s)
    {
        WorldThing? parent = null;
        if (s.Rides is { } rides && (parent = place.Thing(rides)) is null) return null;
        if (parent is { InWorld: false }) return false;
        // The number it had, or not at all: the script removes it by that number, and a pad under another
        // would stay for good.
        if (place.Thing(s.Guid) is not null)
        {
            log.LogWarning("load: {Effect}'s {Template} cannot have GUID {Guid} back, which another thing holds; not made again",
                           w.Effect.Definition.Name, s.Template, s.Guid);
            return false;
        }
        if (place.Restore(s.Template, s.Guid, null) is not { } made)
        {
            log.LogWarning("load: {Effect} had made a {Template}, which there is no template for; not made again", w.Effect.Definition.Name, s.Template);
            return false;
        }
        if (made.Guid != s.Guid || !Makes(made))
        {
            place.Remove(made, drop: false);
            log.LogWarning("load: {Effect}'s {Template} is not a prop a load makes again; left out", w.Effect.Definition.Name, s.Template);
            return false;
        }
        made.Orientation = s.Orientation;
        made.Position = s.Position;
        made.Sector = s.Sector;
        made.Rehome(s.Position, s.Orientation);
        place.Enter(made);
        place.Resector(made, s.Sector);
        if (s.Keyframes is { } keys) made.Keyframes = [.. keys.Select(k => new Formats.NsdKeyframe(k.Orientation, k.Position))];
        if (s.Alpha is { } alpha) made.Model3D.SetAlpha(alpha, 0f);
        if (s.Scale is { } scale) made.Model3D.SetScale(scale, 0f);
        made.MadeByEffect = w.Effect.Guid;
        again.GetOrCreateValue(place).Things.Add((w.Effect.Guid, made));
        Net.MadeHere.Again(w.Effect, made);                                        // kept back from the players' machines as it was
        if (parent is not null && s.Relative is { Length: 12 } r && made.AttachedTo is null)
        {
            var m = new Matrix4x4(r[0], r[1], r[2], 0, r[3], r[4], r[5], 0, r[6], r[7], r[8], 0, r[9], r[10], r[11], 1);
            parent.Attachments.Add(new Attachment(made, s.AttachFlags, s.Bone, m));
            made.AttachedTo = parent;
        }
        return true;
    }

    /// <summary>
    /// <see cref="Looks"/> of a thing wherever it is — just built and not yet in the world, too: what a record
    /// may ask a load to make. A record that names anything else (a save edited by hand) is passed over.
    /// </summary>
    public static bool Makes(WorldThing made) =>
        (made.Flags & WorldThing.Flag.SaveNever) != 0 && made.Type == ThingType.Prop && made.Collide == CollideShape.None
        && made.Script.Length == 0 && made.Actor is null && made.Item is null && made.ExpireAt == 0;
}
