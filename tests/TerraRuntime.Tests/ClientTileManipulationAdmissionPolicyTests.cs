using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class ClientTileManipulationAdmissionPolicyTests
{
    [Theory]
    [InlineData((byte)TerrariaTileManipulationAction.KillTile, (byte)ClientTileManipulationAdmissionResult.Admitted)]
    [InlineData((byte)TerrariaTileManipulationAction.PlaceTile, (byte)ClientTileManipulationAdmissionResult.Admitted)]
    [InlineData((byte)TerrariaTileManipulationAction.KillWall, (byte)ClientTileManipulationAdmissionResult.AuthorityUnavailable)]
    [InlineData((byte)TerrariaTileManipulationAction.PlaceWall, (byte)ClientTileManipulationAdmissionResult.AuthorityUnavailable)]
    [InlineData((byte)TerrariaTileManipulationAction.KillTileNoItem, (byte)ClientTileManipulationAdmissionResult.AuthorityUnavailable)]
    [InlineData(255, (byte)ClientTileManipulationAdmissionResult.UnknownWireAction)]
    public void Admission_is_runtime_owned_and_fail_closed(byte rawAction, byte expectedRaw)
    {
        var state = new TerrariaTileManipulationState(rawAction, 10, 10, 0, 0);
        ClientTileManipulationAdmissionResult expected = (ClientTileManipulationAdmissionResult)expectedRaw;

        ClientTileManipulationAdmissionResult result =
            ClientTileManipulationAdmissionPolicy.Evaluate(in state, out TerrariaTileManipulationAction action);

        Assert.Equal(expected, result);
        if (expected != ClientTileManipulationAdmissionResult.UnknownWireAction)
            Assert.Equal(rawAction, (byte)action);
    }


}
