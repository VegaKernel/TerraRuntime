using System.Buffers;
using System.Buffers.Binary;

namespace TerraRuntime.World;

public enum WorldSectionPayloadEncodeResult : byte
{
    Encoded = 0,
    UnsupportedVersion = 1,
    InvalidArea = 2,
    AreaOutOfBounds = 3,
    InvalidTileState = 4
}

/// <summary>
/// Encodes the uncompressed inner payload consumed by Terraria 1.4.5.8 packet 10.
/// The current slice includes complete tile state and zero chest/sign/tile-entity tails; object metadata is layered next.
/// </summary>
public static class WorldSectionPayloadEncoder
{
    public const int MaximumWidth = TerrariaSectionGeometry.WidthTiles;
    public const int MaximumHeight = TerrariaSectionGeometry.HeightTiles;

    public static WorldSectionPayloadEncodeResult TryEncodeTileOnly(
        WorldFileData world,
        int xStart,
        int yStart,
        int width,
        int height,
        out byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(world);
        payload = [];

        WorldSectionEncodingContext context = WorldSectionEncodingContext.Capture(world);
        WorldSectionPayloadEncodeResult validation = ValidateArea(context, xStart, yStart, width, height);
        if (validation != WorldSectionPayloadEncodeResult.Encoded)
            return validation;

        return TryEncodeCore(
            context,
            new LiveTileSource(world.Tiles),
            xStart,
            yStart,
            width,
            height,
            out payload);
    }

    /// <summary>
    /// Encodes a previously captured immutable network-section image. This compatibility overload captures the
    /// immutable format rules from <paramref name="world"/> before delegating to the worker-safe overload.
    /// </summary>
    public static WorldSectionPayloadEncodeResult TryEncodeTileOnly(
        WorldFileData world,
        WorldSectionTileSnapshot snapshot,
        out byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(world);
        return TryEncodeTileOnly(WorldSectionEncodingContext.Capture(world), snapshot, out payload);
    }

    /// <summary>
    /// Encodes an immutable network-section image using only immutable world-format rules. This overload is safe
    /// for asynchronous workers and does not retain or read a live <see cref="WorldFileData"/> instance.
    /// </summary>
    public static WorldSectionPayloadEncodeResult TryEncodeTileOnly(
        WorldSectionEncodingContext context,
        WorldSectionTileSnapshot snapshot,
        out byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        payload = [];

        WorldTileBounds bounds = snapshot.Bounds;
        WorldSectionPayloadEncodeResult validation = ValidateArea(
            context,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height);
        if (validation != WorldSectionPayloadEncodeResult.Encoded)
            return validation;

        WorldTileBounds expectedBounds;
        try
        {
            expectedBounds = TerrariaSectionGeometry.GetBounds(context.Dimensions, snapshot.Section);
        }
        catch (ArgumentOutOfRangeException)
        {
            return WorldSectionPayloadEncodeResult.AreaOutOfBounds;
        }

        if (expectedBounds != bounds)
            return WorldSectionPayloadEncodeResult.InvalidArea;

        return TryEncodeCore(
            context,
            new SnapshotTileSource(snapshot),
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            out payload);
    }

    private static WorldSectionPayloadEncodeResult ValidateArea(
        WorldSectionEncodingContext context,
        int xStart,
        int yStart,
        int width,
        int height)
    {
        if (context.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
            return WorldSectionPayloadEncodeResult.UnsupportedVersion;
        if (width is < 1 or > MaximumWidth || height is < 1 or > MaximumHeight || xStart < 0 || yStart < 0)
            return WorldSectionPayloadEncodeResult.InvalidArea;

        WorldDimensions dimensions = context.Dimensions;
        return xStart > dimensions.WidthTiles - width || yStart > dimensions.HeightTiles - height
            ? WorldSectionPayloadEncodeResult.AreaOutOfBounds
            : WorldSectionPayloadEncodeResult.Encoded;
    }

    private static WorldSectionPayloadEncodeResult TryEncodeCore<TTileSource>(
        WorldSectionEncodingContext context,
        TTileSource source,
        int xStart,
        int yStart,
        int width,
        int height,
        out byte[] payload)
        where TTileSource : struct, ITileSource
    {
        var output = new ArrayBufferWriter<byte>(Math.Min(64 * 1024, checked(width * height * 4 + 18)));
        WriteInt32(output, xStart);
        WriteInt32(output, yStart);
        WriteInt16(output, checked((short)width));
        WriteInt16(output, checked((short)height));

        Span<byte> encodedTile = stackalloc byte[18];
        WorldTile previous = default;
        bool hasPrevious = false;
        int previousLength = 0;
        int previousHeaderIndex = 0;
        int runLength = 0;

        for (int y = yStart; y < yStart + height; y++)
        {
            for (int x = xStart; x < xStart + width; x++)
            {
                WorldTile tile = source.Get(x, y);
                if (!ValidateTile(tile))
                {
                    payload = [];
                    return WorldSectionPayloadEncodeResult.InvalidTileState;
                }

                bool frameImportant = tile.IsActive && context.IsFrameImportant(tile.Type);
                if (hasPrevious &&
                    VanillaWorldFormat326.AllowsSaveCompressionBatching(tile.Type) &&
                    TilesEncodeIdentically(previous, tile, frameImportant))
                {
                    runLength++;
                    continue;
                }

                if (hasPrevious)
                    FlushEncodedTile(output, encodedTile, previousHeaderIndex, previousLength, runLength);

                EncodeTile(tile, frameImportant, encodedTile, out previousHeaderIndex, out previousLength);
                previous = tile;
                runLength = 0;
                hasPrevious = true;
            }
        }

        if (hasPrevious)
            FlushEncodedTile(output, encodedTile, previousHeaderIndex, previousLength, runLength);

        // Terraria's tile-block tail contains section-local chest, sign and tile-entity metadata counts.
        // This first encoder slice deliberately emits empty tails; object indexing is added separately.
        WriteInt16(output, 0);
        WriteInt16(output, 0);
        WriteInt16(output, 0);

        payload = output.WrittenSpan.ToArray();
        return WorldSectionPayloadEncodeResult.Encoded;
    }

    private static bool ValidateTile(in WorldTile tile)
    {
        if (tile.Type >= VanillaWorldFormat326.TileTypeCount || tile.Wall >= VanillaWorldFormat326.WallTypeCount)
            return false;
        if (tile.Shape > 5)
            return false;
        if (tile.LiquidAmount == 0)
            return true;
        return tile.LiquidKind <= WorldLiquidKind.Shimmer;
    }

    private static bool TilesEncodeIdentically(in WorldTile left, in WorldTile right, bool frameImportant)
    {
        if (left.Type != right.Type ||
            left.Wall != right.Wall ||
            left.Flags != right.Flags ||
            left.LiquidAmount != right.LiquidAmount ||
            left.TileColor != right.TileColor ||
            left.WallColor != right.WallColor ||
            left.Shape != right.Shape ||
            left.LiquidKind != right.LiquidKind)
        {
            return false;
        }

        return !frameImportant || left.FrameX == right.FrameX && left.FrameY == right.FrameY;
    }

    private static void EncodeTile(
        in WorldTile tile,
        bool frameImportant,
        Span<byte> buffer,
        out int headerIndex,
        out int length)
    {
        int payloadIndex = 4;
        byte header1 = 0;
        byte header2 = 0;
        byte header3 = 0;
        byte header4 = 0;

        if (tile.IsActive)
        {
            header1 |= 0x02;
            buffer[payloadIndex++] = (byte)tile.Type;
            if (tile.Type > byte.MaxValue)
            {
                buffer[payloadIndex++] = (byte)(tile.Type >> 8);
                header1 |= 0x20;
            }

            if (frameImportant)
            {
                BinaryPrimitives.WriteInt16LittleEndian(buffer[payloadIndex..], tile.FrameX);
                payloadIndex += sizeof(short);
                BinaryPrimitives.WriteInt16LittleEndian(buffer[payloadIndex..], tile.FrameY);
                payloadIndex += sizeof(short);
            }

            if (tile.TileColor != 0)
            {
                header3 |= 0x08;
                buffer[payloadIndex++] = tile.TileColor;
            }
        }

        if (tile.Wall != 0)
        {
            header1 |= 0x04;
            buffer[payloadIndex++] = (byte)tile.Wall;
            if (tile.WallColor != 0)
            {
                header3 |= 0x10;
                buffer[payloadIndex++] = tile.WallColor;
            }
        }

        if (tile.LiquidAmount != 0)
        {
            switch (tile.LiquidKind)
            {
                case WorldLiquidKind.Water:
                    header1 |= 0x08;
                    break;
                case WorldLiquidKind.Lava:
                    header1 |= 0x10;
                    break;
                case WorldLiquidKind.Honey:
                    header1 |= 0x18;
                    break;
                case WorldLiquidKind.Shimmer:
                    header1 |= 0x08;
                    header3 |= 0x80;
                    break;
            }
            buffer[payloadIndex++] = tile.LiquidAmount;
        }

        WorldTileFlags flags = tile.Flags;
        if ((flags & WorldTileFlags.WireRed) != 0) header2 |= 0x02;
        if ((flags & WorldTileFlags.WireBlue) != 0) header2 |= 0x04;
        if ((flags & WorldTileFlags.WireGreen) != 0) header2 |= 0x08;
        header2 |= checked((byte)(tile.Shape << 4));
        if ((flags & WorldTileFlags.Actuator) != 0) header3 |= 0x02;
        if ((flags & WorldTileFlags.Inactive) != 0) header3 |= 0x04;
        if ((flags & WorldTileFlags.WireYellow) != 0) header3 |= 0x20;

        if (tile.Wall > byte.MaxValue)
        {
            buffer[payloadIndex++] = (byte)(tile.Wall >> 8);
            header3 |= 0x40;
        }

        if ((flags & WorldTileFlags.InvisibleBlock) != 0) header4 |= 0x02;
        if ((flags & WorldTileFlags.InvisibleWall) != 0) header4 |= 0x04;
        if ((flags & WorldTileFlags.FullbrightBlock) != 0) header4 |= 0x08;
        if ((flags & WorldTileFlags.FullbrightWall) != 0) header4 |= 0x10;

        headerIndex = 3;
        if (header4 != 0)
        {
            header3 |= 0x01;
            buffer[headerIndex--] = header4;
        }
        if (header3 != 0)
        {
            header2 |= 0x01;
            buffer[headerIndex--] = header3;
        }
        if (header2 != 0)
        {
            header1 |= 0x01;
            buffer[headerIndex--] = header2;
        }
        buffer[headerIndex] = header1;
        length = payloadIndex - headerIndex;
    }

    private static void FlushEncodedTile(
        ArrayBufferWriter<byte> output,
        Span<byte> encodedTile,
        int headerIndex,
        int length,
        int runLength)
    {
        if (runLength > 0)
        {
            int end = headerIndex + length;
            if (runLength > byte.MaxValue)
            {
                encodedTile[headerIndex] |= 0x80;
                BinaryPrimitives.WriteUInt16LittleEndian(encodedTile[end..], checked((ushort)runLength));
                length += sizeof(ushort);
            }
            else
            {
                encodedTile[headerIndex] |= 0x40;
                encodedTile[end] = (byte)runLength;
                length++;
            }
        }

        Write(output, encodedTile.Slice(headerIndex, length));
    }

    private static void WriteInt16(ArrayBufferWriter<byte> output, short value)
    {
        Span<byte> destination = output.GetSpan(sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(destination, value);
        output.Advance(sizeof(short));
    }

    private static void WriteInt32(ArrayBufferWriter<byte> output, int value)
    {
        Span<byte> destination = output.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(destination, value);
        output.Advance(sizeof(int));
    }

    private static void Write(ArrayBufferWriter<byte> output, ReadOnlySpan<byte> value)
    {
        value.CopyTo(output.GetSpan(value.Length));
        output.Advance(value.Length);
    }

    private interface ITileSource
    {
        WorldTile Get(int x, int y);
    }

    private readonly struct LiveTileSource(WorldTileStore tiles) : ITileSource
    {
        public WorldTile Get(int x, int y) => tiles.Get(x, y);
    }

    private readonly struct SnapshotTileSource(WorldSectionTileSnapshot snapshot) : ITileSource
    {
        public WorldTile Get(int x, int y) => snapshot.GetUnchecked(x, y);
    }
}
