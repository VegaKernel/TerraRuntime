using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class PlayerBuffState
{
    internal bool RemoveEquipmentImmuneBuffs(in VanillaPlayerCombatSnapshot combat)
    {
        if (!combat.AnkhShield && combat.BuffImmunity == VanillaPlayerBuffImmunity1458.None)
            return false;

        var effects = CaptureDebuffFlags();
        var effectTypes = CaptureEffectTypes();
        bool noItems = HasNoItemsEffect;
        bool changed = false;
        // Player.Update walks forward after UpdateBuffs/UpdateEquips. DelBuff
        // compacts slots0..42, so an adjacent immune entry may survive until
        // the next update. Slot43 stays independent in that source compactor.
        for (int slot = 0; slot < Capacity; slot++)
            if (types[slot].Value > 0 && durations[slot] > 0 && combat.IsImmuneToNpcContactBuff(types[slot]))
            {
                RemoveFedStateSlot(slot);
                changed = true;
            }
        if (!changed) return false;

        // The source has already derived these effects, and does not reset
        // them when removing their timers. Reuse the existing per-update
        // carrier; the next owned buff update clears it normally.
        contactDebuffsForUpdate = effects;
        contactEffectTypesForUpdate ??= effectTypes;
        contactNoItemsForUpdate |= noItems;
        InvalidateFoodClockLineage();
        return true;
    }
}
