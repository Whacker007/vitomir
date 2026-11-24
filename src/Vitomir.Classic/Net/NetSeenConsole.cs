// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Three host commands (ours) for seeing what each machine sees of the others without a chronicle's
// script to do it: a worn item, an effect and a blow on a player's character, each made on the host
// by the same functions the game uses, so what reaches the players' machines is what a script's or a
// fight's would. Typed as a chat line on a windowed host (`--host`) or on a headless one's console.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Net;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir;

static class NetSeenConsole
{
    /// <summary>The commands' one-line help.</summary>
    public const string Help = "/wear <slot> <item template>, /effect <slot> <effect> [seconds] [value], /hit <slot> [amount] [type] [by <slot>], /rename <slot> <name>";

    static readonly Rng Dice = new(7);

    /// <summary>
    /// `/wear &lt;slot&gt; &lt;template&gt;` — the item made in that slot's character's pack and put on,
    /// as a double-click does; `/effect &lt;slot&gt; &lt;name&gt; [seconds]` — the effect put on
    /// at level 0 — or by value, as `AddActorEffectByValue` does — for that long or
    /// for good; `/hit &lt;slot&gt; [amount] [type] [by &lt;slot&gt;]` — a blow of that much damage, normal
    /// unless a damage type is named (`fire`, `electric`, `cold`, … — to see a player's machine make a burn's
    /// or a shock's own sparks),
    /// unsoaked, from **nobody** unless a character is named with `by` (a test command blames
    /// nobody, so no town's guards or witnesses take the host's character for a killer — a
    /// `/hit 1 1000` on a windowed host brought them down on it). Slot 0 is the host's own. Answers whether
    /// the line was one of these.
    /// </summary>
    public static bool Command(GameServer server, string line, Action<string> say)
    {
        var words = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // `/jump` on either host's chat line (it was the headless host's console's alone).
        if (words.Length > 0 && words[0] == "/jump") { say(NetPlaces.Jump(server, words)); return true; }
        if (words.Length < 2 || words[0] is not ("/wear" or "/effect" or "/hit" or "/rename")) return false;
        if (!int.TryParse(words[1], out int slot) || server.Slots.ElementAtOrDefault(slot)?.Avatar is not { Actor: { } a, Place: { } place } body)
        {
            say($"  {words[0][1..]}: no such player");
            return true;
        }
        string who = server.Slots[slot]!.Name;
        switch (words[0])
        {
            case "/wear" when words.Length > 2:
                if (place.Make(words[2]) is not { Item: { } item } made) { say($"  wear: no item template {words[2]}"); break; }
                if (place.Effects is not { } effects) { say("  wear: the place has no effects table"); break; }
                made.Owner = body;
                a.Inventory.Add(item);
                var result = Equipment.Equip(body, item, Slot.None, effects, place.Milliseconds);
                if (result != EquipResult.Equipped) Stow.Place(a, item, Slot.Inventory);
                say($"  wear: {words[2]} on {who} — {result}");
                break;
            case "/effect" when words.Length > 2:
                int ms = words.Length > 3 && float.TryParse(words[3], System.Globalization.NumberStyles.Float,
                                                            System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                    ? (int)(seconds * 1000) : 0;
                float? value = words.Length > 4 && float.TryParse(words[4], System.Globalization.NumberStyles.Float,
                                                                  System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
                var on = EffectRuntime.Apply(body, words[2], 0, durationMs: ms, value: value);
                say($"  effect: {words[2]} on {who} — {(on is null ? "not put on" : $"instance {on.Guid}, {on.Value}")}");
                break;
            case "/hit":
                float amount = words.Length > 2 && float.TryParse(words[2], System.Globalization.NumberStyles.Float,
                                                                 System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 5f;
                // The attacker only when named: `by <slot>` (a blow from nobody is the damage's own case).
                int byAt = Array.FindIndex(words, w => w.Equals("by", StringComparison.OrdinalIgnoreCase));
                WorldThing? by = null;
                if (byAt >= 0)
                {
                    if (byAt + 1 >= words.Length || !int.TryParse(words[byAt + 1], out int bySlot)
                        || server.Slots.ElementAtOrDefault(bySlot)?.Avatar is not { } striker || striker == body)
                    {
                        say("  hit: no such attacker");
                        break;
                    }
                    by = striker;
                }
                var type = words.Length > 3 && Enum.TryParse<DamageType>(words[3], ignoreCase: true, out var named)
                           && !int.TryParse(words[3], out _) ? named : DamageType.Normal;
                var harm = Damage.Apply(body, by, HitKind.Effect, type, amount, Dice, place.Milliseconds,
                                        skipSoak: true, motions: place.Motions);
                say($"  hit: {who} takes {harm.Taken:0.#}{(by is null ? "" : $" from {server.Players.First(p => p.Avatar == by).Name}")}" +
                    $"{(harm.Killed ? " and dies" : harm.Refused ? " (refused)" : "")}");
                break;
            // 1.1's console `rename <name>`: the Storyteller's current target, a character, takes
            // the name as its own and is marked renamed (its flag 0x800) — on a host, in a game with
            // Storyteller moderation only. Here by slot (ours). Nothing is sent: other machines learn the
            // name when the character is next announced to them, a newcomer in its catch-up (0x53).
            case "/rename" when words.Length > 2:
                if (!server.Flags.HasFlag(GameFlags.Moderated)) { say("  rename: only in a game with Storyteller moderation"); break; }
                string name = string.Join(' ', words[2..]);
                if (name.Length > 79) name = name[..79];
                body.DescriptionId = name;
                a.Flags2 |= HostSeen.Renamed;
                say($"  rename: {who} is now {name}");
                break;
            default:
                say($"  {Help}");
                break;
        }
        return true;
    }
}
