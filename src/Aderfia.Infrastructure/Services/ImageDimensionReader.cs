using System.Buffers.Binary;

namespace Aderfia.Infrastructure.Services;

/// <summary>
/// Reads pixel dimensions straight from an image file's header.
/// <para>
/// Deliberately dependency-free. The admin only needs width and height — to
/// record the aspect ratio the storefront reserves space from — and pulling in
/// a full imaging library (with its own licence terms) to read four numbers
/// would be a poor trade. Resizing or thumbnailing would justify one; this
/// does not.
/// </para>
/// <para>
/// Supports JPEG, PNG, GIF and WebP, which covers everything the upload
/// endpoint accepts.
/// </para>
/// </summary>
public static class ImageDimensionReader
{
    /// <summary>
    /// Returns (width, height), or (0, 0) when the format is not recognised.
    /// A zero result is not fatal: the caller falls back to a sensible ratio.
    /// </summary>
    public static (int Width, int Height) Read(Stream stream)
    {
        if (!stream.CanSeek) return (0, 0);

        var origin = stream.Position;
        try
        {
            stream.Position = 0;
            Span<byte> header = stackalloc byte[32];
            var read = stream.Read(header);
            if (read < 24) return (0, 0);

            // ---- PNG: 8-byte signature, then IHDR with width/height ----
            if (header[..8].SequenceEqual<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            {
                return (
                    BinaryPrimitives.ReadInt32BigEndian(header[16..20]),
                    BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
            }

            // ---- GIF: dimensions are little-endian at offset 6 ----
            if (header[0] == 'G' && header[1] == 'I' && header[2] == 'F')
            {
                return (
                    BinaryPrimitives.ReadUInt16LittleEndian(header[6..8]),
                    BinaryPrimitives.ReadUInt16LittleEndian(header[8..10]));
            }

            // ---- WebP: "RIFF" .... "WEBP" ----
            if (header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
                && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P')
            {
                return ReadWebP(stream, header);
            }

            // ---- JPEG: walk the segment chain to a start-of-frame marker ----
            if (header[0] == 0xFF && header[1] == 0xD8) return ReadJpeg(stream);

            return (0, 0);
        }
        catch (Exception)
        {
            // A malformed upload must not take down the request; the caller
            // simply records unknown dimensions.
            return (0, 0);
        }
        finally
        {
            stream.Position = origin;
        }
    }

    private static (int, int) ReadWebP(Stream stream, ReadOnlySpan<byte> header)
    {
        var format = header[12..16];

        // Simple lossy: 14-bit dimensions after the 3-byte start code.
        if (format.SequenceEqual("VP8 "u8))
        {
            stream.Position = 26;
            Span<byte> b = stackalloc byte[4];
            if (stream.Read(b) < 4) return (0, 0);
            return (
                BinaryPrimitives.ReadUInt16LittleEndian(b[..2]) & 0x3FFF,
                BinaryPrimitives.ReadUInt16LittleEndian(b[2..4]) & 0x3FFF);
        }

        // Lossless: 14-bit values packed across four bytes.
        if (format.SequenceEqual("VP8L"u8))
        {
            stream.Position = 21;
            Span<byte> b = stackalloc byte[4];
            if (stream.Read(b) < 4) return (0, 0);
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(b);
            return ((int)((bits & 0x3FFF) + 1), (int)(((bits >> 14) & 0x3FFF) + 1));
        }

        // Extended: 24-bit "canvas size minus one".
        if (format.SequenceEqual("VP8X"u8))
        {
            stream.Position = 24;
            Span<byte> b = stackalloc byte[6];
            if (stream.Read(b) < 6) return (0, 0);
            var w = b[0] | (b[1] << 8) | (b[2] << 16);
            var h = b[3] | (b[4] << 8) | (b[5] << 16);
            return (w + 1, h + 1);
        }

        return (0, 0);
    }

    private static (int, int) ReadJpeg(Stream stream)
    {
        stream.Position = 2;
        Span<byte> marker = stackalloc byte[2];
        Span<byte> length = stackalloc byte[2];
        Span<byte> frame = stackalloc byte[5];

        while (stream.Read(marker) == 2)
        {
            if (marker[0] != 0xFF) return (0, 0);

            // Standalone markers carry no payload.
            if (marker[1] is 0xD8 or 0x01 || (marker[1] >= 0xD0 && marker[1] <= 0xD7)) continue;

            if (stream.Read(length) != 2) return (0, 0);
            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(length);
            if (segmentLength < 2) return (0, 0);

            /* SOF0-SOF15 hold the frame dimensions. DHT (C4), JPGA (C8) and
               DAC (CC) sit inside that numeric range but are not frames. */
            var isStartOfFrame = marker[1] >= 0xC0 && marker[1] <= 0xCF
                                 && marker[1] != 0xC4 && marker[1] != 0xC8 && marker[1] != 0xCC;

            if (isStartOfFrame)
            {
                if (stream.Read(frame) != 5) return (0, 0);
                // frame[0] is precision; then height, then width.
                return (
                    BinaryPrimitives.ReadUInt16BigEndian(frame[3..5]),
                    BinaryPrimitives.ReadUInt16BigEndian(frame[1..3]));
            }

            stream.Position += segmentLength - 2;
        }

        return (0, 0);
    }
}
