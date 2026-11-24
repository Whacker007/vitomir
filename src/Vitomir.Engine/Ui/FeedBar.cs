// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The feed bar: while the character you are steering is feeding, a thin red bar in
// the bottom left corner empties with the **victim's** blood, and a looping drain sound follows it
// down through three speeds. One routine loads it, another runs it every frame.
using Vitomir.Engine.Audio;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>The feed bar's per-frame state: whether the bar is up, where, how full, and which loop is playing.</summary>
public sealed class FeedBar
{
    /// <summary>`ui/feedBar.nui` and its one progress control.</summary>
    public const string PaneName = "feedBar", Progress = "feedProgress";

    /// <summary>The three loops, fast to slow.</summary>
    public static readonly string[] Loops =
        ["blood_drain_fast_lp.wav", "blood_drain_medium_lp.wav", "blood_drain_slow_lp.wav"];

    /// <summary>
    /// Where the pane goes, in the interface's y-up space: (2, 4) in the bottom left corner, or
    /// (2, 131) — just above the status bar — while the bar is up (`SetPos(2, 0x83)`).
    /// </summary>
    public const int X = 2, Y = 4, YWithBar = 131;

    /// <summary>The bands the loop changes at: half the pool and above is fast, a quarter medium, below slow.</summary>
    public const float FastFrom = 0.5f, MediumFrom = 0.25f;

    /// <summary>Whether the pane is up.</summary>
    public bool Shown { get; private set; }
    /// <summary>The pane's position this frame.</summary>
    public int Left { get; private set; } = X;
    /// <inheritdoc cref="Left"/>
    public int Bottom { get; private set; } = Y;
    /// <summary>The victim's blood and pool: the progress control's value and range.</summary>
    public float Blood { get; private set; }
    /// <inheritdoc cref="Blood"/>
    public float Pool { get; private set; }
    /// <summary>The band whose loop is playing, 0 fast, 1 medium, 2 slow, or −1 for none.</summary>
    public int Band { get; private set; } = -1;

    int channel;

    /// <summary>
    /// One frame of the bar. Only the character being steered, and only while it is a feeder
    /// (neck or wrist, action 8 or 11): the victim — its action target — must be a character, and
    /// then the bar reads its blood out of its pool and the loop is chosen by that fraction, changed
    /// only when the band changes. Anything else takes the pane down and stops the loop.
    ///
    /// Two of the engine's own edges are kept: a feeder whose target is gone or is not a character
    /// leaves the pane as it was, neither updated nor hidden; and a loop that cannot be started
    /// leaves no band, so the next frame tries again.
    /// </summary>
    public void Update(WorldThing? steered, bool barShown, GameAudio? audio)
    {
        if (steered?.Actor is not { Action: ActionMode.Feeding or ActionMode.WristFeeding } me)
        {
            Shown = false;
            StopLoop(audio);
            return;
        }
        if (me.Target is not { Type: ThingType.Actor or ThingType.Player, Actor: { } victim }) return;
        Shown = true;
        Blood = victim.Stat[(int)ActorStat.Blood];
        Pool = victim.Stat[(int)ActorStat.BloodPool];
        float fraction = Pool > 0f ? Blood / Pool : 0f;
        int band = fraction >= MediumFrom ? fraction >= FastFrom ? 0 : 1 : 2;
        if (band != Band)
        {
            StopLoop(audio);
            // Pan 0 — hard left: every other flat play passes 0.5
            // for the centre. Kept as the engine passes it.
            if (audio?.Bank.Load(Loops[band]) is { } loop
                && audio.Mixer.Play2D(loop, 1f, 0f, ChannelFlag.Looping) is var id and not 0)
            {
                channel = id;
                Band = band;
            }
        }
        Left = X;
        Bottom = barShown ? YWithBar : Y;
    }

    /// <summary>How full the bar is, 0 to 1.</summary>
    public float Fraction => Pool > 0f ? Math.Clamp(Blood / Pool, 0f, 1f) : 0f;

    void StopLoop(GameAudio? audio)
    {
        if (channel != 0) audio?.Mixer.Stop(channel);
        channel = 0;
        Band = -1;
    }
}
