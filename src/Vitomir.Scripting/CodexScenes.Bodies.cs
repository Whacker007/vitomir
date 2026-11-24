// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The natives that change what a thing looks like or what hangs off it — the model it is drawn
// with, the weapon in its hand, the things attached to its bones — and the repeating game messages.
using Microsoft.Extensions.Logging;
using System.Numerics;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.World;

namespace Vitomir.Scripting;

public sealed partial class CodexScenes
{
    void RegisterBodies()
    {
        // **Attachment** (all three functions read). The
        // native resolves both things, refuses a bone on a thing with no render model,
        // takes the three floats of the offset, and hands the attach the **child's own orientation**
        // as the Euler angles — so an effect spawned facing the way its bearer faces
        // keeps that turn relative to the bone it rides. The flags go through as the script gave
        // them; every shipped call passes `ATTACH_FLAG_AUTOREMOVE`.
        Engine.On("CodexThing.AttachThing", (self, a) =>
        {
            if (Of(self) is not { } parent || location?.Thing(CodexHost.Int(a, 0)) is not { } child)
                return CodexHost.Box(false);
            int bone = CodexHost.Int(a, 1);
            if (bone != -1 && parent.Animation is null) return CodexHost.Box(false);
            var offset = a.Length > 2 && a[2] is float[] f && f.Length >= 3 ? new Vector3(f[0], f[1], f[2]) : Vector3.Zero;
            bool attached = Attachments.Attach(parent, child, bone, offset, child.Rotation, (uint)CodexHost.Int(a, 3));
            if (attached) global::Vitomir.Engine.Net.HostSeen.Attached(parent, child);                 // on a host, sent as message 0x25
            return CodexHost.Box(attached);
        });
        // Let the child go; one attached to go with its parent is destroyed.
        Engine.On("CodexThing.DetachThing", (self, a) =>
            CodexHost.Box(Of(self) is { } parent && location?.Thing(CodexHost.Int(a, 0)) is { } child
                          && Attachments.Detach(parent, child)));
        // A motion tag (1 to 43) to the bone the thing's motion set puts it on, or −1.
        Engine.On("CodexThing.FindBone", (self, a) =>
        {
            int tag = CodexHost.Int(a, 0);
            if (Of(self)?.Animation is not { } animation || tag <= 0 || tag >= 0x2c) return CodexHost.Box(-1);
            return CodexHost.Box(animation.TagBone(tag));
        });
        // The same tag's offset from its bone — the motion set's three floats — or zeros.
        Engine.On("CodexThing.FindBoneOffset", (self, a) =>
        {
            int tag = CodexHost.Int(a, 0);
            if (Of(self)?.Animation is { } animation && tag > 0 && tag < 0x2c)
                foreach (var t in animation.Set.Tags)
                    if (t.Index == tag) return new[] { t.Offset.X, t.Offset.Y, t.Offset.Z };
            return new float[3];
        });

        // **The model.** `SetModel` swaps what the thing is drawn with and
        // answers false for a name it cannot load; `PreloadModel` only loads one into the
        // cache and lets it go again, answering whether it could.
        Engine.On("CodexThing.SetModel", (self, a) =>
            CodexHost.Box(Of(self) is { } t && location is not null && location.SetModel(t, CodexHost.Str(a, 0) ?? "")));
        Engine.On("Codex.PreloadModel", (_, a) =>
            CodexHost.Box(location?.Files is { } files && location.Mesh.Model(CodexHost.Str(a, 0) ?? "", files) is not null));

        // `EnableActorWeapon(on)`: the weapon sub-model's own hide bit (bit 8 of the weapon
        // sub-model's flags). It is only the picture — the weapon is still in the hand and
        // still swings. The Convent's morning hides Christof's sword while he lies in bed.
        Engine.On("CodexActor.EnableActorWeapon", (self, a) =>
        {
            if (Of(self) is { Type: ThingType.Actor or ThingType.Player, Actor: { } actor } t && t.Animation is not null)
                actor.WeaponHidden = !CodexHost.Bool(a, 0);
        });
        // `OverrideActorWeapon` / `CancelOverrideActorWeapon` are registered
        // with the other natives (CodexScenes.Natives.cs → Rpg.WeaponOverride).
        // `SetActorNoItemArmor(suffix)`: the body shown now — its model's
        // name with the suffix on, or the model's own with "" (World.BodyModels). The name is kept
        // on the actor for the save; the only shipped call puts the default back.
        Engine.On("CodexActor.SetActorNoItemArmor", (self, a) =>
        {
            if (Of(self) is { Type: ThingType.Actor or ThingType.Player, Actor: { } actor } t)
            {
                actor.NoItemArmor = CodexHost.Str(a, 0) ?? "";
                BodyModels.Set(t, actor.NoItemArmor);                               // the body model as the armour leaves it
            }
        });

        // **The repeating game messages** (`Ui.GameMessages`, three of the engine's bugs kept).
        Engine.On("Codex.MessageAddSystem", (_, a) => CodexHost.Box(Session?.Notices.AddSystem(
            CodexHost.Int(a, 0), CodexHost.Int(a, 1), CodexHost.Int(a, 2), CodexHost.Int(a, 3), CodexHost.Int(a, 4),
            location?.Milliseconds ?? 0) ?? 0));
        Engine.On("Codex.MessageAddUser", (_, a) => CodexHost.Box(Session?.Notices.AddUser(
            CodexHost.Str(a, 0) ?? "", CodexHost.Int(a, 1), CodexHost.Int(a, 2), CodexHost.Int(a, 3), CodexHost.Int(a, 4),
            location?.Milliseconds ?? 0) ?? 0));
        Engine.On("Codex.MessageRemoveByGuid", (_, a) => Session?.Notices.RemoveByGuid(CodexHost.Int(a, 0)));
        Engine.On("Codex.MessageRemoveBySystemId", (_, a) => Session?.Notices.RemoveBySystemId(CodexHost.Int(a, 0)));
        Engine.On("Codex.MessageRemoveByStringId", (_, a) => Session?.Notices.RemoveByStringId(CodexHost.Str(a, 0) ?? ""));
    }

    /// <summary>The game the scene belongs to, through the place it is running in.</summary>
    Vitomir.Engine.Session? Session => location?.Session;
}
