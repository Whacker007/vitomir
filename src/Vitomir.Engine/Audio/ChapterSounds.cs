// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A chapter swap's sounds (World/ChapterSwap.cs): the old chapter's off, the new one's on.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Audio;

public sealed partial class GameAudio
{
    /// <summary>The place whose placed sounds are hung on the sectors now — the one last entered.</summary>
    public Location? Bound { get; private set; }

    /// <summary>
    /// A chapter torn down: its sector and point sounds taken off their sectors and
    /// each one's channel stopped. The level scene's stay, playing.
    /// </summary>
    public void Unbind(NsdScene scene)
    {
        foreach (var list in sectorSounds.Values)
        {
            foreach (var s in list) if (ReferenceEquals(s.From, scene)) Mixer.Stop(s.Channel);
            list.RemoveAll(s => ReferenceEquals(s.From, scene));
        }
        foreach (var list in pointSounds.Values)
        {
            foreach (var p in list) if (ReferenceEquals(p.From, scene)) Mixer.Stop(p.Channel);
            list.RemoveAll(p => ReferenceEquals(p.From, scene));
        }
    }

    /// <summary>
    /// A chapter loaded into the place being heard: its sounds hung on their sectors. A
    /// sector sound starts as the listener next comes into its sector, a point sound as the
    /// listener is within reach — the swap starts nothing itself, and the music and the
    /// ambient are the scene start's, not the swap's.
    /// </summary>
    public void Rebind(NsdScene scene) => Bind(scene);
}
