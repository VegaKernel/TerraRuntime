using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private bool combatEquipmentExpertMode;
    private bool combatEquipmentMasterMode;
    private VanillaPlayerLocalVanityArmor1458? combatEquipmentLocalVanityArmor;

    internal void SetCombatEquipmentLocalVanityArmor(VanillaPlayerLocalVanityArmor1458 armor)
    {
        if (combatEquipmentLocalVanityArmor is { } current && current != armor)
            throw new InvalidOperationException("Source-local equipment is bound once before ticking.");
        combatEquipmentLocalVanityArmor = armor;
    }

    // Standard composition binds source effective difficulty (including GoodWorld) once before ticking.
    internal void SetCombatEquipmentWorldModes(bool expert, bool master)
    {
        if (master && !expert) throw new ArgumentException("Master requires Expert.", nameof(master));
        combatEquipmentExpertMode = expert;
        combatEquipmentMasterMode = master;
    }
}
