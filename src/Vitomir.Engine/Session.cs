// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a game keeps that belongs to no one place: the message log, the quest log, the party's purse.
using Vitomir.Engine.World;

namespace Vitomir.Engine;

/// <summary>
/// The state of a game that outlives a level change. Each of these is a global in the original —
/// the status bar's text box, the quest array, the party
/// object's purse (<c>rpgParty</c>) — and none of them is reset by travel.
/// Every <see cref="Location"/> is handed the one session, the way it is handed the one sound
/// system, and <see cref="Here"/> follows the party.
/// </summary>
public sealed class Session
{
    public Ui.MessageLog Messages { get; } = new();
    public Rpg.QuestLog Quests { get; } = new();

    /// <summary>Every shop built this session and the one open — `Rpg.Shops`.</summary>
    public Rpg.Shops Shops { get; } = new();

    /// <summary>The repeating messages a script can post (<see cref="Ui.GameMessages"/>), written into <see cref="Messages"/>.</summary>
    public Ui.GameMessages Notices { get; }

    /// <summary>Whether the game pause is up, as the host's front end has it (<see cref="Rpg.PausedOrders"/>).</summary>
    public Func<bool>? Paused { get; set; }

    /// <summary>Whether the host sits in the Storyteller's seat, for the stealth fade (<see cref="World.StealthFade"/>).</summary>
    public Func<bool>? Seated { get; set; }

    /// <summary>
    /// The party's money. Cash picked up by a party member goes here, not to
    /// the character; the inventory pane shows it.
    /// </summary>
    public int Gold { get; set; }

    /// <summary>
    /// The screen's fades, flashes and filters (the camera's, `WorldCamera.Effects`), for what the
    /// game does to the whole picture — a frenzy's red flash. Null for a headless run.
    /// </summary>
    public ScreenEffects? Screen { get; set; }

    /// <summary>
    /// The mouse pointer (the game's global cursor), for a refusal the
    /// game itself shows — a put-down whose item is no longer the carrier's (<see cref="Rpg.PutRefusal"/>).
    /// Null for a headless run.
    /// </summary>
    public Ui.CursorState? Cursor { get; set; }

    /// <summary>The sixteen teleport slots (<see cref="World.Teleports"/>): a global, like the quests.</summary>
    public Teleports Teleports { get; }

    /// <summary>The place the party is in now, which is where a quest's experience goes.</summary>
    public Location? Here { get; set; }

    /// <summary>The chronicle being played, when there is one.</summary>
    public Chronicle? Chronicle { get; set; }

    /// <summary>
    /// The chronicle's info bit 0: set for the modern-day chronicles, which is
    /// the difference between "gold" and "cash" in a pickup line.
    /// </summary>
    public bool ModernDay => ((Chronicle?.Flags ?? 0) & 1) != 0;

    /// <summary>
    /// Whether the panes wear their modern-day art (at every scene's start): a modern-day
    /// chronicle that is not a multiplayer one (info bits 1 set and 2 clear). <see cref="Ui.UiAssets.SetModernDay"/>.
    /// </summary>
    public bool ModernDayArt => ((Chronicle?.Flags ?? 0) & 3) == 1;

    /// <summary>Told about every rank a party member reaches, after the message and the bell.</summary>
    public Action<WorldThing, int>? Ranked { get; set; }

    public Session()
    {
        Teleports = new Teleports(this);
        Notices = new Ui.GameMessages { Log = Messages };
        Quests.Post = key => Messages.Post(key);
        // A quest's award is tied to this machine's player — which, on a
        // network host, keeps the split to those in the host's own place.
        Quests.AwardParty = amount => Here?.AwardPartyExperience(amount, RankGained, near: Here?.Player?.Thing);
        Quests.Speak = slot =>
        {
            // The steered character says it, when it is free to (`CanSpeak` on the current control).
            var who = Here?.Minds?.Steered ?? Here?.Player?.Thing;
            if (Audio.Noise.CanSpeak(who)) Audio.Noise.Foley(who, slot);
        };
    }

    /// <summary>
    /// A new rank: <c>RPG_NEWRANKGAINED</c> in yellow with the character's name,
    /// and the interface's bell.
    /// </summary>
    public void RankGained(WorldThing who, int rank)
    {
        Messages.Post("RPG_NEWRANKGAINED", "%P" + NameOf(who), Ui.MessageLog.Yellow);
        Audio.Noise.Ui(who.Place ?? Here, Audio.FoleyEvent.NewRank);
        Ranked?.Invoke(who, rank);
    }

    /// <summary>A thing's name as the messages give it.</summary>
    public string NameOf(WorldThing who) => Ui.Rollover.NameOf(who, Messages.Text);

    /// <summary>
    /// Cash. A party member's goes into the purse, anybody else's onto the character; more
    /// than 10 at once is a one-in-five chance of <c>vox_gotcash</c>, when the character is free to
    /// speak.
    /// </summary>
    public void AddCash(WorldThing to, int amount)
    {
        if (to.Actor is { } actor)
        {
            // A network game's characters each keep their own (the party's purse is taken only
            // outside a network game).
            if (to.Type == ThingType.Player && actor.Has(Actor.Flag.Party) && Net.NetOrders.Mode(to.Place) == Net.NetMode.Single) Gold += amount;
            else actor.Cash += amount;
        }
        if (amount > 10 && to.Place?.Minds?.Random is { } rng && rng.Unit() < GotCashChance && Audio.Noise.CanSpeak(to))
            Audio.Noise.Foley(to, Audio.FoleyEvent.VoxGotCash);
    }

    /// <summary>The chance of <c>vox_gotcash</c> (0.2).</summary>
    public const float GotCashChance = 0.2f;
}
