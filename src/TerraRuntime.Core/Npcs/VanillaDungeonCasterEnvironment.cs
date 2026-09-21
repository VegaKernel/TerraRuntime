using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

/// <summary>World query used by AI_008 Hardmode Dungeon Skeleton caster teleport placement.</summary>
public interface IVanillaDungeonCasterEnvironment
{
    bool TryFindDungeonCasterTeleportSpot(
        float npcCenterX,
        float npcCenterY,
        int targetTileX,
        int targetTileY,
        bool skeletronActive,
        ReadOnlySpan<VanillaNpcTargetCandidate> players,
        IVanillaNpcRandom random,
        out int tileX,
        out int tileY);
}
