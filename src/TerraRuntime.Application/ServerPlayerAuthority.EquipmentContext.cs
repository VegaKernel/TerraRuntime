namespace TerraRuntime.Application;

internal sealed partial class ServerPlayerAuthority
{
    private bool combatEquipmentExpertMode;
    private bool combatEquipmentMasterMode;

    // Standard composition binds source effective difficulty (including GoodWorld) once before ticking.
    internal void SetCombatEquipmentWorldModes(bool expert, bool master)
    {
        if (master && !expert) throw new ArgumentException("Master requires Expert.", nameof(master));
        combatEquipmentExpertMode = expert;
        combatEquipmentMasterMode = master;
    }
}
