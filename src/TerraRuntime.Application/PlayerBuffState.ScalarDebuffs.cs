using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Buffs;

namespace TerraRuntime.Application;

internal sealed partial class PlayerBuffState
{
    // Player.UpdateBuffs derives this frame's indexed effects before an owned
    // timer expires or Update's later equipment immunity pass removes its slot.
    // This is an immutable projection, never another set of buff timers.
    private BuffTypeId[]? contactEffectTypesForUpdate;

    internal bool HasDarknessEffect => HasCurrentEffect(22);
    internal bool HasBleedingEffect => HasCurrentEffect(30);
    internal bool HasSlowEffect => HasCurrentEffect(32);
    internal bool HasSilenceEffect => HasCurrentEffect(35);
    internal bool HasBrokenArmorEffect => HasCurrentEffect(36);

    private bool HasCurrentEffect(int type)
    {
        if (contactEffectTypesForUpdate is { } effects)
        {
            foreach (var effect in effects) if (effect.Value == type) return true;
            return false;
        }
        return CountActive(new BuffTypeId(type)) > 0;
    }

    private BuffTypeId[] CaptureLiveEffectTypes() => Enumerable.Range(0, count)
        .Where(i => types[i] != VanillaBuffIds.None && durations[i] > 0 &&
            (!IsOwnedConsumableClockType(types[i].Value) || ownedFood[i]))
        .Select(i => types[i]).ToArray();

    private void CaptureContactEffectsBeforeCountdown()
    {
        // An expired scalar-only contact still owns one clearing update through
        // the nonnull projection. Food alone must not start a contact clock.
        bool liveOwnedContact = false;
        for (int i = 0; i < Capacity; i++)
            liveOwnedContact |= ownedNpcContact[i] && durations[i] > 0;
        contactEffectTypesForUpdate = liveOwnedContact ? CaptureLiveEffectTypes() : null;
    }
}
