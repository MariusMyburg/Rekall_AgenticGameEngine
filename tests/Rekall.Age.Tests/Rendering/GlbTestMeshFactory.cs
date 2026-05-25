using System.Buffers.Binary;
using System.Text;

namespace Rekall.Age.Tests.Rendering;

internal static class GlbTestMeshFactory
{
    public static byte[] CreateTriangleGlb()
    {
        var binary = new MemoryStream();
        WriteSingle(binary, -0.5f);
        WriteSingle(binary, -0.5f);
        WriteSingle(binary, 0);
        WriteSingle(binary, 0.5f);
        WriteSingle(binary, -0.5f);
        WriteSingle(binary, 0);
        WriteSingle(binary, 0);
        WriteSingle(binary, 0.5f);
        WriteSingle(binary, 0);
        WriteUInt16(binary, 0);
        WriteUInt16(binary, 1);
        WriteUInt16(binary, 2);
        var rawBinary = binary.ToArray();

        var json = """
            {
              "asset": { "version": "2.0" },
              "buffers": [{ "byteLength": 42 }],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 36, "target": 34962 },
                { "buffer": 0, "byteOffset": 36, "byteLength": 6, "target": 34963 }
              ],
              "accessors": [
                {
                  "bufferView": 0,
                  "componentType": 5126,
                  "count": 3,
                  "type": "VEC3",
                  "min": [-0.5, -0.5, 0],
                  "max": [0.5, 0.5, 0]
                },
                { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
              ],
              "materials": [
                { "pbrMetallicRoughness": { "baseColorFactor": [0.2, 0.7, 1.0, 1.0] } }
              ],
              "meshes": [
                {
                  "name": "Triangle",
                  "primitives": [
                    { "attributes": { "POSITION": 0 }, "indices": 1, "material": 0, "mode": 4 }
                  ]
                }
              ],
              "nodes": [{ "mesh": 0 }],
              "scenes": [{ "nodes": [0] }],
              "scene": 0
            }
            """;

        var jsonBytes = Pad(Encoding.UTF8.GetBytes(json), 0x20);
        var binaryBytes = Pad(rawBinary, 0);
        var output = new MemoryStream();
        WriteUInt32(output, 0x46546C67);
        WriteUInt32(output, 2);
        WriteUInt32(output, checked((uint)(12 + 8 + jsonBytes.Length + 8 + binaryBytes.Length)));
        WriteUInt32(output, checked((uint)jsonBytes.Length));
        WriteUInt32(output, 0x4E4F534A);
        output.Write(jsonBytes);
        WriteUInt32(output, checked((uint)binaryBytes.Length));
        WriteUInt32(output, 0x004E4942);
        output.Write(binaryBytes);
        return output.ToArray();
    }

    public static byte[] CreateTexturedTriangleGlb(byte[] pngBytes)
    {
        var binary = new MemoryStream();
        var positionOffset = (int)binary.Position;
        WriteSingle(binary, -0.5f);
        WriteSingle(binary, -0.5f);
        WriteSingle(binary, 0);
        WriteSingle(binary, 0.5f);
        WriteSingle(binary, -0.5f);
        WriteSingle(binary, 0);
        WriteSingle(binary, 0);
        WriteSingle(binary, 0.5f);
        WriteSingle(binary, 0);
        var uvOffset = (int)binary.Position;
        WriteSingle(binary, 0);
        WriteSingle(binary, 1);
        WriteSingle(binary, 0);
        WriteSingle(binary, 1);
        WriteSingle(binary, 0);
        WriteSingle(binary, 1);
        var indexOffset = (int)binary.Position;
        WriteUInt16(binary, 0);
        WriteUInt16(binary, 1);
        WriteUInt16(binary, 2);
        PadStream(binary, 0);
        var imageOffset = (int)binary.Position;
        binary.Write(pngBytes);
        var binaryLength = checked((int)binary.Length);

        var json = $$"""
            {
              "asset": { "version": "2.0" },
              "buffers": [{ "byteLength": {{binaryLength}} }],
              "bufferViews": [
                { "buffer": 0, "byteOffset": {{positionOffset}}, "byteLength": 36, "target": 34962 },
                { "buffer": 0, "byteOffset": {{uvOffset}}, "byteLength": 24, "target": 34962 },
                { "buffer": 0, "byteOffset": {{indexOffset}}, "byteLength": 6, "target": 34963 },
                { "buffer": 0, "byteOffset": {{imageOffset}}, "byteLength": {{pngBytes.Length}} }
              ],
              "accessors": [
                { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
                { "bufferView": 1, "componentType": 5126, "count": 3, "type": "VEC2" },
                { "bufferView": 2, "componentType": 5123, "count": 3, "type": "SCALAR" }
              ],
              "images": [{ "bufferView": 3, "mimeType": "image/png", "name": "paint" }],
              "samplers": [{ "magFilter": 9728, "minFilter": 9728, "wrapS": 33071, "wrapT": 33648 }],
              "textures": [{ "source": 0, "sampler": 0 }],
              "materials": [
                { "pbrMetallicRoughness": { "baseColorFactor": [1.0, 1.0, 1.0, 1.0], "baseColorTexture": { "index": 0 } } }
              ],
              "meshes": [
                {
                  "name": "TexturedTriangle",
                  "primitives": [
                    { "attributes": { "POSITION": 0, "TEXCOORD_0": 1 }, "indices": 2, "material": 0, "mode": 4 }
                  ]
                }
              ],
              "nodes": [{ "mesh": 0 }],
              "scenes": [{ "nodes": [0] }],
              "scene": 0
            }
            """;

        return CreateGlb(Encoding.UTF8.GetBytes(json), binary.ToArray());
    }

    public static byte[] CreatePbrTexturedTriangleGlb(
        byte[] baseColorPngBytes,
        byte[] metallicRoughnessPngBytes,
        byte[] normalPngBytes)
    {
        var binary = new MemoryStream();
        var positionOffset = (int)binary.Position;
        WriteSingle(binary, -0.5f);
        WriteSingle(binary, -0.5f);
        WriteSingle(binary, 0);
        WriteSingle(binary, 0.5f);
        WriteSingle(binary, -0.5f);
        WriteSingle(binary, 0);
        WriteSingle(binary, 0);
        WriteSingle(binary, 0.5f);
        WriteSingle(binary, 0);
        var normalOffset = (int)binary.Position;
        for (var i = 0; i < 3; i++)
        {
            WriteSingle(binary, 0);
            WriteSingle(binary, 0);
            WriteSingle(binary, 1);
        }

        var uvOffset = (int)binary.Position;
        WriteSingle(binary, 0);
        WriteSingle(binary, 1);
        WriteSingle(binary, 1);
        WriteSingle(binary, 1);
        WriteSingle(binary, 0.5f);
        WriteSingle(binary, 0);
        var indexOffset = (int)binary.Position;
        WriteUInt16(binary, 0);
        WriteUInt16(binary, 1);
        WriteUInt16(binary, 2);
        PadStream(binary, 0);
        var baseImageOffset = (int)binary.Position;
        binary.Write(baseColorPngBytes);
        PadStream(binary, 0);
        var metallicRoughnessImageOffset = (int)binary.Position;
        binary.Write(metallicRoughnessPngBytes);
        PadStream(binary, 0);
        var normalImageOffset = (int)binary.Position;
        binary.Write(normalPngBytes);
        var binaryLength = checked((int)binary.Length);

        var json = $$"""
            {
              "asset": { "version": "2.0" },
              "buffers": [{ "byteLength": {{binaryLength}} }],
              "bufferViews": [
                { "buffer": 0, "byteOffset": {{positionOffset}}, "byteLength": 36, "target": 34962 },
                { "buffer": 0, "byteOffset": {{normalOffset}}, "byteLength": 36, "target": 34962 },
                { "buffer": 0, "byteOffset": {{uvOffset}}, "byteLength": 24, "target": 34962 },
                { "buffer": 0, "byteOffset": {{indexOffset}}, "byteLength": 6, "target": 34963 },
                { "buffer": 0, "byteOffset": {{baseImageOffset}}, "byteLength": {{baseColorPngBytes.Length}} },
                { "buffer": 0, "byteOffset": {{metallicRoughnessImageOffset}}, "byteLength": {{metallicRoughnessPngBytes.Length}} },
                { "buffer": 0, "byteOffset": {{normalImageOffset}}, "byteLength": {{normalPngBytes.Length}} }
              ],
              "accessors": [
                { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
                { "bufferView": 1, "componentType": 5126, "count": 3, "type": "VEC3" },
                { "bufferView": 2, "componentType": 5126, "count": 3, "type": "VEC2" },
                { "bufferView": 3, "componentType": 5123, "count": 3, "type": "SCALAR" }
              ],
              "images": [
                { "bufferView": 4, "mimeType": "image/png", "name": "base" },
                { "bufferView": 5, "mimeType": "image/png", "name": "metalrough" },
                { "bufferView": 6, "mimeType": "image/png", "name": "normal" }
              ],
              "samplers": [{ "magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497 }],
              "textures": [
                { "source": 0, "sampler": 0 },
                { "source": 1, "sampler": 0 },
                { "source": 2, "sampler": 0 }
              ],
              "materials": [
                {
                  "pbrMetallicRoughness": {
                    "baseColorFactor": [0.9, 0.8, 0.7, 1.0],
                    "metallicFactor": 0.8,
                    "roughnessFactor": 0.35,
                    "baseColorTexture": { "index": 0 },
                    "metallicRoughnessTexture": { "index": 1 }
                  },
                  "normalTexture": { "index": 2, "scale": 0.7 }
                }
              ],
              "meshes": [
                {
                  "name": "PbrTriangle",
                  "primitives": [
                    { "attributes": { "POSITION": 0, "NORMAL": 1, "TEXCOORD_0": 2 }, "indices": 3, "material": 0, "mode": 4 }
                  ]
                }
              ],
              "nodes": [{ "mesh": 0 }],
              "scenes": [{ "nodes": [0] }],
              "scene": 0
            }
            """;

        return CreateGlb(Encoding.UTF8.GetBytes(json), binary.ToArray());
    }

    public static byte[] CreateIndexedQuadGlb()
    {
        var binary = new MemoryStream();
        WriteSingle(binary, -0.5f);
        WriteSingle(binary, -0.5f);
        WriteSingle(binary, 0);
        WriteSingle(binary, 0.5f);
        WriteSingle(binary, -0.5f);
        WriteSingle(binary, 0);
        WriteSingle(binary, 0.5f);
        WriteSingle(binary, 0.5f);
        WriteSingle(binary, 0);
        WriteSingle(binary, -0.5f);
        WriteSingle(binary, 0.5f);
        WriteSingle(binary, 0);
        var indexOffset = (int)binary.Position;
        WriteUInt16(binary, 0);
        WriteUInt16(binary, 1);
        WriteUInt16(binary, 2);
        WriteUInt16(binary, 0);
        WriteUInt16(binary, 2);
        WriteUInt16(binary, 3);
        var binaryLength = checked((int)binary.Length);

        var json = $$"""
            {
              "asset": { "version": "2.0" },
              "buffers": [{ "byteLength": {{binaryLength}} }],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 48, "target": 34962 },
                { "buffer": 0, "byteOffset": {{indexOffset}}, "byteLength": 12, "target": 34963 }
              ],
              "accessors": [
                { "bufferView": 0, "componentType": 5126, "count": 4, "type": "VEC3" },
                { "bufferView": 1, "componentType": 5123, "count": 6, "type": "SCALAR" }
              ],
              "meshes": [
                {
                  "name": "Quad",
                  "primitives": [
                    { "attributes": { "POSITION": 0 }, "indices": 1, "mode": 4 }
                  ]
                }
              ],
              "nodes": [{ "mesh": 0 }],
              "scenes": [{ "nodes": [0] }],
              "scene": 0
            }
            """;

        return CreateGlb(Encoding.UTF8.GetBytes(json), binary.ToArray());
    }

    private static byte[] Pad(byte[] bytes, byte value)
    {
        var paddedLength = (bytes.Length + 3) & ~3;
        if (paddedLength == bytes.Length)
        {
            return bytes;
        }

        var padded = new byte[paddedLength];
        bytes.CopyTo(padded, 0);
        Array.Fill(padded, value, bytes.Length, padded.Length - bytes.Length);
        return padded;
    }

    private static void PadStream(Stream stream, byte value)
    {
        while (stream.Position % 4 != 0)
        {
            stream.WriteByte(value);
        }
    }

    private static void WriteSingle(Stream stream, float value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, BitConverter.SingleToInt32Bits(value));
        stream.Write(bytes);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static byte[] CreateGlb(byte[] json, byte[] binary)
    {
        var jsonBytes = Pad(json, 0x20);
        var binaryBytes = Pad(binary, 0);
        var output = new MemoryStream();
        WriteUInt32(output, 0x46546C67);
        WriteUInt32(output, 2);
        WriteUInt32(output, checked((uint)(12 + 8 + jsonBytes.Length + 8 + binaryBytes.Length)));
        WriteUInt32(output, checked((uint)jsonBytes.Length));
        WriteUInt32(output, 0x4E4F534A);
        output.Write(jsonBytes);
        WriteUInt32(output, checked((uint)binaryBytes.Length));
        WriteUInt32(output, 0x004E4942);
        output.Write(binaryBytes);
        return output.ToArray();
    }
}
