using System.Text;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

public enum WorldFileChestEncodeResult : byte
{
    Encoded = 0,
    TooManyChests = 1,
    NonCanonicalSlotOrder = 2,
    InvalidChestCoordinates = 3,
    InvalidName = 4,
    InvalidItemCount = 5,
    InvalidItemState = 6,
    InvalidItemType = 7,
    DestinationNotWritable = 8,
    WriteFailed = 9,
    DuplicateChestCoordinates = 10
}

/// <summary>
/// Encodes the Terraria 1.4.5.8 .wld chest section from detached authoritative chest state.
/// Vanilla does not persist chest slot ids: file order becomes the slot id on load. To avoid silently changing
/// network identity across restart, only dense canonical snapshots with SlotId == file-order index are writable.
/// </summary>
public static class WorldFileChestEncoder
{
    private const int MaximumNameBytes = 4 * 1024;
    private const int MaximumRuntimeItemSlots = byte.MaxValue + 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static WorldFileChestEncodeResult TryEncode(
        ReadOnlySpan<WorldChest> source,
        WorldDimensions dimensions,
        Stream destination,
        out long bytesWritten)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(destination);
        bytesWritten = 0;

        if (!destination.CanWrite)
            return WorldFileChestEncodeResult.DestinationNotWritable;
        if (source.Length > VanillaWorldFormat326.MaximumChestSlots || source.Length > short.MaxValue)
            return WorldFileChestEncodeResult.TooManyChests;

        WorldFileChestEncodeResult validation = Validate(source, dimensions, out long encodedLength);
        if (validation != WorldFileChestEncodeResult.Encoded)
            return validation;

        try
        {
            using var writer = new BinaryWriter(destination, StrictUtf8, leaveOpen: true);
            writer.Write(checked((short)source.Length));
            foreach (WorldChest chest in source)
            {
                writer.Write(chest.X);
                writer.Write(chest.Y);
                writer.Write(chest.Name);
                writer.Write(chest.Items.Length);

                foreach (WorldChestItem item in chest.Items)
                {
                    writer.Write(checked((short)item.Stack));
                    if (item.Stack == 0)
                        continue;

                    writer.Write(item.ItemType);
                    writer.Write(item.Prefix);
                }
            }
            writer.Flush();
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            // The caller owns the destination. A failed write may already have emitted a prefix, so the caller must
            // discard that destination rather than publish it as a complete chest section.
            bytesWritten = 0;
            return WorldFileChestEncodeResult.WriteFailed;
        }

        bytesWritten = encodedLength;
        return WorldFileChestEncodeResult.Encoded;
    }

    private static WorldFileChestEncodeResult Validate(
        ReadOnlySpan<WorldChest> source,
        WorldDimensions dimensions,
        out long encodedLength)
    {
        encodedLength = sizeof(short);
        int width = dimensions.WidthTiles;
        int height = dimensions.HeightTiles;
        var positions = new HashSet<long>();

        for (int index = 0; index < source.Length; index++)
        {
            if (source[index] is not WorldChest chest)
                return WorldFileChestEncodeResult.InvalidItemState;
            if (chest.SlotId != index)
                return WorldFileChestEncodeResult.NonCanonicalSlotOrder;
            if (width < 2 || height < 2 ||
                (uint)chest.X >= (uint)(width - 1) ||
                (uint)chest.Y >= (uint)(height - 1))
            {
                return WorldFileChestEncodeResult.InvalidChestCoordinates;
            }

            long positionKey = ((long)(uint)chest.X << 32) | (uint)chest.Y;
            if (!positions.Add(positionKey))
                return WorldFileChestEncodeResult.DuplicateChestCoordinates;

            if (chest.Name is null)
                return WorldFileChestEncodeResult.InvalidName;
            if (chest.Items is null || chest.Items.Length > MaximumRuntimeItemSlots)
                return WorldFileChestEncodeResult.InvalidItemCount;

            int nameBytes;
            try
            {
                nameBytes = StrictUtf8.GetByteCount(chest.Name);
            }
            catch (EncoderFallbackException)
            {
                return WorldFileChestEncodeResult.InvalidName;
            }
            if (nameBytes > MaximumNameBytes)
                return WorldFileChestEncodeResult.InvalidName;

            encodedLength = checked(
                encodedLength +
                sizeof(int) + sizeof(int) +
                Get7BitEncodedIntLength(nameBytes) + nameBytes +
                sizeof(int));

            foreach (WorldChestItem item in chest.Items)
            {
                if (item.Stack < 0 || item.Stack > short.MaxValue)
                    return WorldFileChestEncodeResult.InvalidItemState;

                encodedLength = checked(encodedLength + sizeof(short));
                if (item.Stack == 0)
                {
                    if (item.ItemType != 0 || item.Prefix != 0)
                        return WorldFileChestEncodeResult.InvalidItemState;
                    continue;
                }

                if (!VanillaItemIds.TryCreate(item.ItemType, out ItemTypeId itemType) || itemType.IsNone)
                    return WorldFileChestEncodeResult.InvalidItemType;

                encodedLength = checked(encodedLength + sizeof(int) + sizeof(byte));
            }
        }

        return WorldFileChestEncodeResult.Encoded;
    }

    private static int Get7BitEncodedIntLength(int value)
    {
        int length = 1;
        while ((uint)value >= 0x80)
        {
            value >>= 7;
            length++;
        }
        return length;
    }
}
