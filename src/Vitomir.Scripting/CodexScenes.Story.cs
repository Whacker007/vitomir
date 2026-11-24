// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The one call into the scripts the Storyteller makes: the chronicle's `advance(scene, subscene)`.
namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    /// <summary>
    /// The Storyteller's Advance and Skip To: every
    /// chronicle script's `advance(scene, subscene)` — the method `MP_CTDChronicle` moves its story
    /// with. False when there is no chronicle script or it has no such method.
    /// </summary>
    public bool AdvanceChronicle(int scene, int subscene)
    {
        if (chronicle is null) return false;
        try
        {
            return runtime.TryInvoke(chronicle, "advance", "(II)V", [CodexHost.Box(scene), CodexHost.Box(subscene)], out _);
        }
        catch (Exception e)
        {
            Engine.Log($"advance({scene}, {subscene}) failed: {ScriptException.From(ChronicleName ?? "chronicle", e).JavaType}");
            return false;
        }
    }
}
