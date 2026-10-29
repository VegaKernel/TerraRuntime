using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed class RuntimeProjectileCollisionTileCut1458(VanillaUnifiedRandom1458 random)
{
    public bool Evaluate(in ProjectileSimulationStepResult step) => Evaluate(in step, random);

    internal static bool Evaluate(in ProjectileSimulationStepResult step, VanillaUnifiedRandom1458 random)
    {
        if (!step.CollisionTileCutOffer.HasValue) return false;
        // HandleMovement offers cutting BEFORE its owner/CanCutTiles/friendly/damage gates.
        // AI110 has zero source damage. Admitted hostile ground-combat shots use
        // the existing world-motion refusal when a cuttable tile is in their path.
        bool selected = random.Next(3) == 0;
        return selected && VanillaProjectileOwnership.IsServerOwned(step.State.Spawner) &&
            VanillaDefinitionCatalog.TryGet(step.State.Type, out var definition) && definition.CanCutTiles &&
            step.State.Damage > 0;
    }
}
