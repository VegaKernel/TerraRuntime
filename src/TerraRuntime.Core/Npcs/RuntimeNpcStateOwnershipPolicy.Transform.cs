using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

internal static partial class RuntimeNpcStateOwnershipPolicy
{
    // NPC.Transform 1.4.5.8 applies SetDefaults during the phase, then continues
    // that phase. This materializes its fields before continuation; applying the
    // ordinary changed-identity normalization at the end would erase that work.
    internal static bool TryMaterializeTransform(in NpcSnapshot source, NpcTypeId targetType,
        in VanillaNpcSpawnContext context, in NpcAiState suppliedAi, VanillaNpcBehaviorContext behavior,
        out NpcSnapshot transformed)
    {
        transformed = default;
        if (!TryMaterializeTransformDefaults(in source, targetType, in context, in suppliedAi, out var state) ||
            !VanillaNpcDefinitionCatalog.TryGet(targetType, new(targetType.Value), out var definition)) return false;
        var reset = source with { Type = state.Type, NetId = state.NetId,
            PositionX = state.PositionX, PositionY = state.PositionY, VelocityX = state.VelocityX, VelocityY = state.VelocityY,
            Target = state.Target, Ai = state.Ai, Simulation = state.Simulation };
        if (!behavior.TrySelectTransformClosestTarget(in reset, in definition, out var closest)) return false;
        transformed = reset with { Target = closest.Target, Simulation = reset.Simulation with
            { DirectionX = closest.DirectionX, DirectionY = closest.DirectionY } };
        return true;
    }

    private static bool TryMaterializeTransformDefaults(in NpcSnapshot source, NpcTypeId targetType,
        in VanillaNpcSpawnContext context, in NpcAiState suppliedAi,
        out NpcStateUpdate transformed)
    {
        transformed = default;
        if (!source.IsActive || source.Type != source.NetId ||
            !VanillaNpcTransformation1458.Supports(source.TypeIdentity, targetType) ||
            source.Simulation.LifeMax <= 0 || source.Simulation.Life < 0 ||
            source.Simulation.MoneyValue is not float previousMoney ||
            !VanillaNpcDefinitionCatalog.TryGet(source.TypeIdentity, source.NetIdentity, out var sourceDefinition) ||
            !sourceDefinition.TryResolveHitbox(source.Simulation, out var beforeBody) ||
            !VanillaNpcDefinitionCatalog.TryGet(targetType, new(targetType.Value), out var targetDefinition) ||
            !VanillaNpcSpawnDefaults.TryResolve(in targetDefinition, in context, out var defaults) ||
            defaults.VerifiedMoneyValue is not float targetMoney || defaults.LifeMax <= 0 ||
            !suppliedAi.IsValidFor(targetType) ||
            !VanillaNpcTownFlags1458.TryGet(targetType, out bool town))
            return false;

        // Transform uses integer arithmetic, including the source multiplication
        // order, rather than converting the retained life ratio to floating point.
        int life = Math.Max(1, unchecked(source.Simulation.Life * defaults.LifeMax) / source.Simulation.LifeMax);
        bool catchable = VanillaNpcTransformation1458.HasDestinationCatchProtection(targetType);
        var simulation = source.Simulation with
        {
            AiStyleOverride = null,
            HitboxOverride = new(defaults.Hitbox.Width, defaults.Hitbox.Height),
            HomeTileX = -1,
            HomeTileY = -1,
            Homeless = town,
            CatchableNpcTempImmunityCounter = catchable ? 90 : 0,
            ReleaseOwner = 255,
            Life = life,
            LifeMax = defaults.LifeMax,
            JustHit = false,
            LifeRegenCounter = 0,
            TownNpc = town,
            Breath = 200,
            BreathCounter = 0,
            Confused = false,
            TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
            LocalAi = default,
            FrameCounter = 0d,
            FrameIndex = 0,
            Hidden = targetDefinition.HiddenAtSpawn,
            CanBeReplacedByOtherNpcs = false,
            DontTakeDamage = targetDefinition.DontTakeDamageAtSpawn,
            Friendly = catchable || VanillaNpcChaseability1458.FriendlyAtSpawn(targetType.Value),
            Chaseable = VanillaNpcChaseability1458.ChaseableAtSpawn(targetType.Value),
            Immortal = VanillaNpcChaseability1458.ImmortalAtSpawn(targetType.Value),
            Alpha = targetDefinition.AlphaAtSpawn,
            DefenseOverride = defaults.Defense,
            DamageOverride = defaults.Damage,
            BaseDamage = defaults.Damage,
            BaseDefense = defaults.Defense,
            BaseLifeMax = defaults.LifeMax,
            SpawnDifficulty = defaults.Difficulty ?? context.Difficulty,
            MoneyValue = previousMoney == 0f ? 0f : targetMoney,
            Midas = false,
            KnockBackResist = defaults.KnockBackResist ?? targetDefinition.KnockBackResist,
            Rotation = 0f,
            ReflectsProjectiles = false,
            CollideX = false,
            CollideY = false,
            WetCount = 0,
            NoGravity = targetDefinition.NoGravityAtSpawn,
            NoTileCollide = targetDefinition.NoTileCollideAtSpawn,
            Scale = defaults.Scale
        };
        float y = source.PositionY + beforeBody.Height;
        y -= defaults.Hitbox.Height;
        if (targetType == VanillaNpcIds.TownSlimeYellow)
            simulation = simulation with
            {
                HomeTileX = (int)(source.PositionX + defaults.Hitbox.Width / 2) / 16,
                HomeTileY = (int)(y + defaults.Hitbox.Height) / 16
            };
        transformed = new(targetType.Value, checked((short)targetType.Value), source.PositionX, y,
            source.VelocityX, source.VelocityY, byte.MaxValue, suppliedAi, simulation);
        return RuntimeNpcStore.IsValid(in transformed);
    }
}
