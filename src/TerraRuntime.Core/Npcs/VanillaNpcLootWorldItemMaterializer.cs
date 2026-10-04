using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// TerrariaServer 1.4.5.8 world-item materializer for the currently source-backed NPC loot items.
/// Prefix(-1) selection and default velocity consume the same random stream used by loot rules, matching vanilla.
/// </summary>
public sealed class VanillaNpcLootWorldItemMaterializer : INpcLootWorldItemMaterializer
{
    // TerrariaServer 1.4.5.8 WorldItem(Item) fixes the entity body independently of Item.SetDefaults dimensions.
    private const int PhysicalBodySize1458 = 16;
    private readonly Func<VanillaSeasonalItemDropContext1458>? seasonalContext;

    public static VanillaNpcLootWorldItemMaterializer Instance { get; } = new();

    public VanillaNpcLootWorldItemMaterializer(Func<VanillaSeasonalItemDropContext1458>? seasonalContext = null)
    {
        this.seasonalContext = seasonalContext;
    }

    public bool CanMaterialize(ItemTypeId itemType) =>
        VanillaDefinitionCatalog.TryGetWorldDrop(itemType, out _) &&
        VanillaNaturalItemPrefixRoller.CanRoll(itemType);

    public bool TryMaterialize(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        INpcLootRollSource random,
        out WorldItemDropStateUpdate worldItem)
        => TryMaterialize(in origin, in drop, random, null, out worldItem);

    public bool TryMaterialize(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        INpcLootRollSource random,
        NpcLootWorldItemVelocity1458? velocity,
        out WorldItemDropStateUpdate worldItem)
    {
        ArgumentNullException.ThrowIfNull(random);
        worldItem = default;
        if (velocity is { IsValid: false }) return false;

        if (!origin.IsValid || !drop.IsValid || !CanMaterialize(drop.ItemType))
            return false;

        // Item.NewItem substitutes seasonal hearts/stars before defaults, Prefix(-1) and launch velocity.
        VanillaSeasonalItemDropContext1458 context = seasonalContext?.Invoke() ?? default;
        ItemTypeId itemType = VanillaSeasonalItemDropFacts1458.Resolve(drop.ItemType, in context, random);
        if (!CanMaterialize(itemType) ||
            !VanillaDefinitionCatalog.TryGetWorldDrop(
                itemType,
                out VanillaItemWorldDropDefinition definition) ||
            !VanillaNaturalItemPrefixRoller.TryRoll(itemType, random, out PrefixId prefix) ||
            prefix.Value > byte.MaxValue ||
            itemType.Value > short.MaxValue)
        {
            return false;
        }

        // Item.NewItem applies Prefix(-1) before it consumes default world-item velocity RNG.
        float velocityX = velocity?.X ?? random.NextInt32(-30, 31) * 0.1f;
        float velocityY = velocity?.Y ?? (definition.NoGravity
            ? random.NextInt32(-30, 31) * 0.1f
            : random.NextInt32(-40, -15) * 0.1f);

        worldItem = new WorldItemDropStateUpdate(
            PositionX: origin.CenterX - PhysicalBodySize1458 / 2f,
            PositionY: origin.CenterY - PhysicalBodySize1458 / 2f,
            VelocityX: velocityX,
            VelocityY: velocityY,
            Stack: drop.Stack,
            Prefix: checked((byte)prefix.Value),
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: checked((short)itemType.Value),
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0);
        return true;
    }
}
