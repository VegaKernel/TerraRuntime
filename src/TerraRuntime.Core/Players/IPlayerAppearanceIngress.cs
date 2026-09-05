using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Players;

/// <summary>
/// Posts a normalized player appearance candidate into authoritative execution.
/// </summary>
public interface IPlayerAppearanceIngress
{
    bool TryPost(ConnectionHandle connection, in PlayerAppearanceCommitRequest request);
}
