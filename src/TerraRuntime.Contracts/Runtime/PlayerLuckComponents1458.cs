namespace TerraRuntime.Contracts.Runtime;

public readonly record struct VanillaPlayerLuckComponents1458(
    int LadyBugLuckTimeLeft, float TorchLuck, byte LuckPotion, bool HasGardenGnomeNearby,
    bool BrokenMirrorBadLuck, float EquipmentBasedLuckBonus, float CoinLuck, byte KiteLuckLevel)
{
    public bool IsFinite => float.IsFinite(TorchLuck) && float.IsFinite(EquipmentBasedLuckBonus) && float.IsFinite(CoinLuck);
}
