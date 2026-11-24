// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// `GetNSSIndex` and `SetNSSIndex` on a host: the scene
// pane's index, which is the host's (`Net.SceneIndex`). Elsewhere the single-player
// natives (CodexScenes.Progress.cs) answer -1 and move nothing.
using Vitomir.Engine.Net;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    void HostSceneIndex(GameServer server)
    {
        Wrap("CodexSequence.GetNSSIndex", (_, _) => CodexHost.Box(server.SceneIndex.Reached));
        Wrap("CodexSequence.SetNSSIndex", (a, _) => { server.SceneIndex.Set(CodexHost.Int(a, 0)); return null; });
    }
}
