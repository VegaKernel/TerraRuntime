using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcBuffStatus1458
{
    private static bool ReadVenom(in RuntimeNpcBuffSlots1458 slots)
    {
        for (int i = 0; i < Capacity; i++)
            if (slots[i].Type == VanillaBuffIds.Venom.Value && slots[i].Duration > 0) return true;
        return false;
    }

}
