from pathlib import Path


def replace_once(path: str, old: str, new: str, label: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"{label} anchor changed in {path}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")


def patch_content_ids() -> None:
    path = "src/TerraRuntime.Contracts/Gameplay/VanillaContentIds.cs"
    replace_once(
        path,
        "    public static readonly NpcTypeId Bee = new(210);\n    public static readonly NpcTypeId SmallBee = new(211);\n    public static readonly NpcTypeId FattyHornet = new(231);",
        "    public static readonly NpcTypeId Bee = new(210);\n    public static readonly NpcTypeId SmallBee = new(211);\n    public static readonly NpcTypeId QueenBee = new(222);\n    public static readonly NpcTypeId FattyHornet = new(231);",
        "queen-bee-npc-id")
    replace_once(
        path,
        "    public static readonly NpcAiStyleId BlazingWheel = new(21);\n    public static readonly NpcAiStyleId BrainOfCthulhu = new(54);",
        "    public static readonly NpcAiStyleId BlazingWheel = new(21);\n    public static readonly NpcAiStyleId QueenBee = new(43);\n    public static readonly NpcAiStyleId BrainOfCthulhu = new(54);",
        "queen-bee-ai-style")
    replace_once(
        path,
        "    public static readonly ProjectileTypeId SkeletronSkull = new(270);\n    public static readonly ProjectileTypeId BloodShot = new(811);",
        "    public static readonly ProjectileTypeId SkeletronSkull = new(270);\n    public static readonly ProjectileTypeId QueenBeeStinger = new(719);\n    public static readonly ProjectileTypeId BloodShot = new(811);",
        "queen-bee-projectile-id")


def patch_definition_catalog() -> None:
    path = "src/TerraRuntime.Core/Npcs/VanillaNpcDefinitionCatalog.cs"
    replace_once(
        path,
        "    SkeletronHead = 13,\n    SkeletronHand = 14\n}",
        "    SkeletronHead = 13,\n    SkeletronHand = 14,\n    QueenBee = 15\n}",
        "queen-bee-behavior-family")
    marker = """        if (VanillaFlyerNpcCatalog.TryGetDefinition(type, out definition))
            return true;

        if (type == VanillaNpcIds.BrainOfCthulhu)"""
    queen = """        if (VanillaFlyerNpcCatalog.TryGetDefinition(type, out definition))
            return true;

        if (type == VanillaNpcIds.QueenBee)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.QueenBee,
                AiStyle: VanillaNpcAiStyles.QueenBee,
                BehaviorFamily: VanillaNpcBehaviorFamily.QueenBee,
                PhysicsFamily: VanillaNpcPhysicsFamily.NoClipFlight,
                Role: NpcArchetypeRole.Boss,
                BaseWidth: 66,
                BaseHeight: 66,
                Damage: 30,
                Defense: 8,
                LifeMax: 3400,
                KnockBackResist: 0f,
                Scale: 1f,
                NoGravityAtSpawn: true,
                NoTileCollideAtSpawn: true,
                SyncAnchor: VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == VanillaNpcIds.BrainOfCthulhu)"""
    replace_once(path, marker, queen, "queen-bee-definition")


def patch_behavior_context() -> None:
    path = "src/TerraRuntime.Core/Npcs/VanillaNpcBehaviorContext.cs"
    marker = """    public bool TryFindFirstNpcPeer(NpcTypeId type, out NpcSnapshot peer)
    {"""
    addition = """    public int CountActivePlayersWithin(float centerX, float centerY, float radius)
    {
        if (!float.IsFinite(centerX) || !float.IsFinite(centerY) || !float.IsFinite(radius) || radius < 0f)
            return 0;

        float radiusSquared = radius * radius;
        int count = 0;
        for (int index = 0; index < _candidateCount; index++)
        {
            VanillaNpcTargetCandidate candidate = _candidates[index];
            if (!candidate.Active || candidate.Dead || candidate.Ghost)
                continue;
            float dx = candidate.CenterX - centerX;
            float dy = candidate.CenterY - centerY;
            if (dx * dx + dy * dy < radiusSquared)
                count++;
        }
        return count;
    }

    public bool TryFindFirstNpcPeer(NpcTypeId type, out NpcSnapshot peer)
    {"""
    replace_once(path, marker, addition, "queen-bee-player-radius")


def patch_spawn_intents() -> None:
    path = "src/TerraRuntime.Core/Npcs/RuntimeNpcAiSpawnIntents.cs"
    replace_once(
        path,
        "    public NpcAiState InitialAi { get; init; }\n\n    /// <summary>",
        "    public NpcAiState InitialAi { get; init; }\n\n    /// <summary>Source-owned initial localAI state applied atomically with allocation.</summary>\n    public NpcAiState InitialLocalAi { get; init; }\n\n    /// <summary>",
        "spawn-local-ai-property")
    replace_once(
        path,
        "            !float.IsFinite(intent.VelocityY) ||\n            !intent.InitialAi.IsFinite)",
        "            !float.IsFinite(intent.VelocityY) ||\n            !intent.InitialAi.IsFinite ||\n            !intent.InitialLocalAi.IsFinite)",
        "spawn-local-ai-validation")
    replace_once(
        path,
        "                TimeLeft = VanillaNpcSpawnFacts.NewNpcTimeLeft\n            });",
        "                TimeLeft = VanillaNpcSpawnFacts.NewNpcTimeLeft,\n                LocalAi = intent.InitialLocalAi\n            });",
        "spawn-local-ai-commit")


def patch_targeting_stepper() -> None:
    path = "src/TerraRuntime.Core/Npcs/VanillaNpcTargetingAiStepper.cs"
    replace_once(
        path,
        "    private readonly VanillaSkeletronHandNpcBehaviorStrategy _skeletronHand = new();\n    private readonly IVanillaNpcRandom _random;\n    private IVanillaNpcProjectileEnvironment? _projectileEnvironment;",
        "    private readonly VanillaSkeletronHandNpcBehaviorStrategy _skeletronHand = new();\n    private readonly VanillaQueenBeeNpcBehaviorStrategy _queenBee;\n    private readonly IVanillaNpcRandom _random;\n    private IVanillaNpcProjectileEnvironment? _projectileEnvironment;\n    private IVanillaQueenBeeEnvironment? _queenBeeEnvironment;",
        "queen-bee-stepper-fields")
    replace_once(
        path,
        "        _spikeBall = new VanillaSpikeBallNpcBehaviorStrategy(_random);\n        if (kingSlimeEnvironment is IVanillaEyeOfCthulhuEnvironment eyeEnvironment)",
        "        _spikeBall = new VanillaSpikeBallNpcBehaviorStrategy(_random);\n        _queenBee = new VanillaQueenBeeNpcBehaviorStrategy(_random);\n        if (kingSlimeEnvironment is IVanillaEyeOfCthulhuEnvironment eyeEnvironment)",
        "queen-bee-stepper-init")
    replace_once(
        path,
        "    public void SetWormEnvironment(IVanillaWormEnvironment environment) =>\n        _worm.SetEnvironment(environment);",
        "    public void SetQueenBeeEnvironment(IVanillaQueenBeeEnvironment environment)\n    {\n        _queenBeeEnvironment = environment ?? throw new ArgumentNullException(nameof(environment));\n        _queenBee.SetEnvironment(environment);\n    }\n\n    public void SetWormEnvironment(IVanillaWormEnvironment environment) =>\n        _worm.SetEnvironment(environment);",
        "queen-bee-environment-setter")
    replace_once(
        path,
        "        _projectileEnvironment = environment;\n        _flyer.SetProjectileEnvironment(environment);",
        "        _projectileEnvironment = environment;\n        _flyer.SetProjectileEnvironment(environment);\n        _queenBee.SetProjectileEnvironment(environment);",
        "queen-bee-los-setter")
    replace_once(
        path,
        "            VanillaNpcBehaviorFamily.SkeletronHand => _skeletronHand,\n            _ => null",
        "            VanillaNpcBehaviorFamily.SkeletronHand => _skeletronHand,\n            VanillaNpcBehaviorFamily.QueenBee => _queenBee,\n            _ => null",
        "queen-bee-dispatch")
    replace_once(
        path,
        "        if (source.Type == VanillaNpcIds.SkeletronHead.Value &&\n            proposed.Type == VanillaNpcIds.SkeletronHead.Value)\n        {\n            return PlanSkeletronHands(in source, in proposed, destination);\n        }",
        "        if (source.Type == VanillaNpcIds.SkeletronHead.Value &&\n            proposed.Type == VanillaNpcIds.SkeletronHead.Value)\n        {\n            return PlanSkeletronHands(in source, in proposed, destination);\n        }\n\n        if (source.Type == VanillaNpcIds.QueenBee.Value && proposed.Type == source.Type)\n            return PlanQueenBeeMinion(in source, in proposed, destination);",
        "queen-bee-npc-planner-route")
    replace_once(
        path,
        "        if (source.Type == VanillaNpcIds.SkeletronHead.Value && proposed.Type == source.Type)\n            return PlanSkeletronSkull(in source, in proposed, destination);\n\n        return _flyer.PlanProjectileSpawns(in source, in proposed, _context, destination);",
        "        if (source.Type == VanillaNpcIds.SkeletronHead.Value && proposed.Type == source.Type)\n            return PlanSkeletronSkull(in source, in proposed, destination);\n        if (source.Type == VanillaNpcIds.QueenBee.Value && proposed.Type == source.Type)\n            return PlanQueenBeeStinger(in source, in proposed, destination);\n\n        return _flyer.PlanProjectileSpawns(in source, in proposed, _context, destination);",
        "queen-bee-projectile-planner-route")

    marker = """    private static void NormalizeTo(ref float x, ref float y, float speed)
    {"""
    methods = """    private int PlanQueenBeeMinion(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiSpawnIntent> destination)
    {
        if (destination.IsEmpty || source.Ai.Ai0 != 1f || proposed.Type != source.Type ||
            proposed.Ai.Ai2 <= source.Ai.Ai2 || _queenBeeEnvironment is null || _projectileEnvironment is null ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.QueenBee, out VanillaNpcDefinition definition) ||
            !VanillaQueenBeeNpcBehaviorStrategy.TryGetTarget(in source, _context, ref UnsafeTarget(proposed.Target), out _))
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
                target.CenterX - VanillaNpcBehaviorContext.BasePlayerWidth * 0.5f,
                target.CenterY - VanillaNpcBehaviorContext.BasePlayerHeight * 0.5f,
                (int)VanillaNpcBehaviorContext.BasePlayerWidth,
                (int)VanillaNpcBehaviorContext.BasePlayerHeight))
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

        float targetTop = target.CenterY - VanillaNpcBehaviorContext.BasePlayerHeight * 0.5f;
        if (source.PositionY + definition.Height >= targetTop)
            return 0;

        float centerX = source.PositionX + definition.Width * 0.5f;
        int direction = target.CenterX < centerX ? -1 : 1;
        float spawnX = source.PositionX + definition.Width * 0.5f + _random.NextInt32(0, 20) * direction;
        float spawnY = source.PositionY + definition.Height * 0.8f;
        if (!_projectileEnvironment.CanHit(
                spawnX, spawnY, 1, 1,
                target.CenterX - VanillaNpcBehaviorContext.BasePlayerWidth * 0.5f,
                targetTop,
                (int)VanillaNpcBehaviorContext.BasePlayerWidth,
                (int)VanillaNpcBehaviorContext.BasePlayerHeight))
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

    private static ref ushort UnsafeTarget(ushort target) => throw new NotSupportedException();

    private static void NormalizeTo(ref float x, ref float y, float speed)
    {"""
    replace_once(path, marker, methods, "queen-bee-planners")
    # Remove a construction-time helper that exists only to make the textual insertion obvious; target validation
    # is performed explicitly below and must not require a ref-return trick.
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    old = """            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.QueenBee, out VanillaNpcDefinition definition) ||
            !VanillaQueenBeeNpcBehaviorStrategy.TryGetTarget(in source, _context, ref UnsafeTarget(proposed.Target), out _))
        {
            return 0;
        }

        ushort targetSlot = proposed.Target;"""
    new = """            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.QueenBee, out VanillaNpcDefinition definition))
        {
            return 0;
        }

        ushort targetSlot = proposed.Target;"""
    if old not in text:
        raise SystemExit("queen-bee-planner-cleanup anchor changed")
    text = text.replace(old, new, 1)
    text = text.replace("\n    private static ref ushort UnsafeTarget(ushort target) => throw new NotSupportedException();\n", "\n", 1)
    p.write_text(text, encoding="utf-8")


def patch_projectile_catalogs() -> None:
    path = "src/TerraRuntime.Contracts/Gameplay/VanillaProjectileDefinitionCatalog.cs"
    replace_once(
        path,
        """    private static readonly VanillaProjectileDefinition BoneDefinition = new(""",
        """    private static readonly VanillaProjectileDefinition QueenBeeStingerDefinition = new(
        Width: 10,
        Height: 10,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 10,
        CollisionHeight: 10);

    private static readonly VanillaProjectileDefinition BoneDefinition = new(""",
        "queen-bee-projectile-definition")
    replace_once(
        path,
        """        if (type == VanillaProjectileIds.SkeletronSkull)
        {
            definition = SkeletronSkullDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.Bone)""",
        """        if (type == VanillaProjectileIds.SkeletronSkull)
        {
            definition = SkeletronSkullDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.QueenBeeStinger)
        {
            definition = QueenBeeStingerDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.Bone)""",
        "queen-bee-projectile-lookup")

    path = "src/TerraRuntime/VanillaProjectileBehaviorProfile.cs"
    replace_once(
        path,
        "        type == VanillaProjectileIds.ConfettiGun ||\n        type == VanillaProjectileIds.ConfettiMelee ||",
        "        type == VanillaProjectileIds.ConfettiGun ||\n        type == VanillaProjectileIds.QueenBeeStinger ||\n        type == VanillaProjectileIds.ConfettiMelee ||",
        "queen-bee-basic-arrow")


def patch_items() -> None:
    path = "src/TerraRuntime.Core/Items/VanillaItemDefinitionCatalog.cs"
    marker = """    private static readonly VanillaItemDefinition ChippysWingsInactiveDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.ChippysWingsInactive, 24, 8);
"""
    definitions = marker + """
    private static readonly VanillaItemDefinition QueenBeeHatDefinition = QueenBeeWorldDrop(VanillaQueenBeeItemIds.BeeHat, 28, 20);
    private static readonly VanillaItemDefinition QueenBeeShirtDefinition = QueenBeeWorldDrop(VanillaQueenBeeItemIds.BeeShirt, 18, 14);
    private static readonly VanillaItemDefinition QueenBeePantsDefinition = QueenBeeWorldDrop(VanillaQueenBeeItemIds.BeePants, 18, 14);
    private static readonly VanillaItemDefinition BeeGunDefinition = QueenBeeWorldDrop(VanillaQueenBeeItemIds.BeeGun, 50, 18);
    private static readonly VanillaItemDefinition BeeKeeperDefinition = QueenBeeWorldDrop(VanillaQueenBeeItemIds.BeeKeeper, 40, 40);
    private static readonly VanillaItemDefinition HiveWandDefinition = QueenBeeWorldDrop(VanillaQueenBeeItemIds.HiveWand, 8, 10);
    private static readonly VanillaItemDefinition BeenadeDefinition = QueenBeeWorldDrop(VanillaQueenBeeItemIds.Beenade, 10, 10);
    private static readonly VanillaItemDefinition HoneyCombDefinition = QueenBeeWorldDrop(VanillaQueenBeeItemIds.HoneyComb, 22, 22);
    private static readonly VanillaItemDefinition NectarDefinition = QueenBeeWorldDrop(VanillaQueenBeeItemIds.Nectar, 16, 30);
    private static readonly VanillaItemDefinition QueenBeeTrophyDefinition = QueenBeeWorldDrop(VanillaQueenBeeItemIds.QueenBeeTrophy, 30, 30);
    private static readonly VanillaItemDefinition BeeMaskDefinition = QueenBeeWorldDrop(VanillaQueenBeeItemIds.BeeMask, 28, 20);
    private static readonly VanillaItemDefinition BeeWaxDefinition = QueenBeeWorldDrop(VanillaQueenBeeItemIds.BeeWax, 18, 16);
    private static readonly VanillaItemDefinition HoneyedGogglesDefinition = QueenBeeWorldDrop(VanillaQueenBeeItemIds.HoneyedGoggles, 16, 30);
    private static readonly VanillaItemDefinition BeesKneesDefinition = QueenBeeWorldDrop(VanillaQueenBeeItemIds.BeesKnees, 12, 28);
    private static readonly VanillaItemDefinition QueenBeeBossBagDefinition = QueenBeeWorldDrop(VanillaQueenBeeItemIds.QueenBeeBossBag, 24, 24);
    private static readonly VanillaItemDefinition QueenBeePetItemDefinition = QueenBeeWorldDrop(VanillaQueenBeeItemIds.QueenBeePetItem, 16, 30);
    private static readonly VanillaItemDefinition QueenBeeMasterTrophyDefinition = QueenBeeWorldDrop(VanillaQueenBeeItemIds.QueenBeeMasterTrophy, 14, 14);
    private static readonly VanillaItemDefinition QueenOfBeesDefinition = QueenBeeWorldDrop(VanillaQueenBeeItemIds.QueenOfBees, 30, 30);
"""
    replace_once(path, marker, definitions, "queen-bee-item-definitions")
    replace_once(
        path,
        """    private static VanillaItemDefinition EaterWorldDrop(ItemTypeId type, int width, int height) =>""",
        """    private static VanillaItemDefinition QueenBeeWorldDrop(ItemTypeId type, int width, int height) =>
        new(
            Type: type,
            RuntimeDefaults: new VanillaItemRuntimeDefaults(width, height, CommonMaximumStack),
            UseTiming: null,
            Placement: null,
            PickTool: null,
            WorldDrop: new VanillaItemWorldDropDefinition(
                width,
                height,
                NoGravity: false,
                PrefixFamily: VanillaItemPrefixFamily.None));

    private static VanillaItemDefinition EaterWorldDrop(ItemTypeId type, int width, int height) =>""",
        "queen-bee-item-helper")
    marker = """        if (type == VanillaSkeletronItemIds.ChippysWingsInactive) { definition = ChippysWingsInactiveDefinition; return true; }

        definition = default;"""
    lookups = """        if (type == VanillaSkeletronItemIds.ChippysWingsInactive) { definition = ChippysWingsInactiveDefinition; return true; }

        if (type == VanillaQueenBeeItemIds.BeeHat) { definition = QueenBeeHatDefinition; return true; }
        if (type == VanillaQueenBeeItemIds.BeeShirt) { definition = QueenBeeShirtDefinition; return true; }
        if (type == VanillaQueenBeeItemIds.BeePants) { definition = QueenBeePantsDefinition; return true; }
        if (type == VanillaQueenBeeItemIds.BeeGun) { definition = BeeGunDefinition; return true; }
        if (type == VanillaQueenBeeItemIds.BeeKeeper) { definition = BeeKeeperDefinition; return true; }
        if (type == VanillaQueenBeeItemIds.HiveWand) { definition = HiveWandDefinition; return true; }
        if (type == VanillaQueenBeeItemIds.Beenade) { definition = BeenadeDefinition; return true; }
        if (type == VanillaQueenBeeItemIds.HoneyComb) { definition = HoneyCombDefinition; return true; }
        if (type == VanillaQueenBeeItemIds.Nectar) { definition = NectarDefinition; return true; }
        if (type == VanillaQueenBeeItemIds.QueenBeeTrophy) { definition = QueenBeeTrophyDefinition; return true; }
        if (type == VanillaQueenBeeItemIds.BeeMask) { definition = BeeMaskDefinition; return true; }
        if (type == VanillaQueenBeeItemIds.BeeWax) { definition = BeeWaxDefinition; return true; }
        if (type == VanillaQueenBeeItemIds.HoneyedGoggles) { definition = HoneyedGogglesDefinition; return true; }
        if (type == VanillaQueenBeeItemIds.BeesKnees) { definition = BeesKneesDefinition; return true; }
        if (type == VanillaQueenBeeItemIds.QueenBeeBossBag) { definition = QueenBeeBossBagDefinition; return true; }
        if (type == VanillaQueenBeeItemIds.QueenBeePetItem) { definition = QueenBeePetItemDefinition; return true; }
        if (type == VanillaQueenBeeItemIds.QueenBeeMasterTrophy) { definition = QueenBeeMasterTrophyDefinition; return true; }
        if (type == VanillaQueenBeeItemIds.QueenOfBees) { definition = QueenOfBeesDefinition; return true; }

        definition = default;"""
    replace_once(path, marker, lookups, "queen-bee-item-lookups")


def patch_progression() -> None:
    path = "src/TerraRuntime.World/WorldFileProgressionHeaderPatcher.cs"
    replace_once(
        path,
        "        (1UL << (int)VanillaWorldProgressionId.EvilBoss) |\n        (1UL << (int)VanillaWorldProgressionId.Skeletron);",
        "        (1UL << (int)VanillaWorldProgressionId.EvilBoss) |\n        (1UL << (int)VanillaWorldProgressionId.Skeletron) |\n        (1UL << (int)VanillaWorldProgressionId.QueenBee);",
        "queen-bee-progression-mask")
    replace_once(
        path,
        """        int downedBoss3Offset = reader.Offset;
        if (!reader.TryReadBool(out bool persistedDownedBoss3) || !reader.TrySkipBools(7))
            return WorldFileProgressionHeaderPatchResult.InvalidHeader;

        int downedSlimeKingOffset""",
        """        int downedBoss3Offset = reader.Offset;
        if (!reader.TryReadBool(out bool persistedDownedBoss3))
            return WorldFileProgressionHeaderPatchResult.InvalidHeader;
        int downedQueenBeeOffset = reader.Offset;
        if (!reader.TryReadBool(out bool persistedDownedQueenBee) || !reader.TrySkipBools(6))
            return WorldFileProgressionHeaderPatchResult.InvalidHeader;

        int downedSlimeKingOffset""",
        "queen-bee-progression-offset")
    replace_once(
        path,
        "        if (mutations.IsCompleted(VanillaWorldProgressionId.Skeletron) && !persistedDownedBoss3)\n            patchedHeader[downedBoss3Offset] = 1;\n        if (mutations.IsCompleted(VanillaWorldProgressionId.KingSlime)",
        "        if (mutations.IsCompleted(VanillaWorldProgressionId.Skeletron) && !persistedDownedBoss3)\n            patchedHeader[downedBoss3Offset] = 1;\n        if (mutations.IsCompleted(VanillaWorldProgressionId.QueenBee) && !persistedDownedQueenBee)\n            patchedHeader[downedQueenBeeOffset] = 1;\n        if (mutations.IsCompleted(VanillaWorldProgressionId.KingSlime)",
        "queen-bee-progression-write")


def patch_combat() -> None:
    path = "src/TerraRuntime/RuntimeNpcNetworkCombatPipeline.cs"
    replace_once(
        path,
        "    private readonly RuntimeSkeletronLootDeliverySink skeletronLoot;\n    private readonly VanillaNpcLootWorldItemMaterializer materializer",
        "    private readonly RuntimeSkeletronLootDeliverySink skeletronLoot;\n    private readonly RuntimeQueenBeeLootDeliverySink queenBeeLoot;\n    private readonly VanillaNpcLootWorldItemMaterializer materializer",
        "queen-bee-loot-field")
    replace_once(
        path,
        "    private readonly VanillaSkeletronLootPlayer[] activeSkeletronLootPlayers =\n        new VanillaSkeletronLootPlayer[RuntimeNpcPlayerInteractionLedger.VanillaInteractablePlayerSlots];\n    private readonly NpcSnapshot[] npcFamilyBuffer;",
        "    private readonly VanillaSkeletronLootPlayer[] activeSkeletronLootPlayers =\n        new VanillaSkeletronLootPlayer[RuntimeNpcPlayerInteractionLedger.VanillaInteractablePlayerSlots];\n    private readonly VanillaQueenBeeLootPlayer[] activeQueenBeeLootPlayers =\n        new VanillaQueenBeeLootPlayer[RuntimeNpcPlayerInteractionLedger.VanillaInteractablePlayerSlots];\n    private readonly NpcSnapshot[] npcFamilyBuffer;",
        "queen-bee-player-buffer")
    replace_once(
        path,
        "        skeletronLoot = new RuntimeSkeletronLootDeliverySink(\n            worldItems,\n            instancedLeases,\n            worldItemReplication);",
        "        skeletronLoot = new RuntimeSkeletronLootDeliverySink(\n            worldItems,\n            instancedLeases,\n            worldItemReplication);\n        queenBeeLoot = new RuntimeQueenBeeLootDeliverySink(\n            worldItems,\n            instancedLeases,\n            worldItemReplication);",
        "queen-bee-loot-init")
    replace_once(
        path,
        "            else if (dead.TypeIdentity == VanillaNpcIds.SkeletronHead)\n                ApplySkeletronDeathEffects();\n            else if (eaterBoss",
        "            else if (dead.TypeIdentity == VanillaNpcIds.SkeletronHead)\n                ApplySkeletronDeathEffects();\n            else if (dead.TypeIdentity == VanillaNpcIds.QueenBee)\n                ApplyQueenBeeDeathEffects();\n            else if (eaterBoss",
        "queen-bee-packet-death")
    replace_once(
        path,
        "        else if (dead.TypeIdentity == VanillaNpcIds.SkeletronHead)\n            ApplySkeletronDeathEffects();\n        else if (eaterBoss",
        "        else if (dead.TypeIdentity == VanillaNpcIds.SkeletronHead)\n            ApplySkeletronDeathEffects();\n        else if (dead.TypeIdentity == VanillaNpcIds.QueenBee)\n            ApplyQueenBeeDeathEffects();\n        else if (eaterBoss",
        "queen-bee-melee-death")
    replace_once(
        path,
        "        if (npc.TypeIdentity == VanillaNpcIds.SkeletronHead)\n            return TryExecuteSkeletronLoot(in npc);\n\n        if (npc.TypeIdentity == VanillaNpcIds.KingSlime",
        "        if (npc.TypeIdentity == VanillaNpcIds.SkeletronHead)\n            return TryExecuteSkeletronLoot(in npc);\n        if (npc.TypeIdentity == VanillaNpcIds.QueenBee)\n            return TryExecuteQueenBeeLoot(in npc);\n\n        if (npc.TypeIdentity == VanillaNpcIds.KingSlime",
        "queen-bee-loot-route")
    marker = """    private void MarkSkeletronInteraction(PlayerHandle player)
    {"""
    method = """    private bool TryExecuteQueenBeeLoot(in NpcSnapshot npc)
    {
        if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.QueenBee, out VanillaNpcDefinition definition))
            return false;

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player))
                continue;
            activeQueenBeeLootPlayers[activeCount++] = new VanillaQueenBeeLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = new NpcLootWorldItemOrigin(
            (int)npc.PositionX + definition.Width * 0.5f,
            (int)npc.PositionY + definition.Height * 0.5f);
        var context = new VanillaQueenBeeLootContext(expertMode, masterMode);
        return VanillaQueenBeeLootEvaluator.TryExecute(
            in context,
            in origin,
            activeQueenBeeLootPlayers.AsSpan(0, activeCount),
            random,
            queenBeeLoot,
            out _);
    }

    private void MarkSkeletronInteraction(PlayerHandle player)
    {"""
    replace_once(path, marker, method, "queen-bee-loot-method")
    replace_once(
        path,
        """    private void ApplySkeletronDeathEffects()
    {
        if (worldTiles is null)
            return;
        RuntimeWorldProgressionRegistry.GetOrCreate(worldTiles)
            .MarkCompleted(VanillaWorldProgressionId.Skeletron);
    }

    private void ApplyEvilBossDeathEffects()""",
        """    private void ApplySkeletronDeathEffects()
    {
        if (worldTiles is null)
            return;
        RuntimeWorldProgressionRegistry.GetOrCreate(worldTiles)
            .MarkCompleted(VanillaWorldProgressionId.Skeletron);
    }

    private void ApplyQueenBeeDeathEffects()
    {
        if (worldTiles is null)
            return;
        RuntimeWorldProgressionRegistry.GetOrCreate(worldTiles)
            .MarkCompleted(VanillaWorldProgressionId.QueenBee);
    }

    private void ApplyEvilBossDeathEffects()""",
        "queen-bee-death-effect")
    replace_once(
        path,
        "            !float.IsFinite(intent.VelocityY) ||\n            !intent.InitialAi.IsFinite)",
        "            !float.IsFinite(intent.VelocityY) ||\n            !intent.InitialAi.IsFinite ||\n            !intent.InitialLocalAi.IsFinite)",
        "combat-spawn-local-ai-validation")
    replace_once(
        path,
        "                TimeLeft = VanillaNpcSpawnFacts.NewNpcTimeLeft\n            });\n        return npcs.TrySpawnVanilla",
        "                TimeLeft = VanillaNpcSpawnFacts.NewNpcTimeLeft,\n                LocalAi = intent.InitialLocalAi\n            });\n        return npcs.TrySpawnVanilla",
        "combat-spawn-local-ai")


def patch_server_composition() -> None:
    path = "src/TerraRuntime/ServerRuntimeState.cs"
    replace_once(
        path,
        "                _vanillaNpcTargetingAiStepper.SetFlyingEyeEnvironment(new VanillaFlyingEyeWorldEnvironment(worldTiles));\n                _vanillaNpcTargetingAiStepper.SetProjectileEnvironment(new VanillaNpcProjectileWorldEnvironment(worldTiles));",
        "                _vanillaNpcTargetingAiStepper.SetFlyingEyeEnvironment(new VanillaFlyingEyeWorldEnvironment(worldTiles));\n                _vanillaNpcTargetingAiStepper.SetQueenBeeEnvironment(new VanillaQueenBeeWorldEnvironment(\n                    worldTiles,\n                    worldSurfaceTiles,\n                    townCommerceWorldFacts?.RemixWorld ?? false));\n                _vanillaNpcTargetingAiStepper.SetProjectileEnvironment(new VanillaNpcProjectileWorldEnvironment(worldTiles));",
        "queen-bee-production-environment")


def patch_coverage_docs() -> None:
    path = "src/TerraRuntime.Core/Npcs/VanillaNpcAiCoverageCatalog.cs"
    replace_once(
        path,
        "    BossDeathLootProgressionSlice = 1ul << 37\n}",
        "    BossDeathLootProgressionSlice = 1ul << 37,\n    QueenBeeStateSlice = 1ul << 38,\n    QueenBeeMinionSpawnSlice = 1ul << 39,\n    QueenBeeStingerProjectileSlice = 1ul << 40\n}",
        "queen-bee-capabilities")
    replace_once(path, "            11 +\n", "            12 +\n", "queen-bee-coverage-count")
    replace_once(
        path,
        """        entries[10] = Partial(
            VanillaNpcIds.SkeletronHand,
            OrdinaryCore |
            VanillaNpcAiCapability.SkeletronHandStateSlice);

        int index = 11;""",
        """        entries[10] = Partial(
            VanillaNpcIds.SkeletronHand,
            OrdinaryCore |
            VanillaNpcAiCapability.SkeletronHandStateSlice);
        entries[11] = Partial(
            VanillaNpcIds.QueenBee,
            OrdinaryCore |
            VanillaNpcAiCapability.ChildSpawnSlice |
            VanillaNpcAiCapability.QueenBeeStateSlice |
            VanillaNpcAiCapability.QueenBeeMinionSpawnSlice |
            VanillaNpcAiCapability.QueenBeeStingerProjectileSlice |
            VanillaNpcAiCapability.BossDeathLootProgressionSlice);

        int index = 12;""",
        "queen-bee-coverage-entry")
    replace_once(
        "tests/TerraRuntime.Tests/VanillaNpcAiCoverageCatalogTests.cs",
        "        int expected = 11 +\n",
        "        int expected = 12 +\n",
        "queen-bee-coverage-test")
    roadmap = "docs/roadmap/npc-ai-parity.md"
    replace_once(
        roadmap,
        "| Skeletron | yes | partial | partial | head/hand ownership, Expert skull homing/lifetime, death/progression and source-ordered loot | no |\n| 23 additional hostile AI_001 types",
        "| Skeletron | yes | partial | partial | head/hand ownership, Expert skull homing/lifetime, death/progression and source-ordered loot | no |\n| Queen Bee | yes | partial | partial | AI_043 attack cycle, Jungle/surface/Good World enrage, Bee/SmallBee ownership, stinger 719, death/progression and source-ordered loot | no |\n| 23 additional hostile AI_001 types",
        "queen-bee-roadmap-table")
    replace_once(
        roadmap,
        "- [x] add Skeletron gameplay vertical: source-backed head/hand ownership, Expert skull cadence/homing/lifetime, shared head/hand interaction credit, Classic/Expert/Master loot, isolated RedHat-condition evaluator coverage and persisted `downedBoss3` progression;\n- [ ] add remaining pre-Hardmode bosses with complete child/projectile ownership;",
        "- [x] add Skeletron gameplay vertical: source-backed head/hand ownership, Expert skull cadence/homing/lifetime, shared head/hand interaction credit, Classic/Expert/Master loot, isolated RedHat-condition evaluator coverage and persisted `downedBoss3` progression;\n- [x] add Queen Bee gameplay vertical: AI_043 attack cycle, source-shaped Jungle/surface/Good World enrage, Bee/SmallBee spawn ownership with localAI seed, stinger 719 lifetime, Classic/Expert/Master loot and persisted `downedQueenBee`;\n- [ ] add remaining pre-Hardmode bosses with complete child/projectile ownership;",
        "queen-bee-roadmap-n3")


def fix_loot_roll_order() -> None:
    path = "src/TerraRuntime.Core/Npcs/VanillaQueenBeeLoot.cs"
    replace_once(
        path,
        "            DropGuaranteed(VanillaQueenBeeItemIds.QueenBeeMasterTrophy, in npcOrigin, rolls, sink, ref worldItems);",
        "            Roll(VanillaQueenBeeItemIds.QueenBeeMasterTrophy, 1, 1, 1, 1, in npcOrigin, rolls, sink, ref worldItems);",
        "queen-bee-master-relic-roll-order")


def write_tests() -> None:
    Path("tests/TerraRuntime.Tests/VanillaQueenBeeVerticalTests.cs").write_text(r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaQueenBeeVerticalTests
{
    [Fact]
    public void Definition_and_stinger_are_source_backed()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.QueenBee, out VanillaNpcDefinition queen));
        Assert.Equal(222, queen.Type.Value);
        Assert.Equal(43, queen.AiStyle.Value);
        Assert.Equal((66, 66, 30, 8, 3400), (queen.Width, queen.Height, queen.Damage, queen.Defense, queen.LifeMax));
        Assert.Equal(VanillaNpcBehaviorFamily.QueenBee, queen.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.NoClipFlight, queen.PhysicsFamily);
        Assert.True(queen.NoGravityAtSpawn);
        Assert.True(queen.NoTileCollideAtSpawn);

        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(VanillaProjectileIds.QueenBeeStinger, out VanillaProjectileDefinition stinger));
        Assert.Equal((10, 10), (stinger.Width, stinger.Height));
        Assert.Equal(VanillaProjectileAiStyles.Arrow, stinger.AiStyle);
        Assert.True(stinger.TileCollide);
        Assert.True(stinger.CanCutTiles);
        Assert.True(VanillaProjectileFacts.IsHostile(VanillaProjectileIds.QueenBeeStinger));
    }

    [Fact]
    public void Enrage_and_expert_cadence_match_source_thresholds()
    {
        NpcSnapshot queen = Queen(life: 300, lifeMax: 3400, y: 200f, ai0: 3f, ai1: 0f);
        var target = new VanillaNpcTargetCandidate(7, 500f, 500f, 0, true, false, false, false);
        var context = new VanillaNpcBehaviorContext();
        context.SetWorldConditions(dayTime: true, slimeRainActive: false, goodWorld: true, expertMode: true);
        var environment = new FakeQueenEnvironment(worldSurfacePixels: 400d, jungle: false);
        Assert.Equal(2.5f, VanillaQueenBeeNpcBehaviorStrategy.ComputeEnrage(in queen, in target, context, environment));
        Assert.Equal(3, VanillaQueenBeeNpcBehaviorStrategy.GetStingerCadence(in queen, expertMode: true, enrage: 2.5f));
        Assert.Equal(-5, VanillaQueenBeeNpcBehaviorStrategy.GetBeeSummonThreshold(2.5f));
    }

    [Fact]
    public void Bee_spawn_intent_carries_source_local_ai_seed()
    {
        var random = new QueueRandom(0, 210);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false);
        stepper.SetQueenBeeEnvironment(new FakeQueenEnvironment(1000d, jungle: true));
        stepper.SetProjectileEnvironment(new AlwaysHitEnvironment());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 400f, 400f, 0, true, false, false, false)]);
        NpcSnapshot source = Queen(life: 3400, lifeMax: 3400, y: 300f, ai0: 1f, ai1: 40f, ai2: 0f);
        source = source with { Target = 7 };
        var proposed = new NpcStateUpdate(source.Type, source.NetId, source.PositionX, source.PositionY, 0f, 0f, 7,
            source.Ai with { Ai1 = 0f, Ai2 = 1f }, source.Simulation);
        Span<NpcAiSpawnIntent> intents = stackalloc NpcAiSpawnIntent[2];
        Assert.Equal(1, stepper.PlanNpcSpawns(in source, in proposed, intents));
        Assert.True(intents[0].Type == VanillaNpcIds.Bee || intents[0].Type == VanillaNpcIds.SmallBee);
        Assert.Equal(60f, intents[0].InitialLocalAi.Ai0);
    }

    [Fact]
    public void Stinger_intent_is_server_owned_damage_11_and_300_ticks()
    {
        var random = new QueueRandom(0, 0, 0);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false);
        stepper.SetQueenBeeEnvironment(new FakeQueenEnvironment(1000d, jungle: true));
        stepper.SetProjectileEnvironment(new AlwaysHitEnvironment());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 400f, 600f, 0, true, false, false, false)]);
        NpcSnapshot source = Queen(life: 3400, lifeMax: 3400, y: 100f, ai0: 3f, ai1: 38f);
        source = source with { Target = 7 };
        var proposed = new NpcStateUpdate(source.Type, source.NetId, source.PositionX, source.PositionY, 0f, 0f, 7,
            source.Ai with { Ai1 = 39f }, source.Simulation);
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[2];
        Assert.Equal(1, stepper.PlanProjectileSpawns(in source, in proposed, intents));
        Assert.Equal(VanillaProjectileIds.QueenBeeStinger, intents[0].Type);
        Assert.Equal(11, intents[0].Damage);
        Assert.Equal(300, intents[0].TimeLeftOverride);
    }

    private static NpcSnapshot Queen(int life, int lifeMax, float y, float ai0, float ai1, float ai2 = 0f) =>
        new(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), 222, 222, 100f, y, 0f, 0f, 255,
            new NpcAiState(ai0, ai1, ai2, 0f), NpcSimulationState.Initial with { Life = life, LifeMax = lifeMax, TimeLeft = 750 });

    private sealed class FakeQueenEnvironment(double worldSurfacePixels, bool jungle) : IVanillaQueenBeeEnvironment
    {
        public double WorldSurfacePixels => worldSurfacePixels;
        public float WorldCenterX => 4200f;
        public bool IsPlayerInJungle(float playerCenterX, float playerCenterY) => jungle;
    }

    private sealed class AlwaysHitEnvironment : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(float a, float b, int c, int d, float e, float f, int g, int h) => true;
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }

    private sealed class QueueRandom(params int[] values) : IVanillaNpcRandom
    {
        private readonly Queue<int> values = new(values);
        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            if (values.Count == 0) return inclusiveMin;
            int value = values.Dequeue();
            return Math.Clamp(value, inclusiveMin, exclusiveMax - 1);
        }
    }
}
''', encoding="utf-8")

    Path("tests/TerraRuntime.Tests/QueenBeeDeathProgressionTests.cs").write_text(r'''using System.Reflection;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class QueenBeeDeathProgressionTests
{
    [Fact]
    public void Progression_header_patcher_sets_downed_queen_bee_and_keeps_world_loadable()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.False(source.RuntimeMetadata.DownedQueenBee);
        Assert.True(WorldFilePreservedSections.TryCapture(sourceFile, source.Envelope, out WorldFilePreservedSections? preserved));
        var mutations = new RuntimeWorldProgressionMutations();
        Assert.True(mutations.MarkCompleted(VanillaWorldProgressionId.QueenBee));
        RuntimeWorldProgressionMutationSnapshot snapshot = mutations.CaptureSnapshot();
        byte[] originalHeader = preserved!.Header.ToArray();
        Assert.Equal(WorldFileProgressionHeaderPatchResult.Patched,
            WorldFileProgressionHeaderPatcher.TryPatch(originalHeader, source.Header, in snapshot, out byte[] patchedHeader));
        Assert.Equal(1, originalHeader.Zip(patchedHeader).Count(pair => pair.First != pair.Second));
        byte[] patchedFile = sourceFile.ToArray();
        patchedHeader.CopyTo(patchedFile.AsSpan(source.Envelope.SectionOffsets[0], patchedHeader.Length));
        Assert.True(WorldFileLoader.TryLoad(patchedFile, limits, out WorldFileData? loadedWorld).IsLoaded);
        WorldFileData loaded = Assert.IsType<WorldFileData>(loadedWorld);
        Assert.True(loaded.RuntimeMetadata.DownedQueenBee);
        Assert.Equal(source.RuntimeMetadata.DownedBoss3, loaded.RuntimeMetadata.DownedBoss3);
        Assert.Equal(source.RuntimeMetadata.DownedMechBoss1, loaded.RuntimeMetadata.DownedMechBoss1);
    }

    private static T LoaderFixture<T>(string methodName)
    {
        MethodInfo? method = typeof(WorldFileLoaderTests).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(null, null));
    }
}
''', encoding="utf-8")

    Path("tests/TerraRuntime.Tests/VanillaQueenBeeLootTests.cs").write_text(r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaQueenBeeLootTests
{
    [Fact]
    public void Classic_source_order_includes_three_quarter_beenades_and_guaranteed_wax()
    {
        var random = new ScriptedRolls();
        var sink = new RecordingSink();
        var context = new VanillaQueenBeeLootContext(false, false);
        Assert.True(VanillaQueenBeeLootEvaluator.TryExecute(in context, new NpcLootWorldItemOrigin(100, 100), [], random, sink, out QueenBeeLootExecutionResult result));
        Assert.Contains(sink.WorldDrops, d => d.ItemType == VanillaQueenBeeItemIds.BeeWax && d.Stack is >= 17 and <= 30);
        Assert.Contains(sink.WorldDrops, d => d.ItemType == VanillaQueenBeeItemIds.Beenade && d.Stack is >= 10 and <= 30);
        Assert.True(result.WorldItemCount >= 3);
    }

    [Theory]
    [InlineData(842,28,20)] [InlineData(843,18,14)] [InlineData(844,18,14)] [InlineData(1121,50,18)]
    [InlineData(1123,40,40)] [InlineData(1129,8,10)] [InlineData(1130,10,10)] [InlineData(1132,22,22)]
    [InlineData(1170,16,30)] [InlineData(1364,30,30)] [InlineData(2108,28,20)] [InlineData(2431,18,16)]
    [InlineData(2502,16,30)] [InlineData(2888,12,28)] [InlineData(3322,24,24)] [InlineData(4802,16,30)]
    [InlineData(4928,14,14)] [InlineData(5483,30,30)]
    public void Queen_bee_loot_items_have_materializable_source_dimensions(int raw, int width, int height)
    {
        var type = new ItemTypeId(raw);
        Assert.True(VanillaItemDefinitionCatalog.TryGetRuntimeDefaults(type, out VanillaItemRuntimeDefaults defaults));
        Assert.Equal((width, height), (defaults.Width, defaults.Height));
        Assert.True(VanillaItemDefinitionCatalog.TryGetWorldDrop(type, out _));
    }

    private sealed class ScriptedRolls : INpcLootRollSource
    {
        public int RollLuck(int chanceDenominator) => 0;
        public int NextInt32(int inclusiveMin, int exclusiveMax) => inclusiveMin;
    }

    private sealed class RecordingSink : IQueenBeeLootDeliverySink
    {
        public List<NpcLootDrop> WorldDrops { get; } = [];
        public bool CanDeliverInstanced(ItemTypeId itemType) => true;
        public bool CanDeliverWorldItem(ItemTypeId itemType) => true;
        public bool TryDeliverInstanced(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, ReadOnlySpan<VanillaQueenBeeLootPlayer> recipients, int slotLeaseTicks, INpcLootRollSource random) => true;
        public bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random) { WorldDrops.Add(drop); return true; }
    }
}
''', encoding="utf-8")


def main() -> None:
    patch_content_ids()
    patch_definition_catalog()
    patch_behavior_context()
    patch_spawn_intents()
    patch_targeting_stepper()
    patch_projectile_catalogs()
    patch_items()
    patch_progression()
    patch_combat()
    patch_server_composition()
    patch_coverage_docs()
    fix_loot_roll_order()
    write_tests()
    print("queen_bee_vertical_1458=staged")


if __name__ == "__main__":
    main()
