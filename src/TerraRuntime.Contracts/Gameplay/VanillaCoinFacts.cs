namespace TerraRuntime.Contracts.Gameplay;

/// <summary>
/// TerrariaServer 1.4.5.8 vanilla coin identities and copper-unit values used by the runtime commerce path.
/// Item IDs are pinned against the official server assembly by tools/ci/probe_vanilla_coins.py.
/// </summary>
public static class VanillaCoinFacts
{
    public static readonly ItemTypeId CopperCoin = new(71);
    public static readonly ItemTypeId SilverCoin = new(72);
    public static readonly ItemTypeId GoldCoin = new(73);
    public static readonly ItemTypeId PlatinumCoin = new(74);

    public const long CopperValue = 1;
    public const long SilverValue = 100;
    public const long GoldValue = 10_000;
    public const long PlatinumValue = 1_000_000;

    // Copper/silver/gold naturally carry at most 99 after vanilla normalization; platinum is the terminal
    // denomination and uses the modern item stack ceiling. These bounds are used only when materializing change.
    public const short LowerDenominationCanonicalMaximum = 99;
    public const short PlatinumMaximumStack = 9_999;

    public static bool TryGetValue(ItemTypeId itemType, out long value)
    {
        if (itemType == CopperCoin)
        {
            value = CopperValue;
            return true;
        }

        if (itemType == SilverCoin)
        {
            value = SilverValue;
            return true;
        }

        if (itemType == GoldCoin)
        {
            value = GoldValue;
            return true;
        }

        if (itemType == PlatinumCoin)
        {
            value = PlatinumValue;
            return true;
        }

        value = 0;
        return false;
    }
}
