using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

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
    INpcAiProjectileMutationIntentPlanner,
    INpcAiPeerSnapshotConsumer,
    INpcAiRetainedSlotSnapshotConsumer,
    INpcAiForcedUpdateIntentPlanner,
    INpcAiStatePostCommitEffect
{
    public const int MaximumPlayerCandidates = VanillaNpcBehaviorContext.MaximumPlayerCandidates;

    public bool RequiresForcedUpdate(in NpcSnapshot before, in NpcStateUpdate proposed) =>
        VanillaDestroyerNpcBehaviorStrategy.RequiresDiggingStateSync(in before, in proposed) ||
        VanillaDukeFishronNpcBehaviorStrategy.RequiresImmediateSync(in before, in proposed, _context);

    public bool RequiresForcedUpdateAfterPlanning(
        in NpcSnapshot before,
        in NpcStateUpdate proposed,
        ReadOnlySpan<NpcAiProjectileIntent> plannedProjectiles) =>
        RequiresForcedUpdate(in before, in proposed) ||
        (before.TypeIdentity == VanillaNpcIds.DestroyerBody &&
         proposed.Type == before.Type &&
         plannedProjectiles.Length == 1 &&
         plannedProjectiles[0].Type == VanillaProjectileIds.RetinazerDeathLaser);

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
    private readonly VanillaBatNpcBehaviorStrategy _bat = new();
    private readonly VanillaFishNpcBehaviorStrategy _fish;
    private readonly VanillaSkeletronHeadNpcBehaviorStrategy _skeletronHead = new();
    private readonly VanillaSkeletronHandNpcBehaviorStrategy _skeletronHand = new();
    private readonly VanillaQueenBeeNpcBehaviorStrategy _queenBee;
    private readonly VanillaDeerclopsNpcBehaviorStrategy _deerclops = new();
    private readonly VanillaWallOfFleshNpcBehaviorStrategy _wallOfFlesh = new();
    private readonly VanillaWallOfFleshEyeNpcBehaviorStrategy _wallOfFleshEye = new();
    private readonly VanillaWallOfFleshHungryNpcBehaviorStrategy _wallOfFleshHungry = new();
    private readonly VanillaFireImpNpcBehaviorStrategy _fireImp = new();
    private readonly VanillaDarkCasterBehavior _darkCaster = new();
    private readonly VanillaSphereNpcBehaviorStrategy _burningSphere = new();
    private readonly VanillaQueenSlimeNpcBehaviorStrategy _queenSlime;
    private readonly VanillaSkeletronPrimeNpcBehaviorStrategy _skeletronPrime = new();
    private readonly VanillaSkeletronPrimeLimbNpcBehaviorStrategy _skeletronPrimeLimb = new();
    private readonly VanillaPrimeRangedBehavior _primeRanged = new();
    private readonly VanillaTwinNpcBehaviorStrategy _retinazer = new(false);
    private readonly VanillaTwinNpcBehaviorStrategy _spazmatism = new(true);
    private readonly VanillaDestroyerNpcBehaviorStrategy _destroyer;
    private readonly VanillaPlanteraNpcBehaviorStrategy _plantera = new();
    private readonly VanillaGolemNpcBehaviorStrategy _golem = new();
    private readonly VanillaDukeFishronNpcBehaviorStrategy _dukeFishron;
    private readonly VanillaLunaticCultistNpcBehaviorStrategy _lunaticCultist = new();
    private readonly VanillaEmpressOfLightNpcBehaviorStrategy _empressOfLight = new();
    private readonly VanillaMoonLordNpcBehaviorStrategy _moonLord;
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
        _fish = new VanillaFishNpcBehaviorStrategy(_random);
        _queenBee = new VanillaQueenBeeNpcBehaviorStrategy(_random);
        _queenSlime = new VanillaQueenSlimeNpcBehaviorStrategy(_random, kingSlimeEnvironment);
        _destroyer = new VanillaDestroyerNpcBehaviorStrategy(_random);
        _dukeFishron = new VanillaDukeFishronNpcBehaviorStrategy(_random);
        _moonLord = new VanillaMoonLordNpcBehaviorStrategy(_random);
        if (kingSlimeEnvironment is IVanillaEyeOfCthulhuEnvironment eyeEnvironment)
            _eyeOfCthulhu.SetEnvironment(eyeEnvironment);
        if (kingSlimeEnvironment is IVanillaBrainOfCthulhuEnvironment brainEnvironment)
            _brainOfCthulhu.SetEnvironment(brainEnvironment);
    }

    public void EnableBlueSlimeMotion(double worldSurfaceTiles = double.PositiveInfinity) =>
        _context.EnableSlimeGround(worldSurfaceTiles);

    public void EnableZombieMotion(double worldSurfaceTiles) =>
        _context.EnableGroundFighter(worldSurfaceTiles);

    public void SetSkeletronEnvironment(IVanillaSkeletronEnvironment environment) =>
        _skeletronHead.Environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public void SetCasterEnvironment(IVanillaCasterEnvironment environment) =>
        _darkCaster.Environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public void SetWorldBounds(int widthTiles, double worldSurfaceTiles, double rockLayerTiles = double.PositiveInfinity) =>
        _context.SetWorldBounds(widthTiles, worldSurfaceTiles, rockLayerTiles);

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

    public void SetFishEnvironment(IVanillaFishEnvironment1458 environment) =>
        _fish.SetEnvironment(environment);

    public void SetFlyingEyeEnvironment(IVanillaFlyingEyeEnvironment environment) =>
        _flyingEye.SetEnvironment(environment);

    public void SetProjectileAnchors(IVanillaNpcProjectileAnchorLookup anchors) =>
        _context.ProjectileAnchors = anchors ?? throw new ArgumentNullException(nameof(anchors));

    public void SetProjectileEnvironment(IVanillaNpcProjectileEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _projectileEnvironment = environment;
        _flyer.SetProjectileEnvironment(environment);
        _bat.SetEnvironment(environment);
        _queenBee.SetProjectileEnvironment(environment);
        _retinazer.SetProjectileEnvironment(environment);
        _spazmatism.SetProjectileEnvironment(environment);
    }

    public void SetWorldConditions(
        bool dayTime,
        bool slimeRainActive,
        bool goodWorld = false,
        bool expertMode = false,
        bool masterMode = false,
        float windSpeedCurrent = 0f,
        bool remixWorld = false,
        double worldTime = 0d) =>
        _context.SetWorldConditions(dayTime, slimeRainActive, goodWorld, expertMode, masterMode, windSpeedCurrent, remixWorld, worldTime);

    public void SetCandidates(ReadOnlySpan<VanillaNpcTargetCandidate> candidates) =>
        _context.SetCandidates(candidates);

    public void SetNpcPeers(ReadOnlySpan<NpcSnapshot> peers) =>
        _context.SetNpcPeers(peers);

    void INpcAiRetainedSlotSnapshotConsumer.SetRetainedNpcSlots(ReadOnlySpan<VanillaNpcRetainedSlot> slots) =>
        _context.SetRetainedNpcSlots(slots);

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

        // AI_069's ocean/enrage guard requires both loaded world dimensions. Do not let an unavailable
        // world context fall through to a generic AI stepper and manufacture an unverified Duke transition.
        if (npcType == VanillaNpcIds.DukeFishron &&
            (_context.WorldWidthPixels <= 0d || !double.IsFinite(_context.WorldSurfacePixels)))
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
            VanillaNpcBehaviorFamily.Bat => _bat,
            VanillaNpcBehaviorFamily.Fish => _fish,
            VanillaNpcBehaviorFamily.SkeletronHead => _skeletronHead,
            VanillaNpcBehaviorFamily.SkeletronHand => _skeletronHand,
            VanillaNpcBehaviorFamily.QueenBee => _queenBee,
            VanillaNpcBehaviorFamily.Deerclops => _deerclops,
            VanillaNpcBehaviorFamily.WallOfFlesh => _wallOfFlesh,
            VanillaNpcBehaviorFamily.WallOfFleshEye => _wallOfFleshEye,
            VanillaNpcBehaviorFamily.WallOfFleshHungry => _wallOfFleshHungry,
            VanillaNpcBehaviorFamily.FireImp => _fireImp,
            VanillaNpcBehaviorFamily.DarkCaster => _darkCaster,
            VanillaNpcBehaviorFamily.BurningSphere => _burningSphere,
            VanillaNpcBehaviorFamily.QueenSlime => _queenSlime,
            VanillaNpcBehaviorFamily.SkeletronPrime => _skeletronPrime,
            VanillaNpcBehaviorFamily.PrimeSaw => _skeletronPrimeLimb,
            VanillaNpcBehaviorFamily.PrimeVice => _skeletronPrimeLimb,
            VanillaNpcBehaviorFamily.PrimeCannon => _primeRanged,
            VanillaNpcBehaviorFamily.PrimeLaser => _primeRanged,
            VanillaNpcBehaviorFamily.Retinazer => _retinazer,
            VanillaNpcBehaviorFamily.Spazmatism => _spazmatism,
            VanillaNpcBehaviorFamily.Destroyer => _destroyer,
            VanillaNpcBehaviorFamily.Plantera => _plantera,
            VanillaNpcBehaviorFamily.PlanteraHook => _plantera,
            VanillaNpcBehaviorFamily.PlanteraTentacle => _plantera,
            VanillaNpcBehaviorFamily.PlanteraSpore => _plantera,
            VanillaNpcBehaviorFamily.Golem => _golem,
            VanillaNpcBehaviorFamily.GolemHead => _golem,
            VanillaNpcBehaviorFamily.GolemFist => _golem,
            VanillaNpcBehaviorFamily.GolemHeadFree => _golem,
            VanillaNpcBehaviorFamily.DukeFishron => _dukeFishron,
            VanillaNpcBehaviorFamily.DetonatingBubble => _dukeFishron,
            VanillaNpcBehaviorFamily.LunaticCultist => _lunaticCultist,
            VanillaNpcBehaviorFamily.LunaticCultistClone => _lunaticCultist,
            VanillaNpcBehaviorFamily.EmpressOfLight => _empressOfLight,
            VanillaNpcBehaviorFamily.MoonLordCore => _moonLord,
            VanillaNpcBehaviorFamily.MoonLordHand => _moonLord,
            VanillaNpcBehaviorFamily.MoonLordHead => _moonLord,
            VanillaNpcBehaviorFamily.MoonLordFreeEye => _moonLord,
            VanillaNpcBehaviorFamily.MoonLordLeechBlob => _moonLord,
            _ => null
        };

        return strategy is null
            ? _inner.TryStepState(in npc, out next)
            : strategy.TryStep(in npc, in definition, _context, _inner, out next);
    }

    public bool TryPlanInitialization(in NpcSnapshot source, in NpcStateUpdate proposed,
        Span<NpcAiSpawnIntent> destination, out NpcStateUpdate initialization, out int count)
    {
        initialization = default;
        count = 0;
        if (source.TypeIdentity != VanillaNpcIds.SkeletronHead || proposed.Type != source.Type ||
            source.Ai.Ai0 != 0f || proposed.Ai.Ai0 != 1f)
            return false;
        // AI_011 creates hands before counting them for defense and skull attacks. Do not advance motion/timers here.
        initialization = new(source.Type, source.NetId, source.PositionX, source.PositionY,
            source.VelocityX, source.VelocityY, proposed.Target, source.Ai with { Ai0 = 1f }, source.Simulation);
        count = PlanSkeletronHands(in source, in initialization, destination);
        return true;
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

        if (source.Type == VanillaNpcIds.Plantera.Value && proposed.Type == source.Type)
            return PlanPlanteraSpawns(in source, in proposed, destination);
        if (source.Type == VanillaNpcIds.Golem.Value && proposed.Type == source.Type)
            return PlanGolemSpawns(in source, in proposed, destination);
        if (source.Type == VanillaNpcIds.DukeFishron.Value && proposed.Type == source.Type)
            return PlanDukeFishronBubble(in source, in proposed, destination);
        if (source.Type == VanillaNpcIds.LunaticCultist.Value && proposed.Type == source.Type)
            return PlanLunaticCultistSpawns(in source, in proposed, destination);
        if (source.Type == VanillaNpcIds.MoonLordCore.Value && proposed.Type == source.Type)
            return PlanMoonLordParts(in source, in proposed, destination);

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
            return 0; // AI_011 draws and allocates only in the accepted effect phase.
        if (source.Type == VanillaNpcIds.QueenBee.Value && proposed.Type == source.Type)
            return PlanQueenBeeStinger(in source, in proposed, destination);
        if (source.Type == VanillaNpcIds.Deerclops.Value && proposed.Type == source.Type)
            return PlanDeerclopsProjectiles(in source, in proposed, destination);
        if (source.Type == VanillaNpcIds.WallOfFleshEye.Value && proposed.Type == source.Type)
            return PlanWallOfFleshEyeLaser(in source, in proposed, destination);
        if (source.Type == VanillaNpcIds.QueenSlime.Value && proposed.Type == source.Type)
            return PlanQueenSlimeProjectiles(in source, in proposed, destination);
        if ((source.Type == VanillaNpcIds.PrimeCannon.Value || source.Type == VanillaNpcIds.PrimeLaser.Value) && proposed.Type == source.Type)
            return 0; // AI_035/036 shots consume RNG only after the source state commits.
        if ((source.Type == VanillaNpcIds.Retinazer.Value || source.Type == VanillaNpcIds.Spazmatism.Value) && proposed.Type == source.Type)
            return PlanTwinProjectile(in source, in proposed, destination);
        if (source.Type == VanillaNpcIds.DestroyerBody.Value && proposed.Type == source.Type)
            return PlanDestroyerLaser(in source, in proposed, destination);
        if (source.Type == VanillaNpcIds.Plantera.Value && proposed.Type == source.Type)
            return PlanPlanteraProjectile(in source, in proposed, destination);
        if ((source.Type == VanillaNpcIds.GolemHead.Value || source.Type == VanillaNpcIds.GolemHeadFree.Value) && proposed.Type == source.Type)
            return PlanGolemHeadProjectiles(in source, in proposed, destination);
        if (source.Type == VanillaNpcIds.DukeFishron.Value && proposed.Type == source.Type)
            return PlanDukeFishronProjectiles(in source, in proposed, destination);
        if (source.Type == VanillaNpcIds.LunaticCultist.Value && proposed.Type == source.Type)
            return PlanLunaticCultistProjectiles(in source, in proposed, destination);
        if (source.Type == VanillaNpcIds.AncientDoom.Value && proposed.Type == source.Type)
            return PlanAncientDoomProjectiles(in source, in proposed, destination);
        if (source.Type == VanillaNpcIds.EmpressOfLight.Value && proposed.Type == source.Type)
            return PlanEmpressProjectiles(in source, in proposed, destination);
        if ((source.Type == VanillaNpcIds.MoonLordHead.Value || source.Type == VanillaNpcIds.MoonLordHand.Value || source.Type == VanillaNpcIds.MoonLordFreeEye.Value) && proposed.Type == source.Type)
            return PlanMoonLordProjectiles(in source, in proposed, destination);

        return _flyer.PlanProjectileSpawns(in source, in proposed, _context, destination);
    }

    public int PlanProjectileMutations(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiProjectileMutationIntent> destination)
    {
        if (source.Type != VanillaNpcIds.MoonLordHand.Value || proposed.Type != source.Type)
            return 0;

        int row = source.Ai.Ai2 <= 0f ? 0 : 1;
        int elapsed = MoonPartAttackElapsed(proposed.Ai.Ai1, row, out int state, out _);
        if (state != 2 || elapsed != 292)
            return 0;

        VanillaNpcTargetCandidate target = _context.FindClosestPlayer(source.PositionX, source.PositionY, 46f, 66f);
        if (destination.IsEmpty)
            return 1;

        float centerX = source.PositionX + 23f;
        float centerY = source.PositionY + 33f;
        float dx = target.CenterX - centerX;
        float dy = target.CenterY - (centerY - 350f);
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        float velocityX;
        float velocityY;
        if (distance > 0f && float.IsFinite(distance))
        {
            // FNA Vector2.Normalize multiplies by a shared reciprocal before scaling.
            float inverse = 1f / distance;
            velocityX = dx * inverse * 12f;
            velocityY = dy * inverse * 12f;
        }
        else
        {
            // Vector2.Normalize(zero) yields NaN in vanilla, then falls back to Vector2.UnitY.
            velocityX = 0f;
            velocityY = 12f;
        }

        destination[0] = new NpcAiProjectileMutationIntent(
            VanillaProjectileIds.PhantasmalSphere,
            velocityX,
            velocityY,
            Ai0: -1f);
        return 1;
    }

    private int PlanWallOfFleshSpawns(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiSpawnIntent> destination)
    {
        if (_wallOfFleshEnvironment is null ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.WallOfFlesh, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation, out VanillaNpcHitboxSize hitbox))
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
            !definition.TryResolveHitbox(source.Simulation, out VanillaNpcHitboxSize hitbox) ||
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

    private void SpawnSkeletronSkull(
        in NpcSnapshot source,
        in NpcSnapshot committed,
        INpcAiCommittedNpcMutationSink mutations)
    {
        ushort attackTarget = source.Target;
        if (_context.DayTime || committed.Ai.Ai1 == 3f || !_context.ExpertMode || _projectileEnvironment is null ||
            !VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.SkeletronSkull, out var skull) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.SkeletronHead, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation, out VanillaNpcHitboxSize hitbox) ||
            !VanillaSkeletronHeadNpcBehaviorStrategy.TryGetHeadTarget(in source, _context,
                ref attackTarget, out var target, out _))
        {
            return;
        }

        int handCount = _context.CountNpcPeers(VanillaNpcIds.SkeletronHand);
        if (handCount >= 2 && source.Simulation.Life >= source.Simulation.LifeMax * 0.75d)
            return;

        float cadence = handCount == 0 ? 40f : 80f;
        if (_context.GoodWorld)
            cadence *= 0.8f;
        if (source.Ai.Ai1 != 0f || source.Ai.Ai2 % cadence != 0f)
            return;

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
            return;
        }

        // A host collision query can reenter ownership. Do not draw for a superseded source.
        if (!mutations.TryGetActive(committed.Handle.Slot, out var current) ||
            current.Handle != committed.Handle || current.Revision != committed.Revision)
            return;

        float speed = handCount == 0 ? 5f : 3f;
        float dx = target.CenterX - centerX + _random.NextInt32(-20, 21);
        float dy = target.CenterY - centerY + _random.NextInt32(-20, 21);
        int jitterX = _random.NextInt32(-50, 51);
        int jitterY = _random.NextInt32(-50, 51);
        VanillaSkeletronSkull.Aim(ref dx, ref dy, speed, jitterX, jitterY, OperatingSystem.IsWindows());
        dx += source.VelocityX;
        dy += source.VelocityY;

        // NewProjectile accepts a center; the owned intent/store retains physical top-left coordinates.
        var intent = new NpcAiProjectileIntent(
            VanillaProjectileIds.SkeletronSkull,
            centerX + dx * 5f - skull.Width * 0.5f,
            centerY + dy * 5f - skull.Height * 0.5f,
            dx,
            dy,
            Damage: 17,
            KnockBack: 0f)
        {
            InitialAi = new ProjectileAiState(-1f, 0f, 0f),
            TimeLeftOverride = 300
        };
        mutations.TrySpawnProjectile(in committed, in intent, out _);
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
            InitialLocalAi = new NpcAiState(60f, 0f, 0f, 0f),
            CanBeReplacedByOtherNpcs = true
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
            !definition.TryResolveHitbox(source.Simulation, out VanillaNpcHitboxSize hitbox))
        {
            return 0;
        }

        // NPC.AI style 11 truncates Y before adding integer half-height, including odd Good World dimensions.
        int spawnX = (int)(source.PositionX + hitbox.Width / 2);
        int spawnY = (int)source.PositionY + hitbox.Height / 2;
        destination[0] = new NpcAiSpawnIntent(
            VanillaNpcIds.SkeletronHand,
            spawnX,
            spawnY,
            0f,
            0f,
            proposed.Target)
        {
            InitialAi = new NpcAiState(-1f, source.Handle.Slot, 0f, 0f),
            StartSlot = source.Handle.Slot
        };
        destination[1] = new NpcAiSpawnIntent(
            VanillaNpcIds.SkeletronHand,
            spawnX,
            spawnY,
            0f,
            0f,
            proposed.Target)
        {
            InitialAi = new NpcAiState(1f, source.Handle.Slot, 0f, 150f),
            StartSlot = source.Handle.Slot
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
                proposed.Simulation,
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
            !brain.TryResolveHitbox(proposed.Simulation, out VanillaNpcHitboxSize hitbox))
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
            !eye.TryResolveHitbox(source.Simulation, out VanillaNpcHitboxSize hitbox))
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
            !definition.TryResolveHitbox(source.Simulation, out VanillaNpcHitboxSize hitbox))
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
            !definition.TryResolveHitbox(proposed.Simulation, out VanillaNpcHitboxSize hitbox) ||
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
            !definition.TryResolveHitbox(source.Simulation, out VanillaNpcHitboxSize hitbox))
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
            !definition.TryResolveHitbox(source.Simulation, out VanillaNpcHitboxSize hitbox))
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
        if (!VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.SkeletronPrime, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation, out VanillaNpcHitboxSize hitbox))
            return 0;
        // NPC.AI (1.4.5.8, style 32) creates arms before movement; Y truncates before adding half-height.
        int x = (int)(source.PositionX + hitbox.Width / 2);
        int y = (int)source.PositionY + hitbox.Height / 2;
        byte parent = source.Handle.Slot;
        destination[0] = new NpcAiSpawnIntent(VanillaNpcIds.PrimeCannon, x, y, 0f, 0f, proposed.Target) { InitialAi = new NpcAiState(-1f, parent, 0f, 0f), StartSlot = parent };
        destination[1] = new NpcAiSpawnIntent(VanillaNpcIds.PrimeSaw, x, y, 0f, 0f, proposed.Target) { InitialAi = new NpcAiState(1f, parent, 0f, 0f), StartSlot = parent };
        destination[2] = new NpcAiSpawnIntent(VanillaNpcIds.PrimeVice, x, y, 0f, 0f, proposed.Target) { InitialAi = new NpcAiState(-1f, parent, 0f, 150f), StartSlot = parent };
        destination[3] = new NpcAiSpawnIntent(VanillaNpcIds.PrimeLaser, x, y, 0f, 0f, proposed.Target) { InitialAi = new NpcAiState(1f, parent, 0f, 150f), StartSlot = parent };
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
            damage = spaz ? (_context.ExpertMode ? 22 : 25) : (_context.ExpertMode ? 19 : 20);
            fire = true;
        }
        else if (source.Ai.Ai0 >= 3f && proposed.Ai.Ai0 >= 3f && source.Simulation.LocalAi.Ai1 > 0f && proposed.Simulation.LocalAi.Ai1 == 0f)
        {
            type = spaz ? VanillaProjectileIds.SpazmatismEyeFire : VanillaProjectileIds.RetinazerDeathLaser;
            if (spaz) { speed = 6f; damage = _context.ExpertMode ? 27 : 30; }
            else
            {
                speed = source.Ai.Ai1 == 0f ? (_context.ExpertMode ? 10f : 8.5f) : 9f;
                damage = source.Ai.Ai1 == 0f ? (_context.ExpertMode ? 23 : 25) : (_context.ExpertMode ? 17 : 18);
            }
            fire = true;
        }
        else return 0;
        if (!fire) return 0;
        if (!VanillaNpcDefinitionCatalog.TryGet(source.TypeIdentity, source.NetIdentity, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation, out VanillaNpcHitboxSize hitbox))
            return 0;
        float cx = source.PositionX + hitbox.Width * .5f, cy = source.PositionY + hitbox.Height * .5f;
        float dx = target.CenterX - cx, dy = target.CenterY - cy;
        float d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
        float vx = dx / d * speed;
        float vy = dy / d * speed;
        float lead;
        if (type == VanillaProjectileIds.RetinazerDeathLaser)
        {
            // AI_030 late lasers have no random perturbation and start fifteen velocity units ahead.
            lead = 15f;
        }
        else if (type == VanillaProjectileIds.SpazmatismEyeFire)
        {
            // AI_031 draws Y then X before applying its own velocity.  Keep the draw order even when
            // Mechdusa replaces the result below, because Terraria consumes those random values first.
            vy += _random.NextInt32(-40, 41) * .01f;
            vx += _random.NextInt32(-40, 41) * .01f;
            vx += proposed.VelocityX * .5f;
            vy += proposed.VelocityY * .5f;
            if (IsMechQueenUp())
            {
                float rotation = proposed.Simulation.Rotation ?? 0f;
                vx = MathF.Cos(rotation + MathF.PI * .5f) * speed + proposed.VelocityX * .5f;
                vy = MathF.Sin(rotation + MathF.PI * .5f) * speed + proposed.VelocityY * .5f;
                lead = -3f;
            }
            else lead = -1f;
        }
        else
        {
            float jitter = type == VanillaProjectileIds.WallOfFleshEyeLaser ? .08f : .05f;
            vx += _random.NextInt32(-40, 41) * jitter;
            vy += _random.NextInt32(-40, 41) * jitter;
            lead = type == VanillaProjectileIds.WallOfFleshEyeLaser ? 15f : 4f;
        }
        destination[0] = new NpcAiProjectileIntent(type, cx + vx*lead, cy + vy*lead, vx, vy, damage, 0f);
        return 1;
    }

    private bool IsMechQueenUp()
    {
        Span<NpcSnapshot> primes = stackalloc NpcSnapshot[VanillaNpcSpawnRules.PhysicalSlotCount];
        int count = _context.CopyNpcPeers(VanillaNpcIds.SkeletronPrime, primes);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot prime = primes[index];
            if (prime.Ai.Ai3 == prime.Handle.Slot)
                return true;
        }

        return false;
    }

    private int PlanPlanteraSpawns(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiSpawnIntent> destination)
    {
        NpcAiState before = source.Simulation.LocalAi;
        NpcAiState after = proposed.Simulation.LocalAi;
        float cx = proposed.PositionX + 43f;
        float cy = proposed.PositionY + 43f;
        if (before.Ai0 == 0f && after.Ai0 == 1f)
        {
            if (destination.Length < 3) return destination.Length + 1;
            // The source picks solid/walled tiles around the target. Until the world-aware hook-anchor selector is
            // admitted, retain three stable server-owned fallback anchors so the linked topology is authoritative.
            (float x, float y)[] offsets = [(-240f, 80f), (240f, 80f), (0f, 260f)];
            for (int i = 0; i < 3; i++)
            {
                float ax = cx + offsets[i].x;
                float ay = cy + offsets[i].y;
                destination[i] = new NpcAiSpawnIntent(
                    VanillaNpcIds.PlanteraHook, (int)cx, (int)cy, 0f, 0f, proposed.Target)
                {
                    InitialAi = new NpcAiState(MathF.Max(1f, MathF.Round((ax + 8f) / 16f)), MathF.Max(1f, MathF.Round((ay + 8f) / 16f)), 0f, 0f),
                    InitialLocalAi = new NpcAiState(0f, 0f, 0f, source.Handle.Slot + 1f)
                };
            }
            return 3;
        }

        if (before.Ai0 == 1f && after.Ai0 == 2f)
        {
            int rootTentacles = _context.GoodWorld ? 14 : 8;
            int hookTentaclesPerHook = _context.ExpertMode ? rootTentacles / 2 - 1 : 0;
            int hookCount = Math.Min(3, _context.CopyOwnedNpcPeers(VanillaNpcIds.PlanteraHook, source.Handle.Slot, stackalloc NpcSnapshot[3]));
            int total = rootTentacles + hookCount * hookTentaclesPerHook;
            if (destination.Length < total) return destination.Length + 1;
            int index = 0;
            for (int i = 0; i < rootTentacles; i++)
            {
                destination[index++] = new NpcAiSpawnIntent(VanillaNpcIds.PlanteraTentacle, (int)cx, (int)cy, 0f, 0f, proposed.Target)
                {
                    InitialAi = new NpcAiState((i % 4 - 1.5f) * 45f, (i / 4 - 1f) * 45f, 0f, 0f),
                    InitialLocalAi = new NpcAiState(0f, 0f, 0f, source.Handle.Slot + 1f)
                };
            }
            if (hookTentaclesPerHook > 0)
            {
                Span<NpcSnapshot> hooks = stackalloc NpcSnapshot[3];
                int copied = _context.CopyOwnedNpcPeers(VanillaNpcIds.PlanteraHook, source.Handle.Slot, hooks);
                for (int hookIndex = 0; hookIndex < copied; hookIndex++)
                {
                    NpcSnapshot hook = hooks[hookIndex];
                    for (int j = 0; j < hookTentaclesPerHook; j++)
                    {
                        destination[index++] = new NpcAiSpawnIntent(VanillaNpcIds.PlanteraTentacle, (int)cx, (int)cy, 0f, 0f, proposed.Target)
                        {
                            InitialAi = new NpcAiState((j - hookTentaclesPerHook / 2f) * 35f, 60f, 0f, hook.Handle.Slot + 1f),
                            InitialLocalAi = new NpcAiState(0f, 0f, 0f, source.Handle.Slot + 1f)
                        };
                    }
                }
            }
            return index;
        }

        int lifeMax = Math.Max(1, proposed.Simulation.LifeMax);
        bool phaseTwo = proposed.Simulation.Life <= lifeMax / 2;
        if (phaseTwo && before.Ai1 > 0f && after.Ai1 == 0f)
        {
            if (destination.IsEmpty) return 1;
            if (proposed.Target >= byte.MaxValue || !_context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate target) || !target.Active || target.Dead)
                return 0;
            float dx = target.CenterX - cx + _random.NextInt32(-10, 11);
            float dy = target.CenterY - cy + _random.NextInt32(-10, 11);
            if (dy < 0f) dy -= MathF.Abs(dx * .2f);
            float d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
            destination[0] = new NpcAiSpawnIntent(VanillaNpcIds.PlanteraSpore, (int)cx, (int)cy, dx / d * 8f, dy / d * 8f, proposed.Target)
            {
                InitialLocalAi = new NpcAiState(0f, 0f, 0f, source.Handle.Slot + 1f)
            };
            return 1;
        }
        return 0;
    }

    private int PlanPlanteraProjectile(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiProjectileIntent> destination)
    {
        int lifeMax = Math.Max(1, proposed.Simulation.LifeMax);
        if (proposed.Simulation.Life <= lifeMax / 2 || source.Simulation.LocalAi.Ai1 <= 0f || proposed.Simulation.LocalAi.Ai1 != 0f)
            return 0;
        if (destination.IsEmpty) return 1;
        if (proposed.Target >= byte.MaxValue || !_context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate target) || !target.Active || target.Dead)
            return 0;
        ProjectileTypeId type = VanillaProjectileIds.PlanteraSeed;
        int damage = 22;
        if (proposed.Simulation.Life < lifeMax * .8f && _random.NextInt32(0, _context.ExpertMode ? 2 : 4) == 0)
        { type = VanillaProjectileIds.PlanteraPoisonSeed; damage = 27; }
        else if (proposed.Simulation.Life < lifeMax * .8f && _random.NextInt32(0, _context.ExpertMode ? 6 : 8) == 0)
        { type = VanillaProjectileIds.PlanteraThornBall; damage = 31; }
        float cx = proposed.PositionX + 43f, cy = proposed.PositionY + 43f;
        float dx = target.CenterX - cx, dy = target.CenterY - cy;
        float speed = _context.ExpertMode ? 17f : 15f;
        float d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
        float vx = dx / d * speed, vy = dy / d * speed;
        destination[0] = new NpcAiProjectileIntent(type, cx + vx * 3f, cy + vy * 3f, vx, vy, damage, 0f)
        { TimeLeftOverride = type == VanillaProjectileIds.PlanteraThornBall ? 0 : 300 };
        return 1;
    }

    private int PlanLunaticCultistSpawns(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiSpawnIntent> destination)
    {
        if (source.Ai.Ai0 == 7f && proposed.Ai.Ai0 == 7f && source.Ai.Ai1 >= 4f)
        {
            int cadence = _context.ExpertMode ? 30 : 20;
            if (((int)(source.Ai.Ai1 - 4f) % cadence) == 0)
            {
                const int lightRequested = 5;
                if (destination.Length < lightRequested) return destination.Length + 1;
                if (proposed.Target >= byte.MaxValue || !_context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate target) || !target.Active || target.Dead)
                    return 0;
                float lightCx = proposed.PositionX + 12f, lightCy = proposed.PositionY + 25f;
                float dx = target.CenterX - lightCx, dy = target.CenterY - lightCy;
                float d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
                float baseAngle = MathF.Atan2(dy / d, dx / d);
                for (int i = 0; i < lightRequested; i++)
                {
                    float angle = baseAngle + (i - 2) * (MathF.PI * 2f / 25f);
                    float vx = MathF.Cos(angle) * 8f, vy = MathF.Sin(angle) * 8f;
                    destination[i] = new NpcAiSpawnIntent(VanillaNpcIds.AncientLight, (int)(lightCx + MathF.Sign(dx) * 30f), (int)(lightCy + 19f), vx, vy, proposed.Target)
                    { InitialAi = new NpcAiState(0f, NextFloatDirection() * .015707964f, vx, vy) };
                }
                return lightRequested;
            }
        }

        if (source.Ai.Ai0 == 8f && proposed.Ai.Ai0 == 8f && source.Ai.Ai1 >= 4f && ((int)(source.Ai.Ai1 - 4f) % 20) == 0)
        {
            int doomRequested = Math.Min(3, _context.CountNpcPeers(VanillaNpcIds.LunaticCultistClone) + 1);
            if (destination.Length < doomRequested) return destination.Length + 1;
            if (proposed.Target >= byte.MaxValue || !_context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate target) || !target.Active || target.Dead)
                return 0;
            for (int i = 0; i < doomRequested; i++)
            {
                float angle = (i + 1) * (MathF.PI * 2f / (doomRequested + 1));
                int x = (int)(target.CenterX + MathF.Cos(angle) * 280f);
                int y = (int)(target.CenterY + MathF.Sin(angle) * 220f);
                destination[i] = new NpcAiSpawnIntent(VanillaNpcIds.AncientDoom, x, y, 0f, 0f, proposed.Target)
                { InitialAi = new NpcAiState(source.Handle.Slot, 0f, 0f, 0f) };
            }
            return doomRequested;
        }

        // The ritual projectile reaches ai[0]=300 three hundred ticks after the source-owned ritual spawn at boss ai[1]=30.
        // Preserve the source side effect here until projectile-to-NPC spawn intents become a first-class runtime boundary.
        if (source.Ai.Ai0 == 5f && proposed.Ai.Ai0 == 5f && source.Ai.Ai1 == 330f)
        {
            if (destination.IsEmpty) return 1;
            NpcTypeId child = _context.CountNpcPeers(VanillaNpcIds.CultistDragonHead) == 0 ? VanillaNpcIds.CultistDragonHead : VanillaNpcIds.AncientVision;
            destination[0] = new NpcAiSpawnIntent(child, (int)(proposed.PositionX + 12f), (int)(proposed.PositionY + 25f), 0f, 0f, proposed.Target);
            return 1;
        }

        if (source.Ai.Ai0 != 5f || proposed.Ai.Ai0 != 5f || source.Ai.Ai1 >= 30f || proposed.Ai.Ai1 < 30f)
            return 0;
        int existing = _context.CountNpcPeers(VanillaNpcIds.LunaticCultistClone);
        int requested = Math.Min(2, Math.Max(0, 6 - existing));
        if (requested == 0) return 0;
        if (destination.Length < requested) return destination.Length + 1;
        float cx = proposed.PositionX + 12f;
        float cy = proposed.PositionY + 25f;
        for (int i = 0; i < requested; i++)
        {
            float angle = (i + 1) * (MathF.PI * 2f / (requested + 1));
            destination[i] = new NpcAiSpawnIntent(
                VanillaNpcIds.LunaticCultistClone,
                (int)(cx + MathF.Cos(angle) * 180f),
                (int)(cy + MathF.Sin(angle) * 180f),
                0f, 0f, proposed.Target)
            {
                InitialAi = new NpcAiState(5f, proposed.Ai.Ai1, 0f, source.Handle.Slot),
                InitialLocalAi = new NpcAiState(0f, proposed.Simulation.LocalAi.Ai1, 0f, source.Handle.Slot + 1f)
            };
        }
        return requested;
    }

    public bool DefersStatePublication(in NpcSnapshot before, in NpcStateUpdate proposed) =>
        before.TypeIdentity == VanillaNpcIds.DarkCaster && proposed.Type == before.Type;

    public NpcSnapshot CompleteCommittedState(in NpcSnapshot before, in NpcSnapshot committed,
        INpcAiCommittedNpcMutationSink mutations) =>
        before.TypeIdentity == VanillaNpcIds.DarkCaster && committed.TypeIdentity == VanillaNpcIds.DarkCaster
            ? _darkCaster.Complete(in before, in committed, _context, _random, mutations) : committed;

    public bool DeactivatesAfterStep(in NpcSnapshot before, in NpcStateUpdate proposed) =>
        (proposed.Type == before.Type && proposed.Simulation.Life == 0 &&
         (VanillaSkeletronPrimeLimbNpcBehaviorStrategy.Deactivates(in before, in proposed) ||
          (before.TypeIdentity == VanillaNpcIds.MoonLordLeechBlob && proposed.Simulation.TimeLeft == 0) ||
          // AI_012 removes the orphan immediately; its internal negative-life sentinel never enters the store.
          ((before.TypeIdentity == VanillaNpcIds.SkeletronHand || before.TypeIdentity == VanillaNpcIds.PrimeCannon ||
            before.TypeIdentity == VanillaNpcIds.PrimeLaser) && proposed.Ai.Ai2 > 50f &&
           proposed.Ai.Ai2 == before.Ai.Ai2 + 10f))) ||
        VanillaDestroyerNpcBehaviorStrategy.DespawnsAtDaytimeRockLayer(in before, in proposed, _context);

    public void ApplyCommittedEffect(
        in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations)
    {
        if (before.TypeIdentity == VanillaNpcIds.SkeletronHead && committed.TypeIdentity == VanillaNpcIds.SkeletronHead)
        {
            SpawnSkeletronSkull(in before, in committed, mutations);
            if (mutations.TryGetActive(committed.Handle.Slot, out var head) &&
                head.Handle == committed.Handle && head.Revision == committed.Revision)
                _skeletronHead.ApplyEffects(in before, in committed, _context, _random, mutations);
        }
        if ((before.TypeIdentity == VanillaNpcIds.PrimeCannon || before.TypeIdentity == VanillaNpcIds.PrimeLaser) &&
            before.TypeIdentity == committed.TypeIdentity)
            _primeRanged.ApplyEffects(in before, in committed, _context, _random, mutations);
        if ((before.TypeIdentity == VanillaNpcIds.PrimeSaw || before.TypeIdentity == VanillaNpcIds.PrimeVice) &&
            before.TypeIdentity == committed.TypeIdentity)
            VanillaSkeletronPrimeLimbNpcBehaviorStrategy.ApplyEffects(in before, in committed, _random, mutations);
        if (before.TypeIdentity == VanillaNpcIds.DarkCaster && committed.TypeIdentity == VanillaNpcIds.DarkCaster)
            VanillaDarkCasterBehavior.SpawnSphere(in before, in committed, mutations);
        VanillaMoonLordLeechBehavior.ApplyHealing(in before, in committed, _context, mutations);
        VanillaMoonLordLeechBehavior.SpawnFromHead(in before, in committed, _context, mutations);
        VanillaDestroyerNpcBehaviorStrategy.SpawnChain(in before, in committed, _context.GoodWorld, mutations);
        VanillaDestroyerNpcBehaviorStrategy.DespawnDaytimeChain(in before, in committed, _context, mutations);
    }

    public void ApplyCommittedEffectAfterSpawns(
        in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations)
    {
        if (before.TypeIdentity == VanillaNpcIds.DarkCaster && committed.TypeIdentity == VanillaNpcIds.DarkCaster)
            _random.NextInt32(0, 3);
        // AI_009 still makes its two dust-chance draws on a dedicated server; Dust.NewDust itself does not.
        if (before.TypeIdentity == VanillaNpcIds.WaterSphere && committed.TypeIdentity == VanillaNpcIds.WaterSphere)
        {
            _random.NextInt32(0, 5);
            _random.NextInt32(0, 5);
        }
        // AI_078 sets netUpdate when changing attacks and when acquiring the bolt target.
        if (committed.TypeIdentity == VanillaNpcIds.MoonLordHand &&
            (before.Ai.Ai0 != committed.Ai.Ai0 ||
             (committed.Ai.Ai0 == 3f && VanillaMoonLordHandBehavior.Phase(
                 committed.Ai.Ai1, committed.Ai.Ai2 == 0f, out _, out _) == 0)))
            mutations.TryTranslate(committed.Handle, 0f, 0f, out _);
        VanillaMoonLordNpcBehaviorStrategy.ApplyTeleportToParts(in before, in committed, _context, mutations);
        if (committed.TypeIdentity == VanillaNpcIds.MoonLordHead && before.Ai.Ai0 >= 0f)
        {
            int elapsed = VanillaMoonLordHeadBehavior.Phase(committed.Ai.Ai1, out int phase, out _);
            if (before.Ai.Ai0 != committed.Ai.Ai0 || before.Target != committed.Target ||
                (phase == 1 && elapsed == 180) || (phase == 3 && elapsed == 1))
                mutations.TryTranslate(committed.Handle, 0f, 0f, out _);
        }
    }

    private int PlanMoonLordParts(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiSpawnIntent> destination)
    {
        bool initialShell = (source.Simulation.LocalAi.Ai3 == 0f || source.Ai.Ai0 == -1f) &&
            source.Ai.Ai1 + 1f == 60f && proposed.Ai.Ai0 is 0f or 3f or -2f;
        if (initialShell)
        {
            if (destination.Length < 3) return destination.Length + 1;
            // NewNPC runs before the core's world-motion step, and the source truncates
            // its center before adding the hand offsets (relevant across coordinate zero).
            int cx = (int)(source.PositionX + 23f);
            int cy = (int)(source.PositionY + 33f);
            NpcAiState owned = new(0f, 0f, 0f, source.Handle.Slot + 1f);
            destination[0] = new NpcAiSpawnIntent(VanillaNpcIds.MoonLordHand, cx - 400, cy - 100, 0f, 0f, byte.MaxValue)
            { InitialAi = new NpcAiState(0f, 0f, 0f, source.Handle.Slot), InitialLocalAi = owned, LinkSourceLocalAiSlot = 0 };
            destination[1] = new NpcAiSpawnIntent(VanillaNpcIds.MoonLordHand, cx + 400, cy - 100, 0f, 0f, byte.MaxValue)
            { InitialAi = new NpcAiState(0f, 0f, 1f, source.Handle.Slot), InitialLocalAi = owned, LinkSourceLocalAiSlot = 1 };
            destination[2] = new NpcAiSpawnIntent(VanillaNpcIds.MoonLordHead, cx, cy - 400, 0f, 0f, byte.MaxValue)
            { InitialAi = new NpcAiState(0f, 0f, 0f, source.Handle.Slot), LinkSourceLocalAiSlot = 2 };
            return 3;
        }

        // TerrariaServer creates each True Eye from the first lethal retirement of its hand/head part. The core-open
        // transition only changes core/part state; synthesizing three eyes here duplicates those source-owned spawns.
        return 0;
    }

    private int PlanDukeFishronBubble(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiSpawnIntent> destination)
    {
        if (destination.IsEmpty || proposed.Target >= byte.MaxValue ||
            !_context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate target) ||
            !target.Active || target.Dead || target.Ghost)
            return 0;

        // AI_069 (1.4.5.8) emits before incrementing ai[2] and before rotating the circle velocity.
        bool circle = source.Ai.Ai0 == 7f;
        float timer = source.Ai.Ai2;
        if ((source.Ai.Ai0 != 2f && !circle) || !float.IsFinite(timer) || timer < 0f ||
            timer >= (circle ? 120f : 80f) || timer % 4f != 0f)
            return 0;

        float cx = source.PositionX + 75f;
        float cy = source.PositionY + 50f;
        float dx = circle ? source.VelocityX : target.CenterX - cx;
        float dy = circle ? source.VelocityY : target.CenterY - cy;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (!float.IsFinite(distance) || distance <= 0f || (circle && source.Simulation.DirectionX is not (-1 or 1)))
            return 0;
        float nx = dx / distance, ny = dy / distance;
        destination[0] = new NpcAiSpawnIntent(
            VanillaNpcIds.DetonatingBubble,
            (int)(cx + nx * 85f),
            (int)(cy + ny * 85f) + 45,
            circle ? -ny * source.Simulation.DirectionX * 6f : 0f,
            circle ? nx * source.Simulation.DirectionX * 6f : 0f,
            circle ? proposed.Target : VanillaNpcDefinitionCatalog.DefaultTarget)
        {
            // Ordinary bubbles defer targeting, random launch and scale to AI70's first update.
            InitialAi = circle ? new NpcAiState(0f, 0f, 0f, _random.NextInt32(80, 121) / 100f) : default
        };
        return 1;
    }


    private int PlanDukeFishronProjectiles(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiProjectileIntent> destination)
    {
        if (source.Ai.Ai2 != 60f || (source.Ai.Ai0 != 3f && source.Ai.Ai0 != 8f))
            return 0;
        if (proposed.Target >= byte.MaxValue ||
            !_context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate player) ||
            !player.Active || player.Dead ||
            !VanillaDukeFishronNpcBehaviorStrategy.TryResolveEnrage(_context, in player, out bool enraged))
            return 0;
        int requested = source.Ai.Ai0 == 3f ? 2 : 1;
        if (destination.Length < requested) return destination.Length + 1;
        float cx = proposed.PositionX + 75f, cy = proposed.PositionY + 50f;
        int direction = source.Simulation.DirectionX;
        if (direction == 0 && proposed.Target < byte.MaxValue && _context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate target))
            direction = target.CenterX >= cx ? 1 : -1;
        if (direction == 0) direction = 1;
        if (source.Ai.Ai0 == 3f)
        {
            destination[0] = new NpcAiProjectileIntent(VanillaProjectileIds.SharknadoBolt, cx, cy, direction * 2f, 8f, 0, 0f);
            destination[1] = new NpcAiProjectileIntent(VanillaProjectileIds.SharknadoBolt, cx, cy, -direction * 2f, 8f, 0, 0f);
            return 2;
        }
        destination[0] = new NpcAiProjectileIntent(VanillaProjectileIds.SharknadoBolt, cx, cy, 0f, 0f, 0, 0f)
        { InitialAi = new ProjectileAiState(1f, proposed.Target + 1f, enraged ? 1f : 0f) };
        return 1;
    }

    private int PlanLunaticCultistProjectiles(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiProjectileIntent> destination)
    {
        if (proposed.Target >= byte.MaxValue || !_context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate target) || !target.Active || target.Dead)
            return 0;
        int state = (int)source.Ai.Ai0;
        float timer = source.Ai.Ai1;
        float cx = source.PositionX + 12f, cy = source.PositionY + 25f;
        float dx = target.CenterX - cx, dy = target.CenterY - cy;
        float d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
        float nx = dx / d, ny = dy / d;
        int clones = Math.Min(6, _context.CountNpcPeers(VanillaNpcIds.LunaticCultistClone));

        if (state == 5 && timer == 30f)
        {
            if (destination.IsEmpty) return 1;
            destination[0] = new NpcAiProjectileIntent(VanillaProjectileIds.CultistRitual, cx, cy, 0f, 0f, 0, 0f)
            { InitialAi = new ProjectileAiState(0f, source.Handle.Slot, 0f) };
            return 1;
        }

        int cadence;
        ProjectileTypeId mainType;
        int mainDamage;
        bool cloneVolley = false;
        int cloneShotsPerClone = 1;
        if (state == 2)
        {
            cadence = _context.ExpertMode ? 90 : 120;
            if (_context.GoodWorld) cadence -= 30;
            if (timer < 4f || ((int)(timer - 4f) % cadence) != 0) return 0;
            mainType = VanillaProjectileIds.CultistBossIceMist;
            mainDamage = _context.ExpertMode ? 25 : 35;
            cloneVolley = true;
        }
        else if (state == 3)
        {
            cadence = _context.GoodWorld ? 10 : _context.ExpertMode ? 12 : 18;
            if (timer < 4f || ((int)(timer - 4f) % cadence) != 0) return 0;
            mainType = VanillaProjectileIds.CultistBossFireBall;
            mainDamage = _context.ExpertMode ? 20 : 30;
            cloneVolley = ((int)(timer - 4f) / cadence) == 2;
        }
        else if (state == 4)
        {
            if (timer != 20f) return 0;
            mainType = VanillaProjectileIds.CultistBossLightningOrb;
            mainDamage = _context.ExpertMode ? 30 : 45;
            cloneVolley = true;
        }
        else if (state == 7)
        {
            cadence = _context.ExpertMode ? 30 : 20;
            if (timer < 4f || ((int)(timer - 4f) % cadence) != 0 || ((int)(timer - 4f) / cadence) != 2)
                return 0;
            mainType = default;
            mainDamage = 0;
            cloneVolley = true;
            cloneShotsPerClone = 5;
        }
        else return 0;

        int requested = (mainType.Value == 0 ? 0 : 1) + (cloneVolley ? clones * cloneShotsPerClone : 0);
        if (requested == 0) return 0;
        if (destination.Length < requested) return destination.Length + 1;
        int index = 0;
        if (mainType.Value != 0)
        {
            float leadX = target.CenterX + target.VelocityX * 20f - cx;
            float leadY = target.CenterY + target.VelocityY * 20f - cy;
            float leadDistance = MathF.Max(.001f, MathF.Sqrt(leadX * leadX + leadY * leadY));
            float aimX = leadX / leadDistance, aimY = leadY / leadDistance;
            float speed = mainType == VanillaProjectileIds.CultistBossIceMist ? 4f :
                mainType == VanillaProjectileIds.CultistBossFireBall ? 6f + NextUnitFloat() * 4f : 0f;
            if (mainType == VanillaProjectileIds.CultistBossFireBall)
                Rotate(ref aimX, ref aimY, NextFloatDirection() * .2617994f);
            destination[index++] = new NpcAiProjectileIntent(mainType, cx + MathF.Sign(dx) * 30f, cy + (mainType == VanillaProjectileIds.CultistBossLightningOrb ? -100f : 12f), aimX * speed, aimY * speed, mainDamage, 0f)
            { InitialAi = mainType == VanillaProjectileIds.CultistBossIceMist ? new ProjectileAiState(0f, 1f, 0f) : default };
        }
        if (cloneVolley)
        {
            Span<NpcSnapshot> peers = stackalloc NpcSnapshot[6];
            int copied = _context.CopyOwnedNpcPeers(VanillaNpcIds.LunaticCultistClone, source.Handle.Slot, peers);
            for (int i = 0; i < copied && index < requested; i++)
            {
                NpcSnapshot clone = peers[i];
                float ccx = clone.PositionX + 12f, ccy = clone.PositionY + 25f;
                float cdx = target.CenterX + target.VelocityX * 20f - ccx, cdy = target.CenterY + target.VelocityY * 20f - ccy;
                float cd = MathF.Max(.001f, MathF.Sqrt(cdx * cdx + cdy * cdy));
                float baseX = cdx / cd, baseY = cdy / cd;
                for (int shot = 0; shot < cloneShotsPerClone && index < requested; shot++)
                {
                    float shotX = baseX, shotY = baseY;
                    float speed = 6f + NextUnitFloat() * 4f;
                    float spread = cloneShotsPerClone == 5 ? .62831855f : .2617994f;
                    Rotate(ref shotX, ref shotY, NextFloatDirection() * spread);
                    destination[index++] = new NpcAiProjectileIntent(VanillaProjectileIds.CultistBossFireBallClone, ccx, ccy, shotX * speed, shotY * speed, 18, 0f);
                }
            }
        }
        return index;
    }

    private static int PlanAncientDoomProjectiles(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiProjectileIntent> destination)
    {
        if (source.Ai.Ai1 >= 420f || proposed.Ai.Ai1 < 420f)
            return 0;
        const int requested = 4;
        if (destination.Length < requested) return destination.Length + 1;
        float cx = proposed.PositionX + 21f, cy = proposed.PositionY + 21f;
        for (int i = 0; i < requested; i++)
        {
            float angle = i * MathF.PI * .5f;
            destination[i] = new NpcAiProjectileIntent(VanillaProjectileIds.AncientDoomProjectile, cx, cy, MathF.Cos(angle) * 4f, MathF.Sin(angle) * 4f, 30, 0f);
        }
        return requested;
    }

    private int PlanEmpressProjectiles(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiProjectileIntent> destination)
    {
        int state = (int)source.Ai.Ai0;
        float timer = source.Ai.Ai1;
        // AI-120 enters phase two only after state 10 changes ai[3] at its source tick 90.
        // Health alone merely requests that transition, so projectile cadence and damage stay phase one beforehand.
        bool phaseTwo = source.Ai.Ai3 is 1f or 3f;
        bool rageCondition = _context.ShouldEmpressBeEnraged(in source);
        bool enraged = rageCondition || source.Ai.Ai3 is 2f or 3f;
        bool expertCadence = _context.ExpertMode || rageCondition;
        if (proposed.Target >= byte.MaxValue || !_context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate target) || !target.Active || target.Dead)
            return 0;

        int Damage(int normal, int phaseNormal, int expert, int phaseExpert) => enraged ? 9999 : _context.ExpertMode ? (phaseTwo ? phaseExpert : expert) : (phaseTwo ? phaseNormal : normal);
        float cx = source.PositionX + 50f, cy = source.PositionY + 50f;
        if (state == 0 && timer == 0f)
        {
            if (destination.IsEmpty) return 1;
            destination[0] = new NpcAiProjectileIntent(VanillaProjectileIds.HallowBossDeathAurora, cx, cy - 80f, 0f, 0f, 0, 0f);
            return 1;
        }
        if (state == 2)
        {
            int cadence = phaseTwo && expertCadence ? 2 : 3;
            if (timer >= 60f || ((int)timer % cadence) != 0) return 0;
            int rotationIndex = ((int)(timer / cadence)) % 3;
            int requested = 1 + CountEmpressExtraTargets(in source, rotationIndex, 2_400f);
            if (destination.Length < requested) return destination.Length + 1;
            float randomAngle = phaseTwo && expertCadence
                ? NextUnitFloat() * MathF.PI * 2f
                : MathF.PI * .5f * NextFloatDirection();
            float speed = phaseTwo && expertCadence ? 10f : 6f;
            float vx = MathF.Sin(randomAngle) * speed;
            float vy = -MathF.Cos(randomAngle) * speed;
            int written = 0;
            destination[written++] = EmpressRainbowStreak(cx, cy, vx, vy, Damage(45, 50, 30, 35), proposed.Target, timer / 60f);
            for (int index = 0; index < _context.CandidateCount; index++)
            {
                VanillaNpcTargetCandidate extra = _context.GetCandidateAt(index);
                if (CanEmpressShootExtraAt(in source, in extra, rotationIndex, 2_400f, cx, cy))
                    destination[written++] = EmpressRainbowStreak(cx, cy, vx, vy, Damage(45, 50, 30, 35), extra.Slot, timer / 60f);
            }
            return written;
        }
        if (state == 4 && timer < 100f && ((int)timer % 4) == 0)
        {
            // AI_120 state 4 uses Expert/rage mode, not phase, for its five-lance ring and 450-pixel radius.
            float sourceDistanceX = target.CenterX - cx;
            float sourceDistanceY = target.CenterY - cy;
            if (sourceDistanceX * sourceDistanceX + sourceDistanceY * sourceDistanceY > 2_400f * 2_400f)
                return 0;
            int rotationIndex = ((int)(timer / 4f)) % 3;
            int requested = 1 + CountEmpressExtraTargets(in source, rotationIndex, 2_400f);
            if (destination.Length < requested) return destination.Length + 1;
            float segmentCount = expertCadence ? 5f : 4f;
            float angle = MathF.PI / (segmentCount * 2f) + (timer / 4f) * (MathF.PI / segmentCount);
            float directionX = MathF.Cos(angle);
            float directionY = MathF.Sin(angle);
            if (!expertCadence)
            {
                directionX += directionX > 0f ? .5f : -.5f;
                float directionLength = MathF.Sqrt(directionX * directionX + directionY * directionY);
                directionX /= directionLength;
                directionY /= directionLength;
            }
            float radius = expertCadence ? 450f : 300f;
            int written = 0;
            destination[written++] = EmpressRingLance(in target, ref directionX, ref directionY, radius, timer, Damage(50, 60, 30, 35));
            for (int index = 0; index < _context.CandidateCount; index++)
            {
                VanillaNpcTargetCandidate extra = _context.GetCandidateAt(index);
                if (!CanEmpressShootExtraAt(in source, in extra, rotationIndex, 2_400f, cx, cy))
                    continue;

                // Source reuses vector29, so a velocity-dot reversal for one extra player affects the following one.
                destination[written++] = EmpressRingLance(in extra, ref directionX, ref directionY, radius, timer, Damage(50, 60, 30, 35));
            }
            return written;
        }
        if (state == 5 && timer == 0f)
        {
            const int requested = 13;
            if (destination.Length < requested) return destination.Length + 1;
            float offset = NextUnitFloat() * MathF.PI * 2f;
            for (int i = 0; i < requested; i++)
            {
                float angle = offset + i * MathF.PI * 2f / requested;
                // AI_120 state 5 starts from UnitY rotated by +Pi/2, hence the negative cosine/sine direction.
                float vx = -MathF.Cos(angle) * 8f, vy = -MathF.Sin(angle) * 8f;
                float px = cx + 55f + vy / 8f * 30f;
                float py = cy - 30f - vx / 8f * 30f;
                destination[i] = new NpcAiProjectileIntent(VanillaProjectileIds.HallowBossLastingRainbow, px, py, vx, vy, Damage(45, 50, 30, 35), 0f)
                { InitialAi = new ProjectileAiState(0f, i / (float)requested, 0f) };
            }
            return requested;
        }
        if (state == 6 && timer < 180f && ((int)timer % 60) == 0)
        {
            // AI_120 state 6 takes its Sun Dance count from Expert/rage mode, independently of phase two.
            int requested = expertCadence ? 8 : 6;
            if (destination.Length < requested) return destination.Length + 1;
            int wave = (int)timer / 60;
            for (int i = 0; i < requested; i++)
            {
                float fraction = (i + .5f + wave * .5f) / requested;
                float angle = MathF.PI * 2f * (fraction + (target.CenterX > cx ? 1f : 0f));
                destination[i] = new NpcAiProjectileIntent(VanillaProjectileIds.FairyQueenSunDance, cx, cy - 100f, 0f, 0f, Damage(50, 60, 35, 40), 0f)
                { InitialAi = new ProjectileAiState(angle, source.Handle.Slot, 0f) };
            }
            return requested;
        }
        if (state == 7)
        {
            // AI_120 state 7 takes cadence, line count and geometry from Expert/rage mode, not phase two.
            int cadence = expertCadence ? 40 : 60;
            int waves = expertCadence ? 6 : 4;
            if (timer >= cadence * waves || ((int)timer % cadence) != 0) return 0;
            int rayCount = expertCadence ? 18 : 13;
            // Source float accumulation includes the Classic endpoint but overshoots it in the Expert 1/18 loop.
            int requested = expertCadence ? 18 : 14;
            if (destination.Length < requested) return destination.Length + 1;
            _ = NextUnitFloat(); // AI_120 consumes this presentation phase before its distance gate.
            float lineSpacing = expertCadence ? 200f : 150f;
            float lineLength = 13f * 150f;
            if (expertCadence)
                lineLength *= .5f;
            int wave = (int)timer / cadence;
            float centerX = target.CenterX;
            float centerY = target.CenterY;
            float lineX = 0f;
            float lineY = 0f;
            float baseAimX = 0f;
            float baseAimY = 1f;
            switch (wave)
            {
                case 0:
                    centerX -= lineLength * .5f;
                    lineY = lineLength;
                    baseAimX = 1f;
                    baseAimY = 0f;
                    break;
                case 1:
                    centerX += lineLength * .5f;
                    centerY += lineSpacing * .5f;
                    lineY = lineLength;
                    baseAimX = -1f;
                    baseAimY = 0f;
                    break;
                case 2:
                    centerX -= lineLength * .4f;
                    centerY -= lineLength * .4f;
                    lineX = lineLength * 1.4f;
                    baseAimX = 1f;
                    baseAimY = 1f;
                    break;
                case 3:
                    centerX += lineLength * .4f + lineSpacing * .5f;
                    centerY -= lineLength * .4f;
                    lineX = -lineLength * 1.4f;
                    baseAimX = -1f;
                    baseAimY = 1f;
                    break;
                case 4:
                    centerX -= lineLength * .4f;
                    centerY += lineLength * .4f;
                    lineX = lineLength * 1.4f;
                    baseAimX = target.CenterX - centerX;
                    baseAimY = target.CenterY - centerY;
                    NormalizeTo(ref baseAimX, ref baseAimY, 1f);
                    break;
                case 5:
                    centerX += lineLength * .4f + lineSpacing * .5f;
                    centerY += lineLength * .4f;
                    lineX = -lineLength * 1.4f;
                    baseAimX = target.CenterX - centerX;
                    baseAimY = target.CenterY - centerY;
                    NormalizeTo(ref baseAimX, ref baseAimY, 1f);
                    break;
            }
            float step = 1f / rayCount;
            int index = 0;
            for (float fraction = 0f; fraction <= 1f && index < requested; fraction += step)
            {
                float px = centerX + lineX * (fraction - .5f);
                float py = centerY + lineY * (fraction - .5f);
                float aimX = baseAimX;
                float aimY = baseAimY;
                if (expertCadence)
                {
                    float targetAimX = target.CenterX + target.VelocityX * 20f * fraction - px;
                    float targetAimY = target.CenterY + target.VelocityY * 20f * fraction - py;
                    NormalizeTo(ref targetAimX, ref targetAimY, 1f);
                    aimX = baseAimX * .25f + targetAimX * .75f;
                    aimY = baseAimY * .25f + targetAimY * .75f;
                    NormalizeTo(ref aimX, ref aimY, 1f);
                }
                destination[index++] = new NpcAiProjectileIntent(VanillaProjectileIds.FairyQueenLance, px, py, 0f, 0f, Damage(70, 65, 65, 30), 0f)
                { InitialAi = new ProjectileAiState(MathF.Atan2(aimY, aimX), fraction, 0f) };
            }
            return index;
        }
        if (state == 11 && timer < 100f && ((int)timer % 3) == 0)
        {
            float sourceDistanceX = target.CenterX - cx;
            float sourceDistanceY = target.CenterY - cy;
            if (sourceDistanceX * sourceDistanceX + sourceDistanceY * sourceDistanceY > 2_400f * 2_400f)
                return 0;
            int rotationIndex = ((int)(timer / 3f)) % 3;
            int requested = 1 + CountEmpressExtraTargets(in source, rotationIndex, 2_400f);
            if (destination.Length < requested) return destination.Length + 1;
            int written = 0;
            destination[written++] = EmpressDirectLance(in target, timer, Damage(50, 60, 30, 35));
            for (int index = 0; index < _context.CandidateCount; index++)
            {
                VanillaNpcTargetCandidate extra = _context.GetCandidateAt(index);
                if (CanEmpressShootExtraAt(in source, in extra, rotationIndex, 2_400f, cx, cy))
                    destination[written++] = EmpressDirectLance(in extra, timer, Damage(50, 60, 30, 35));
            }
            return written;
        }
        if (state == 12 && timer >= 10f && timer < 60f)
        {
            int cadence = expertCadence ? 4 : 6;
            if (((int)timer % cadence) != 0) return 0;
            int rotationIndex = ((int)(timer % cadence)) % 3;
            int requested = 1 + CountEmpressExtraTargets(in source, rotationIndex, 2_400f);
            if (destination.Length < requested) return destination.Length + 1;
            float progress = (timer - 10f) / 50f;
            float angle = MathF.PI * 2f * progress;
            // AI_120 state 12 rotates (0, -20), rather than a positive X-axis vector.
            float vx = -MathF.Sin(angle) * 20f, vy = -MathF.Cos(angle) * 20f;
            int written = 0;
            destination[written++] = EmpressRainbowStreak(cx, cy, vx, vy, Damage(45, 50, 30, 35), proposed.Target, progress);
            for (int index = 0; index < _context.CandidateCount; index++)
            {
                VanillaNpcTargetCandidate extra = _context.GetCandidateAt(index);
                if (CanEmpressShootExtraAt(in source, in extra, rotationIndex, 2_400f, cx, cy))
                    destination[written++] = EmpressRainbowStreak(cx, cy, vx, vy, Damage(45, 50, 30, 35), extra.Slot, progress);
            }
            return written;
        }
        return 0;
    }

    private int CountEmpressExtraTargets(in NpcSnapshot source, int rotationIndex, float range)
    {
        float centerX = source.PositionX + 50f;
        float centerY = source.PositionY + 50f;
        int count = 0;
        for (int index = 0; index < _context.CandidateCount; index++)
        {
            VanillaNpcTargetCandidate candidate = _context.GetCandidateAt(index);
            if (CanEmpressShootExtraAt(in source, in candidate, rotationIndex, range, centerX, centerY))
                count++;
        }
        return count;
    }

    private bool CanEmpressShootExtraAt(in NpcSnapshot source, in VanillaNpcTargetCandidate candidate,
        int rotationIndex, float range, float sourceCenterX, float sourceCenterY) =>
        _playerInteractions is not null &&
        candidate.Slot != source.Target &&
        candidate.Slot % 3 == rotationIndex &&
        candidate.Active && !candidate.Dead &&
        _playerInteractions.HasInteraction(source.Handle, new PlayerSlotId(candidate.Slot)) &&
        (candidate.CenterX - sourceCenterX) * (candidate.CenterX - sourceCenterX) +
        (candidate.CenterY - sourceCenterY) * (candidate.CenterY - sourceCenterY) <= range * range;

    private static NpcAiProjectileIntent EmpressRainbowStreak(float centerX, float centerY, float velocityX, float velocityY,
        int damage, ushort target, float progress) =>
        new(VanillaProjectileIds.HallowBossRainbowStreak, centerX - 55f, centerY - 30f, velocityX, velocityY, damage, 0f)
        { InitialAi = new ProjectileAiState(target, progress, 0f) };

    private static NpcAiProjectileIntent EmpressRingLance(in VanillaNpcTargetCandidate target,
        ref float directionX, ref float directionY, float radius, float timer, int damage)
    {
        float velocityLength = MathF.Sqrt(target.VelocityX * target.VelocityX + target.VelocityY * target.VelocityY);
        float normalizedVelocityX = velocityLength > .001f ? target.VelocityX / velocityLength : 0f;
        float normalizedVelocityY = velocityLength > .001f ? target.VelocityY / velocityLength : 1f;
        if (normalizedVelocityX * directionX + normalizedVelocityY * directionY > 0f)
        {
            directionX = -directionX;
            directionY = -directionY;
        }
        float predictedX = target.CenterX + target.VelocityX * 90f;
        float predictedY = target.CenterY + target.VelocityY * 90f;
        float positionX = target.CenterX + directionX * radius - target.VelocityX * 30f;
        float positionY = target.CenterY + directionY * radius - target.VelocityY * 30f;
        float radialX = positionX - target.CenterX;
        float radialY = positionY - target.CenterY;
        float radialDistance = MathF.Sqrt(radialX * radialX + radialY * radialY);
        if (radialDistance < radius)
        {
            float fallbackX = radialDistance <= .001f ? -directionX : -radialX / radialDistance;
            float fallbackY = radialDistance <= .001f ? -directionY : -radialY / radialDistance;
            positionX = target.CenterX + fallbackX * radius;
            positionY = target.CenterY + fallbackY * radius;
        }
        float aim = MathF.Atan2(predictedY - positionY, predictedX - positionX);
        return new NpcAiProjectileIntent(VanillaProjectileIds.FairyQueenLance, positionX, positionY, 0f, 0f, damage, 0f)
        { InitialAi = new ProjectileAiState(aim, timer / 100f, 0f) };
    }

    private static NpcAiProjectileIntent EmpressDirectLance(in VanillaNpcTargetCandidate target, float timer, int damage)
    {
        float velocityLength = MathF.Sqrt(target.VelocityX * target.VelocityX + target.VelocityY * target.VelocityY);
        float directionX = velocityLength > .001f ? -target.VelocityX / velocityLength : 0f;
        float directionY = velocityLength > .001f ? -target.VelocityY / velocityLength : -1f;
        float positionX = target.CenterX + directionX * 100f;
        float positionY = target.CenterY + directionY * 100f;
        float aim = MathF.Atan2(target.CenterY + target.VelocityY * 90f - positionY,
            target.CenterX + target.VelocityX * 90f - positionX);
        return new NpcAiProjectileIntent(VanillaProjectileIds.FairyQueenLance, positionX, positionY, 0f, 0f, damage, 0f)
        { InitialAi = new ProjectileAiState(aim, timer / 100f, 0f) };
    }

    private int PlanMoonLordProjectiles(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiProjectileIntent> destination)
    {
        if (proposed.Simulation.Life == 0 || proposed.Simulation.TimeLeft == 0)
            return 0;
        if (source.TypeIdentity == VanillaNpcIds.MoonLordHand)
            return VanillaMoonLordHandBehavior.PlanProjectiles(in source, in proposed, destination, _random);
        if (source.TypeIdentity == VanillaNpcIds.MoonLordHead)
            return VanillaMoonLordHeadBehavior.PlanProjectiles(in source, in proposed, _context, destination);
        if (proposed.Target >= byte.MaxValue || !_context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate target) || !target.Active || target.Dead)
            return 0;
        float cx = source.PositionX + 30f, cy = source.PositionY + 30f;

        if (source.Type == VanillaNpcIds.MoonLordFreeEye.Value)
        {
            int elapsed = MoonEyeAttackElapsed(source.Ai.Ai1, out int state, out int duration);
            if (state == 1 && (elapsed == duration - 14 || elapsed == duration - 7 || elapsed == duration))
            {
                if (destination.IsEmpty) return 1;
                float dx = target.CenterX - cx, dy = target.CenterY - cy;
                float d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
                destination[0] = new NpcAiProjectileIntent(VanillaProjectileIds.PhantasmalBolt, cx, cy, dx / d * 8f, dy / d * 8f, 35, 0f);
                return 1;
            }
            if (state == 2 && elapsed >= 15 && elapsed < 105 && (elapsed - 15) % 30 == 0)
            {
                if (destination.IsEmpty) return 1;
                float dx = target.CenterX - cx, dy = target.CenterY - cy;
                float d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
                destination[0] = new NpcAiProjectileIntent(VanillaProjectileIds.PhantasmalSphere, cx, cy, dx / d * 7f, dy / d * 7f, 40, 0f)
                { InitialAi = new ProjectileAiState(30f, source.Handle.Slot, 0f) };
                return 1;
            }
            if (state == 4 && elapsed >= 0 && elapsed < 140 && elapsed % 20 == 0)
            {
                if (destination.IsEmpty) return 1;
                float dx = target.CenterX - cx, dy = target.CenterY - cy;
                float d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
                destination[0] = new NpcAiProjectileIntent(VanillaProjectileIds.PhantasmalEye, cx, cy, dx / d * 7f, dy / d * 7f, 35, 0f);
                return 1;
            }
        }
        return 0;
    }

    private static int MoonPartAttackElapsed(float ai1, int row, out int state, out int duration)
    {
        ReadOnlySpan<int> states = row switch { 0 => [0,1,2,0,3], 1 => [1,0,3,0,2], _ => [3,0,2,3,1] };
        ReadOnlySpan<int> durations = row switch { 0 => [50,70,330,60,90], 1 => [70,50,90,60,330], _ => [180,30,435,180,375] };
        int total = 0; for (int i = 0; i < durations.Length; i++) total += durations[i];
        int t = ((int)ai1) % total; if (t < 0) t += total;
        int start = 0;
        for (int i = 0; i < states.Length; i++)
        {
            if (t < start + durations[i]) { state = states[i]; duration = durations[i]; return t - start; }
            start += durations[i];
        }
        state = states[0]; duration = durations[0]; return 0;
    }

    private static int MoonEyeAttackElapsed(float ai1, out int state, out int duration)
    {
        ReadOnlySpan<int> states = [0,1,0,2,0,3,0,4,0,2];
        ReadOnlySpan<int> durations = [53,90,53,135,53,200,53,375,53,135];
        const int total = 1200;
        int t = ((int)ai1) % total; if (t < 0) t += total;
        int start = 0;
        for (int i = 0; i < states.Length; i++)
        {
            if (t < start + durations[i]) { state = states[i]; duration = durations[i]; return t - start; }
            start += durations[i];
        }
        state = 0; duration = 53; return 0;
    }

    private int PlanGolemSpawns(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiSpawnIntent> destination)
    {
        NpcAiState before = source.Simulation.LocalAi;
        NpcAiState after = proposed.Simulation.LocalAi;
        float cx = proposed.PositionX + 70f, cy = proposed.PositionY + 70f;
        float owner = source.Handle.Slot + 1f;
        if (before.Ai0 == 0f && after.Ai0 == 1f)
        {
            if (destination.Length < 3) return destination.Length + 1;
            NpcAiState owned = new(0f, 0f, 0f, owner);
            destination[0] = new NpcAiSpawnIntent(VanillaNpcIds.GolemFistLeft, (int)(cx - 84f), (int)(cy - 9f), 0f, 0f, proposed.Target) { InitialLocalAi = owned };
            destination[1] = new NpcAiSpawnIntent(VanillaNpcIds.GolemFistRight, (int)(cx + 78f), (int)(cy - 9f), 0f, 0f, proposed.Target) { InitialLocalAi = owned };
            destination[2] = new NpcAiSpawnIntent(VanillaNpcIds.GolemHead, (int)(cx - 3f), (int)(cy - 57f), 0f, 0f, proposed.Target) { InitialLocalAi = owned };
            return 3;
        }
        if (before.Ai2 == 0f && after.Ai2 == 1f)
        {
            if (destination.IsEmpty) return 1;
            destination[0] = new NpcAiSpawnIntent(VanillaNpcIds.GolemHeadFree, (int)cx, (int)(proposed.PositionY + 70f), 0f, 0f, proposed.Target)
            { InitialLocalAi = new NpcAiState(0f, 0f, 0f, owner) };
            return 1;
        }
        return 0;
    }

    private int PlanGolemHeadProjectiles(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiProjectileIntent> destination)
    {
        bool eyeWrap = source.Ai.Ai1 > 0f && proposed.Ai.Ai1 == 0f;
        bool fireballWrap = source.Ai.Ai2 > 0f && proposed.Ai.Ai2 == 0f;
        if (!eyeWrap && !fireballWrap) return 0;
        if (proposed.Target >= byte.MaxValue || !_context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate target) || !target.Active || target.Dead)
            return 0;
        int count = (eyeWrap ? 1 : 0) + (fireballWrap ? 2 : 0);
        if (destination.Length < count) return destination.Length + 1;
        float cx = proposed.PositionX + 35f, cy = proposed.PositionY + 25f;
        int index = 0;
        if (eyeWrap)
        {
            float dx = target.CenterX - cx, dy = target.CenterY - cy;
            float d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
            destination[index++] = new NpcAiProjectileIntent(VanillaProjectileIds.GolemEyeBeam, cx, cy, dx / d * 8f, dy / d * 8f, source.Type == VanillaNpcIds.GolemHead.Value && source.Ai.Ai0 == 0f ? 18 : 24, 0f);
        }
        if (fireballWrap)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float sx = cx + side * 18f;
                float sy = proposed.PositionY + 13f;
                float dx = target.CenterX - sx, dy = target.CenterY - sy;
                float d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
                destination[index++] = new NpcAiProjectileIntent(VanillaProjectileIds.GolemFireball, sx + dx / d * 33f, sy + dy / d * 33f, dx / d * 11f, dy / d * 11f, 28, 0f) { TimeLeftOverride = 300 };
            }
        }
        return index;
    }

    private int PlanDestroyerLaser(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiProjectileIntent> destination)
    {
        if (destination.IsEmpty || _projectileEnvironment is null || source.Simulation.LocalAi.Ai0 <= 0f ||
            proposed.Simulation.LocalAi.Ai0 != 0f || proposed.Target >= byte.MaxValue ||
            !_context.TryFindCandidate((byte)proposed.Target, out VanillaNpcTargetCandidate target) || !target.Active || target.Dead ||
            !VanillaNpcDefinitionCatalog.TryGet(source.TypeIdentity, source.NetIdentity, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation, out VanillaNpcHitboxSize hitbox))
        {
            return 0;
        }

        float targetX = target.CenterX - VanillaPlayerHitboxFacts.BaseWidth * .5f;
        float targetY = target.CenterY - VanillaPlayerHitboxFacts.BaseHeight * .5f;
        if (!_projectileEnvironment.CanHit(source.PositionX, source.PositionY, hitbox.Width, hitbox.Height,
                targetX, targetY, (int)VanillaPlayerHitboxFacts.BaseWidth, (int)VanillaPlayerHitboxFacts.BaseHeight))
        {
            return 0;
        }

        float cx = source.PositionX + hitbox.Width * .5f;
        float cy = source.PositionY + hitbox.Height * .5f;
        float dx = target.CenterX - cx + _random.NextInt32(-20, 21);
        float dy = target.CenterY - cy + _random.NextInt32(-20, 21);
        float d = MathF.Max(.001f, MathF.Sqrt(dx * dx + dy * dy));
        float vx = dx / d * 8f + _random.NextInt32(-20, 21) * .05f;
        float vy = dy / d * 8f + _random.NextInt32(-20, 21) * .05f;
        int damage = _context.ExpertMode ? 18 : 22;
        destination[0] = new NpcAiProjectileIntent(VanillaProjectileIds.RetinazerDeathLaser,
            cx + vx * 5f, cy + vy * 5f, vx, vy, damage, 0f) { TimeLeftOverride = 300 };
        return 1;
    }

}
