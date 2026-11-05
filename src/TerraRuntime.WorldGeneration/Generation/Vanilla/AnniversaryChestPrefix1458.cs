using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-reachable accessory, guns/bows, magic, summon and melee slices of TerrariaServer 1.4.5.8
/// WorldGen.ImproveAllChestContents/PrefixItemFromOptions. The verified
/// Eye of Cthulhu Shield dual-category case is included; positive-request
/// fallback and other existing-prefix items remain refused.
/// </summary>
internal static class AnniversaryChestPrefix1458
{
    private static ReadOnlySpan<byte> GoodAccessoryPrefixes => [65, 68, 72, 76];
    private static ReadOnlySpan<byte> GoodRangedPrefixes => [82, 57, 60];
    private static ReadOnlySpan<byte> GoodMagicPrefixes => [83, 57, 60];
    private static ReadOnlySpan<byte> GoodSummonPrefixes => [85, 59, 95, 96, 97];
    private static ReadOnlySpan<byte> GoodMeleePrefixes => [81, 59, 57];

    internal static int ImproveAccessoryChests(Workspace workspace,
        IWorldGenerationVanillaRandom random, CancellationToken cancellationToken = default) =>
        ImproveQualifiedChests(workspace, random, cancellationToken);

    internal static int ImproveAccessoryAndRangedChests(Workspace workspace,
        IWorldGenerationVanillaRandom random, CancellationToken cancellationToken = default) =>
        ImproveQualifiedChests(workspace, random, cancellationToken);

    internal static int ImproveQualifiedChests(Workspace workspace,
        IWorldGenerationVanillaRandom random, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(random);
        WorldChest[] chests = workspace.CaptureGeneratedChests();
        ValidateQualifiedChests(chests);

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
                if (item.IsEmpty || IsNoAnniversaryPrefixCategory(item.ItemType) ||
                    !VanillaItemPrefixTable1458.TryGet(new ItemTypeId(item.ItemType),
                        out VanillaItemPrefixRecord1458 record))
                    continue;
                ReadOnlySpan<byte> choices = GoodOptions(record.Family);
                byte prefix = choices[random.Next(choices.Length)];
                if (item.ItemType == 3097)
                    prefix = GoodMeleePrefixes[random.Next(GoodMeleePrefixes.Length)];
                items[slot] = items[slot] with { Prefix = new PrefixId(prefix) };
                chestChanged = true;
                changed++;
            }
            if (chestChanged && !workspace.TryReplaceGeneratedChestItems(chest.X, chest.Y, items))
                throw new InvalidOperationException("Anniversary prefix update lost the canonical chest side table.");
        }
        return changed;
    }

    internal static void ValidateQualifiedChests(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ValidateQualifiedChests(workspace.CaptureGeneratedChests());
    }

    private static void ValidateQualifiedChests(ReadOnlySpan<WorldChest> chests)
    {

        // Source Prefix(-3) checks CanHavePrefixes and returns without an RNG
        // draw. Reject other prefixable families, prior prefixes and any positive
        // good-prefix option that would enter Item.Prefix's random fallback.
        foreach (WorldChest chest in chests)
            foreach (WorldChestItem item in chest.Items)
            {
                if (item.IsEmpty || IsNoAnniversaryPrefixCategory(item.ItemType) ||
                    !VanillaItemPrefixTable1458.TryGet(new ItemTypeId(item.ItemType),
                        out VanillaItemPrefixRecord1458 record))
                    continue;
                if (item.Prefix != 0)
                    throw new NotSupportedException("Anniversary prefixing requires the source item category and prior-prefix state.");
                ReadOnlySpan<byte> choices = GoodOptions(record.Family);
                foreach (byte candidate in choices)
                    if (!VanillaItemPrefixTable1458.Accepts(in record, candidate))
                        throw new NotSupportedException("Anniversary positive-prefix fallback is not qualified.");
                if (item.ItemType == 3097)
                    foreach (byte candidate in GoodMeleePrefixes)
                        if (!VanillaItemPrefixTable1458.Accepts(in record, candidate))
                            throw new NotSupportedException("Anniversary shield melee fallback is not qualified.");
            }
    }

    private static ReadOnlySpan<byte> GoodOptions(int family) => family switch
    {
        0 or 1 or 5 or 6 => GoodMeleePrefixes,
        7 => GoodAccessoryPrefixes,
        2 => GoodRangedPrefixes,
        3 => GoodMagicPrefixes,
        4 => GoodSummonPrefixes,
        _ => []
    };

    // Direct 1.4.5.8 Item.SetDefaults audit across all 886 prefixable items:
    // these 19 roll a melee prefix family but all five GiveItemGoodPrefixes
    // category predicates are false. Source skips them without a draw.
    // Eye of Cthulhu Shield 3097 is the only dual-category item; its two
    // source passes are handled explicitly above.
    private static bool IsNoAnniversaryPrefixCategory(int id) => id is 160 or 4672 or 4678 or 4679 or 4680 or
        4911 or 4912 or 4913 or 4914 or 5074 or 5473 or 5474 or 5475 or 5476 or 5477 or
        5478 or 5479 or 5480 or 5688;
}
