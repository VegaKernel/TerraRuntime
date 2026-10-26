using TerraRuntime.Protocol;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTileManipulationReplicationRegistry
{
    internal bool TryPublishFurnitureCorrection(TerraRuntime.Contracts.Runtime.GameCommandSourceId source,
        WorldTileStore tiles, int x, int y, byte width, byte height)
    {
        if (source.IsSystem || !TerrariaTileSquareCodec.TryEncode(tiles, x, y, width, height,
                TerraRuntime.Contracts.Gameplay.VanillaTileChangeType1458.None, out var bytes) ||
            !endpoints.TryGetValue(source, out var endpoint) || !endpoint.IsPlaying) return false;
        Publish(endpoint, new OutboundFrame(bytes)); return true;
    }
    internal bool TryPublishFurnitureFootprint(TerraRuntime.Contracts.Runtime.GameCommandSourceId excluded, byte[] frame) =>
        TryPublishFrame(excluded, frame);
    internal bool TryPublishFurnitureEntity(byte[] frame) => TryPublishFrameToAll(frame);

    internal bool TryPublishDollPose(TerraRuntime.Contracts.Runtime.GameCommandSourceId excludedSource,
        in TerrariaDollPoseState1458 pose) => TryPublishFrame(excludedSource, TerrariaDollPoseCodec1458.Encode(in pose));

    internal bool TryPublishToolTileSnapshot(WorldTileStore tiles, int x, int y, byte[] encoded) =>
        TryPublishEncodedTileSquare(encoded, tiles, x, y, 1, 1, respectSectionRange: true);

    internal bool TryPublishToolTreeEffect(in TerrariaTreeEffectsState1458 effect) =>
        TryPublishFrameToAll(TerrariaTreeEffectsCodec1458.Encode(in effect));
    internal bool TryPublishToolTileManipulation(in TerrariaTileManipulationState effect) =>
        TerrariaTileManipulationCodec.TryEncode(in effect, out byte[] encoded) == TerrariaTileManipulationEncodeResult.Encoded &&
        TryPublishFrameToAll(encoded);
    internal bool TryPublishToolChestUpdate(in TerrariaChestUpdatesState1458 effect) =>
        TryPublishFrameToAll(TerrariaChestUpdatesCodec1458.Encode(in effect));
}
