using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-reachable accessory, guns/bows, magic, summon and melee slices of TerrariaServer 1.4.5.8
/// WorldGen.ImproveAllChestContents/PrefixItemFromOptions. The verified
/// Eye of Cthulhu Shield dual-category case and the source-qualified weak
/// magic 3069 fallback (including its prior prefix 60) are included; other
/// positive-request fallback and existing-prefix items remain refused.
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
                if (item.Prefix != 0)
                    record = ApplyPrefixStats(in record, item.Prefix);
                byte prefix = ChooseGoodPrefix(ref record, choices, item.Prefix, item.ItemType, random);
                if (item.ItemType == 3097)
                    prefix = ChooseGoodPrefix(ref record, GoodMeleePrefixes, prefix, item.ItemType, random);
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
        // draw. Only 3069 with prior 60 has a source-qualified existing-prefix
        // reconstruction; all other prior states remain fail-closed. Keep
        // unqualified positive fallback refused before any mutation or draw.
        foreach (WorldChest chest in chests)
            foreach (WorldChestItem item in chest.Items)
            {
                if (item.IsEmpty || IsNoAnniversaryPrefixCategory(item.ItemType) ||
                    !VanillaItemPrefixTable1458.TryGet(new ItemTypeId(item.ItemType),
                        out VanillaItemPrefixRecord1458 record))
                    continue;
                if (item.Prefix != 0)
                {
                    if (item.ItemType != 3069 || item.Prefix != 60)
                        throw new NotSupportedException("Anniversary prior-prefix item state is not source-qualified.");
                    record = ApplyPrefixStats(in record, item.Prefix);
                }
                ReadOnlySpan<byte> choices = GoodOptions(record.Family);
                foreach (byte candidate in choices)
                    if (!VanillaItemPrefixTable1458.Accepts(in record, candidate) && item.ItemType != 3069)
                        throw new NotSupportedException($"Anniversary positive-prefix fallback is not qualified: " +
                            $"chest ({chest.X},{chest.Y}), item {item.ItemType}, family {record.Family}, candidate {candidate}.");
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

    private static byte ChooseGoodPrefix(ref VanillaItemPrefixRecord1458 current,
        ReadOnlySpan<byte> options, byte originalPrefix, int itemType,
        IWorldGenerationVanillaRandom random)
    {
        byte[] remaining = options.ToArray();
        int count = remaining.Length;
        while (count > 0)
        {
            int index = random.Next(count);
            byte candidate = remaining[index];
            if (VanillaItemPrefixTable1458.Accepts(in current, candidate))
            {
                current = ApplyPrefixStats(in current, candidate);
                return candidate;
            }
            if (itemType != 3069)
                throw new NotSupportedException("Unqualified positive-prefix fallback.");
            // Item.Prefix(positive) retries inside the item before the outer
            // PrefixItemFromOptions loop removes the rejected option.
            ReadOnlySpan<byte> family = VanillaItemPrefixTable1458.GetFamily(current.Family);
            bool chosen = false;
            // The accepted 3069 family always has valid outcomes, but refuse a
            // pathological stream instead of spinning indefinitely in generation.
            for (int attempt = 0; attempt < 1024; attempt++)
            {
                // Item.Prefix(positive) falls back with rolledPrefix=-1. Its
                // retry prelude still has the ordinary one-in-four no-prefix
                // draw, even though the original request was positive.
                byte rolled = random.Next(4) == 0 ? (byte)0 : family[random.Next(family.Length)];
                if (!VanillaItemPrefixTable1458.Accepts(in current, rolled))
                    continue;
                if (rolled != 0)
                    current = ApplyPrefixStats(in current, rolled);
                chosen = true;
                break;
            }
            if (!chosen)
                throw new NotSupportedException("Positive-prefix fallback exceeded the source roll bound.");
            for (int move = index; move + 1 < count; move++)
                remaining[move] = remaining[move + 1];
            count--;
        }
        if (originalPrefix != 0)
            current = ApplyPrefixStats(in current, originalPrefix);
        return originalPrefix;
    }

    private static VanillaItemPrefixRecord1458 ApplyPrefixStats(in VanillaItemPrefixRecord1458 current,
        int prefix)
    {
        if (!VanillaItemPrefixTable1458.TryGetStatModifiers(new PrefixId(prefix), out var modifiers) ||
            !VanillaItemPrefixTable1458.TryGetManaMultiplier(new PrefixId(prefix), out float mana))
            throw new NotSupportedException("Unowned positive-prefix stat modifiers.");
        return current with
        {
            Damage = checked((short)Math.Round(current.Damage * (double)modifiers.DamageMultiplier)),
            UseAnimation = checked((byte)Math.Round(current.UseAnimation * (double)modifiers.SpeedMultiplier)),
            Mana = checked((byte)Math.Round(current.Mana * (double)mana))
        };
    }

    // Direct 1.4.5.8 Item.SetDefaults audit across all 886 prefixable items:
    // these 19 roll a melee prefix family but all five GiveItemGoodPrefixes
    // category predicates are false. Source skips them without a draw.
    // Eye of Cthulhu Shield 3097 is the only dual-category item; its two
    // source passes are handled explicitly above.
    private static bool IsNoAnniversaryPrefixCategory(int id) => id is 160 or 4672 or 4678 or 4679 or 4680 or
        4911 or 4912 or 4913 or 4914 or 5074 or 5473 or 5474 or 5475 or 5476 or 5477 or
        5478 or 5479 or 5480 or 5688;
}
