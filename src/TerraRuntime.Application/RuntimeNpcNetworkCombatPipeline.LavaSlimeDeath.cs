using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private WorldTileAuthority? deathWorldTiles;
    private WorldTileAuthority.LavaSlimeDeathPreparation? plannedLavaDeath;

    internal void BindDeathWorldTiles(WorldTileAuthority owner)
    {
        if (worldTiles is null || !owner.OwnsDeathTerrain(worldTiles, npcs, worldItems) ||
            deathWorldTiles is not null && !ReferenceEquals(deathWorldTiles, owner))
            throw new InvalidOperationException("NPC death terrain must bind once to the existing world owner.");
        deathWorldTiles = owner;
    }

    private bool TryPrepareLavaDeathTerrain(in NpcSnapshot dead)
    {
        // Source excludes Remix before consulting terrain or choosing an amount.
        bool sourceExpert = expertMode || npcSpecificGoodWorld || worldClock?.GetGoodWorld == true;
        if (lootRemixWorld == true || !sourceExpert && plannedSpecificLowTiles == false) return true;
        if (lootRemixWorld != false || plannedSpecificLowTiles is not { } lowTiles || deathWorldTiles is null ||
            !VanillaNpcDefinitionCatalog.TryGet(dead.TypeIdentity, dead.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(dead.Simulation, out var body)) return false;
        return deathWorldTiles.TryPrepareLavaSlimeDeath(dead.PositionX, dead.PositionY, body.Width, body.Height,
            lowTiles, random.SourceRandom, out plannedLavaDeath, drop => lootDelivery.Preview?.TryStage(NpcDeathDropPhase1458.Prelude, in drop) == true);
    }
}
