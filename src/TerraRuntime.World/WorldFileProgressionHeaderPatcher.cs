using System.Buffers.Binary;
using System.Text;

namespace TerraRuntime.World;

public enum WorldFileProgressionHeaderPatchResult : byte
{
    Patched = 0,
    InvalidHeader = 1,
    UnsupportedMutation = 2
}

/// <summary>
/// Applies source-backed runtime progression mutations to a validated Terraria 1.4.5.8 world-header section.
/// Only bytes explicitly owned by a supported milestone are changed; every other byte is preserved verbatim.
/// Unknown mutation bits fail closed rather than being silently discarded during a save.
/// </summary>
public static class WorldFileProgressionHeaderPatcher
{
    private const int MaximumStringBytes = 4 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const ulong SupportedMutationMask = 1UL << (int)VanillaWorldProgressionId.KingSlime;

    public static WorldFileProgressionHeaderPatchResult TryPatch(
        ReadOnlySpan<byte> sourceHeader,
        WorldFileHeader header,
        in RuntimeWorldProgressionMutationSnapshot mutations,
        out byte[] patchedHeader)
    {
        ArgumentNullException.ThrowIfNull(header);
        patchedHeader = [];

        if ((mutations.CompletedMask & ~SupportedMutationMask) != 0)
            return WorldFileProgressionHeaderPatchResult.UnsupportedMutation;

        var reader = new HeaderPrefixReader(sourceHeader);
        if (!reader.TryReadString(out string name) ||
            !reader.TryReadString(out string seed) ||
            !reader.TryReadUInt64(out ulong generatorVersion) ||
            !reader.TryReadGuid(out Guid uniqueId) ||
            !reader.TryReadInt32(out int worldId) ||
            !reader.TryReadInt32(out int leftWorld) ||
            !reader.TryReadInt32(out int rightWorld) ||
            !reader.TryReadInt32(out int topWorld) ||
            !reader.TryReadInt32(out int bottomWorld) ||
            !reader.TryReadInt32(out int heightTiles) ||
            !reader.TryReadInt32(out int widthTiles))
        {
            return WorldFileProgressionHeaderPatchResult.InvalidHeader;
        }

        if (!string.Equals(name, header.Name, StringComparison.Ordinal) ||
            !string.Equals(seed, header.SeedText, StringComparison.Ordinal) ||
            generatorVersion != header.WorldGeneratorVersion ||
            uniqueId != header.UniqueId ||
            worldId != header.WorldId ||
            leftWorld != header.LeftWorld ||
            rightWorld != header.RightWorld ||
            topWorld != header.TopWorld ||
            bottomWorld != header.BottomWorld ||
            heightTiles != header.Dimensions.HeightTiles ||
            widthTiles != header.Dimensions.WidthTiles)
        {
            return WorldFileProgressionHeaderPatchResult.InvalidHeader;
        }

        // gameMode, 9 seed flags, creation/last-played ticks, moon type,
        // tree/cave background tables, spawn, world-surface and rock-layer.
        const int bytesBeforeTime =
            sizeof(int) +
            9 +
            (sizeof(long) * 2) +
            sizeof(byte) +
            (sizeof(int) * 3) +
            (sizeof(int) * 4) +
            (sizeof(int) * 3) +
            (sizeof(int) * 4) +
            (sizeof(int) * 3) +
            (sizeof(int) * 2) +
            (sizeof(double) * 2);
        if (!reader.TrySkip(bytesBeforeTime) ||
            !reader.TrySkip(sizeof(double)) ||
            !reader.TryReadBool(out _) ||
            !reader.TrySkip(sizeof(int)) ||
            !reader.TryReadBool(out _) ||
            !reader.TryReadBool(out _) ||
            !reader.TrySkip(sizeof(int) * 2))
        {
            return WorldFileProgressionHeaderPatchResult.InvalidHeader;
        }

        // crimson; downedBoss1/2/3; Queen Bee; mech 1/2/3/any; Plantera; Golem.
        for (int index = 0; index < 11; index++)
        {
            if (!reader.TryReadBool(out _))
                return WorldFileProgressionHeaderPatchResult.InvalidHeader;
        }

        int downedSlimeKingOffset = reader.Offset;
        if (!reader.TryReadBool(out bool persistedDownedSlimeKing))
            return WorldFileProgressionHeaderPatchResult.InvalidHeader;

        patchedHeader = sourceHeader.ToArray();
        if (mutations.IsCompleted(VanillaWorldProgressionId.KingSlime) && !persistedDownedSlimeKing)
            patchedHeader[downedSlimeKingOffset] = 1;

        return WorldFileProgressionHeaderPatchResult.Patched;
    }

    private ref struct HeaderPrefixReader
    {
        private readonly ReadOnlySpan<byte> data;
        private int offset;

        public HeaderPrefixReader(ReadOnlySpan<byte> data)
        {
            this.data = data;
            offset = 0;
        }

        public int Offset => offset;

        public bool TrySkip(int length)
        {
            if (length < 0 || data.Length - offset < length)
                return false;

            offset += length;
            return true;
        }

        public bool TryReadBool(out bool value)
        {
            if (offset >= data.Length || data[offset] > 1)
            {
                value = default;
                return false;
            }

            value = data[offset++] != 0;
            return true;
        }

        public bool TryReadInt32(out int value)
        {
            if (data.Length - offset < sizeof(int))
            {
                value = default;
                return false;
            }

            value = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
            offset += sizeof(int);
            return true;
        }

        public bool TryReadUInt64(out ulong value)
        {
            if (data.Length - offset < sizeof(ulong))
            {
                value = default;
                return false;
            }

            value = BinaryPrimitives.ReadUInt64LittleEndian(data[offset..]);
            offset += sizeof(ulong);
            return true;
        }

        public bool TryReadGuid(out Guid value)
        {
            if (data.Length - offset < 16)
            {
                value = default;
                return false;
            }

            value = new Guid(data.Slice(offset, 16));
            offset += 16;
            return true;
        }

        public bool TryReadString(out string value)
        {
            value = string.Empty;
            if (!TryRead7BitEncodedInt(out int length) ||
                length < 0 ||
                length > MaximumStringBytes ||
                data.Length - offset < length)
            {
                return false;
            }

            try
            {
                value = StrictUtf8.GetString(data.Slice(offset, length));
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            offset += length;
            return true;
        }

        private bool TryRead7BitEncodedInt(out int value)
        {
            uint result = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                if (offset >= data.Length)
                {
                    value = default;
                    return false;
                }

                byte current = data[offset++];
                if (shift == 28 && (current & 0xF0) != 0)
                {
                    value = default;
                    return false;
                }

                result |= (uint)(current & 0x7F) << shift;
                if ((current & 0x80) != 0)
                    continue;

                if (result > int.MaxValue)
                {
                    value = default;
                    return false;
                }

                value = (int)result;
                return true;
            }

            value = default;
            return false;
        }
    }
}
