using TerraRuntime.Core.Projectiles;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private PlayerNpcHealthWorld1458? remotePlayerHealthWorld;
    private RuntimeProjectileStore? remotePlayerProjectiles;

    internal void SetRemotePlayerEnvironment(PlayerNpcHealthWorld1458 world, RuntimeProjectileStore projectiles)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        if ((remotePlayerHealthWorld is { } previous && previous != world) ||
            (remotePlayerProjectiles is not null && !ReferenceEquals(remotePlayerProjectiles, projectiles)))
            throw new InvalidOperationException("Source player environment is bound once per runtime session.");
        remotePlayerHealthWorld = world;
        remotePlayerProjectiles = projectiles;
    }

    private bool? CaptureRemotePlayerGrappling(PlayerHandle player) =>
        remotePlayerProjectiles is { } projectiles ? CaptureNpcHealthGrappling(projectiles, player) : null;

    internal static bool? CaptureNpcHealthGrappling(RuntimeProjectileStore projectiles, PlayerHandle player)
    {
        for (int slot = 0; slot < projectiles.Capacity; slot++)
        {
            if (!projectiles.TryGetActive((ushort)slot, out var projectile) || projectile.Spawner != player.Slot.Value)
                continue;
            // Unknown metadata and source hook attachment history cannot establish the retained
            // player grappling slots. Read concrete owned generations without extension callbacks.
            if (!VanillaDefinitionCatalog.TryGet(projectile.Type, out var definition) || definition.AiStyle.Value == 7)
                return null;
        }
        return false;
    }
}
