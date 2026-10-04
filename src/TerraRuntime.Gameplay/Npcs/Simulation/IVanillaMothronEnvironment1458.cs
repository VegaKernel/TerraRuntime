namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Read-only bounded world facts used by the admitted AI_088/089/090 family.</summary>
public interface IVanillaMothronEnvironment1458
{
    int WidthTiles { get; }
    int HeightTiles { get; }
    double WorldSurfaceTiles { get; }
    bool TryReadTile(int x, int y, out bool solid, out bool lava);
    bool CanHit(float sourceX, float sourceY, float targetX, float targetY);
    bool SolidCollision(float x, float y, int width, int height);
}
