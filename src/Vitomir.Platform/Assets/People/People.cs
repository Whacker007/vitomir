// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The contract between the glTF loader (which fills it), the animation, the rendering and the movement (which read it).
using System.Numerics;
using Vitomir.Platform.Animation;

namespace Vitomir.Platform.Assets.People;

/// <summary>
/// A person to build from the models repository's files (the player is the models repository's own man on the
/// male base): a base body and what is worn over it, each named by its file's stem (<c>base_male</c>,
/// <c>townsman_male</c>, <c>hair_collar_male</c>, <c>beard_short_male</c>), found where the models repository keeps
/// them (<c>assets/characters/&lt;base&gt;/</c>, <c>assets/outfits/&lt;outfit&gt;/</c>, <c>assets/hair/</c>). No sliders
/// yet: the base's own shape, skin and joint positions (people at cook time or at run time,
/// still open).
/// </summary>
/// <param name="Name">The person's name in logs and kinds ("player", "townsman").</param>
/// <param name="Sex">"male" or "female": which motions file plays on it (<c>motions_&lt;sex&gt;.glb</c>).</param>
public sealed record PersonRecipe(string Name, string Sex, string Base, IReadOnlyList<string> Wear)
{
    /// <summary>
    /// The player as before dresses him: the base man, the townsman's clothes, hair to the collar, a short beard; his own skin
    /// map (<see cref="PlayerSkinMap"/>).
    /// </summary>
    public static readonly PersonRecipe Player = new("player", "male", "base_male", ["townsman_male", "hair_collar_male", "beard_short_male"]) { SkinMap = PlayerSkinMap };

    /// <summary>
    /// The player's skin, the base man's colour map with the scalp shaded as under his hair and the
    /// beard's area as under a beard (4096²; under the models folder).
    /// </summary>
    public const string PlayerSkinMap = "assets/characters/base_male/player/skin_male_player_basecolor.png";

    /// <summary>
    /// A colour map (a PNG, its path under the models folder) the base's skin is drawn with in place of the base file's own
    /// (its <c>skin_…</c> material's <c>baseColorTexture</c>; the normal and occlusion-roughness-metal maps stay the base's), or null for
    /// the base's. A folder without the file (an older set) draws the base's, said once in the library's log.
    /// </summary>
    public string? SkinMap { get; init; }

    /// <summary>A woman to try the female base with (<c>--body woman</c>).</summary>
    public static readonly PersonRecipe Woman = new("woman", "female", "base_female", ["townswoman_female", "hair_braids_female"]);

    /// <summary>
    /// The person's look over its base and worn files (a crowd person's hair colour and grey, its skin's tint:
    /// <see cref="PersonLook"/>), or null for the files as made (the player, the woman).
    /// </summary>
    public PersonLook? Look { get; init; }

    /// <summary>
    /// The folder, under the models folder (<c>/</c>-separated), a baked person's files are
    /// found in first: its body (<see cref="Base"/>: <c>person_04</c>) and what it wears fitted to it (<c>person_04_townsman</c>,
    /// <c>person_04_hair</c>); null for a person made from the bases (the player, the woman, the earlier build's crowd).
    /// </summary>
    public string? Folder { get; init; }

    /// <summary>
    /// For each worn file baked for this person, the stem of the file it was fitted from (<c>townsman_male</c>,
    /// <c>hair_crop_male</c>), whose <c>.json</c> and <c>_fit.npz</c> say what it is, whether it covers the hair and which of the body's
    /// triangles it hides: a baked body keeps its base's triangles in their order (107,024 the man's and the woman's alike), so
    /// the base's lists hold for it. Null, or a stem missing here: the worn file's own <c>.json</c> and fit beside it, when it has them.
    /// </summary>
    public IReadOnlyDictionary<string, string>? FittedFrom { get; init; }

    /// <summary>A key for caches and logs: the base and the worn files in order (and the look, when there is one; and the skin map; and a baked person's folder).</summary>
    public string Key => Base + "+" + string.Join("+", Wear) + (SkinMap is { } skin ? " skin " + Path.GetFileNameWithoutExtension(skin) : "")
                         + (Look is { } look ? " (" + look.Key + ")" : "")
                         + (Folder is { } folder ? " in " + folder : "");
}

/// <summary>
/// The bones of a person's skeleton the platform poses by name: the models repository's 78-bone skeleton (56 and the
/// 22 twist bones), the same names for both sexes. −1 where a bone is missing. The skeleton's
/// first bone, <c>root</c>, carries a turn on the spot; the hips are <see cref="Pelvis"/>, never the first
/// parentless bone (<see cref="LimbMap.FromTips"/> would take <c>root</c>).
/// </summary>
public sealed record PersonRig(int Root, int Pelvis, int[] Spine, int Neck, int Head, int Jaw, int EyeL, int EyeR,
                               Limb LeftLeg, Limb RightLeg, Limb LeftArm, Limb RightArm,
                               IReadOnlyDictionary<string, PersonSocket> Sockets)
{
    /// <summary>The rig of a skeleton by the models repository's bone names (<c>pelvis</c>, <c>spine_01</c>–<c>_03</c>, <c>neck_01</c>,
    /// <c>head</c>, <c>jaw</c>, <c>eye_l</c>, <c>thigh_l</c>/<c>calf_l</c>/<c>foot_l</c>/<c>ball_l</c>,
    /// <c>upperarm_l</c>/<c>lowerarm_l</c>/<c>hand_l</c>/<c>middle_01_l</c>, and the right side's).</summary>
    public static PersonRig From(Skeleton skeleton, IReadOnlyDictionary<string, PersonSocket>? sockets = null)
    {
        int F(string n) => skeleton.Find(n);
        Limb L(string upper, string lower, string end, string tip)
        {
            int u = F(upper), l = F(lower), e = F(end), t = F(tip);
            return u >= 0 && l >= 0 && e >= 0 ? new Limb(u, l, e, t) : Limb.None;
        }
        int[] spine = new[] { F("spine_01"), F("spine_02"), F("spine_03") }.Where(b => b >= 0).ToArray();
        return new PersonRig(F("root"), F("pelvis"), spine, F("neck_01"), F("head"), F("jaw"), F("eye_l"), F("eye_r"),
                             L("thigh_l", "calf_l", "foot_l", "ball_l"), L("thigh_r", "calf_r", "foot_r", "ball_r"),
                             L("upperarm_l", "lowerarm_l", "hand_l", "middle_01_l"), L("upperarm_r", "lowerarm_r", "hand_r", "middle_01_r"),
                             sockets ?? new Dictionary<string, PersonSocket>());
    }

    /// <summary>Every bone the rig needs is there.</summary>
    public bool Complete => Root >= 0 && Pelvis >= 0 && Spine.Length == 3 && Neck >= 0 && Head >= 0 && EyeL >= 0 && EyeR >= 0
                            && LeftLeg.Has && RightLeg.Has && LeftArm.Has && RightArm.Has;
}

/// <summary>A socket (<c>socket_hand_r</c> …): a node under a bone, its offset from that bone in the bone's own frame.</summary>
public readonly record struct PersonSocket(int Bone, BoneTransform Offset);

/// <summary>
/// A person built: one <see cref="SkinnedModel"/> holding the body (less the triangles what is worn hides),
/// its eyes, lashes, teeth and tongue, and every worn file's meshes, on one skeleton (the body's bones, then the swing and
/// strap bones the worn files add, matched by name; parents before children); in the platform's model space (metres, Z
/// up, the person facing +Y with the right hand at +X: glTF's (x, y, z) is (−x, z, y) here, a half turn about Z on
/// <c>GltfReader.ToEngine</c>'s); each part with its <see cref="PartMaterial"/>; the images its parts name.
/// </summary>
public sealed record PersonModel(PersonRecipe Recipe, SkinnedModel Model, PersonRig Rig, IReadOnlyList<TextureImage> Textures)
{
    /// <summary>Triangles of the body not drawn because something worn covers them (the fit data's <c>hidden</c>).</summary>
    public int HiddenTriangles { get; init; }

    /// <summary>
    /// Each vertex's place in the order its hair or beard greys (the fit data's <c>&lt;mesh&gt;.grey</c>, 0 first), NaN
    /// for a vertex of no hair; null when nothing worn is hair (<see cref="PersonLook.Grey"/>).
    /// </summary>
    public float[]? HairGrey { get; init; }

    /// <summary>The base body's height (its <c>.json</c>'s <c>height_m</c>; 0 when it does not say), for a person scaled to its own.</summary>
    public float BaseHeight { get; init; }

    /// <summary>
    /// The swing record of what the person wears (<see cref="SwingRecord"/>: each worn file's <c>extras.vitomir.swing</c>, merged in the
    /// recipe's order; a hair left out under what covers it is not in it), or null when nothing worn has swing chains.
    /// </summary>
    public SwingRecord? Swing { get; init; }
}

/// <summary>
/// The models repository's files: its repository read from disk while developing
/// (<c>C:\Games\Vitomir\models</c> by default), or the copy a package carries. Loads people and the motions of each
/// sex once and keeps them.
/// </summary>
public sealed partial class PeopleLibrary
{
    /// <summary>The folder: the repository's root (it has <c>assets/characters/</c> and <c>assets/motions/</c>).</summary>
    public required string Folder { get; init; }

    /// <summary>The repository's commit the files came from (its <c>.git</c>, or a package's <c>source.json</c>), or null.</summary>
    public string? Commit { get; init; }

    /// <summary>The folder if it holds the models repository's files, else null with the reason in <paramref name="why"/>.</summary>
    public static PeopleLibrary? TryOpen(string folder, out string why) => Open(folder, out why);   // PeopleLoader.cs

    /// <summary>A person, built once and kept (by <see cref="PersonRecipe.Key"/>).</summary>
    public PersonModel Person(PersonRecipe recipe) => Build(recipe);   // PeopleLoader.cs

    /// <summary>The standard set of one sex ("male", "female": <c>assets/motions/motions_&lt;sex&gt;.glb</c>), every clip with its notes, in the file's order; loaded once.</summary>
    public IReadOnlyList<INotedClip> Motions(string sex) => LoadMotions(sex);   // PeopleLoader.cs
}
