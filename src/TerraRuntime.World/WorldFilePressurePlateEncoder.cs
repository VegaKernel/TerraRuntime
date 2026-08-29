using System.Buffers.Binary;

namespace TerraRuntime.World;

public enum WorldFilePressurePlateEncodeResult : byte
{
    Encoded = 0,
    InvalidCoordinates = 1,
    DuplicateCoordinates = 2,
    DestinationNotWritable = 3,
    WriteFailed = 4
}

/// <summary>
/// Encodes the Terraria 1.4.5.8 .wld pressure-plate section. The layout is the exact inverse of
/// <see cref="WorldFilePressurePlateDecoder"/>: an Int32 count followed by Int32 X/Y pairs.
/// </summary>
public static class WorldFilePressurePlateEncoder
{
    public static WorldFilePressurePlateEncodeResult TryEncode(
        ReadOnlySpan<WorldPressurePlate> source,
        WorldDimensions dimensions,
        Stream destination,
        out long bytesWritten)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(destination);
        bytesWritten = 0;

        if (!destination.CanWrite)
            return WorldFilePressurePlateEncodeResult.DestinationNotWritable;

        var seen = new HashSet<long>();
        foreach (WorldPressurePlate pressurePlate in source)
        {
            if ((uint)pressurePlate.X >= (uint)dimensions.WidthTiles ||
                (uint)pressurePlate.Y >= (uint)dimensions.HeightTiles)
            {
                return WorldFilePressurePlateEncodeResult.InvalidCoordinates;
            }

            long key = ((long)(uint)pressurePlate.X << 32) | (uint)pressurePlate.Y;
            if (!seen.Add(key))
                return WorldFilePressurePlateEncodeResult.DuplicateCoordinates;
        }

        long encodedLength = sizeof(int) + (long)source.Length * (sizeof(int) * 2);
        try
        {
            Span<byte> buffer = stackalloc byte[sizeof(int) * 2];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, source.Length);
            destination.Write(buffer[..sizeof(int)]);

            foreach (WorldPressurePlate pressurePlate in source)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buffer, pressurePlate.X);
                BinaryPrimitives.WriteInt32LittleEndian(buffer[sizeof(int)..], pressurePlate.Y);
                destination.Write(buffer);
            }
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            bytesWritten = 0;
            return WorldFilePressurePlateEncodeResult.WriteFailed;
        }

        bytesWritten = encodedLength;
        return WorldFilePressurePlateEncodeResult.Encoded;
    }
}
