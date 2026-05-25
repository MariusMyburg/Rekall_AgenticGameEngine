using System.Buffers.Binary;
using System.IO.Compression;

namespace Rekall.Age.Rendering;

public static class RekallAgePngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static async ValueTask WriteRgbaAsync(
        string path,
        int width,
        int height,
        byte[] rgba,
        CancellationToken cancellationToken)
    {
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException("RGBA buffer length does not match image dimensions.", nameof(rgba));
        }

        using var stream = File.Create(path);
        await stream.WriteAsync(Signature, cancellationToken).ConfigureAwait(false);
        await WriteChunkAsync(stream, "IHDR", CreateHeader(width, height), cancellationToken).ConfigureAwait(false);
        await WriteChunkAsync(stream, "IDAT", CompressRows(width, height, rgba), cancellationToken).ConfigureAwait(false);
        await WriteChunkAsync(stream, "IEND", Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);
    }

    private static byte[] CreateHeader(int width, int height)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        return header;
    }

    private static byte[] CompressRows(int width, int height, byte[] rgba)
    {
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba, y * width * 4, width * 4);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        return compressed.ToArray();
    }

    private static async ValueTask WriteChunkAsync(
        Stream stream,
        string type,
        byte[] data,
        CancellationToken cancellationToken)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        await stream.WriteAsync(typeBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);

        var crcInput = new byte[typeBytes.Length + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
        Buffer.BlockCopy(data, 0, crcInput, typeBytes.Length, data.Length);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, ComputeCrc32(crcInput));
        await stream.WriteAsync(crc, cancellationToken).ConfigureAwait(false);
    }

    private static uint ComputeCrc32(byte[] bytes)
    {
        var crc = 0xffffffffu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                var mask = 0u - (crc & 1u);
                crc = (crc >> 1) ^ (0xedb88320u & mask);
            }
        }

        return ~crc;
    }
}
