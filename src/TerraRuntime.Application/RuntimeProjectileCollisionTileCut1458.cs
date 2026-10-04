using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed class RuntimeProjectileCollisionTileCut1458(VanillaUnifiedRandom1458 random)
{
    public bool Evaluate(in ProjectileSimulationStepResult step)
    {
        if (!step.CollisionTileCutOffer.HasValue) return false;
        // HandleMovement offers cutting BEFORE its owner/CanCutTiles/friendly/damage gates.
        // AI110 is admitted with genuine zero source damage, so its selected offer has no tile effect.
        bool selected = random.Next(3) == 0;
        return selected && VanillaProjectileOwnership.IsServerOwned(step.State.Spawner) &&
            VanillaDefinitionCatalog.TryGet(step.State.Type, out var definition) && definition.CanCutTiles &&
            step.State.Damage > 0;
    }
}
