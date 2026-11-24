// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **The shipped disciplines' <c>cancast</c> judged on a player's machine** (<see cref="CodexScenes.Judge"/>,
/// <see cref="OwnCast"/>): each class's own script, run on a player's machine's script layer (<see cref="CodexScenes.PlayersMachine"/>)
/// at her character and, for a power that takes a thing, at a townsman beside her. The judge answers yes or no when the script read
/// only what her machine has as the host has it, and nothing when it read anything else — and then her cast's start is not guessed.
/// It says nothing while it judges: a refusal's line is the host's to send.
/// </summary>
[Collection("scripts")]
public class CastJudgeTests(Scripts s)
{
    /// <summary>
    /// The classes whose <c>cancast</c> reads, on this path, what her machine does not have: the pet count (the minds are the host's),
    /// the target's blood (a character's stats ride the flags message only when something else of it changes), the chronicle's place.
    /// </summary>
    static readonly Dictionary<string, string> NotJudged = new(StringComparer.Ordinal)
    {
        ["Beckoning"] = "AIGetNumPets",
        ["DarkHunter"] = "AIGetNumPets",
        ["HatchTheViper"] = "AIGetNumPets",
        ["SummonElemental"] = "AIGetNumPets",
        ["SummonSoul"] = "AIGetNumPets",
        ["ShamblingHordes"] = "AIGetNumPets",
        ["TheftOfVitae"] = "GetActorStat",
        ["TongueOfTheAsp"] = "GetActorStat",
        ["WalkTheAbyss"] = "GetLocationNum",
    };

    [Fact]
    public void Her_machine_judges_each_shipped_disciplines_cancast_or_says_it_cannot()
    {
        s.Ready();
        var natives = s.Engine.Natives;
        var hers = s.Engine.Branch();
        Location? place = null;
        try
        {
            using var files = new GameFiles(GameFiles.Locate()!);
            var set = ChronicleSet.Load(files);
            var settings = GameSettings.For(set["MP_CurseDarkness"]!, "Anna's game", GameFlags.AutoStoryteller);
            place = Location.Load(files, settings.Level, scene: settings.Scene.Length > 0 ? settings.Scene : null, player: "MP_Serena", entrance: settings.Entrance);
            place.Session = new Session { Here = place };
            var powers = new DisciplineDefinitions(files);
            place.Awaken(new AiClasses(files), 1, new EffectDefinitions(files), powers);
            NetOrders.Attach(place, NetMode.Client);
            var scenes = new CodexScenes(s.Runtime!) { PlayersMachine = true };
            scenes.Enter(place);
            var her = place.Player!.Thing;
            her.Actor!.Health = her.Actor.MaxHealth * 0.5f;                          // Blood Healing and Heal: not at full health
            var townsman = place.Things.First(t => t.Type == ThingType.Actor && t.InWorld && t.Actor is { Dead: false } && !ReferenceEquals(t, her));
            int lines = scenes.Lines.Count;
            var found = new SortedDictionary<string, bool?>(StringComparer.Ordinal);
            foreach (var def in powers.File!.All)
            {
                if (def.Script.Length == 0) continue;
                var inst = DisciplineBook.Make(her, def);
                Assert.NotNull(inst);
                Assert.NotNull(inst!.Script);
                inst.Level = 1;
                bool thing = (def.Flags & Cast.ThingTargets) != 0;
                found[def.Name] = scenes.Judge(inst.Script!, 1, her, thing ? townsman : null,
                                              def.Has(DisciplineFlags.TargetArea) ? townsman.Position : null);
            }
            var unjudged = found.Where(f => f.Value is null).Select(f => f.Key).ToHashSet();
            Assert.True(unjudged.SetEquals(NotJudged.Keys),
                        $"not judged here: {string.Join(", ", unjudged.Order())}; expected {string.Join(", ", NotJudged.Keys.Order())}");
            // Judged, and by what her machine has: a townsman is no player (Command, Entrancement, Possession, Vigor Mortis say yes), is
            // not an animal (Feral Whispers and Subsume the Spirit say no), is not dead (Awaken no), is no item (Spirit's Touch no).
            Assert.True(found["Command"] == true && found["Possession"] == true && found["Entrancement"] == true && found["VigorMortis"] == true);
            Assert.True(found["FeralWhispers"] == false && found["SubsumeTheSpirit"] == false && found["Awaken"] == false && found["SpiritsTouch"] == false);
            Assert.True(found["BloodHealing"] == true && found["Heal"] == true);
            Assert.True(found["Celerity"] == true);                                   // no cancast of its own: yes
            Assert.Equal(lines, scenes.Lines.Count);                                  // and it said nothing
            // At full health Blood Healing's own refusal, judged, and still silent.
            her.Actor.Health = her.Actor.MaxHealth;
            Assert.False(scenes.Judge(her.Actor.Disciplines["BloodHealing"]!.Script!, 1, her, null, null));
            Assert.Equal(lines, scenes.Lines.Count);
            Assert.Null(s.Engine.Judging);
        }
        finally
        {
            if (place is not null) NetOrders.Detach(place);
            s.Engine.Use(natives);
        }
    }
}
