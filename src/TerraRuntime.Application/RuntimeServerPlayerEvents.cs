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

    void ServerPlayerItemUpdated(PlayerHandle player, in ServerPlayerItemState item);

    void ServerPlayerMoved(in PlayerStateSnapshot player);

    void ServerPlayerDespawned(PlayerHandle player);
}
