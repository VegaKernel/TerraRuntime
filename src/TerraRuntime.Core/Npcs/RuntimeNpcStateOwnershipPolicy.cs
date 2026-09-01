using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Owns runtime-local NPC state defaults and preservation rules independently from slot storage.
/// The NPC store owns identity/generation/revision and commit ordering; this policy owns which
/// combat/lifetime/presentation fields are materialized at spawn and preserved across state-only updates.
/// </summary>
internal static class RuntimeNpcStateOwnershipPolicy
{
    public static NpcStateUpdate MaterializeSpawnDefaults(in NpcStateUpdate update)
    {
        NpcSimulationState simulation = update.Simulation;
        if (TryGetDefinition(update.Type, update.NetId, out VanillaNpcDefinition definition))
        {
            if (simulation.LifeMax == 0)
            {
                simulation = simulation with
                {
                    Life = definition.LifeMax,
                    LifeMax = definition.LifeMax
                };
            }

            simulation = simulation with { Scale = definition.Scale };

            if (definition.DontTakeDamageAtSpawn)
                simulation = simulation with { DontTakeDamage = true };

            if (definition.NoGravityAtSpawn || definition.NoTileCollideAtSpawn)
            {
                simulation = simulation with
                {
                    NoGravity = simulation.NoGravity || definition.NoGravityAtSpawn,
                    NoTileCollide = simulation.NoTileCollide || definition.NoTileCollideAtSpawn
                };
            }
        }

        if (simulation.TimeLeft < 0)
            simulation = simulation with { TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft };

        if (simulation.SpriteDirection == 0)
            simulation = simulation with { SpriteDirection = VanillaNpcDefinitionCatalog.DefaultSpriteDirection };

        return update with { Simulation = simulation };
    }

    public static NpcStateUpdate PreserveUnownedUpdateState(
        in NpcStateUpdate update,
        in NpcStateUpdate previous)
    {
        NpcSimulationState simulation = update.Simulation;
        bool sameType = update.Type == previous.Type;
        bool sameDefinition = sameType && update.NetId == previous.NetId;
        VanillaNpcDefinition definition = default;
        bool hasDefinition = TryGetDefinition(update.Type, update.NetId, out definition);

        if (simulation.LifeMax == 0)
        {
            if (sameDefinition && previous.Simulation.LifeMax > 0)
            {
                simulation = simulation with
                {
                    Life = previous.Simulation.Life,
                    LifeMax = previous.Simulation.LifeMax
                };
            }
            else if (hasDefinition)
            {
                simulation = simulation with
                {
                    Life = definition.LifeMax,
                    LifeMax = definition.LifeMax
                };
            }
        }

        if (!sameDefinition && hasDefinition)
            simulation = simulation with { Scale = definition.Scale };

        if (simulation.TimeLeft < 0)
        {
            simulation = simulation with
            {
                TimeLeft = sameDefinition && previous.Simulation.TimeLeft >= 0
                    ? previous.Simulation.TimeLeft
                    : VanillaNpcDefinitionCatalog.DefaultTimeLeft
            };
        }

        if (simulation.SpriteDirection == 0)
        {
            simulation = simulation with
            {
                SpriteDirection = sameDefinition && previous.Simulation.SpriteDirection != 0
                    ? previous.Simulation.SpriteDirection
                    : VanillaNpcDefinitionCatalog.DefaultSpriteDirection
            };
        }

        return update with { Simulation = simulation };
    }

    private static bool TryGetDefinition(
        int rawType,
        short rawNetId,
        out VanillaNpcDefinition definition)
    {
        if (!NpcTypeId.TryCreate(rawType, out NpcTypeId type))
        {
            definition = default;
            return false;
        }

        return VanillaNpcDefinitionCatalog.TryGet(type, new NpcNetId(rawNetId), out definition);
    }
}
