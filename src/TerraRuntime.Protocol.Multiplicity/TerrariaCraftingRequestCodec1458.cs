using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Protocol.Multiplicity;

public readonly record struct TerrariaCraftingIngredient1458(int ItemIdOrRecipeGroup, int Amount);

/// <summary>Owned bounded module12 values. These are chest deficits, not a recipe or output certificate.</summary>
public sealed class TerrariaCraftingRequest1458
{
    private readonly TerrariaCraftingIngredient1458[] ingredients;
    private readonly short[] chests;

    internal TerrariaCraftingRequest1458(TerrariaCraftingIngredient1458[] ingredients, short[] chests)
    {
        this.ingredients = ingredients;
        this.chests = chests;
    }

    public ReadOnlySpan<TerrariaCraftingIngredient1458> Ingredients => ingredients;
    public ReadOnlySpan<short> Chests => chests;
}

public enum TerrariaCraftingRequestDecode1458 : byte
{
    Decoded,
    WrongModule,
    Malformed
}

/// <summary>Official NetworkInitializer/CraftingRequests module12, with explicit bounded positive-input policy.</summary>
public static class TerrariaCraftingRequestCodec1458
{
    public const ushort ModuleId = 12;
    public const int MaximumIngredients = 15;
    public const int MaximumWorldChests = VanillaWorldFormat326.MaximumChestSlots;
    public const int MaximumChestListEntries = MaximumWorldChests + 4;
    private const int MaximumPayloadLength = sizeof(ushort) + 5 + MaximumIngredients * 9 + 5 + MaximumChestListEntries * 5;

    public static bool IsCraftingModule(in TerrariaFrame frame)
    {
        if (frame.MessageId != (byte)TerrariaMessageId.LoadNetModule || frame.Payload.Length < sizeof(ushort))
            return false;
        Span<byte> module = stackalloc byte[sizeof(ushort)];
        frame.Payload.Slice(0, sizeof(ushort)).CopyTo(module);
        return BinaryPrimitives.ReadUInt16LittleEndian(module) == ModuleId;
    }

    public static TerrariaCraftingRequestDecode1458 TryDecode(in TerrariaFrame frame,
        out TerrariaCraftingRequest1458? request)
    {
        request = null;
        if (!IsCraftingModule(in frame)) return TerrariaCraftingRequestDecode1458.WrongModule;
        if (frame.Payload.Length > MaximumPayloadLength) return TerrariaCraftingRequestDecode1458.Malformed;

        byte[] body = frame.Payload.ToArray();
        int offset = sizeof(ushort);
        if (!TryReadPositive7Bit(body, ref offset, out int count) || count > MaximumIngredients)
            return TerrariaCraftingRequestDecode1458.Malformed;
        var ingredients = new TerrariaCraftingIngredient1458[count];
        for (int index = 0; index < count; index++)
        {
            if (body.Length - offset < sizeof(int)) return TerrariaCraftingRequestDecode1458.Malformed;
            int item = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(offset));
            offset += sizeof(int);
            if (!TryRead7Bit(body, ref offset, out int amount))
                return TerrariaCraftingRequestDecode1458.Malformed;
            ingredients[index] = new(item, amount);
        }

        if (!TryReadPositive7Bit(body, ref offset, out int chestCount) || chestCount > MaximumChestListEntries)
            return TerrariaCraftingRequestDecode1458.Malformed;
        var chests = new short[chestCount];
        int worldChestCount = 0;
        for (int index = 0; index < chestCount; index++)
        {
            if (!TryRead7Bit(body, ref offset, out int chest) || chest >= MaximumWorldChests)
                return TerrariaCraftingRequestDecode1458.Malformed;
            // Duplicate chest lists are well-formed vanilla wire, but the authoritative
            // preparation rejects their double-counting as an admission policy.
            if (chest >= 0) chests[worldChestCount++] = (short)chest;
            // Original DeserializeRequest treats every signed negative bank
            // index as null; only real world chest IDs participate server-side.
        }

        if (offset != body.Length) return TerrariaCraftingRequestDecode1458.Malformed;
        if (worldChestCount != chests.Length) Array.Resize(ref chests, worldChestCount);
        request = new(ingredients, chests);
        return TerrariaCraftingRequestDecode1458.Decoded;
    }

    public static byte[] EncodeResponse(bool approved)
    {
        byte[] result = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(result, (ushort)result.Length);
        result[2] = (byte)TerrariaMessageId.LoadNetModule;
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(3), ModuleId);
        result[5] = approved ? (byte)1 : (byte)0;
        return result;
    }

    private static bool TryReadPositive7Bit(ReadOnlySpan<byte> body, ref int offset, out int value) =>
        TryRead7Bit(body, ref offset, out value) && value > 0;

    private static bool TryRead7Bit(ReadOnlySpan<byte> body, ref int offset, out int value)
    {
        value = 0;
        for (int index = 0; index < 5; index++)
        {
            if (offset >= body.Length) return false;
            byte current = body[offset++];
            if (index == 4 && current > 15) return false;
            value |= (current & 127) << (index * 7);
            if ((current & 128) == 0)
                return index == 0 || current != 0; // Reject noncanonical overlong forms.
        }
        return false;
    }
}
