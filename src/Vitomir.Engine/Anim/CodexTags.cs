// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// An animation's three script tags, `Codex1`..`Codex3`, handed to the thing's scripts as `keytag`.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Anim;

/// <summary>
/// **The keyframe tags a script hears** — the keyframe tag dispatcher's cases
/// 13, 14 and 15 (the SDK's `Codex1`, `Codex2`, `Codex3`): the thing whose animation crossed the
/// tag has `keytag(thingGuid, n, captureId)` sent to its scripts, `n` being
/// the tag less 13 — 0, 1 or 2. Any thing that animates, character or not: the dispatcher's other
/// cases test the thing's type, these do not.
///
/// No shipped class declares `keytag`, so in the shipped game nobody hears it; a mod's class may,
/// and (*"Yes."*) it is sent as 1.1 sends it. The sender's two loops are
/// the scripts' (<c>Ground.Scripts.KeyTag</c>).
/// </summary>
public static class CodexTags
{
    /// <summary>`Codex1` (13), the first of the three.</summary>
    public const uint First = 13;

    /// <summary>`Codex3` (15), the last (case 0xf).</summary>
    public const uint Last = 15;

    /// <summary>Whether a tag is one of the three.</summary>
    public static bool Is(NadTag tag) => tag.Type is >= First and <= Last;

    /// <summary>
    /// A thing's animation crossed a tag: one of the three is sent to its scripts and answers true;
    /// any other tag is left to its own handler and answers false.
    /// </summary>
    public static bool Crossed(WorldThing thing, NadTag tag)
    {
        if (!Is(tag)) return false;
        thing.Place?.ItemScripts?.KeyTag?.Invoke(thing, (int)(tag.Type - First));   // The tag less 13
        return true;
    }
}
