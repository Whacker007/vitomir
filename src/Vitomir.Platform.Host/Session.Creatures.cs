// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Host;

/// <summary>
/// 1.1's creatures move and close as 1.1 has them (after playing the earlier build's szlachta: <i>"The szlachta
/// moves a bit too fast and is too aggressive. In the base game they move and pause more frequently, move much more slowly."</i>). A
/// fighter's <b>movement</b> (an arena fighter's or a scenario actor's <c>movement</c>, a foe preset's) is a movement profile by name
/// (<c>human</c>, <c>neonate</c>, <c>elder</c>) or <c>template</c>: its 1.1 template's <c>ACTOR_WALKSPEED</c> and <c>ACTOR_RUNSPEED</c>,
/// read from the player's install (<see cref="OneOneCreature"/>), its sprint its run, everything else the human profile's. Its brain's
/// <b>approach</b> <c>oneone</c> is 1.1's manoeuvre (<see cref="DuelOneOnes"/>) with the numbers of the template's own AI file, read from
/// the install the same way. Nothing of 1.1's is in our data. The switch <see cref="FoesUniformSwitch"/> gives the earlier build's foes exactly.
/// </summary>
public sealed partial class Session
{
    /// <summary>The switch that gives the earlier build's fighters exactly: every fighter moves as the human, every brain closes as the duelling brain.</summary>
    public const string FoesUniformSwitch = "foes-uniform";

    /// <summary>The movement that is a 1.1 template's own speeds.</summary>
    public const string TemplateMovement = "template";

    /// <summary>The movement a fighter has when nothing is said: the human's, as before.</summary>
    public const string HumanMovement = "human";

    /// <summary>1.1's creatures read for this session's cast and foes, by template (null: the install has none, or no speeds).</summary>
    IReadOnlyDictionary<string, OneOneCreature?> creatures = new Dictionary<string, OneOneCreature?>();

    bool FoesUniform => options.Switches.Contains(FoesUniformSwitch);

    /// <summary>
    /// Whether a session's world needs 1.1's approach registered: a cast fighter's brain or a foe's preset asks for it and the switch
    /// <see cref="FoesUniformSwitch"/> is not given. A world without it is the world earlier, digest for digest.
    /// </summary>
    internal static bool UsesOneOne(SessionOptions options, LoadedGame modules)
    {
        if (options.Switches.Contains(FoesUniformSwitch)) return false;
        if (options.Arena is not null) return ArenaCast.OneOneIn(options.Arena);
        if (options.Foes.Count == 0) return false;
        var presets = FoePresets.From(modules, _ => { });
        foreach (var text in options.Foes)
            if (FoeRequest.TryParse(text, out var r, out _) && presets.TryGetValue(r.Who, out var p) && p.Approach == DuelApproach.OneOne) return true;
        return false;
    }

    /// <summary>The 1.1 creatures a session's cast and foes may ask for, read once with its content (every template named, whether it asks or not).</summary>
    internal static Dictionary<string, OneOneCreature?> ReadCreatures(SessionOptions options, LoadedGame modules, OneOneLibrary library, GameInstall install)
    {
        var templates = new SortedSet<string>(StringComparer.Ordinal);
        if (options.Arena is { } castText)
        {
            try { foreach (var f in ArenaCast.Read(castText).Fighters) templates.Add(f.Template); }
            catch (ArenaSpecException) { }
        }
        if (options.Foes.Count > 0)
        {
            var presets = FoePresets.From(modules, _ => { });
            foreach (var text in options.Foes)
                if (FoeRequest.TryParse(text, out var r, out _) && presets.TryGetValue(r.Who, out var p)) templates.Add(p.Template);
        }
        var read = new Dictionary<string, OneOneCreature?>(StringComparer.Ordinal);
        foreach (var t in templates) read[t] = OneOneCreature.Read(library.Templates, install, t);
        return read;
    }

    OneOneCreature Creature(string who, string template, string what) =>
        creatures.TryGetValue(template, out var c) && c is not null ? c
        : throw new SessionException($"{who}: {what} reads its 1.1 template's speeds and AI file from the install, and {template} " +
                                     (creatures.ContainsKey(template) ? "has no ACTOR_WALKSPEED and ACTOR_RUNSPEED there" : "was not read (no 1.1 install?)"));

    /// <summary>
    /// Gives body <paramref name="e"/> its movement: null or <c>human</c> leaves it as made (the human's, as before); a profile's
    /// name gives that profile (<see cref="MovementProfiles.Give"/>); <c>template</c> its 1.1 template's walk and run, its sprint its run,
    /// everything else the human's — a profile <c>oneone:movement/&lt;template&gt;</c> made once a session. Nothing with
    /// <see cref="FoesUniformSwitch"/>. An unknown name stops the start by name.
    /// </summary>
    void GiveMovement(EntityId e, string who, string template, string? movement)
    {
        if (movement is null || movement == HumanMovement || FoesUniform) return;
        if (!world.TryService<MovementProfiles>(out var profiles) || profiles.Human is not { } human)
            throw new SessionException($"{who}: \"movement\": \"{movement}\", but the game's modules give no movement profiles");
        int index;
        if (movement == TemplateMovement)
        {
            var c = Creature(who, template, "\"movement\": \"template\"");
            var id = AssetId.Of(AssetId.OneOne, "movement/" + template.ToLowerInvariant());
            index = profiles.IndexOf(id.ToString());
            if (index < 0)
            {
                var numbers = human.Numbers with { WalkSpeed = c.WalkMetresPerSecond, RunSpeed = c.RunMetresPerSecond, SprintSpeed = c.RunMetresPerSecond };
                index = profiles.Add(new MovementProfile(id, $"{template}, its 1.1 template's walk and run (read from the install), the human's otherwise", numbers, Sandbox: false)
                    { HalfLife = human.HalfLife });   // its starts and stops the human's spring
            }
        }
        else
        {
            index = profiles.IndexOf(movement);
            if (index < 0) throw new SessionException($"{who}: \"movement\": there is no movement \"{movement}\" (human, neonate, elder: the profiles {profiles.Names}; or template)");
        }
        profiles.Give(world, e, index);
        var l = world.Get<Bodies.Locomotion>(e);
        options.Say(string.Create(CultureInfo.InvariantCulture,
            $"movement: {who} moves as {profiles.All[index].Name}{(movement == TemplateMovement ? $" ({template}'s 1.1 template)" : "")}: walk {l.WalkSpeed:0.##}, run {l.RunSpeed:0.##}, sprint {l.SprintSpeed:0.##} m/s"));
    }

    /// <summary>
    /// A brain's numbers with its approach made ready: with <c>oneone</c>, the creature's AI file's <c>moveRate</c>, <c>chaseDist</c>
    /// and <c>attackFlags</c> read from the install (a template with no AI file stops the start by name); with <see cref="FoesUniformSwitch"/>,
    /// no approach (the duelling brain's own).
    /// </summary>
    DuelBrainSettings Approached(DuelBrainSettings s, string who, string template)
    {
        if (FoesUniform) return s.Approach is null ? s : s with { Approach = null };
        if (s.Approach != DuelApproach.OneOne) return s;
        var c = Creature(who, template, "\"approach\": \"oneone\"");
        if (c.Ai is not { } ai)
            throw new SessionException($"{who}: \"approach\": \"oneone\" reads its 1.1 template's AI file, and {template} has {(c.AiFile is null ? "no ACTOR_AI" : $"{c.AiFile}, which the install does not have")}");
        options.Say(string.Create(CultureInfo.InvariantCulture,
            $"approach: {who} comes on as 1.1's {template} ({c.AiFile}: a step every {ai.MoveSeconds:0.##} s, up to {ai.ChaseMetres:0.##} m, at its {(ai.ClosesAtRun ? "run" : "walk")}; within its striking range the duelling brain)"));
        return s with { OneOneMoveSeconds = ai.MoveSeconds, OneOneStepMostMetres = ai.ChaseMetres, OneOneRuns = ai.ClosesAtRun };
    }
}
