using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal interface IFurniturePlacementNetworkIngress1458
{
    bool TryPostFurnitureSquare(ConnectionHandle connection, TerrariaFurnitureSquareState1458 state);
    bool TryPostFurnitureEntity(ConnectionHandle connection, in TerrariaFurnitureEntityPlacementState1458 state);
}
internal partial class RuntimeTileNetworkIngress : IFurniturePlacementNetworkIngress1458
{
    public bool TryPostFurnitureSquare(ConnectionHandle connection, TerrariaFurnitureSquareState1458 state) =>
        connection.IsAssigned && Ingress.TryPost(connection.Source, new ClientFurniturePlacementSquareCommand1458(connection, state));
    public bool TryPostFurnitureEntity(ConnectionHandle connection, in TerrariaFurnitureEntityPlacementState1458 state) =>
        connection.IsAssigned && Ingress.TryPost(connection.Source, new ClientFurniturePlacementEntityCommand1458(connection, state));
}
internal sealed record ClientFurniturePlacementSquareCommand1458(ConnectionHandle Connection,
    TerrariaFurnitureSquareState1458 State) : RuntimeCommand;
internal sealed record ClientFurniturePlacementEntityCommand1458(ConnectionHandle Connection,
    TerrariaFurnitureEntityPlacementState1458 State) : RuntimeCommand;
