using System.Text;

namespace TerraRuntime.World;

public enum WorldFileNpcEncodeResult : byte
{
    Encoded = 0,
    InvalidPersistence = 1,
    InvalidShimmerCount = 2,
    InvalidShimmerIndex = 3,
    TownNpcBudgetExceeded = 4,
    PersistentNpcBudgetExceeded = 5,
    InvalidName = 6,
    NameBudgetExceeded = 7,
    NonFinitePosition = 8,
    DestinationNotWritable = 9,
    WriteFailed = 10
}

/// <summary>
/// Encodes the Terraria 1.4.5.8 inert NPC persistence section. This is persistence state only; runtime NPC
/// identity, AI and replication handles are intentionally outside the .wld contract.
/// </summary>
public static class WorldFileNpcEncoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static WorldFileNpcEncodeResult TryEncode(
        WorldNpcPersistence source,
        WorldFileNpcDecodeOptions options,
        Stream destination,
        out long bytesWritten)
    {
        ArgumentNullException.ThrowIfNull(source);
        options.Validate();
        ArgumentNullException.ThrowIfNull(destination);
        bytesWritten = 0;

        if (!destination.CanWrite)
            return WorldFileNpcEncodeResult.DestinationNotWritable;

        WorldFileNpcEncodeResult validation = Validate(source, options, out long encodedLength);
        if (validation != WorldFileNpcEncodeResult.Encoded)
            return validation;

        try
        {
            using var writer = new BinaryWriter(destination, StrictUtf8, leaveOpen: true);
            writer.Write(source.ShimmeredTownNpcIndices.Length);
            foreach (int index in source.ShimmeredTownNpcIndices)
                writer.Write(index);

            foreach (WorldTownNpc npc in source.TownNpcs)
            {
                writer.Write(true);
                writer.Write(npc.NetId);
                writer.Write(npc.GivenName);
                writer.Write(npc.X);
                writer.Write(npc.Y);
                writer.Write(npc.Homeless);
                writer.Write(npc.HomeTileX);
                writer.Write(npc.HomeTileY);
                byte bits = npc.TownNpcVariationIndex.HasValue ? (byte)0x01 : (byte)0;
                writer.Write(bits);
                if (npc.TownNpcVariationIndex is int variation)
                    writer.Write(variation);
                writer.Write(npc.HomelessDespawn);
            }
            writer.Write(false);

            foreach (WorldPersistentNpc npc in source.PersistentNpcs)
            {
                writer.Write(true);
                writer.Write(npc.NetId);
                writer.Write(npc.X);
                writer.Write(npc.Y);
            }
            writer.Write(false);
            writer.Flush();
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            bytesWritten = 0;
            return WorldFileNpcEncodeResult.WriteFailed;
        }

        bytesWritten = encodedLength;
        return WorldFileNpcEncodeResult.Encoded;
    }

    private static WorldFileNpcEncodeResult Validate(
        WorldNpcPersistence source,
        WorldFileNpcDecodeOptions options,
        out long encodedLength)
    {
        encodedLength = sizeof(int);
        if (source.ShimmeredTownNpcIndices is null || source.TownNpcs is null || source.PersistentNpcs is null)
            return WorldFileNpcEncodeResult.InvalidPersistence;
        if (source.ShimmeredTownNpcIndices.Length > options.MaxShimmeredTownNpcIndices)
            return WorldFileNpcEncodeResult.InvalidShimmerCount;

        encodedLength = checked(encodedLength + ((long)source.ShimmeredTownNpcIndices.Length * sizeof(int)));
        foreach (int index in source.ShimmeredTownNpcIndices)
        {
            if ((uint)index >= (uint)options.MaxShimmerIndexExclusive)
                return WorldFileNpcEncodeResult.InvalidShimmerIndex;
        }

        if (source.TownNpcs.Length > options.MaxTownNpcs)
            return WorldFileNpcEncodeResult.TownNpcBudgetExceeded;

        long totalNameBytes = 0;
        foreach (WorldTownNpc npc in source.TownNpcs)
        {
            if (npc is null || npc.GivenName is null)
                return WorldFileNpcEncodeResult.InvalidName;
            if (!float.IsFinite(npc.X) || !float.IsFinite(npc.Y))
                return WorldFileNpcEncodeResult.NonFinitePosition;

            int nameBytes;
            try
            {
                nameBytes = StrictUtf8.GetByteCount(npc.GivenName);
            }
            catch (EncoderFallbackException)
            {
                return WorldFileNpcEncodeResult.InvalidName;
            }

            if (nameBytes > options.MaxNameBytesPerTownNpc ||
                nameBytes > options.MaxTotalNameBytes - totalNameBytes)
            {
                return WorldFileNpcEncodeResult.NameBudgetExceeded;
            }
            totalNameBytes += nameBytes;

            encodedLength = checked(
                encodedLength +
                sizeof(byte) + sizeof(int) +
                Get7BitEncodedIntLength(nameBytes) + nameBytes +
                sizeof(float) + sizeof(float) + sizeof(byte) +
                sizeof(int) + sizeof(int) + sizeof(byte) +
                (npc.TownNpcVariationIndex.HasValue ? sizeof(int) : 0) +
                sizeof(byte));
        }
        encodedLength = checked(encodedLength + sizeof(byte));

        if (source.PersistentNpcs.Length > options.MaxPersistentNpcs)
            return WorldFileNpcEncodeResult.PersistentNpcBudgetExceeded;
        foreach (WorldPersistentNpc npc in source.PersistentNpcs)
        {
            if (!float.IsFinite(npc.X) || !float.IsFinite(npc.Y))
                return WorldFileNpcEncodeResult.NonFinitePosition;
            encodedLength = checked(
                encodedLength + sizeof(byte) + sizeof(int) + sizeof(float) + sizeof(float));
        }
        encodedLength = checked(encodedLength + sizeof(byte));

        return WorldFileNpcEncodeResult.Encoded;
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
