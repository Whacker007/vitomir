// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What each machine sees of the others, a player's machine's side: the host's kit, effects, flags,
// blows, sounds and hitscan shots (`Net.HostSeen`) put on this machine's copies of its things — as
// pictures. Nothing here decides anything: a blow is shown and not dealt, an effect is worn for its
// look and its icon while the host's sheet keeps the numbers, a shot is fired and hurts nobody.
using System.Runtime.CompilerServices;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Net;

/// <summary>A player's machine's side of what it sees of the others.</summary>
public sealed partial class ClientSeen
{
    static readonly ConditionalWeakTable<Location, ClientSeen> places = new();

    readonly Action<byte[]> send;
    readonly Func<int> avatarGuid;
    readonly Dictionary<int, (WorldThing On, Effect Effect)> effects = [];
    readonly Rng dice = Chance.Game?.Combat ?? new(1);   // a game's: the launch's fight stream
    WorldMirror? mirror;

    public ClientSeen(Action<byte[]> send, Func<int> avatarGuid)
    {
        this.send = send;
        this.avatarGuid = avatarGuid;
    }

    /// <summary>The console's account of the kits and effects taken from the host.</summary>
    public Action<string>? Log { get; set; }

    /// <summary>The effects table (null: the place's own, when it was awakened with one).</summary>
    public EffectDefinitions? Effects { get; set; }

    /// <summary>A blow shown here, with this machine's victim and attacker (either may be unknown here).</summary>
    public event Action<SeenBlow, WorldThing?, WorldThing?>? Struck;
    /// <summary>A foley played here for the host, on this machine's thing, with its event.</summary>
    public event Action<WorldThing, int>? Heard;
    /// <summary>A named sound played here for the host, on this machine's thing.</summary>
    public event Action<WorldThing, string>? Sounded;
    /// <summary>A hitscan shot fired here for the host: this machine's shot, made and already resolved.</summary>
    public event Action<WorldThing>? Shot;

    /// <summary>The place's world is up here: the host's things are found through <paramref name="mirror"/>.</summary>
    public void Enter(WorldMirror mirror)
    {
        this.mirror = mirror;
        effects.Clear();
        places.AddOrUpdate(mirror.Place, this);
    }

    /// <summary>One of the messages <see cref="Msg.Kit"/> … <see cref="Msg.Shot"/>.</summary>
    public void Receive(NetReader r)
    {
        if (mirror is null) return;
        switch (r.Kind)
        {
            case Msg.Kit: Kit(r); break;
            case Msg.EffectOn: EffectOn(SeenEffect.Read(r)); break;
            case Msg.EffectOff: EffectOff(r.I32(), r.I32()); break;
            case Msg.Blow: Blow(SeenBlow.Read(r)); break;
            case Msg.Foley: Foley(r.I32(), r.I32(), r.I32()); break;
            case Msg.Sound: Sound(r); break;
            case Msg.Flags: Flags(r); break;
            case Msg.Shot: Fire(ThingFired.Read(r)); break;
            case Msg.Attached: Attached(r); break;
            case Msg.Named: Named(r); break;
            case Msg.Gibbed: Gibbed(r.I32()); break;                   // ClientSeen.Looks.cs
            case Msg.Beheaded: Beheaded(r.I32(), r.I32()); break;
            case Msg.Revived: Revived(r.I32(), r.F32(), r.F32()); break;      // ClientSeen.Looks.cs
        }
    }

    Location Place => mirror!.Place;
    bool Mine(WorldThing t) => avatarGuid() is var g && g != 0 && ReferenceEquals(mirror!.Find(g), t);

    // ---- the kit (0x13 … 0x15, 0x35) ----------------------------------------------------------------

    /// <summary>
    /// What a character wears, as the host has it (the item made in its pack if this
    /// machine has not got it, and worn; at the join, the four worn slots at once, 1.1's 0xe). Each item is found by the host's GUID or made from its template,
    /// put in its slot, and the character takes up what it fights with, its shield and its armour
    /// (the picture half of the equip, `Equipment.Restore`) — **not** the
    /// item's effects, which come as effects, nor its script (the equip tells neither on a player's
    /// machine). Somebody else's character wears exactly the list; this player's own keeps
    /// its pack, which the host's sheet sends. The utility item held in the off hand — a torch — goes through its
    /// setter here too (<see cref="SeenUtility"/>).
    /// </summary>
    void Kit(NetReader r)
    {
        int guid = r.I32();
        var listed = new List<WornItem>();
        for (int n = r.U8(); n > 0; n--) listed.Add(new WornItem(r.I32(), r.Str(), r.I32()));
        if (mirror!.Find(guid) is not { Actor: { } a } body) return;
        bool mine = Mine(body);
        var worn = new List<Item>();
        foreach (var w in listed)
        {
            var t = mirror.Find(w.Guid);
            if (t is null)
            {
                t = Place.Restore(w.Template, w.Guid, null);
                if (t is null) continue;
                mirror.Bind(w.Guid, t);
            }
            if (t.Item is not { } item) continue;
            if (t.InWorld && item.Type != ItemType.Utility) Place.Withdraw(t);
            item.Location = w.Slot;
            item.Flags |= ItemFlags.Identified | (ItemFlags)Equipment.EquippedBit;
            t.Owner = body;
            if (!a.Inventory.Items.Contains(item)) a.Inventory.Add(item);
            worn.Add(item);
        }
        foreach (var off in a.Inventory.Items.Where(i => (i.Location & Slot.Worn) != 0 && !worn.Contains(i)).ToList())
        {
            off.Flags &= ~(ItemFlags)Equipment.EquippedBit;
            if (mine) off.Location = Slot.Inventory;          // the host's next sheet says where it went
            else a.Inventory.Remove(off);
        }
        Dress(body, a, worn);
        Log?.Invoke($"net: thing {guid} wears {(worn.Count == 0 ? "nothing" : string.Join(", ", worn.Select(i => i.Thing?.Template)))}");
    }

    /// <summary>
    /// The setters an equip reaches, for what is worn now (the equip's switch on the item's type): the
    /// weapon in hand, or the built-in one with none, and the locomotion chosen again;
    /// the shield on the off arm; the armour and its body model. A weapon swap
    /// in force keeps the hand as it is.
    /// </summary>
    static void Dress(WorldThing body, Actor a, List<Item> worn)
    {
        var weapon = worn.FirstOrDefault(i => i.Type == ItemType.Weapon && i.Weapon is not null)?.Weapon ?? a.DefaultWeapon;
        if (!WeaponOverride.Active(a))
        {
            if (!ReferenceEquals(a.Weapon, weapon)) { a.Weapon = weapon; body.Locomotion?.Reanimate(); }
            a.Offhand = worn.FirstOrDefault(i => i.Type == ItemType.Shield);
        }
        var armour = worn.FirstOrDefault(i => i.Type == ItemType.Armor);
        if (!ReferenceEquals(a.Armor, armour))
        {
            a.Armor = armour;
            BodyModels.Set(body, armour?.ArmorModel);
        }
        SeenUtility.Hold(body, a, worn);
    }

    // ---- effects (0x19, 0x1a) ----------------------------------------------------------------------

    /// <summary>
    /// An effect put on a character (marked as from the network): the same row at
    /// the same level or value, for the time it has left, so its look — alpha, scale, the limp — and, on
    /// this player's own character, its icon are here. It is known by the host's instance GUID. Whatever
    /// it does to the numbers the host's sheet overwrites; a blow it deals is refused here.
    /// </summary>
    void EffectOn(SeenEffect e)
    {
        if (effects.ContainsKey(e.Guid)) return;
        if (mirror!.Find(e.Actor) is not { Actor: not null } body) return;
        // This player's own pane puts an item on here too (`HostPack`: the move is made on both), but not its worn
        // effects: a player's machine puts on only what the host sends, so they come here.
        if ((Effects ?? Place.Effects)?[e.Name] is not { } definition) return;
        var creator = e.Creator != 0 ? mirror.Find(e.Creator) : null;
        var made = EffectRuntime.Apply(body, definition, Math.Max(0, e.Level), Place.Milliseconds, e.RemainingMs,
                                       creator, value: e.Level < 0 ? e.Value : null, flags: e.Flags,
                                       intParam: e.IntParam, floatParam: e.FloatParam, creatorGuid: creator?.Guid ?? 0,
                                       network: true);                     // as from the network
        if (made is not null) effects[e.Guid] = (body, made);
        Log?.Invoke($"net: thing {e.Actor} has {e.Name} (#{e.Guid}){(made is null ? " (not put on here)" : "")}");
    }

    /// <summary>An effect's end: taken off here, with no script to tell.</summary>
    void EffectOff(int actor, int guid)
    {
        if (!effects.Remove(guid, out var had)) return;
        if (!had.Effect.Spent) EffectRuntime.Remove(had.On, had.Effect, notify: false, network: true);
    }

    /// <summary>
    /// The right click on a cancellable effect icon on a player's machine: the
    /// request goes to the host with the host's GUID for the effect, and the host ends it;
    /// nothing is taken off here until it says so. Answers whether the place is a player's machine that
    /// sent it — then the icon's own removal is not made.
    /// </summary>
    public static bool Cancel(WorldThing steered, Effect effect)
    {
        if (steered.Place is not { } place || NetOrders.Mode(place) != NetMode.Client) return false;
        if (!places.TryGetValue(place, out var seen)) return false;
        foreach (var (guid, had) in seen.effects)
            if (ReferenceEquals(had.Effect, effect))
            {
                seen.send(new NetWriter(Msg.EffectCancel).I32(guid).ToArray());
                return true;
            }
        return true;                       // not the host's: nothing to ask, and nothing to take off here
    }

    // ---- blows (0xd) -------------------------------------------------------------------------------

    /// <summary>
    /// A blow the host dealt, shown (the blow shown on each machine): for a death, what the death
    /// does besides the dying — whatever the character was saying cut off, `vox_death_&lt;type&gt;` and
    /// `death_&lt;type&gt;`, the interface's bell and the message line in red for a party member ("…
    /// dies", "… enters torpor" for a vampire); for a kill that cost this player's own character
    /// Humanity, "%P lost some Humanity!". The flinch and the fall come with the host's
    /// animations, the health and the death with the snapshot; the hurt noises, the chunks, the sparks
    /// and the blood this machine makes itself (<see cref="Looks"/>).
    /// </summary>
    void Blow(SeenBlow b)
    {
        var victim = mirror!.Find(b.Victim);
        var attacker = b.Attacker != 0 ? mirror.Find(b.Attacker) : null;
        Struck?.Invoke(b, victim, attacker);
        if (victim is { Actor: { } dead } && b.Killed)
        {
            if (Place.Audio is { } audio)
            {
                if ((uint)audio.Mixer.GameMs < (uint)victim.MouthBusyUntil && victim.MouthChannel > 0) audio.Mixer.Stop(victim.MouthChannel);
                audio.Thing(victim, Audio.FoleyEvent.VoxDeath + (int)b.Type);
                audio.Thing(victim, Audio.FoleyEvent.Death + (int)b.Type);
                if (victim.Type == ThingType.Player) audio.Ui(Audio.FoleyEvent.StMessage1);
            }
            if (victim.Type == ThingType.Player && Place.Session is { } session)
                session.Messages.Post(dead.Type == ActorType.Vampire ? "RPG_DIEDV" : "RPG_DIED",
                                      "%P" + session.NameOf(victim), Ui.MessageLog.Red);
        }
        if (b.HumanityLost && attacker is not null && ReferenceEquals(attacker, Place.Steered) && Place.Session is { } mine)
            mine.Messages.Post("RPG_HUMANITYLOST", "%P" + mine.NameOf(attacker));
        Looks(b, victim, attacker);                                   // what the blow or the death makes here
    }

    // ---- sounds (0x4b, 0x4c) -----------------------------------------------------------------------

    /// <summary>A foley the host played: this machine's thing makes the same noise from its own set.</summary>
    void Foley(int guid, int slot, int at)
    {
        if (mirror!.Find(guid) is not { } thing) return;
        Heard?.Invoke(thing, slot);
        Audio.Noise.Foley(thing, slot, at != 0 ? mirror.Find(at) : null);
    }

    /// <summary>A named sound on a thing: loaded here by its name and played on this machine's thing.</summary>
    void Sound(NetReader r)
    {
        int guid = r.I32();
        string name = r.Str();
        float min = r.F32(), max = r.F32(), volume = r.F32();
        uint flags = r.U32();
        if (mirror!.Find(guid) is not { InWorld: true } thing) return;
        Sounded?.Invoke(thing, name);
        if (Place.Audio is { } audio && audio.Bank.Load(name, stream: (flags & Audio.ChannelFlag.AsStream) != 0) is { } sound
            && audio.Mixer.PlayAtThing(sound, thing, max, min, volume, flags) == 0)
            audio.Bank.Release(sound);
    }

    // ---- flags (message 4) -------------------------------------------------------------------------

    /// <summary>
    /// Things' flags as the host has them: the render flags — hidden or shown — and the thing
    /// flags, each keeping this machine's own bits (<see cref="ThingFlags.LocalRender"/>,
    /// <see cref="ThingFlags.LocalThing"/>), and the collide type; for a character, its actor flags — talk-to,
    /// neutral, dead … — its second flags but a conversation's, its aura, its teams, its blood, mana and frenzy
    /// (clamped as from the network: no mark) and its money; for an item its count and item
    /// flags, for a weapon its two loads and item flags, for a container its state. This player's own character keeps
    /// its actor flags, stats and money, which its sheet and purse carry.
    /// </summary>
    void Flags(NetReader r)
    {
        for (int n = r.U16(); n > 0; n--)
        {
            var f = ThingFlags.Read(r);
            if (mirror!.Find(f.Guid) is not { } t) continue;
            t.RenderFlags = (f.Render & ~ThingFlags.LocalRender) | (t.RenderFlags & ThingFlags.LocalRender);
            t.Flags = (f.Thing & ~ThingFlags.LocalThing) | (t.Flags & ThingFlags.LocalThing);
            t.Collide = (CollideShape)f.Collide;
            SeenNumbers.Apply(t, f, Mine(t));
        }
    }

    // ---- hitscan shots (0x50) ----------------------------------------------------------------------

    /// <summary>
    /// A hitscan shot the host fired: fired here the same way from this machine's
    /// copy of the shooter, so its flash, tracer, impact and noises are this machine's own; it hurts
    /// nobody here (`Damage.Apply`). **Ours**: this machine's own dice aim its spread.
    /// </summary>
    void Fire(ThingFired f)
    {
        var owner = f.Owner != 0 ? mirror!.Find(f.Owner) : null;
        if (owner is not { InWorld: true }) return;
        if (Projectiles.Fire(Place, owner, f.Template, f.Position, f.Orientation, owner.Guid, dice, Place.Milliseconds, Place.Motions) is { } shot)
            Shot?.Invoke(shot);
    }
}
