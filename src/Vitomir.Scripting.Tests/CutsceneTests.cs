// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// The monastery's own scripts, run end to end: `MON1_Main` opens the level on a conversation
/// between Christof and Wilhem, frames it with the cutscene camera line by line, gives the camera
/// back when it ends and hands out 50 experience; walking into its regions starts the rest.
/// </summary>
[Collection("scripts")]
public class CutsceneTests(Scripts s)
{
    sealed class Monastery : IDisposable
    {
        public required GameFiles Files { get; init; }
        public required Location Place { get; init; }
        public required CodexScenes Scenes { get; init; }
        public required WorldCamera Camera { get; init; }
        public required Conversations Talk { get; init; }
        public required Simulation Sim { get; init; }
        public required WorldThing Christof { get; init; }
        public required WorldThing Wilhem { get; init; }
        public List<string> Started { get; } = [];
        public List<(WorldThing Who, int Amount)> Given { get; } = [];

        public void Run(float seconds)
        {
            for (float t = 0; t < seconds; t += 1f / 30f) Sim.Step(1f / 30f);
        }

        public void Dispose()
        {
            Scenes.Leave();
            Files.Dispose();
        }
    }

    /// <summary>
    /// The monastery as the game has it on arrival: Christof steering, Wilhem with him (the
    /// campaign brings him; no monastery scene places him), the minds awake, and the scripts in.
    /// </summary>
    Monastery Arrive()
    {
        s.Ready();
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var owner = set.ForLevel("P1_MON1")!.Value;
        var place = Location.Load(files, "P1_MON1", sky: false, scene: owner.Entry!.SceneName, player: "Christof");
        var christof = place.Player!.Thing;
        var wilhem = place.Place("wilhem", christof.Position + new Vector3(96, 0, 0), christof.Orientation, christof.Sector)!;
        wilhem.Actor!.Flags |= Actor.Flag.Party;
        place.Awaken(new AiClasses(files));
        var camera = new WorldCamera();
        camera.Start(christof);
        var talk = new Conversations();
        var scenes = new CodexScenes(s.Runtime!) { Chronicles = set, Camera = camera, Talk = talk };
        var m = new Monastery
        {
            Files = files, Place = place, Scenes = scenes, Camera = camera, Talk = talk,
            Sim = new Simulation(place) { Scripts = scenes, Camera = camera, Talk = talk },
            Christof = christof, Wilhem = wilhem,
        };
        talk.Started += c => m.Started.Add(c.Talk.Name);
        scenes.ExperienceGiven = (who, amount) => m.Given.Add((who, amount));
        scenes.Enter(place);
        return m;
    }

    [Fact]
    public void The_monastery_opens_on_a_cutscene_conversation_that_pauses_the_ai()
    {
        using var m = Arrive();
        // beginscene: AIOff, the party stood on conversation group 0, and the conversation begun.
        Assert.Equal(["8_1_Mission2"], m.Started);
        Assert.NotEqual(0u, m.Place.Minds!.MainFlags & AiMainFlag.Paused);
        var starts = m.Place.ConversationStarts.Where(st => st.Group == 0).ToArray();
        Assert.Equal(starts[0].Position, m.Christof.Position);
        Assert.Equal(starts[1].Position, m.Wilhem.Position);
        Assert.Empty(m.Talk.Current!.Missing);

        // The first line reached: convreached sets up the cutscene on the two of them, shot by
        // shot — ANGULAR B, medium angle, medium distance, no lerp.
        m.Run(0.1f);
        Assert.Equal(CameraMode.Cutscene, m.Camera.Mode);
        Assert.Same(m.Christof, m.Camera.CutsceneA);
        Assert.Same(m.Wilhem, m.Camera.CutsceneB);
        Assert.Equal(((int)CameraShot.AngularB, 2, 2, 0), m.Camera.LastShot);
        Assert.Equal(55f, m.Camera.Fov);               // two-target ANGULAR B's lens
        Assert.True(m.Camera.StatusBarHidden);
        // The eye is somewhere sensible: off to the side of the pair, looking at their heads.
        float toChristof = Vector3.Distance(m.Camera.Eye, m.Christof.Position);
        Assert.InRange(toChristof, 100f, 800f);
    }

    /// <summary>
    /// The speaker plays `talk` and the other plays `listen`, line by line, and nothing puts `stand`
    /// back over them: the locomotion update is an event in the engine (the locomotion choice has thirteen
    /// callers, every one of them a change of state), not a frame's work. In play,
    /// during the conversation, Christof and Wilhem did not animate their conversation modes.
    /// When it ends, both go back to standing.
    /// </summary>
    [Fact]
    public void The_speakers_play_talk_and_listen_line_by_line_and_stand_when_it_is_over()
    {
        using var m = Arrive();
        var seen = new HashSet<(int Christof, int Wilhem)>();
        for (int i = 0; i < 40 && m.Talk.Current is not null; i++)
        {
            m.Run(0.1f);
            seen.Add((m.Christof.Animation!.Motion!.Mode.Index, m.Wilhem.Animation!.Motion!.Mode.Index));
        }
        Assert.Contains((Conversation.TalkMode, Conversation.Listen), seen);
        Assert.Contains((Conversation.Listen, Conversation.TalkMode), seen);
        // Checked the frame it ends: forty seconds on, the idle flourish (a roll on the AI's
        // stream every four seconds) may be playing, and whether it is depends on every roll before.
        for (int i = 0; i < 40 * 30 && m.Talk.Current is not null; i++) m.Run(1f / 30f);
        Assert.Null(m.Talk.Current);
        Assert.Equal(Player.ModeStand, m.Christof.Animation!.Motion!.Mode.Index);
        Assert.Equal(Player.ModeStand, m.Wilhem.Animation!.Motion!.Mode.Index);
    }

    /// <summary>
    /// **A journey in the middle of it still turns the AI back on**. The place change ends every
    /// script's scene and then aborts the conversation, and `MON1_Main`'s `convended` — told
    /// it was aborted — calls `AIOn`. In 1.1 that clears the one word the whole game has; in Vitomir the
    /// scripts had already let go of the place by then, so the native found no minds, the pause stayed
    /// on the word, and the word went with the party: a new game replayed through
    /// `4_1_AnezkaPray` reached Old Town with nobody moving.
    /// </summary>
    [Fact]
    public void A_conversation_aborted_by_a_journey_still_turns_the_ai_back_on()
    {
        using var m = Arrive();
        m.Run(0.1f);
        Assert.NotNull(m.Talk.Current);
        Assert.NotEqual(0u, m.Place.Minds!.MainFlags & AiMainFlag.Paused);            // AIOff
        m.Scenes.Leave(0);                                                            // every endscene first,
        Assert.True(m.Talk.Abandon(m.Place));                                         // then the abort
        Assert.Equal(0u, m.Place.Minds.MainFlags & AiMainFlag.Paused);                // AIOn, on the word that travels
    }

    [Fact]
    public void The_opening_conversation_ends_by_giving_the_camera_back_and_fifty_experience()
    {
        using var m = Arrive();
        m.Run(40f);
        Assert.Null(m.Talk.Current);
        Assert.Equal(CameraMode.Game, m.Camera.Mode);
        Assert.Same(m.Christof, m.Camera.Subject);
        Assert.Equal(0u, m.Place.Minds!.MainFlags & AiMainFlag.Paused);            // AIOn
        Assert.Equal([(m.Christof, 50), (m.Wilhem, 50)], m.Given);
        Assert.Equal(50, m.Christof.Actor!.Experience);
        // Wilhem is made with his sheet, whose `totalexperience 6000` is
        // his lifetime figure and nothing to spend — the 50 is on top: 6,050, and 50 to spend.
        Assert.Equal(6050, m.Wilhem.Actor!.Experience);
        Assert.Equal(50, m.Wilhem.Actor.SpendableExperience);
    }

    /// <summary>
    /// **The camera given back goes to whoever is steered** (the reliquary
    /// handed to the prince with Wilhem steered: *"the camera was focused on Christof, but I still had
    /// control of wilhem"*). 1.1 frames the steered character, not the party's leader.
    /// </summary>
    [Fact]
    public void A_released_camera_goes_back_to_the_character_being_steered()
    {
        using var m = Arrive();
        Assert.True(m.Place.Steer(m.Wilhem));
        m.Run(40f);
        Assert.Null(m.Talk.Current);
        Assert.Equal(CameraMode.Game, m.Camera.Mode);
        Assert.Same(m.Wilhem, m.Place.Steered);
        Assert.Same(m.Wilhem, m.Camera.Subject);
    }

    /// <summary>
    /// Walking into the monastery's regions starts their conversations — the strange clan, the
    /// mural and the fight — and the fight's first line hands the camera to a path out of
    /// `paths/MON1Fight.ncp`. Each walk-in starts from a spot in the same sector outside every
    /// region there, because which regions a character is in is one word read against the sector's
    /// own list: stepped straight from one region into another sector's, a character already has
    /// the bit and nothing fires (`StoryTests` shows that quirk on its own).
    /// </summary>
    [Fact]
    public void Walking_into_the_regions_starts_their_conversations()
    {
        using var m = Arrive();
        m.Run(40f);                                   // let the opening play out
        bool pathed = false;
        foreach (var region in m.Place.Things.Where(t => t.Type == ThingType.Region && t.Scene.Model is { Planes.Length: > 0 }
                                                        && Regions.Contains(t, t.Position)).ToArray())
        {
            long sector = m.Place.At(region.Position, m.Christof.Sector);
            var list = m.Place.Regions!.In(sector).OfType<WorldThing>().ToArray();
            Vector3? clear = Enumerable.Range(1, 60).SelectMany(r => new[]
                {
                    new Vector3(20f * r, 0, 0), new Vector3(-20f * r, 0, 0),
                    new Vector3(0, 20f * r, 0), new Vector3(0, -20f * r, 0),
                })
                .Select(d => region.Position + d)
                .Where(p => m.Place.At(p, sector) == sector && list.All(x => !Regions.Contains(x, p)))
                .Select(p => (Vector3?)p).FirstOrDefault();
            if (clear is not { } outside) continue;
            m.Place.Move(m.Christof, outside);
            m.Place.Move(m.Christof, region.Position);
            m.Run(0.2f);
            pathed |= m.Camera.Mode == CameraMode.Path;
            m.Run(60f);
        }
        Assert.Contains("8_2_Strange", m.Started);
        Assert.Contains("8_2_Cainite2", m.Started);
        Assert.Contains("8_2_Fight", m.Started);
        Assert.True(pathed, "the fight conversation never put the camera on its path");
    }
}
