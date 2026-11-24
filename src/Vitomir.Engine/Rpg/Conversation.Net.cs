// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A conversation in a network game. In 1.1 a
// conversation a script starts for a player runs **on that player's machine** — the words, the voices,
// the pane and the replies are theirs — and the host keeps a second instance of it in the player's
// slot that runs nothing and only answers the player's machine's reports
// with the script's three callbacks. This is the conversation's half of that: what the
// player's machine reports, and the host's instance firing the callbacks from the reports.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>What a player's machine says about the conversation it is running (1.1's message 0x30).</summary>
public interface IConversationReport
{
    /// <summary>A line has been spoken (kind 0) — only a `LINE:` step, not a delay or a reply offered.</summary>
    void Line(Conversation c, int eventIndex, int stepIndex, int ms, int speakerGuid);
    /// <summary>The player has picked a reply (kind 1): the event and the step it was offered at.</summary>
    void Reply(Conversation c, int eventIndex, int stepIndex, int ms, int speakerGuid);
    /// <summary>It is over (kind 2), with the return code.</summary>
    void Over(Conversation c, bool aborted, int code);
}

public sealed partial class Conversation
{
    /// <summary>
    /// The character the script named as the one being talked to (`ExecuteConversation`'s sixth
    /// argument, the instance's id). The engine only uses it on the host of a network game, to
    /// refuse a second conversation with somebody already in one.
    /// </summary>
    public int Npc { get; set; }

    /// <summary>
    /// On a player's machine, where this conversation's lines, replies and end are reported — to the
    /// host, which fires the script's callbacks. 1.1's client branches of the line, the reply
    /// and the finish send message 0x30 whatever the conversation's flags say; so does this.
    /// </summary>
    public IConversationReport? Report { get; set; }

    /// <summary>
    /// On the host: a player's machine reported a line (kind 0) — <c>convreached(id, event,
    /// step, ms, who)</c>, behind the same <see cref="ConversationFlag.Callbacks"/> gate.
    /// </summary>
    public void HeardLine(int eventIndex, int stepIndex, int ms, int speakerGuid)
    {
        if ((Flags & ConversationFlag.Callbacks) != 0) Reached?.Invoke(Id, eventIndex, stepIndex, ms, speakerGuid);
    }

    /// <summary>
    /// On the host: a player picked a reply (kind 1) — <c>convchosen</c>. **Faithful to an
    /// oddity**: the host's call passes the event and the <em>step</em> the reply was
    /// offered at, where a conversation on the host's own machine passes the event and the reply's
    /// <em>index among the replies</em>. The report carries the answer record's step
    /// and event (filled from the instance's when offered), and the host hands the
    /// two words on as they came. So a script hears a different third number for the same reply
    /// depending on whose machine the conversation ran on.
    /// </summary>
    public void HeardReply(int eventIndex, int stepIndex, int ms, int speakerGuid)
    {
        if ((Flags & ConversationFlag.Callbacks) != 0) Chosen?.Invoke(Id, eventIndex, stepIndex, ms, speakerGuid);
    }

    /// <summary>
    /// On the host: it is over on the player's machine (kind 2): the code is kept
    /// and the finish runs — <c>convended(id, aborted, code)</c>. The host's instance was
    /// never started, so there is nobody to let go of (the end runs only for the host's own).
    /// </summary>
    public void HeardEnd(bool aborted, int code)
    {
        ReturnCode = code;
        if ((Flags & ConversationFlag.Callbacks) != 0) Ended?.Invoke(Id, aborted, code);
    }

    /// <summary>A `LINE:` step reported (kind 0).</summary>
    void ReportLine(int ms, WorldThing? who)
    {
        if (Report is null || EventIndex < 0 || StepIndex < 0 || EventIndex >= Talk.Events.Count) return;
        var steps = Talk.Events[EventIndex].Steps;
        if (StepIndex < steps.Count && steps[StepIndex].Kind == Formats.NcoStep.Line)
            Report.Line(this, EventIndex, StepIndex, ms, Guid(who));
    }
}
