using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed record RuntimeChestCustodyImage(WorldChest Chest, short[] Quantity, long[] Copper);
internal sealed record RuntimeWorldChestCustodySnapshot(Guid WorldUniqueId, int WorldId, uint Revision,
    RuntimeChestCustodyImage[] Chests)
{
    internal static RuntimeWorldChestCustodySnapshot FullImport(WorldFileData world) => new(
        world.Header.UniqueId, world.Header.WorldId, world.Envelope.Revision,
        world.Chests.Select(static chest =>
        {
            var quantity = new short[chest.Items.Length];
            var copper = new long[chest.Items.Length];
            for (int i = 0; i < chest.Items.Length; i++)
            {
                var item = chest.Items[i];
                if (VanillaCoinFacts.TryGetValue(new(item.ItemType), out long unit)) copper[i] = checked(unit * item.Stack);
                else quantity[i] = checked((short)item.Stack);
            }
            return new RuntimeChestCustodyImage(new(chest.SlotId, chest.X, chest.Y, chest.Name, chest.Items.ToArray()), quantity, copper);
        }).ToArray());
}

/// <summary>Exact generation metadata for existing chest slots. This is not a report/import funding producer.
/// Complete coverage and whole-world hash/revision binding prevent a missing slot from becoming full by default.</summary>
internal static class RuntimeWorldChestCustodyCodec
{
    internal const int MaximumChests = 8000;
    internal const int MaximumSlotsPerChest = byte.MaxValue + 1;
    internal const int MaximumEncodedBytes = 40 * 1024 * 1024;
    private static ReadOnlySpan<byte> Magic => "TRWCQ001"u8;

    internal static byte[] Encode(RuntimeWorldChestCustodySnapshot snapshot, ReadOnlySpan<byte> worldHash)
    {
        if (worldHash.Length != SHA256.HashSizeInBytes || snapshot.Chests.Length > MaximumChests)
            throw new InvalidDataException("Custody snapshot bounds are invalid.");
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(snapshot.WorldUniqueId.ToByteArray());
        writer.Write(snapshot.WorldId);
        writer.Write(snapshot.Revision);
        writer.Write(worldHash);
        writer.Write(snapshot.Chests.Length);
        var identities = new HashSet<short>();
        var coordinates = new HashSet<(int, int)>();
        foreach (var image in snapshot.Chests)
        {
            var chest = image.Chest;
            if (chest.SlotId is < 0 or >= MaximumChests || !identities.Add(chest.SlotId) ||
                !coordinates.Add((chest.X, chest.Y)) || chest.Items.Length > MaximumSlotsPerChest ||
                chest.Items.Length != image.Quantity.Length || chest.Items.Length != image.Copper.Length)
                throw new InvalidDataException("Custody chest coverage is invalid.");
            writer.Write(chest.SlotId);
            writer.Write(chest.X);
            writer.Write(chest.Y);
            writer.Write((ushort)chest.Items.Length);
            for (int slot = 0; slot < chest.Items.Length; slot++)
            {
                var item = chest.Items[slot];
                ValidateSlot(in item, image.Quantity[slot], image.Copper[slot]);
                writer.Write(item.Stack);
                writer.Write(item.ItemType);
                writer.Write(item.Prefix);
                writer.Write(image.Quantity[slot]);
                writer.Write(image.Copper[slot]);
            }
        }
        writer.Flush();
        if (buffer.Length + SHA256.HashSizeInBytes > MaximumEncodedBytes)
            throw new InvalidDataException("Custody snapshot exceeds its encoded bound.");
        byte[] body = buffer.ToArray();
        byte[] result = new byte[body.Length + SHA256.HashSizeInBytes];
        body.CopyTo(result, 0);
        SHA256.HashData(body).CopyTo(result, body.Length);
        return result;
    }

    internal static RuntimeWorldChestCustodySnapshot Decode(ReadOnlySpan<byte> encoded, WorldFileData world,
        ReadOnlySpan<byte> worldHash)
    {
        if (encoded.Length is < 100 or > MaximumEncodedBytes || worldHash.Length != SHA256.HashSizeInBytes ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(encoded[..^SHA256.HashSizeInBytes]), encoded[^SHA256.HashSizeInBytes..]))
            throw new InvalidDataException("Custody integrity or length is invalid.");
        using var stream = new MemoryStream(encoded[..^SHA256.HashSizeInBytes].ToArray(), writable: false);
        using var reader = new BinaryReader(stream);
        try
        {
            if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic) ||
                new Guid(reader.ReadBytes(16)) != world.Header.UniqueId || reader.ReadInt32() != world.Header.WorldId ||
                reader.ReadUInt32() != world.Envelope.Revision ||
                !CryptographicOperations.FixedTimeEquals(reader.ReadBytes(SHA256.HashSizeInBytes), worldHash))
                throw new InvalidDataException("Custody source generation does not match the canonical world.");
            int count = reader.ReadInt32();
            if (count is < 0 or > MaximumChests || count != world.Chests.Length)
                throw new InvalidDataException("Custody chest count does not cover the loaded world.");
            var loaded = world.Chests.ToDictionary(static chest => chest.SlotId);
            var result = new RuntimeChestCustodyImage[count];
            var seen = new HashSet<short>();
            for (int i = 0; i < count; i++)
            {
                short id = reader.ReadInt16();
                int x = reader.ReadInt32(), y = reader.ReadInt32(), slots = reader.ReadUInt16();
                if (!seen.Add(id) || !loaded.TryGetValue(id, out var physical) || physical.X != x || physical.Y != y ||
                    physical.Items.Length != slots || slots > MaximumSlotsPerChest)
                    throw new InvalidDataException("Custody chest identity does not match canonical stock.");
                var quantity = new short[slots];
                var copper = new long[slots];
                for (int slot = 0; slot < slots; slot++)
                {
                    var item = new WorldChestItem(reader.ReadInt32(), reader.ReadInt32(), reader.ReadByte());
                    quantity[slot] = reader.ReadInt16();
                    copper[slot] = reader.ReadInt64();
                    if (item != physical.Items[slot]) throw new InvalidDataException("Custody physical tuple does not match canonical stock.");
                    ValidateSlot(in item, quantity[slot], copper[slot]);
                }
                result[i] = new(new(id, x, y, physical.Name, physical.Items.ToArray()), quantity, copper);
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Custody has trailing data.");
            return new(world.Header.UniqueId, world.Header.WorldId, world.Envelope.Revision, result);
        }
        catch (EndOfStreamException error) { throw new InvalidDataException("Custody is truncated.", error); }
        catch (ArgumentException error) { throw new InvalidDataException("Custody identity encoding is invalid.", error); }
    }

    private static void ValidateSlot(in WorldChestItem item, short quantity, long copper)
    {
        if (item.Stack == 0)
        {
            if (item != default || quantity != 0 || copper != 0) throw new InvalidDataException("Empty custody slot is noncanonical.");
            return;
        }
        if (item.Stack is < 0 or > short.MaxValue || !VanillaItemIds.TryCreate(item.ItemType, out var type) || type.IsNone ||
            quantity < 0 || copper < 0)
            throw new InvalidDataException("Custody slot is invalid.");
        if (VanillaCoinFacts.TryGetValue(type, out long unit))
        {
            if (quantity != 0 || copper > checked(unit * item.Stack)) throw new InvalidDataException("Coin custody exceeds physical stock.");
        }
        else if (copper != 0 || quantity > item.Stack) throw new InvalidDataException("Quantity custody exceeds physical stock.");
    }
}
