using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Gameplay.Players;

public readonly record struct LocalPlacementTail1458(ToolClockState1458 State,
    int ConsumedQuantity, int RemainingStack, int PlacedCells);

/// <summary>The local PlaceThing and following primary-consumption tail, after a qualified world plan.</summary>
public static class VanillaLocalPlacement1458
{
    public static bool TryPlanTail(in LocalPlacementDefinition1458 definition,
        in ToolClockState1458 ownerStage, int beforeStack, int additionalWalls,
        float placementSpeed, bool windowsArithmetic, out LocalPlacementTail1458 tail)
    {
        tail = default;
        if (definition.Item.IsNone || !ownerStage.IsValid || ownerStage.Selected.Animation <= 0 ||
            ownerStage.Selected.ItemTime != 0 || beforeStack is < 1 || beforeStack > definition.MaximumStack ||
            additionalWalls is < 0 or > 4 || (!definition.IsWall && additionalWalls != 0) ||
            (beforeStack == 1 && additionalWalls != 0) || !float.IsFinite(placementSpeed) ||
            placementSpeed < 0f || definition.UseTime <= 0) return false;
        // CLR4/x86 keeps the product extended until the integer conversion. The
        // other official edition rounds the float product first (e.g. 10*.9).
        double product = windowsArithmetic ? definition.UseTime * (double)placementSpeed :
            definition.UseTime * placementSpeed;
        if (!double.IsFinite(product) || product > short.MaxValue) return false;
        int itemTime = Math.Max(1, (int)product);
        int maximum = itemTime;
        int rawStack = beforeStack;
        bool sourceConsumable = true;
        for (int index = 0; index < additionalWalls; index++)
        {
            rawStack--;
            // FillEmptySpace continues after SetDefaults(0); later decrements
            // belong only to the detached source execution, never inventory.
            if (rawStack == 0) sourceConsumable = false;
        }
        if (sourceConsumable)
        {
            if (rawStack > 0) rawStack--;
            if (rawStack <= 0) itemTime = ownerStage.Selected.Animation;
        }
        int remaining = Math.Max(0, rawStack);
        tail = new(ownerStage with { Selected = ownerStage.Selected with
            { ItemTime = itemTime, ItemTimeMax = maximum } }, beforeStack - remaining,
            remaining, 1 + additionalWalls);
        return true;
    }
}
