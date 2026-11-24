// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The sequential parse of `!main.nsg`, in the order the original game writes it. The model types and the
// engine addresses each section comes from are in Nsg.cs.
namespace Vitomir.Formats;

sealed class NsgReader(BinReader r, NsgReadOptions options)
{
    const int Name = 0x20;

    internal void Tag(uint tag, string what)
    {
        int at = r.Pos;
        uint got = r.U32();
        if (got != tag)
            throw new NodFormatException($"{r.Name}: expected tag {tag} ({what}) at 0x{at:x}, found {got}");
    }

    /// <summary>The `u32 present; if (present) char[0x20]` pair the writer uses for every optional name.</summary>
    string? OptionalName() => r.U32() == 1 ? r.CStr(Name) : null;

    public NsgSave ReadFile()
    {
        string description = BinReader.CString(r.Bytes(0x78));
        string magic = BinReader.CString(r.Bytes(4));
        if (magic != "NSG") throw new NodFormatException($"{r.Name}: not a save (magic {magic:l})");
        uint version = r.U32();
        if (version != NsgSave.ExpectedVersion)
            throw new NodFormatException($"{r.Name}: save version {version}, expected {NsgSave.ExpectedVersion}");
        uint e4 = r.U32(), f108 = r.U32();
        string e8 = r.CStr(Name);

        var chronicle = ReadChronicle();

        Tag(NsgTag.Teleports, "teleports");
        int slots = (int)r.U32();
        if (slots != NsgSave.TeleportSlots)
            throw new NodFormatException($"{r.Name}: {slots} teleport slots, expected {NsgSave.TeleportSlots}");
        var teleports = new NsgTeleport[slots];
        for (int i = 0; i < slots; i++)
            teleports[i] = new NsgTeleport(r.U32(), r.U32(), r.U32(), r.U32(), r.U32(), r.U32(), r.U32());

        Tag(NsgTag.Clock, "clock");
        uint[] clock = r.U32s(NsgSave.ClockWords);
        Tag(NsgTag.ThingCounter, "thing counter");
        uint thingCounter = r.U32();
        Tag(NsgTag.CodexCounter, "codex counter");
        uint codexCounter = r.U32();
        Tag(NsgTag.TreasureCounter, "treasure counter");
        uint treasureCounter = r.U32();

        var shops = ReadShops();

        Tag(NsgTag.Effects, "effect counter");
        uint effectCounter = r.U32();

        // A network game's save ends here: the save's writer writes the party only outside a network game
        // (the purse functions' test), and the load reads it only outside a network
        // session. The saves MP0 and MP1 end on the effect counter.
        bool hasParty = r.Remaining > 0;
        var party = hasParty ? ReadParty() : new NsgParty();
        r.ExpectEof();

        return new NsgSave
        {
            Description = description, Magic = magic, Version = version,
            FieldE4 = e4, Field108 = f108, TextE8 = e8,
            Chronicle = chronicle, Teleports = teleports, Clock = clock,
            ThingCounter = thingCounter, CodexCounter = codexCounter,
            TreasureCounter = treasureCounter, EffectCounter = effectCounter,
            Shops = shops, Party = party, HasParty = hasParty,
        };
    }

    // ---- the chronicle: tags 2, 3 and 7 -------------------------

    NsgChronicle ReadChronicle()
    {
        Tag(NsgTag.Chronicle, "chronicle");
        string name = r.CStr(Name);
        uint f0 = r.U32(), f4 = r.U32();
        int count = r.Count32(4096);
        uint[] fields = r.U32s(5);
        string t1af88 = r.CStr(Name);

        var locations = new NsgLocationState[count];
        for (int i = 0; i < count; i++)
        {
            uint flags = r.U32();
            string locName = r.CStr(0x80);
            string level = r.CStr(Name), scene = r.CStr(Name), map = r.CStr(Name), marker = r.CStr(Name);
            uint f78 = r.U32();
            int exits = r.Count32(NscLocation.MaxExits);
            var list = new NsgExit[exits];
            for (int e = 0; e < exits; e++) list[e] = new NsgExit(r.U32(), r.U32(), r.U32());
            locations[i] = new NsgLocationState
            {
                Flags = flags, Name = locName, Level = level, Scene = scene,
                MapFile = map, MapMarker = marker, Field78 = f78, Exits = list,
            };
        }

        string mapFile = r.CStr(Name);
        Tag(NsgTag.ChronicleFlags, "chronicle flags");
        uint[] chronicleFlags = r.U32s(NsgSave.ChronicleFlagWords);

        var quests = ReadQuests();

        Tag(NsgTag.CodexList, "codex list");
        int codices = r.Count32(NsgSave.QuestSlots);
        var codexList = new (string, NsgCodexRecord)[codices];
        for (int i = 0; i < codices; i++) { string cls = r.CStr(Name); codexList[i] = (cls, ReadCodexRecord(cls)); }

        return new NsgChronicle
        {
            Name = name, Field0 = f0, Field4 = f4, Fields = fields, Text1Af88 = t1af88,
            Locations = locations, MapFile = mapFile, Flags = chronicleFlags,
            Quests = quests, Codices = codexList,
        };
    }

    // ---- quests: tags 4, 5 and 6 ------------------------
    // The log itself (tag 4); one quest and its items (tag 5).

    NsgQuest[] ReadQuests()
    {
        Tag(NsgTag.Quest, "quests");
        int count = r.Count32(NsgSave.QuestSlots);
        var quests = new NsgQuest[count];
        for (int i = 0; i < count; i++)
        {
            int index = (int)r.U32();
            Tag(NsgTag.QuestRecord, "quest");
            string name = r.CStr(Name), t38 = r.CStr(Name), t58 = r.CStr(Name);
            uint[] fields = r.U32s(3);
            string t78 = r.CStr(Name);
            int steps = r.Count32(4096);
            uint f30 = r.U32();
            var list = new NsgQuestStep[steps];
            for (int s = 0; s < steps; s++)
            {
                Tag(NsgTag.QuestStep, "quest step");
                list[s] = new NsgQuestStep
                {
                    Text10 = r.CStr(Name), Text30 = r.CStr(Name),
                    Field0 = r.U32(), Field58 = r.U32(), Text5C = r.CStr(Name),
                    Field4 = r.U32(), Field8 = r.U32(),
                };
            }
            quests[i] = new NsgQuest
            {
                Index = index, Name = name, Text38 = t38, Text58 = t58,
                Fields = fields, Text78 = t78, Field30 = f30, Steps = list,
            };
        }
        return quests;
    }

    // ---- codex records: tags 0x1c and 0x1d ------------------------------

    NsgCodexRecord ReadCodexRecord(string? className = null)
    {
        Tag(NsgTag.CodexRecord, "codex record");
        uint kind = r.U32();
        int count = r.Count32(4096);
        var fields = new NsgCodexField[count];
        for (int i = 0; i < count; i++)
        {
            uint type = r.U32();
            fields[i] = type == NsdParam.String
                ? new NsgCodexField(type, BinReader.CString(r.Bytes(r.Count32(1 << 20))), null)
                : new NsgCodexField(type, null, r.Bytes(0xc));
        }
        Tag(NsgTag.CodexEnd, "codex state");

        // The class's own save() output: handed to the caller when it asked for it, else stepped over
        // by the shipped classes' layouts (NsgScriptLayouts) — a class with no save writes nothing,
        // which is the common case: none of the chronicles declares one.
        int start = r.Pos;
        if (options.ReadScriptState is { } read) read(r);
        else NsgScriptLayouts.TrySkip(className, r);
        byte[] state = r.Pos == start ? [] : r.Data[start..r.Pos];
        int at = r.Pos;
        uint tail = r.U32();
        if (tail != NsgTag.CodexEnd)
            throw new NodFormatException(
                $"{r.Name}: the codex object {className ?? "(unnamed)"} at 0x{start:x} saved script state this " +
                "reader cannot skip (its class is not in NsgScriptLayouts; set NsgReadOptions.ReadScriptState to consume it)");

        return new NsgCodexRecord { Kind = kind, Fields = fields, ScriptState = state };
    }

    NsgScript? OptionalScript()
    {
        if (r.U32() != 1) return null;
        string cls = r.CStr(Name);
        return new NsgScript { Class = cls, Guid = r.U32(), Record = ReadCodexRecord(cls) };
    }

    // ---- shops: tag 15 --------------------------------------------

    NsgShop[] ReadShops()
    {
        Tag(NsgTag.Shops, "shops");
        int count = r.Count32(4096);
        var shops = new NsgShop[count];
        for (int i = 0; i < count; i++)
        {
            uint owner = r.U32();
            string name = r.CStr(Name);
            uint flags = r.U32(), buy = r.U32(), sell = r.U32(), sellFlags = r.U32();
            int lines = r.Count32(65536);
            var stock = new NsgStock[lines];
            for (int s = 0; s < lines; s++)
            {
                uint remaining = r.U32();
                int copies = r.I32();
                uint kind = r.U32();
                string? template = kind == 1 ? r.CStr(Name) : null;
                uint treasureClass = kind == 1 ? 0 : r.U32();
                NsgTreasure? treasure = r.U32() != 0 ? ReadTreasure() : null;
                stock[s] = new NsgStock
                {
                    Remaining = remaining, Copies = copies, Kind = kind,
                    Template = template, TreasureClass = treasureClass, Treasure = treasure,
                };
            }
            shops[i] = new NsgShop
            {
                OwnerGuid = owner, Name = name, Flags = flags,
                BuyRate = buy, SellRate = sell, SellFlags = sellFlags, Stock = stock,
            };
        }
        return shops;
    }

    // ---- treasure: tag 16 (the record itself) -----------

    NsgTreasure ReadTreasure()
    {
        Tag(NsgTag.Treasure, "treasure");
        uint slot = r.U32();
        uint type = r.U32();
        uint amount = r.U32(), flags = r.U32(), itemType = r.U32();
        uint baseCost = r.U32(), sellFlags = r.U32();
        uint damage = r.U32(), damageType = r.U32(), strikeTime = r.U32();
        uint f90 = r.U32(), f94 = r.U32(), f98 = r.U32();
        string descriptionId = r.CStr(Name), identifiedId = r.CStr(Name);
        string effectId = r.CStr(Name), template = r.CStr(Name);
        string? icon = null;
        uint iconField = 0;
        if (r.U32() == 1) { icon = r.CStr(Name); iconField = r.U32(); }
        var restrictions = ReadRestrictions();
        var effects = ReadItemEffects();
        return new NsgTreasure
        {
            Slot = slot, Type = (NsgThingType)type, Amount = amount, Flags = flags, ItemType = itemType,
            BaseCost = baseCost, SellFlags = sellFlags,
            WeaponDamage = damage, WeaponDamageType = damageType, WeaponStrikeTime = strikeTime,
            Field90 = f90, Field94 = f94, Field98 = f98,
            DescriptionId = descriptionId, IdentifiedId = identifiedId, EffectId = effectId,
            Template = template, Icon = icon, IconField = iconField,
            Restrictions = restrictions, Effects = effects,
        };
    }

    NsgRestriction[] ReadRestrictions()
    {
        int n = r.Count32(65536);
        var list = new NsgRestriction[n];
        for (int i = 0; i < n; i++) list[i] = new NsgRestriction(r.U32(), r.U32());
        return list;
    }

    NsgItemEffect[] ReadItemEffects()
    {
        int n = r.Count32(65536);
        var effects = new NsgItemEffect[n];
        for (int i = 0; i < n; i++)
            effects[i] = new NsgItemEffect(r.CStr(Name), r.I32(), r.U32(), r.U32(), r.U32());
        return effects;
    }

    // ---- the party: tag 12 -------------------------------------------

    NsgParty ReadParty()
    {
        Tag(NsgTag.Party, "party");
        uint gold = r.U32();
        uint[] junk = r.U32s(4);
        uint[] slots = r.U32s(4);
        int players = r.Count32(4);
        uint current = r.U32();
        var list = new NsgThing[players];
        for (int i = 0; i < players; i++) list[i] = ReadThing();
        return new NsgParty
        {
            Gold = gold, Junk = junk, Slots = slots,
            CurrentPlayerGuid = current, Players = list,
        };
    }

    // ---- things: the thing record and the per-type records -----------------------------

    internal NsgThing ReadThing()
    {
        int at = r.Pos;
        uint type = r.U32();
        if (type > (uint)NsgThingType.Explosion)
            throw new NodFormatException($"{r.Name}: thing type {type} at 0x{at:x}");
        Tag(NsgTag.Thing, "thing");

        uint guid = r.U32();
        string name = r.CStr(Name), descriptionId = r.CStr(Name);
        int world = r.I32(), scene = r.I32(), sector = r.I32();
        byte[] transform = r.Bytes(0x30), transform2 = r.Bytes(0x30);
        uint f90 = r.U32(), flags = r.U32(), f128 = r.U32();
        byte[] ffc = r.Bytes(0x28);

        uint shape = r.U32();
        byte[] shapeData = shape switch
        {
            0 or 1 => [],
            2 => r.Bytes(0x90),
            3 => r.Bytes(0x48),
            _ => throw new NodFormatException($"{r.Name}: thing shape {shape} at 0x{r.Pos - 4:x}"),
        };

        int records = r.Count32(65536);
        var blocks = new byte[records][];
        for (int i = 0; i < records; i++) blocks[i] = r.Bytes(0x18);

        string? template = OptionalName();
        uint f28 = r.U32();
        var model = ReadModel();
        Tag(NsgTag.Thing21, "thing 0x21");
        Tag(NsgTag.Thing22, "thing 0x22");
        var links = ReadLinks();
        Tag(NsgTag.ThingEnd, "thing end");
        var script = OptionalScript();

        NsgActor? actor = null;
        NsgPlayer? player = null;
        NsgItem? item = null;
        uint[] typeFields = [];
        uint[] ammo = [];
        byte ammoTrailer = 0;

        switch ((NsgThingType)type)
        {
            case NsgThingType.Actor:
                actor = ReadActor();
                break;
            case NsgThingType.Player:
                actor = ReadActor();
                player = ReadPlayer();
                break;
            case NsgThingType.Item:
                item = ReadItem();
                break;
            case NsgThingType.Weapon:
                item = ReadItem();
                Tag(NsgTag.Ammo, "weapon");
                int rounds = r.U8();
                ammo = r.U32s(rounds);
                ammoTrailer = r.U8();
                break;
            case NsgThingType.Armour:
                item = ReadItem();
                Tag(NsgTag.Armour, "armour");
                typeFields = r.U32s(2);
                break;
            case NsgThingType.Prop:
                Tag(NsgTag.Prop, "prop");
                typeFields = r.U32s(2);
                break;
            case NsgThingType.Region:
                Tag(NsgTag.Region, "region");
                typeFields = r.U32s(1);
                break;
            case NsgThingType.Container:
                Tag(NsgTag.Container, "container");
                typeFields = r.U32s(3);
                break;
            default:
                // PROJECTILE, NONE and EXPLOSION override the save with a no-op, so nothing of
                // theirs ever reaches a file — reaching one here means the walk has gone wrong.
                throw new NodFormatException(
                    $"{r.Name}: a {(NsgThingType)type} record at 0x{at:x}, which the original never writes");
        }

        return new NsgThing
        {
            Type = (NsgThingType)type, Guid = guid, Name = name, DescriptionId = descriptionId,
            World = world, Scene = scene, Sector = sector,
            Transform = transform, Transform2 = transform2,
            Field90 = f90, Flags = flags, Field128 = f128, FieldFC = ffc,
            Shape = shape, ShapeData = shapeData, Records = blocks,
            Template = template, Field28 = f28, Model = model, Links = links, Script = script,
            Actor = actor, Player = player, Item = item,
            TypeFields = typeFields, Ammo = ammo, AmmoTrailer = ammoTrailer,
            Start = at, End = r.Pos,
        };
    }

    NsgModel ReadModel()
    {
        Tag(NsgTag.Model, "model");
        uint mode = r.U32();
        switch (mode)
        {
            case 0:
                return new NsgModel { Mode = 0 };
            case 1:
                return new NsgModel
                {
                    Mode = 1, Flags = r.U32(), MotionSet = r.CStr(Name),
                    MotionFields = r.U32s(2), Field14C = r.U32(),
                };
            case 2:
                uint flags = r.U32();
                string? motionSet = null;
                uint[] motionFields = [];
                if (r.U32() == 1) { motionSet = r.CStr(Name); motionFields = r.U32s(2); }
                string model = r.CStr(Name);
                string? animation = null;
                uint animationFlags = 0;
                if (r.U32() == 1) { animation = r.CStr(Name); animationFlags = r.U32(); }
                return new NsgModel
                {
                    Mode = 2, Flags = flags, MotionSet = motionSet, MotionFields = motionFields,
                    Model = model, Animation = animation, AnimationFlags = animationFlags,
                };
            default:
                throw new NodFormatException($"{r.Name}: model mode {mode} at 0x{r.Pos - 4:x}");
        }
    }

    NsgLinks ReadLinks()
    {
        Tag(NsgTag.Links, "links");
        uint parent = r.U32();
        int count = r.Count32(65536);
        var attachments = new NsgAttachment[count];
        for (int i = 0; i < count; i++)
            attachments[i] = new NsgAttachment(r.U32(), r.U32(), r.U32(), r.Bytes(0x30));
        return new NsgLinks { ParentGuid = parent, Attachments = attachments };
    }

    NsgActor ReadActor()
    {
        Tag(NsgTag.Actor, "actor");
        uint f1f4 = r.U32(), flags = r.U32();
        byte actorType = r.U8(), f1fe = r.U8();
        uint f22c = r.U32(), f230 = r.U32();
        string castId = BinReader.CString(r.Bytes(r.Count32(1 << 16)));
        string? t208 = OptionalName();
        byte[] extra = (flags & 0x800) != 0 ? r.Bytes(0x50) : [];
        byte f1ff = r.U8(), f200 = r.U8();
        float health = r.F32(), maxHealth = r.F32();
        float walk = r.F32(), run = r.F32(), move = r.F32(), attack = r.F32();
        uint f224 = r.U32();
        float[] baseStats = r.Floats(NsgActor.StatCount), baseSoaks = r.Floats(NsgActor.SoakCount);
        float[] stats = r.Floats(NsgActor.StatCount), soaks = r.Floats(NsgActor.SoakCount);
        uint fadeAt = r.U32();

        Tag(NsgTag.ActorBody, "actor body");
        var effects = ReadEffects();
        var inventory = ReadInventory();
        var (disciplines, selected) = ReadDisciplines();
        Tag(NsgTag.Inventory, "actor end");

        string? mind = null;
        uint mindField = 0;
        if (r.U32() == 1) { mind = r.CStr(Name); mindField = r.U32(); }

        return new NsgActor
        {
            Field1F4 = f1f4, Flags = flags, ActorType = actorType, Field1FE = f1fe,
            Field22C = f22c, Field230 = f230, CastId = castId, Text208 = t208, Extra = extra,
            Field1FF = f1ff, Field200 = f200,
            Health = health, MaxHealth = maxHealth, WalkSpeed = walk, RunSpeed = run,
            MoveSpeed = move, AttackSpeed = attack, Field224 = f224,
            BaseStats = baseStats, BaseSoaks = baseSoaks, Stats = stats, Soaks = soaks,
            FadeAt = fadeAt, Effects = effects, Inventory = inventory,
            Disciplines = disciplines, SelectedDiscipline = selected,
            Mind = mind, MindField = mindField,
        };
    }

    NsgEffect[] ReadEffects()
    {
        Tag(NsgTag.ActorEffects, "actor effects");
        int count = r.Count32(65536);
        var effects = new NsgEffect[count];
        for (int i = 0; i < count; i++)
            effects[i] = new NsgEffect
            {
                Name = r.CStr(Name),
                Level = r.I32(), NextAt = r.U32(), Field10 = r.U32(), Id = r.U32(),
                SourceGuid = r.U32(), Flags = r.U32(), ValueLevel = r.I32(), Value = r.U32(),
                Field28 = r.U32(), Field2C = r.U32(),
                Script = OptionalScript(),
            };
        return effects;
    }

    NsgInventory ReadInventory()
    {
        Tag(NsgTag.Inventory, "inventory");
        int count = r.Count32(65536);
        var items = new NsgThing[count];
        for (int i = 0; i < count; i++) items[i] = ReadThing();
        var held = r.U32() != 0 ? ReadThing() : null;
        var unarmed = r.U32() != 0 ? ReadThing() : null;
        var held36c = r.U32() != 0 ? ReadThing() : null;
        return new NsgInventory
        {
            Items = items, Held360 = held, UnarmedWeapon = unarmed, Held36C = held36c,
            Guids = r.U32s(6),
        };
    }

    (NsgDiscipline[], string?) ReadDisciplines()
    {
        Tag(NsgTag.Disciplines, "disciplines");
        int count = r.Count32(65536);
        var list = new NsgDiscipline[count];
        for (int i = 0; i < count; i++)
            list[i] = new NsgDiscipline
            {
                Name = r.CStr(Name), Field8 = r.U32(), Level = r.I32(),
                Field18 = r.U32(), FieldC = r.U32(), ScriptValue = r.U32(),
            };
        string selected = r.CStr(Name);
        return (list, selected == NoDiscipline ? null : selected);
    }

    /// <summary>What the writer puts in the selected-discipline slot when nothing is selected.</summary>
    public const string NoDiscipline = "...";

    NsgItem ReadItem()
    {
        Tag(NsgTag.Item, "item");
        return new NsgItem
        {
            Flags = r.U32(), Amount = r.U32(), BaseCost = r.U32(),
            SellFlags = r.U32(), Field21C = r.U32(), Location = r.U32(),
            Fields2BC = r.U32s(2),
            Restrictions = ReadRestrictions(),
            Effects = ReadItemEffects(),
        };
    }

    NsgPlayer ReadPlayer()
    {
        Tag(NsgTag.Player, "player");
        return new NsgPlayer
        {
            Text3C4 = r.CStr(Name), Field3E8 = r.U32(), Field3EC = r.U32(),
            Text3F0 = OptionalName(), Text494 = r.CStr(Name),
            Field4B4 = r.U8(), Field4B5 = r.U8(),
        };
    }
}
