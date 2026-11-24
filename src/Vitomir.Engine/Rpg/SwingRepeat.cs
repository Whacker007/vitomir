// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The end of a swing's question — is the player still asking for another? — as 1.1 asks it:
// the swing's end, with the Controls screen's *auto repeat attack* box that
// gates it.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// What the player is asking for at the end of a swing, which the host works out once a
/// frame from the input and leaves on <see cref="Actor.Repeating"/> for <c>Actions</c> to read when
/// the swing is over:
/// <list type="bullet">
/// <item>nothing at all unless the options word's **repeat attack** bit (0x4) is set —
/// the Controls screen's *auto repeat attack* — and the character is the one being steered and is
/// answering to orders;</item>
/// <item>the target is **whatever is under the pointer now** (the rollover pass's own
/// answer), not the one first clicked, and it must still be something to attack (verb 2);</item>
/// <item>Space held asks for the hold attack (mask 8);</item>
/// <item>with the **left button** held, the **first** attack — 1.1 asks for the second when
/// one of the key-state array's entries for DirectInput
/// codes 0xA0 and 0xA1 (Mute and Calculator): the values of Windows' `VK_LSHIFT` and `VK_RSHIFT`
/// tested against an array indexed by scan code. So a held Shift+click swings its second attack
/// once and its first after that, **and Vitomir does too** — unless the quality of life <c>heldshift</c> is on (as it is by default)
/// (<c>heldShift</c>), which asks for the second while Shift is held;</item>
/// <item>with the **middle button** held (1.1's third binding), the
/// **second** attack.</item>
/// </list>
/// Neither held, or the option off: nobody is asking, and the swing's end cancels.
/// </summary>
public static class SwingRepeat
{
    public static Actions.Asking? Ask(bool repeatOption, bool answering, WorldThing? underPointer,
                                      bool leftHeld, bool middleHeld, InputModifier mods, bool heldShift = false)
    {
        if (!repeatOption || !answering || underPointer is null) return null;
        bool hold = (mods & InputModifier.Space) != 0;
        if (leftHeld) return new Actions.Asking(underPointer, heldShift && (mods & InputModifier.Shift) != 0 ? 1 : 0, hold);
        if (middleHeld) return new Actions.Asking(underPointer, 1, hold);
        return null;
    }
}

/// <summary>
/// **The pick under a still pointer while a click is held**. 1.1 picks what is under the pointer
/// every frame (the hover writes it afresh each time it runs), so the end of a
/// swing asks about whatever is under the pointer *now*: an enemy that steps out from under
/// a still pointer, or a game camera that follows the character round under it, ends the fight. Vitomir
/// picks on a mouse move (ours), which left the old enemy under a pointer that had
/// not moved, and the held button kept closing in on it. So while a click is held — the only time the
/// answer feeds a swing or a held stream of fire — the host re-runs the pick each frame, in the game
/// camera only (the hover casts no ray in any other) and not with a menu up.
/// </summary>
public static class HeldPick
{
    /// <summary>Whether the pick is re-run this frame.</summary>
    public static bool Wanted(bool clickHeld, bool menuUp, bool gameCamera) => clickHeld && !menuUp && gameCamera;
}
