namespace TerraRuntime.Core.Npcs;

/// <summary>Tile query used by TerrariaServer 1.4.5.8 Everscream AI_057 hover control.</summary>
public interface IVanillaEverscreamEnvironment
{
    bool SolidCollision(float positionX, float positionY, int width, int height);
}
