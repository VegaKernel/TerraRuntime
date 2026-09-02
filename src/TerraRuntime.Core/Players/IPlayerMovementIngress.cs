using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Posts validated player movement into the authoritative game loop.
/// </summary>
public interface IPlayerMovementIngress
{
    bool TryPost(ConnectionHandle connection, in PlayerMovementCommitRequest request);
}
