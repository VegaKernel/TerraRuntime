using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class ClientTileManipulationAdmissionPolicyTests
{
    [Theory]
    [InlineData((byte)TerrariaTileManipulationAction.KillTile, ClientTileManipulationAdmissionResult.Admitted)]
    [InlineData((byte)TerrariaTileManipulationAction.PlaceTile, ClientTileManipulationAdmissionResult.Admitted)]
    [InlineData((byte)TerrariaTileManipulationAction.KillWall, ClientTileManipulationAdmissionResult.AuthorityUnavailable)]
    [InlineData((byte)TerrariaTileManipulationAction.PlaceWall, ClientTileManipulationAdmissionResult.AuthorityUnavailable)]
    [InlineData((byte)TerrariaTileManipulationAction.KillTileNoItem, ClientTileManipulationAdmissionResult.AuthorityUnavailable)]
    [InlineData(255, ClientTileManipulationAdmissionResult.UnknownWireAction)]
    public void Admission_is_runtime_owned_and_fail_closed(
        byte rawAction,
        ClientTileManipulationAdmissionResult expected)
    {
        var state = new TerrariaTileManipulationState(rawAction, 10, 10, 0, 0);

        ClientTileManipulationAdmissionResult result =
            ClientTileManipulationAdmissionPolicy.Evaluate(in state, out TerrariaTileManipulationAction action);

        Assert.Equal(expected, result);
        if (expected != ClientTileManipulationAdmissionResult.UnknownWireAction)
            Assert.Equal(rawAction, (byte)action);
    }

    [Theory]
    [InlineData((byte)TerrariaTileManipulationAction.KillTile, true)]
    [InlineData((byte)TerrariaTileManipulationAction.PlaceTile, true)]
    [InlineData((byte)TerrariaTileManipulationAction.KillWall, false)]
    [InlineData((byte)TerrariaTileManipulationAction.PlaceWall, false)]
    [InlineData((byte)TerrariaTileManipulationAction.KillTileNoItem, false)]
    [InlineData(255, false)]
    public void Existing_runtime_call_shape_delegates_to_runtime_policy(byte rawAction, bool admitted)
    {
        var state = new TerrariaTileManipulationState(rawAction, 10, 10, 0, 0);

        bool resolved = state.TryGetKnownAction(out TerrariaTileManipulationAction action);

        Assert.Equal(admitted, resolved);
        if (admitted)
            Assert.Equal(rawAction, (byte)action);
    }
}
