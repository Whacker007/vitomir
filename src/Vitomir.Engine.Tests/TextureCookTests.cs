// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Formats;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The texture cook (ours, Phase 3): KTX2 files written and read back
/// level for level and refused when wrong; our BC7 (mode 6) blocks decoded by another decoder
/// (BCnEncoder.Net's) within a small error of the picture; the mip chains, colour averaged in linear
/// light, normals kept unit and compressed to BC5.
/// </summary>
[Collection(LevelCooksCollection.Name)]
public class TextureCookTests
{
    /// <summary>A test picture: smooth gradients, a hard edge, a little noise, and alpha that varies.</summary>
    static byte[] Picture(int w, int h)
    {
        var rnd = new Random(7);
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                rgba[o] = (byte)(x * 255 / (w - 1));
                rgba[o + 1] = (byte)(y * 255 / (h - 1));
                rgba[o + 2] = (byte)Math.Clamp((x > w / 2 ? 200 : 40) + rnd.Next(-6, 7), 0, 255);
                rgba[o + 3] = (byte)(128 + 127 * Math.Sin(x * 0.1));
            }
        return rgba;
    }

    [Fact]
    public void BC7_blocks_decode_within_a_small_error_of_the_picture()
    {
        const int W = 64, H = 48;
        var rgba = Picture(W, H);
        var blocks = Bc7Mode6.Encode(rgba, W, H);
        Assert.Equal(W / 4 * (H / 4) * 16, blocks.Length);
        var back = new BcDecoder().DecodeRaw(blocks, W, H, CompressionFormat.Bc7);
        double sum = 0; int worst = 0;
        for (int i = 0; i < W * H; i++)
        {
            var c = back[i];
            int[] d = [c.r - rgba[4 * i], c.g - rgba[4 * i + 1], c.b - rgba[4 * i + 2], c.a - rgba[4 * i + 3]];
            foreach (int e in d) { sum += e * e; worst = Math.Max(worst, Math.Abs(e)); }
        }
        double rmse = Math.Sqrt(sum / (W * H * 4));
        Assert.True(rmse < 4.0, $"root mean square error {rmse:0.00} of 255");
        Assert.True(worst <= 40, $"worst channel error {worst}");

        // A block of one colour comes back to within one step of it (7 bits and a p-bit a channel).
        var flat = new byte[4 * 4 * 4];
        for (int i = 0; i < 16; i++) { flat[4 * i] = 201; flat[4 * i + 1] = 77; flat[4 * i + 2] = 13; flat[4 * i + 3] = 255; }
        var one = new BcDecoder().DecodeRaw(Bc7Mode6.Encode(flat, 4, 4), 4, 4, CompressionFormat.Bc7);
        Assert.All(one, c => Assert.True(Math.Abs(c.r - 201) <= 1 && Math.Abs(c.g - 77) <= 1 && Math.Abs(c.b - 13) <= 1 && c.a >= 254, $"{c}"));
        // Every block is mode 6: bit 6 of its first byte set and bits 0–5 clear.
        for (int b = 0; b < blocks.Length; b += 16) Assert.Equal(0x40, blocks[b] & 0x7f);
    }

    [Fact]
    public void A_KTX2_file_reads_back_as_written_and_a_wrong_one_is_refused()
    {
        var ktx = TextureCook.Cook(Picture(64, 32), 64, 32, TextureKind.Colour);
        Assert.Equal(Ktx2Format.Bc7Srgb, ktx.Format);
        Assert.Equal(7, ktx.Levels.Count);                                               // 64 × 32 down to 1 × 1
        Assert.Equal([2048, 512, 128, 32, 16, 16, 16], ktx.Levels.Select(l => l.Length)); // 16 bytes a 4 × 4 block, at least one block
        var bytes = ktx.Write();
        var back = Ktx2.Read(bytes);
        Assert.Equal((ktx.Format, ktx.Width, ktx.Height), (back.Format, back.Width, back.Height));
        for (int l = 0; l < ktx.Levels.Count; l++) Assert.Equal(ktx.Levels[l], back.Levels[l]);
        // The levels lie smallest first, each on a 16-byte boundary, after the header and descriptor.
        var header = Ktx2.ReadHeader(bytes, bytes.Length);
        for (int l = 0; l < header.Levels.Length; l++) Assert.Equal(0, header.Levels[l].Offset % 16);
        for (int l = 1; l < header.Levels.Length; l++) Assert.True(header.Levels[l].Offset < header.Levels[l - 1].Offset);
        Assert.Equal(bytes.Length, header.Levels[0].Offset + header.Levels[0].Length);
        // The data format descriptor names BC7 (colour model 134) and sRGB (transfer 2).
        int dfd = BitConverter.ToInt32(bytes, 48);
        Assert.Equal(134, bytes[dfd + 12]);
        Assert.Equal(2, bytes[dfd + 14]);

        var wrong = (byte[])bytes.Clone(); wrong[0] = 0;
        Assert.Throws<NodFormatException>(() => Ktx2.Read(wrong));
        var shortened = bytes[..^16];
        Assert.Throws<NodFormatException>(() => Ktx2.Read(shortened));
        var otherFormat = (byte[])bytes.Clone(); BitConverter.GetBytes(999u).CopyTo(otherFormat, 12);
        Assert.Throws<NodFormatException>(() => Ktx2.Read(otherFormat));
    }

    [Fact]
    public void Colour_mips_are_averaged_in_linear_light_and_normal_mips_stay_unit()
    {
        // Black and white stripes: in linear light their mean is 0.5, which is 188 in sRGB, not 128.
        const int W = 16, H = 16;
        var stripes = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++) { byte v = (byte)((i % W) % 2 == 0 ? 0 : 255); stripes[4 * i] = stripes[4 * i + 1] = stripes[4 * i + 2] = v; stripes[4 * i + 3] = 255; }
        var ktx = TextureCook.Cook(stripes, W, H, TextureKind.Colour);
        var last = new BcDecoder().DecodeRaw(ktx.Levels[^1], 1, 1, CompressionFormat.Bc7)[0];
        Assert.InRange(last.r, 185, 191);
        var data = TextureCook.Cook(stripes, W, H, TextureKind.Data);
        Assert.Equal(Ktx2Format.Bc7Unorm, data.Format);
        Assert.InRange(new BcDecoder().DecodeRaw(data.Levels[^1], 1, 1, CompressionFormat.Bc7)[0].r, 125, 130);

        // Two normals leaning opposite ways average to straight up, made unit again; stored as X and Y in BC5.
        var normals = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            bool left = (i % W) % 2 == 0;
            normals[4 * i] = (byte)(left ? 37 : 219); normals[4 * i + 1] = 128; normals[4 * i + 2] = 218; normals[4 * i + 3] = 255;   // ±0.71, 0, 0.71
        }
        var (half, hw, hh) = TextureCook.Half(normals, W, H, TextureKind.Normal);
        Assert.Equal((8, 8), (hw, hh));
        Assert.InRange(half[0], 126, 130);
        Assert.InRange(half[2], 253, 255);                                                   // z made 1 again, not 0.71
        var bc5 = TextureCook.Cook(normals, W, H, TextureKind.Normal);
        Assert.Equal(Ktx2Format.Bc5Unorm, bc5.Format);
        var decoded = new BcDecoder().DecodeRaw(bc5.Levels[0], W, H, CompressionFormat.Bc5);
        Assert.InRange(decoded[0].r, 33, 41);
        Assert.InRange(decoded[1].r, 215, 223);
    }
    [Fact]
    public void A_cut_out_maps_mips_keep_the_share_of_texels_that_pass_its_alpha_test()
    {
        // Leaves on a card: small hard-edged blobs over about a third of a 256 × 256 picture, nothing between.
        const int W = 256;
        var rnd = new Random(3);
        var leaves = new byte[W * W * 4];
        for (int i = 0; i < W * W; i++) { leaves[4 * i] = 60; leaves[4 * i + 1] = 110; leaves[4 * i + 2] = 40; }
        for (int b = 0; b < 700; b++)
        {
            int cx = rnd.Next(W), cy = rnd.Next(W), r = rnd.Next(2, 5);
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) leaves[4 * (((y + W) % W) * W + (x + W) % W) + 3] = 255;
        }
        float full = TextureCook.Coverage(leaves, 0.5f);
        Assert.InRange(full, 0.2f, 0.5f);
        float Cover(Ktx2 ktx, int level)
        {
            int w = Math.Max(1, W >> level);
            var px = new BcDecoder().DecodeRaw(ktx.Levels[level], w, w, CompressionFormat.Bc7);
            return px.Count(p => p.a / 255f >= 0.5f) / (float)px.Length;
        }
        // Plain mips thin it: by the 16 × 16 mip hardly a texel passes.
        var plain = TextureCook.Cook(leaves, W, W, TextureKind.Colour);
        Assert.True(Cover(plain, 4) < full * 0.5f, $"plain mip 4 covers {Cover(plain, 4):P0} of {full:P0}");
        // Told the cutoff, each mip down to 8 × 8 keeps the picture's cover to within a few texels in a hundred.
        var kept = TextureCook.Cook(leaves, W, W, TextureKind.Colour, alphaCutoff: 0.5f);
        for (int level = 1; level <= 5; level++)
            Assert.True(MathF.Abs(Cover(kept, level) - full) < 0.05f, $"mip {level} covers {Cover(kept, level):P0}, the picture {full:P0}");
        // The colour is untouched, and an opaque map (no cutoff) is as before.
        Assert.Equal(plain.Levels[0], kept.Levels[0]);
    }
}

