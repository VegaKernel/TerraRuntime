using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// World-backed player target lookup for hostile projectile AI. Physical slot order, the 2000 px acquisition
/// envelope and 1x1 center-to-center Collision.CanHit query mirror the TerrariaServer 1.4.5.8 Cultist fireball
/// path without making projectile behavior reach into global player/world state.
/// </summary>
internal sealed class VanillaProjectilePlayerTargetResolver
{
    private const float PlayerCenterOffsetX = PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
    private const float PlayerCenterOffsetY = PlayerAuthority.VanillaBasePlayerHeight * 0.5f;

    private readonly IRuntimePlayerSlotSnapshotLookup players;
    private readonly WorldTileStore tiles;

    public VanillaProjectilePlayerTargetResolver(IRuntimePlayerSlotSnapshotLookup players, WorldTileStore tiles)
    {
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
    }

    public bool TryFindClosestTargetWithLineOfSight(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition definition,
        float maxRange,
        out PlayerSlotId slot,
        out float targetCenterX,
        out float targetCenterY,
        out float distance)
    {
        slot = default;
        targetCenterX = 0f;
        targetCenterY = 0f;
        distance = maxRange;
        if (!(maxRange > 0f) || !float.IsFinite(maxRange))
            return false;

        float sourceCenterX = projectile.PositionX + definition.Width * 0.5f;
        float sourceCenterY = projectile.PositionY + definition.Height * 0.5f;
        bool found = false;

        for (int rawSlot = 0; rawSlot < byte.MaxValue; rawSlot++)
        {
            var candidateSlot = new PlayerSlotId(checked((byte)rawSlot));
            if (!TryGetActiveTargetCenter(candidateSlot, out float centerX, out float centerY))
                continue;

            float dx = centerX - sourceCenterX;
            float dy = centerY - sourceCenterY;
            float candidateDistance = MathF.Sqrt(dx * dx + dy * dy);
            if (!float.IsFinite(candidateDistance) || !(candidateDistance < distance) ||
                !VanillaWorldCanHit.HasLineOfSight(
                    tiles,
                    sourceCenterX,
                    sourceCenterY,
                    1,
                    1,
                    centerX,
                    centerY,
                    1,
                    1))
            {
                continue;
            }

            slot = candidateSlot;
            targetCenterX = centerX;
            targetCenterY = centerY;
            distance = candidateDistance;
            found = true;
        }

        return found;
    }

    public bool TryGetActiveTargetCenter(PlayerSlotId slot, out float centerX, out float centerY)
    {
        centerX = 0f;
        centerY = 0f;
        if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player) || player.IsDead)
            return false;

        centerX = player.PositionX + PlayerCenterOffsetX;
        centerY = player.PositionY + PlayerCenterOffsetY;
        return float.IsFinite(centerX) && float.IsFinite(centerY);
    }
}
