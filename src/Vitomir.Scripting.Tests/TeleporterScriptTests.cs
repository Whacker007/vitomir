// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **Walk the Abyss, played** from a save (`SP3.vsave`: Monastery 1, vampire Christof and
/// Wilhem) with the shipped scripts: `DisciplineWalkTheAbyss.cast` calls `CreateTeleporter`, the
/// `Teleporter` script on each end calls `ActivateTeleporter` when it is used, and the main
/// sequence's journey (state 6) carries the party through — to the haven and back — and a save
/// taken with the portal open loads with it still working. And a lit torch in Christof's hand comes
/// through the portal and back, or an exit and back, with **one** flame, on the torch: until then the
/// torch's old body, left in the monastery, relit itself there when the party came back
/// (`CodexScenes.ForgetCarriedBodies`). The engine's rules on their own are
/// `Vitomir.Engine.Tests.TeleportTests`.
/// </summary>
[Collection("scripts")]
public class TeleporterScriptTests(Scripts s)
{
    /// <summary>
    /// The Monastery 1 save, wherever this machine keeps it: `VITOMIR_SP3` if set, the player's
    /// own save folder (`%LOCALAPPDATA%\Vitomir\Saves` on Windows), or the cloud workspace's staged
    /// copy. The first that exists; read only, never written. A slot 03 that is not the monastery any
    /// more is skipped by <see cref="BeginPlayed"/>.
    /// </summary>
    static readonly string PlayedSave = new[]
    {
        Environment.GetEnvironmentVariable("VITOMIR_SP3"),
        Path.Combine(Vitomir.Engine.Diagnostics.Diag.UserDataFolder, "Saves", "SP3.vsave"),
        Path.Combine(Environment.GetEnvironmentVariable("VITOMIR_NPF_SAMPLES") ?? "", "Vitomir", "Saves", "SP3.vsave"),
    }.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p)) ?? "SP3.vsave";

    sealed class Game : IDisposable
    {
        public required GameFiles Files { get; init; }
        public required Campaign Campaign { get; init; }
        public required Session Session { get; init; }
        public required CodexScenes Scenes { get; init; }
        public required WorldCamera Camera { get; init; }
        public required Conversations Talk { get; init; }
        public required Simulation Sim { get; set; }
        public Chronicle Chronicle => Campaign.Chronicle;
        public Location Here => Campaign.Here;
        public WorldThing Leader => Here.Player!.Thing;

        public bool RunUntil(Func<bool> done, float seconds)
        {
            for (float t = 0; t < seconds; t += 1f / 30f)
            {
                if (done()) return true;
                Sim.Step(1f / 30f);
            }
            return done();
        }

        /// <summary>What the host's main loop does once a frame: take the journey, and hand the new place its session, scripts and simulation.</summary>
        public Campaign.Arrival? Travel()
        {
            if (Campaign.Travel() is not { } arrival) return null;
            // The place left keeps its scripts for when the party comes back, as the host's arrival does.
            Scenes.Leave(0, keep: Campaign.Resident.Values.Any(p => ReferenceEquals(p, arrival.From)));
            var to = arrival.To;
            to.Session = Session;
            Session.Here = to;
            Camera.Start(to.Player!.Thing);
            Scenes.Enter(to);
            var was = Sim.Seconds;
            Sim = new Simulation(to) { Scripts = Scenes, Camera = Camera, Talk = Talk };
            Sim.Resume(was);
            return arrival;
        }

        public void Dispose() { Scenes.Leave(); Files.Dispose(); }
    }

    /// <summary>
    /// A game from a save, as the host loads one: the story put back, the place the party is in
    /// built (from its record when the save has one — this one, imported from 1.1, has none and is
    /// built as first made), the party and the session, every other place parked, the scripts armed
    /// and entered.
    /// </summary>
    Game Begin(SaveGame from)
    {
        s.Ready();
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var effects = new EffectDefinitions(files);
        var powers = new DisciplineDefinitions(files);
        var story = set[from.Header.Chronicle]!;
        SaveApply.Chronicle(story, from.Chronicle!);
        string leader = from.Party.Members[0].Template;
        Location Build(string level, string? scene, int entrance)
        {
            var place = Location.Load(files, level, sky: false, scene: scene, player: leader, entrance: entrance);
            place.Awaken(new AiClasses(files), 1, effects, powers);
            return place;
        }
        Location here;
        ScriptRestore restore;
        if (from.World.TryGetValue(from.Header.Location, out var record))
        {
            here = Location.Load(files, record.Level, sky: false, scene: record.Scene.Length > 0 ? record.Scene : null,
                                 player: leader, entrance: record.Entrance);
            restore = SaveApply.Place(here, record, effects, powers);
        }
        else
        {
            var at = story.At(story.Current)!;
            here = Location.Load(files, at.LevelName, sky: false, scene: at.SceneName.Length > 0 ? at.SceneName : null,
                                 player: leader, entrance: -1);
            restore = new ScriptRestore();
        }
        var steered = SaveApply.Party(here, from.Party, restore, effects, powers);
        var minds = here.Awaken(new AiClasses(files), 1, effects, powers, fromSave: true);
        if (steered is not null) minds.Steered = steered;
        here.Milliseconds = (int)from.Header.ClockMs;
        var session = new Session { Here = here, Chronicle = story };
        here.Session = session;
        SaveApply.Session(session, from.Session, here);

        var camera = new WorldCamera();
        camera.Start(here.Player!.Thing);
        var talk = new Conversations();
        var scenes = new CodexScenes(s.Runtime!)
        {
            Chronicles = set, Camera = camera, Talk = talk, Nls = new Vitomir.Engine.Ui.Strings(files).Find,
        };
        var campaign = new Campaign
        {
            Chronicle = story, Here = here, Build = Build,
            BuildSaved = (r, entrance) =>
            {
                var place = Location.Load(files, r.Level, sky: false, scene: r.Scene.Length > 0 ? r.Scene : null,
                                          player: leader, entrance: entrance);
                var back = SaveApply.Place(place, r, effects, powers);
                place.Awaken(new AiClasses(files), 1, effects, powers, fromSave: true);
                scenes.Arm(place, back);
                return place;
            },
        };
        foreach (var (name, other) in from.World)
            if (story.Find(name) is var i && i >= 0 && i != story.Current) campaign.Parked[i] = other;
        session.Teleports.Campaign = campaign;
        scenes.Reset();
        if (from.Chronicle is { } c) scenes.ArmChronicle(c.Scripts);
        scenes.Arm(here, restore);
        scenes.Enter(here);
        var sim = new Simulation(here) { Scripts = scenes, Camera = camera, Talk = talk };
        sim.Resume(from.Header.ClockMs / 1000f);
        return new Game { Files = files, Campaign = campaign, Session = session, Scenes = scenes, Camera = camera, Talk = talk, Sim = sim };
    }

    Game BeginPlayed()
    {
        s.Ready();
        Assert.SkipUnless(File.Exists(PlayedSave), $"no {PlayedSave}");
        var save = SaveArchive.Read(PlayedSave);
        Assert.SkipUnless(save.Header.Location.Contains("MON1", StringComparison.OrdinalIgnoreCase)
                          || save.Header.Description.Contains("Monastery", StringComparison.OrdinalIgnoreCase),
                          $"{PlayedSave} is no longer the Monastery 1 save");
        return Begin(save);
    }

    /// <summary>
    /// That save is format 1 — written earlier, when each place numbered its own things — and a
    /// load keeps the party on the numbers it gave them (`SaveGuids.Convert`): a script that asks
    /// after Christof or Wilhem by the GUID it kept finds him, before and after the game has run on.
    /// </summary>
    [Fact]
    public void A_script_finds_the_party_by_the_GUIDs_the_old_save_gave_them()
    {
        using var g = BeginPlayed();
        var saved = SaveArchive.Read(PlayedSave);
        Assert.Equal(SaveArchive.FormatVersion, saved.Header.Format);           // converted as read; the file untouched
        foreach (var member in saved.Party.Members)
        {
            Assert.True(member.Guid > 0);
            Assert.Equal(member.Template, g.Here.Thing(member.Guid)?.Template, StringComparer.OrdinalIgnoreCase);
            Assert.True(IsActor(member.Guid), $"a script does not find {member.Template} as {member.Guid}");
        }
        Assert.Equal(saved.Party.Members[0].Guid, g.Leader.Guid);
        Assert.Equal(g.Here.Things.Count, g.Here.Things.Select(t => t.Guid).Distinct().Count());
        g.RunUntil(() => false, 2f);
        Assert.Same(g.Leader, g.Here.Thing(saved.Party.Members[0].Guid));
        Assert.True(IsActor(saved.Party.Members[^1].Guid));
    }

    bool IsActor(int guid) =>
        ((java.lang.Boolean)s.Engine.get(new object[] { "Codex.IsActorGuid", null!, new object[] { java.lang.Integer.valueOf(guid) } })!).booleanValue();

    static IEnumerable<WorldThing> Portals(Location place) =>
        place.Things.Where(t => !t.Removed && t.Template.Equals("havenPortal", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Christof learns Walk the Abyss if he lacks it (a `discScroll_WalkTheAbyss_1` would teach it at
    /// level 0), has the 25 blood level 0 costs — the save has him at 20 — and casts as if from a
    /// scroll, which is the one thing `Discipline.Fizzled` reads before its `Math.random()`.
    /// </summary>
    static void Cast(Game g, DisciplineDefinitions powers)
    {
        var christof = g.Leader;
        var me = christof.Actor!;
        var walk = me.Disciplines["WalkTheAbyss"] ?? DisciplineBook.SetLevel(christof, powers, "WalkTheAbyss", 0)!;
        me.SetBaseStat((int)ActorStat.Blood, Math.Max(me.Stat[(int)ActorStat.Blood], 100f));
        me.Stat[(int)ActorStat.Blood] = Math.Max(me.Stat[(int)ActorStat.Blood], 100f);
        me.Flags2 |= Actor.Flag2.ScrollCast;
        Assert.Equal(OrderResult.Started, Engine.Rpg.Cast.Order(christof, null, null, walk, InputModifier.Space));
        Assert.True(g.RunUntil(() => g.Session.Teleports.Slots.Any(x => !x.Free), 6f), "the cast opened no portal");
        // The pair opens on the `Cast` key; the spell animation plays on after it, and he is busy till it ends.
        Assert.True(g.RunUntil(() => me.BusyUntil <= g.Here.Milliseconds, 6f), "the cast never ended");
    }

    /// <summary>The use a click on a thing gives, and the frames until its script has asked for the journey.</summary>
    static void Use(Game g, WorldThing who, WorldThing thing)
    {
        var answer = Actions.Use(who, thing, g.Here.Milliseconds);
        Assert.True(g.RunUntil(() => g.Chronicle.Pending is { Kind: SequenceKind.Teleport }, 8f),
                    $"{thing.Template} was used ({answer}) and asked for no journey; {who.Template} {who.Type} at {who.Position}, " +
                    $"{thing.Template} at {thing.Position}, action {who.Actor?.Action}, current {g.Chronicle.Current}, slots " +
                    string.Join("; ", g.Session.Teleports.Slots.Where(x => !x.Free)) + $", its id {Vitomir.Engine.Save.ThingIds.Of(g.Here, thing)}, " +
                    $"scripts {string.Join(",", g.Scenes.Running)}, lines {string.Join(" | ", g.Scenes.Lines.TakeLast(3))}");
    }

    [Fact]
    public void Casting_Walk_the_Abyss_in_the_monastery_opens_a_portal_that_takes_the_party_to_the_haven_and_back()
    {
        using var g = BeginPlayed();
        var powers = new DisciplineDefinitions(g.Files);
        int monastery = g.Chronicle.Current;
        Assert.Equal("Monastery", g.Chronicle.NameOf(monastery));
        Assert.Equal(2, g.Here.PartyMembers().Count());

        Cast(g, powers);
        // The discipline said its line, and the pair is open: one end beside Christof, the other in
        // the haven, which was loaded to take it.
        Assert.Contains(g.Scenes.Lines, l => l.Contains("Walk The Abyss", StringComparison.OrdinalIgnoreCase));
        var near = Assert.Single(Portals(g.Here));
        Assert.True(Vector2.Distance(new(near.Position.X, near.Position.Y), new(g.Leader.Position.X, g.Leader.Position.Y)) < 120f);
        Assert.Contains("Teleporter", g.Scenes.Running);
        int haven = g.Chronicle.Find("Haven");
        Assert.Single(Portals(g.Campaign.Resident[haven]));

        // Christof uses it: the `Teleporter` script's `clicked` → `ActivateTeleporter` → state 6.
        Use(g, g.Leader, near);
        Assert.NotNull(g.Travel());
        Assert.Equal(haven, g.Chronicle.Current);
        var far = Assert.Single(Portals(g.Here));
        Assert.Equal(new Vector3(193, -616, -368), far.Position);
        Assert.Equal(2, g.Here.PartyMembers().Count());
        Assert.All(g.Here.PartyMembers(), m =>
            Assert.True(Vector2.Distance(new(m.Position.X, m.Position.Y), new(far.Position.X, far.Position.Y)) < 120f));
        // The far end was made while the party was away, and has its script now they are here.
        Assert.Contains("Teleporter", g.Scenes.Running);

        // And back: the haven's end takes them to the monastery, and the pair closes behind them.
        Use(g, g.Leader, far);
        Assert.True(far.Removed);
        Assert.NotNull(g.Travel());
        Assert.Equal(monastery, g.Chronicle.Current);
        Assert.True(near.Removed);
        Assert.Empty(Portals(g.Here));
        Assert.All(g.Session.Teleports.Slots, slot => Assert.True(slot.Free));
        Assert.True(Vector2.Distance(new(near.Position.X, near.Position.Y), new(g.Leader.Position.X, g.Leader.Position.Y)) < 120f);

        // Again: the haven is still loaded, with the scripts it had, and its new end — made while the
        // party was away — has a script of its own by the time they arrive.
        Cast(g, powers);
        var second = Assert.Single(Portals(g.Here));
        Use(g, g.Leader, second);
        Assert.NotNull(g.Travel());
        Assert.Equal(haven, g.Chronicle.Current);
        var secondFar = Assert.Single(Portals(g.Here));
        Assert.NotSame(far, secondFar);
        Use(g, g.Leader, secondFar);
        Assert.NotNull(g.Travel());
        Assert.Equal(monastery, g.Chronicle.Current);
        Assert.Empty(Portals(g.Here));
    }

    /// <summary>
    /// **Where the party lands, to the tenth of a unit, as in 1.1**:
    /// Christof casting at (−1090, −632.3, −252) facing −82.7 with Wilhem at
    /// (−915.8, −610.1) facing −96. The near end goes beside him at the two radii apart × 1.1 (32 +
    /// 55 → 95.7): not ahead, where Wilhem stood, and not turned +45, whose floor is another
    /// sector's (the ground lookup looks only in the anchor's own sector and the one the point is in), but
    /// turned −45, facing out. In the haven Christof stood ahead of the far end and Wilhem at +45;
    /// and coming back, round a portal that stands on no surface, no direction found a floor, and
    /// both came back **on the portal**, on one spot, facing its way.
    /// </summary>
    [Fact]
    public void Walk_the_Abyss_lands_the_party_where_1_1_landed_it()
    {
        using var g = BeginPlayed();
        var powers = new DisciplineDefinitions(g.Files);
        var christof = g.Leader;
        var wilhem = g.Here.PartyMembers().First(m => !ReferenceEquals(m, christof));
        void Stand(WorldThing who, Vector3 at, float heading)
        {
            g.Here.Move(who, at);
            who.Orientation = new Vector3(0f, 0f, heading);
            if (g.Here.Player is { } p && ReferenceEquals(p.Thing, who)) { p.Stop(); p.Ground(); p.Heading = heading; }
            else if (who.Mind?.Walker is { } w) { w.Stop(); w.Ground(); }
        }
        Stand(christof, new Vector3(-1090f, -632.3f, -252f), -82.7f);
        Stand(wilhem, new Vector3(-915.8f, -610.1f, -252f), -96f);
        static void At(WorldThing t, float x, float y, float z, float heading)
        {
            Assert.True(Vector3.Distance(t.Position, new Vector3(x, y, z)) < 0.15f, $"{t.Template} at {t.Position}, 1.1 had ({x}, {y}, {z})");
            Assert.True(MathF.Abs(Player.Wrap(t.Orientation.Z - heading)) < 0.15f, $"{t.Template} facing {t.Orientation.Z}, 1.1 had {heading}");
        }

        Cast(g, powers);
        var near = Assert.Single(Portals(g.Here));
        At(near, -1014.3f, -690.8f, -252f, -127.7f);

        Use(g, christof, near);
        Assert.NotNull(g.Travel());
        var far = Assert.Single(Portals(g.Here));
        At(far, 193f, -616f, -368f, 75f);
        var party = g.Here.PartyMembers().ToList();
        At(party.Single(m => m.Template.Equals("christof", StringComparison.OrdinalIgnoreCase)), 100.6f, -591.3f, -368f, 75f);
        At(party.Single(m => m.Template.Equals("wilhem", StringComparison.OrdinalIgnoreCase)), 110.1f, -663.9f, -368f, 120f);

        Use(g, g.Leader, far);
        Assert.NotNull(g.Travel());
        foreach (var m in g.Here.PartyMembers()) At(m, -1014.3f, -690.8f, -252f, -127.7f);
    }

    [Fact]
    public void A_save_taken_with_the_portal_open_loads_with_it_open_and_it_still_goes_to_the_haven()
    {
        SaveGame taken;
        using (var g = BeginPlayed())
        {
            Cast(g, new DisciplineDefinitions(g.Files));
            taken = SaveCapture.Take(g.Here, g.Session, g.Campaign);
        }
        string path = Path.Combine(Path.GetTempPath(), $"vitomir-portal-{Guid.NewGuid():N}.vsave");
        try { SaveArchive.Write(taken, path); taken = SaveArchive.Read(path); }
        finally { File.Delete(path); }
        Assert.Single(taken.Session.Teleports);
        Assert.True(taken.World.ContainsKey("Haven"));

        using var back = Begin(taken);
        var near = Assert.Single(Portals(back.Here));
        // The restored end is a thing like any other: its template's script comes back with it.
        Assert.Contains("Teleporter", back.Scenes.Running);
        Assert.False(back.Session.Teleports.Slots[0].Free);

        Use(back, back.Leader, near);
        Assert.NotNull(back.Travel());
        Assert.Equal("Haven", back.Chronicle.NameOf(back.Chronicle.Current));
        var far = Assert.Single(Portals(back.Here));
        Assert.Equal(new Vector3(193, -616, -368), far.Position);
        Assert.Contains("Teleporter", back.Scenes.Running);
    }

    // ---- the torch's flame on a journey (the lone flame once left in the monastery) ------------

    static IEnumerable<WorldThing> Flames(Location place) =>
        place.Things.Where(t => !t.Removed && t.InWorld && t.Template.Equals("torchFlame_nosave", StringComparison.OrdinalIgnoreCase));

    /// <summary>Every place the game has loaded: the one the party is in and the ones kept.</summary>
    static IEnumerable<Location> Loaded(Game g) => g.Campaign.Resident.Values.Append(g.Here).Distinct();

    /// <summary>A torch made, given to Christof and put in his off hand, as `--give torchU` and a double-click do.</summary>
    static Item Hold(Game g, string template)
    {
        var body = g.Here.Make(template)!;
        g.Leader.Actor!.Inventory.Add(body.Item!);
        body.Owner = g.Leader;
        Assert.Equal(EquipResult.Equipped, Equipment.Equip(g.Leader, body.Item!, Slot.None, new EffectDefinitions(g.Files), 0));
        return body.Item!;
    }

    /// <summary>Exactly one flame burning anywhere the game has loaded, and it rides the torch in the leader's place.</summary>
    static void OneFlameOnTheTorch(Game g, Item torch, string when)
    {
        var all = Loaded(g).SelectMany(p => Flames(p).Select(f => (p, f))).ToList();
        Assert.True(all.Count == 1, $"{when}: {all.Count} flames: " + string.Join("; ", all.Select(x => $"{x.p.Name} at {x.f.Position} on {x.f.AttachedTo?.Template}")));
        var (place, flame) = all[0];
        Assert.Same(g.Here, place);
        Assert.Same(torch.Thing, flame.AttachedTo);
        Assert.Same(g.Leader, torch.Thing!.AttachedTo);
    }

    [Fact]
    public void Through_the_portal_and_back_leaves_one_flame_and_it_is_on_the_torch()
    {
        using var g = BeginPlayed();
        var powers = new DisciplineDefinitions(g.Files);
        var torch = Hold(g, "torchU");
        OneFlameOnTheTorch(g, torch, "lit");

        Cast(g, powers);
        var near = Assert.Single(Portals(g.Here));
        Use(g, g.Leader, near);
        Assert.NotNull(g.Travel());
        OneFlameOnTheTorch(g, torch, "in the haven");

        var far = Assert.Single(Portals(g.Here));
        Use(g, g.Leader, far);
        Assert.NotNull(g.Travel());
        Assert.Equal("Monastery", g.Chronicle.NameOf(g.Chronicle.Current));
        OneFlameOnTheTorch(g, torch, "back in the monastery");
    }

    [Fact]
    public void Out_of_an_exit_and_back_leaves_one_flame_and_it_is_on_the_torch()
    {
        using var g = BeginPlayed();
        var torch = Hold(g, "torchU");
        int home = g.Chronicle.Current;
        // The monastery's open exit (to the Convent, which has no exit straight back), and back by
        // the scripts' own journey, `Jump` — both ordinary journeys, the place
        // left each time kept loaded.
        var here = g.Chronicle.At(home)!;
        int exit = Enumerable.Range(0, here.ExitCount).First(n => !here.Exits[n].Closed && here.Exits[n].To >= 0);
        Assert.True(g.Chronicle.TakeExit(exit));
        Assert.NotNull(g.Travel());
        Assert.NotEqual(home, g.Chronicle.Current);
        Assert.Contains(g.Campaign.Resident.Values, p => p.Name.Equals("P1_MON1", StringComparison.OrdinalIgnoreCase));
        OneFlameOnTheTorch(g, torch, "through the exit");

        Assert.True(g.Chronicle.Jump(g.Chronicle.NameOf(home), 0));
        Assert.NotNull(g.Travel());
        Assert.Equal(home, g.Chronicle.Current);
        OneFlameOnTheTorch(g, torch, "back");
    }
}
