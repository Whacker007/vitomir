// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Which picture a shadow piece is drawn with (earlier, in Engine/World/Shadows.cs; data of the Classic snapshot).
namespace Vitomir.Platform.Presentation.Classic;

/// <summary>Which picture a shadow polygon is drawn with.</summary>
public enum ShadowTexture
{
    /// <summary><c>shadowblob</c> (<c>pDropShadowMat</c>).</summary>
    Blob,
    /// <summary><c>silshadow</c> (<c>pSilhouetteMat</c>).</summary>
    Silhouette,
    /// <summary>The full-shadow picture (<c>pFullShadowBmp</c>), one a caster here.</summary>
    Bitmap,
}
