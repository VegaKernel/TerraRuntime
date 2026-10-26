using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Projectiles;

public sealed partial class RuntimeProjectileStore
{
    internal readonly record struct WorldSpawnActorImage1458(ushort Slot, bool Active,
        ulong Generation, ulong Revision, ProjectileStateUpdate Update, ProjectileLifecycleState Lifecycle,
        bool CombatTrusted, PlayerHandle TrustedOwner, NpcHandle SourceNpc, int? SourceDirection);

    internal sealed partial class SimulationPreparation
    {
        internal bool TryCaptureWorldSpawnActor(out WorldSpawnActorImage1458 beforeImage,
            out WorldSpawnActorImage1458 afterImage)
        {
            beforeImage = default; afterImage = default;
            if (!IsCurrent) return false;
            beforeImage = CaptureWorldSpawnImage(in before);
            afterImage = CaptureWorldSpawnImage(in shadow);
            return true;
        }
        internal bool HasWorldSpawnProjection(RuntimeProjectileStore expectedOwner,
            in WorldSpawnActorImage1458 beforeImage, in WorldSpawnActorImage1458 afterImage) =>
            ReferenceEquals(owner, expectedOwner) && CaptureWorldSpawnImage(in before) == beforeImage &&
            CaptureWorldSpawnImage(in shadow) == afterImage;
        private WorldSpawnActorImage1458 CaptureWorldSpawnImage(in SlotState state) =>
            new(slot, state.Active, state.Generation, state.Revision, state.Update, state.Lifecycle,
                state.CombatTrusted, state.CombatTrustedOwner, state.SourceNpc, state.SourceDirection);
    }
}
