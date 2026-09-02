using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

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
