using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTileManipulationReplicationRegistry
{
    internal bool TryPublishToolFurnitureSquare(GameCommandSourceId excludedSource, WorldTileStore tiles,
        in TerrariaToolTileSquareState1458 state)
    {
        if (!TerrariaTileSquareCodec.TryEncodeToolServerFrame(in state, out var bytes))
        { Interlocked.Increment(ref encodeFailures); return false; }
        var frame = new OutboundFrame(bytes);
        int x = state.TileX, y = state.TileY;
        foreach ((GameCommandSourceId source, Endpoint endpoint) in endpoints)
        {
            if (source == excludedSource || !endpoint.IsPlaying) continue;
            var sections = endpoint.Sections;
            if (sections is not null && !Owns(x, y) && !Owns(x + 1, y) && !Owns(x, y + 1) && !Owns(x + 1, y + 1)) continue;
            Publish(endpoint, frame);
            bool Owns(int tx, int ty) => tx >= 0 && ty >= 0 && tx < tiles.Dimensions.WidthTiles &&
                ty < tiles.Dimensions.HeightTiles && sections!.OwnsSectionAtTile(tx, ty);
        }
        return true;
    }
}
