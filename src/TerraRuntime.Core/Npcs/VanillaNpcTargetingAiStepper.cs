using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Version-pinned dispatcher for verified vanilla NPC targeting/AI behavior. The dispatcher resolves one source-backed
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
    private readonly VanillaQueenBeeNpcBehaviorStrategy _queenBee;
    private readonly VanillaDeerclopsNpcBehaviorStrategy _deerclops = new();
    private readonly VanillaWallOfFleshNpcBehaviorStrategy _wallOfFlesh = new();
    private readonly VanillaWallOfFleshEyeNpcBehaviorStrategy _wallOfFleshEye = new();
    private readonly VanillaWallOfFleshHungryNpcBehaviorStrategy _wallOfFleshHungry = new();
    private readonly VanillaFireImpNpcBehaviorStrategy _fireImp = new();
    private readonly VanillaBurningSphereNpcBehaviorStrategy _burningSphere = new();
    private readonly VanillaQueenSlimeNpcBehaviorStrategy _queenSlime;
    private readonly VanillaSkeletronPrimeNpcBehaviorStrategy _skeletronPrime = new();
    private readonly VanillaSkeletronPrimeLimbNpcBehaviorStrategy _skeletronPrimeLimb = new();
    private readonly VanillaTwinNpcBehaviorStrategy _retinazer = new(false);
    private readonly VanillaTwinNpcBehaviorStrategy _spazmatism = new(true);
    private readonly VanillaDestroyerNpcBehaviorStrategy _destroyer;
    private readonly IVanillaNpcRandom _random;
    private IVanillaNpcProjectileEnvironment? _projectileEnvironment;
    private IVanillaQueenBeeEnvironment? _queenBeeEnvironment;
    private IVanillaDeerclopsEnvironment? _deerclopsEnvironment;
    private IVanillaWallOfFleshEnvironment? _wallOfFleshEnvironment;
    private RuntimeNpcPlayerInteractionLedger? _playerInteractions;

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
        _queenBee = new VanillaQueenBeeNpcBehaviorStrategy(_random);
        _queenSlime = new VanillaQueenSlimeNpcBehaviorStrategy(_random, kingSlimeEnvironment);
        _destroyer = new VanillaDestroyerNpcBehaviorStrategy(_random);
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
        _queenSlime.SetEnvironment(environment);
    }

    public void SetBrainOfCthulhuEnvironment(IVanillaBrainOfCthulhuEnvironment environment) =>
        _brainOfCthulhu.SetEnvironment(environment);

    public void SetQueenBeeEnvironment(IVanillaQueenBeeEnvironment environment)
    {
        _queenBeeEnvironment = environment ?? throw new ArgumentNullException(nameof(environment));
        _queenBee.SetEnvironment(environment);
    }

    public void SetDeerclopsEnvironment(IVanillaDeerclopsEnvironment environment)
    {
        _deerclopsEnvironment = environment ?? throw new ArgumentNullException(nameof(environment));
        _deerclops.SetEnvironment(environment);
    }

    public void SetPlayerInteractions(RuntimeNpcPlayerInteractionLedger interactions) =>
        _playerInteractions = interactions ?? throw new ArgumentNullException(nameof(interactions));

    public void SetWallOfFleshEnvironment(IVanillaWallOfFleshEnvironment environment)
    {
        _wallOfFleshEnvironment = environment ?? throw new ArgumentNullException(nameof(environment));
        _wallOfFlesh.SetEnvironment(environment);
        _wallOfFleshEye.SetEnvironment(environment);
        _fireImp.SetEnvironment(environment);
    }

    public void SetWormEnvironment(IVanillaWormEnvironment environment)
    {
        _worm.SetEnvironment(environment);
        _destroyer.SetEnvironment(environment);
    }

    public void SetFlyingEyeEnvironment(IVanillaFlyingEyeEnvironment environment) =>
        _flyingEye.SetEnvironment(environment);

    public void SetProjectileEnvironment(IVanillaNpcProjectileEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _projectileEnvironment = environment;
        _flyer.SetProjectileEnvironment(environment);
        _queenBee.SetProjectileEnvironment(environment);
    }

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
            VanillaNpcBehaviorFamily.QueenBee => _queenBee,
            VanillaNpcBehaviorFamily.Deerclops => _deerclops,
            VanillaNpcBehaviorFamily.WallOfFlesh => _wallOfFlesh,
            VanillaNpcBehaviorFamily.WallOfFleshEye => _wallOfFleshEye,
            VanillaNpcBehaviorFamily.WallOfFleshHungry => _wallOfFleshHungry,
            VanillaNpcBehaviorFamily.FireImp => _fireImp,
            VanillaNpcBehaviorFamily.BurningSphere => _burningSphere,
            VanillaNpcBehaviorFamily.QueenSlime => _queenSlime,
            VanillaNpcBehaviorFamily.SkeletronPrime => _skeletronPrime,
            VanillaNpcBehaviorFamily.PrimeSaw => _skeletronPrimeLimb,
            VanillaNpcBehaviorFamily.PrimeVice => _skeletronPrimeLimb,
            VanillaNpcBehaviorFamily.PrimeCannon => _skeletronPrimeLimb,
            VanillaNpcBehaviorFamily.PrimeLaser => _skeletronPrimeLimb,
            VanillaNpcBehaviorFamily.Retinazer => _retinazer,
            VanillaNpcBehaviorFamily.Spazmatism => _spazmatism,
            VanillaNpcBehaviorFamily.Destroyer => _destroyer,
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

        if (source.Type == VanillaNpcIds.QueenBee.Value && proposed.Type == source.Type)
            return PlanQueenBeeMinion(in source, in proposed, destination);

        if (source.Type == VanillaNpcIds.WallOfFlesh.Value && proposed.Type == source.Type)
            return PlanWallOfFleshSpawns(in source, in proposed, destination);

        if (source.Type == VanillaNpcIds.FireImp.Value && proposed.Type == source.Type)
            return PlanFireImpSphere(in source, in proposed, destination);

        if (source.Type == VanillaNpcIds.SkeletronPrime.Value && proposed.Type == source.Type)
            return PlanSkeletronPrimeArms(in source, in proposed, destination);

        if (source.Type == VanillaNpcIds.QueenSlime.Value && proposed.Type == source.Type)
            return PlanQueenSlimeMinions(in source, in proposed, destination);

        if ((source.Type == VanillaNpcIds.Destroyer.Value || source.Type == VanillaNpcIds.DestroyerBody.Value) && proposed.Type == source.Type)
            return PlanDestroyerFollower(in source, in proposed, destination);

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
        Span<NpcAiProjectileIntent> destination)
    {
        if (source.Type == VanillaNpcIds.SkeletronHead.Value && proposed.Type == source.Type)
            return PlanSkeletronSkull(in source, in proposed, destination);
        if (source.Type == VanillaNpcIds.QueenBee.Value && proposed.Type == source.Type)
            return PlanQueenBeeStinger(in source, in proposed, destination);
        if (source.Type == VanillaNpcIds.Deerclops.Value && proposed.Type == source.Type)
            return PlanDeerclopsProjectiles(in source, in proposed, destination);
        if (source.Type == VanillaNpcIds.WallOfFleshEye.Value && proposed.Type == source.Type)
            return PlanWallOfFleshEyeLaser(in source, in proposed, destination);
        if (source.Type == VanillaNpcIds.QueenSlime.Value && proposed.Type == source.Type)
            return PlanQueenSlimeProjectiles(in source, in proposed, destination);
        if ((source.Type == VanillaNpcIds.PrimeCannon.Value || source.Type == VanillaNpcIds.PrimeLaser.Value) && proposed.Type == source.Type)
            return PlanPrimeLimbProjectile(in source, in proposed, destination);
        if ((source.Type == VanillaNpcIds.Retinazer.Value || source.Type == VanillaNpcIds.Spazmatism.Value) && proposed.Type == source.Type)
            return PlanTwinProjectile(in source, in proposed, destination);
        if (source.Type == VanillaNpcIds.DestroyerBody.Value && proposed.Type == source.Type)
            return PlanDestroyerLaser(in source, in proposed, destination);

        return _flyer.PlanProjectileSpawns(in source, in proposed, _context, destination);
    }

    private int PlanWallOfFleshSpawns(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiSpawnIntent> destination)
    {
        if (_wallOfFleshEnvironment is null ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.WallOfFlesh, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
            return 0;

        NpcAiState sourceLocal = source.Simulation.LocalAi;
        NpcAiState nextLocal = proposed.Simulation.LocalAi;
        if (sourceLocal.Ai0 == 0f && nextLocal.Ai0 == 2f)
        {
            const int initialChildren = 13;
            if (destination.Length < initialChildren)
                return destination.Length + 1;

            float centerY = proposed.PositionY + hitbox.Height * 0.5f;
            float top = nextLocal.Ai2;
            float bottom = nextLocal.Ai3;
            int index = 0;
            float upperEyeY = (centerY + top) * 0.5f;
            float lowerEyeY = (centerY + bottom) * 0.5f;
            destination[index++] = new NpcAiSpawnIntent(
                VanillaNpcIds.WallOfFleshEye, (int)proposed.PositionX, (int)upperEyeY, 0f, 0f, proposed.Target)
            {
                InitialAi = new NpcAiState(1f, 0f, 0f, source.Handle.Slot)
            };
            destination[index++] = new NpcAiSpawnIntent(
                VanillaNpcIds.WallOfFleshEye, (int)proposed.PositionX, (int)lowerEyeY, 0f, 0f, proposed.Target)
            {
                InitialAi = new NpcAiState(-1f, 0f, 0f, source.Handle.Slot)
            };
            for (int hungry = 0; hungry < 11; hungry++)
            {
                destination[index++] = new NpcAiSpawnIntent(
                    VanillaNpcIds.TheHungry, (int)proposed.PositionX, (int)lowerEyeY, 0f, 0f, proposed.Target)
                {
                    InitialAi = new NpcAiState(hungry * 0.1f - 0.05f, 0f, 0f, source.Handle.Slot)
                };
            }
            return index;
        }

        int count = 0;
        bool leechPulse = source.Ai.Ai2 > 0f && source.Ai.Ai1 <= 60f &&
                          proposed.Ai.Ai1 == 0f && proposed.Ai.Ai2 != source.Ai.Ai2;
        if (leechPulse && _context.CountNpcPeers(VanillaNpcIds.LeechHead) < 10 && count < destination.Length)
        {
            int direction = proposed.Simulation.DirectionX == 0 ? 1 : proposed.Simulation.DirectionX;
            destination[count++] = new NpcAiSpawnIntent(
                VanillaNpcIds.LeechHead,
                (int)(proposed.PositionX + hitbox.Width * 0.5f),
                (int)(proposed.PositionY + hitbox.Height * 0.5f + 20f),
                direction * 8f,
                0f,
                proposed.Target);
        }

        if (_context.GoodWorld && _random.NextInt32(0, 180) == 0 &&
            _context.CountNpcPeers(VanillaNpcIds.FireImp) < 4 && proposed.Target < byte.MaxValue &&
            _context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate target))
        {
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                int tileX = (int)(target.CenterX / 16f) + _random.NextInt32(-50, 51);
                int tileY = (int)(target.CenterY / 16f) + _random.NextInt32(-50, 51);
                if (!_wallOfFleshEnvironment.TryFindGroundSpawn(tileX, tileY, out int bottomX, out int bottomY))
                    continue;
                if (count >= destination.Length)
                    return destination.Length + 1;
                destination[count++] = new NpcAiSpawnIntent(VanillaNpcIds.FireImp, bottomX, bottomY, 0f, 0f, proposed.Target);
                break;
            }
        }

        if (_context.ExpertMode && CountAttachedHungry(source.Handle.Slot) < 10)
        {
            int lifeMax = Math.Max(1, proposed.Simulation.LifeMax);
            int chance = (int)(1f + proposed.Simulation.Life / (float)lifeMax * 10f);
            chance *= chance;
            if (chance < 400) chance = (chance * 19 + 400) / 20;
            if (chance < 60) chance = (chance * 3 + 60) / 4;
            if (chance < 20) chance = (chance + 20) / 2;
            chance = Math.Max(1, (int)(chance * 0.7));
            if (_random.NextInt32(0, chance) == 0)
            {
                Span<bool> used = stackalloc bool[10];
                int existing = MarkAttachedHungrySlots(source.Handle.Slot, used);
                int maxValue = 1 + existing * 2;
                if (existing < 10 && _random.NextInt32(0, maxValue) <= 1)
                {
                    int selected = -1;
                    for (int attempt = 0; attempt < 1000; attempt++)
                    {
                        int candidate = _random.NextInt32(0, 10);
                        if (!used[candidate]) { selected = candidate; break; }
                    }
                    if (selected >= 0)
                    {
                        if (count >= destination.Length)
                            return destination.Length + 1;
                        destination[count++] = new NpcAiSpawnIntent(
                            VanillaNpcIds.TheHungry,
                            (int)proposed.PositionX,
                            (int)(proposed.PositionY + hitbox.Height * 0.5f),
                            0f,
                            0f,
                            proposed.Target)
                        {
                            InitialAi = new NpcAiState(selected * 0.1f - 0.05f, 0f, 0f, source.Handle.Slot)
                        };
                    }
                }
            }
        }

        return count;
    }

    private int PlanFireImpSphere(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiSpawnIntent> destination)
    {
        if (destination.IsEmpty || source.Ai.Ai1 != 11f || proposed.Ai.Ai1 != 10f)
            return 0;
        int direction = proposed.Simulation.DirectionX == 0 ? 1 : proposed.Simulation.DirectionX;
        destination[0] = new NpcAiSpawnIntent(
            VanillaNpcIds.BurningSphere,
            (int)proposed.PositionX + 9 + direction * 8,
            (int)proposed.PositionY + 20,
            0f,
            0f,
            byte.MaxValue);
        return 1;
    }

    private int PlanWallOfFleshEyeLaser(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiProjectileIntent> destination)
    {
        if (destination.IsEmpty || proposed.Simulation.LocalAi.Ai0 != 1f || proposed.Target >= byte.MaxValue ||
            !_context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate target) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.WallOfFleshEye, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation.Scale, out VanillaNpcHitboxSize hitbox) ||
            !VanillaWallOfFleshEyeNpcBehaviorStrategy.TryResolveRoot(in source, _context, out NpcSnapshot root))
            return 0;

        int life = root.Simulation.Life;
        int lifeMax = Math.Max(1, root.Simulation.LifeMax);
        float speed = 9f;
        int damage = 11;
        if (life < lifeMax * .50f) { damage++; speed++; }
        if (life < lifeMax * .25f) { damage++; speed++; }
        if (life < lifeMax * .10f) { damage += 2; speed += 2f; }
        float centerX = proposed.PositionX + hitbox.Width * .5f;
        float centerY = proposed.PositionY + hitbox.Height * .5f;
        float dx = target.CenterX - centerX;
        float dy = target.CenterY - centerY;
        NormalizeTo(ref dx, ref dy, speed);
        destination[0] = new NpcAiProjectileIntent(
            VanillaProjectileIds.WallOfFleshEyeLaser,
            centerX + dx,
            centerY + dy,
            dx,
            dy,
            damage,
            0f)
        {
            TimeLeftOverride = 600
        };
        return 1;
    }

    private int CountAttachedHungry(byte rootSlot)
    {
        int count = 0;
        for (int slot = 0; slot < byte.MaxValue; slot++)
            if (_context.TryFindNpcPeer((byte)slot, out NpcSnapshot peer) && peer.TypeIdentity == VanillaNpcIds.TheHungry && (byte)peer.Ai.Ai3 == rootSlot)
                count++;
        return count;
    }

    private int MarkAttachedHungrySlots(byte rootSlot, Span<bool> used)
    {
        int count = 0;
        for (int slot = 0; slot < byte.MaxValue; slot++)
        {
            if (!_context.TryFindNpcPeer((byte)slot, out NpcSnapshot peer) || peer.TypeIdentity != VanillaNpcIds.TheHungry || (byte)peer.Ai.Ai3 != rootSlot)
                continue;
            int index = (int)MathF.Round((peer.Ai.Ai0 + 0.05f) * 10f);
            if ((uint)index < (uint)used.Length) used[index] = true;
            count++;
        }
        return count;
    }

    private int PlanSkeletronSkull(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiProjectileIntent> destination)
    {
        if (destination.IsEmpty || !_context.ExpertMode || _projectileEnvironment is null ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.SkeletronHead, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation.Scale, out VanillaNpcHitboxSize hitbox) ||
            source.Target >= byte.MaxValue ||
            !_context.TryFindCandidate((byte)source.Target, out VanillaNpcTargetCandidate target) ||
            !target.Active || target.Dead || target.Ghost)
        {
            return 0;
        }

        int handCount = _context.CountNpcPeers(VanillaNpcIds.SkeletronHand);
        if (handCount >= 2 && source.Simulation.Life >= source.Simulation.LifeMax * 0.75f)
            return 0;

        float cadence = handCount == 0 ? 40f : 80f;
        if (_context.GoodWorld)
            cadence *= 0.8f;
        if (source.Ai.Ai1 != 0f || source.Ai.Ai2 % cadence != 0f)
            return 0;

        float centerX = source.PositionX + hitbox.Width * 0.5f;
        float centerY = source.PositionY + hitbox.Height * 0.5f;
        float targetX = target.CenterX - VanillaPlayerHitboxFacts.BaseWidth * 0.5f;
        float targetY = target.CenterY - VanillaPlayerHitboxFacts.BaseHeight * 0.5f;
        if (!_projectileEnvironment.CanHit(
                centerX, centerY, 1, 1,
                targetX, targetY,
                (int)VanillaPlayerHitboxFacts.BaseWidth,
                (int)VanillaPlayerHitboxFacts.BaseHeight))
        {
            return 0;
        }

        float speed = handCount == 0 ? 5f : 3f;
        float dx = target.CenterX - centerX + _random.NextInt32(-20, 21);
        float dy = target.CenterY - centerY + _random.NextInt32(-20, 21);
        NormalizeTo(ref dx, ref dy, speed);
        dx += _random.NextInt32(-50, 51) * 0.01f;
        dy += _random.NextInt32(-50, 51) * 0.01f;
        NormalizeTo(ref dx, ref dy, speed);
        dx += source.VelocityX;
        dy += source.VelocityY;

        destination[0] = new NpcAiProjectileIntent(
            VanillaProjectileIds.SkeletronSkull,
            centerX + dx * 5f,
            centerY + dy * 5f,
            dx,
            dy,
            Damage: 17,
            KnockBack: 0f)
        {
            InitialAi = new ProjectileAiState(-1f, 0f, 0f),
            TimeLeftOverride = 300
        };
        return 1;
    }

    private int PlanQueenBeeMinion(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiSpawnIntent> destination)
    {
        if (destination.IsEmpty || source.Ai.Ai0 != 1f || proposed.Type != source.Type ||
            proposed.Ai.Ai2 <= source.Ai.Ai2 || _queenBeeEnvironment is null || _projectileEnvironment is null ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.QueenBee, out VanillaNpcDefinition definition))
        {
            return 0;
        }

        ushort targetSlot = proposed.Target;
        if (targetSlot >= byte.MaxValue || !_context.TryFindCandidate((byte)targetSlot, out VanillaNpcTargetCandidate target) ||
            !target.Active || target.Dead || target.Ghost)
            return 0;

        float centerX = source.PositionX + definition.Width * 0.5f;
        float centerY = source.PositionY + definition.Height * 0.5f;
        int direction = target.CenterX < centerX ? -1 : 1;
        float spawnX = source.PositionX + definition.Width * 0.5f + _random.NextInt32(0, 20) * direction;
        float spawnY = source.PositionY + definition.Height * 0.8f;
        if (!_projectileEnvironment.CanHit(
                spawnX, spawnY, 1, 1,
                target.CenterX - VanillaPlayerHitboxFacts.BaseWidth * 0.5f,
                target.CenterY - VanillaPlayerHitboxFacts.BaseHeight * 0.5f,
                (int)VanillaPlayerHitboxFacts.BaseWidth,
                (int)VanillaPlayerHitboxFacts.BaseHeight))
            return 0;

        NpcTypeId child = _random.NextInt32(VanillaNpcIds.Bee.Value, VanillaNpcIds.SmallBee.Value + 1) == VanillaNpcIds.Bee.Value
            ? VanillaNpcIds.Bee
            : VanillaNpcIds.SmallBee;
        float vx = target.CenterX - centerX;
        float vy = target.CenterY - centerY;
        NormalizeTo(ref vx, ref vy, 5f);
        destination[0] = new NpcAiSpawnIntent(
            child,
            BottomX: (int)spawnX,
            BottomY: (int)spawnY,
            VelocityX: vx,
            VelocityY: vy,
            Target: proposed.Target)
        {
            InitialLocalAi = new NpcAiState(60f, 0f, 0f, 0f)
        };
        return 1;
    }

    private int PlanQueenBeeStinger(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiProjectileIntent> destination)
    {
        if (destination.IsEmpty || source.Ai.Ai0 != 3f || proposed.Type != source.Type ||
            _queenBeeEnvironment is null || _projectileEnvironment is null ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.QueenBee, out VanillaNpcDefinition definition) ||
            proposed.Target >= byte.MaxValue ||
            !_context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate target) ||
            !target.Active || target.Dead || target.Ghost)
            return 0;

        float enrage = VanillaQueenBeeNpcBehaviorStrategy.ComputeEnrage(in source, in target, _context, _queenBeeEnvironment);
        int cadence = VanillaQueenBeeNpcBehaviorStrategy.GetStingerCadence(in source, _context.ExpertMode, enrage);
        int timer = (int)(source.Ai.Ai1 + 1f);
        if (timer % cadence != cadence - 1)
            return 0;

        float targetTop = target.CenterY - VanillaPlayerHitboxFacts.BaseHeight * 0.5f;
        if (source.PositionY + definition.Height >= targetTop)
            return 0;

        float centerX = source.PositionX + definition.Width * 0.5f;
        int direction = target.CenterX < centerX ? -1 : 1;
        float spawnX = source.PositionX + definition.Width * 0.5f + _random.NextInt32(0, 20) * direction;
        float spawnY = source.PositionY + definition.Height * 0.8f;
        if (!_projectileEnvironment.CanHit(
                spawnX, spawnY, 1, 1,
                target.CenterX - VanillaPlayerHitboxFacts.BaseWidth * 0.5f,
                targetTop,
                (int)VanillaPlayerHitboxFacts.BaseWidth,
                (int)VanillaPlayerHitboxFacts.BaseHeight))
            return 0;

        float speed = 8f;
        if (_context.ExpertMode)
        {
            speed += 2f;
            if (source.Simulation.LifeMax > 0 && (double)source.Simulation.Life < source.Simulation.LifeMax * 0.1)
                speed += 3f;
        }
        speed += 7f * enrage;
        int jitterX = Math.Max(1, (int)(80f - 39f * enrage));
        int jitterY = Math.Max(1, (int)(40f - 19f * enrage));
        float vx = target.CenterX - spawnX + _random.NextInt32(-jitterX, jitterX + 1);
        float vy = target.CenterY - spawnY + _random.NextInt32(-jitterY, jitterY + 1);
        NormalizeTo(ref vx, ref vy, speed);
        destination[0] = new NpcAiProjectileIntent(
            VanillaProjectileIds.QueenBeeStinger,
            spawnX,
            spawnY,
            vx,
            vy,
            Damage: 11,
            KnockBack: 0f)
        {
            TimeLeftOverride = 300
        };
        return 1;
    }


    private static void NormalizeTo(ref float x, ref float y, float speed)
    {
        float length = MathF.Sqrt(x * x + y * y);
        if (length <= 0f)
        {
            x = 0f;
            y = speed;
            return;
        }
        x = x / length * speed;
        y = y / length * speed;
    }

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

    private int PlanDeerclopsProjectiles(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiProjectileIntent> destination)
    {
        if (destination.IsEmpty ||
            _deerclopsEnvironment is null ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Deerclops, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            return 0;
        }

        int written = PlanDeerclopsPassiveShadowHands(in source, destination);
        if (written >= destination.Length)
            return written;

        Span<NpcAiProjectileIntent> attackDestination = destination[written..];
        int state = (int)source.Ai.Ai0;
        int timer = (int)proposed.Ai.Ai1;
        int attackCount = 0;
        if (state == 1 && proposed.Ai.Ai0 == 1f)
            attackCount = PlanDeerclopsForwardSpikes(in source, in proposed, in hitbox, timer, attackDestination);
        else if (state == 4 && proposed.Ai.Ai0 == 4f)
            attackCount = PlanDeerclopsBothSideSpikes(in source, in proposed, in hitbox, timer, attackDestination);
        else if (state == 2 && proposed.Ai.Ai0 == 2f)
            attackCount = PlanDeerclopsRubble(in source, in proposed, in hitbox, timer, attackDestination);
        else if (state == 5 && proposed.Ai.Ai0 == 5f && timer == 30)
            attackCount = PlanDeerclopsShadowHands(in source, in proposed, attackDestination);
        return written + attackCount;
    }

    private int PlanDeerclopsPassiveShadowHands(
        in NpcSnapshot source,
        Span<NpcAiProjectileIntent> destination)
    {
        if (!_context.ExpertMode || _playerInteractions is null || source.Simulation.LifeMax <= 0)
            return 0;

        int interval = VanillaDeerclopsNpcBehaviorStrategy.ResolvePassiveShadowHandInterval(
            source.Simulation.Life,
            source.Simulation.LifeMax);
        int counter = Math.Max(0, (int)source.Simulation.LocalAi.Ai2) + 1;
        if (counter % interval != 0)
            return 0;

        int rotationIndex = counter / interval % 3;
        if (!VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Deerclops, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            return 0;
        }

        float centerX = source.PositionX + hitbox.Width * 0.5f;
        float centerY = source.PositionY + hitbox.Height * 0.5f;
        int written = 0;
        for (int index = 0; index < _context.CandidateCount && written < destination.Length; index++)
        {
            VanillaNpcTargetCandidate candidate = _context.GetCandidateAt(index);
            if (candidate.Slot % 3 != rotationIndex ||
                !candidate.Active || candidate.Dead || candidate.Ghost ||
                !_playerInteractions.HasInteraction(source.Handle, new PlayerSlotId(candidate.Slot)))
            {
                continue;
            }

            float dx = candidate.CenterX - centerX;
            float dy = candidate.CenterY - centerY;
            if (dx * dx + dy * dy > 1200f * 1200f)
                continue;

            if (TryCreateShadowHandIntent(in candidate, Damage: 10, out NpcAiProjectileIntent intent))
                destination[written++] = intent;
        }
        return written;
    }

    private int PlanDeerclopsForwardSpikes(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        in VanillaNpcHitboxSize hitbox,
        int timer,
        Span<NpcAiProjectileIntent> destination)
    {
        const int windup = 36;
        const int spikeCount = 20;
        const int batchSize = 4;
        if (timer < windup || (timer - windup) % batchSize != 0)
            return 0;

        int first = timer - windup;
        if (first >= spikeCount)
            return 0;

        int sourceX = (int)MathF.Floor((source.PositionX + hitbox.Width * 0.5f) / 16f);
        int sourceY = (int)MathF.Floor((source.PositionY + hitbox.Height) / 16f);
        int direction = source.Simulation.DirectionX < 0 ? -1 : 1;
        sourceX += direction * 3;
        TryResolveDeerclopsTarget(proposed.Target, out VanillaNpcTargetCandidate target);
        int written = 0;
        for (int i = first; i < Math.Min(first + batchSize, spikeCount) && written < destination.Length; i++)
        {
            if (TryCreateDeerclopsSpikeIntent(sourceX, sourceY, in target, direction, spikeCount, i, i, out NpcAiProjectileIntent intent))
                destination[written++] = intent;
        }
        return written;
    }

    private int PlanDeerclopsBothSideSpikes(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        in VanillaNpcHitboxSize hitbox,
        int timer,
        Span<NpcAiProjectileIntent> destination)
    {
        const int windup = 56;
        const int spikeCount = 15;
        const int batchSize = 2;
        if (timer < windup || (timer - windup) % batchSize != 0)
            return 0;

        int first = timer - windup;
        if (first >= spikeCount)
            return 0;

        int sourceX = (int)MathF.Floor((source.PositionX + hitbox.Width * 0.5f) / 16f);
        int sourceY = (int)MathF.Floor((source.PositionY + hitbox.Height) / 16f);
        int direction = source.Simulation.DirectionX < 0 ? -1 : 1;
        TryResolveDeerclopsTarget(proposed.Target, out VanillaNpcTargetCandidate target);
        int written = 0;
        for (int i = first; i < Math.Min(first + batchSize, spikeCount) && written < destination.Length; i++)
        {
            if (TryCreateDeerclopsSpikeIntent(sourceX, sourceY, in target, direction, spikeCount, -i, i, out NpcAiProjectileIntent right))
                destination[written++] = right;
            if (written < destination.Length &&
                TryCreateDeerclopsSpikeIntent(sourceX, sourceY, in target, -direction, spikeCount, -i, i, out NpcAiProjectileIntent left))
            {
                destination[written++] = left;
            }
        }
        return written;
    }

    private bool TryCreateDeerclopsSpikeIntent(
        int sourceTileX,
        int sourceTileY,
        in VanillaNpcTargetCandidate target,
        int direction,
        int totalSpikes,
        int angleIndex,
        int xOffset,
        out NpcAiProjectileIntent intent)
    {
        intent = default;
        if (_deerclopsEnvironment is null)
            return false;

        int tileX = sourceTileX + xOffset * direction;
        int tileY = FindDeerclopsSpikeY(sourceTileY, tileX, in target);
        if (!_deerclopsEnvironment.IsWalkableTile(tileX, tileY))
            return false;

        float angle = angleIndex * direction * 0.7f * (MathF.PI / 4f / totalSpikes);
        float velocityX = MathF.Sin(angle);
        float velocityY = -MathF.Cos(angle);
        float randomScale = NextUnitFloat() * 0.1f;
        float scale = 0.1f + randomScale + xOffset * 1.1f / totalSpikes;
        intent = new NpcAiProjectileIntent(
            VanillaProjectileIds.DeerclopsIceSpike,
            tileX * 16f + 8f,
            tileY * 16f - 8f,
            velocityX,
            velocityY,
            Damage: 13,
            KnockBack: 0f)
        {
            InitialAi = new ProjectileAiState(0f, scale, 0f)
        };
        return true;
    }

    private int FindDeerclopsSpikeY(int sourceTileY, int tileX, in VanillaNpcTargetCandidate target)
    {
        if (_deerclopsEnvironment is null)
            return sourceTileY;

        int result = sourceTileY;
        if (target.Active && !target.Dead && !target.Ghost)
        {
            int targetBottomY = (int)((target.CenterY + VanillaPlayerHitboxFacts.BaseHeight * 0.5f) / 16f);
            int direction = Math.Sign(targetBottomY - result);
            if (direction != 0)
            {
                int endExclusive = targetBottomY + direction * 15;
                int? best = null;
                float bestDistance = float.PositiveInfinity;
                for (int y = result; y != endExclusive; y += direction)
                {
                    if (!_deerclopsEnvironment.IsWalkableTile(tileX, y))
                        continue;
                    float dx = tileX * 16f + 8f - target.CenterX;
                    float dy = y * 16f + 8f - (target.CenterY + VanillaPlayerHitboxFacts.BaseHeight * 0.5f);
                    float distance = dx * dx + dy * dy;
                    if (!best.HasValue || distance < bestDistance)
                    {
                        best = y;
                        bestDistance = distance;
                    }
                }
                if (best.HasValue)
                    result = best.Value;
            }
        }

        for (int i = 0; i < 20 && result >= 10 && _deerclopsEnvironment.IsSolidTile(tileX, result); i++)
            result--;
        for (int i = 0; i < 20 && result <= _deerclopsEnvironment.WorldHeightTiles - 10 && !_deerclopsEnvironment.IsWalkableTile(tileX, result); i++)
            result++;
        return result;
    }

    private int PlanDeerclopsRubble(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        in VanillaNpcHitboxSize hitbox,
        int timer,
        Span<NpcAiProjectileIntent> destination)
    {
        if (_deerclopsEnvironment is null || timer < 32 || timer >= 52 || destination.IsEmpty)
            return 0;
        int index = timer - 32;
        int direction = source.Simulation.DirectionX < 0 ? -1 : 1;
        int sourceX = (int)MathF.Floor((source.PositionX + hitbox.Width * 0.5f) / 16f) + direction * 3;
        int sourceY = (int)MathF.Floor(source.PositionY / 16f) - 10;
        int tileX = sourceX + index * direction;
        for (int down = 0; down < 35; down++)
        {
            int tileY = sourceY + down;
            if (!_deerclopsEnvironment.IsSolidTile(tileX, tileY))
                continue;

            float angle = index * direction * 0.7f * (MathF.PI / 4f / 20f);
            float speed = 8f + NextUnitFloat() * 8f;
            int frame = 6 + _random.NextInt32(0, 6);
            destination[0] = new NpcAiProjectileIntent(
                VanillaProjectileIds.DeerclopsRubble,
                tileX * 16f + 8f,
                tileY * 16f - 8f,
                MathF.Sin(angle) * speed,
                -MathF.Cos(angle) * speed,
                Damage: 18,
                KnockBack: 0f)
            {
                InitialAi = new ProjectileAiState(0f, frame, 0f),
                TimeLeftOverride = 220
            };
            return 1;
        }
        return 0;
    }

    private int PlanDeerclopsShadowHands(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiProjectileIntent> destination)
    {
        if (proposed.Target >= byte.MaxValue ||
            !_context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate target) ||
            !target.Active || target.Dead || target.Ghost)
        {
            return 0;
        }

        int written = 0;
        while (written < 6 && written < destination.Length)
        {
            if (!TryCreateShadowHandIntent(in target, Damage: 15, out NpcAiProjectileIntent intent))
                break;
            destination[written++] = intent;
        }
        return written;
    }

    private bool TryCreateShadowHandIntent(
        in VanillaNpcTargetCandidate target,
        int Damage,
        out NpcAiProjectileIntent intent)
    {
        const float radius = 200f;
        int side = _random.NextInt32(0, 2) * 2 - 1;
        int variation = _random.NextInt32(0, 4);
        if (target.VelocityX * side > 0f)
            side *= -1;

        float travelTicks = 30f;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            if (variation == 0)
                travelTicks += 10f;
            float spawnX;
            float spawnY;
            float velocityX;
            float velocityY;
            float ai0;
            float ai1;
            switch (variation)
            {
                case 1:
                {
                    float angle = NextUnitFloat() * MathF.PI * 2f;
                    float cos = MathF.Cos(angle);
                    float sin = MathF.Sin(angle);
                    spawnX = target.CenterX - cos * radius;
                    spawnY = target.CenterY - sin * radius;
                    velocityX = cos * 4f;
                    velocityY = sin * 4f;
                    ai0 = 180f;
                    ai1 = angle - MathF.PI * 0.5f;
                    break;
                }
                case 2:
                {
                    float angle = NextUnitFloat() * MathF.PI * 2f;
                    float cos = MathF.Cos(angle);
                    float sin = MathF.Sin(angle);
                    spawnX = target.CenterX - cos * radius;
                    spawnY = target.CenterY - sin * radius;
                    velocityX = cos * 4f;
                    velocityY = sin * 4f;
                    ai0 = 300f;
                    ai1 = angle;
                    break;
                }
                case 3:
                {
                    const int leadTicks = 60;
                    float angle = NextUnitFloat() * MathF.PI * 2f;
                    float curve = MathF.PI * 0.5f / leadTicks * NextFloatDirection();
                    spawnX = target.CenterX + target.VelocityX * leadTicks;
                    spawnY = target.CenterY + target.VelocityY * leadTicks;
                    velocityX = MathF.Cos(angle) * 8f;
                    velocityY = MathF.Sin(angle) * 8f;
                    for (int tick = 0; tick < leadTicks; tick++)
                    {
                        spawnX -= velocityX;
                        spawnY -= velocityY;
                        Rotate(ref velocityX, ref velocityY, -curve);
                    }
                    ai0 = 390f;
                    ai1 = curve;
                    break;
                }
                default:
                {
                    float jitter = NextFloatDirection() * MathF.PI * 0.125f;
                    float cos = MathF.Cos(jitter);
                    float sin = MathF.Sin(jitter);
                    float offsetX = -side * radius;
                    spawnX = target.CenterX + target.VelocityX * 30f + offsetX * cos;
                    spawnY = target.CenterY + target.VelocityY * 30f + offsetX * sin;
                    float speed = side * radius / (travelTicks + 10f);
                    velocityX = speed * cos;
                    velocityY = speed * sin;
                    ai0 = 0f;
                    ai1 = 0f;
                    break;
                }
            }

            if (!_context.ShadowSpawnIntersectsOtherPlayer(target.Slot, spawnX, spawnY, 50f))
            {
                intent = new NpcAiProjectileIntent(
                    VanillaProjectileIds.DeerclopsShadowHand,
                    spawnX,
                    spawnY,
                    velocityX,
                    velocityY,
                    Damage,
                    KnockBack: 0f)
                {
                    InitialAi = new ProjectileAiState(ai0, ai1, 0f),
                    TimeLeftOverride = 300
                };
                return true;
            }
            variation = (variation + 1) % 4;
        }

        intent = default;
        return false;
    }

    private bool TryResolveDeerclopsTarget(ushort targetSlot, out VanillaNpcTargetCandidate target)
    {
        if (targetSlot < byte.MaxValue &&
            _context.TryFindCandidate((byte)targetSlot, out target) &&
            target.Active && !target.Dead && !target.Ghost)
        {
            return true;
        }

        target = default;
        return false;
    }

    private float NextUnitFloat() => _random.NextInt32(0, 1 << 20) / (float)(1 << 20);

    private float NextFloatDirection() => NextUnitFloat() * 2f - 1f;

    private static void Rotate(ref float x, ref float y, float radians)
    {
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        float nextX = x * cos - y * sin;
        y = x * sin + y * cos;
        x = nextX;
    }

    private int PlanSkeletronPrimeArms(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiSpawnIntent> destination)
    {
        if (source.Ai.Ai0 != 0f || proposed.Ai.Ai0 == 0f)
            return 0;
        if (destination.Length < 4)
            return destination.Length + 1;
        int x = (int)(proposed.PositionX + 40f);
        int y = (int)(proposed.PositionY + 51f);
        byte parent = source.Handle.Slot;
        destination[0] = new NpcAiSpawnIntent(VanillaNpcIds.PrimeCannon, x, y, 0f, 0f, proposed.Target) { InitialAi = new NpcAiState(-1f, parent, 0f, 0f) };
        destination[1] = new NpcAiSpawnIntent(VanillaNpcIds.PrimeSaw, x, y, 0f, 0f, proposed.Target) { InitialAi = new NpcAiState(1f, parent, 0f, 0f) };
        destination[2] = new NpcAiSpawnIntent(VanillaNpcIds.PrimeVice, x, y, 0f, 0f, proposed.Target) { InitialAi = new NpcAiState(-1f, parent, 0f, 150f) };
        destination[3] = new NpcAiSpawnIntent(VanillaNpcIds.PrimeLaser, x, y, 0f, 0f, proposed.Target) { InitialAi = new NpcAiState(1f, parent, 0f, 150f) };
        return 4;
    }

    private int PlanQueenSlimeMinions(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiSpawnIntent> destination)
    {
        int lifeMax = Math.Max(1, proposed.Simulation.LifeMax);
        int life = proposed.Simulation.Life;
        bool phaseTwo = life <= lifeMax / 2;
        float threshold = lifeMax * (phaseTwo ? 0.015f : 0.02f);
        float previousAnchor = source.Simulation.LocalAi.Ai0;
        float nextAnchor = proposed.Simulation.LocalAi.Ai0;
        if (previousAnchor <= 0f || life + threshold >= previousAnchor || nextAnchor == previousAnchor)
            return 0;
        int count = _random.NextInt32(1, 3);
        if (destination.Length < count)
            return destination.Length + 1;
        NpcTypeId[] types = [VanillaNpcIds.QueenSlimeMinionBlue, VanillaNpcIds.QueenSlimeMinionPink, VanillaNpcIds.QueenSlimeMinionPurple];
        for (int i = 0; i < count; i++)
        {
            NpcTypeId type = types[_random.NextInt32(0, types.Length)];
            float vx = _random.NextInt32(-20, 21) * 0.1f;
            float vy = _random.NextInt32(-20, 1) * 0.1f;
            destination[i] = new NpcAiSpawnIntent(type, (int)(proposed.PositionX + 57f), (int)(proposed.PositionY + 100f), vx, vy, proposed.Target)
            { InitialAi = new NpcAiState(-500f * _random.NextInt32(0, 3), 0f, 0f, 0f) };
        }
        return count;
    }

    private int PlanQueenSlimeProjectiles(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiProjectileIntent> destination)
    {
        // Slam projectile is emitted on the landing transition from state 4/substate 1 back to idle.
        if (source.Ai.Ai0 == 4f && source.Ai.Ai2 == 1f && proposed.Ai.Ai0 == 0f && proposed.Ai.Ai2 == 0f)
        {
            if (destination.IsEmpty) return 1;
            destination[0] = new NpcAiProjectileIntent(VanillaProjectileIds.QueenSlimeSmash, proposed.PositionX + 57f, proposed.PositionY + 100f, 0f, 0f, 40, 0f);
            return 1;
        }
        // Radial gel burst occurs on the substate-1 timer crossing 10 and resets the attack to idle.
        if (source.Ai.Ai0 == 5f && source.Ai.Ai2 == 1f && source.Ai.Ai1 < 10f && proposed.Ai.Ai0 == 0f)
        {
            int baseCount = _context.GoodWorld ? 15 : 10;
            int lifeMax = Math.Max(1, proposed.Simulation.LifeMax);
            bool phaseTwo = proposed.Simulation.Life <= lifeMax / 2;
            int count = phaseTwo ? baseCount : 6;
            if (destination.Length < count) return destination.Length + 1;
            float cx = proposed.PositionX + 57f, cy = proposed.PositionY + 50f;
            for (int i = 0; i < count; i++)
            {
                float angle = -i * (MathF.PI * 2f) / baseCount;
                destination[i] = new NpcAiProjectileIntent(VanillaProjectileIds.QueenSlimeGelAttack, cx, cy, MathF.Cos(angle) * 9f, MathF.Sin(angle) * 9f, 30, 0f);
            }
            return count;
        }
        return 0;
    }

    private int PlanPrimeLimbProjectile(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiProjectileIntent> destination)
    {
        if (destination.IsEmpty) return 0;
        float previous = source.Simulation.LocalAi.Ai0;
        float current = proposed.Simulation.LocalAi.Ai0;
        float threshold = source.Type == VanillaNpcIds.PrimeCannon.Value
            ? (source.Ai.Ai2 == 1f ? 40f : 140f)
            : (source.Ai.Ai2 == 1f ? 80f : 200f);
        if (!(previous <= threshold && current > threshold) || proposed.Target >= byte.MaxValue ||
            !_context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate target) || target.Dead || !target.Active)
            return 0;
        float cx = proposed.PositionX + 26f, cy = proposed.PositionY + 26f;
        float dx, dy, speed;
        ProjectileTypeId type;
        int damage;
        if (source.Type == VanillaNpcIds.PrimeCannon.Value)
        {
            type = VanillaProjectileIds.SkeletronPrimeBomb; damage = 0;
            if (source.Ai.Ai2 == 1f) { dx = target.CenterX - cx; dy = target.CenterY - cy; speed = 10f; }
            else { dx = cx - (target.CenterX); dy = cy - target.CenterY; speed = 12f; }
        }
        else
        {
            type = VanillaProjectileIds.RetinazerDeathLaser; damage = 25;
            dx = target.CenterX - cx; dy = target.CenterY - cy; speed = source.Ai.Ai2 == 1f ? 10f : 8f;
        }
        float d = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy));
        float vx = dx / d * speed + _random.NextInt32(-40, 41) * (source.Type == VanillaNpcIds.PrimeCannon.Value ? 0.01f : 0.05f);
        float vy = dy / d * speed + _random.NextInt32(-40, 41) * (source.Type == VanillaNpcIds.PrimeCannon.Value ? 0.01f : 0.05f);
        destination[0] = new NpcAiProjectileIntent(type, cx + vx * (source.Type == VanillaNpcIds.PrimeCannon.Value ? 4f : 8f), cy + vy * (source.Type == VanillaNpcIds.PrimeCannon.Value ? 4f : 8f), vx, vy, damage, 0f);
        return 1;
    }

    private int PlanTwinProjectile(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiProjectileIntent> destination)
    {
        if (destination.IsEmpty || proposed.Target >= byte.MaxValue || !_context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate target) || !target.Active || target.Dead)
            return 0;
        bool spaz = source.Type == VanillaNpcIds.Spazmatism.Value;
        ProjectileTypeId type;
        float speed;
        int damage;
        bool fire = false;
        if (source.Ai.Ai0 == 0f && source.Ai.Ai1 == 0f && proposed.Ai.Ai0 == 0f && proposed.Ai.Ai1 == 0f && source.Ai.Ai3 > 0f && proposed.Ai.Ai3 == 0f)
        {
            type = spaz ? VanillaProjectileIds.SpazmatismCursedFlame : VanillaProjectileIds.WallOfFleshEyeLaser;
            speed = spaz ? (_context.ExpertMode ? 14f : 12f) : (_context.ExpertMode ? 10.5f : 9f);
            damage = spaz ? 25 : 20;
            fire = true;
        }
        else if (source.Ai.Ai0 >= 3f && proposed.Ai.Ai0 >= 3f && source.Simulation.LocalAi.Ai1 > 0f && proposed.Simulation.LocalAi.Ai1 == 0f)
        {
            type = spaz ? VanillaProjectileIds.SpazmatismEyeFire : VanillaProjectileIds.RetinazerDeathLaser;
            if (spaz) { speed = 6f; damage = 30; }
            else { speed = source.Ai.Ai1 == 0f ? (_context.ExpertMode ? 10f : 8.5f) : 9f; damage = source.Ai.Ai1 == 0f ? 25 : 18; }
            fire = true;
        }
        else return 0;
        if (!fire) return 0;
        float cx = proposed.PositionX + 50f, cy = proposed.PositionY + 55f;
        float dx = target.CenterX - cx, dy = target.CenterY - cy;
        float d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
        float jitter = type == VanillaProjectileIds.WallOfFleshEyeLaser ? .08f : type == VanillaProjectileIds.SpazmatismEyeFire ? .01f : .05f;
        float vx = dx/d*speed + _random.NextInt32(-40,41)*jitter;
        float vy = dy/d*speed + _random.NextInt32(-40,41)*jitter;
        if (spaz && type == VanillaProjectileIds.SpazmatismEyeFire) { vx += proposed.VelocityX*.5f; vy += proposed.VelocityY*.5f; }
        float lead = type == VanillaProjectileIds.WallOfFleshEyeLaser || type == VanillaProjectileIds.RetinazerDeathLaser ? 15f : type == VanillaProjectileIds.SpazmatismCursedFlame ? 4f : -1f;
        destination[0] = new NpcAiProjectileIntent(type, cx + vx*lead, cy + vy*lead, vx, vy, damage, 0f);
        return 1;
    }

    private int PlanDestroyerFollower(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiSpawnIntent> destination)
    {
        if (destination.IsEmpty || source.Ai.Ai0 != 0f || proposed.Ai.Ai0 != 0f)
            return 0;
        int remaining;
        float root;
        if (source.Type == VanillaNpcIds.Destroyer.Value)
        {
            remaining = (_context.GoodWorld ? 100 : 80) - 1;
            root = source.Handle.Slot;
        }
        else
        {
            if (!float.IsFinite(source.Ai.Ai2) || source.Ai.Ai2 < 0f || source.Ai.Ai2 != MathF.Truncate(source.Ai.Ai2)) return 0;
            remaining = (int)source.Ai.Ai2 - 1;
            root = source.Ai.Ai3;
        }
        NpcTypeId child = remaining >= 0 ? VanillaNpcIds.DestroyerBody : VanillaNpcIds.DestroyerTail;
        if (!VanillaNpcDefinitionCatalog.TryGet(NpcTypeId.TryCreate(source.Type, out var st) ? st : VanillaNpcIds.Destroyer, out VanillaNpcDefinition def) ||
            !def.TryResolveHitbox(proposed.Simulation.Scale, out VanillaNpcHitboxSize hb)) return 0;
        destination[0] = new NpcAiSpawnIntent(child, (int)(proposed.PositionX + hb.Width*.5f), (int)(proposed.PositionY + hb.Height), 0f, 0f, proposed.Target)
        {
            InitialAi = new NpcAiState(0f, source.Handle.Slot, Math.Max(remaining,0), root),
            LinkSourceFollowerSlot = true
        };
        return 1;
    }

    private int PlanDestroyerLaser(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiProjectileIntent> destination)
    {
        if (destination.IsEmpty || source.Simulation.LocalAi.Ai0 <= 0f || proposed.Simulation.LocalAi.Ai0 != 0f ||
            proposed.Target >= byte.MaxValue || !_context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate target) || !target.Active || target.Dead)
            return 0;
        float cx=proposed.PositionX+23.75f,cy=proposed.PositionY+23.75f;
        float dx=target.CenterX-cx+_random.NextInt32(-20,21),dy=target.CenterY-cy+_random.NextInt32(-20,21);
        float d=MathF.Max(.001f,MathF.Sqrt(dx*dx+dy*dy));
        float vx=dx/d*8f+_random.NextInt32(-20,21)*.05f,vy=dy/d*8f+_random.NextInt32(-20,21)*.05f;
        destination[0]=new NpcAiProjectileIntent(VanillaProjectileIds.RetinazerDeathLaser,cx+vx*5f,cy+vy*5f,vx,vy,22,0f){TimeLeftOverride=300};
        return 1;
    }

}
