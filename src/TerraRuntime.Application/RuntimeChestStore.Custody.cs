using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeChestStore
{
    private readonly short[]?[] custodyQuantity = new short[]?[VanillaWorldFormat326.MaximumChestSlots];
    private readonly long[]?[] custodyCopper = new long[]?[VanillaWorldFormat326.MaximumChestSlots];
    internal bool SupportsDurableCustody { get; }

    internal RuntimeChestStore(ReadOnlySpan<WorldChest> source, RuntimeWorldChestCustodySnapshot custody)
        : this(source, ownsLoadedWorldStock: true)
    {
        ArgumentNullException.ThrowIfNull(custody);
        if (source.Length != custody.Chests.Length)
            throw new InvalidDataException("Managed custody must cover every loaded chest.");
        var seen = new HashSet<short>();
        foreach (var image in custody.Chests)
        {
            var chest = image.Chest;
            if (!seen.Add(chest.SlotId) || (uint)chest.SlotId >= chests.Length ||
                chests[chest.SlotId] is not { } physical || physical.X != chest.X || physical.Y != chest.Y ||
                !physical.Items.AsSpan().SequenceEqual(chest.Items) ||
                image.Quantity.Length != physical.Items.Length || image.Copper.Length != physical.Items.Length)
                throw new InvalidDataException("Managed custody does not match loaded physical stock.");
            for (int slot = 0; slot < physical.Items.Length; slot++)
                ValidateCustodySlot(in physical.Items[slot], image.Quantity[slot], image.Copper[slot]);
            custodyQuantity[chest.SlotId] = image.Quantity.ToArray();
            custodyCopper[chest.SlotId] = image.Copper.ToArray();
        }
        SupportsDurableCustody = true;
    }

    internal RuntimeWorldChestCustodySnapshot CaptureCustodySnapshot(Guid worldUniqueId, int worldId, uint revision)
    {
        var physical = CaptureSnapshot();
        var detached = new RuntimeChestCustodyImage[physical.Length];
        for (int i = 0; i < physical.Length; i++)
        {
            var chest = physical[i];
            var quantity = custodyQuantity[chest.SlotId] ?? throw new InvalidDataException("Missing owned chest quantities.");
            var copper = custodyCopper[chest.SlotId] ?? throw new InvalidDataException("Missing owned chest copper.");
            if (quantity.Length != chest.Items.Length || copper.Length != chest.Items.Length)
                throw new InvalidDataException("Chest custody slot coverage changed.");
            for (int slot = 0; slot < chest.Items.Length; slot++)
                ValidateCustodySlot(in chest.Items[slot], quantity[slot], copper[slot]);
            detached[i] = new(chest, quantity.ToArray(), copper.ToArray());
        }
        return new(worldUniqueId, worldId, revision, detached);
    }

    private void InitializeFullCustody(WorldChest chest)
    {
        var quantity = new short[chest.Items.Length];
        var copper = new long[chest.Items.Length];
        for (int slot = 0; slot < chest.Items.Length; slot++)
        {
            var item = chest.Items[slot];
            if (item.IsEmpty) continue;
            if (VanillaCoinFacts.TryGetValue(new(item.ItemType), out long unit)) copper[slot] = checked(unit * item.Stack);
            else quantity[slot] = checked((short)item.Stack);
        }
        custodyQuantity[chest.SlotId] = quantity;
        custodyCopper[chest.SlotId] = copper;
    }

    private void ApplyRawCustodyMinimum(WorldChest chest, int slot, in WorldChestItem after)
    {
        var before = chest.Items[slot];
        bool sameIdentity = before.ItemType == after.ItemType && before.Prefix == after.Prefix;
        var quantities = custodyQuantity[chest.SlotId]!;
        var copper = custodyCopper[chest.SlotId]!;
        if (after.IsEmpty || !sameIdentity) { quantities[slot] = 0; copper[slot] = 0; return; }
        if (VanillaCoinFacts.TryGetValue(new(after.ItemType), out long unit))
        {
            quantities[slot] = 0;
            copper[slot] = Math.Min(copper[slot], checked(unit * after.Stack));
        }
        else { copper[slot] = 0; quantities[slot] = Math.Min(quantities[slot], checked((short)after.Stack)); }
    }

    private void ApplyConsumedCustody(WorldChest chest, int slot, in WorldChestItem after)
    {
        var before = chest.Items[slot];
        int amount = before.Stack - after.Stack;
        var quantity = custodyQuantity[chest.SlotId]!;
        var copper = custodyCopper[chest.SlotId]!;
        if (VanillaCoinFacts.TryGetValue(new(before.ItemType), out long unit))
            copper[slot] = Math.Max(0, copper[slot] - checked(unit * amount));
        else quantity[slot] = checked((short)Math.Max(0, quantity[slot] - amount));
    }

    private static void ValidateCustodySlot(in WorldChestItem item, short quantity, long copper)
    {
        if (item.IsEmpty)
        {
            if (item != default || quantity != 0 || copper != 0) throw new InvalidDataException("Noncanonical empty custody.");
            return;
        }
        if (!item.HasValidItemType || item.Stack > short.MaxValue || quantity < 0 || copper < 0)
            throw new InvalidDataException("Invalid custody tuple.");
        if (VanillaCoinFacts.TryGetValue(new(item.ItemType), out long unit))
        {
            if (quantity != 0 || copper > checked(unit * item.Stack)) throw new InvalidDataException("Invalid copper custody.");
        }
        else if (copper != 0 || quantity > item.Stack) throw new InvalidDataException("Invalid quantity custody.");
    }
}
