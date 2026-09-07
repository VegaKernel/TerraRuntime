using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

/// <summary>
/// Post-commit observer for connection-free server-player lifecycle and state.
/// </summary>
internal interface IRuntimeServerPlayerEventSink
{
    void ServerPlayerCreated(in PlayerStateSnapshot player);

    void ServerPlayerAppearanceUpdated(PlayerHandle player, in ServerPlayerAppearanceState appearance);

    void ServerPlayerVitalsUpdated(PlayerHandle player, in ServerPlayerVitalsState vitals);

    void ServerPlayerPvpUpdated(PlayerHandle player, bool hostile);

    void ServerPlayerGodModeUpdated(PlayerHandle player, bool enabled);

    void ServerPlayerDied(PlayerHandle player, DamageSource source, ProjectileTypeId projectileType, int damage, int hitDirection);

    void ServerPlayerItemUpdated(PlayerHandle player, in ServerPlayerItemState item);

    void ServerPlayerMoved(in PlayerStateSnapshot player);

    void ServerPlayerRecallPresented(in PlayerStateSnapshot player, short floorX, short floorY);

    void ServerPlayerDespawned(PlayerHandle player);
}
