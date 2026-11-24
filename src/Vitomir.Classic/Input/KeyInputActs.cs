// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The commands of ours with no 1.1 key (`KeyBindings.Acted`): the binding layer hands nothing on for
// them and queues the command instead, and the frame loop takes them once its events are read.
using Vitomir.Engine.Ui;

namespace Vitomir;

static partial class KeyInput
{
    static readonly Queue<KeyCommand> acts = new();

    /// <summary>Queues a command the host acts on itself (<see cref="KeyBindings.Acted"/>): the swivel lock, the quick save, the quick load.</summary>
    static void Act(KeyCommand command) => acts.Enqueue(command);

    /// <summary>
    /// The next such command pressed since the last call, in the order they came; null when there are no more.
    /// Pressed under a menu they never come here — the menu has every event as it came (<see cref="Poll"/>).
    /// </summary>
    public static KeyCommand? TakeAct() => acts.TryDequeue(out var c) ? c : null;
}
