using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

/// <summary>Owned tile/player query for the dedicated-server AI_008 Dark Caster teleport search (1.4.5.8).</summary>
public interface IVanillaCasterEnvironment
{
    bool TryFindTeleportSpot(float npcCenterX, float npcCenterY, int targetTileX, int targetTileY,
        bool skeletronActive, ReadOnlySpan<VanillaNpcTargetCandidate> players, IVanillaNpcRandom random,
        out int tileX, out int tileY);
}
