// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json;
using Vitomir.Modules.Enhanced.Rules;
using Vitomir.Modules.Enhanced.Sheets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Modules.Enhanced.Tests;

/// <summary>
/// Armour over the parts it covers (the rules package: <i>"actual locally covering armor"</i>, and
/// <i>"an armor item in inventory cannot protect an uncovered arm"</i>). Each piece of
/// <c>combat/armor</c> covers named regions and gives soak dice there by kind of harm — 1.1's soak points over 20 — and a blow on
/// a covered part adds them to the soak as their own pool; a part no piece covers gets none. Pieces worn over one another add;
/// one piece a slot. What a person wears is its sheet's condition <c>worn_armor</c> (saved, digested, given with the sheet);
/// 1.1's guards and watch are dressed by their template. <c>armor-off</c> gives the harm of before armour, outcome for outcome.
/// The world tests roll every die a 10 (<see cref="MaxDice"/>) so a blow's harm is exact: Strength 200 with a broadsword is
/// 12 damage dice, 12 successes, and the trained guard (a human) has no soak of his own against lethal harm.
/// </summary>
[Collection(AllocationCollection.Name)]   // it measures allocations (Allocations.cs)
public sealed class ArmorTests
{
    sealed class Rig
    {
        public required World W;
        public required ActorSheets Sheets;
        public required IPersonSheets Route;
        public required EntityId Attacker, Target;
        public required List<(long Tick, ContactMade Contact)> Contacts;
        public required StringWriter Trace;
        public readonly List<HarmResolved> Harm = [];
        long next = 1;

        public void Hit(string site, int swing)
        {
            Contacts.Add((W.Tick, new ContactMade(next++, Attacker, swing, Target, ContactMet.Body, site, ContactMaterial.Cloth, 0,
                new Vector3(1, 2, 1.3f), new Vector3(-1, 0, 0), 9.5f, 13f, false, false, true, false)));
            W.Step();
            Harm.AddRange(W.Events<HarmResolved>().ThisTick);
        }

        public List<JsonElement> Lines() => [.. Trace.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement)
            .Where(l => l.GetProperty("schema").GetString() == HarmTrace.Schema)];
    }

    /// <summary>Every d10 a 10 and every unit draw 0 (a fractional pool rounds up): a blow's harm exact.</summary>
    sealed class MaxDice : IDeterministicRandom
    {
        readonly Dictionary<string, long> drawn = [];
        public int NextD10(string purpose) { Count(purpose); return 10; }
        public double NextUnitInterval(string purpose) { Count(purpose); return 0; }
        public long Position(string purpose) => drawn.TryGetValue(purpose, out long n) ? n : 0;
        void Count(string purpose) => drawn[purpose] = Position(purpose) + 1;
    }

    /// <summary>
    /// The combat rules' newly Embraced at Strength <paramref name="strength"/> against their trained guard (Health 1000 bought: nothing is
    /// cut) wearing <paramref name="wears"/>; every die a 10 unless <paramref name="maxDice"/> is false.
    /// </summary>
    static Rig Make(string[] wears, string weapon = "BroadSword", int strength = 200, bool maxDice = true, ulong seed = 5, params string[] switches)
    {
        var m = TestData.Sandbox(null, switches);
        var b = new WorldBuilder(seed);
        if (maxDice) b.Service<IDeterministicRandom>(new MaxDice());
        var trace = new StringWriter();
        b.Service(new CombatTrace(trace));
        DeathServices.Register(b);
        b.Event<ContactMade>();
        var contacts = new List<(long, ContactMade)>();
        b.System(Phase.Contact, 0, "test.contacts", new DelegateSystem(w =>
        {
            foreach (var (tick, c) in contacts)
                if (tick == w.Tick) w.Events<ContactMade>().Publish(c with { Tick = w.Tick });
        }));
        m.Register(b);
        var world = b.Build();
        world.Service<HarmWeapons>().Of = (_, _) => weapon;
        var people = world.Service<PeopleHooks>();
        var (a, t) = (world.Create(), world.Create());
        people.Made(a, PersonRole.Player, "Christof");
        people.Made(t, PersonRole.Townsperson, "rudolfGuard");
        var route = world.Service<IPersonSheets>();
        Assert.True(route.TryTemplate(a, "sandbox.newly_embraced", out string error), error);
        Assert.True(route.TryTemplate(t, "sandbox.guard_trained", out error), error);
        Assert.True(route.TrySet(t, "health_pool", 1000, out error), error);
        Assert.True(route.TrySet(a, "strength", strength, out error), error);
        Assert.True(route.TryWear(t, wears, out error), error);
        return new Rig { W = world, Sheets = world.Service<ActorSheets>(), Route = route, Attacker = a, Target = t, Contacts = contacts, Trace = trace };
    }

    static ArmorData Data => TestData.Sandbox().Armor!;

    static uint Bits(params BodyRegion[] rs) { uint b = 0; foreach (var r in rs) b |= 1u << (int)r; return b; }

    static readonly BodyRegion[] Trunk = [BodyRegion.Chest, BodyRegion.Belly, BodyRegion.Hips];
    static readonly BodyRegion[] UpperArms = [BodyRegion.LeftUpperArm, BodyRegion.RightUpperArm];
    static readonly BodyRegion[] Forearms = [BodyRegion.LeftForearm, BodyRegion.RightForearm];
    static readonly BodyRegion[] Hands = [BodyRegion.LeftHand, BodyRegion.RightHand];
    static readonly BodyRegion[] Legs = [BodyRegion.LeftThigh, BodyRegion.RightThigh, BodyRegion.LeftShin, BodyRegion.RightShin, BodyRegion.LeftFoot, BodyRegion.RightFoot];

    // ---- the data ----

    [Fact]
    public void Each_piece_covers_its_parts_and_no_others_with_1_1s_points_over_twenty()
    {
        var d = Data;
        Assert.Equal(("SANDBOX", 20.0, ArmorLayering.Sum, "worn_armor"), (d.Status, d.PointsPerDie, d.Layering, d.Condition));
        var want = new (string Id, string Slot, BodyRegion[] Covers, double B, double L, double A, string OneOne)[]
        {
            ("padded_coat", "under", [.. Trunk, .. UpperArms, .. Forearms], 10, 10, 10, "paddedClothing"),
            ("leather_coat", "body", [.. Trunk, .. UpperArms], 10, 15, 10, "leatherClothing"),
            ("studded_leather", "body", [.. Trunk, .. UpperArms], 15, 30, 15, "studdedLeather"),
            ("mail_shirt", "body", [.. Trunk, .. UpperArms], 15, 45, 20, "chainMail"),
            ("mail_hauberk", "body", [.. Trunk, .. UpperArms, .. Forearms, BodyRegion.LeftThigh, BodyRegion.RightThigh], 15, 45, 20, "chainMail"),
            ("scale_shirt", "body", [.. Trunk, .. UpperArms], 30, 45, 20, "scaleMail"),
            ("half_plate", "body", [.. Trunk, .. UpperArms, .. Forearms], 30, 60, 25, "halfPlate"),
            ("plate_harness", "body", [.. Trunk, .. UpperArms, .. Forearms, .. Legs], 30, 80, 30, "plateMail"),
            ("skullcap", "head", [BodyRegion.Head], 10, 10, 10, "skullcap"),
            ("light_helm", "head", [BodyRegion.Head], 15, 15, 15, "lightHelm"),
            ("full_helm", "head", [BodyRegion.Head, BodyRegion.Neck], 30, 30, 20, "fullHelm"),
            ("leather_gloves", "hands", Hands, 0, 10, 10, "leathergloves"),
            ("gauntlets", "hands", Hands, 10, 10, 10, "gauntlets"),
            ("heavy_gauntlets", "hands", Hands, 15, 15, 15, "gauntlets_heavy"),
            ("greaves", "legs", [BodyRegion.LeftShin, BodyRegion.RightShin], 30, 80, 30, ""),
        };
        Assert.Equal(want.Select(w => w.Id), d.Pieces.Select(p => p.Id));
        foreach (var w in want)
        {
            var p = d.Piece(w.Id)!;
            Assert.Equal((w.Slot, Bits(w.Covers), w.OneOne), (p.Slot, p.Covers, p.OneOne));
            foreach (var r in BodyRegions.All) Assert.Equal(w.Covers.Contains(r), p.CoversRegion(r));
            Assert.Equal([w.B, w.L, w.A], p.Points);
            Assert.Equal([w.B / 20, w.L / 20, w.A / 20], p.Dice);
            Assert.NotEmpty(p.Source);
        }
        Assert.Equal((0.75, 2.25, 1.0), (d.Piece("mail_shirt")!.DiceOf(HarmKind.Bashing), d.Piece("mail_shirt")!.DiceOf(HarmKind.Lethal), d.Piece("mail_shirt")!.DiceOf(HarmKind.Aggravated)));
        Assert.Equal(4.0, d.Piece("plate_harness")!.DiceOf(HarmKind.Lethal));
        // Bashing and lethal for all; aggravated only from claws and fangs (fire and sunlight pass ordinary armour).
        Assert.True(d.Soaks(HarmKind.Bashing, "blunt_one_hand") && d.Soaks(HarmKind.Lethal, "sword_one_hand"));
        Assert.True(d.Soaks(HarmKind.Aggravated, "claws") && d.Soaks(HarmKind.Aggravated, "fangs"));
        Assert.False(d.Soaks(HarmKind.Aggravated, "fire"));
        // 1.1's guards and watch: a mail hauberk and a light helm (their template's soaks, 30/60/30; their models' cut).
        Assert.Equal(["mail_hauberk", "light_helm"], d.DressOf("rudolfGuard")!);
        Assert.Equal(["mail_hauberk", "light_helm"], d.DressOf("KnightOfStJohn_torch")!);
        Assert.Null(d.DressOf("Christof"));
        Assert.Null(d.DressOf("Civ12male"));
    }

    [Fact]
    public void A_wrong_armour_file_is_refused_by_name()
    {
        foreach (var (from, to, says) in new[]
        {
            ("\"covers\": [\"head\"],\n      \"covers_source\": \"ours: an iron cap\"", "\"covers\": [\"nose\"],\n      \"covers_source\": \"ours: an iron cap\"", "no body region"),
            ("\"aggravated_classes\": [\"claws\", \"fangs\"]", "\"aggravated_classes\": [\"claws\", \"sword_one_hand\"]", "is not aggravated"),
            ("\"layering\": \"sum\"", "\"layering\": \"average\"", "layering 'average'"),
            ("\"slot\": \"legs\"", "\"slot\": \"feet\"", "is not one of the slots"),
            ("\"pieces\": [\"mail_hauberk\", \"light_helm\"],\n      \"source\": \"1.1: darkAges.not rudolfGuard", "\"pieces\": [\"mail_hauberk\", \"bucket\"],\n      \"source\": \"1.1: darkAges.not rudolfGuard", "no piece of this file"),
            ("\"pieces\": [\"mail_hauberk\", \"light_helm\"],\n      \"source\": \"1.1: darkAges.not KnightOfStJohn_torch", "\"pieces\": [\"mail_hauberk\", \"plate_harness\"],\n      \"source\": \"1.1: darkAges.not KnightOfStJohn_torch", "both worn in the body slot"),
            ("\"points\": { \"bashing\": 15, \"lethal\": 45, \"aggravated\": 20 },\n      \"oneone\": \"chainMail\",\n      \"source\": \"1.1: darkAges.not chainMail", "\"points\": { \"bashing\": -15, \"lethal\": 45, \"aggravated\": 20 },\n      \"oneone\": \"chainMail\",\n      \"source\": \"1.1: darkAges.not chainMail", "must not be below 0"),
            ("\"points_per_die\": 20", "\"points_per_die\": 0", "must be above 0"),
        })
        {
            using var copy = new TestData.Copy();
            copy.Edit("combat/armor.json", s => { Assert.Contains(from, s); return s.Replace(from, to); });
            var x = Assert.Throws<ModuleRefusedException>(() => TestData.Sandbox(copy.Folder));
            Assert.Contains(x.Reasons, r => r.Contains("combat/armor") && r.Contains(says));
        }
    }

    // ---- what is worn ----

    [Fact]
    public void Pieces_worn_over_one_another_add_on_the_parts_they_share_and_one_piece_a_slot()
    {
        var rules = new ArmorRules(Data);
        var set = rules.Set("padded_coat,mail_shirt");
        Assert.Equal(0.5 + 2.25, set.Dice(BodyRegion.Chest, HarmKind.Lethal));
        Assert.Equal("padded_coat+mail_shirt", set.Over(BodyRegion.Chest));
        Assert.Equal(0.5, set.Dice(BodyRegion.LeftForearm, HarmKind.Lethal));           // the padding alone below the mail's sleeves
        Assert.Equal("padded_coat", set.Over(BodyRegion.LeftForearm));
        Assert.Equal(0, set.Dice(BodyRegion.Neck, HarmKind.Lethal));                    // a gap is a gap
        Assert.Null(set.Over(BodyRegion.Neck));
        Assert.Same(set, rules.Set("padded_coat,mail_shirt"));                          // put together once, kept by its text

        // The canonical text is the data's order; two pieces in a slot, one twice or an unknown one are refused by name.
        Assert.Equal("padded_coat,mail_shirt,light_helm", rules.Canonical(["light_helm", "mail_shirt", "padded_coat"], out _));
        Assert.Null(rules.Canonical(["mail_shirt", "plate_harness"], out string error));
        Assert.Contains("both worn in the body slot", error);
        Assert.Null(rules.Canonical(["light_helm", "light_helm"], out error));
        Assert.Contains("named twice", error);
        Assert.Null(rules.Canonical(["bucket"], out error));
        Assert.Contains("'bucket' is no piece of armour", error);
        Assert.Contains("mail_shirt", error);

        // The other layering rule: the most a part's pieces give.
        using var copy = new TestData.Copy();
        copy.Edit("combat/armor.json", s => s.Replace("\"layering\": \"sum\"", "\"layering\": \"highest\""));
        var highest = new ArmorRules(TestData.Sandbox(copy.Folder).Armor!).Set("padded_coat,mail_shirt");
        Assert.Equal(2.25, highest.Dice(BodyRegion.Chest, HarmKind.Lethal));
    }

    [Fact]
    public void The_guards_and_the_watch_are_dressed_by_their_1_1_template_and_a_sheet_given_whole_replaces_what_is_worn()
    {
        var m = TestData.Sandbox();
        var b = new WorldBuilder(1);
        m.Register(b);
        var w = b.Build();
        var people = w.Service<PeopleHooks>();
        var route = w.Service<IPersonSheets>();
        var (player, guard, watch, civ) = (w.Create(), w.Create(), w.Create(), w.Create());
        people.Made(player, PersonRole.Player, "Christof");
        people.Made(guard, PersonRole.Townsperson, "rudolfGuard");
        people.Made(watch, PersonRole.Townsperson, "KnightOfStJohn_torch");
        people.Made(civ, PersonRole.Townsperson, "Civ12male");
        Assert.Equal(["mail_hauberk", "light_helm"], route.WornBy(guard));
        Assert.Equal(["mail_hauberk", "light_helm"], route.WornBy(watch));
        Assert.Empty(route.WornBy(player));                                              // 1.1's Christof starts in none
        Assert.Empty(route.WornBy(civ));
        Assert.Equal("mail_hauberk,light_helm", w.Service<ActorSheets>().Condition(guard, "worn_armor"));
        Assert.Contains("gauntlets", route.ArmorPieces);

        // A template given whole replaces the sheet, what it wore with it; --armor (TryWear) dresses after.
        Assert.True(route.TryTemplate(guard, "sandbox.guard_trained", out string error), error);
        Assert.Empty(route.WornBy(guard));
        Assert.True(route.TryWear(guard, ["plate_harness", "full_helm", "gauntlets"], out error), error);
        Assert.Equal(["plate_harness", "full_helm", "gauntlets"], route.WornBy(guard));
        Assert.False(route.TryWear(guard, ["mail_shirt", "half_plate"], out error));
        Assert.Contains("one piece a slot", error);
        Assert.Equal(["plate_harness", "full_helm", "gauntlets"], route.WornBy(guard));   // a refusal changes nothing
        Assert.True(route.TryWear(guard, [], out error), error);                         // none undresses
        Assert.Null(w.Service<ActorSheets>().Condition(guard, "worn_armor"));
    }

    [Fact]
    public void What_is_worn_is_saved_and_loaded_with_the_sheet_and_digested()
    {
        var (w, sheets, _) = TestData.World();
        var route = w.Service<IPersonSheets>();
        var (a, b) = (w.Create(), w.Create());
        Assert.True(route.TryTemplate(a, "sandbox.guard_trained", out string error), error);
        Assert.True(route.TryTemplate(b, "sandbox.guard_trained", out error), error);
        ulong bare = w.Digest();
        Assert.True(route.TryWear(a, ["mail_shirt", "light_helm"], out error), error);
        Assert.NotEqual(bare, w.Digest());                                                // digested with the person
        string save = SheetJson.Write(sheets.Export(a));
        Assert.Contains("worn_armor", save);
        Assert.Contains("mail_shirt,light_helm", save);
        Assert.True(route.TryImport(b, save, out error), error);
        Assert.Equal(["mail_shirt", "light_helm"], route.WornBy(b));
        // A save naming a piece this module has not is refused by name.
        Assert.False(route.TryImport(b, save.Replace("mail_shirt,light_helm", "mail_shirt,bucket"), out error));
        Assert.Contains("worn_armor", error);
        Assert.Contains("'bucket' is no piece of armour", error);
    }

    // ---- the soak by the struck part ----

    [Fact]
    public void A_blow_on_a_covered_part_adds_the_armours_dice_to_the_soak_and_one_on_a_gap_gets_none()
    {
        var rig = Make(["mail_shirt", "light_helm"]);
        rig.Hit("chest", 1);           // mail: 2.25 lethal dice, made whole upward (unit draw 0): 3, all successes
        rig.Hit("left forearm", 2);    // below the mail's sleeves: a gap
        rig.Hit("head", 3);            // the light helm: 0.75 -> 1
        rig.Hit("neck", 4);            // nothing over the neck
        var h = rig.Harm;
        Assert.Equal(4, h.Count);
        Assert.Equal((3, "mail_shirt", 3, 3, 9, 3), (h[0].ArmorDice, h[0].Armor, h[0].SoakDice, h[0].SoakSuccesses, h[0].Wounds, h[0].ArmorKept));
        Assert.Equal((0, (string?)null, 0, 12, 0), (h[1].ArmorDice, h[1].Armor, h[1].SoakDice, h[1].Wounds, h[1].ArmorKept));
        Assert.Equal((1, "light_helm", 11, 1), (h[2].ArmorDice, h[2].Armor, h[2].Wounds, h[2].ArmorKept));
        Assert.Equal((0, (string?)null, 12), (h[3].ArmorDice, h[3].Armor, h[3].Wounds));
        var lines = rig.Lines();
        var armor = lines[0].GetProperty("events").EnumerateArray().Single(e => e.GetProperty("step").GetString() == "armor");
        Assert.Equal("chest", armor.GetProperty("inputs").GetProperty("region").GetString());
        Assert.Equal("mail_shirt,light_helm", armor.GetProperty("inputs").GetProperty("worn").GetString());
        Assert.Equal("Covered", armor.GetProperty("outputs").GetProperty("verdict").GetString());
        Assert.Equal(2.25, armor.GetProperty("outputs").GetProperty("armor_dice").GetDouble());
        Assert.Equal(3, armor.GetProperty("outputs").GetProperty("wounds_kept").GetInt32());
        Assert.Equal("mail_shirt", lines[0].GetProperty("armor").GetString());
        var gap = lines[1].GetProperty("events").EnumerateArray().Single(e => e.GetProperty("step").GetString() == "armor");
        Assert.Equal("Gap", gap.GetProperty("outputs").GetProperty("verdict").GetString());
        Assert.Contains("a gap is a gap", gap.GetProperty("outputs").GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, lines[1].GetProperty("armor").ValueKind);
    }

    [Fact]
    public void The_soak_is_by_the_harms_kind_bashing_lethal_and_the_claws_aggravated()
    {
        // Mail on the chest: bashing 0.75 -> 1 die, lethal 2.25 -> 3, aggravated (claws) 1.0 -> 1.
        var club = Make(["mail_shirt"], weapon: "Club");
        club.Hit("chest", 1);
        var sword = Make(["mail_shirt"], weapon: "BroadSword");
        sword.Hit("chest", 1);
        var claws = Make(["mail_shirt"], weapon: "Claw");
        claws.Hit("chest", 1);
        Assert.Equal((HarmKind.Bashing, 1), (club.Harm[0].Kind, club.Harm[0].ArmorDice));
        Assert.Equal((HarmKind.Lethal, 3), (sword.Harm[0].Kind, sword.Harm[0].ArmorDice));
        Assert.Equal((HarmKind.Aggravated, 1), (claws.Harm[0].Kind, claws.Harm[0].ArmorDice));
        // A human's bare Stamina soaks bashing, not lethal or aggravated the armour's dice join it.
        Assert.True(club.Harm[0].SoakDice > club.Harm[0].ArmorDice);
        Assert.Equal(sword.Harm[0].ArmorDice, sword.Harm[0].SoakDice);
    }

    [Fact]
    public void Armour_off_is_the_harm_of_before_armour_outcome_for_outcome()
    {
        // Real dice, the same seed: the guard in mail and a helm with armor-off against the guard in nothing with armour on.
        var off = Make(["mail_shirt", "light_helm"], strength: 15, maxDice: false, seed: 77, switches: EnhancedModule.ArmorOffSwitch);
        var bare = Make([], strength: 15, maxDice: false, seed: 77);
        var on = Make(["mail_shirt", "light_helm"], strength: 15, maxDice: false, seed: 77);
        string[] sites = ["chest", "belly", "head", "left forearm", "right upper arm", "neck", "hips", "left thigh"];
        for (int i = 0; i < 40; i++)
        {
            off.Hit(sites[i % sites.Length], i + 1);
            bare.Hit(sites[i % sites.Length], i + 1);
            on.Hit(sites[i % sites.Length], i + 1);
        }
        Assert.Equal(40, off.Harm.Count);
        Assert.Equal(bare.Harm, off.Harm);                                                     // every field of every blow
        Assert.All(off.Harm, h => Assert.Equal((0, (string?)null, 0), (h.ArmorDice, h.Armor, h.ArmorKept)));
        Assert.Contains("armour is off", off.Lines()[0].GetProperty("events").EnumerateArray().Single(e => e.GetProperty("step").GetString() == "armor")
            .GetProperty("outputs").GetProperty("reason").GetString());
        // Armour on keeps wounds off where it covers: fewer wounds in all, and some kept.
        Assert.True(on.Harm.Sum(h => h.Wounds) < bare.Harm.Sum(h => h.Wounds));
        Assert.True(on.Harm.Sum(h => h.ArmorKept) > 0);
    }

    [Fact]
    public void The_same_seed_and_the_same_contacts_give_the_same_harm_and_digest()
    {
        var a = Make(["padded_coat", "mail_shirt", "full_helm"], maxDice: false, seed: 9);
        var b = Make(["padded_coat", "mail_shirt", "full_helm"], maxDice: false, seed: 9);
        for (int i = 0; i < 12; i++) { a.Hit(i % 2 == 0 ? "chest" : "neck", i + 1); b.Hit(i % 2 == 0 ? "chest" : "neck", i + 1); }
        Assert.Equal(a.Harm, b.Harm);
        Assert.Equal(a.W.Digest(), b.W.Digest());
        Assert.Equal(a.Trace.ToString(), b.Trace.ToString());
        Assert.Contains(a.Harm, h => h.Armor == "full_helm");                                 // the neck under the aventail
        Assert.Contains(a.Harm, h => h.Armor == "padded_coat+mail_shirt");
    }

    [Fact]
    public void Nothing_is_allocated_per_tick_with_contacts_on_armour()
    {
        var m = TestData.Sandbox();
        var b = new WorldBuilder(3);
        DeathServices.Register(b);
        b.Event<ContactMade>();
        EntityId attacker = EntityId.None, target = EntityId.None;
        long id = 1;
        b.System(Phase.Contact, 0, "test.contacts", new DelegateSystem(x =>
        {
            if (x.Tick % 16 == 0 && attacker != EntityId.None)
                x.Events<ContactMade>().Publish(new ContactMade(id++, attacker, (int)id, target, ContactMet.Body, (id & 1) == 0 ? "chest" : "left forearm",
                    ContactMaterial.Cloth, x.Tick, Vector3.Zero, Vector3.UnitX, 9f, 12f, false, false, true, false));
        }));
        m.Register(b);
        var world = b.Build();
        world.Service<HarmWeapons>().Of = (_, _) => "BroadSword";
        var people = world.Service<PeopleHooks>();
        (attacker, target) = (world.Create(), world.Create());
        people.Made(attacker, PersonRole.Player, "Christof");
        people.Made(target, PersonRole.Townsperson, "rudolfGuard");                          // dressed: a hauberk and a helm
        var route = world.Service<IPersonSheets>();
        Assert.True(route.TrySet(target, "health_pool", 1000, out string error), error);
        Assert.True(route.TryTemplate(attacker, "sandbox.newly_embraced", out error), error);
        Assert.True(route.TryWear(target, ["padded_coat", "mail_shirt", "light_helm"], out error), error);
        for (int i = 0; i < 200; i++) world.Step();
        Allocations.None("400 ticks of contacts on armour and a gap", () => { for (int i = 0; i < 400; i++) world.Step(); });
        Assert.True(world.Service<HarmLedger>().Resolved > 30);
    }
}
