// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What the Storyteller can do, as the engine does it — the half of the Storyteller's bar and its five
// panes that is not drawing. Each power answers the line 1.1 puts in the message
// box (`UI_STSB*`, `UI_STCP*`, `UI_STOP*` …), in words, so the interface only has to show it.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

public sealed class StoryPowers(GameServer server, Location place, Ui.Strings? strings)
{
    readonly Func<string, string?> find = k => strings?.Find(k);

    public GameServer Server { get; } = server;
    public Location Place { get; } = place;

    /// <summary>The Storyteller's current target: what every power works on.</summary>
    public WorldThing? Target { get; set; }
    /// <summary>The Storyteller's own invisible body, the thing named `storyteller` (found once and kept).</summary>
    public WorldThing? Ghost { get; private set; }
    /// <summary>What the Storyteller steered before taking the seat, to hand back when leaving it.</summary>
    public WorldThing? Before { get; set; }

    /// <summary>The Storyteller's template (`Global.not`'s `Storyteller`, model `storyteller.nod`), found by name.</summary>
    public const string GhostTemplate = "Storyteller";

    /// <summary>The disposition colours of the character-mode button: talk, neutral, enemy.</summary>
    public enum Disposition { Talk = 0, Neutral = 1, Enemy = 2 }

    string Text(string key) => find(key) ?? key;
    string Say(string key, string name) => Ui.Strings.Format(find, key, "%s" + name).Replace("%s", name);

    static bool Person(WorldThing? t) => t?.Type is ThingType.Actor or ThingType.Player;

    /// <summary>Whether a thing is some player's character (the character of one of the player slots).</summary>
    public bool IsPlayer(WorldThing t) => Server.Players.Any(p => ReferenceEquals(p.Avatar, t));

    /// <summary>A thing's name as the panes list it.</summary>
    public string NameOf(WorldThing t) => Ui.Rollover.NameOf(t, strings) is { Length: > 0 } n ? n : t.Template;

    // ---- the bar ------------------------------------------------------------------------------

    /// <summary>
    /// Possess (from the bar's button and the actor pane's, each with the
    /// target): a character (type 0 or 7) is refused when its mind is **a pet** (bit 0x4),
    /// **possessed** already (bit 0x10000000 — a Dominate's Possession holds it) or **itself
    /// possessing** (mode 13) — <see cref="Refuses"/>; one with no mind passes all three. A
    /// frenzied, afraid or mesmerised one is taken (an earlier build refused those instead, misreading the three tests).
    /// Then, when it is not the current control, it becomes it, its panes
    /// and the camera go with it, and its player hears `UI_NET_POSSESSED` if they try to move it;
    /// when it **is** — Possess asked again on the body held — the control goes back to **the host's own
    /// character** (`*`, the local player), not the ghost. Either
    /// way `UI_STSBPOSSESS` "Possessing %s.". A null <paramref name="who"/> lets go of whatever is held —
    /// the callers' own (ours: leaving the seat, a player's body jumped away); 1.1's button does nothing then.
    /// </summary>
    public string Possess(WorldThing? who)
    {
        if (who is not null && (!Person(who) || Refuses(who))) return "";
        if (Server.Possessed is { } held && (who is null || ReferenceEquals(who, held)))
        {
            Server.Possessed = null;
            var back = Own is { InWorld: true } mine ? mine : Before;
            if (back is not null) Place.SetSteered(back);
            return Say("UI_STSBPOSSESS", back is null ? "" : NameOf(back));
        }
        if (who is null) return "";
        // One control for the whole game: a body in another of the host's places (one carried there and let
        // go) is steered in its own place, as the carried body is (`StoryHand.Take`; ours in the way of it).
        if (who.Place is { } there && !ReferenceEquals(there, Place))
            return StoryHand.Take(Server, who) ? Say("UI_STSBPOSSESS", NameOf(who)) : "";
        Before ??= Place.Steered;
        Server.Possessed = who;
        Place.SetSteered(who);
        return Say("UI_STSBPOSSESS", NameOf(who));
    }

    /// <summary>
    /// The three minds 1.1's Possess will not take: a pet (<see cref="Ai.Mind.FlagPet"/>),
    /// one possessed already (<see cref="Ai.Mind.FlagPossessed"/>) and one that is possessing
    /// somebody (mode 13, <see cref="Ai.AiMode.Possessing"/>). No mind refuses nothing.
    /// </summary>
    public static bool Refuses(WorldThing who) =>
        who.Mind is { } mind && ((mind.Flags & (Ai.Mind.FlagPet | Ai.Mind.FlagPossessed)) != 0 || mind.Mode == Ai.AiMode.Possessing);

    /// <summary>The host's own character — 1.1's local player (`*`), slot 0's — which a second Possess steers again.</summary>
    WorldThing? Own => Server.Slots.Count > 0 ? Server.Slots[0]?.Avatar : null;

    /// <summary>
    /// **A left click on a thing in the Storyteller's seat** (the seat's arm of the left-click handler):
    /// no order at all — a thing that is not a region (type 3) becomes the current target,
    /// the camera goes to it (the caller's), the panes are redone, and the ghost — when
    /// it is in the world, is not the one steered and is not the thing — is taken out of the world
    /// (the actor pane redone). So in the seat the steered body only walks; its attacks,
    /// words and pick-ups are the host's out of the seat, which keeps the control. False for a region.
    /// Taking it out is `RemoveFromWorld` (<see cref="Location.Withdraw"/>): the ghost goes on existing, its GUID kept
    /// by the Storyteller, for the right button (<see cref="GhostAt"/>) and Set Ghost to bring back — an earlier build
    /// destroyed it (<see cref="Location.Remove"/>), so the next Set Ghost made another.
    /// </summary>
    public bool Clicked(WorldThing thing, WorldThing? steered)
    {
        if (thing.Type == ThingType.Region) return false;
        Target = thing;
        if (Ghost is { InWorld: true, Type: ThingType.Actor or ThingType.Player, Place: { } at } g
            && !ReferenceEquals(g, steered) && !ReferenceEquals(g, thing))
            at.Withdraw(g);
        return true;
    }

    /// <summary>
    /// The seat's arm of the left click whole: <see cref="Clicked(WorldThing, WorldThing?)"/>, and **the camera given
    /// the thing** (the camera made to follow it, the old subject kept for the handover's
    /// slide). In the host's own place an earlier build made the thing the target and nothing more; 1.1 does the same
    /// there as in the carried body's place, so the camera goes to a townsman clicked in the inn as well.
    /// Null <paramref name="camera"/> (no window) leaves the camera out of it.
    /// </summary>
    public bool Clicked(WorldThing thing, WorldThing? steered, WorldCamera? camera)
    {
        if (!Clicked(thing, steered)) return false;
        camera?.SetSubject(thing);
        return true;
    }

    /// <summary>
    /// **The Storyteller's ground move, in the seat** (the first arm of the right-click handler, the command since
    /// <see cref="Ground"/>): the target is put where the pointer meets the ground. The original tests the
    /// click's mask for bit 0x4 — Alt (DirectInput's LMENU and RMENU; <see cref="InputModifier"/>)
    /// — and takes the arm in the seat, or out of it with the options word's bit 0x80
    /// in a game that is not a single player's and, over the network, only a moderated one
    /// (flag 0x400) — that bit is unread, so only the seat's is built. Vitomir asks the key command instead of
    /// the bit (`Ui.KeyCommand.StoryMove`, Alt and the right button by default, and
    /// remappable), so this answers the seat alone. It comes after the effect icon's cancel
    /// (<paramref name="cancelEffect"/>) and before the mind's refusal and the hand, so it is never a cast.
    /// The ground is the camera's sweep through the pointer (<paramref name="ground"/>, the
    /// caller's `WorldSweep.Pointer` in the place the camera is in); nothing met spends the click. Then the
    /// target stops walking, and with **Space** held
    /// it only turns to face the spot (which sends the facing in a network
    /// game) — otherwise it keeps its turn and **is put at the spot** (the transform with the point
    /// for its position, the sector found again; <see cref="Location.Move"/>), and a network
    /// game sends where it is (here the next snapshot, `Replication`). **Any thing** is moved — the move
    /// asks no type, so a door goes where it is put (as one can be in 1.1). Answers what happened, for the
    /// console; null out of the seat, where the click casts as ever. **Ours**: 1.1 reads through a null target
    /// (no target: it reads the null thing); here nothing happens, and a target in another of the
    /// host's places than the one picked is not moved.
    /// </summary>
    public string? Placed(Location place, Vector3? ground, InputModifier mods, Func<bool>? cancelEffect = null)
    {
        if (!Server.Storyteller) return null;
        if (cancelEffect?.Invoke() == true) return "an effect cancelled";           // the effect icon's cancel first
        if (ground is not { } point) return "no ground under the pointer";          // the sweep met nothing
        if (Target is not { InWorld: true } target) return "no target";             // ours: 1.1 reads a null
        if (!ReferenceEquals(target.Place, place)) return $"{target.Template} is not in {place.Name}";
        return Put(place, target, point, mods);
    }

    /// <summary>
    /// The arm's work on <paramref name="who"/> — the target in the seat, the control out of it:
    /// it stops walking, and with **Space** held only turns to face the spot,
    /// otherwise is put at it with its own turn kept (<see cref="Location.Move"/>).
    /// </summary>
    public static string Put(Location place, WorldThing who, Vector3 point, InputModifier mods)
    {
        Actions.StopWalking(who);
        if ((mods & InputModifier.Space) != 0)
        {
            who.Face(point);                                                          // facing the point, sent in a network game
            return $"{who.Template} turns to {point.X:0},{point.Y:0},{point.Z:0}";
        }
        place.Move(who, point);
        return $"{who.Template} put at {point.X:0},{point.Y:0},{point.Z:0}";
    }

    /// <summary>
    /// **The same arm out of the seat**: with the options word's **bit 0x80**
    /// — `-devmode` on 1.1's command line (<see cref="Ui.GameOptions.DevMode"/>,
    /// Vitomir's `--devmode`) — the move is taken outside the seat too, in a game that is **not a player's
    /// machine** (net mode not 1: single player or a host) and, on a host, only a **moderated** one (flag
    /// 0x400). Out of the seat it works on **the control** (the steered character),
    /// not the target: a developer's Alt and right button puts the player's own character where the pointer meets
    /// the ground (<see cref="Put"/>). As in the seat the effect icon's cancel comes first and a sweep
    /// that meets nothing spends the click. Null when the arm is not taken — the click then goes on as ever (the
    /// cast). The key is the command (<see cref="Ui.KeyCommand.StoryMove"/>), which the caller asks; the seat's
    /// own is <see cref="Placed"/>. **Ours**: no control (1.1 reads a null) is not taken.
    /// </summary>
    public static string? DevMoved(uint options, bool client, GameFlags? host, Location place, WorldThing? steered,
                                   Vector3? ground, InputModifier mods, Func<bool>? cancelEffect = null)
    {
        if ((options & Ui.GameOptions.DevMode) == 0 || client) return null;          // Mode 1
        if (host is { } flags && (flags & GameFlags.Moderated) == 0) return null;
        if (steered is null) return null;                                             // ours
        if (cancelEffect?.Invoke() == true) return "an effect cancelled";           // the effect icon's cancel first
        if (ground is not { } point) return "no ground under the pointer";          // the sweep met nothing: 4
        return Put(place, steered, point, mods);
    }

    /// <summary>
    /// **The right button on the ground, in the seat**, whole: the ground move
    /// when the click is it (<paramref name="move"/>, <see cref="Placed"/>); otherwise the right click goes on as
    /// ever — the effect icon's cancel, the mind's refusal (on the party's selected member,
    /// `Rpg.CastInput.Refused`), and then **the control's hand** (`*`): something in it is used
    /// as ever, and with nothing in it **the seat never casts** — it puts the ghost where the pointer meets the
    /// ground (<see cref="GhostAt"/>) and answers 4, the click spent whether or not the
    /// sweep met anything. Out of the seat the control's discipline is cast there. Null when the
    /// click is the cast's (out of the seat, a refusal, a hand to use), which the caller then makes
    /// (`Rpg.CastInput.OnGround` — which asks the effect icon itself, so it is asked once either way).
    /// <paramref name="ground"/> is the sweep through the pointer in <paramref name="place"/>.
    /// </summary>
    public string? Ground(Location place, Hit? ground, InputModifier mods, bool move, WorldThing? steered,
                          Func<bool>? cancelEffect = null)
    {
        if (!Server.Storyteller) return null;
        if (move) return Placed(place, ground?.Point, mods, cancelEffect);           // the first arm
        if (steered is { } s && (CastInput.Refused(s) || s.Actor?.Held is not null)) return null;   // the mind's refusal; the hand
        if (cancelEffect?.Invoke() == true) return "an effect cancelled";           // the effect icon's cancel first
        if (ground is not { } hit) return "no ground under the pointer";            // the sweep met nothing: 4 all the same
        return GhostAt(place, hit.Point, hit.Sector);
    }

    /// <summary>
    /// **The ghost to the ground**, from the seat's right button on the ground: the ghost — the thing kept
    /// while it is a character (type 0 or 7), else the one named `storyteller` —
    /// is put into the spot's sector (which brings back one a seat click took out of the world) and at the point with its own turn kept, then made **the control** and
    /// **the target**. As the control it is nobody's possession any more: 1.1's "possessed" is the
    /// control (the possession test asks for the control). **Ours**: 1.1 only finds the ghost — with none it would call through a
    /// null thing — where Vitomir makes one as <see cref="SetGhost"/> does. **In another of the host's places**
    /// (the Storyteller's view of a carried body's place, where an earlier build spent the click) the spot's sector is
    /// that place's, so 1.1's ghost goes there — the sector change asks nothing of which world the sector is in — and is
    /// the control there: here it is that place's ghost (<see cref="Summon"/>), steered there and held as a carried
    /// body is (<see cref="StoryHand.Take"/>, which lets the body go), and the caller's view follows it.
    /// </summary>
    public string GhostAt(Location place, Vector3 point, long sector)
    {
        if (Summon(place, point, sector) is not { } ghost) return $"no {GhostTemplate} template";
        if (!Steer(ghost)) return $"the ghost cannot be steered in {place.Name}";
        Target = ghost;
        return $"the ghost put at {point.X:0},{point.Y:0},{point.Z:0}";
    }

    /// <summary>
    /// The ghost made the control: in the host's own place the one steered there, nobody's possession
    /// held; in another of the host's places the one steered there as the possession, the way a carried body is
    /// (<see cref="StoryHand.Take"/>).
    /// </summary>
    bool Steer(WorldThing ghost)
    {
        Before ??= Place.Steered;
        if (ghost.Place is { } there && !ReferenceEquals(there, Place)) return StoryHand.Take(Server, ghost);
        Place.SetSteered(ghost);
        Server.Possessed = null;
        return true;
    }

    /// <summary>The ghost each of the host's places has had: a thing here belongs to one place (weak: a place let go is not kept).</summary>
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<Location, WorldThing> ghosts = new();

    /// <summary>
    /// The ghost found (then by name) or made (ours), and brought into the world at a point in
    /// <paramref name="where"/>: into <paramref name="sector"/> (back into the world if a seat click took
    /// it out) and then moved there (the sector found again from it). **One ghost**: 1.1's one thing
    /// changes sector, and a sector may be in another of the host's places; here each place has its own, and the
    /// one standing in another place first leaves the world there (ours, taken out of the world as a seat click does), let go if it was steered.
    /// </summary>
    WorldThing? Summon(Location where, Vector3 point, long sector)
    {
        if (Ghost is { InWorld: true, Place: { } away } left && !ReferenceEquals(away, where))
        {
            StoryHand.LetGo(left);
            away.Withdraw(left);
        }
        if (Ghost is not { Removed: false, Type: ThingType.Actor or ThingType.Player } kept || !ReferenceEquals(kept.Place ?? Home(kept), where))
            Ghost = (ghosts.TryGetValue(where, out var had) && !had.Removed ? had : null)
                    ?? where.Things.FirstOrDefault(t => t.Template.Equals(GhostTemplate, StringComparison.OrdinalIgnoreCase))
                    ?? where.Place(GhostTemplate, point, Vector3.Zero, sector);
        if (Ghost is not { } ghost) return null;
        ghosts.AddOrUpdate(where, ghost);
        if (ghost.Actor is { } a) a.Flags |= Actor.Flag.AiPaused;
        if (!ghost.InWorld)
        {
            ghost.Position = point;
            ghost.Sector = where.At(point, sector);
            where.Enter(ghost);
        }
        else
        {
            where.Resector(ghost, sector);
            where.Move(ghost, point);
        }
        return ghost;
    }

    /// <summary>The place a ghost out of the world was made in (the host's own for one found there).</summary>
    Location Home(WorldThing ghost) => ghosts.FirstOrDefault(kv => ReferenceEquals(kv.Value, ghost)).Key ?? Place;

    /// <summary>
    /// Set Ghost (the bar's button): the Storyteller's body — found or made when there is
    /// none (<see cref="Summon"/>; one a seat click took out of the world comes back) — put at the target
    /// (or at whoever is steered), made the target, and steered, which lets go of a
    /// possession as the ground's ghost does (<see cref="GhostAt"/>). A target in another of the host's places
    /// takes the ghost there (into the target's sector), where the caller's view follows it.
    /// </summary>
    public string SetGhost(WorldThing? at)
    {
        at ??= Target ?? Place.Steered;
        if (at is null) return "";
        if (Summon(at.Place ?? Place, at.Position, at.Sector) is not { } ghost) return $"no {GhostTemplate} template";
        ghost.Orientation = at.Orientation;                                           // The target's transform, turn and all
        Target = ghost;
        Steer(ghost);
        return "";
    }

    /// <summary>
    /// **The bar's Revive** (on the target): nothing chosen is `UI_STSBREVIVENOSELECTION`; a target
    /// that is **not a character** (type 0 or 7) is `UI_STSBREVIVENOTDEAD` — the only place 1.1 shows that
    /// line, and its only refusal — and **any character**, dead or alive, goes through the actor's revive
    /// (full health and blood, the sound on), which works "whether or not it was dead": full health and blood, the frenzy
    /// ended, the effects reset, the mind restarted, the awakening's noise (<see cref="Damage.Revive"/>). So
    /// the Storyteller heals a wounded, living character to full with it. (A comparison with 1.1 found Vitomir refusing a
    /// living one with the "not dead" line; this takes 1.1's reading.) Answers the line for the message box.
    /// </summary>
    public string Revive(WorldThing? who)
    {
        if (who is null) return Text("UI_STSBREVIVENOSELECTION");
        if (!Person(who)) return Text("UI_STSBREVIVENOTDEAD");                      // Type 0 or 7
        Damage.Revive(who, 1f, 1f, sound: true);
        return "";
    }

    /// <summary>
    /// **The actor pane's Revive** (on the chosen row): no message at all — nothing
    /// chosen does nothing, and anything else is revived as the bar's, dead or
    /// not. 1.1 only asserts the row is a character (the pane lists nothing else); ours refuses one quietly.
    /// True when it revived (1.1 then refills the pane).
    /// </summary>
    public bool ReviveChosen(WorldThing? who)
    {
        if (!Person(who)) return false;                                             // 1.1 asserts this; ours refuses
        Damage.Revive(who!, 1f, 1f, sound: true);
        return true;
    }

    /// <summary>
    /// **The bar's Delete** (on the target): nothing chosen (`UI_STSBDELETENOSELECTION`), a
    /// protected thing (thing flag 0x200000, `UI_STSBDELETEPROTECTED`), an active player's character
    /// (type 7 in a used slot, `UI_STSBDELTHINGERROR`); then <see cref="Remove"/>.
    /// </summary>
    public string Delete(WorldThing? who)
    {
        if (who is null) return Text("UI_STSBDELETENOSELECTION");
        if ((who.Flags & DeleteProtected) != 0) return Text("UI_STSBDELETEPROTECTED");
        if (ActivePlayer(who)) return Text("UI_STSBDELTHINGERROR");
        Remove(who);
        return "";
    }

    /// <summary>
    /// **The actor pane's Delete** (on the chosen row): as the bar's but with **no
    /// test of the protected flag** and no line for nothing chosen — only an active player's character is
    /// refused (`UI_STSBDELTHINGERROR`, the answer). Null when it went (1.1 then refills the pane),
    /// or nothing was chosen.
    /// </summary>
    public string? DeleteChosen(WorldThing? who)
    {
        if (who is null) return null;
        if (ActivePlayer(who)) return Text("UI_STSBDELTHINGERROR");
        Remove(who);
        return null;
    }

    /// <summary>
    /// The tail both Deletes share: a thing that is **the target** has the ghost
    /// set at it first (<see cref="SetGhost"/>: the ghost moved to where it stood, made the target and
    /// **steered**), and then the thing is destroyed **only when it is not the control**.
    /// So Delete on the target leaves the Storyteller's ghost standing where the thing was, steered; the ghost
    /// itself is never deleted (set at itself, it is the control), and neither is a possessed body the pane's row
    /// names while it is not the target (it is the control). A possessed body that is the target is deleted:
    /// the ghost takes the control from it first. **Ours**: with no ghost 1.1's Set Ghost reads through a null
    /// thing; here one is made, as <see cref="SetGhost"/> does.
    /// </summary>
    void Remove(WorldThing who)
    {
        if (ReferenceEquals(who, Target)) SetGhost(who);
        if (ReferenceEquals(who, Control)) return;
        (who.Place ?? Place).Remove(who, drop: false);
    }

    /// <summary>
    /// The control: the body the Storyteller holds — possessed here, or carried to
    /// another of the host's places (<see cref="StoryHand"/>) — else whoever the host's place steers.
    /// </summary>
    public WorldThing? Control => Server.Possessed is { Removed: false } held ? held : Place.Steered;

    /// <summary>
    /// The Delete's player test: a **type 7** thing that is the character of a used player slot. A character of type 0 is never asked.
    /// </summary>
    bool ActivePlayer(WorldThing who) => who.Type == ThingType.Player && IsPlayer(who);

    /// <summary>`THING_FLAG` 0x200000: not the Storyteller's to delete.</summary>
    public const uint DeleteProtected = 0x200000;

    /// <summary>
    /// Kill: an actor in the world is killed, a script's kill — with its
    /// <c>INVULNERABLE</c> flag (the actor's bit 1) taken off first, so the Storyteller
    /// kills a character the scripts made invulnerable too (a comparison with 1.1; before, it refused).
    /// </summary>
    public string Kill(WorldThing? who)
    {
        if (!Person(who) || who!.Actor is not { Dead: false } a || !who.InWorld) return "";
        a.Flags &= ~Actor.Flag.Invulnerable;
        Damage.Kill(who, null, HitKind.Script, DamageType.Normal, Place.Milliseconds, motions: Place.Motions);
        return "";
    }

    /// <summary>The disposition the character-mode button shows: talk with 0x80, neutral with 0x40000, enemy with neither.</summary>
    public static Disposition DispositionOf(Actor a) =>
        (a.Flags & Actor.Flag.TalkTo) != 0 ? Disposition.Talk
        : (a.Flags & Actor.Flag.Neutral) != 0 ? Disposition.Neutral : Disposition.Enemy;

    /// <summary>
    /// Talk / Neutral / Enemy: the next of the three, with `UI_STSBTALKMODE`,
    /// `UI_STSBNEUTRALMODE` or `UI_STSBENEMYMODE`.
    /// </summary>
    public string CycleDisposition(WorldThing? who)
    {
        if (!Person(who) || who!.Actor is not { } a) return "";
        var next = (Disposition)(((int)DispositionOf(a) + 1) % 3);
        switch (next)
        {
            case Disposition.Talk: a.Flags = (a.Flags & ~Actor.Flag.Neutral) | Actor.Flag.TalkTo; return Say("UI_STSBTALKMODE", NameOf(who));
            case Disposition.Neutral: a.Flags = (a.Flags & ~Actor.Flag.TalkTo) | Actor.Flag.Neutral; return Say("UI_STSBNEUTRALMODE", NameOf(who));
            default: a.Flags &= ~(Actor.Flag.TalkTo | Actor.Flag.Neutral); return Say("UI_STSBENEMYMODE", NameOf(who));
        }
    }

    /// <summary>Pause: the pause for everyone, `UI_STSBPAUSE` / `UI_STSBUNPAUSE`.</summary>
    public void Pause(bool on) => Server.SetPaused(on);

    // ---- the controls pane ----------------------------------------------------------------------

    /// <summary>AI: the whole AI on or off — bit 4 of the AI's main flags, the one the mind gate tests beside PAUSED.</summary>
    public string SetAi(bool on)
    {
        if (Place.Minds is { } minds)
            minds.MainFlags = on ? minds.MainFlags & ~Ai.AiMainFlag.Unnamed4 : minds.MainFlags | Ai.AiMainFlag.Unnamed4;
        return Text(on ? "UI_STCPAIENABLED" : "UI_STCPAIDISABLED");
    }

    public bool AiOn => Place.Minds is not { } m || (m.MainFlags & Ai.AiMainFlag.Unnamed4) == 0;

    /// <summary>Auto ST, Game Lock and Comm Lock: a game flag each, and its line.</summary>
    public string SetFlag(GameFlags flag, bool on)
    {
        Server.Flags = on ? Server.Flags | flag : Server.Flags & ~flag;
        return flag switch
        {
            GameFlags.AutoStoryteller => Text(on ? "UI_STCPENABLEAUTOST" : "UI_STCPDISABLEAUTOST"),
            GameFlags.Locked => Text(on ? "UI_STCPLOCKED" : "UI_STCPUNLOCKED"),
            GameFlags.CommLocked => Text(on ? "UI_STCPCOMMSLOCKED" : "UI_STCPCOMMSUNLOCKED"),
            _ => "",
        };
    }

    /// <summary>The cast list: every player, `"%s (%s)"` with *Server* for the host.</summary>
    public IEnumerable<(string Row, int Slot)> Cast() =>
        Server.Players.Select(p => (p.Host ? $"{p.Name} ({Text("UI_STCPSERVER")})" : p.Name, p.Slot));

    // ---- the actor pane -------------------------------------------------------------------------

    /// <summary>
    /// The actor list: every actor in the place not on its way out (thing flag 0x1000000),
    /// `"%s (%s: %d)"` — the name, *Health*, the health — the players' rows in 0xff317ff4.
    /// </summary>
    public IEnumerable<(string Row, WorldThing Who, uint Colour)> Actors()
    {
        foreach (var t in Place.Things)
        {
            if (!Person(t) || t.Actor is not { } a || t.Removed) continue;
            yield return ($"{NameOf(t)} ({Text("UI_GENERAL_HEALTH")}: {(int)a.Health})", t, IsPlayer(t) ? PlayerRow : 0xffffffffu);
        }
    }

    /// <summary>The actor pane's colour for a player's row.</summary>
    public const uint PlayerRow = 0xff317ff4u;

    // ---- the object pane ------------------------------------------------------------------------

    /// <summary>The object pane's three radio boxes.</summary>
    public enum Kind { Items = 1, Actors = 2, Props = 3 }

    /// <summary>
    /// The templates of a kind (the pane's filter), sorted by name as 1.1's are, and — with the short
    /// list on — only the chronicle's quick list (<paramref name="shortList"/>, its `.nst`). **Ours**:
    /// 1.1 filters by the template's class, template key 5 (items 2, 3, 6–10, 0x13–0x17;
    /// actors 4, 5, 0xc, 0xe; props 0xb, 0xf, 0x10 — the survey); here by the template's thing
    /// type — items, weapons and armour; actors and players; the rest that have a model. 1.1 builds
    /// this name-sorted tree once on chronicle load, one entry
    /// a template — its display name (key 82) and class (key 5), read through its base templates;
    /// here it is built fresh each time the pane asks.
    /// </summary>
    public IEnumerable<string> Templates(Kind kind, IReadOnlyCollection<string>? shortList)
    {
        if (Place.Templates is not { } set) yield break;
        foreach (var name in set.Names.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (name.StartsWith('_')) continue;
            if (shortList is not null && !shortList.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            var tags = set.Resolve(name);
            var type = tags.TryGetValue(Tags.Type, out var ty) ? (ThingType)ty.AsUInt() : ThingType.None;
            bool fits = kind switch
            {
                Kind.Items => type is ThingType.Item or ThingType.Weapon or ThingType.Armor,
                Kind.Actors => type is ThingType.Actor or ThingType.Player,
                _ => type is ThingType.Prop or ThingType.Container && tags.ContainsKey(Tags.Model),
            };
            if (fits) yield return name;
        }
    }

    /// <summary>
    /// Create: the template made where the Storyteller stands, with
    /// `UI_STOPCREATEDOBJECT` or `UI_STOPCOULDNTCREATEOBJECT`.
    /// </summary>
    public string Create(string template)
    {
        var at = Place.Steered ?? Ghost ?? Target;
        WorldThing? made = null;
        if (at is not null) made = Place.Spawn(template, at);
        if (made is not null) Target = made;
        return Say(made is null ? "UI_STOPCOULDNTCREATEOBJECT" : "UI_STOPCREATEDOBJECT", template);
    }

    /// <summary>
    /// The chronicle's quick list: its metafile's `NST` line names a file in `Chronicles/`,
    /// one template a line, `;` and `#` comments skipped.
    /// </summary>
    public static List<string> ShortList(Vfs.GameFiles files, Chronicle? chronicle)
    {
        var names = new List<string>();
        var line = chronicle?.Meta?.Ignored.FirstOrDefault(l => l.StartsWith("NST", StringComparison.OrdinalIgnoreCase));
        if (line is null) return names;
        var file = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
        if (file is null || !files.TryRead("chronicles/" + file, out var data)) return names;
        foreach (var raw in System.Text.Encoding.Latin1.GetString(data).Split('\n'))
        {
            var t = raw.Trim();
            if (t.Length > 0 && t[0] != '#' && t[0] != ';') names.Add(t);
        }
        return names;
    }

    // ---- the scene pane -------------------------------------------------------------------------

    /// <summary>One line of a chronicle's `.nss`: `scene <scene> <subscene> <name key>`.</summary>
    public readonly record struct Step(int Scene, int Subscene, string Name);

    /// <summary>The `.nss` the chronicle's metafile names, at most 64 lines.</summary>
    public static List<Step> Steps(Vfs.GameFiles files, Chronicle? chronicle)
    {
        var steps = new List<Step>();
        foreach (var nss in chronicle?.Meta?.Nss ?? [])
        {
            if (!files.TryRead("chronicles/" + nss, out var data)) continue;
            foreach (var raw in System.Text.Encoding.Latin1.GetString(data).Split('\n'))
            {
                var w = raw.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (w.Length >= 4 && w[0].Equals("scene", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(w[1], out var sc) && int.TryParse(w[2], out var sub) && steps.Count < 64)
                    steps.Add(new Step(sc, sub, w[3]));
            }
        }
        return steps;
    }

    /// <summary>A scene row: `"%s [%d,%d]"`.</summary>
    public string Row(Step s) => $"{Text(s.Name)} [{s.Scene},{s.Subscene}]";

    /// <summary>
    /// **Advance**: the step reached (−1 when the list is read) moves on
    /// one while it is **short of the count** (the test is `reached != count && reached < count`,
    /// signed) and that step runs — every chronicle script's `advance(scene, subscene)`. So from the
    /// **last** step it moves to `count` and runs the slot past the list: the 64-slot table (40
    /// bytes a slot) lies in zeroed memory and the reader writes only the slots it reads, so that slot
    /// is <see cref="PastTheList"/> — `advance(0, 0)`, which neither shipped network chronicle answers
    /// (`MP_CTDChronicle`'s and `MP_LOTChronicle`'s `advance` have no scene 0), and every row is drawn reached.
    /// A further press does nothing (`reached == count`). Null then; otherwise the step to run, with
    /// <paramref name="reached"/> moved on. **Ours**: the slot is always zeros here — 1.1's would still hold a
    /// longer list's step there if a chronicle with more steps had been read earlier in the same run.
    /// </summary>
    public static Step? Advance(ref int reached, int count, Func<int, Step> step)
    {
        if (reached >= count) return null;                                          // `!= count && < count`
        reached++;
        return reached < count ? step(reached) : PastTheList;                      // The slot `count`
    }

    /// <summary>The step table's slot past the list (40 × count bytes in): zeroes — scene 0, subscene 0, no name.</summary>
    public static readonly Step PastTheList = new(0, 0, "");

    /// <summary>Everybody's characters, for the location pane's Jump All — not built: moving players between places needs the host to run more than one place.</summary>
    public static string NotBuilt(string what) => $"{what} is not built yet";

    /// <summary>Where the Storyteller's creations appear if nobody is steered: a player's character, else the start.</summary>
    public Vector3 Here => (Place.Steered ?? Server.Players.FirstOrDefault(p => p.Avatar is not null)?.Avatar)?.Position ?? Place.StartPosition;
}
