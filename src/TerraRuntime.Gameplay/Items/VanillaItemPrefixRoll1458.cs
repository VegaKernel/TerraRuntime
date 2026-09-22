using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// Source <c>Item.Prefix(-1)</c> as world generation reaches it, on the shared generation stream.
/// </summary>
/// <remarks>
/// <para>
/// Chest loot calls this for most of what it puts in a chest, and its draws are not a detail that stays inside
/// the chest: they are taken from the same stream every later placement reads, so a roll that takes one value
/// too few or too many moves every tile generated after it. That is why this is a port rather than an
/// approximation, and why the differential checks the values consumed and not only the prefix produced.
/// </para>
/// <para>
/// The shape is a reroll loop. One value in four ends it with no prefix at all; otherwise one value picks a
/// prefix out of the item's family, a prefix with a reduced natural chance then needs a second value to survive,
/// and a prefix whose multipliers leave the item's stats unchanged is thrown away and the whole loop runs again.
/// An item that cannot have prefixes draws nothing.
/// </para>
/// </remarks>
public static class VanillaItemPrefixRoll1458
{
    /// <summary>
    /// Rolls a natural prefix, consuming exactly the values the source consumes. Returns zero both for an item
    /// that cannot have prefixes, which draws nothing, and for a roll that legitimately lands on no prefix.
    /// </summary>
    public static byte Roll(ItemTypeId type, IWorldGenerationVanillaRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);

        // Source Item.CanHavePrefixes: an item with no rollable family refuses before touching the stream.
        if (!VanillaItemPrefixTable1458.TryGet(type, out VanillaItemPrefixRecord1458 record))
            return 0;

        ReadOnlySpan<byte> family = VanillaItemPrefixTable1458.GetFamily(record.Family);
        int rolled = -1;
        bool reroll = true;
        while (reroll)
        {
            reroll = false;
            if (rolled == -1 && random.Next(4) == 0)
                rolled = 0;
            if (rolled == -1)
                rolled = family[random.Next(family.Length)];
            if (VanillaItemPrefixTable1458.HasReducedNaturalChance(rolled) && random.Next(3) != 0)
                rolled = 0;
            if (!VanillaItemPrefixTable1458.Accepts(in record, rolled))
            {
                reroll = true;
                rolled = -1;
            }
        }

        return (byte)rolled;
    }
}
