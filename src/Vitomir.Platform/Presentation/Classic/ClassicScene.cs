// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Classic snapshot's scene: a mesh of things and a level as the 1.1 look draws it.
using System.Numerics;

namespace Vitomir.Platform.Presentation.Classic;

/// <summary>A picture as the card takes it: RGBA, the bottom row first.</summary>
public sealed record ClassicPicture(int Width, int Height, byte[] Rgba);

/// <summary>
/// **A material, as the 1.1 look draws it**: a resource, sent once, by its number in the place's material cache (its id).
/// Everything about it that does not change while it is loaded; what does (the frame showing, the passes switched off) is
/// the frame's (<see cref="ClassicMaterialFrame"/>). Its pictures go with it the one time (<see cref="ClassicScene.NewPictures"/>).
/// </summary>
public sealed class ClassicMaterial
{
    public required string Name { get; init; }
    public BlendKind Blend { get; init; }
    public MaterialTraits Traits { get; init; }
    /// <summary>The <c>pass</c> lines in file order; empty for the ordinary single-texture material.</summary>
    public MaterialPass[] Passes { get; init; } = [];
    /// <summary>How many images it has (the texture frames first, then the pass textures).</summary>
    public int Images { get; init; }
    /// <summary>The <c>glow</c> map's image, or −1.</summary>
    public int GlowImage { get; init; } = -1;
    /// <summary>The <c>detail</c> texture's image, or −1.</summary>
    public int DetailImage { get; init; } = -1;
    public float DetailScale { get; init; } = 1f;
    /// <summary>The NAM's <c>2sided</c>.</summary>
    public bool TwoSided { get; init; }
    /// <summary>The NAM's <c>fullylit</c>.</summary>
    public bool FullyLit { get; init; }
    /// <summary>The light style the glow map follows (the NAM's <c>glowramp</c>, 0 without one).</summary>
    public int GlowRamp { get; init; }
    /// <summary>Its layer-0 slide (<c>slide 0</c>), in texture coordinates per second; zero without one.</summary>
    public Vector2 Slide0 { get; init; }
}

/// <summary>What a material is showing this frame: the image its frame draws, and whether its passes are drawn.</summary>
public readonly record struct ClassicMaterialFrame(int CurrentImage, bool DrawPasses);

/// <summary>
/// **The scene's geometry at one revision**: a resource, sent when the mesh grows (a thing walks in after the place loaded),
/// never changed after. Its vertices are as the mesh held them when it was sent (lit); a frame's posed and relit vertices
/// go over their slices (<see cref="ClassicScene.Posed"/>, <see cref="ClassicScene.Relit"/>).
/// </summary>
public sealed class ClassicMesh
{
    public required int Revision { get; init; }
    public required SceneVertex[] Vertices { get; init; }
    public required uint[] Indices { get; init; }
    /// <summary>The draw calls; a batch's <see cref="DrawBatch.Thing"/> indexes <see cref="ClassicScene.Things"/>.</summary>
    public required DrawBatch[] Batches { get; init; }
}

/// <summary>
/// **One thing, as this frame draws it**: where its batches go (from where the scene built them to where the thing is now),
/// how see-through, whether it is there at all, its shell and its hover glow. Laid out by the producer from the thing's own
/// state; the renderer reads nothing else of it.
/// </summary>
public struct ClassicThing
{
    /// <summary>From where the scene built the thing's geometry to where it is now, its own scale in it; only when <see cref="Placed"/>.</summary>
    public Matrix4x4 Placement;
    /// <summary>It has moved or is scaled, and its batches are drawn with <see cref="Placement"/>; otherwise with the camera's matrix alone.</summary>
    public bool Placed;
    /// <summary><c>SetAlpha</c>, MODELALPHA: decides the translucent stage.</summary>
    public float ModelAlpha;
    /// <summary>The stealth fade, 1 without one; multiplied in as it is drawn, and 0 leaves it out.</summary>
    public float Fade;
    /// <summary>Still in the world (a thing that left keeps its geometry in the mesh and is not drawn).</summary>
    public bool InWorld;
    /// <summary>Its render flags leave its model out (DON'T RENDER, REFLECTION ONLY).</summary>
    public bool ModelHidden;
    /// <summary>The mirror may show it: neither REFLECTION ONLY's bit 2 nor NO REFLECTION.</summary>
    public bool Reflected;
    /// <summary>Not posed this frame: the view cannot show it (<c>ScenePoses</c>), so it is not drawn.</summary>
    public bool Passed;
    /// <summary>Its shell material's number, or −1 for none (or one that does not load).</summary>
    public int Shell;
    /// <summary>Its shell pass as the shell material alone gives it (the model's own slide is the batch's material's, added as drawn).</summary>
    public ShellDraw? ShellDraw;
    /// <summary>The shell's bit 0x80: the model's own pass is left out.</summary>
    public bool HidesModel;
    /// <summary>The hover glow's tint is on it, <see cref="Glow"/>.</summary>
    public bool Glowing;
    public Vector3 Glow;
    /// <summary>The first thing in the list that is the same thing (a scene asked for it; else its own number): what <c>Only</c> compares.</summary>
    public int Identity;
}

/// <summary>
/// One level surface drawn see-through this frame for being in the camera's way (ours): the batch it is in, which of the
/// batch's <see cref="DrawBatch.Runs"/> it is, and how far it is faded (0 solid, 1 as clear as it goes), with the opacity
/// that level is drawn at.
/// </summary>
public readonly record struct ClassicFaded(int Batch, int Run, float Level, float Opacity);

/// <summary>
/// One soft window the faded surfaces are drawn clear in (ours; the engine's <c>SeeThrough.Hole</c>): the unit line from the eye
/// to a subject, the cosines of the angles from it at which it is clear and solid again, and how far along the line it reaches.
/// </summary>
public readonly record struct ClassicHole(Vector3 Axis, float CosInner, float CosOuter, float Far);

/// <summary>A run of vertices sent over the mesh's: from <see cref="Offset"/> of <see cref="ClassicScene.Vertices"/>, to the mesh's vertex <see cref="First"/>.</summary>
public readonly record struct ClassicSpan(uint First, int Offset, int Count);

/// <summary>The 32 light styles' values this frame, read as the engine's <c>LightStyles</c> reads them.</summary>
public sealed class ClassicStyles
{
    public const int Count = 32;
    public float[] Values { get; } = new float[Count];
    /// <summary>A style's value; 1 for one past the 32.</summary>
    public float Value(int style) => (uint)style < Count ? Values[style] : 1f;
    /// <summary>A style's brightness, 0–255.</summary>
    public byte Brightness(int style) => (byte)(int)(Value(style) * 255f);
}

/// <summary>
/// **The scene half of a Classic snapshot**: a mesh of a level and things, its materials, and this frame's state of each —
/// what the old world renderer read from the engine's <c>SceneMesh</c>, <c>MaterialCache</c>, <c>LightStyles</c>,
/// <c>ScenePoses</c> and the things until then. The resources (the mesh, the materials and their pictures) are sent by id
/// once; everything else is this frame's. Filled by the engine (<c>SceneSnapshot</c>), read by the renderer.
/// <para>**Every fill is read.** A fill carries only the resources new since the last (<see cref="NewPictures"/>,
/// <see cref="Repainted"/>), so whoever reads a scene reads each of its fills, in order.</para>
/// </summary>
public sealed class ClassicScene
{
    /// <summary>The mesh at its current revision (the same object until it grows).</summary>
    public ClassicMesh Mesh { get; set; } = new() { Revision = -1, Vertices = [], Indices = [], Batches = [] };
    /// <summary>Every material sent so far, by id (shared by both of the producer's buffers; it only grows).</summary>
    public IReadOnlyList<ClassicMaterial> Materials { get; set; } = [];
    /// <summary>How many of <see cref="Materials"/> have their pictures sent (and so can be drawn) by this fill.</summary>
    public int Drawable { get; set; }
    /// <summary>The pictures of the materials made drawable by this fill (one a slot, null for a picture missing).</summary>
    public List<(int Material, ClassicPicture?[] Pictures)> NewPictures { get; } = [];
    /// <summary>The pictures a material film painted since the last fill (1.1's <c>bHWUpdate</c>).</summary>
    public List<(int Material, int Image, ClassicPicture Picture)> Repainted { get; } = [];

    /// <summary>Each material's frame and passes now, by id (the first <see cref="Materials"/>.Count of them).</summary>
    public ClassicMaterialFrame[] MaterialFrames { get; set; } = [];
    public ClassicStyles Styles { get; } = new();
    /// <summary>The scene's fog (the sky turns it off).</summary>
    public FogSettings Fog { get; set; }
    /// <summary>What the materials' sliding textures run on, in seconds.</summary>
    public float Seconds { get; set; }

    /// <summary>The things the batches name, <see cref="ThingCount"/> of them.</summary>
    public ClassicThing[] Things { get; set; } = [];
    public int ThingCount { get; set; }
    /// <summary>By batch: a thing's batch whose named shape is hidden on its instance (the arrow off the bow).</summary>
    public bool[] BatchHidden { get; set; } = [];
    /// <summary>
    /// The level surfaces drawn see-through this frame (ours; the engine's <c>SeeThrough</c>), by batch and run, the batches in
    /// order. Empty with see-through off, and then the level is drawn exactly as without it. A thing faded the same way has
    /// its <see cref="ClassicThing.ModelAlpha"/> lowered instead, which takes it to the translucent stage.
    /// </summary>
    public List<ClassicFaded> Faded { get; } = [];
    /// <summary>
    /// The soft windows the <see cref="Faded"/> surfaces are clear in (ours), seen from <see cref="HoleEye"/>: each faded surface
    /// drawn solid outside them and clear inside, by the engine's sum (<c>SeeThrough.ClearnessOf</c>). Empty under the
    /// developer's x-ray, whose faded surfaces are drawn at their <see cref="ClassicFaded.Opacity"/> whole, outlined.
    /// </summary>
    public List<ClassicHole> Holes { get; } = [];
    /// <summary>The eye the <see cref="Holes"/> are seen from.</summary>
    public Vector3 HoleEye { get; set; }
    /// <summary>Whether the faded surfaces are drawn by the <see cref="Holes"/> (the players' window), not whole (the x-ray).</summary>
    public bool Windowed { get; set; }
    /// <summary>A stealth fade is in force (<see cref="ClassicThing.Fade"/>).</summary>
    public bool Fades { get; set; }
    /// <summary>Some thing has been given a shell material (the shells' draw runs only then).</summary>
    public bool AnyShells { get; set; }
    /// <summary>A hover glow is fed, and some thing is tinted by it.</summary>
    public bool GlowOn { get; set; }
    public bool GlowAny { get; set; }

    /// <summary>The posed and relit vertices of this frame, <see cref="VertexCount"/> of them, which <see cref="Posed"/> and <see cref="Relit"/> place.</summary>
    public SceneVertex[] Vertices { get; set; } = [];
    public int VertexCount { get; set; }
    /// <summary>The animating things posed this frame, and what they carry, each a slice of the mesh.</summary>
    public List<ClassicSpan> Posed { get; } = [];
    /// <summary>The standing-still things lit again this frame.</summary>
    public List<ClassicSpan> Relit { get; } = [];
    /// <summary>How many animating things were posed, and how many passed over (for the diagnostics).</summary>
    public (int Posed, int Passed) Animated { get; set; }

    /// <summary>Whether a material with these pictures is drawn by its <c>pass</c> lines this frame.</summary>
    public bool HasPasses(int material) => MaterialFrames[material].DrawPasses && Materials[material].Passes.Length > 0;

    /// <summary>The image a material's current frame draws.</summary>
    public int CurrentImage(int material) => MaterialFrames[material].CurrentImage;

    /// <summary>Whether the passes start with a blended draw (the surface goes with the translucent ones).</summary>
    public bool PassesAreTranslucent(int material) =>
        HasPasses(material) && (RenderKinds.State(Materials[material].Passes[0].Kind)?.BlendEnable ?? false);
}
