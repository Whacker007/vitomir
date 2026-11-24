// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The party as a script sees it: who is in it, where a conversation stands them, and the experience
// a script hands out — and which of them the player is steering.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

public sealed partial class Location
{
    /// <summary>
    /// The party in slot order: the player character first, then everybody else carrying
    /// <c>THING_AF_PARTY</c> in the order they are in the world — the order the travel code carries
    /// them in (<see cref="Campaign"/>). The engine's own roster (`rpgParty`, up to four) is kept in
    /// the order members joined, closed up when one leaves; the player
    /// character joins first and never leaves, and companions are made in the order they join, so
    /// this is the same list — except for somebody who leaves and later rejoins, whom the engine
    /// puts at the end and this puts back where the world has them. Steering somebody else does not
    /// reorder it: F6 is always the first row. The player can (ours, <c>partyorder</c>: <see cref="ReorderParty"/>),
    /// which changes the world's order of the companions and nothing else. At a scene start the scene's own player
    /// character is put in the roster, steered, and the discipline pane set on it; here that
    /// character is simply always the leader below and the default steered/selected one
    /// (<see cref="Selected"/>).
    /// </summary>
    public IEnumerable<WorldThing> PartyMembers()
    {
        var leader = Player?.Thing;
        if (leader is not null && leader.InWorld) yield return leader;
        foreach (var t in Things)
            if (!ReferenceEquals(t, leader) && t.InWorld && t.Actor is { } a && a.Has(Actor.Flag.Party))
                yield return t;
    }

    /// <summary>
    /// A start by group and order: the start whose group matches and whose
    /// own number is <paramref name="order"/>, else the <paramref name="order"/>th unnumbered one
    /// of that group — or the last unnumbered one, when there are fewer. <paramref name="order"/>
    /// counts from 1.
    /// </summary>
    public static NsdStart? PickStart(IReadOnlyList<NsdStart> starts, uint group, uint order)
    {
        NsdStart? found = null;
        uint unnumbered = 0;
        foreach (var s in starts)
        {
            if (s.Group != group) continue;
            if (s.Number == order) return s;
            if (s.Number == 0 && ++unnumbered <= order) found = s;
        }
        return found;
    }

    /// <summary>A conversation start: <see cref="PickStart"/> over <see cref="ConversationStarts"/>.</summary>
    public NsdStart? ConversationStart(uint group, uint order) => PickStart(ConversationStarts, group, order);

    /// <summary>
    /// <c>CodexPlayer.TeleportPartyToConversation(player, index)</c> (the party's
    /// start move). Every party member is picked up and put down on its own start of conversation
    /// group <paramref name="group"/> — the leader on the first, the next member on the second —
    /// turned the way the start faces, with whatever it was walking or doing dropped. The whole
    /// thing is refused, and <b>nobody</b> after the first member without a start is moved, if the
    /// scene has no start for a slot. An empty party is refused.
    /// </summary>
    public bool TeleportPartyToConversation(uint group)
    {
        var party = PartyMembers().ToList();
        if (party.Count == 0) return false;
        for (int i = 0; i < party.Count; i++)
        {
            if (ConversationStart(group, (uint)(i + 1)) is not { } start) return false;
            var member = party[i];
            member.Position = start.Position;
            // The start's matrix is a turn about Z with its sine in element [1].
            float yaw = MathF.Atan2(start.Rotation[1], start.Rotation[0]) * (180f / MathF.PI);
            member.Orientation = new Vector3(0f, 0f, yaw);
            long was = member.Sector;
            member.Sector = start.Sector;
            Props?.Moved(member, was, member.Sector);
            // The ground test (**1**) finds which ground it is over without moving it — the start's own
            // height stands, which is how the convent puts Christof in his bed rather than on the
            // floor beside it. The walk is stopped; the order cancelled.
            if (Player is { } p && ReferenceEquals(p.Thing, member)) p.Ground(snap: false);
            else member.Mind?.Walker.Ground(snap: false);
            member.Mind?.Walker.Stop();
            Rpg.Actions.Cancel(member);                 // unforced: a busy member keeps its order
            // The sector move carries the region word into the new sector and runs no region test: only
            // SetPosition and SetTransform do, so `entered` waits for the member's
            // next move (until then Vitomir tested here and fired it on the spot).
            Regions?.Crossed(member, was);
        }
        return true;
    }

    // ---- steering ------------------------------------------------------------------------------

    /// <summary>The walker of the character being steered — what the mouse drives.</summary>
    public Player? SteeredWalker => Steered is { } who
        ? who.Locomotion ?? (ReferenceEquals(who, Player?.Thing) ? Player : null)
        : Player;

    /// <summary>
    /// **The party's selected member** (`rpgParty+4`) — the steered character
    /// (<see cref="Steered"/>) except while a possession lasts: then the
    /// possessed body is steered and the caster stays selected. The engine reads the two in
    /// different places, and the difference is how a possession hands control back. The party
    /// follows the selected member, and so do the fight leash, the hurt
    /// member's run back and the rally; the mouse refusal and the death's
    /// hand-over test it; the next member counts from it. The
    /// camera, the panes, the cursor and the AI's own gate go by the steered one.
    /// </summary>
    public WorldThing? Selected => Minds?.Selected ?? Player?.Thing;

    /// <summary>
    /// Raised when the player takes over somebody else, with the one now steered. The engine
    /// updates the interface in the same breath: the panes follow the new character,
    /// the status bar lights its row, the camera slides over to it,
    /// and the walk ring is put on where it is walking to, or taken away.
    /// </summary>
    public event Action<WorldThing>? SteeredChanged;

    /// <summary>The shell flashed over a character as the player takes it over.</summary>
    public const string SteerShell = "shellSprite_white";
    /// <summary>In, then out, over a fifth of a second (flags 0x5000, of which 0x4000 is the one kept).</summary>
    public const float SteerShellRamp = 0.2f;

    /// <summary>
    /// **Take over a party member** — the party's select. Only a player-type character,
    /// and nothing to do when it is already both the selected member and the steered
    /// one — so while a possession lasts, the caster's own slot takes the
    /// controls back from the body. Otherwise it must be in the party: it becomes the
    /// party's selected member (`party+4`) and then the steered character (<see cref="SetSteered"/>),
    /// and whoever was steered before goes back to its own party AI, following the new one.
    /// Whatever either was doing carries on: a walk the new one's AI had started is simply now the
    /// player's, ring and all.
    /// </summary>
    public bool Steer(WorldThing who)
    {
        if (who.Type != ThingType.Player || who.Actor is not { } actor) return false;
        if (ReferenceEquals(Selected, who) && ReferenceEquals(Steered, who)) return true;
        if (!actor.Has(Actor.Flag.Party) || Minds is not { } minds) return false;
        minds.Selected = who;
        return SetSteered(who);
    }

    /// <summary>
    /// **The steered character, and nothing else** — `SetSteered`: the global is set,
    /// the white shell flashes over it, and the panes turn to it. It asks nothing about the party:
    /// it is how a possession hands the controls to the body it took and back to the
    /// caster, and <see cref="Steer"/> is this and the party's own bookkeeping.
    /// </summary>
    public bool SetSteered(WorldThing who)
    {
        if (Minds is not { } minds) return false;
        minds.Steered = who;
        who.Model3D.SetShell(SteerShell, 0x5000, 0f, 1f, SteerShellRamp);
        SteeredChanged?.Invoke(who);
        return true;
    }

    /// <summary>
    /// The member in party slot <paramref name="slot"/> — F6 is slot 0, F9 slot 3.
    /// Nothing when the slot is empty.
    /// </summary>
    public bool SteerSlot(int slot) =>
        slot >= 0 && PartyMembers().ElementAtOrDefault(slot) is { } member && Steer(member);

    /// <summary>
    /// **The party put in a new order** (ours, <c>partyorder</c>: the portraits dragged about on the bar). 1.1 has no
    /// way to change the order: its roster keeps the members in the order they joined. Here the order is the world's own
    /// (<see cref="PartyMembers"/>), so the members are put in the new order <b>in the places in the world's list they
    /// already take</b>: nothing else in the list moves, the leader is not touched, and everything that goes by the
    /// party's order — the rows on the bar and their heads, F6 to F9, Tab, the formation's slots, where travel and a
    /// conversation stand each one, and the save — follows without anything new to keep.
    /// </summary>
    /// <param name="order">
    /// The whole party, the leader first: exactly the members <see cref="PartyMembers"/> answers now, in any order after
    /// the first. Anything else (somebody missing, somebody extra, the leader moved) changes nothing.
    /// </param>
    /// <returns>Whether the order changed.</returns>
    public bool ReorderParty(IReadOnlyList<WorldThing> order)
    {
        var now = PartyMembers().ToList();
        if (now.Count < 3 || order.Count != now.Count || !ReferenceEquals(order[0], now[0])) return false;
        var set = new HashSet<WorldThing>(now, ReferenceEqualityComparer.Instance);
        for (int i = 0; i < order.Count; i++) if (!set.Remove(order[i])) return false;   // each exactly once
        if (now.SequenceEqual(order)) return false;
        // The places the companions take in the world's list now, in the list's order, are given out again in the new order.
        var places = new List<int>(now.Count - 1);
        var companions = new HashSet<WorldThing>(now.Skip(1), ReferenceEqualityComparer.Instance);
        for (int i = 0; i < things.Count; i++) if (companions.Contains(things[i])) places.Add(i);
        for (int i = 0; i < places.Count; i++) things[places[i]] = order[i + 1];
        Diagnostics.Journal.Current?.Write(Milliseconds, Diagnostics.JournalKind.Order,
            ("what", "party order"), ("who", string.Join(", ", order.Select(Diagnostics.Watch.Name))));
        return true;
    }

    /// <summary>
    /// The next member after the selected one, round to the first (which counts from
    /// `party+4`). With <paramref name="skipDead"/> the dead are passed over — which is how control
    /// moves on when the selected character dies or leaves; Tab asks without
    /// it, so it will hand you a fallen companion. False when nobody qualifies — and then
    /// the search itself ends the game (<see cref="PartyDown"/>).
    /// </summary>
    /// <param name="going">A member being destroyed, which keeps its slot for the search (`Location.Destroyed.cs`).</param>
    public bool SteerNext(bool skipDead, WorldThing? going = null)
    {
        var party = going is null ? PartyMembers().ToList() : PartyWith(going);
        int at = Selected is { } now ? party.IndexOf(now) : -1;
        bool Fit(WorldThing t) => !skipDead || t.Actor?.Has(Actor.Flag.Dead) != true;
        for (int i = at + 1; i < party.Count; i++)
            if (Fit(party[i])) return Steer(party[i]);
        for (int i = 0; i < party.Count; i++)
            if (Fit(party[i])) return Steer(party[i]);
        PartyDown();
        return false;
    }

    /// <summary>
    /// **Nobody left standing**: the party's next-member search came up empty
    /// — every member dead, or no member at all — so it asks the main sequence for the party's end:
    /// "All coterie members are dead!" and "Press Esc to load saved game." over
    /// the world, which goes on. The search is asked when the selected member dies
    /// or leaves, so a last companion walking out on a dead party ends it too.
    /// A journal row marks the moment.
    /// </summary>
    void PartyDown()
    {
        Diagnostics.Journal.Current?.Write(Milliseconds, Diagnostics.JournalKind.Order,
            ("what", "party down"), ("who", Selected is { } s ? Diagnostics.Watch.Name(s) : ""));
        Session?.Chronicle?.EndGame(Chronicle.EndPartyDead);
    }

    /// <summary>
    /// **Humanity's end** (single player): every member of the party is marked dead
    /// (flag <c>0x10000000</c>), and each whose Humanity is at nothing
    /// gets the frenzied face and their name in the headline. Answers the headline's
    /// pairs: <c>%P</c> and the names run together with nothing between them — the original appends
    /// each with a bare string copy — so two members lost at once read "ChristofWilhelm".
    /// </summary>
    public string HumanityGone()
    {
        var pairs = new System.Text.StringBuilder("%P");
        foreach (var member in PartyMembers())
        {
            if (member.Actor is not { } a) continue;
            a.Flags |= Actor.Flag.Dead;
            if (a.Stat[(int)ActorStat.Humanity] != 0f) continue;
            pairs.Append(Session?.NameOf(member) ?? member.Template);
            if (member.Type == ThingType.Player) a.HeadAction = Ui.HeadMode.Frenzy;
        }
        return pairs.ToString();
    }

    /// <summary>
    /// **Tab**: the interface's click (sound 12), the next member (without
    /// skipping the dead), and then, one time in five, the one now steered says
    /// so — <c>vox_newplayer</c> (0x94) — when it is standing still, not busy, not dead or held, and
    /// free to speak. Answers who is steered after.
    ///
    /// **Except while steering a possessed body, when Tab ends the possession instead**
    /// (<see cref="EndSteeredPossession"/>) and moves on to nobody: the controls come back to the
    /// caster, and the body goes back to its own team — which is how the party can then attack it.
    /// F6 to F9 and a portrait click do not end it (the party's select, which asks
    /// nothing about a possession): they take the controls back and leave the body on the caster's
    /// side until the effect runs out.
    /// </summary>
    /// <param name="network">A network game: Tab's party branch is not reached — nobody else steered, no possession ended (`Net.StoryTab`).</param>
    public WorldThing? SteerTab(Func<float> roll, bool network = false)
    {
        Engine.Audio.Noise.Ui(this, TabSound);
        if (!network && !EndSteeredPossession()) SteerNext(skipDead: false);
        if (Steered is { Actor: { } a } who && SteeredWalker is not { Walking: true }
            && a.BusyUntil <= Milliseconds && !a.Has(Actor.Flag.Dead) && !a.Has2(Actor.Flag2.Restrained)
            && Engine.Audio.Noise.CanSpeak(who) && roll() < NewPlayerChance)
            Engine.Audio.Noise.Foley(who, Engine.Audio.FoleyEvent.VoxNewPlayer);
        return Steered;
    }

    /// <summary>
    /// **Ending the possession the player is steering** — the one test Tab and a journey
    /// both make before anything else: when the steered character's mind is possessed
    /// (0x10000000) and the party has a selected member with a mind, every possessing effect comes off
    /// the body (0, 999999.9) — and the possession's end hands the
    /// controls back to the caster and the effect's script gives the body its own team back. False
    /// when nobody possessed is being steered.
    /// </summary>
    public bool EndSteeredPossession()
    {
        if (Steered is not { Mind: { } body } possessed || (body.Flags & Ai.Mind.FlagPossessed) == 0
            || Selected?.Mind is null) return false;
        Rpg.EffectRuntime.RemoveKind(possessed, EffectKind.Possessing, 0);
        return true;
    }

    /// <summary>The interface sound Tab makes.</summary>
    public const int TabSound = 12;
    /// <summary>How often the one taken over by Tab says so.</summary>
    public const float NewPlayerChance = 0.2f;

    /// <summary>
    /// <c>CodexPlayer.AwardPartyExperience(amount)</c>: the amount, made positive, to
    /// every party member that is not dead, through the one award. Answers who got it.
    /// On a network host it is split between the players instead (`Net.NetAwards` —
    /// <paramref name="near"/> is the thing a kill's or a quest's award is tied to).
    /// </summary>
    public List<WorldThing> AwardPartyExperience(int amount, Action<WorldThing, int>? rankGained = null, WorldThing? near = null)
    {
        if (Net.NetAwards.Split(this, amount, near, rankGained) is { } split) return split;
        int gift = Math.Abs(amount);
        var given = new List<WorldThing>();
        foreach (var member in PartyMembers())
        {
            if (member.Actor is not { } a || a.Has(Actor.Flag.Dead)) continue;
            Experience.Award(member, gift, rankGained);
            given.Add(member);
        }
        return given;
    }
}
