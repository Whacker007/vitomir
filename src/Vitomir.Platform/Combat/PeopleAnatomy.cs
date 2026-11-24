// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours. SANDBOX data, as 1.1's tables: the rules package's
// anatomy replaces it.
using Vitomir.Platform.Assets;

namespace Vitomir.Platform.Combat;

/// <summary>
/// The anatomy of the models repository's people, by their bones' names (<b>SANDBOX</b>): every person shares the 78 bones
/// (the 56 and the 22 twist bones), and what is worn adds its own (<c>swing_*</c>, <c>strap_*</c>), so the table is made for
/// each skeleton by name rather than matched by parents. <b>One volume a region</b>, on the bone named first, fitted from the
/// skin of all the region's bones together (<see cref="AnatomyTable.Volumes"/>, <see cref="HitVolumeFit"/>):
/// <list type="bullet">
/// <item>head: <c>head</c>, <c>jaw</c>, <c>eye_l</c>, <c>eye_r</c> (the hair, beard, lashes, teeth and tongue are weighted to them);</item>
/// <item>neck: <c>neck_01</c>; chest: <c>spine_03</c>, <c>clavicle_l</c>, <c>clavicle_r</c>; belly: <c>spine_02</c>, <c>spine_01</c>; hips: <c>pelvis</c>
/// and its bend helpers <c>hip_bend_front/back_l/r</c>;</item>
/// <item>each upper arm: <c>upperarm</c> and its <c>upperarm_twist_*</c>; forearm: <c>lowerarm</c> (which has no skin) and its
/// <c>lowerarm_twist_*</c>; hand: <c>hand</c> and the fingers;</item>
/// <item>each thigh: <c>thigh</c>, its <c>thigh_twist_*</c> and its knee's bend helpers <c>knee_bend_front/back</c>; shin:
/// <c>calf</c>; foot: <c>foot</c>, <c>ball</c>;</item>
/// <item>none: <c>root</c>, the sockets, <c>swing_*</c> (cloth, hair), <c>strap_*</c>, and anything else.</item>
/// </list>
/// The person's left is the model's −X (it faces +Y), as 1.1's. a bend helper's
/// skin counts as its parent's — the knee's for the thigh, the hip's for the hips — since the fit sums a vertex's weights on the bones
/// of one volume before choosing its volume (<see cref="HitVolumeFit"/>), so the skin the helpers share at a deep knee or hip stays in
/// the thigh's or the hips' volume.
/// </summary>
public static class PeopleAnatomy
{
    /// <summary>The table's name (<see cref="AnatomyTable.Name"/>), the bone count after it.</summary>
    public const string Name = "models repository person";

    /// <summary>The table for a skeleton with the models repository's bone names (<see cref="Assets.People.PersonRig.From"/> complete), or null.</summary>
    public static AnatomyTable? For(Skeleton skeleton)
    {
        if (!Assets.People.PersonRig.From(skeleton).Complete) return null;
        int n = skeleton.Count;
        var parents = new int[n];
        var regions = new BodyRegion[n];
        var volumes = new int[n];
        for (int b = 0; b < n; b++)
        {
            parents[b] = skeleton.Bones[b].Parent;
            var (region, on) = Of(skeleton.Bones[b].Name);
            regions[b] = region;
            volumes[b] = region == BodyRegion.None ? -1 : skeleton.Find(on);
            if (volumes[b] < 0) regions[b] = BodyRegion.None;
        }
        return new AnatomyTable { Name = $"{Name} ({n} bones)", Parents = parents, Regions = regions, Volumes = volumes };
    }

    /// <summary>A bone's region by its name, and the name of the bone its region's volume rides on.</summary>
    public static (BodyRegion Region, string On) Of(string bone)
    {
        string b = bone.ToLowerInvariant();
        switch (b)
        {
            case "head" or "jaw" or "eye_l" or "eye_r": return (BodyRegion.Head, "head");
            case "neck_01": return (BodyRegion.Neck, "neck_01");
            case "spine_03" or "clavicle_l" or "clavicle_r": return (BodyRegion.Chest, "spine_03");
            case "spine_01" or "spine_02": return (BodyRegion.Belly, "spine_02");
            case "pelvis": return (BodyRegion.Hips, "pelvis");
            case "hip_bend_front_l" or "hip_bend_back_l" or "hip_bend_front_r" or "hip_bend_back_r": return (BodyRegion.Hips, "pelvis");   // the pelvis's children
        }
        bool left = b.EndsWith("_l"), right = b.EndsWith("_r");
        if (!left && !right) return (BodyRegion.None, "");
        string s = left ? "_l" : "_r";
        BodyRegion Side(BodyRegion l, BodyRegion r) => left ? l : r;
        if (b == "upperarm" + s || (b.StartsWith("upperarm_twist_") && b.EndsWith(s))) return (Side(BodyRegion.LeftUpperArm, BodyRegion.RightUpperArm), "upperarm" + s);
        if (b == "lowerarm" + s || (b.StartsWith("lowerarm_twist_") && b.EndsWith(s))) return (Side(BodyRegion.LeftForearm, BodyRegion.RightForearm), "lowerarm" + s);
        if (b == "hand" + s || IsFinger(b)) return (Side(BodyRegion.LeftHand, BodyRegion.RightHand), "hand" + s);
        if (b == "thigh" + s || (b.StartsWith("thigh_twist_") && b.EndsWith(s)) || b == "knee_bend_front" + s || b == "knee_bend_back" + s) return (Side(BodyRegion.LeftThigh, BodyRegion.RightThigh), "thigh" + s);
        if (b == "calf" + s) return (Side(BodyRegion.LeftShin, BodyRegion.RightShin), "calf" + s);
        if (b == "foot" + s || b == "ball" + s) return (Side(BodyRegion.LeftFoot, BodyRegion.RightFoot), "foot" + s);
        return (BodyRegion.None, "");

        static bool IsFinger(string name)
        {
            foreach (var f in new[] { "thumb_", "index_", "middle_", "ring_", "pinky_" })
                if (name.StartsWith(f) && name.Length == f.Length + 4 && char.IsDigit(name[f.Length]) && char.IsDigit(name[f.Length + 1])) return true;
            return false;
        }
    }
}
