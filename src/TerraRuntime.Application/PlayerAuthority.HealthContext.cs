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
            bool outOfRange = true;
            if (worldTiles is not null)
            {
                (float width, float height) = member.HasMount
                    ? VanillaPlayerMountHitbox1458.Resolve(member.MountType)
                    : (VanillaBasePlayerWidth, VanillaBasePlayerHeight);
                outOfRange = VanillaPlayerHealthContext1458.IsRemoteOutOfRange(member.PositionX, member.PositionY,
                    (int)width, (int)height, worldTiles.Dimensions.WidthTiles, worldTiles.Dimensions.HeightTiles);
                bool ghost = (member.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0;
                next = VanillaPlayerHealthContext1458.Resolve(member.DerivedLifeMax, member.HasHealth ? member.MaxLife : null,
                    transferProfiles.CountActiveBuffs(member.Connection, VanillaBuffIds.Lifeforce), outOfRange, ghost, member.IsDead);

            }
            bool clear = !outOfRange && member.IsDead && transferProfiles.HasNonPersistentBuffs(member.Connection) &&
                (member.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) == 0;
            if (next == member.DerivedLifeMax && !clear) continue;
            if (!member.TryAdvanceRevision()) continue;
            member.DerivedLifeMax = next;
            if (clear) transferProfiles.ClearNonPersistentBuffs(member.Connection);
        }
    }

}
