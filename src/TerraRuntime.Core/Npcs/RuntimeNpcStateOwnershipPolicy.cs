using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// Owns runtime-local NPC state defaults and preservation rules independently from slot storage.
/// The NPC store owns identity/generation/revision and commit ordering; this policy owns which
/// combat/lifetime/presentation fields are materialized at spawn and preserved across state-only updates.
/// </summary>
internal static partial class RuntimeNpcStateOwnershipPolicy
{
    private static bool IsOwnedGroundCritter(int type, int netId) => VanillaNonresidentGroundCritterCatalog1458.IsSupported(type) && type == netId;
    private static int? GroundCritterProtectionAtSpawn(int type, int netId) =>
        IsOwnedGroundCritter(type, netId) ? VanillaNonresidentGroundCritterCatalog1458.HasCatchProtection(type) ? 90 : 0 : null;

    public static NpcStateUpdate MaterializeSpawnDefaults(in NpcStateUpdate update, VanillaNpcSpawnDefaults? spawnDefaults = null)
    {
        bool ownsPhase = VanillaNpcPhaseAccepted1458.SupportsIdentity(new(update.Type), new(update.NetId));
        bool ownsSlimeContinuation = update.Type == VanillaNpcIds.TownSlimeYellow.Value && update.NetId == update.Type;
        bool ownsFreshTransform = update.Type == update.NetId &&
            (VanillaNpcTransformation1458.IsNewDefinition(new(update.Type)) || ownsSlimeContinuation ||
             VanillaMartianChildAi1458.IsSupported(new(update.Type), new(update.NetId)) ||
             update.Type == VanillaNpcIds.Maggot.Value || update.Type == VanillaNpcIds.Scutlix.Value ||
             update.Type is 379 or 380 or 383 or 386 or 389 or 631 or 632);
        NpcSimulationState simulation = update.Simulation with {
            StairFall = update.Simulation.StairFall ?? false,
            TeleportTime = update.Simulation.TeleportTime ?? (ownsFreshTransform ? 0f : null),
            TeleportStyle = update.Simulation.TeleportStyle ?? (ownsFreshTransform ? 0 : null),
            BreathCounter = update.Simulation.BreathCounter ?? (ownsFreshTransform ? 0 : null),
            WetCount = update.Simulation.WetCount ?? (ownsPhase || ownsFreshTransform ? 0 : null),
            LavaWet = update.Simulation.LavaWet ?? (ownsPhase || ownsFreshTransform ? false : null),
            HoneyWet = update.Simulation.HoneyWet ?? (ownsPhase || ownsFreshTransform ? false : null),
            ShimmerWet = update.Simulation.ShimmerWet ?? (ownsPhase || ownsFreshTransform ? false : null),
            HomeTileX = update.Simulation.HomeTileX ?? -1,
            HomeTileY = update.Simulation.HomeTileY ?? -1,
            Homeless = update.Simulation.Homeless ?? false,
            CatchableNpcTempImmunityCounter = update.Simulation.CatchableNpcTempImmunityCounter ??
                (GroundCritterProtectionAtSpawn(update.Type, update.NetId) ??
                    (ownsPhase || ownsFreshTransform
                        ? VanillaNpcPhaseAccepted1458.HasCatchProtection(new(update.Type)) ? 90 : 0 : null)),
            ReleaseOwner = update.Simulation.ReleaseOwner ??
                (IsOwnedGroundCritter(update.Type, update.NetId) ||
                    ownsPhase || ownsFreshTransform ? (short)255 : null),
            FrameIndex = update.Simulation.FrameIndex ?? 0, Breath = update.Simulation.Breath ?? 200, LifeRegenCounter = update.Simulation.LifeRegenCounter ?? 0 };
        if (TryGetDefinition(update.Type, update.NetId, out VanillaNpcDefinition definition) && !definition.DefinitionOnly)
        {
            // A fresh NPC constructor owns zero. SetDefaults/Transform do not reset this source field.
            if (definition.Type == VanillaNpcIds.Zombie || ownsSlimeContinuation ||
                VanillaNpcPhaseAccepted1458.SupportsIdentity(definition.Type, new(update.NetId)))
                simulation = simulation with { ShimmerTransparency = simulation.ShimmerTransparency ?? 0f };
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
                BaseLifeMax = simulation.BaseLifeMax ?? spawnDefaults?.LifeMax ?? definition.LifeMax,
                SpawnDifficulty = simulation.SpawnDifficulty ?? 1f,
                MoneyValue = simulation.MoneyValue ?? spawnDefaults?.VerifiedMoneyValue ?? ResolveMoney(update.Type, update.NetId, simulation.SpawnDifficulty ?? 1f),
                ExtraMoneyValue = simulation.ExtraMoneyValue ?? 0,
                Midas = simulation.Midas ?? false,
                KnockBackResist = simulation.KnockBackResist ?? spawnDefaults?.KnockBackResist ?? definition.KnockBackResist,
                Rotation = simulation.Rotation ?? 0f,
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
        bool hasDefinition = TryGetDefinition(update.Type, update.NetId, out definition) && !definition.DefinitionOnly;

        simulation = simulation with
        {
            // A source same-identity AI-style change survives omitted local updates. SetDefaults
            // for a changed identity restores its definition's style; slot reuse gets a fresh state.
            AiStyleOverride = sameDefinition ? simulation.AiStyleOverride ?? previous.Simulation.AiStyleOverride : null,
            // SetDefaults/Transform do not clear source stair-fall history.
            StairFall = simulation.StairFall ?? previous.Simulation.StairFall ?? false,
            // SetDefaults leaves teleport visual history untouched. The source
            // NPC-phase prelude retires it, independently of identity changes.
            TeleportTime = simulation.TeleportTime ?? previous.Simulation.TeleportTime,
            TeleportStyle = simulation.TeleportStyle ?? previous.Simulation.TeleportStyle,
            BreathCounter = simulation.BreathCounter ?? (sameDefinition ? previous.Simulation.BreathCounter :
                previous.Simulation.BreathCounter.HasValue ? 0 : null),
            // SetDefaults clears the wet cooldown, while leaving the sticky liquid flags alone.
            // Unknown history stays unknown outside the source-owned creation/transform paths.
            WetCount = simulation.WetCount ?? (sameDefinition ? previous.Simulation.WetCount :
                previous.Simulation.WetCount.HasValue ? 0 : null),
            LavaWet = simulation.LavaWet ?? previous.Simulation.LavaWet,
            HoneyWet = simulation.HoneyWet ?? previous.Simulation.HoneyWet,
            ShimmerWet = simulation.ShimmerWet ?? previous.Simulation.ShimmerWet,
            // SetDefaults resets home memory, unlike stairFall. Omitted same-identity updates
            // preserve the committed fields; AI007 critters use them without town housing.
            HomeTileX = simulation.HomeTileX ?? (sameDefinition ? previous.Simulation.HomeTileX : -1),
            HomeTileY = simulation.HomeTileY ?? (sameDefinition ? previous.Simulation.HomeTileY : -1),
            Homeless = simulation.Homeless ?? (sameDefinition ? previous.Simulation.Homeless : false),
            CatchableNpcTempImmunityCounter = simulation.CatchableNpcTempImmunityCounter ??
                (sameDefinition ? previous.Simulation.CatchableNpcTempImmunityCounter : GroundCritterProtectionAtSpawn(update.Type, update.NetId)),
            ReleaseOwner = simulation.ReleaseOwner ??
                (sameDefinition ? previous.Simulation.ReleaseOwner : IsOwnedGroundCritter(update.Type, update.NetId) ? (short)255 : null),
            ShimmerTransparency = sameDefinition ? simulation.ShimmerTransparency : previous.Simulation.ShimmerTransparency,
            TownNpc = sameDefinition ? simulation.TownNpc :
                VanillaNpcTownFlags1458.TryGet(new(update.Type), out bool townNpc) ? townNpc : null,
            SpawnDifficulty = sameDefinition ? simulation.SpawnDifficulty ?? previous.Simulation.SpawnDifficulty ?? 1f : 1f,
            MoneyValue = simulation.MoneyValue ?? (sameDefinition ? previous.Simulation.MoneyValue :
                ResolveMoney(update.Type, update.NetId, 1f)),
            ExtraMoneyValue = simulation.ExtraMoneyValue ?? (sameDefinition ? previous.Simulation.ExtraMoneyValue : 0),
            Midas = simulation.Midas ?? (sameDefinition ? previous.Simulation.Midas : false),
            Rotation = sameDefinition ? simulation.Rotation ?? previous.Simulation.Rotation ?? 0f : 0f,
            KnockBackResist = sameDefinition ? simulation.KnockBackResist ?? previous.Simulation.KnockBackResist ??
                (hasDefinition ? definition.KnockBackResist : null) : hasDefinition ? definition.KnockBackResist : null,
            BaseDamage = sameDefinition ? simulation.BaseDamage ?? previous.Simulation.BaseDamage ??
                (hasDefinition ? definition.Damage : null) : hasDefinition ? definition.Damage : null,
            BaseDefense = sameDefinition ? simulation.BaseDefense ?? previous.Simulation.BaseDefense ??
                (hasDefinition ? definition.Defense : null) : hasDefinition ? definition.Defense : null,
            BaseLifeMax = sameDefinition ? simulation.BaseLifeMax ?? previous.Simulation.BaseLifeMax ??
                (hasDefinition ? definition.LifeMax : null) : hasDefinition ? definition.LifeMax : null,
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
            simulation = simulation with { Scale = definition.Scale, FrameIndex = 0, FrameCounter = 0d, Breath = 200, LifeRegenCounter = 0 };

        if (simulation.TimeLeft < 0)
        {
            simulation = simulation with
            {
                TimeLeft = sameDefinition && previous.Simulation.TimeLeft >= 0
                    ? previous.Simulation.TimeLeft
                    : VanillaNpcDefinitionCatalog.DefaultTimeLeft
            };
        }

        if (simulation.SpriteDirection == 0 &&
            !(sameDefinition && VanillaFlyingEyeNpcCatalog.IsPigron(new NpcTypeId(update.Type))))
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
    private static float? ResolveMoney(int type, short net, float difficulty) =>
        VanillaNpcMoneyDefaults1458.TryResolve(new(type), new(net), difficulty, out float amount) ? amount : null;

}
