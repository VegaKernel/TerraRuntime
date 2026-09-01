using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Compatibility facade for verified vanilla NPC targeting/AI dispatch. The facade resolves one version-pinned
/// definition and delegates the state step to an explicit runtime-owned behavior family strategy. Family-specific
/// targeting geometry, world conditions and motion rules are kept outside this dispatcher. Boss NPC spawn side
/// effects are exposed separately as speculative NPC/projectile intents and are committed only by
/// RuntimeNpcAiStateExecutor after the source state transition succeeds.
/// </summary>
public sealed class VanillaNpcTargetingAiStepper :
    INpcAiStateStepper,
    INpcAiSpawnIntentPlanner,
    INpcAiProjectileIntentPlanner,
    INpcAiPeerSnapshotConsumer
{
    public const int MaximumPlayerCandidates = VanillaNpcBehaviorContext.MaximumPlayerCandidates;

    private const float EyeOfCthulhuClassicServantCadenceThreshold = 110f;
    private const float EyeOfCthulhuExpertServantCadenceThreshold = 44f;
    private const float EyeOfCthulhuClassicServantSpeed = 5f;
    private const float EyeOfCthulhuExpertServantSpeed = 6f;
    private const float EyeOfCthulhuClassicPhaseOneHoverTicks = 600f;
    private const float EyeOfCthulhuExpertPhaseOneHoverTicks = 210f;
    private const int EyeOfCthulhuTransformationServantCadenceTicks = 20;
    private const int EyeOfCthulhuGoodWorldLowLifeTransformationServantCadenceTicks = 10;
    private const float EyeOfCthulhuTransformationServantSpeed = 5f;
    private const float EyeOfCthulhuServantSpawnLeadTicks = 10f;

    private readonly INpcAiStateStepper _inner;
    private readonly VanillaNpcBehaviorContext _context = new();
    private readonly IVanillaNpcBehaviorStrategy _slimeGround = new VanillaSlimeGroundNpcBehaviorStrategy();
    private readonly VanillaFlyingEyeNpcBehaviorStrategy _flyingEye = new();
    private readonly IVanillaNpcBehaviorStrategy _groundFighter = new VanillaGroundFighterNpcBehaviorStrategy();
    private readonly VanillaEyeOfCthulhuExpertRapidDashNpcBehaviorStrategy _eyeOfCthulhu;
    private readonly VanillaServantOfCthulhuNpcBehaviorStrategy _flyer;
    private readonly VanillaWormNpcBehaviorStrategy _worm = new();
    private readonly VanillaKingSlimeNpcBehaviorStrategy _kingSlime;
    private readonly VanillaBrainOfCthulhuNpcBehaviorStrategy _brainOfCthulhu;
    private readonly VanillaBrainCreeperNpcBehaviorStrategy _brainCreeper;
    private readonly IVanillaNpcBehaviorStrategy _vulture = new VanillaVultureNpcBehaviorStrategy();
    private readonly VanillaSpikeBallNpcBehaviorStrategy _spikeBall;
    private readonly IVanillaNpcBehaviorStrategy _blazingWheel = new VanillaBlazingWheelNpcBehaviorStrategy();
    private readonly VanillaSkeletronHeadNpcBehaviorStrategy _skeletronHead = new();
    private readonly VanillaSkeletronHandNpcBehaviorStrategy _skeletronHand = new();
    private readonly IVanillaNpcRandom _random;

    public VanillaNpcTargetingAiStepper(
        INpcAiStateStepper inner,
        IVanillaKingSlimeEnvironment? kingSlimeEnvironment = null,
        IVanillaNpcRandom? random = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _random = random ?? new SystemVanillaNpcRandom();
        _flyer = new VanillaServantOfCthulhuNpcBehaviorStrategy(_random);
        _eyeOfCthulhu = new VanillaEyeOfCthulhuExpertRapidDashNpcBehaviorStrategy(_random);
        _kingSlime = new VanillaKingSlimeNpcBehaviorStrategy(kingSlimeEnvironment);
        _brainOfCthulhu = new VanillaBrainOfCthulhuNpcBehaviorStrategy(_random);
        _brainCreeper = new VanillaBrainCreeperNpcBehaviorStrategy(_random);
        _spikeBall = new VanillaSpikeBallNpcBehaviorStrategy(_random);
        if (kingSlimeEnvironment is IVanillaEyeOfCthulhuEnvironment eyeEnvironment)
            _eyeOfCthulhu.SetEnvironment(eyeEnvironment);
        if (kingSlimeEnvironment is IVanillaBrainOfCthulhuEnvironment brainEnvironment)
            _brainOfCthulhu.SetEnvironment(brainEnvironment);
    }

    public void EnableBlueSlimeMotion(double worldSurfaceTiles = double.PositiveInfinity) =>
        _context.EnableSlimeGround(worldSurfaceTiles);

    public void EnableZombieMotion(double worldSurfaceTiles) =>
        _context.EnableGroundFighter(worldSurfaceTiles);

    public void SetPlayerSnapshotLookup(IRuntimePlayerSlotSnapshotLookup playerSnapshots) =>
        _context.SetPlayerSnapshotLookup(playerSnapshots);

    public void SetKingSlimeEnvironment(IVanillaKingSlimeEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _kingSlime.SetEnvironment(environment);
        if (environment is IVanillaEyeOfCthulhuEnvironment eyeEnvironment)
            _eyeOfCthulhu.SetEnvironment(eyeEnvironment);
        if (environment is IVanillaBrainOfCthulhuEnvironment brainEnvironment)
            _brainOfCthulhu.SetEnvironment(brainEnvironment);
    }

    public void SetBrainOfCthulhuEnvironment(IVanillaBrainOfCthulhuEnvironment environment) =>
        _brainOfCthulhu.SetEnvironment(environment);

    public void SetWormEnvironment(IVanillaWormEnvironment environment) =>
        _worm.SetEnvironment(environment);

    public void SetFlyingEyeEnvironment(IVanillaFlyingEyeEnvironment environment) =>
        _flyingEye.SetEnvironment(environment);

    public void SetProjectileEnvironment(IVanillaNpcProjectileEnvironment environment) =>
        _flyer.SetProjectileEnvironment(environment);

    public void SetWorldConditions(
        bool dayTime,
        bool slimeRainActive,
        bool goodWorld = false,
        bool expertMode = false,
        bool masterMode = false) =>
        _context.SetWorldConditions(dayTime, slimeRainActive, goodWorld, expertMode, masterMode);

    public void SetCandidates(ReadOnlySpan<VanillaNpcTargetCandidate> candidates) =>
        _context.SetCandidates(candidates);

    public void SetNpcPeers(ReadOnlySpan<NpcSnapshot> peers) =>
        _context.SetNpcPeers(peers);

    public bool TryGetCandidate(byte slot, out VanillaNpcTargetCandidate candidate) =>
        _context.TryFindCandidate(slot, out candidate);

    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        if (!NpcTypeId.TryCreate(npc.Type, out NpcTypeId npcType))
        {
            next = default;
            return false;
        }

        if (!VanillaNpcDefinitionCatalog.TryGet(npcType, npc.NetIdentity, out VanillaNpcDefinition definition))
            return _inner.TryStepState(in npc, out next);

        if (definition.Role == NpcArchetypeRole.Boss &&
            definition.BehaviorFamily == VanillaNpcBehaviorFamily.None)
        {
            next = default;
            return false;
        }

        IVanillaNpcBehaviorStrategy? strategy = definition.BehaviorFamily switch
        {
            VanillaNpcBehaviorFamily.SlimeGround when _context.SlimeGroundEnabled => _slimeGround,
            VanillaNpcBehaviorFamily.FlyingEye => _flyingEye,
            VanillaNpcBehaviorFamily.GroundFighter when _context.GroundFighterEnabled => _groundFighter,
            VanillaNpcBehaviorFamily.EyeOfCthulhu => _eyeOfCthulhu,
            VanillaNpcBehaviorFamily.Flyer => _flyer,
            VanillaNpcBehaviorFamily.Worm => _worm,
            VanillaNpcBehaviorFamily.KingSlime => _kingSlime,
            VanillaNpcBehaviorFamily.BrainOfCthulhu => _brainOfCthulhu,
            VanillaNpcBehaviorFamily.BrainCreeper => _brainCreeper,
            VanillaNpcBehaviorFamily.Vulture => _vulture,
            VanillaNpcBehaviorFamily.SpikeBall => _spikeBall,
            VanillaNpcBehaviorFamily.BlazingWheel => _blazingWheel,
            VanillaNpcBehaviorFamily.SkeletronHead => _skeletronHead,
            VanillaNpcBehaviorFamily.SkeletronHand => _skeletronHand,
            _ => null
        };

        return strategy is null
            ? _inner.TryStepState(in npc, out next)
            : strategy.TryStep(in npc, in definition, _context, _inner, out next);
    }

    public int PlanNpcSpawns(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiSpawnIntent> destination)
    {
        if (destination.IsEmpty)
            return 0;

        if (source.Type == VanillaNpcIds.EyeOfCthulhu.Value &&
            proposed.Type == VanillaNpcIds.EyeOfCthulhu.Value)
        {
            return PlanEyeOfCthulhuServant(in source, in proposed, destination);
        }

        if (source.Type == VanillaNpcIds.KingSlime.Value &&
            proposed.Type == VanillaNpcIds.KingSlime.Value)
        {
            return PlanKingSlimeMinions(in source, in proposed, destination);
        }

        if (source.Type == VanillaNpcIds.BrainOfCthulhu.Value &&
            proposed.Type == VanillaNpcIds.BrainOfCthulhu.Value)
        {
            return PlanBrainOfCthulhuCreepers(in source, in proposed, destination);
        }

        if (source.Type == VanillaNpcIds.SkeletronHead.Value &&
            proposed.Type == VanillaNpcIds.SkeletronHead.Value)
        {
            return PlanSkeletronHands(in source, in proposed, destination);
        }

        if (NpcTypeId.TryCreate(source.Type, out NpcTypeId sourceType) &&
            VanillaWormNpcCatalog.TryGet(sourceType, out _))
        {
            return PlanWormFollower(in source, in proposed, sourceType, destination);
        }

        return 0;
    }

    public int PlanProjectileSpawns(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiProjectileIntent> destination) =>
        _flyer.PlanProjectileSpawns(in source, in proposed, _context, destination);

    private int PlanSkeletronHands(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiSpawnIntent> destination)
    {
        if (destination.Length < 2 ||
            source.Ai.Ai0 != 0f ||
            proposed.Ai.Ai0 != 1f ||
            proposed.Type != source.Type ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.SkeletronHead, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            return 0;
        }

        int spawnX = (int)(source.PositionX + hitbox.Width * 0.5f);
        int spawnY = (int)(source.PositionY + hitbox.Height * 0.5f);
        destination[0] = new NpcAiSpawnIntent(
            VanillaNpcIds.SkeletronHand,
            spawnX,
            spawnY,
            0f,
            0f,
            proposed.Target)
        {
            InitialAi = new NpcAiState(-1f, source.Handle.Slot, 0f, 0f)
        };
        destination[1] = new NpcAiSpawnIntent(
            VanillaNpcIds.SkeletronHand,
            spawnX,
            spawnY,
            0f,
            0f,
            proposed.Target)
        {
            InitialAi = new NpcAiState(1f, source.Handle.Slot, 0f, 150f)
        };
        return 2;
    }

    private int PlanWormFollower(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        NpcTypeId sourceType,
        Span<NpcAiSpawnIntent> destination)
    {
        if (source.Ai.Ai0 != 0f ||
            proposed.Ai.Ai0 != 0f ||
            proposed.Type != source.Type ||
            !VanillaWormNpcCatalog.TryGet(sourceType, out VanillaWormNpcEntry worm) ||
            worm.Role == VanillaWormSegmentRole.Tail ||
            !VanillaWormNpcCatalog.HasChainProfile(worm.HeadType) ||
            !worm.Definition.TryResolveHitbox(
                proposed.Simulation.Scale,
                out VanillaNpcHitboxSize hitbox))
        {
            return 0;
        }

        int remaining;
        float rootSlot;
        if (worm.Role == VanillaWormSegmentRole.Head)
        {
            if (sourceType == VanillaNpcIds.EaterOfWorldsHead)
            {
                remaining = VanillaWormNpcCatalog.GetEaterOfWorldsBodyCount(_context.ExpertMode) - 1;
            }
            else if (VanillaWormNpcCatalog.TryGetFixedFollowerCount(sourceType, out int fixedCount))
            {
                remaining = fixedCount - 1;
            }
            else if (VanillaWormNpcCatalog.TryGetInitialSegmentCountRange(
                         sourceType,
                         out int minimum,
                         out int maximum))
            {
                remaining = _random.NextInt32(minimum, maximum) - 1;
            }
            else
            {
                return 0;
            }

            rootSlot = source.Handle.Slot;
        }
        else
        {
            if (!float.IsFinite(source.Ai.Ai2) ||
                source.Ai.Ai2 < 0f ||
                source.Ai.Ai2 > int.MaxValue ||
                source.Ai.Ai2 != MathF.Truncate(source.Ai.Ai2))
            {
                return 0;
            }

            if (VanillaWormNpcCatalog.TryGetFixedFollowerCount(worm.HeadType, out _) &&
                source.Ai.Ai2 == 0f)
            {
                return 0;
            }

            remaining = (int)source.Ai.Ai2 - 1;
            rootSlot = source.Ai.Ai3;
        }

        NpcTypeId childType;
        if (!VanillaWormNpcCatalog.TryGetFixedFollowerType(
                worm.HeadType,
                remaining,
                out childType))
        {
            childType = remaining >= 0 ? worm.BodyType : worm.TailType;
        }

        int childRemaining = Math.Max(remaining, 0);
        destination[0] = new NpcAiSpawnIntent(
            childType,
            BottomX: (int)(proposed.PositionX + hitbox.Width * 0.5f),
            BottomY: (int)(proposed.PositionY + hitbox.Height),
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget)
        {
            InitialAi = new NpcAiState(
                Ai0: 0f,
                Ai1: source.Handle.Slot,
                Ai2: childRemaining,
                Ai3: rootSlot),
            LinkSourceFollowerSlot = true
        };
        return 1;
    }

    private int PlanBrainOfCthulhuCreepers(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiSpawnIntent> destination)
    {
        if (source.Simulation.LocalAi.Ai0 != 0f ||
            proposed.Simulation.LocalAi.Ai0 != 1f ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.BrainOfCthulhu, out VanillaNpcDefinition brain) ||
            !brain.TryResolveHitbox(proposed.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            return 0;
        }

        int requested = _context.GoodWorld ? 40 : 20;
        int count = Math.Min(requested, destination.Length);
        float centerX = proposed.PositionX + hitbox.Width * 0.5f;
        float centerY = proposed.PositionY + hitbox.Height * 0.5f;
        for (int index = 0; index < count; index++)
        {
            int bottomX = (int)(centerX + _random.NextInt32(-hitbox.Width, hitbox.Width));
            int bottomY = (int)(centerY + _random.NextInt32(-hitbox.Height, hitbox.Height));
            float velocityX = _random.NextInt32(-30, 31) * 0.1f;
            float velocityY = _random.NextInt32(-30, 31) * 0.1f;
            destination[index] = new NpcAiSpawnIntent(
                VanillaNpcIds.BrainCreeper,
                bottomX,
                bottomY,
                velocityX,
                velocityY,
                VanillaNpcDefinitionCatalog.DefaultTarget);
        }

        return count;
    }

    private int PlanEyeOfCthulhuServant(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiSpawnIntent> destination)
    {
        if (_context.ExpertMode &&
            source.Ai.Ai0 is 1f or 2f &&
            float.IsFinite(source.Ai.Ai1) &&
            source.Ai.Ai1 >= 0f &&
            source.Ai.Ai1 < 100f)
        {
            int transformationCadence = EyeOfCthulhuTransformationServantCadenceTicks;
            if (_context.GoodWorld &&
                source.Simulation.LifeMax > 0 &&
                source.Simulation.Life < source.Simulation.LifeMax / 3)
            {
                transformationCadence = EyeOfCthulhuGoodWorldLowLifeTransformationServantCadenceTicks;
            }

            float nextTick = source.Ai.Ai1 + 1f;
            if (nextTick == MathF.Truncate(nextTick) &&
                (int)nextTick % transformationCadence == 0)
            {
                return PlanEyeOfCthulhuTransformationServant(in source, destination);
            }
        }

        float cadenceThreshold = _context.ExpertMode
            ? EyeOfCthulhuExpertServantCadenceThreshold
            : EyeOfCthulhuClassicServantCadenceThreshold;
        if (_context.GoodWorld)
            cadenceThreshold *= 0.8f;
        float hoverTicks = _context.ExpertMode
            ? EyeOfCthulhuExpertPhaseOneHoverTicks
            : EyeOfCthulhuClassicPhaseOneHoverTicks;
        if (source.Ai.Ai0 != 0f ||
            source.Ai.Ai1 != 0f ||
            proposed.Ai.Ai0 != 0f ||
            proposed.Ai.Ai1 != 0f ||
            source.Ai.Ai3 < cadenceThreshold - 1f ||
            proposed.Ai.Ai3 != 0f ||
            proposed.Ai.Ai2 != source.Ai.Ai2 + 1f ||
            proposed.Ai.Ai2 >= hoverTicks ||
            proposed.Target >= byte.MaxValue ||
            !_context.TryFindCandidate(checked((byte)proposed.Target), out VanillaNpcTargetCandidate target) ||
            !target.Active ||
            target.Dead ||
            target.Ghost ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.EyeOfCthulhu, out VanillaNpcDefinition eye) ||
            !eye.TryResolveHitbox(source.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            return 0;
        }

        float centerX = source.PositionX + hitbox.Width * 0.5f;
        float centerY = source.PositionY + hitbox.Height * 0.5f;
        float deltaX = target.CenterX - centerX;
        float deltaY = target.CenterY - centerY;
        float distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (!float.IsFinite(distance) || distance <= float.Epsilon)
            return 0;

        float servantSpeed = _context.ExpertMode
            ? EyeOfCthulhuExpertServantSpeed
            : EyeOfCthulhuClassicServantSpeed;
        float scale = servantSpeed / distance;
        float velocityX = deltaX * scale;
        float velocityY = deltaY * scale;
        int bottomX = (int)(centerX + velocityX * EyeOfCthulhuServantSpawnLeadTicks);
        int bottomY = (int)(centerY + velocityY * EyeOfCthulhuServantSpawnLeadTicks);

        destination[0] = new NpcAiSpawnIntent(
            Type: VanillaNpcIds.ServantOfCthulhu,
            BottomX: bottomX,
            BottomY: bottomY,
            VelocityX: velocityX,
            VelocityY: velocityY,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget);
        return 1;
    }

    private int PlanEyeOfCthulhuTransformationServant(
        in NpcSnapshot source,
        Span<NpcAiSpawnIntent> destination)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(
                VanillaNpcIds.EyeOfCthulhu,
                out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            return 0;
        }

        float randomX = _random.NextInt32(-200, 200);
        float randomY = _random.NextInt32(-200, 200);
        float distance = MathF.Sqrt(randomX * randomX + randomY * randomY);
        if (!float.IsFinite(distance) || distance <= float.Epsilon)
            return 0;

        float velocityScale = EyeOfCthulhuTransformationServantSpeed / distance;
        float velocityX = randomX * velocityScale;
        float velocityY = randomY * velocityScale;
        float centerX = source.PositionX + hitbox.Width * 0.5f;
        float centerY = source.PositionY + hitbox.Height * 0.5f;

        destination[0] = new NpcAiSpawnIntent(
            Type: VanillaNpcIds.ServantOfCthulhu,
            BottomX: (int)(centerX + velocityX * EyeOfCthulhuServantSpawnLeadTicks),
            BottomY: (int)(centerY + velocityY * EyeOfCthulhuServantSpawnLeadTicks),
            VelocityX: velocityX,
            VelocityY: velocityY,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget);
        return 1;
    }

    private int PlanKingSlimeMinions(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiSpawnIntent> destination)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.KingSlime, out VanillaNpcDefinition definition))
            return 0;

        int lifeMax = source.Simulation.LifeMax > 0 ? source.Simulation.LifeMax : definition.LifeMax;
        int life = source.Simulation.LifeMax > 0 ? source.Simulation.Life : definition.LifeMax;
        int threshold = (int)(lifeMax * VanillaKingSlimeMotion.MinionBurstLifeFraction);
        if (source.Ai.Ai3 <= 0f ||
            proposed.Ai.Ai3 != life ||
            !((float)(life + threshold) < source.Ai.Ai3) ||
            !definition.TryResolveHitbox(proposed.Simulation.Scale, out VanillaNpcHitboxSize hitbox) ||
            hitbox.Width <= 32 || hitbox.Height <= 32)
        {
            return 0;
        }

        int requested = _random.NextInt32(1, 4);
        int count = Math.Min(requested, destination.Length);
        for (int index = 0; index < count; index++)
        {
            int bottomX = (int)(proposed.PositionX + _random.NextInt32(0, hitbox.Width - 32));
            int bottomY = (int)(proposed.PositionY + _random.NextInt32(0, hitbox.Height - 32));
            NpcTypeId minionType = VanillaNpcIds.BlueSlime;
            if (_context.ExpertMode && _random.NextInt32(0, 4) == 0)
                minionType = VanillaNpcIds.SpikedSlime;

            float velocityX = _random.NextInt32(-15, 16) * 0.1f;
            float velocityY = _random.NextInt32(-30, 1) * 0.1f;
            float ai0 = -1000f * _random.NextInt32(0, 3);

            destination[index] = new NpcAiSpawnIntent(
                Type: minionType,
                BottomX: bottomX,
                BottomY: bottomY,
                VelocityX: velocityX,
                VelocityY: velocityY,
                Target: VanillaNpcDefinitionCatalog.DefaultTarget)
            {
                InitialAi = new NpcAiState(ai0, -1f, 0f, 0f)
            };
        }

        return count;
    }
}
