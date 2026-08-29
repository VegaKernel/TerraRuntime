using TerraRuntime.World;

namespace TerraRuntime.Core;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 packet-17 world-coordinate rules.
/// MessageBuffer calls WorldGen.InWorld(x, y, 3) before dispatching a tile action.
/// </summary>
public static class VanillaTileManipulationWorldRules
{
    public const int Packet17WorldMargin = 3;

    public static bool IsInPacket17WorldBounds(WorldDimensions dimensions, int x, int y) =>
        x >= Packet17WorldMargin &&
        y >= Packet17WorldMargin &&
        x < dimensions.WidthTiles - Packet17WorldMargin &&
        y < dimensions.HeightTiles - Packet17WorldMargin;
}
