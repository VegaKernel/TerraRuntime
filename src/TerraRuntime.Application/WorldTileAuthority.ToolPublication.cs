using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    // Invoke only after the publication owner has checked its exact accepted
    // target afterimage. A system-origin cascade passes an unassigned sender,
    // so all represented local tool caches receive the source notification.
    internal bool TryPublishToolTile(GameCommandSourceId excludedSource, PlayerHandle sender,
        in TerrariaTileManipulationState state)
    {
        var excludedPlayer = excludedSource.IsSystem ? default : sender;
        if (state.Action == 0 && state.Data == 0 &&
            !players.TryClearOtherToolHits(excludedPlayer, state.TileX, state.TileY, this))
        {
            if (tiles is { } current) replication?.TryPublishTileSquareToAll(current, state.TileX, state.TileY);
            return false;
        }
        return replication?.TryPublishCommitted(excludedSource, in state) == true;
    }
}
