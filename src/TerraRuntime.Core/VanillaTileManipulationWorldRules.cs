namespace TerraRuntime.Core;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 packet-17 world-coordinate rules.
/// MessageBuffer calls WorldGen.InWorld(x, y, 3) before dispatching a tile action.
/// Core accepts primitive world dimensions here so the dependency direction remains Core -> Contracts rather
/// than introducing the forbidden Core -> World project edge.
/// </summary>
public static class VanillaTileManipulationWorldRules
{
    public const int Packet17WorldMargin = 3;

    public static bool IsInPacket17WorldBounds(int widthTiles, int heightTiles, int x, int y) =>
        widthTiles > Packet17WorldMargin * 2 &&
        heightTiles > Packet17WorldMargin * 2 &&
        x >= Packet17WorldMargin &&
        y >= Packet17WorldMargin &&
        x < widthTiles - Packet17WorldMargin &&
        y < heightTiles - Packet17WorldMargin;
}
