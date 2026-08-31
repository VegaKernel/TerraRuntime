using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Owned, protocol-neutral player spawn data submitted to the authoritative game loop after packet decoding.
/// The leased slot remains part of the connection-owned <see cref="PlayerJoinSession"/> until commit succeeds.
/// </summary>
public readonly record struct PlayerSpawnCommitRequest(
    PlayerSlotId ClaimedSlot,
    short SpawnX,
    short SpawnY,
    int RespawnTimer,
    short DeathsPve,
    short DeathsPvp,
    byte Team,
    byte SpawnContext);

/// <summary>
/// Posts a validated spawn candidate without exposing mutable game state to the network thread.
/// </summary>
public interface IPlayerSpawnCommitIngress
{
    bool TryPost(
        GameCommandSourceId source,
        PlayerJoinSession session,
        in PlayerSpawnCommitRequest request);
}
