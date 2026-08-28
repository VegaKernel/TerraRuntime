using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

internal sealed class VanillaWorldNpcCanHitQuery(WorldTileStore tiles) : IVanillaNpcCanHitQuery
{
    private const float VanillaPlayerWidth = 20f;
    private const float VanillaPlayerHeight = 42f;

    private readonly WorldTileStore _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    public bool CanHit(in NpcSnapshot npc, in VanillaNpcTargetCandidate target)
    {
        if (!NpcTypeId.TryCreate(npc.Type, out NpcTypeId npcType) ||
            !VanillaNpcDefinitionCatalog.TryGet(npcType, out VanillaNpcDefinition definition))
        {
            return false;
        }

        float targetX = target.CenterX - VanillaPlayerWidth * 0.5f;
        float targetY = target.CenterY - VanillaPlayerHeight * 0.5f;
        return VanillaWorldCanHit.HasLineOfSight(
            _tiles,
            npc.PositionX,
            npc.PositionY,
            definition.Width,
            definition.Height,
            targetX,
            targetY,
            checked((int)VanillaPlayerWidth),
            checked((int)VanillaPlayerHeight));
    }
}
