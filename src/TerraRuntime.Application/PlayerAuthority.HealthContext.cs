using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal void TickHealthContext()
    {
        foreach (RuntimePlayerMember member in membership.Members.ToArray())
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
            bool ghostPhase = (member.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0;
            if (!TryPlanNpcHealth(member, next, outOfRange, ghostPhase, out var health)) continue;
            bool healthCurrent = health is not null;
            if (next == member.DerivedLifeMax && debuffs == member.Debuffs && !clear &&
                member.NpcLifeCurrent == healthCurrent && member.NpcHealth == health) continue;
            if (!member.TryAdvanceRevision()) continue;
            member.DerivedLifeMax = next;
            member.Debuffs = debuffs;
            member.NpcHealth = health;
            member.NpcLifeCurrent = healthCurrent;
            if (clear) transferProfiles.ClearNonPersistentBuffs(member.Connection);
        }
    }

}
