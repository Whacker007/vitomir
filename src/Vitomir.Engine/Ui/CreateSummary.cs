// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The multiplayer create screen's summary, read: its last step,
// the clan list shown only for a human.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Ui;

/// <summary>
/// **The create screen's summary**, run whenever the new character changes — a type box ticked,
/// a model chosen, a clan chosen. It writes the five boxes under the lists:
/// the type (`UI_GENERAL_VAMPIRE`, `…_GHOUL`, `…_HUMAN` by the actor's type), the clan's name (`%s` of the clan record's
/// name) and the generation (`%d`, the float truncated) — both `UI_GENERAL_NOTAPPLICABLE` for a human — the
/// status (the rank's title) and the experience (`%d` of the lifetime experience); `MpMenus.Describe` writes those. And then
/// **the clan list** (`mppcClanList`): for a human it is made only shown
/// (no row hovered or pressed, and the chosen row forgotten, <see cref="MenuList.Inert"/>), for a vampire or a
/// ghoul choosable again (no row chosen until one is clicked; the character keeps the clan
/// it had). Earlier, Vitomir's clan list could still be clicked for a human (which 1.1's Create then drops when it
/// writes the character's save).
/// </summary>
public static class CreateSummary
{
    /// <summary>The clan list made only shown for a human and choosable otherwise.</summary>
    public static void ClanList(MenuList clans, ActorType type) => clans.Inert = type == ActorType.Human;
}
