// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// Scripts a thing gets from its **template** rather than from its placement (tag 11 through the
/// template chain), and the two callbacks that came with them: `created`,
/// at a thing's first entry into the world, and `awaken`, when a character
/// wakes up to fight.
/// </summary>
[Collection("scripts")]
public class TemplateScriptTests(Scripts s)
{
    (CodexScenes Scenes, Location Where, GameFiles Files) Enter(string level, string scene, bool minds = false)
    {
        s.Ready();
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var location = Location.Load(files, level, sky: false, scene: scene, player: "Christof");
        var session = new Session { Here = location, Chronicle = set.ForLevel(level)?.Chronicle };
        location.Session = session;
        location.Player!.Thing.Actor!.Flags |= Actor.Flag.Party;
        if (minds) location.Awaken(new AiClasses(files));
        var scenes = new CodexScenes(s.Runtime!) { Chronicles = set };
        scenes.Enter(location);
        return (scenes, location, files);
    }

    [Fact]
    public void The_convent_vault_opens_through_the_script_its_template_carries()
    {
        var (scenes, where, files) = Enter("P1_CTDY", "CTDY_4_1");
        using var _ = files;
        // A click on it did nothing: the placement names no script, the
        // `VAULT` template does.
        var vault = where.Things.Single(t => t.Template.Equals("vault", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("", vault.Scene.Script);
        Assert.Equal("HavenVault", vault.Script);
        int opened = -1;
        scenes.Vault = guid => opened = guid;
        var me = where.Player!.Thing;
        Assert.True(where.Clicked!(vault, me));
        Assert.Equal(me.Guid, opened);
        // The save cross beside it is `SAVEcross`, with `HavenSave`.
        var cross = where.Things.Single(t => t.Template.Equals("SAVEcross", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("HavenSave", cross.Script);
        bool saved = false;
        scenes.SaveRequested = () => saved = true;
        Assert.True(where.Clicked!(cross, me));
        Assert.True(saved);
        scenes.Leave();
    }

    [Fact]
    public void A_template_naming_a_class_file_is_cut_at_its_last_dot()
    {
        Assert.Equal("Door", Location.ScriptName("Door.class"));
        Assert.Equal("Exit0", Location.ScriptName("Exit0.class"));
        Assert.Equal("HavenVault", Location.ScriptName("HavenVault"));
        Assert.Equal("a.b", Location.ScriptName("a.b.c"));
    }

    [Fact]
    public void A_torch_bearer_lights_its_torch_when_it_is_created_and_carries_it_on_a_bone()
    {
        // Anezka carries a torch the night Christof is brought in (`Anezka_torch`, `TorchBearer`):
        // `created` spawns `torchFlame` and attaches it to the bone her motion set puts USER0 on.
        var (scenes, where, files) = Enter("P1_CNVT", "CNVT_3_1");
        using var _ = files;
        var anezka = where.Things.Single(t => t.Template.Equals("Anezka_torch", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("TorchBearer", anezka.Script);
        var flame = where.Things.Single(t => t.Template.Equals("torchFlame", StringComparison.OrdinalIgnoreCase));
        Assert.Same(anezka, flame.AttachedTo);
        Assert.Single(anezka.Attachments);
        // Only once: `beginscene` would light another if `created` had not already.
        Assert.Single(where.Things, t => t.Template.Equals("torchFlame", StringComparison.OrdinalIgnoreCase));
        // It rides the bone: move her, step the attachments, and the flame has gone with her. (Until
        // the first update it sits on her own matrix, which is where the attach leaves a new child.)
        Assert.Equal(anezka.Position, flame.Position);
        Attachments.Update(anezka);
        Assert.NotEqual(anezka.Position, flame.Position);
        var was = flame.Position;
        anezka.Position += new System.Numerics.Vector3(100, 0, 0);
        Attachments.Update(anezka);
        Assert.InRange(flame.Position.X - was.X, 99f, 101f);
        // And when she is gone, so is it — it was attached to go with her.
        Assert.True(where.Remove(anezka, drop: false));
        Assert.True(flame.Removed);
        scenes.Leave();
    }

    [Fact]
    public void A_buried_cappadocian_is_hidden_until_it_wakes_and_its_script_brings_it_up()
    {
        var (scenes, where, files) = Enter("P1_MON2", "MON2_8_2", minds: true);
        using var _ = files;
        var buried = where.Things.First(t => t.Template.Equals("Cappadocian_buried", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("ClassCappadocian", buried.Script);
        Assert.True(buried.ModelHidden);                       // DON'T RENDER, from its template
        where.Minds!.Aggravate(buried.Mind!, where.Player!.Thing);
        // `awaken` reached `ClassCappadocian`, which clears DON'T RENDER and plays MOTION_AWAKEN.
        Assert.False(buried.ModelHidden);
        scenes.Leave();
    }
}
