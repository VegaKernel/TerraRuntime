using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeChestCommandProcessor
{
    private Func<ConnectionHandle, TerrariaChestItemState, bool>? ownedItemReports;
    private Func<ConnectionHandle, PlayerAuthority.RemoteCraftCostCapture?>? captureRemoteCosts;
    internal void BindRemoteCraftCosts(Func<ConnectionHandle, PlayerAuthority.RemoteCraftCostCapture?> capture) => captureRemoteCosts = capture;
    internal RuntimeChestStore OwnedChestStore => store;
    internal WorldTileStore? OwnedChestTiles => craftingTiles;
    internal RuntimeChestReplicationRegistry OwnedChestReplication => replication;
    internal void BindOwnedItemReports(Func<ConnectionHandle, TerrariaChestItemState, bool> reports) =>
        ownedItemReports = reports ?? throw new ArgumentNullException(nameof(reports));
}
