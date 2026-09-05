using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Worlds;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 packet-17 world-coordinate rules.
/// MessageBuffer calls WorldGen.InWorld(x, y, 3) before dispatching a tile action.
/// The runtime consumes protocol-neutral tile bounds so Core never depends on the World storage assembly.
/// </summary>
public static class VanillaTileManipulationWorldRules
{
    public const int Packet17WorldMargin = 3;

    public static bool IsInPacket17WorldBounds(WorldTileDimensions dimensions, int x, int y) =>
        IsInPacket17WorldBounds(dimensions.WidthTiles, dimensions.HeightTiles, x, y);

    public static bool IsInPacket17WorldBounds(int widthTiles, int heightTiles, int x, int y) =>
        widthTiles > Packet17WorldMargin * 2 &&
        heightTiles > Packet17WorldMargin * 2 &&
        x >= Packet17WorldMargin &&
        y >= Packet17WorldMargin &&
        x < widthTiles - Packet17WorldMargin &&
        y < heightTiles - Packet17WorldMargin;
}
