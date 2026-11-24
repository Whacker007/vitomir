// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Classic snapshot: one frame of a 1.1 place as the old 3D renderer draws it.
using System.Numerics;

namespace Vitomir.Platform.Presentation.Classic;

/// <summary>
/// The world's picture laid out on its target (the engine's <c>FrameLayout</c>, <c>WorldFrame.Lay</c>): the size the world
/// is drawn at and its stretch, the rows it is drawn into from the top and how far down it carries on, the columns a pane
/// leaves it (none: <see cref="ColumnsDrawn"/> false), its zoom, projection and view.
/// </summary>
public readonly record struct ClassicLayout(int TargetWidth, int TargetHeight, int Width, int Height, float Stretch,
                                            int Rows, int Drawn, bool ColumnsDrawn, float Magnify, int X0, int Cols,
                                            Matrix4x4 Projection, Matrix4x4 ViewProjection);

/// <summary>
/// The drawn camera: its view, its view turned only (what the sky rides on), where it is, and the sky's own projection
/// with it (the far plane <see cref="ClassicSky.FarPlane"/>).
/// </summary>
public readonly record struct ClassicCamera(Matrix4x4 View, Matrix4x4 RotationView, Vector3 Position, Matrix4x4 SkyViewProjection);

/// <summary>A sky model (a resource, sent when the place's sky changes): its vertices unposed, its triangles, its groups' materials.</summary>
public sealed class ClassicSky
{
    /// <summary>The far clip plane the original sets while drawing the sky.</summary>
    public const float FarPlane = 100000f;
    /// <summary>Which sky, among those this producer has sent.</summary>
    public required int Id { get; init; }
    public required SceneVertex[] Vertices { get; init; }
    public required uint[] Indices { get; init; }
    public required (int Material, uint First, uint Count)[] Groups { get; init; }
}

/// <summary>One shadow piece: its picture (a material, or one of the frame's bitmaps), and its points in <see cref="ClassicShadows.Points"/>.</summary>
public readonly record struct ClassicShadowPiece(ShadowTexture Texture, int Bitmap, int First, int Count);

/// <summary>One point of a shadow piece, with its texture coordinates and its shade (0–255).</summary>
public readonly record struct ClassicShadowPoint(Vector3 Position, Vector2 Uv, float Shade);

/// <summary>
/// The shadows of a frame (the engine's <c>Shadows</c>): the pieces and their points, and the full shadows' bitmaps
/// (<see cref="BitmapSize"/> squared, one byte a texel), <see cref="BitmapCount"/> of them this frame.
/// </summary>
public sealed class ClassicShadows
{
    public const int BitmapSize = 128;
    /// <summary>They were worked out this frame (a cell level's things cast the sun's own instead).</summary>
    public bool Cast { get; set; }
    public List<ClassicShadowPiece> Pieces { get; } = [];
    public List<ClassicShadowPoint> Points { get; } = [];
    /// <summary>The bitmaps, reused from frame to frame; the first <see cref="BitmapCount"/> are this frame's.</summary>
    public List<byte[]> Bitmaps { get; } = [];
    public int BitmapCount { get; set; }
    /// <summary>The materials the blob and the silhouette pieces are drawn with (−1, −2: the game lacks them).</summary>
    public int BlobMaterial { get; set; } = -2;
    public int SilhouetteMaterial { get; set; } = -2;
}

/// <summary>A run of particles with one material (the engine's <c>ParticleBatch</c>, less whose they are).</summary>
public readonly record struct ClassicParticleBatch(int Material, int First, int Count, ParticleRun Run);

/// <summary>A place's particles this frame, as the three lists the engine collects them in and the runs over them.</summary>
public sealed class ClassicParticles
{
    public List<ParticleSprite> Sprites { get; } = [];
    public List<ParticleQuad> Quads { get; } = [];
    public List<ParticleStreak> Streaks { get; } = [];
    public List<ClassicParticleBatch> Batches { get; } = [];
}

/// <summary>
/// One dynamic light's blob on one surface (the engine's <c>LightBlob</c>, laid on its surface): the blob's material and
/// the wall's (−1: none), whether the surface's sector was reached by the walk, its packed colour, and the surface's
/// points in <see cref="ClassicFrame.BlobPoints"/> with the blob's coordinates and the wall's own.
/// </summary>
public readonly record struct ClassicBlob(int Blob, int Surface, bool Seen, uint Color, int First, int Count);

/// <summary>One decal (the engine's <c>Decal</c>): its material (−1: none), its colour 0–255, its points in <see cref="ClassicFrame.DecalPoints"/>.</summary>
public readonly record struct ClassicDecal(int Material, Vector4 Color, int First, int Count);

/// <summary>One halo (the engine's <c>Halo</c>): its material, where its thing is, its size, the fluorescent bit, its colour (ARGB).</summary>
public readonly record struct ClassicHalo(int Material, Vector3 Position, float Size, bool Fluoro, uint Colour);

/// <summary>The halos and the twinkles of a frame (the engine's <c>Halos</c>, stepped by the frame's producer).</summary>
public sealed class ClassicHalos
{
    /// <summary>The twinkles' material (−1: none), their colour (ARGB) and half their size, and where each is.</summary>
    public int TwinkleMaterial { get; set; } = -1;
    public uint TwinkleColour { get; set; }
    public float TwinkleHalf { get; set; }
    public List<Vector3> Twinkles { get; } = [];
    public List<ClassicHalo> Halos { get; } = [];
}

/// <summary>The click-to-move ring this frame: shown, its colour (0–255), its four corners (position and coordinates).</summary>
public sealed class ClassicRing
{
    public bool Shown { get; set; }
    public Vector4 Colour { get; set; }
    public SceneVertex[] Corners { get; } = new SceneVertex[4];
}

/// <summary>The mirrors in view (the engine's <c>MirrorView</c>): their rectangle on the screen (−1..1, y up), the mirrored view and eye.</summary>
public sealed class ClassicMirror
{
    public bool Seen { get; set; }
    public float X0 { get; set; }
    public float Y0 { get; set; }
    public float X1 { get; set; }
    public float Y1 { get; set; }
    public Matrix4x4 View { get; set; }
    public Matrix4x4 ViewProjection { get; set; }
    /// <summary>The reflected eye.</summary>
    public Vector3 Eye { get; set; }
}

/// <summary>
/// What the reflection pass needs of the level, sent once (a resource): each level batch's sector (−1: not built from one)
/// and whether its group is a mirror (Reflect), and the Nodraw mirrors' glass as triangles.
/// </summary>
public sealed class ClassicLevel
{
    public required int[] BatchSector { get; init; }
    public required bool[] BatchReflective { get; init; }
    public required SceneVertex[] Glass { get; init; }
    /// <summary>A level batch's sector, −1 for a thing's or one built later.</summary>
    public int SectorOf(int batch) => (uint)batch < BatchSector.Length ? BatchSector[batch] : -1;
    public bool Reflective(int batch) => (uint)batch < BatchReflective.Length && BatchReflective[batch];
}

/// <summary>
/// **The camera's full-screen fade and flash this frame** (per-frame world state, like the sky
/// window), as 1.1 draws them over everything after the interface: the fade's level (255 the picture as it is, 0 black)
/// and the flash's intensity (0..255) and colour (0xRRGGBB). The filter is not here: 1.1 draws it with no material
/// (its material is never set), so nothing of it reaches the screen.
/// </summary>
public readonly record struct ClassicScreen(float FadeLevel, float FlashIntensity, uint FlashColor)
{
    /// <summary>Nothing over the picture: no fade, no flash.</summary>
    public static ClassicScreen None => new(255f, 0f, 0);

    /// <summary>The fade darkens the picture this frame.</summary>
    public bool Fades => FadeLevel < 255f;

    /// <summary>The flash brightens it.</summary>
    public bool Flashes => FlashIntensity > 0f;
}

/// <summary>
/// **One frame of a 1.1 place, as the old 3D renderer draws it** (the Classic snapshot): the
/// picture's layout and camera, the clocks, the scene (<see cref="ClassicScene"/>), the sky, the shadows (the world's and
/// the mirror's), the particles, the light blobs, the decals, the halos, the click ring, the mirror, the walk and the screen's
/// fade and flash (<see cref="ClassicScreen"/>). Plain data
/// and no engine type: the engine fills it (<c>WorldSnapshot</c>) after the frame's game side
/// (<c>WorldFrame.Frame</c>, the poses), the renderer reads it and nothing else of the game. The producer keeps two and
/// fills them in turn (<see cref="ClassicFrames"/>), reusing their lists.
/// </summary>
public sealed class ClassicFrame
{
    public ClassicLayout Layout { get; set; }
    public ClassicCamera Camera { get; set; }
    /// <summary>The simulation's clock, in seconds (the particles', the decals' and the passes' slides).</summary>
    public float Seconds { get; set; }
    /// <summary>The interface's clock this frame.</summary>
    public float InterfaceSeconds { get; set; }
    public ClassicScene Scene { get; } = new();
    /// <summary>The level's resource (the reflection pass's), sent once.</summary>
    public ClassicLevel? Level { get; set; }

    /// <summary>The place's sky now (null: none), and this frame's posed positions of its vertices while it shows.</summary>
    public ClassicSky? Sky { get; set; }
    public Vector3[] SkyPositions { get; set; } = [];
    /// <summary>The sky is on the screen this frame, inside <see cref="SkyWindow"/> (pixels, y down, from the world's first column).</summary>
    public bool SkyOn { get; set; }
    public (int X, int Y, int W, int H) SkyWindow { get; set; }

    public ClassicShadows Shadows { get; } = new();
    /// <summary>The mirror's own shadows: its rule for which things cast, from the reflected eye.</summary>
    public ClassicShadows MirrorShadows { get; } = new();
    public ClassicParticles Particles { get; } = new();
    public List<ClassicBlob> Blobs { get; } = [];
    public List<SceneVertex> BlobPoints { get; } = [];
    public List<ClassicDecal> Decals { get; } = [];
    public List<SceneVertex> DecalPoints { get; } = [];
    public ClassicHalos Halos { get; } = new();
    public ClassicRing Ring { get; } = new();
    public ClassicMirror Mirror { get; } = new();
    /// <summary>Reflections are on (the Reflect groups are the reflection pass's, not the world's).</summary>
    public bool Reflections { get; set; }
    /// <summary>The frame's portal walk ran (the eye was in a sector), and which sectors it reached.</summary>
    public bool Walked { get; set; }
    public bool[] SectorSeen { get; set; } = [];
    public bool Sees(int sector) => (uint)sector < SectorSeen.Length && SectorSeen[sector];
    /// <summary>The camera's fade and flash, drawn over the whole picture after the interface.</summary>
    public ClassicScreen Screen { get; set; } = ClassicScreen.None;
}

/// <summary>
/// The producer's two frames, filled in turn: <see cref="Next"/> hands back the one not last published, to be filled, and
/// <see cref="Publish"/> makes it the one read. Their lists are kept and refilled, so a frame allocates nothing once they
/// have grown to the place.
/// </summary>
public sealed class ClassicFrames
{
    readonly ClassicFrame[] frames = [new(), new()];
    int front = 1;

    /// <summary>The frame last published (empty until the first).</summary>
    public ClassicFrame Front => frames[front];
    /// <summary>The frame to fill next: the other one.</summary>
    public ClassicFrame Next() => frames[front ^ 1];
    /// <summary>The frame just filled becomes <see cref="Front"/>.</summary>
    public ClassicFrame Publish() { front ^= 1; return frames[front]; }
}
