using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Original Item.SetDefaults/Prefix(-1) facts for global mechanical summons; no use capability.</summary>
internal static class VanillaMechSummonDropCatalog1458
{
    public static bool TryGet(ItemTypeId type, out VanillaItemDefinition definition)
    {
        if (type.Value is 544 or 556 or 557)
        {
            definition = new(type, new(22, 14, VanillaDefinitionCatalog.CommonMaximumStack),
                null, null, null, new(22, 14, false, VanillaItemPrefixFamily.None));
            return true;
        }
        definition = default;
        return false;
    }
}
