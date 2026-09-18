using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class ClientTileManipulationAdmissionPolicyTests
{
    [Theory]
    [InlineData((byte)TerrariaTileManipulationAction.KillTile, (byte)ClientTileManipulationAdmissionResult.Admitted)]
    [InlineData((byte)TerrariaTileManipulationAction.PlaceTile, (byte)ClientTileManipulationAdmissionResult.Admitted)]
    [InlineData((byte)TerrariaTileManipulationAction.KillWall, (byte)ClientTileManipulationAdmissionResult.Admitted)]
    [InlineData((byte)TerrariaTileManipulationAction.PlaceWall, (byte)ClientTileManipulationAdmissionResult.Admitted)]
    // Still closed: the source's KillTileNoItem suppresses the drop, which is a break path this runtime has not
    // separated from the ordinary one yet. Actions 5 and above - wiring, actuators, hammering, slopes, replace -
    // are not even wire identities here, so they answer UnknownWireAction rather than AuthorityUnavailable.
    [InlineData((byte)TerrariaTileManipulationAction.KillTileNoItem, (byte)ClientTileManipulationAdmissionResult.AuthorityUnavailable)]
    [InlineData(5, (byte)ClientTileManipulationAdmissionResult.UnknownWireAction)]
    [InlineData(7, (byte)ClientTileManipulationAdmissionResult.UnknownWireAction)]
    [InlineData(21, (byte)ClientTileManipulationAdmissionResult.UnknownWireAction)]
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
