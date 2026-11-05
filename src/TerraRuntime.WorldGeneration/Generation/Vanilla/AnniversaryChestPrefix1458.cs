using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Accessory-only source-reachable slice of TerrariaServer 1.4.5.8
/// WorldGen.ImproveAllChestContents/PrefixItemFromOptions. Weapon and existing-prefix
/// items require the wider Item.Prefix positive-request owner and are refused.
/// </summary>
internal static class AnniversaryChestPrefix1458
{
    private static ReadOnlySpan<byte> GoodAccessoryPrefixes => [65, 68, 72, 76];

    internal static int ImproveAccessoryChests(Workspace workspace,
        IWorldGenerationVanillaRandom random, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(random);
        WorldChest[] chests = workspace.CaptureGeneratedChests();

        // Source Prefix(-3) checks CanHavePrefixes and returns without an RNG
        // draw. Reject all non-accessory prefixable items and pre-prefixed
        // accessories before mutating any chest or consuming the shared stream.
        foreach (WorldChest chest in chests)
            foreach (WorldChestItem item in chest.Items)
            {
                if (item.IsEmpty || !VanillaItemPrefixTable1458.TryGet(new ItemTypeId(item.ItemType),
                        out VanillaItemPrefixRecord1458 record))
                    continue;
                if (record.Family != 7 || item.Prefix != 0)
                    throw new NotSupportedException("Anniversary prefixing requires the source item category and prior-prefix state.");
                foreach (byte candidate in GoodAccessoryPrefixes)
                    if (!VanillaItemPrefixTable1458.Accepts(in record, candidate))
                        throw new NotSupportedException("Anniversary accessory positive-prefix fallback is not qualified.");
            }

        int changed = 0;
        foreach (WorldChest chest in chests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = new WorldGenerationChestItem[chest.Items.Length];
            bool chestChanged = false;
            for (int slot = 0; slot < items.Length; slot++)
            {
                WorldChestItem item = chest.Items[slot];
                items[slot] = new WorldGenerationChestItem(item.Stack,
                    new ItemTypeId(item.ItemType), new PrefixId(item.Prefix));
                if (item.IsEmpty || !VanillaItemPrefixTable1458.TryGet(new ItemTypeId(item.ItemType),
                        out _))
                    continue;
                byte prefix = GoodAccessoryPrefixes[random.Next(GoodAccessoryPrefixes.Length)];
                items[slot] = items[slot] with { Prefix = new PrefixId(prefix) };
                chestChanged = true;
                changed++;
            }
            if (chestChanged && !workspace.TryReplaceGeneratedChestItems(chest.X, chest.Y, items))
                throw new InvalidOperationException("Anniversary prefix update lost the canonical chest side table.");
        }
        return changed;
    }
}
