using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// Owns runtime-local NPC state defaults and preservation rules independently from slot storage.
/// The NPC store owns identity/generation/revision and commit ordering; this policy owns which
/// combat/lifetime/presentation fields are materialized at spawn and preserved across state-only updates.
/// </summary>
internal static class RuntimeNpcStateOwnershipPolicy
{
    public static NpcStateUpdate MaterializeSpawnDefaults(in NpcStateUpdate update, VanillaNpcSpawnDefaults? spawnDefaults = null)
    {
        NpcSimulationState simulation = update.Simulation;
        if (TryGetDefinition(update.Type, update.NetId, out VanillaNpcDefinition definition))
        {
            if (simulation.LifeMax == 0)
            {
                simulation = simulation with
                {
                    Life = spawnDefaults?.LifeMax ?? definition.LifeMax,
                    LifeMax = spawnDefaults?.LifeMax ?? definition.LifeMax
                };
            }

            simulation = simulation with
            {
                Scale = spawnDefaults?.Scale ?? definition.Scale,
                BaseDamage = simulation.BaseDamage ?? spawnDefaults?.Damage ?? definition.Damage,
                BaseDefense = simulation.BaseDefense ?? spawnDefaults?.Defense ?? definition.Defense,
                SpawnDifficulty = simulation.SpawnDifficulty ?? 1f,
                KnockBackResist = simulation.KnockBackResist ?? spawnDefaults?.KnockBackResist ?? definition.KnockBackResist,
                Alpha = simulation.Alpha == 0 ? definition.AlphaAtSpawn : simulation.Alpha,
                Friendly = simulation.Friendly ?? VanillaNpcChaseability1458.FriendlyAtSpawn(update.Type),
                Chaseable = simulation.Chaseable ?? VanillaNpcChaseability1458.ChaseableAtSpawn(update.Type),
                Immortal = simulation.Immortal ?? VanillaNpcChaseability1458.ImmortalAtSpawn(update.Type)
            };

            if (spawnDefaults is { } scaled)
            {
                // Difficulty can change visual scale after physical dimensions have already been assigned.
                NpcHitboxDimensions? physical = scaled.Scale != definition.Scale ||
                    scaled.Hitbox.Width != definition.Width || scaled.Hitbox.Height != definition.Height
                    ? new(scaled.Hitbox.Width, scaled.Hitbox.Height) : null;
                simulation = simulation with
                {
                    HitboxOverride = simulation.HitboxOverride ?? physical,
                    DamageOverride = simulation.DamageOverride ?? (scaled.Damage != definition.Damage ? scaled.Damage : null),
                    DefenseOverride = simulation.DefenseOverride ?? (scaled.Defense != definition.Defense ? scaled.Defense : null)
                };
            }

            if (definition.HiddenAtSpawn)
                simulation = simulation with { Hidden = true };

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

        simulation = simulation with
        {
            SpawnDifficulty = sameDefinition ? simulation.SpawnDifficulty ?? previous.Simulation.SpawnDifficulty ?? 1f : 1f,
            KnockBackResist = sameDefinition ? simulation.KnockBackResist ?? previous.Simulation.KnockBackResist ??
                (hasDefinition ? definition.KnockBackResist : null) : hasDefinition ? definition.KnockBackResist : null,
            BaseDamage = sameDefinition ? simulation.BaseDamage ?? previous.Simulation.BaseDamage ??
                (hasDefinition ? definition.Damage : null) : hasDefinition ? definition.Damage : null,
            BaseDefense = sameDefinition ? simulation.BaseDefense ?? previous.Simulation.BaseDefense ??
                (hasDefinition ? definition.Defense : null) : hasDefinition ? definition.Defense : null,
            HitboxOverride = sameDefinition ? simulation.HitboxOverride ?? previous.Simulation.HitboxOverride : simulation.HitboxOverride,
            Friendly = simulation.Friendly ?? (sameDefinition ? previous.Simulation.Friendly : null) ??
                (hasDefinition ? VanillaNpcChaseability1458.FriendlyAtSpawn(update.Type) : null),
            Chaseable = simulation.Chaseable ?? (sameDefinition ? previous.Simulation.Chaseable : null) ??
                (hasDefinition ? VanillaNpcChaseability1458.ChaseableAtSpawn(update.Type) : null),
            Immortal = simulation.Immortal ?? (sameDefinition ? previous.Simulation.Immortal : null) ??
                (hasDefinition ? VanillaNpcChaseability1458.ImmortalAtSpawn(update.Type) : null)
        };

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
