using System.Text;

namespace TerraRuntime.World;

public enum WorldFileBestiaryEncodeResult : byte
{
    Encoded = 0,
    InvalidData = 1,
    EntryBudgetExceeded = 2,
    DuplicatePersistentId = 3,
    InvalidPersistentId = 4,
    StringTooLarge = 5,
    TotalStringBudgetExceeded = 6,
    InvalidKillCount = 7,
    DestinationNotWritable = 8,
    WriteFailed = 9
}

/// <summary>
/// Encodes the Terraria 1.4.5.8 bestiary persistence section. Duplicate IDs inside one collection are rejected
/// before writing instead of relying on decoder normalization, keeping restart-visible state canonical.
/// </summary>
public static class WorldFileBestiaryEncoder
{
    private const int MaximumKillCount = 999_999_999;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static WorldFileBestiaryEncodeResult TryEncode(
        WorldBestiaryData source,
        WorldFileBestiaryLimits limits,
        Stream destination,
        out long bytesWritten)
    {
        ArgumentNullException.ThrowIfNull(source);
        limits.Validate();
        ArgumentNullException.ThrowIfNull(destination);
        bytesWritten = 0;

        if (!destination.CanWrite)
            return WorldFileBestiaryEncodeResult.DestinationNotWritable;

        WorldFileBestiaryEncodeResult validation = Validate(source, limits, out long encodedLength);
        if (validation != WorldFileBestiaryEncodeResult.Encoded)
            return validation;

        try
        {
            using var writer = new BinaryWriter(destination, StrictUtf8, leaveOpen: true);
            writer.Write(source.Kills.Length);
            foreach (WorldBestiaryKill kill in source.Kills)
            {
                writer.Write(kill.PersistentId);
                writer.Write(kill.KillCount);
            }

            writer.Write(source.Sightings.Length);
            foreach (string persistentId in source.Sightings)
                writer.Write(persistentId);

            writer.Write(source.Chats.Length);
            foreach (string persistentId in source.Chats)
                writer.Write(persistentId);
            writer.Flush();
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            bytesWritten = 0;
            return WorldFileBestiaryEncodeResult.WriteFailed;
        }

        bytesWritten = encodedLength;
        return WorldFileBestiaryEncodeResult.Encoded;
    }

    private static WorldFileBestiaryEncodeResult Validate(
        WorldBestiaryData source,
        WorldFileBestiaryLimits limits,
        out long encodedLength)
    {
        encodedLength = 0;
        if (source.Kills is null || source.Sightings is null || source.Chats is null)
            return WorldFileBestiaryEncodeResult.InvalidData;
        if (source.Kills.Length > limits.MaxKillEntries ||
            source.Sightings.Length > limits.MaxSightEntries ||
            source.Chats.Length > limits.MaxChatEntries)
        {
            return WorldFileBestiaryEncodeResult.EntryBudgetExceeded;
        }

        long totalStringBytes = 0;
        encodedLength = sizeof(int);
        var killIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (WorldBestiaryKill kill in source.Kills)
        {
            WorldFileBestiaryEncodeResult idResult = ValidatePersistentId(
                kill.PersistentId,
                limits,
                ref totalStringBytes,
                ref encodedLength);
            if (idResult != WorldFileBestiaryEncodeResult.Encoded)
                return idResult;
            if (!killIds.Add(kill.PersistentId))
                return WorldFileBestiaryEncodeResult.DuplicatePersistentId;
            if (kill.KillCount < 0 || kill.KillCount > MaximumKillCount)
                return WorldFileBestiaryEncodeResult.InvalidKillCount;
            encodedLength = checked(encodedLength + sizeof(int));
        }

        encodedLength = checked(encodedLength + sizeof(int));
        var sightIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string persistentId in source.Sightings)
        {
            WorldFileBestiaryEncodeResult idResult = ValidatePersistentId(
                persistentId,
                limits,
                ref totalStringBytes,
                ref encodedLength);
            if (idResult != WorldFileBestiaryEncodeResult.Encoded)
                return idResult;
            if (!sightIds.Add(persistentId))
                return WorldFileBestiaryEncodeResult.DuplicatePersistentId;
        }

        encodedLength = checked(encodedLength + sizeof(int));
        var chatIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string persistentId in source.Chats)
        {
            WorldFileBestiaryEncodeResult idResult = ValidatePersistentId(
                persistentId,
                limits,
                ref totalStringBytes,
                ref encodedLength);
            if (idResult != WorldFileBestiaryEncodeResult.Encoded)
                return idResult;
            if (!chatIds.Add(persistentId))
                return WorldFileBestiaryEncodeResult.DuplicatePersistentId;
        }

        return WorldFileBestiaryEncodeResult.Encoded;
    }

    private static WorldFileBestiaryEncodeResult ValidatePersistentId(
        string persistentId,
        WorldFileBestiaryLimits limits,
        ref long totalStringBytes,
        ref long encodedLength)
    {
        if (persistentId is null)
            return WorldFileBestiaryEncodeResult.InvalidPersistentId;

        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(persistentId);
        }
        catch (EncoderFallbackException)
        {
            return WorldFileBestiaryEncodeResult.InvalidPersistentId;
        }

        if (byteCount > limits.MaxPersistentIdBytes)
            return WorldFileBestiaryEncodeResult.StringTooLarge;
        if (byteCount > limits.MaxTotalPersistentIdBytes - totalStringBytes)
            return WorldFileBestiaryEncodeResult.TotalStringBudgetExceeded;

        totalStringBytes += byteCount;
        encodedLength = checked(encodedLength + Get7BitEncodedIntLength(byteCount) + byteCount);
        return WorldFileBestiaryEncodeResult.Encoded;
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
