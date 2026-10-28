using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Gameplay.Players;

public static class VanillaWiringTool1458
{
    public static bool TryPlanTail(in WiringToolDefinition1458 definition,
        in ToolClockState1458 ownerClocks, int selectedStack, out ToolClockState1458 clocks)
    {
        clocks = default;
        if (!VanillaWiringToolCatalog1458.TryGet(definition.Item, out var canonical) || canonical != definition ||
            !ownerClocks.IsValid || ownerClocks.Selected.Animation <= 0 || ownerClocks.Selected.ItemTime != 0 ||
            selectedStack <= 0) return false;
        // ItemCheck_UseWiringTools applies item time before the owner assigns toolTime.
        // Generic last-stack consumption occurs later and replaces only itemTime.
        int time = definition.Kind == WiringToolKind1458.Actuator && selectedStack == 1
            ? ownerClocks.Selected.Animation : definition.UseTime;
        clocks = ownerClocks with { ToolTime = definition.UseTime,
            Selected = ownerClocks.Selected with { ItemTime = time, ItemTimeMax = definition.UseTime } };
        return clocks.IsValid;
    }
}
