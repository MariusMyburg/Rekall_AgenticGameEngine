using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Rekall.Age.Rendering;

public static class RekallAgePngReader
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static async ValueTask<RekallAgeRgbaImage> ReadRgbaAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return ReadRgba(bytes);
    }

    private static RekallAgeRgbaImage ReadRgba(byte[] bytes)
    {
        if (bytes.Length < Signature.Length || !bytes.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            throw new InvalidDataException("File is not a PNG image.");
        }

        var offset = Signature.Length;
        var width = 0;
        var height = 0;
        var colorType = 0;
        var bitDepth = 0;
        using var idat = new MemoryStream();
        var sawHeader = false;

        while (offset < bytes.Length)
        {
            if (offset + 8 > bytes.Length)
            {
                throw new InvalidDataException("PNG chunk header is truncated.");
            }

            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
            offset += 4;
            if (length < 0)
            {
                throw new InvalidDataException("PNG chunk length is invalid.");
            }

            var type = Encoding.ASCII.GetString(bytes, offset, 4);
            offset += 4;
            if (offset + length + 4 > bytes.Length)
            {
                throw new InvalidDataException($"PNG chunk {type} is truncated.");
            }

            var data = bytes.AsSpan(offset, length);
            offset += length + 4;

            switch (type)
            {
                case "IHDR":
                    if (length != 13)
                    {
                        throw new InvalidDataException("PNG IHDR chunk has an invalid length.");
                    }

                    width = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
                    height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4));
                    bitDepth = data[8];
                    colorType = data[9];
                    var compression = data[10];
                    var filter = data[11];
                    var interlace = data[12];
                    if (width <= 0 || height <= 0)
                    {
                        throw new InvalidDataException("PNG image dimensions must be positive.");
                    }

                    if (bitDepth != 8)
                    {
                        throw new InvalidDataException("Only 8-bit PNG images are supported.");
                    }

                    if (colorType is not 2 and not 6)
                    {
                        throw new InvalidDataException("Only RGB and RGBA PNG images are supported.");
                    }

                    if (compression != 0 || filter != 0 || interlace != 0)
                    {
                        throw new InvalidDataException("Only standard, non-interlaced PNG images are supported.");
                    }

                    sawHeader = true;
                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
                case "IEND":
                    offset = bytes.Length;
                    break;
            }
        }

        if (!sawHeader)
        {
            throw new InvalidDataException("PNG image has no IHDR chunk.");
        }

        if (idat.Length == 0)
        {
            throw new InvalidDataException("PNG image has no IDAT data.");
        }

        var channels = colorType == 6 ? 4 : 3;
        var rowLength = checked(width * channels);
        var rgba = new byte[checked(width * height * 4)];
        var inflated = Inflate(idat.ToArray());
        var expectedLength = checked((rowLength + 1) * height);
        if (inflated.Length != expectedLength)
        {
            throw new InvalidDataException("PNG decompressed data length does not match image dimensions.");
        }

        var previous = new byte[rowLength];
        var current = new byte[rowLength];
        var sourceOffset = 0;
        for (var y = 0; y < height; y++)
        {
            var filter = inflated[sourceOffset++];
            var row = inflated.AsSpan(sourceOffset, rowLength);
            sourceOffset += rowLength;
            ReconstructRow(filter, row, previous, current, channels);
            CopyRowToRgba(current, rgba, y, width, channels);
            (previous, current) = (current, previous);
            Array.Clear(current);
        }

        return new RekallAgeRgbaImage(width, height, rgba);
    }

    private static byte[] Inflate(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static void ReconstructRow(
        byte filter,
        ReadOnlySpan<byte> row,
        byte[] previous,
        byte[] current,
        int bytesPerPixel)
    {
        for (var i = 0; i < row.Length; i++)
        {
            var left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
            var up = previous[i];
            var upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
            var predictor = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upLeft),
                _ => throw new InvalidDataException($"PNG filter {filter} is not supported.")
            };
            current[i] = unchecked((byte)(row[i] + predictor));
        }
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var leftDistance = Math.Abs(estimate - left);
        var upDistance = Math.Abs(estimate - up);
        var upLeftDistance = Math.Abs(estimate - upLeft);
        if (leftDistance <= upDistance && leftDistance <= upLeftDistance)
        {
            return left;
        }

        return upDistance <= upLeftDistance ? up : upLeft;
    }

    private static void CopyRowToRgba(byte[] row, byte[] rgba, int y, int width, int channels)
    {
        for (var x = 0; x < width; x++)
        {
            var source = x * channels;
            var destination = (y * width + x) * 4;
            rgba[destination + 0] = row[source + 0];
            rgba[destination + 1] = row[source + 1];
            rgba[destination + 2] = row[source + 2];
            rgba[destination + 3] = channels == 4 ? row[source + 3] : (byte)255;
        }
    }
}
