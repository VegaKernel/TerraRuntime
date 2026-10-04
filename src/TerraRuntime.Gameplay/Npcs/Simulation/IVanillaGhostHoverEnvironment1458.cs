namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Bounded source AI_022 ahead/below nactive-solid-or-liquid scan and entity sight.</summary>
public interface IVanillaGhostHoverEnvironment1458
{
    bool TryHasObstacle(int tileX, int tileY, out bool obstacle);

    bool CanHit(float sourceX, float sourceY, int sourceWidth, int sourceHeight,
        float targetX, float targetY, int targetWidth, int targetHeight);
}
