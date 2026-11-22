using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Effects supplied by source-effective functional accessory slots only.
/// Carried-inventory mechanical effects are captured by the player phase separately.</summary>
public readonly record struct VanillaPlayerToolAccessoryFacts1458(bool TileRange, int BlockRange)
{
    public bool TileSpeed { get; init; }
    public bool WallSpeed { get; init; }
    public bool AutoPaint { get; init; }

    // Player.UpdateEquips (1.4.5.8) tests the selected createTile against
    // TileID.Sets.Torches (whose sole source member is tile 4) before the
    // permanent boosters and final reciprocal speed conversion.
    public (float Tile, float Wall) ResolveRawSpeeds(ItemTypeId selectedItem)
    {
        bool nonTorchTile = VanillaItemPlacementTable1458.TryGet(selectedItem, out var placement) &&
            placement.CreateTile >= 0 && placement.CreateTile != 4;
        return (TileSpeed && nonTorchTile ? 1.5f : 1f,
            WallSpeed ? 1.5f : 1f);
    }
}

public static partial class VanillaPlayerCombatEquipmentCatalog
{
    // Player.UpdateEquips/ApplyEquipFunctional (1.4.5.8) read the effective
    // functional accessory slots, including source favorite inheritance.
    // The speed flags are pre-reciprocal UpdateEquips inputs from this equipped
    // family only. Its separate inventory 0..57 mechanical pass, selected tile
    // and torch checks still belong to the later player item phase.
    public static bool TryCaptureToolAccessories(ReadOnlySpan<PlayerEquipmentCommitRequest> equipment,
        in VanillaPlayerCombatEquipmentContext context, out VanillaPlayerToolAccessoryFacts1458 facts)
    {
        facts = default;
        for (int index = 3; index < VanillaPlayerItemSlotCatalog.FunctionalArmorCount; index++)
        {
            if (!HasEffectiveCandidate(equipment, index)) continue;
            if (!context.TryIsSlotUsable(index, out bool usable)) return false;
            if (!usable) continue;
            if (!TrySelectEffectiveEquipment(equipment, in context, index, out var selected)) return false;
            if (selected.Stack <= 0) continue;
            if (!selected.TryGetCanonicalItemType(out var type)) return false;
            facts = facts with
            {
                TileRange = facts.TileRange || type.Value is 2215 or 3061 or 5126,
                BlockRange = facts.BlockRange + (type.Value == 407 ? 1 : 0),
                TileSpeed = facts.TileSpeed || type.Value is 2214 or 3061 or 5126,
                WallSpeed = facts.WallSpeed || type.Value is 2217 or 3061 or 5126,
                AutoPaint = facts.AutoPaint || type.Value is 2216 or 3061 or 5126
            };
        }
        return true;
    }
}
