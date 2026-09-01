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
/// Only bytes explicitly owned by a supported milestone/unlock are changed; every other byte is preserved verbatim.
/// </summary>
public static class WorldFileProgressionHeaderPatcher
{
    private const int MaximumStringBytes = 4 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const ulong SupportedMutationMask =
        (1UL << (int)VanillaWorldProgressionId.KingSlime) |
        (1UL << (int)VanillaWorldProgressionId.EvilBoss);

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

        const int bytesBeforeTime =
            sizeof(int) + 9 + (sizeof(long) * 2) + sizeof(byte) +
            (sizeof(int) * 3) + (sizeof(int) * 4) +
            (sizeof(int) * 3) + (sizeof(int) * 4) + (sizeof(int) * 3) +
            (sizeof(int) * 2) + (sizeof(double) * 2);
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

        // crimson; downedBoss1; downedBoss2; then boss3, Queen Bee, mech 1/2/3/any, Plantera and Golem.
        if (!reader.TryReadBool(out _) || !reader.TryReadBool(out _))
            return WorldFileProgressionHeaderPatchResult.InvalidHeader;
        int downedBoss2Offset = reader.Offset;
        if (!reader.TryReadBool(out bool persistedDownedBoss2) || !reader.TrySkipBools(8))
            return WorldFileProgressionHeaderPatchResult.InvalidHeader;

        int downedSlimeKingOffset = reader.Offset;
        if (!reader.TryReadBool(out bool persistedDownedSlimeKing))
            return WorldFileProgressionHeaderPatchResult.InvalidHeader;

        int slimeBlueUnlockOffset = -1;
        int truffleUnlockOffset = -1;
        bool persistedSlimeBlueUnlock = false;
        bool persistedTruffleUnlock = false;
        if ((mutations.UnlockSlimeBlueSpawn || mutations.UnlockTruffleSpawn) &&
            !TryLocateTownSpawnUnlocks(
                ref reader,
                out slimeBlueUnlockOffset,
                out persistedSlimeBlueUnlock,
                out truffleUnlockOffset,
                out persistedTruffleUnlock))
        {
            return WorldFileProgressionHeaderPatchResult.InvalidHeader;
        }

        patchedHeader = sourceHeader.ToArray();
        if (mutations.IsCompleted(VanillaWorldProgressionId.EvilBoss) && !persistedDownedBoss2)
            patchedHeader[downedBoss2Offset] = 1;
        if (mutations.IsCompleted(VanillaWorldProgressionId.KingSlime) && !persistedDownedSlimeKing)
            patchedHeader[downedSlimeKingOffset] = 1;
        if (mutations.UnlockSlimeBlueSpawn && !persistedSlimeBlueUnlock)
            patchedHeader[slimeBlueUnlockOffset] = 1;
        if (mutations.UnlockTruffleSpawn && !persistedTruffleUnlock)
            patchedHeader[truffleUnlockOffset] = 1;

        return WorldFileProgressionHeaderPatchResult.Patched;
    }

    private static bool TryLocateTownSpawnUnlocks(
        ref HeaderPrefixReader reader,
        out int slimeBlueOffset,
        out bool persistedSlimeBlue,
        out int truffleOffset,
        out bool persistedTruffle)
    {
        slimeBlueOffset = -1;
        persistedSlimeBlue = false;
        truffleOffset = -1;
        persistedTruffle = false;

        // savedGoblin/Wizard/Mechanic, seven invasion/world booleans after King Slime.
        if (!reader.TrySkipBools(9) ||
            !reader.TryReadByte(out _) ||
            !reader.TryReadInt32(out _) ||
            !reader.TrySkipBools(2) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadDouble(out _) ||
            !reader.TryReadDouble(out _) ||
            !reader.TryReadByte(out _) ||
            !reader.TryReadBool(out _) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadSingle(out _) ||
            !reader.TrySkip(sizeof(int) * 3) ||
            !reader.TrySkip(8) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadInt16(out _) ||
            !reader.TryReadSingle(out _))
        {
            return false;
        }

        if (!reader.TryReadInt32(out int anglerCount) || anglerCount < 0 || anglerCount > 255)
            return false;
        for (int i = 0; i < anglerCount; i++)
        {
            if (!reader.TryReadString(out _))
                return false;
        }

        if (!reader.TryReadBool(out _) ||
            !reader.TryReadInt32(out _) ||
            !reader.TrySkipBools(3) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadInt32(out _))
        {
            return false;
        }

        // BannerSystem.Save: Int16 killCount length + Int32 entries, then Int16 claimable length + UInt16 entries.
        if (!reader.TryReadInt16(out short killCount) || killCount < 0 ||
            !reader.TrySkip(checked(killCount * sizeof(int))) ||
            !reader.TryReadInt16(out short claimableCount) || claimableCount < 0 ||
            !reader.TrySkip(checked(claimableCount * sizeof(ushort))))
        {
            return false;
        }

        // fastForwardTimeToDawn; 18 boss/event/tower booleans; party state and celebrating-NPC list.
        if (!reader.TryReadBool(out _) ||
            !reader.TrySkipBools(18) ||
            !reader.TrySkipBools(2) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadInt32(out int partyCount) ||
            partyCount < 0 || partyCount > 255 ||
            !reader.TrySkip(checked(partyCount * sizeof(int))))
        {
            return false;
        }

        // sandstorm; bartender; DD2 T1/T2/T3; five background bytes; combat book.
        if (!reader.TryReadBool(out _) ||
            !reader.TryReadInt32(out _) ||
            !reader.TryReadSingle(out _) ||
            !reader.TryReadSingle(out _) ||
            !reader.TryReadBool(out _) ||
            !reader.TrySkipBools(3) ||
            !reader.TrySkip(5) ||
            !reader.TryReadBool(out _) ||
            !reader.TryReadInt32(out _) ||
            !reader.TrySkipBools(3))
        {
            return false;
        }

        // TreeTopsInfo.Save: Int32 variation count + Int32 entries.
        if (!reader.TryReadInt32(out int treeTopCount) ||
            treeTopCount < 0 || treeTopCount > 64 ||
            !reader.TrySkip(checked(treeTopCount * sizeof(int))))
        {
            return false;
        }

        // force Halloween/XMas today; four pre-Hardmode ore tiers; pets bought; Empress/Queen Slime/Deerclops.
        if (!reader.TrySkipBools(2) ||
            !reader.TrySkip(sizeof(int) * 4) ||
            !reader.TrySkipBools(6))
        {
            return false;
        }

        slimeBlueOffset = reader.Offset;
        if (!reader.TryReadBool(out persistedSlimeBlue) || !reader.TrySkipBools(4))
            return false;

        truffleOffset = reader.Offset;
        return reader.TryReadBool(out persistedTruffle);
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

        public bool TrySkipBools(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!TryReadBool(out _))
                    return false;
            }
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

        public bool TryReadByte(out byte value)
        {
            if (offset >= data.Length)
            {
                value = default;
                return false;
            }
            value = data[offset++];
            return true;
        }

        public bool TryReadInt16(out short value)
        {
            if (data.Length - offset < sizeof(short))
            {
                value = default;
                return false;
            }
            value = BinaryPrimitives.ReadInt16LittleEndian(data[offset..]);
            offset += sizeof(short);
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

        public bool TryReadSingle(out float value)
        {
            if (data.Length - offset < sizeof(float))
            {
                value = default;
                return false;
            }
            value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data[offset..]));
            offset += sizeof(float);
            return true;
        }

        public bool TryReadDouble(out double value)
        {
            if (data.Length - offset < sizeof(double))
            {
                value = default;
                return false;
            }
            value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data[offset..]));
            offset += sizeof(double);
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
                length < 0 || length > MaximumStringBytes || data.Length - offset < length)
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
