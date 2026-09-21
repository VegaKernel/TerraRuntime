using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>NPC.SetDefaults AI initialization that occurs before the first authoritative AI update in 1.4.5.8.</summary>
public static class VanillaNpcAiSpawnDefaults1458
{
    /// <summary>
    /// Applies the SetDefaults timer only when the caller did not provide NewNPC AI arguments.
    /// A nonzero state remains caller-owned, matching explicit NewNPC ai parameters.
    /// </summary>
    public static NpcAiState Resolve(NpcTypeId type, in VanillaNpcDefinition definition, NpcAiState supplied,
        IVanillaNpcRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (supplied != default || definition.AiStyle != VanillaNpcAiStyles.Caster)
            return supplied;

        if (type == VanillaNpcIds.RuneWizard)
            return new NpcAiState(450f, 0f, 0f, 0f);

        // NPC.SetDefaults rolls this branch only for the two Hardmode Dungeon Skeleton beam casters.
        // The original leaves the ordinary AI_008 initialization at 400 when the roll is nonzero.
        if (type.Value is 283 or 284 && random.NextInt32(0, 2) == 0)
            return new NpcAiState(390f, 0f, 0f, 0f);

        return new NpcAiState(400f, 0f, 0f, 0f);
    }
}
