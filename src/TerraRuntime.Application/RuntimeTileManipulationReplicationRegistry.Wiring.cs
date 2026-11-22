using TerraRuntime.Core;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTileManipulationReplicationRegistry
{
    internal bool TryPublishPoofOfSmoke(int x, int y) =>
        TerrariaPoofOfSmokeCodec1458.TryEncode(x, y, out var frame) && TryPublishFrameToAll(frame);

    internal bool TryPublishTemporaryAnimation(short animation, ushort tile, int x, int y) =>
        TerrariaTemporaryAnimationCodec1458.TryEncode(animation, tile, x, y, out var frame) && TryPublishFrameToAll(frame);

    internal bool TryPublishMassWiringPayments(ConnectionHandle connection, short wires, short actuators)
    {
        if (!endpoints.TryGetValue(connection.Source, out var endpoint) || !endpoint.IsPlayingFor(connection.Player)) return false;
        Publish(endpoint, new(TerrariaMassWiringCodec1458.EncodePayment(530, wires, connection.Player.Slot.Value)));
        Publish(endpoint, new(TerrariaMassWiringCodec1458.EncodePayment(849, actuators, connection.Player.Slot.Value)));
        return true;
    }

    public bool TryPublishHitSwitch(GameCommandSourceId excludedSource, in TerrariaHitSwitchState1458 state) =>
        TryPublishFrame(excludedSource, TerrariaHitSwitchCodec1458.Encode(in state));
}
