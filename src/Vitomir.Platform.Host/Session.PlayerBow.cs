// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Host;

// Ours: the player's bow in the sandbox, the bow's 1.1 look (its kind, read from the install at run time), and the
// arrows drawn with 1.1's arrow model. Nothing of 1.1's is in the repository: the templates, clips and models are read from the install.

/// <summary>How a bow is drawn and posed with 1.1's own (<see cref="BowLook"/>), and what of it is read from the install.</summary>
public static class BowLooks
{
    /// <summary>The 1.1 template the war bow is drawn as (darkAges.not's <c>longBow</c>: carry class 7, the shot mode 72 <c>bow</c>, held <c>bow_0</c>, its projectile <c>arrow</c>, <c>arrow.nod</c>).</summary>
    public const string OneOneTemplate = "longBow";

    /// <summary>The switch that keeps the earlier build's archer (his melee stance and sword).</summary>
    public const string BowLookOffSwitch = "bow-look-off";

    /// <summary>1.1's template tags read here: a weapon's projectile (<c>WEAPON_PROJECTILE</c>) and a template's model (<c>MODEL</c>).</summary>
    const uint ProjectileTag = 50, ModelTag = 4;

    /// <summary>
    /// <paramref name="template"/> carrying 1.1's bow (<see cref="OneOneTemplate"/>), its held model kept for drawing but its things
    /// (1.1's bow fitted as a shield on the bow hand and a blade on the draw hand) dropped, so contact never meets the bow as a shield
    /// or a blade; null when the install has no such character or bow.
    /// </summary>
    public static OneOneCharacter? Character(OneOneLibrary library, string template) => Character(library, template, ready: false);

    /// <summary>
    /// <see cref="Character(OneOneLibrary, string)"/>, its shot composed with the ready pose (<see cref="BowReadyClip"/>,
    /// from 1.1's own clip as read: the bow up near vertical, the draw hand on the string) when <paramref name="ready"/> (not with
    /// <c>bow-aim-off</c>, the look: 1.1's clip as it is).
    /// </summary>
    public static OneOneCharacter? Character(OneOneLibrary library, string template, bool ready)
    {
        var c = library.Character(template, OneOneTemplate);
        if (c is null || c.Weapon is null) return null;
        var actions = c.Actions;
        if (ready && actions is { Quick.Count: > 0 } set && c.Limbs is { LeftArm.Has: true, RightArm.Has: true } limbs
            && BowShot.Find(set.Quick[0].Clip, c.Model.Skeleton, limbs, set.Quick[0].StrikeSeconds, System.Numerics.Matrix4x4.Identity) is { } found)
        {
            var composed = new BowReadyClip(set.Quick[0].Clip, c.Model.Skeleton, limbs.LeftArm, limbs.RightArm, found.DrawStart, found.FullDraw);
            actions = set with { Quick = [set.Quick[0] with { Clip = composed }, .. set.Quick.Skip(1)] };
        }
        return new OneOneCharacter
        {
            Name = c.Name, Template = c.Template, Model = c.Model, Textures = c.Textures, MotionSet = c.MotionSet, Roles = c.Roles,
            HeadBone = c.HeadBone, Scale = c.Scale, Limbs = c.Limbs, Compositions = c.Compositions, SkeletonId = c.SkeletonId, Weapon = c.Weapon,
            Actions = actions, Held = [.. c.Held.Select(h => new HeldModel { Template = h.Template, Model = h.Model, Things = [] })],
            Stitched = [], Flat = [], WindUps = [], LentLeft = null,
        };
    }

    /// <summary>
    /// The bone the arrow of 1.1's held bow model rides (the model is the bow and an arrow nocked on it: the arrow's
    /// vertices on the draw hand, read as a blade when the model is fitted), or null. The picture hides it while no arrow is on the string.
    /// </summary>
    static int? ArrowBone(OneOneLibrary library, string template)
    {
        var c = library.Character(template, OneOneTemplate);
        if (c is null) return null;
        foreach (var h in c.Held)
            foreach (var t in h.Things)
                if (t.Kind == HeldKind.Shield) return t.Bone;
        return null;
    }

    /// <summary>The arrow's model: the bow template's projectile's (<c>arrow</c> → <c>arrow.nod</c>), or null.</summary>
    public static SkinnedModel? Arrow(OneOneLibrary library)
    {
        var bow = library.Templates.Resolve(OneOneTemplate);
        if (!bow.TryGetValue(ProjectileTag, out var p) || p.AsString() is not { Length: > 0 } projectile) return null;
        var arrow = library.Templates.Resolve(projectile);
        return arrow.TryGetValue(ModelTag, out var m) && m.AsString() is { Length: > 0 } model ? library.Model(model) : null;
    }

    /// <summary>The key a template's bow kind is kept under in the content's kinds.</summary>
    public static string KindKey(string template) => ArenaCast.KindKey(template, OneOneTemplate);

    /// <summary>
    /// The bow kinds a session needs, loaded into <paramref name="assets"/> and <paramref name="kinds"/> (the player's Christof in the
    /// sandbox with a bow; each cast fighter with one), and the arrow's model; nothing with <c>bow-look-off</c>.
    /// </summary>
    public static SkinnedModel? Load(SessionOptions options, OneOneLibrary library, CharacterAssets assets, Dictionary<string, int> kinds,
                                     Dictionary<string, TextureImage> textures, Action<string> say, IEnumerable<string>? more = null, Dictionary<int, int>? arrowBones = null)
    {
        var templates = new List<string>();
        if (options.Arena is null && options.PlayerBow is not null) templates.Add("Christof");
        foreach (var t in more ?? []) if (!templates.Contains(t)) templates.Add(t);   // the archer foes'
        bool ready = !Session.PlayerBowAimFor(options).NoAim;                       // the ready pose, unless bow-aim-off
        if (options.Arena is { } castText)
        {
            try { foreach (var f in ArenaCast.Read(castText).Fighters) if (f.Ranged is not null && !templates.Contains(f.Template)) templates.Add(f.Template); }
            catch (ArenaSpecException) { }
        }
        if (templates.Count == 0) return null;
        var arrow = Arrow(library);
        if (arrow is not null) foreach (var t in library.TexturesOf(arrow)) textures.TryAdd(t.Name, t);
        if (options.Switches.Contains(BowLookOffSwitch)) { say("bow: bow-look-off: the archer keeps his melee stance and sword"); return arrow; }
        foreach (var template in templates)
        {
            var c = Character(library, template, ready);
            if (c is null) { say($"bow: {template} with 1.1's {OneOneTemplate} could not be read; the archer keeps his melee look"); continue; }
            int bowKind = kinds[KindKey(template)] = OneOneLibrary.Register(assets, c);
            if (arrowBones is not null && ArrowBone(library, template) is int ab) arrowBones[bowKind] = ab;   // the arrow in 1.1's bow model
            foreach (var t in c.Textures) textures.TryAdd(t.Name, t);
            var shot = BowShot.Of(assets, bowKind);
            say($"bow: {template} drawn with 1.1's {OneOneTemplate} (held {string.Join(", ", c.Held.Select(h => h.Model.Name))}): " +
                $"{string.Join(", ", new[] { MotionRole.Idle, MotionRole.Walk, MotionRole.Run }.Select(r => $"{c.Role(r).Mode} {c.Role(r).Clip?.Name}"))}, " +
                $"the shot {c.Actions?.Quick.FirstOrDefault()?.Mode} {(c.Actions?.Quick.FirstOrDefault()?.Clip as NadClip)?.Name}" +
                (shot is null ? "" : shot.Ready is { } rc
                    ? string.Create(CultureInfo.InvariantCulture, $" composed with the ready pose (the bow up from 1.1's full draw at frame {rc.FullDrawAt * 30f:0}, the draw hand on the string at {BowReadyClip.RestShare:0.##} of the draw{(rc.RestMiss > 1e-3f ? $", {rc.RestMiss:0.###} short" : "")}; raised in {rc.ReadyAt:0.##} s of its time, loosed at {shot.Release:0.###} s; its arrow {shot.AimTurn * DetMath.RadToDeg:0.0}° off the facing, the archer turned by it)")
                    : string.Create(CultureInfo.InvariantCulture, $" (drawn from frame {shot.DrawStart * 30f:0} to {shot.FullDraw * 30f:0}, loosed at {shot.Release * 30f:0.#}; its arrow {shot.AimTurn * DetMath.RadToDeg:0.0}° off the facing, the archer turned by it)")) +
                $", hurt {c.Actions?.Hurt?.Mode}; the arrow {(arrow is null ? "drawn as a line (no model)" : arrow.Name)}");
        }
        return arrow;
    }
}

public sealed partial class Session
{
    /// <summary>The arrows' model (1.1's <c>arrow.nod</c>), or null (drawn as lines).</summary>
    SkinnedModel? arrowModel;

    /// <summary>The bow kinds' arrow bone in 1.1's bow model (hidden while nothing is on the string; presentation only).</summary>
    IReadOnlyDictionary<int, int> bowArrowBones = new Dictionary<int, int>();

    /// <summary>The held bow model's own arrow hidden (its bone's vertices collapsed) while <paramref name="e"/>'s bow has nothing on the string.</summary>
    void HideBowModelArrow(EntityId e, int kind, DrawnCharacter held)
    {
        if (!bowArrowBones.TryGetValue(kind, out int bone) || !world.TryStore<Bow>(out var bows) || !bows.Has(e) || bows.Get(e).OnString) return;
        if ((uint)bone < (uint)held.Skin.Length) held.Skin[bone] = default;
        if (held.ShadowSkin is { } shadow && (uint)bone < (uint)shadow.Length) shadow[bone] = default;
    }

    /// <summary>When each arrow was first seen stopped (seconds; presentation only).</summary>
    readonly Dictionary<ulong, double> arrowStopped = [];
    readonly List<(ulong Entity, double At)> arrowOrder = [];
    readonly Dictionary<ulong, DrawnCharacter> drawnArrows = [];
    readonly HashSet<ulong> arrowsShown = [];

    /// <summary>The view's zoom at full draw (a share, 0 to 0.5; the setting <c>bowZoomPercent</c>, 0 by default). Presentation only.</summary>
    public float BowZoom { get; set; }

    /// <summary>The field's factor now: 1 / (1 + <see cref="BowZoom"/> × the player's draw share), 1 with no zoom or no draw.</summary>
    float BowZoomFactor()
    {
        if (BowZoom <= 0f || !world.Alive(player) || !world.TryStore<Bow>(out var bows) || !bows.Has(player)) return 1f;
        var bow = bows.Get(player);
        return bow.InHand && bow.Drawn ? 1f / (1f + BowZoom * Math.Clamp(bow.Share, 0f, 1f)) : 1f;
    }

    /// <summary>How many stopped arrows are drawn at most, the newest (ENGINE_SANDBOX, ours: presentation only; the simulation keeps them all).</summary>
    public const int StoppedArrowsShown = 30;

    /// <summary>How long a stopped arrow stays drawn where it stopped (s; ENGINE_SANDBOX, ours: presentation only).</summary>
    public const double StoppedArrowSeconds = 60;

    /// <summary>The length of 1.1's arrow model along its shaft, head forward (+Y in the model; m).</summary>
    const float ArrowHeadAlongY = 0.527f;

    /// <summary>The player's bow in the sandbox — the world registers the bow when the player carries one (not in an arena or a scenario: their casts say).</summary>
    static bool PlayerBowIn(SessionOptions options) => options.Arena is null && options.PlayerBow is not null;

    /// <summary>
    /// What of the earlier build's a bow world registers: the player's swap and nock when a controlled body may carry the bow (the
    /// sandbox's player, a scenario's input actor), the bow's 1.1 look unless <c>bow-look-off</c>, the shield raised against a drawn bow
    /// unless <c>shield-at-bow-off</c>. With both switches and no player's bow the world is the earlier build's bow world, digest for digest.
    /// </summary>
    static BowExtras BowExtrasFor(SessionOptions options, WorldBuilder b) =>
        (PlayerBowIn(options) || (b.Peek.TryService<ScenarioDirector>(out var director) && director.Program.Input is not null) ? BowExtras.PlayerHand : BowExtras.None)
        | (options.Switches.Contains(BowLooks.BowLookOffSwitch) ? BowExtras.None : BowExtras.Look)
        | (options.Switches.Contains(ShieldAgainstBowSettings.OffSwitch) ? BowExtras.None : BowExtras.Shield);

    /// <summary>The player's bow's rules — the options' (a tape's), else from the switches <c>bow-aim-off</c> and <c>bow-solve-on</c>.</summary>
    internal static PlayerBowSettings PlayerBowAimFor(SessionOptions options) => options.PlayerBowAim ?? new PlayerBowSettings
    {
        NoAim = options.Switches.Contains(PlayerBowSettings.NoAimSwitch),
        Solve = options.Switches.Contains(PlayerBowSettings.SolveOnSwitch),
    };

    /// <summary>The bow's switches applied once the world is built (the shield against a drawn bow off with <c>shield-at-bow-off</c>).</summary>
    static void BowSwitches(World world, SessionOptions options)
    {
        if (world.TryService<ShieldAgainstBowSettings>(out var shield)) shield.On = !options.Switches.Contains(ShieldAgainstBowSettings.OffSwitch);
    }

    /// <summary>Gives the sandbox's player the bow (put away, its arrows) and its 1.1 look; says so.</summary>
    void GivePlayerBow(IReadOnlyDictionary<string, int> kinds)
    {
        if (!PlayerBowIn(options) || !world.Alive(player)) return;
        if (!BowServices.Give(world, player, options.PlayerBow!, options.PlayerArrows))
        {
            options.Say($"bow: no bow \"{options.PlayerBow}\" in the module's data; the player carries none");
            return;
        }
        LookTheBow(player, kinds, "Christof");
        string rules = PlayerBowAimFor(options) is { NoAim: true } ? "attack held draws, released looses at the crosshair (bow-aim-off); block or cancel lets it down"
            : $"aim held (or toggled) raises it, attack held while aiming draws, released looses {(PlayerBowAimFor(options).Solve ? "at the angle that meets the crosshair's point (bow-solve-on)" : "along the line to the crosshair's point, dropping as a real arrow")}; block or cancel lets it down, aim let go lowers it";
        options.Say(string.Create(CultureInfo.InvariantCulture,
            $"bow: the player carries {options.PlayerBow} and {options.PlayerArrows} arrows beside his sword: swapWeapon (F) swaps them in {world.Service<BowConfig>().Bows[world.Get<Bow>(player).Weapon].SwapSeconds:0.##} s; {rules}"));
    }

    /// <summary>A body with a bow posed and drawn with its template's bow kind while the bow is in hand (unless <c>bow-look-off</c>).</summary>
    void LookTheBow(EntityId e, IReadOnlyDictionary<string, int> kinds, string template)
    {
        if (!world.TryStore<BowLook>(out _) || !world.Has<CharacterAnimation>(e) || !kinds.TryGetValue(BowLooks.KindKey(template), out int ranged)) return;
        world.Set(e, new BowLook { MeleeKind = world.Get<CharacterAnimation>(e).Kind, RangedKind = ranged });
    }

    /// <summary>
    /// The arrows drawn with 1.1's model (when read): every arrow in flight along its velocity, and the newest
    /// <see cref="StoppedArrowsShown"/> stopped ones for <see cref="StoppedArrowSeconds"/> where they stopped (moving with a body, shield or
    /// blade they are stuck in); without the model, as before lines. Presentation only.
    /// </summary>
    void DrawArrows(FrameView view)
    {
        if (arrowModel is null || !world.TryStore<Arrow>(out var arrows) || arrows.Count == 0) { Arrow.Fill(world, view.SwingTrail); return; }
        double now = world.Seconds;
        var ents = arrows.Entities;
        var data = arrows.Data;
        for (int i = 0; i < ents.Length; i++)
            if (!data[i].Flying && arrowStopped.TryAdd(ents[i].Packed, now)) arrowOrder.Add((ents[i].Packed, now));
        int shownStopped = 0;
        for (int i = arrowOrder.Count - 1; i >= 0; i--)
        {
            var (packed, at) = arrowOrder[i];
            bool keep = shownStopped < StoppedArrowsShown && now - at <= StoppedArrowSeconds && world.Alive(EntityId.FromPacked(packed));
            if (keep) shownStopped++;
            else drawnArrows.Remove(packed);
        }
        int firstShown = Math.Max(0, arrowOrder.Count - shownStopped);
        var shown = arrowsShown;
        shown.Clear();
        for (int i = firstShown; i < arrowOrder.Count; i++) shown.Add(arrowOrder[i].Entity);
        for (int i = 0; i < ents.Length; i++)
        {
            var a = data[i];
            if (!a.Flying && !shown.Contains(ents[i].Packed)) continue;
            Arrow.Placed(world, a, out var head, out var heading);
            view.Characters.Add(ArrowDrawn(ents[i].Packed, head, heading));
        }
        // The arrow on the string while the player's (or a film's archer's) bow is up: from the draw hand toward the bow hand.
        foreach (var e in characters)
            if (world.Has<Bow>(e) && world.Get<Bow>(e) is { InHand: true, OnString: true, State: >= BowState.Raising } && NockedArrow(e, out var nockHead, out var nockHeading))
                view.Characters.Add(ArrowDrawn(e.Packed ^ 0x8000_0000_0000_0000UL, nockHead, nockHeading));
    }

    DrawnCharacter ArrowDrawn(ulong key, Vector3 head, Vector3 heading)
    {
        if (!drawnArrows.TryGetValue(key, out var d))
        {
            var skin = new Matrix4x4[Math.Max(1, arrowModel!.Skeleton.Count)];
            for (int k = 0; k < skin.Length; k++) skin[k] = Matrix4x4.Identity;
            drawnArrows[key] = d = new DrawnCharacter { Model = arrowModel, World = Matrix4x4.Identity, Skin = skin, Entity = 0 };
        }
        d.World = ArrowPlace(head, heading);
        return d;
    }

    /// <summary>1.1's arrow (its shaft along +Y, its head at +Y's end) put with its head at <paramref name="head"/> along <paramref name="heading"/>.</summary>
    static Matrix4x4 ArrowPlace(Vector3 head, Vector3 heading)
    {
        var h = heading.LengthSquared() > 1e-8f ? Vector3.Normalize(heading) : Vector3.UnitX;
        var side = Vector3.Cross(h, Vector3.UnitZ);
        side = side.LengthSquared() > 1e-8f ? Vector3.Normalize(side) : Vector3.UnitX;
        var up = Vector3.Cross(side, h);
        var t = head - h * ArrowHeadAlongY;
        return new Matrix4x4(side.X, side.Y, side.Z, 0f, h.X, h.Y, h.Z, 0f, up.X, up.Y, up.Z, 0f, t.X, t.Y, t.Z, 1f);
    }

    /// <summary>Where the arrow on <paramref name="e"/>'s string is drawn: its nock at the draw hand, its head past the bow hand.</summary>
    bool NockedArrow(EntityId e, out Vector3 head, out Vector3 heading)
    {
        head = heading = default;
        var assets = world.Service<CharacterAssets>();
        var kind = assets.Kind(world.Get<CharacterAnimation>(e).Kind);
        if (kind.Limbs is not { LeftArm.Has: true, RightArm.Has: true } limbs || !world.Has<Body>(e)) return false;
        var bones = world.Service<CharacterPoses>().Bones(world, e);
        ref var body = ref world.Get<Body>(e);
        var place = kind.Placement(body.Feet, body.Yaw);
        var bowHand = Vector3.Transform(bones[limbs.LeftArm.End].Translation, place);
        var drawHand = Vector3.Transform(bones[limbs.RightArm.End].Translation, place);
        var way = bowHand - drawHand;
        if (way.LengthSquared() < 1e-4f) return false;
        heading = Vector3.Normalize(way);
        head = drawHand + heading * (2f * ArrowHeadAlongY - 0.05f);
        return true;
    }
}
