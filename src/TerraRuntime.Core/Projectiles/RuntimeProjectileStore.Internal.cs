using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public sealed partial class RuntimeProjectileStore
{
    private bool IsAddressableSlot(ushort slot) => slot < _slots.Length;

    private bool IsCurrentHandleCandidate(ProjectileHandle handle) =>
        handle.IsAssigned && IsAddressableSlot(handle.Slot);

    private static bool TryCreateLifecycle(
        ProjectileTypeId type,
        out ProjectileLifecycleState lifecycle)
    {
        if (!VanillaProjectileLifecycleFacts.TryGetDefaults(type, out VanillaProjectileLifecycleDefaults defaults))
        {
            lifecycle = default;
            return false;
        }

        lifecycle = new ProjectileLifecycleState(defaults.TimeLeft, defaults.NetImportant);
        return true;
    }

    internal static bool IsValidState(in ProjectileStateUpdate update) =>
        VanillaProjectileIds.IsLiveWireType(update.Type) &&
        float.IsFinite(update.PositionX) &&
        float.IsFinite(update.PositionY) &&
        float.IsFinite(update.VelocityX) &&
        float.IsFinite(update.VelocityY) &&
        update.Ai.IsFinite &&
        float.IsFinite(update.KnockBack);

    private static void InitializeSlot(
        ref SlotState state,
        in ProjectileStateUpdate update,
        in ProjectileLifecycleState lifecycle)
    {
        state.Active = true;
        state.Revision = 1;
        state.Update = update;
        state.Lifecycle = lifecycle;
    }

    private static ProjectileSnapshot Capture(ushort slot, in SlotState state)
    {
        ProjectileStateUpdate update = state.Update;
        return new ProjectileSnapshot(
            new ProjectileHandle(slot, new ProjectileGeneration(state.Generation)),
            new ProjectileRevision(state.Revision),
            update.Type,
            update.Spawner,
            update.PositionX,
            update.PositionY,
            update.VelocityX,
            update.VelocityY,
            update.Ai,
            update.BannerIdToRespondTo,
            update.Damage,
            update.KnockBack,
            update.OriginalDamage);
    }

    private static bool TryAdvance(ref ulong value)
    {
        if (value == ulong.MaxValue)
            return false;

        value++;
        return true;
    }

    private struct SlotState
    {
        public bool Active;
        public ulong Generation;
        public ulong Revision;
        public ProjectileStateUpdate Update;
        public ProjectileLifecycleState Lifecycle;
    }
}
