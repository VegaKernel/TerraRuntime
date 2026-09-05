using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Players;

/// <summary>
/// Posts a normalized equipment/inventory candidate into authoritative execution.
/// </summary>
public interface IPlayerEquipmentIngress
{
    bool TryPost(ConnectionHandle connection, in PlayerEquipmentCommitRequest request);
}
