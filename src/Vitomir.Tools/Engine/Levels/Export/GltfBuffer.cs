// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Part of vitomir-tools.
using System.Numerics;
using System.Text.Json.Nodes;

namespace Vitomir.Engine.Levels.Export;

/// <summary>
/// The binary side of a glTF 2.0 file (ours): one growing buffer, a buffer view per accessor, each
/// view starting on a four-byte boundary as the specification asks. The accessors and views are
/// kept as JSON nodes ready to be put into the document.
/// </summary>
public sealed class GltfBuffer
{
    readonly MemoryStream data = new();

    public JsonArray BufferViews { get; } = [];
    public JsonArray Accessors { get; } = [];

    /// <summary>How many bytes the buffer holds so far.</summary>
    public long Length => data.Length;

    /// <summary>The finished buffer.</summary>
    public byte[] ToArray() => data.ToArray();

    // Component types and view targets, as the specification numbers them.
    const int UnsignedByte = 5121, UnsignedShort = 5123, Float = 5126;
    const int ArrayBuffer = 34962, ElementArrayBuffer = 34963;

    int View(ReadOnlySpan<byte> bytes, int target)
    {
        while (data.Length % 4 != 0) data.WriteByte(0);
        long offset = data.Length;
        data.Write(bytes);
        BufferViews.Add(new JsonObject
        {
            ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = bytes.Length, ["target"] = target,
        });
        return BufferViews.Count - 1;
    }

    int Accessor(int view, int componentType, int count, string type, bool normalized = false,
                 JsonArray? min = null, JsonArray? max = null)
    {
        var a = new JsonObject { ["bufferView"] = view, ["componentType"] = componentType, ["count"] = count, ["type"] = type };
        if (normalized) a["normalized"] = true;
        if (min is not null) a["min"] = min;
        if (max is not null) a["max"] = max;
        Accessors.Add(a);
        return Accessors.Count - 1;
    }

    /// <summary>Positions (VEC3 float), with the bounds glTF requires on a POSITION accessor.</summary>
    public int Positions(IReadOnlyList<Vector3> values)
    {
        var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
        foreach (var v in values) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
        return Accessor(View(Floats(values), ArrayBuffer), Float, values.Count, "VEC3", false,
                        Json.Array(mn.X, mn.Y, mn.Z), Json.Array(mx.X, mx.Y, mx.Z));
    }

    /// <summary>Normals or any other VEC3 float attribute.</summary>
    public int Vec3s(IReadOnlyList<Vector3> values) =>
        Accessor(View(Floats(values), ArrayBuffer), Float, values.Count, "VEC3");

    /// <summary>Texture coordinates (VEC2 float).</summary>
    public int Vec2s(IReadOnlyList<Vector2> values)
    {
        var bytes = new byte[values.Count * 8];
        for (int i = 0; i < values.Count; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 8), values[i].X);
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 8 + 4), values[i].Y);
        }
        return Accessor(View(bytes, ArrayBuffer), Float, values.Count, "VEC2");
    }

    /// <summary>Colours as four normalised bytes each, R first.</summary>
    public int Colors(IReadOnlyList<uint> rgba)
    {
        var bytes = new byte[rgba.Count * 4];
        for (int i = 0; i < rgba.Count; i++) BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), rgba[i]);
        return Accessor(View(bytes, ArrayBuffer), UnsignedByte, rgba.Count, "VEC4", normalized: true);
    }

    /// <summary>Triangle indices as unsigned shorts (a sector never has more than 65,535 vertices).</summary>
    public int Indices(IReadOnlyList<ushort> values)
    {
        var bytes = new byte[values.Count * 2];
        for (int i = 0; i < values.Count; i++) BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), values[i]);
        return Accessor(View(bytes, ElementArrayBuffer), UnsignedShort, values.Count, "SCALAR");
    }

    static byte[] Floats(IReadOnlyList<Vector3> values)
    {
        var bytes = new byte[values.Count * 12];
        for (int i = 0; i < values.Count; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 12), values[i].X);
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 12 + 4), values[i].Y);
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 12 + 8), values[i].Z);
        }
        return bytes;
    }
}

/// <summary>Small helpers for writing the glTF document's JSON.</summary>
static class Json
{
    public static JsonArray Array(params float[] values)
    {
        var a = new JsonArray();
        foreach (var v in values) a.Add(JsonValue.Create(Round(v)));
        return a;
    }

    /// <summary>Seven significant digits: a float's precision, without the double's noise in the text.</summary>
    public static double Round(float v) => double.Parse(v.ToString("G7", System.Globalization.CultureInfo.InvariantCulture),
                                                        System.Globalization.CultureInfo.InvariantCulture);

    public static JsonArray Of(IEnumerable<JsonNode?> items)
    {
        var a = new JsonArray();
        foreach (var i in items) a.Add(i);
        return a;
    }
}
