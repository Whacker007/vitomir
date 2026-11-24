// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Net;
using Vitomir.Engine;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **The effects' scripts on a player's machine**.
/// A host with its scripts and a player whose machine runs the
/// effects' scripts only (<see cref="CodexScenes.PlayersMachine"/>), in one process — each with its own natives
/// (<see cref="CodexHost.Branch"/>). Fortitude's effect put on her character on the host reaches her machine,
/// which makes `EffectDiscFortitude` itself and runs its `effectstarted`:
/// its `sawToothBlue` pad, flagged `NO NET SYNCH`, is made and hung on her there, and the host's own pad is never
/// sent — so she still sees exactly one. The host taking it off runs
/// the script's `effectended` there, which takes her pad away. A script's own effect is refused on her machine
/// (the effect's add refuses it there).
/// </summary>
[Collection("scripts")]
public class NetEffectScriptTests(Scripts s)
{
    static bool Until(Func<bool> done, params Action[] turns)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 20000)   // a hang guard (8 s was short for a run under the simulator)
        {
            foreach (var t in turns) t();
            if (done()) return true;
            Thread.Sleep(1);
        }
        return done();
    }

    static WorldThing? Pad(WorldThing on) =>
        on.Attachments.Select(a => a.Child).FirstOrDefault(c => c.InWorld && c.Template.Equals("sawToothBlue", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void A_discipline_s_pad_is_made_by_the_effect_s_script_on_her_machine_and_never_sent() => Play("late");

    /// <summary>
    /// An effect put on a character before the place was hosted — as the place woke, before any player — has its pad
    /// marked all the same (<see cref="MadeHere.ByEffect"/> marks on any machine), so a newcomer's catch-up leaves it
    /// out and her machine hangs its own as the effect reaches it.
    /// </summary>
    [Fact]
    public void A_pad_made_before_she_joined_is_left_out_of_her_catch_up_and_her_machine_makes_its_own() => Play("early");

    /// <summary>
    /// Shape of the Beast put on her character on the host: her machine's own `EffectDiscShapeOfTheBeast` crouches her
    /// copy and, when its timer comes due, shifts it into the clan's shape (`ActorShapeShift` — no network
    /// test), as the host's own does on the host.
    /// </summary>
    [Fact]
    public void Shape_of_the_beast_shifts_her_copy_on_her_machine_by_its_own_script() => Play("shape");

    /// <summary>
    /// Mask of a Thousand Faces put on her character on the host, its second parameter a townsman's GUID (the cast's
    /// `AddActorEffectByValue`, `DisciplineMask1000Faces`): the effect message carries the parameter (the
    /// message's two script parameters), her machine's own `EffectDiscMask1000Faces` reads it
    /// (`GetActorEffectIntParam`), finds its copy of the townsman by the same GUID and shifts her copy into his
    /// template when its timer comes due — **1.1 has no message of its own for a shape**; and the host
    /// taking the effect off runs `effectended` there, which ends it.
    /// </summary>
    [Fact]
    public void The_mask_of_a_thousand_faces_takes_her_copy_into_the_townsman_s_shape_on_her_machine_and_off_again() => Play("mask");

    /// <summary>
    /// A character already in a Mask when she joins: the catch-up sends the effect as 1.1's does (message 0x19),
    /// so her machine's script starts on arrival and her copy of him takes the shape there too.
    /// </summary>
    [Fact]
    public void A_character_masked_before_she_joined_is_masked_on_her_machine_too() => Play("maskearly");

    /// <summary>
    /// A torch worn by her character on the host: the host's `ItemUtilityTorch` hears `worn` and hangs its
    /// `torchFlame_nosave` on it (flagged `NO NET SYNCH`); her machine runs no item's script, and 1.1's host
    /// sends nothing so flagged — so on her screen the torch is dark.
    /// </summary>
    [Fact]
    public void A_torch_worn_on_the_host_is_dark_on_her_machine() => Play("torch");

    /// <summary>With the Fix `netprops` on the host the flame is sent, and her machine shows it on the torch.</summary>
    [Fact]
    public void With_the_netprops_fix_the_host_sends_the_torch_s_flame() => Play("netprops");

    void Play(string what)
    {
        bool early = what == "early";
        bool masked = what == "maskearly";
        s.Ready();
        var hostNatives = s.Engine.Natives;
        try
        {
            using var files = new GameFiles(GameFiles.Locate()!);
            var set = ChronicleSet.Load(files);
            var settings = GameSettings.For(set["MP_CurseDarkness"]!, "Anna's game", GameFlags.AutoStoryteller);
            var place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, entrance: settings.Entrance);
            place.Session = new Session { Here = place };
            var effects = new EffectDefinitions(files);
            var powers = new DisciplineDefinitions(files);
            place.Awaken(new AiClasses(files), 1, effects, powers);
            var scenes = new CodexScenes(s.Runtime!) { Chronicles = set, ReadText = _ => true };
            using var server = new GameServer(settings, new NetPeer(0)) { Qol = new Qol(), Fixes = new Fixes() };
            NetNatives.Register(scenes, server);
            var sim = new Simulation(place) { Scripts = scenes };
            scenes.Enter(place);
            Effect? fortitude = null;
            WorldThing? target = null;
            // Two townsmen: one to wear the mask, one whose face it takes.
            var townsmen = place.Things.Where(t => t.Actor is not null && t.InWorld && t.Type == ThingType.Actor).ToList();
            var face = townsmen.First(t => !townsmen[0].Template.Equals(t.Template, StringComparison.OrdinalIgnoreCase));
            if (masked)
                Assert.NotNull(EffectRuntime.Apply(townsmen[0], "ef_disc_mask1000faces", 0, durationMs: 60000, intParam: face.Guid));
            if (early)
            {
                // Fortitude on somebody of the place, before it is a host's.
                target = place.Things.First(t => t.Actor is not null && t.InWorld && t.Type == ThingType.Actor);
                fortitude = EffectRuntime.Apply(target, "ef_disc_fortitude", 1);
            }
            server.Start();
            server.Enter(place, null);
            hostNatives = s.Engine.Natives;

            // Her machine's scripts: the effects' only, on natives of their own.
            var herNatives = s.Engine.Branch();
            var hers = new CodexScenes(s.Runtime!) { PlayersMachine = true };
            s.Engine.Use(hostNatives);

            int ms = 0;
            void Host() { s.Engine.Use(hostNatives); sim.Step(1f / 30); ms += 33; server.Poll(ms); }
            using var client = new GameClient(new NetPeer(0), new IPEndPoint(IPAddress.Loopback, server.Peer.Port),
                                              new NetCharacter { Name = "Anna", Template = "MP_Serena" });
            client.Texts.Open = _ => true;
            client.Seen.Effects = effects;
            Location? mine = null;
            Simulation? herSim = null;
            client.Started += () =>
            {
                var c = client.Settings!;
                mine = Location.Load(files, c.Level, scene: c.Scene.Length > 0 ? c.Scene : null, player: "MP_Serena", entrance: c.Entrance);
                mine.Session = new Session { Here = mine };
                mine.Awaken(new AiClasses(files), 1, effects, powers);
                herSim = new Simulation(mine) { Scripts = hers };
                hers.Enter(mine);
                client.Enter(mine, mine.Player?.Thing);
            };
            void Her() { s.Engine.Use(herNatives); client.Poll(1f / 30); herSim?.Step(1f / 30); }
            void Both() { Host(); Her(); }

            Assert.True(Until(() => server.Players.Any(p => p.Name == "Anna" && p.Avatar is not null) && client.Mirror is not null, Both),
                        $"she never came in ({client.State})");
            var avatar = server.Players.Single(p => p.Name == "Anna").Avatar!;
            Assert.True(Until(() => client.Mirror!.Find(avatar.Guid) is { InWorld: true }, Both));
            Assert.Empty(hers.Running);                                             // none of the place's scripts
            Assert.Same(hers, mine!.ScriptObjects);

            if (what == "shape")
            {
                s.Engine.Use(hostNatives);
                Assert.NotNull(EffectRuntime.Apply(avatar, "ef_disc_shapeofthebeast", 0, durationMs: 60000));
                Assert.True(Until(() => ShapeShift.IsShifted(avatar.Actor!), Both), "the host never shifted her");
                Assert.True(Until(() => client.Mirror!.Find(avatar.Guid) is { Actor: { } copy } && ShapeShift.IsShifted(copy), Both),
                            "her machine never shifted its copy of her");
                return;
            }

            if (what is "mask" or "maskearly")
            {
                s.Engine.Use(hostNatives);
                var wearer = masked ? townsmen[0] : avatar;
                var mask = masked ? wearer.Effects.All.Single(e => e.Name == "ef_disc_mask1000faces")
                                  : EffectRuntime.Apply(avatar, "ef_disc_mask1000faces", 0, durationMs: 60000, intParam: face.Guid);
                Assert.NotNull(mask);
                Assert.True(Until(() => wearer.Actor!.ShapeChange?.Template.Equals(face.Template, StringComparison.OrdinalIgnoreCase) == true, Both),
                            "the host never masked her");
                Assert.True(Until(() => client.Mirror!.Find(wearer.Guid) is { Actor.ShapeChange: { } shape }
                                        && shape.Template.Equals(face.Template, StringComparison.OrdinalIgnoreCase), Both),
                            "her machine never masked its copy");
                var copy = client.Mirror!.Find(wearer.Guid)!;
                s.Engine.Use(hostNatives);
                EffectRuntime.Remove(wearer, mask!);
                Assert.True(Until(() => !ShapeShift.IsShifted(wearer.Actor!) && !ShapeShift.IsShifted(copy.Actor!), Both),
                            "the mask never came off both");
                return;
            }

            if (what is "torch" or "netprops")
            {
                s.Engine.Use(hostNatives);
                if (what == "netprops") place.Fixes = new Fixes { NetProps = true };
                var torch = place.Make("torchU")!;
                avatar.Actor!.Inventory.Add(torch.Item!);
                torch.Owner = avatar;
                Assert.Equal(EquipResult.Equipped, Equipment.Equip(avatar, torch.Item!, Slot.None, effects, ms));
                var flame = Assert.Single(torch.Attachments).Child;
                Assert.Equal("torchFlame_nosave", flame.Template, ignoreCase: true);
                Assert.True((flame.Flags & WorldThing.Flag.NoNetSync) != 0);
                Assert.True(MadeHere.Scripted(flame));                               // the item's script made it
                Assert.Equal(what == "torch", NetSynch.Kept(flame));
                Assert.True(Until(() => client.Mirror!.Find(torch.Guid) is { InWorld: true }, Both), "the torch never reached her");
                // And it stays in her world as the host's does: the sheet that lists her pack every half second
                // (GameServer.SheetMs) lists the torch in her off hand too, and took it out of the world, so under the
                // simulator a sheet that came after the torch's Made left it out for good.
                for (int i = 0; i < 2 * GameServer.SheetMs / 33 + 10; i++) Both();
                Assert.True(client.Mirror!.Find(torch.Guid) is { InWorld: true }, "her sheet took the torch in her off hand out of the world");
                if (what == "netprops")
                    Assert.True(Until(() => client.Mirror!.Find(flame.Guid) is { InWorld: true }, Both), "the flame never reached her");
                else
                {
                    for (int i = 0; i < 30; i++) Both();
                    Assert.Null(client.Mirror!.Find(flame.Guid));                    // dark on her screen, as in 1.1
                }
                return;
            }

            // Fortitude on her character, on the host: the host's script hangs its own pad on her.
            s.Engine.Use(hostNatives);
            if (!early)
            {
                target = avatar;
                fortitude = EffectRuntime.Apply(avatar, "ef_disc_fortitude", 1);
            }
            Assert.NotNull(fortitude);
            Assert.NotNull(fortitude!.Script);
            Assert.True(Until(() => client.Mirror!.Find(target!.Guid) is { InWorld: true }, Both));
            var herCopy = client.Mirror!.Find(target!.Guid)!;
            var hostPad = Pad(target);
            Assert.NotNull(hostPad);
            Assert.True((hostPad!.Flags & WorldThing.Flag.NoNetSync) != 0);
            Assert.True(NetSynch.Kept(hostPad));                                    // kept back: her machine makes its own

            // Her machine makes the script and its pad itself.
            Assert.True(Until(() => Pad(herCopy) is not null, Both), "her machine never hung a pad of its own");
            var herPad = Pad(herCopy)!;
            Assert.Equal(0, herPad.Guid & ThingGuids.HostBit);                      // made there, by her machine's own numbers
            Assert.Contains(herCopy.Effects.All, e => !e.Spent && e.Name == "ef_disc_fortitude" && e.Script is not null);
            for (int i = 0; i < 30; i++) Both();
            Assert.Null(client.Mirror.Find(hostPad.Guid));                          // the host's never sent
            Assert.Single(herCopy.Attachments, a => a.Child.InWorld && a.Child.Template.Equals("sawToothBlue", StringComparison.OrdinalIgnoreCase));

            // A script's own effect is refused on her machine: only the host's arrive.
            s.Engine.Use(herNatives);
            var asked = s.Engine.get(new object[] { "CodexActor.AddActorEffectByLevel", s.Runtime!.NewThingRef("CodexActor", herCopy.Guid)!,
                new object[] { "ef_disc_potence", java.lang.Integer.valueOf(0), java.lang.Integer.valueOf(1), java.lang.Integer.valueOf(0),
                               java.lang.Integer.valueOf(0), java.lang.Integer.valueOf(0), java.lang.Float.valueOf(0f) } });
            Assert.Equal(0, ((java.lang.Integer)asked).intValue());
            Assert.DoesNotContain(herCopy.Effects.All, e => e.Name == "ef_disc_potence");

            // The host takes it off: her machine's script hears `effectended` and takes her pad away.
            s.Engine.Use(hostNatives);
            EffectRuntime.Remove(target, fortitude);
            Assert.False(hostPad.InWorld);
            Assert.True(Until(() => !herPad.InWorld, Both), "her pad never went");
        }
        finally { s.Engine.Use(hostNatives); }
    }
}
