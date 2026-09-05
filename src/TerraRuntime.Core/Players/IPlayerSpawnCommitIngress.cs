using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Players;

/// <summary>
/// Posts a validated spawn candidate without exposing mutable game state to the network thread.
/// </summary>
public interface IPlayerSpawnCommitIngress
{
    bool TryPost(
        GameCommandSourceId source,
        PlayerJoinSession session,
        in PlayerSpawnCommitRequest request);

    /// <summary>
    /// Posts a subsequent vanilla packet-12 spawn/recall for an already-playing slot.
    /// Implementations that only support initial join may keep the default rejection.
    /// </summary>
    bool TryPostRespawn(
        GameCommandSourceId source,
        PlayerHandle player,
        in PlayerSpawnCommitRequest request) => false;
}
