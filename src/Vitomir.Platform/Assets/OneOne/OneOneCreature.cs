// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Formats;

namespace Vitomir.Platform.Assets.OneOne;

/// <summary>
/// How a 1.1 character moves and closes, read from the player's install at run time (nothing of it is in our data):
/// its template's <c>ACTOR_WALKSPEED</c> and <c>ACTOR_RUNSPEED</c> (tags 54 and 55, units a second, at 72 to the metre) and its AI
/// file (<c>ACTOR_AI</c>, tag 56, from <c>misc/ai/</c>, as 1.1 finds it: the name lower-cased and cut to 31 characters) with the
/// numbers its enemy's think reads — <c>moveRate</c> and <c>attackRate</c> (seconds), <c>chaseDist</c> (metres) and
/// <c>attackFlags</c> bit 0 (close at its run). <see cref="Ai"/> is null when the template names no AI file or the install has none.
/// </summary>
public sealed record OneOneCreature(string Template, float WalkMetresPerSecond, float RunMetresPerSecond, string? AiFile, OneOneAiNumbers? Ai)
{
    /// <summary>The template's tags: <c>ACTOR_WALKSPEED</c>, <c>ACTOR_RUNSPEED</c>, <c>ACTOR_AI</c>.</summary>
    public const uint WalkSpeedTag = 54, RunSpeedTag = 55, AiTag = 56;

    /// <summary>Where 1.1 finds its AI files (file category 0x10).</summary>
    public const string AiFolder = "misc/ai/";

    /// <summary>
    /// The creature of template <paramref name="template"/> (its base chain flattened, as 1.1 does), or null when the template is not
    /// in the install or gives no walk and run speed.
    /// </summary>
    public static OneOneCreature? Read(OneOneTemplates templates, GameInstall install, string template)
    {
        var tags = templates.Resolve(template);
        if (tags.Count == 0 || !tags.TryGetValue(WalkSpeedTag, out var walk) || !tags.TryGetValue(RunSpeedTag, out var run)) return null;
        float walkUnits = walk.AsFloat(), runUnits = run.AsFloat();
        if (!(walkUnits > 0f) || !(runUnits > 0f)) return null;
        string? aiName = tags.TryGetValue(AiTag, out var ai) ? ai.AsString() : null;
        OneOneAiNumbers? numbers = null;
        if (!string.IsNullOrEmpty(aiName) && install.TryRead(AiFolder + NaiFile.CacheName(aiName), out var data))
        {
            var nai = NaiFile.Read(data, aiName);
            numbers = new OneOneAiNumbers(
                (float)(nai.MoveRate / NaiFile.RateScale), (float)(nai.AttackRate / NaiFile.RateScale),
                nai.ChaseDist / OneOneModels.UnitsPerMetre, (nai.AttackFlags & 1u) != 0, nai.AttackAlt);
        }
        return new OneOneCreature(template, walkUnits / OneOneModels.UnitsPerMetre, runUnits / OneOneModels.UnitsPerMetre, aiName, numbers);
    }
}

/// <summary>A 1.1 AI file's numbers its enemy's think reads: <c>moveRate</c>, <c>attackRate</c> (s), <c>chaseDist</c> (m), <c>attackFlags</c> bit 0, <c>attackAlt</c>.</summary>
public sealed record OneOneAiNumbers(float MoveSeconds, float AttackSeconds, float ChaseMetres, bool ClosesAtRun, float AttackAlt);
