using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal void TickHealthContext()
    {
        foreach (RuntimePlayerMember member in membership.Members)
        {
            int? next = null;
            TerraRuntime.Contracts.Runtime.PlayerDebuffSnapshot1458? debuffs = null;
            bool outOfRange = true;
            if (worldTiles is not null)
            {
                (float width, float height) = member.HasMount
                    ? VanillaPlayerMountHitbox1458.Resolve(member.MountType)
                    : (VanillaBasePlayerWidth, VanillaBasePlayerHeight);
                outOfRange = VanillaPlayerHealthContext1458.IsRemoteOutOfRange(member.PositionX, member.PositionY,
                    (int)width, (int)height, worldTiles.Dimensions.WidthTiles, worldTiles.Dimensions.HeightTiles);
                bool ghost = (member.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0;
                next = VanillaPlayerHealthContext1458.Resolve(member.DerivedLifeMax, member.BaseLifeMax,
                    transferProfiles.CountActiveBuffs(member.Connection, VanillaBuffIds.Lifeforce), outOfRange, ghost, member.IsDead);
                debuffs = VanillaPlayerHealthContext1458.ResolveDebuffs(member.Debuffs,
                    transferProfiles.CaptureDebuffFlags(member.Connection), outOfRange, ghost, member.IsDead);

            }
            bool clear = !outOfRange && member.IsDead && transferProfiles.HasNonPersistentBuffs(member.Connection) &&
                (member.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) == 0;
            // Remote Player.Update changes life through regeneration, DoT and environmental writers.
            // Until that complete phase is owned, a synchronized report is only current before this boundary.
            if (next == member.DerivedLifeMax && debuffs == member.Debuffs && !clear && member.NpcLifeCurrent != true) continue;
            if (!member.TryAdvanceRevision()) continue;
            member.DerivedLifeMax = next;
            member.Debuffs = debuffs;
            member.NpcLifeCurrent = false;
            if (clear) transferProfiles.ClearNonPersistentBuffs(member.Connection);
        }
    }

}
