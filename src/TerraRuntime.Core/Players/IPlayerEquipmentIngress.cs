using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Players;

/// <summary>
/// Posts a normalized equipment/inventory candidate into authoritative execution.
/// </summary>
public interface IPlayerEquipmentIngress
{
    bool TryPost(ConnectionHandle connection, in PlayerEquipmentCommitRequest request);

    /// <summary>Posts the empty vanilla138 delimiter after the ordered packet5 inventory/bank batch.</summary>
    bool TryPostInventorySyncComplete(ConnectionHandle connection) => false;
}
