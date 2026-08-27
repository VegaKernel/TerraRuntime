using System.Buffers.Binary;

namespace TerraRuntime.World;

public sealed record WorldCreativePowersData(
    bool FreezeTime,
    float TimeRateSlider,
    bool FreezeRain,
    bool FreezeWind,
    float DifficultySlider,
    bool StopBiomeSpread);

public enum WorldFileCreativePowersDecodeResult : byte
{
    Decoded = 0,
    UnsupportedVersion = 1,
    InvalidSectionBounds = 2,
    Truncated = 3,
    UnknownPowerId = 4,
    DuplicatePowerId = 5,
    InvalidSliderValue = 6,
    MissingPower = 7,
    SectionLengthMismatch = 8
}

/// <summary>
/// Decodes the six world-persistent Journey/Creative powers registered by Terraria 1.4.5.8.
/// Registration order fixes their IDs at 0, 8, 9, 10, 12 and 13.
/// </summary>
public static class WorldFileCreativePowersDecoder
{
    private const ushort FreezeTimeId = 0;
    private const ushort ModifyTimeRateId = 8;
    private const ushort FreezeRainId = 9;
    private const ushort FreezeWindId = 10;
    private const ushort DifficultySliderId = 12;
    private const ushort StopBiomeSpreadId = 13;

    public static WorldFileCreativePowersDecodeResult TryDecode(
        ReadOnlySpan<byte> file,
        WorldFileEnvelope envelope,
        out WorldCreativePowersData? powers,
        out int bytesConsumed)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        powers = null;
        bytesConsumed = 0;

        if (envelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
            return WorldFileCreativePowersDecodeResult.UnsupportedVersion;
        if (envelope.SectionOffsets.Count < VanillaWorldFormat326.SectionCount)
            return WorldFileCreativePowersDecodeResult.InvalidSectionBounds;

        int start = envelope.SectionOffsets[9];
        int end = envelope.SectionOffsets[10];
        if (start < 0 || end <= start || end > file.Length)
            return WorldFileCreativePowersDecodeResult.InvalidSectionBounds;

        var reader = new CreativeReader(file.Slice(start, end - start));
        bool? freezeTime = null;
        float? timeRate = null;
        bool? freezeRain = null;
        bool? freezeWind = null;
        float? difficulty = null;
        bool? stopBiomeSpread = null;

        while (true)
        {
            if (!reader.TryReadByte(out byte hasEntry))
            {
                bytesConsumed = reader.Offset;
                return WorldFileCreativePowersDecodeResult.Truncated;
            }
            if (hasEntry == 0)
                break;

            if (!reader.TryReadUInt16(out ushort powerId))
            {
                bytesConsumed = reader.Offset;
                return WorldFileCreativePowersDecodeResult.Truncated;
            }

            switch (powerId)
            {
                case FreezeTimeId:
                    if (freezeTime.HasValue)
                        return FailDuplicate(ref reader, out bytesConsumed);
                    if (!reader.TryReadByte(out byte freezeTimeValue))
                        return FailTruncated(ref reader, out bytesConsumed);
                    freezeTime = freezeTimeValue != 0;
                    break;

                case ModifyTimeRateId:
                    if (timeRate.HasValue)
                        return FailDuplicate(ref reader, out bytesConsumed);
                    if (!reader.TryReadSingle(out float timeRateValue))
                        return FailTruncated(ref reader, out bytesConsumed);
                    if (!IsValidSlider(timeRateValue))
                    {
                        bytesConsumed = reader.Offset;
                        return WorldFileCreativePowersDecodeResult.InvalidSliderValue;
                    }
                    timeRate = timeRateValue;
                    break;

                case FreezeRainId:
                    if (freezeRain.HasValue)
                        return FailDuplicate(ref reader, out bytesConsumed);
                    if (!reader.TryReadByte(out byte freezeRainValue))
                        return FailTruncated(ref reader, out bytesConsumed);
                    freezeRain = freezeRainValue != 0;
                    break;

                case FreezeWindId:
                    if (freezeWind.HasValue)
                        return FailDuplicate(ref reader, out bytesConsumed);
                    if (!reader.TryReadByte(out byte freezeWindValue))
                        return FailTruncated(ref reader, out bytesConsumed);
                    freezeWind = freezeWindValue != 0;
                    break;

                case DifficultySliderId:
                    if (difficulty.HasValue)
                        return FailDuplicate(ref reader, out bytesConsumed);
                    if (!reader.TryReadSingle(out float difficultyValue))
                        return FailTruncated(ref reader, out bytesConsumed);
                    if (!IsValidSlider(difficultyValue))
                    {
                        bytesConsumed = reader.Offset;
                        return WorldFileCreativePowersDecodeResult.InvalidSliderValue;
                    }
                    difficulty = difficultyValue;
                    break;

                case StopBiomeSpreadId:
                    if (stopBiomeSpread.HasValue)
                        return FailDuplicate(ref reader, out bytesConsumed);
                    if (!reader.TryReadByte(out byte stopBiomeSpreadValue))
                        return FailTruncated(ref reader, out bytesConsumed);
                    stopBiomeSpread = stopBiomeSpreadValue != 0;
                    break;

                default:
                    bytesConsumed = reader.Offset;
                    return WorldFileCreativePowersDecodeResult.UnknownPowerId;
            }
        }

        bytesConsumed = reader.Offset;
        if (reader.Remaining != 0)
            return WorldFileCreativePowersDecodeResult.SectionLengthMismatch;
        if (!freezeTime.HasValue || !timeRate.HasValue || !freezeRain.HasValue || !freezeWind.HasValue ||
            !difficulty.HasValue || !stopBiomeSpread.HasValue)
        {
            return WorldFileCreativePowersDecodeResult.MissingPower;
        }

        powers = new WorldCreativePowersData(
            freezeTime.Value,
            timeRate.Value,
            freezeRain.Value,
            freezeWind.Value,
            difficulty.Value,
            stopBiomeSpread.Value);
        return WorldFileCreativePowersDecodeResult.Decoded;
    }

    private static bool IsValidSlider(float value) => float.IsFinite(value) && value is >= 0f and <= 1f;

    private static WorldFileCreativePowersDecodeResult FailDuplicate(ref CreativeReader reader, out int bytesConsumed)
    {
        bytesConsumed = reader.Offset;
        return WorldFileCreativePowersDecodeResult.DuplicatePowerId;
    }

    private static WorldFileCreativePowersDecodeResult FailTruncated(ref CreativeReader reader, out int bytesConsumed)
    {
        bytesConsumed = reader.Offset;
        return WorldFileCreativePowersDecodeResult.Truncated;
    }

    private ref struct CreativeReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public CreativeReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _offset = 0;
        }

        public int Offset => _offset;
        public int Remaining => _data.Length - _offset;

        public bool TryReadByte(out byte value)
        {
            if (_offset >= _data.Length) { value = default; return false; }
            value = _data[_offset++];
            return true;
        }

        public bool TryReadUInt16(out ushort value)
        {
            if (_data.Length - _offset < sizeof(ushort)) { value = default; return false; }
            value = BinaryPrimitives.ReadUInt16LittleEndian(_data[_offset..]);
            _offset += sizeof(ushort);
            return true;
        }

        public bool TryReadSingle(out float value)
        {
            if (_data.Length - _offset < sizeof(float)) { value = default; return false; }
            int bits = BinaryPrimitives.ReadInt32LittleEndian(_data[_offset..]);
            _offset += sizeof(float);
            value = BitConverter.Int32BitsToSingle(bits);
            return true;
        }
    }
}
