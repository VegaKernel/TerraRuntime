using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Core;

/// <summary>
/// TerrariaServer 1.4.5.8 world-item materializer for the currently source-backed NPC loot items.
/// Prefix(-1) selection and default velocity consume the same random stream used by loot rules, matching vanilla.
/// </summary>
public sealed class VanillaNpcLootWorldItemMaterializer : INpcLootWorldItemMaterializer
{
    public static VanillaNpcLootWorldItemMaterializer Instance { get; } = new();

    private VanillaNpcLootWorldItemMaterializer()
    {
    }

    public bool CanMaterialize(ItemTypeId itemType) =>
        VanillaItemDefinitionCatalog.TryGetWorldDrop(itemType, out _) &&
        VanillaNaturalItemPrefixRoller.CanRoll(itemType);

    public bool TryMaterialize(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        INpcLootRollSource random,
        out WorldItemDropStateUpdate worldItem)
    {
        ArgumentNullException.ThrowIfNull(random);
        worldItem = default;

        if (!origin.IsValid ||
            !drop.IsValid ||
            !CanMaterialize(drop.ItemType) ||
            !VanillaItemDefinitionCatalog.TryGetWorldDrop(
                drop.ItemType,
                out VanillaItemWorldDropDefinition definition) ||
            !VanillaNaturalItemPrefixRoller.TryRoll(drop.ItemType, random, out PrefixId prefix) ||
            prefix.Value > byte.MaxValue ||
            drop.ItemType.Value > short.MaxValue)
        {
            return false;
        }

        // Item.NewItem applies Prefix(-1) before it consumes default world-item velocity RNG.
        float velocityX = random.NextInt32(-30, 31) * 0.1f;
        float velocityY = definition.NoGravity
            ? random.NextInt32(-30, 31) * 0.1f
            : random.NextInt32(-40, -15) * 0.1f;

        worldItem = new WorldItemDropStateUpdate(
            PositionX: origin.CenterX - definition.Width / 2f,
            PositionY: origin.CenterY - definition.Height / 2f,
            VelocityX: velocityX,
            VelocityY: velocityY,
            Stack: drop.Stack,
            Prefix: checked((byte)prefix.Value),
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: checked((short)drop.ItemType.Value),
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0);
        return true;
    }
}
