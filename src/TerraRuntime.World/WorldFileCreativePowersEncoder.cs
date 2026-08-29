namespace TerraRuntime.World;

public enum WorldFileCreativePowersEncodeResult : byte
{
    Encoded = 0,
    InvalidSliderValue = 1,
    DestinationNotWritable = 2,
    WriteFailed = 3
}

/// <summary>
/// Encodes the six world-persistent Journey/Creative powers registered by Terraria 1.4.5.8 in canonical
/// registration order: IDs 0, 8, 9, 10, 12 and 13, followed by the zero entry terminator.
/// </summary>
public static class WorldFileCreativePowersEncoder
{
    private const ushort FreezeTimeId = 0;
    private const ushort ModifyTimeRateId = 8;
    private const ushort FreezeRainId = 9;
    private const ushort FreezeWindId = 10;
    private const ushort DifficultySliderId = 12;
    private const ushort StopBiomeSpreadId = 13;
    private const long CanonicalEncodedLength =
        (4L * (sizeof(byte) + sizeof(ushort) + sizeof(byte))) +
        (2L * (sizeof(byte) + sizeof(ushort) + sizeof(float))) +
        sizeof(byte);

    public static WorldFileCreativePowersEncodeResult TryEncode(
        WorldCreativePowersData source,
        Stream destination,
        out long bytesWritten)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        bytesWritten = 0;

        if (!destination.CanWrite)
            return WorldFileCreativePowersEncodeResult.DestinationNotWritable;
        if (!IsValidSlider(source.TimeRateSlider) || !IsValidSlider(source.DifficultySlider))
            return WorldFileCreativePowersEncodeResult.InvalidSliderValue;

        try
        {
            using var writer = new BinaryWriter(destination, System.Text.Encoding.UTF8, leaveOpen: true);
            WriteBooleanPower(writer, FreezeTimeId, source.FreezeTime);
            WriteFloatPower(writer, ModifyTimeRateId, source.TimeRateSlider);
            WriteBooleanPower(writer, FreezeRainId, source.FreezeRain);
            WriteBooleanPower(writer, FreezeWindId, source.FreezeWind);
            WriteFloatPower(writer, DifficultySliderId, source.DifficultySlider);
            WriteBooleanPower(writer, StopBiomeSpreadId, source.StopBiomeSpread);
            writer.Write((byte)0);
            writer.Flush();
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            bytesWritten = 0;
            return WorldFileCreativePowersEncodeResult.WriteFailed;
        }

        bytesWritten = CanonicalEncodedLength;
        return WorldFileCreativePowersEncodeResult.Encoded;
    }

    private static void WriteBooleanPower(BinaryWriter writer, ushort id, bool value)
    {
        writer.Write((byte)1);
        writer.Write(id);
        writer.Write((byte)(value ? 1 : 0));
    }

    private static void WriteFloatPower(BinaryWriter writer, ushort id, float value)
    {
        writer.Write((byte)1);
        writer.Write(id);
        writer.Write(value);
    }

    private static bool IsValidSlider(float value) => float.IsFinite(value) && value is >= 0f and <= 1f;
}
