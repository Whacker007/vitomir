// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: 1.1's cursor art read from the player's own install, as its characters are.
using Vitomir.Formats;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Assets.OneOne;

/// <summary>
/// 1.1's eight cursors' art, read once from the player's install through <see cref="Vitomir.Formats"/>
/// (<see cref="Cursors.Path"/>: <c>ui/Materials/cursor*.tga</c> in its archives; RGBA, row 0 the top). A cursor that is
/// not there or does not read is null, and the picture draws a plain arrow in its place; nothing is ever shipped.
/// </summary>
public sealed class OneOneCursors
{
    readonly TextureImage?[] art = new TextureImage?[Cursors.Count];

    /// <summary>How many of the eight were read.</summary>
    public int Read { get; }

    OneOneCursors(GameInstall? install)
    {
        if (install is null) return;
        for (int i = 0; i < art.Length; i++)
        {
            var path = Cursors.Path((CursorId)i);
            try
            {
                if (!install.TryRead(path, out var data)) continue;
                var image = TgaImage.Read(data, path);
                art[i] = new TextureImage(Cursors.Name((CursorId)i), image.Width, image.Height, image.Rgba);
                Read++;
            }
            catch (Exception e) when (e is NodFormatException or InvalidDataException or IOException)
            {
                art[i] = null;
            }
        }
    }

    /// <summary>The cursors of <paramref name="install"/> (none without one).</summary>
    public static OneOneCursors From(GameInstall? install) => new(install);

    /// <summary>A cursor's art, or null.</summary>
    public TextureImage? Art(CursorId id) => (uint)id < art.Length ? art[(int)id] : null;
}
