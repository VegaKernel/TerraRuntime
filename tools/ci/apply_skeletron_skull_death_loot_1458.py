from pathlib import Path


def replace_once(path: str, old: str, new: str, label: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"{label} anchor changed in {path}")
    file.write_text(text.replace(old, new, 1), encoding="utf-8")


def patch_projectile_intents() -> None:
    path = "src/TerraRuntime.Core/Npcs/RuntimeNpcAiProjectileIntents.cs"
    replace_once(
        path,
        "public readonly record struct NpcAiProjectileIntent(\n    ProjectileTypeId Type,\n    float PositionX,\n    float PositionY,\n    float VelocityX,\n    float VelocityY,\n    int Damage,\n    float KnockBack);",
        """public readonly record struct NpcAiProjectileIntent(
    ProjectileTypeId Type,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    int Damage,
    float KnockBack)
{
    /// <summary>Source-owned initial projectile AI state applied atomically with allocation.</summary>
    public ProjectileAiState InitialAi { get; init; }

    /// <summary>Positive SetDefaults lifetime override applied atomically with allocation; zero keeps catalog defaults.</summary>
    public int TimeLeftOverride { get; init; }
}""",
        "projectile-intent-init",
    )
    replace_once(
        path,
        """            intent.Damage < 0 ||
            intent.Damage > short.MaxValue)""",
        """            intent.Damage < 0 ||
            intent.Damage > short.MaxValue ||
            !float.IsFinite(intent.InitialAi.Ai0) ||
            !float.IsFinite(intent.InitialAi.Ai1) ||
            !float.IsFinite(intent.InitialAi.Ai2) ||
            intent.TimeLeftOverride < 0)""",
        "projectile-intent-validation",
    )
    replace_once(
        path,
        """            intent.VelocityY,
            default,
            BannerIdToRespondTo: 0,""",
        """            intent.VelocityY,
            intent.InitialAi,
            BannerIdToRespondTo: 0,""",
        "projectile-intent-ai",
    )
    replace_once(
        path,
        "        return projectiles.TrySpawnVanilla(in update, out spawned);",
        "        return projectiles.TrySpawnVanilla(in update, intent.TimeLeftOverride > 0 ? intent.TimeLeftOverride : null, out spawned);",
        "projectile-intent-lifetime",
    )


def patch_projectile_store() -> None:
    path = "src/TerraRuntime.Core/Projectiles/RuntimeProjectileStore.cs"
    old = """    public bool TrySpawnVanilla(in ProjectileStateUpdate update, out ProjectileSnapshot snapshot)
    {
        if (!IsValidState(in update) ||
            !TryCreateLifecycle(update.Type, out ProjectileLifecycleState lifecycle) ||
            !TrySelectVanillaAllocationSlot(out ushort slot))
        {
            snapshot = default;
            return false;
        }

        ref SlotState state = ref _slots[slot];
        if (!TryAdvance(ref state.Generation))
        {
            snapshot = default;
            return false;
        }

        bool wasActive = state.Active;
        InitializeSlot(ref state, in update, in lifecycle);
        if (!wasActive)
            _activeCount++;

        snapshot = Capture(slot, in state);
        _commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in snapshot);
        return true;
    }"""
    new = """    public bool TrySpawnVanilla(in ProjectileStateUpdate update, out ProjectileSnapshot snapshot) =>
        TrySpawnVanilla(in update, timeLeftOverride: null, out snapshot);

    /// <summary>
    /// Applies NewProjectileSetup allocation while allowing a source-owned positive lifetime override to be
    /// committed with the spawn generation. This avoids inventing a second Update commit merely to reproduce
    /// NPC code that assigns projectile.timeLeft immediately after NewProjectile.
    /// </summary>
    public bool TrySpawnVanilla(
        in ProjectileStateUpdate update,
        int? timeLeftOverride,
        out ProjectileSnapshot snapshot)
    {
        if (timeLeftOverride is <= 0 ||
            !IsValidState(in update) ||
            !TryCreateLifecycle(update.Type, out ProjectileLifecycleState lifecycle))
        {
            snapshot = default;
            return false;
        }

        if (timeLeftOverride is int sourceTimeLeft)
            lifecycle = lifecycle with { TimeLeft = sourceTimeLeft };

        if (!TrySelectVanillaAllocationSlot(out ushort slot))
        {
            snapshot = default;
            return false;
        }

        ref SlotState state = ref _slots[slot];
        if (!TryAdvance(ref state.Generation))
        {
            snapshot = default;
            return false;
        }

        bool wasActive = state.Active;
        InitializeSlot(ref state, in update, in lifecycle);
        if (!wasActive)
            _activeCount++;

        snapshot = Capture(slot, in state);
        _commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in snapshot);
        return true;
    }"""
    replace_once(path, old, new, "projectile-store-lifetime-overload")


def patch_projectile_definition() -> None:
    path = "src/TerraRuntime.Contracts/Gameplay/VanillaProjectileDefinitionCatalog.cs"
    replace_once(
        path,
        """    private static readonly VanillaProjectileDefinition BoneDefinition = new(
        Width: 16,""",
        """    private static readonly VanillaProjectileDefinition SkeletronSkullDefinition = new(
        Width: 26,
        Height: 26,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 26,
        CollisionHeight: 26);

    private static readonly VanillaProjectileDefinition BoneDefinition = new(
        Width: 16,""",
        "skeletron-skull-definition",
    )
    replace_once(
        path,
        """        if (type == VanillaProjectileIds.Bone)
        {
            definition = BoneDefinition;""",
        """        if (type == VanillaProjectileIds.SkeletronSkull)
        {
            definition = SkeletronSkullDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.Bone)
        {
            definition = BoneDefinition;""",
        "skeletron-skull-definition-lookup",
    )


def patch_projectile_profile() -> None:
    path = "src/TerraRuntime/VanillaProjectileBehaviorProfile.cs"
    replace_once(
        path,
        """    Thrown = 2,
    Boomerang = 3""",
        """    Thrown = 2,
    Boomerang = 3,
    SkeletronSkull = 4""",
        "skeletron-skull-family",
    )
    replace_once(
        path,
        """    private static readonly VanillaProjectileBehaviorProfile GreenLaserProfile = BasicArrowProfile with
    {
        RejectServerOwned = true
    };

    private static readonly VanillaProjectileBehaviorProfile ThrownProfile""",
        """    private static readonly VanillaProjectileBehaviorProfile GreenLaserProfile = BasicArrowProfile with
    {
        RejectServerOwned = true
    };

    private static readonly VanillaProjectileBehaviorProfile SkeletronSkullProfile = new(
        VanillaProjectileBehaviorFamily.SkeletronSkull,
        VanillaProjectileAiStyles.Arrow,
        BehaviorImplemented: true,
        RequiresDefaultAi2: false,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile ThrownProfile""",
        "skeletron-skull-profile",
    )
    replace_once(
        path,
        """        if (type == VanillaProjectileIds.GreenLaser)
        {
            profile = GreenLaserProfile;
            return true;
        }

        if (IsBasicArrow(type))""",
        """        if (type == VanillaProjectileIds.GreenLaser)
        {
            profile = GreenLaserProfile;
            return true;
        }

        if (type == VanillaProjectileIds.SkeletronSkull)
        {
            profile = SkeletronSkullProfile;
            return true;
        }

        if (IsBasicArrow(type))""",
        "skeletron-skull-profile-lookup",
    )


def patch_projectile_behavior() -> None:
    path = "src/TerraRuntime/VanillaProjectileBehaviorStepper.cs"
    replace_once(
        path,
        "using TerraRuntime.Contracts.Runtime;\n",
        "using TerraRuntime.Contracts.Runtime;\nusing TerraRuntime.Core;\n",
        "projectile-behavior-core-using",
    )
    replace_once(
        path,
        """internal readonly record struct VanillaProjectileBehaviorContext(
    bool WindPhysics,
    float WindSpeedCurrent,
    float WindPhysicsStrength);""",
        """internal readonly record struct VanillaProjectileBehaviorContext(
    bool WindPhysics,
    float WindSpeedCurrent,
    float WindPhysicsStrength,
    IRuntimePlayerSlotSnapshotLookup? PlayerSnapshots = null);""",
        "projectile-player-context",
    )
    replace_once(
        path,
        """internal readonly record struct VanillaProjectileBehaviorResult(
    float VelocityX,
    float VelocityY,
    float Ai0);""",
        """internal readonly record struct VanillaProjectileBehaviorResult(
    float VelocityX,
    float VelocityY,
    float Ai0,
    float? Ai1Override = null);""",
        "projectile-ai1-result",
    )
    replace_once(
        path,
        """        float velocityX = current.VelocityX;
        float velocityY = current.VelocityY;
        float ai0 = current.Ai.Ai0;

        switch (profile.Family)""",
        """        float velocityX = current.VelocityX;
        float velocityY = current.VelocityY;
        float ai0 = current.Ai.Ai0;
        float? ai1Override = null;

        switch (profile.Family)""",
        "projectile-ai1-local",
    )
    replace_once(
        path,
        """            case VanillaProjectileBehaviorFamily.BasicArrow:
                // TerrariaServer 1.4.5.8 Projectile.AI_001(), source-backed basic aiStyle-1 path.
                ai0 += 1f;
                if (ai0 >= 15f)
                {
                    ai0 = 15f;
                    velocityY += 0.1f;
                }

                if (velocityY > MaximumArrowFallSpeed)
                    velocityY = MaximumArrowFallSpeed;
                break;

            default:""",
        """            case VanillaProjectileBehaviorFamily.BasicArrow:
                // TerrariaServer 1.4.5.8 Projectile.AI_001(), source-backed basic aiStyle-1 path.
                ai0 += 1f;
                if (ai0 >= 15f)
                {
                    ai0 = 15f;
                    velocityY += 0.1f;
                }

                if (velocityY > MaximumArrowFallSpeed)
                    velocityY = MaximumArrowFallSpeed;
                break;

            case VanillaProjectileBehaviorFamily.SkeletronSkull:
                float ai1 = current.Ai.Ai1 + 1f;
                ai1Override = ai1;
                float speed = MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
                if (ai1 > 30f && ai1 < 110f && speed > 0f &&
                    TryFindClosestPlayer(in current, in definition, context.PlayerSnapshots, out float targetX, out float targetY))
                {
                    float centerX = current.PositionX + definition.Width * 0.5f;
                    float centerY = current.PositionY + definition.Height * 0.5f;
                    float dx = targetX - centerX;
                    float dy = targetY - centerY;
                    float distance = MathF.Sqrt(dx * dx + dy * dy);
                    if (distance > 0f)
                    {
                        float desiredX = dx / distance * speed;
                        float desiredY = dy / distance * speed;
                        velocityX = (velocityX * 24f + desiredX) / 25f;
                        velocityY = (velocityY * 24f + desiredY) / 25f;
                        float blendedSpeed = MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
                        if (blendedSpeed > 0f)
                        {
                            velocityX = velocityX / blendedSpeed * speed;
                            velocityY = velocityY / blendedSpeed * speed;
                        }
                    }
                }

                if (MathF.Sqrt(velocityX * velocityX + velocityY * velocityY) < 18f)
                {
                    velocityX *= 1.02f;
                    velocityY *= 1.02f;
                }
                break;

            default:""",
        "skeletron-skull-behavior",
    )
    replace_once(
        path,
        """        next = new VanillaProjectileBehaviorResult(velocityX, velocityY, ai0);
        return true;
    }
}""",
        """        next = new VanillaProjectileBehaviorResult(velocityX, velocityY, ai0, ai1Override);
        return true;
    }

    private static bool TryFindClosestPlayer(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition definition,
        IRuntimePlayerSlotSnapshotLookup? players,
        out float centerX,
        out float centerY)
    {
        centerX = 0f;
        centerY = 0f;
        if (players is null)
            return false;

        float projectileCenterX = projectile.PositionX + definition.Width * 0.5f;
        float projectileCenterY = projectile.PositionY + definition.Height * 0.5f;
        float bestDistanceSquared = float.PositiveInfinity;
        bool found = false;
        for (int rawSlot = 0; rawSlot < byte.MaxValue; rawSlot++)
        {
            var slot = new PlayerSlotId(checked((byte)rawSlot));
            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player) || player.IsDead)
                continue;

            float playerCenterX = player.PositionX + 10f;
            float playerCenterY = player.PositionY + 21f;
            float dx = playerCenterX - projectileCenterX;
            float dy = playerCenterY - projectileCenterY;
            float distanceSquared = dx * dx + dy * dy;
            if (distanceSquared >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            centerX = playerCenterX;
            centerY = playerCenterY;
            found = true;
        }
        return found;
    }
}""",
        "skeletron-skull-player-targeting",
    )


def patch_projectile_world() -> None:
    replace_once(
        "src/TerraRuntime/VanillaProjectileWorldMotionResolver.cs",
        "new ProjectileAiState(behavior.Ai0, current.Ai.Ai1, current.Ai.Ai2)",
        "new ProjectileAiState(behavior.Ai0, behavior.Ai1Override ?? current.Ai.Ai1, current.Ai.Ai2)",
        "projectile-world-ai1-commit",
    )
    path = "src/TerraRuntime/VanillaProjectileWorldStateStepper.cs"
    replace_once(
        path,
        """    private readonly VanillaProjectileWorldMotionResolver worldMotion;
    private bool windPhysics;""",
        """    private readonly VanillaProjectileWorldMotionResolver worldMotion;
    private readonly IRuntimePlayerSlotSnapshotLookup? playerSnapshots;
    private bool windPhysics;""",
        "projectile-world-player-field",
    )
    replace_once(
        path,
        """    public VanillaProjectileWorldStateStepper(WorldTileStore tiles)
    {
        worldMotion = new VanillaProjectileWorldMotionResolver(
            tiles ?? throw new ArgumentNullException(nameof(tiles)));
    }""",
        """    public VanillaProjectileWorldStateStepper(
        WorldTileStore tiles,
        IRuntimePlayerSlotSnapshotLookup? playerSnapshots = null)
    {
        worldMotion = new VanillaProjectileWorldMotionResolver(
            tiles ?? throw new ArgumentNullException(nameof(tiles)));
        this.playerSnapshots = playerSnapshots;
    }""",
        "projectile-world-player-constructor",
    )
    replace_once(
        path,
        """        var behaviorContext = new VanillaProjectileBehaviorContext(
            windPhysics,
            windSpeedCurrent,
            windPhysicsStrength);""",
        """        var behaviorContext = new VanillaProjectileBehaviorContext(
            windPhysics,
            windSpeedCurrent,
            windPhysicsStrength,
            playerSnapshots);""",
        "projectile-world-player-context",
    )
    replace_once(
        "src/TerraRuntime/ServerRuntimeState.cs",
        "(worldTiles is null ? null : new VanillaProjectileWorldStateStepper(worldTiles));",
        "(worldTiles is null ? null : new VanillaProjectileWorldStateStepper(worldTiles, this));",
        "production-projectile-player-lookup",
    )


def patch_skeletron_npc_projectile() -> None:
    path = "src/TerraRuntime.Core/Npcs/VanillaNpcTargetingAiStepper.cs"
    replace_once(
        path,
        """    private readonly VanillaSkeletronHandNpcBehaviorStrategy _skeletronHand = new();
    private readonly IVanillaNpcRandom _random;""",
        """    private readonly VanillaSkeletronHandNpcBehaviorStrategy _skeletronHand = new();
    private readonly IVanillaNpcRandom _random;
    private IVanillaNpcProjectileEnvironment? _projectileEnvironment;""",
        "skeletron-projectile-environment-field",
    )
    replace_once(
        path,
        """    public void SetProjectileEnvironment(IVanillaNpcProjectileEnvironment environment) =>
        _flyer.SetProjectileEnvironment(environment);""",
        """    public void SetProjectileEnvironment(IVanillaNpcProjectileEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _projectileEnvironment = environment;
        _flyer.SetProjectileEnvironment(environment);
    }""",
        "skeletron-projectile-environment-setter",
    )
    replace_once(
        path,
        """    public int PlanProjectileSpawns(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiProjectileIntent> destination) =>
        _flyer.PlanProjectileSpawns(in source, in proposed, _context, destination);

    private int PlanSkeletronHands(""",
        """    public int PlanProjectileSpawns(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        Span<NpcAiProjectileIntent> destination)
    {
        if (source.Type == VanillaNpcIds.SkeletronHead.Value && proposed.Type == source.Type)
            return PlanSkeletronSkull(in source, in proposed, destination);

        return _flyer.PlanProjectileSpawns(in source, in proposed, _context, destination);
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
        float targetX = target.CenterX - VanillaNpcBehaviorContext.BasePlayerWidth * 0.5f;
        float targetY = target.CenterY - VanillaNpcBehaviorContext.BasePlayerHeight * 0.5f;
        if (!_projectileEnvironment.CanHit(
                centerX, centerY, 1, 1,
                targetX, targetY,
                (int)VanillaNpcBehaviorContext.BasePlayerWidth,
                (int)VanillaNpcBehaviorContext.BasePlayerHeight))
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

    private int PlanSkeletronHands(""",
        "skeletron-skull-planner",
    )


def patch_item_catalog() -> None:
    path = "src/TerraRuntime.Core/Items/VanillaItemDefinitionCatalog.cs"
    replace_once(
        path,
        """    private static readonly VanillaItemDefinition BrainMasterTrophyDefinition =
        BrainWorldDrop(VanillaBrainOfCthulhuItemIds.BrainOfCthulhuMasterTrophy, 14, 14);

    private static VanillaItemDefinition BrainWorldDrop""",
        """    private static readonly VanillaItemDefinition BrainMasterTrophyDefinition =
        BrainWorldDrop(VanillaBrainOfCthulhuItemIds.BrainOfCthulhuMasterTrophy, 14, 14);

    private static readonly VanillaItemDefinition SkeletronHandDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.SkeletronHand, 30, 10);
    private static readonly VanillaItemDefinition SkeletronMaskDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.SkeletronMask, 28, 20);
    private static readonly VanillaItemDefinition BookOfSkullsDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.BookOfSkulls, 24, 28);
    private static readonly VanillaItemDefinition SkeletronTrophyDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.SkeletronTrophy, 30, 30);
    private static readonly VanillaItemDefinition SkeletronBossBagDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.SkeletronBossBag, 24, 24);
    private static readonly VanillaItemDefinition SkeletronPetItemDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.SkeletronPetItem, 16, 30);
    private static readonly VanillaItemDefinition SkeletronMasterTrophyDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.SkeletronMasterTrophy, 14, 14);
    private static readonly VanillaItemDefinition ChippysCouchDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.ChippysCouch, 20, 20);
    private static readonly VanillaItemDefinition ChippysHeadDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.ChippysHead, 18, 14);
    private static readonly VanillaItemDefinition ChippysBodyDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.ChippysBody, 18, 14);
    private static readonly VanillaItemDefinition ChippysLegsDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.ChippysLegs, 18, 14);
    private static readonly VanillaItemDefinition ChippysHeadbandDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.ChippysHeadband, 26, 30);
    private static readonly VanillaItemDefinition ChippysWingsInactiveDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.ChippysWingsInactive, 24, 8);

    private static VanillaItemDefinition BrainWorldDrop""",
        "skeletron-item-definitions",
    )
    replace_once(
        path,
        """    private static VanillaItemDefinition EaterWorldDrop(ItemTypeId type, int width, int height) =>""",
        """    private static VanillaItemDefinition SkeletronWorldDrop(ItemTypeId type, int width, int height) =>
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
        "skeletron-item-helper",
    )
    cases = """        if (type == VanillaSkeletronItemIds.SkeletronHand) { definition = SkeletronHandDefinition; return true; }
        if (type == VanillaSkeletronItemIds.SkeletronMask) { definition = SkeletronMaskDefinition; return true; }
        if (type == VanillaSkeletronItemIds.BookOfSkulls) { definition = BookOfSkullsDefinition; return true; }
        if (type == VanillaSkeletronItemIds.SkeletronTrophy) { definition = SkeletronTrophyDefinition; return true; }
        if (type == VanillaSkeletronItemIds.SkeletronBossBag) { definition = SkeletronBossBagDefinition; return true; }
        if (type == VanillaSkeletronItemIds.SkeletronPetItem) { definition = SkeletronPetItemDefinition; return true; }
        if (type == VanillaSkeletronItemIds.SkeletronMasterTrophy) { definition = SkeletronMasterTrophyDefinition; return true; }
        if (type == VanillaSkeletronItemIds.ChippysCouch) { definition = ChippysCouchDefinition; return true; }
        if (type == VanillaSkeletronItemIds.ChippysHead) { definition = ChippysHeadDefinition; return true; }
        if (type == VanillaSkeletronItemIds.ChippysBody) { definition = ChippysBodyDefinition; return true; }
        if (type == VanillaSkeletronItemIds.ChippysLegs) { definition = ChippysLegsDefinition; return true; }
        if (type == VanillaSkeletronItemIds.ChippysHeadband) { definition = ChippysHeadbandDefinition; return true; }
        if (type == VanillaSkeletronItemIds.ChippysWingsInactive) { definition = ChippysWingsInactiveDefinition; return true; }

"""
    replace_once(
        path,
        """        definition = default;
        return false;
    }

    public static bool TryGetPlacement(""",
        cases + """        definition = default;
        return false;
    }

    public static bool TryGetPlacement(""",
        "skeletron-item-lookups",
    )


def patch_progression_persistence() -> None:
    path = "src/TerraRuntime.World/WorldFileProgressionHeaderPatcher.cs"
    replace_once(
        path,
        """        (1UL << (int)VanillaWorldProgressionId.KingSlime) |
        (1UL << (int)VanillaWorldProgressionId.EvilBoss);""",
        """        (1UL << (int)VanillaWorldProgressionId.KingSlime) |
        (1UL << (int)VanillaWorldProgressionId.EvilBoss) |
        (1UL << (int)VanillaWorldProgressionId.Skeletron);""",
        "skeletron-progression-mask",
    )
    replace_once(
        path,
        """        int downedBoss2Offset = reader.Offset;
        if (!reader.TryReadBool(out bool persistedDownedBoss2) || !reader.TrySkipBools(8))
            return WorldFileProgressionHeaderPatchResult.InvalidHeader;

        int downedSlimeKingOffset""",
        """        int downedBoss2Offset = reader.Offset;
        if (!reader.TryReadBool(out bool persistedDownedBoss2))
            return WorldFileProgressionHeaderPatchResult.InvalidHeader;
        int downedBoss3Offset = reader.Offset;
        if (!reader.TryReadBool(out bool persistedDownedBoss3) || !reader.TrySkipBools(7))
            return WorldFileProgressionHeaderPatchResult.InvalidHeader;

        int downedSlimeKingOffset""",
        "skeletron-progression-offset",
    )
    replace_once(
        path,
        """        if (mutations.IsCompleted(VanillaWorldProgressionId.EvilBoss) && !persistedDownedBoss2)
            patchedHeader[downedBoss2Offset] = 1;
        if (mutations.IsCompleted(VanillaWorldProgressionId.KingSlime)""",
        """        if (mutations.IsCompleted(VanillaWorldProgressionId.EvilBoss) && !persistedDownedBoss2)
            patchedHeader[downedBoss2Offset] = 1;
        if (mutations.IsCompleted(VanillaWorldProgressionId.Skeletron) && !persistedDownedBoss3)
            patchedHeader[downedBoss3Offset] = 1;
        if (mutations.IsCompleted(VanillaWorldProgressionId.KingSlime)""",
        "skeletron-progression-write",
    )


def patch_combat_pipeline() -> None:
    path = "src/TerraRuntime/RuntimeNpcNetworkCombatPipeline.cs"
    replace_once(
        path,
        """    private readonly RuntimeBrainOfCthulhuLootDeliverySink brainLoot;
    private readonly VanillaNpcLootWorldItemMaterializer materializer""",
        """    private readonly RuntimeBrainOfCthulhuLootDeliverySink brainLoot;
    private readonly RuntimeSkeletronLootDeliverySink skeletronLoot;
    private readonly VanillaNpcLootWorldItemMaterializer materializer""",
        "skeletron-loot-field",
    )
    replace_once(
        path,
        """    private readonly VanillaBrainOfCthulhuLootPlayer[] activeBrainLootPlayers =
        new VanillaBrainOfCthulhuLootPlayer[RuntimeNpcPlayerInteractionLedger.VanillaInteractablePlayerSlots];
    private readonly NpcSnapshot[] npcFamilyBuffer;""",
        """    private readonly VanillaBrainOfCthulhuLootPlayer[] activeBrainLootPlayers =
        new VanillaBrainOfCthulhuLootPlayer[RuntimeNpcPlayerInteractionLedger.VanillaInteractablePlayerSlots];
    private readonly VanillaSkeletronLootPlayer[] activeSkeletronLootPlayers =
        new VanillaSkeletronLootPlayer[RuntimeNpcPlayerInteractionLedger.VanillaInteractablePlayerSlots];
    private readonly NpcSnapshot[] npcFamilyBuffer;""",
        "skeletron-loot-player-buffer",
    )
    replace_once(
        path,
        """        brainLoot = new RuntimeBrainOfCthulhuLootDeliverySink(
            worldItems,
            instancedLeases,
            worldItemReplication);
        if (worldItemReplication is not null)""",
        """        brainLoot = new RuntimeBrainOfCthulhuLootDeliverySink(
            worldItems,
            instancedLeases,
            worldItemReplication);
        skeletronLoot = new RuntimeSkeletronLootDeliverySink(
            worldItems,
            instancedLeases,
            worldItemReplication);
        if (worldItemReplication is not null)""",
        "skeletron-loot-sink-init",
    )
    replace_once(
        path,
        """        if (VanillaEaterOfWorldsLifecycle.IsSegment(current.TypeIdentity))
        {
            VanillaEaterOfWorldsLifecycle.MarkPlayerInteractionAcrossActiveSegments(
                npcs,
                interactions,
                connection.Player,
                npcFamilyBuffer);
        }
        else
        {
            interactions.TryMark(current.Handle, connection.Player);
        }""",
        """        if (VanillaEaterOfWorldsLifecycle.IsSegment(current.TypeIdentity))
        {
            VanillaEaterOfWorldsLifecycle.MarkPlayerInteractionAcrossActiveSegments(
                npcs,
                interactions,
                connection.Player,
                npcFamilyBuffer);
        }
        else if (current.TypeIdentity == VanillaNpcIds.SkeletronHead || current.TypeIdentity == VanillaNpcIds.SkeletronHand)
        {
            MarkSkeletronInteraction(connection.Player);
        }
        else
        {
            interactions.TryMark(current.Handle, connection.Player);
        }""",
        "skeletron-shared-interaction",
    )
    replace_once(
        path,
        """            if (dead.TypeIdentity == VanillaNpcIds.KingSlime)
                ApplyKingSlimeDeathEffects(in dead);
            else if (eaterBoss || dead.TypeIdentity == VanillaNpcIds.BrainOfCthulhu)
                ApplyEvilBossDeathEffects();""",
        """            if (dead.TypeIdentity == VanillaNpcIds.KingSlime)
                ApplyKingSlimeDeathEffects(in dead);
            else if (dead.TypeIdentity == VanillaNpcIds.SkeletronHead)
                ApplySkeletronDeathEffects();
            else if (eaterBoss || dead.TypeIdentity == VanillaNpcIds.BrainOfCthulhu)
                ApplyEvilBossDeathEffects();""",
        "skeletron-network-death-effect",
    )
    replace_once(
        path,
        """        if (dead.TypeIdentity == VanillaNpcIds.KingSlime)
            ApplyKingSlimeDeathEffects(in dead);
        else if (eaterBoss || dead.TypeIdentity == VanillaNpcIds.BrainOfCthulhu)
            ApplyEvilBossDeathEffects();""",
        """        if (dead.TypeIdentity == VanillaNpcIds.KingSlime)
            ApplyKingSlimeDeathEffects(in dead);
        else if (dead.TypeIdentity == VanillaNpcIds.SkeletronHead)
            ApplySkeletronDeathEffects();
        else if (eaterBoss || dead.TypeIdentity == VanillaNpcIds.BrainOfCthulhu)
            ApplyEvilBossDeathEffects();""",
        "skeletron-town-death-effect",
    )
    replace_once(
        path,
        """        if (npc.TypeIdentity == VanillaNpcIds.BrainOfCthulhu || npc.TypeIdentity == VanillaNpcIds.BrainCreeper)
            return TryExecuteBrainOfCthulhuLoot(in npc);

        if (npc.TypeIdentity == VanillaNpcIds.KingSlime && expertMode)""",
        """        if (npc.TypeIdentity == VanillaNpcIds.BrainOfCthulhu || npc.TypeIdentity == VanillaNpcIds.BrainCreeper)
            return TryExecuteBrainOfCthulhuLoot(in npc);
        if (npc.TypeIdentity == VanillaNpcIds.SkeletronHead)
            return TryExecuteSkeletronLoot(in npc);

        if (npc.TypeIdentity == VanillaNpcIds.KingSlime && expertMode)""",
        "skeletron-loot-dispatch",
    )
    method = """    private bool TryExecuteSkeletronLoot(in NpcSnapshot npc)
    {
        if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.SkeletronHead, out VanillaNpcDefinition definition))
        {
            return false;
        }

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player))
                continue;
            activeSkeletronLootPlayers[activeCount++] = new VanillaSkeletronLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = new NpcLootWorldItemOrigin(
            (int)npc.PositionX + definition.Width * 0.5f,
            (int)npc.PositionY + definition.Height * 0.5f);
        var context = new VanillaSkeletronLootContext(
            expertMode,
            masterMode,
            RedHatAdjustmentsEnabled: npc.Ai.Ai3 == 1f);
        return VanillaSkeletronLootEvaluator.TryExecute(
            in context,
            in origin,
            activeSkeletronLootPlayers.AsSpan(0, activeCount),
            random,
            skeletronLoot,
            out _);
    }

    private void MarkSkeletronInteraction(PlayerSlotId player)
    {
        int count = npcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot peer = npcFamilyBuffer[index];
            if (peer.TypeIdentity == VanillaNpcIds.SkeletronHead || peer.TypeIdentity == VanillaNpcIds.SkeletronHand)
                interactions.TryMark(peer.Handle, player);
        }
    }

"""
    replace_once(
        path,
        """    private bool TryExecuteKingSlimeDifficultyLoot(in NpcSnapshot npc)
    {""",
        method + """    private bool TryExecuteKingSlimeDifficultyLoot(in NpcSnapshot npc)
    {""",
        "skeletron-loot-methods",
    )
    replace_once(
        path,
        """    private void ApplyEvilBossDeathEffects()
    {""",
        """    private void ApplySkeletronDeathEffects()
    {
        if (worldTiles is null)
            return;
        RuntimeWorldProgressionRegistry.GetOrCreate(worldTiles)
            .MarkCompleted(VanillaWorldProgressionId.Skeletron);
    }

    private void ApplyEvilBossDeathEffects()
    {""",
        "skeletron-progression-effect",
    )


def patch_coverage_and_docs() -> None:
    path = "src/TerraRuntime.Core/Npcs/VanillaNpcAiCoverageCatalog.cs"
    replace_once(
        path,
        """    BlazingWheelMotionSlice = 1ul << 33
}""",
        """    BlazingWheelMotionSlice = 1ul << 33,
    SkeletronHeadStateSlice = 1ul << 34,
    SkeletronHandStateSlice = 1ul << 35,
    SkeletronSkullProjectileSlice = 1ul << 36,
    BossDeathLootProgressionSlice = 1ul << 37
}""",
        "skeletron-coverage-flags",
    )
    replace_once(path, "            9 +\n", "            11 +\n", "skeletron-coverage-count")
    replace_once(
        path,
        """        entries[8] = Partial(
            VanillaNpcIds.BrainCreeper,
            VanillaNpcAiCapability.BrainCreeperStateSlice |
            OrdinaryCore | VanillaNpcAiCapability.BrainCreeperLifecycleSlice);

        int index = 9;""",
        """        entries[8] = Partial(
            VanillaNpcIds.BrainCreeper,
            VanillaNpcAiCapability.BrainCreeperStateSlice |
            OrdinaryCore | VanillaNpcAiCapability.BrainCreeperLifecycleSlice);
        entries[9] = Partial(
            VanillaNpcIds.SkeletronHead,
            OrdinaryCore |
            VanillaNpcAiCapability.ChildSpawnSlice |
            VanillaNpcAiCapability.SkeletronHeadStateSlice |
            VanillaNpcAiCapability.SkeletronSkullProjectileSlice |
            VanillaNpcAiCapability.BossDeathLootProgressionSlice);
        entries[10] = Partial(
            VanillaNpcIds.SkeletronHand,
            OrdinaryCore |
            VanillaNpcAiCapability.SkeletronHandStateSlice);

        int index = 11;""",
        "skeletron-coverage-entries",
    )
    replace_once(
        "tests/TerraRuntime.Tests/VanillaNpcAiCoverageCatalogTests.cs",
        "            9 +\n",
        "            11 +\n",
        "coverage-test-count",
    )
    roadmap = "docs/roadmap/npc-ai-parity.md"
    replace_once(
        roadmap,
        """| Brain Creeper | yes | partial | partial | Brain-relative orbit/charge, Expert/Good World pursuit and difficulty material loot | no |
| 23 additional hostile AI_001 types""",
        """| Brain Creeper | yes | partial | partial | Brain-relative orbit/charge, Expert/Good World pursuit and difficulty material loot | no |
| Skeletron | yes | partial | partial | head/hand ownership, Expert skull homing/lifetime, death/progression and source-ordered loot | no |
| 23 additional hostile AI_001 types""",
        "skeletron-roadmap-table",
    )
    replace_once(
        roadmap,
        """- [ ] Brain of Cthulhu remaining parity: player `ZoneCrimson` escape gate plus presentation-only sound/dust/gore and client alpha rendering;
- [ ] add remaining pre-Hardmode bosses with complete child/projectile ownership;""",
        """- [ ] Brain of Cthulhu remaining parity: player `ZoneCrimson` escape gate plus presentation-only sound/dust/gore and client alpha rendering;
- [x] add Skeletron gameplay vertical: source-backed head/hand ownership, Expert skull cadence/homing/lifetime, shared head/hand interaction credit, Classic/Expert/Master/RedHat loot and persisted `downedBoss3` progression;
- [ ] add remaining pre-Hardmode bosses with complete child/projectile ownership;""",
        "skeletron-roadmap-n3",
    )


def main() -> None:
    patch_projectile_intents()
    patch_projectile_store()
    patch_projectile_definition()
    patch_projectile_profile()
    patch_projectile_behavior()
    patch_projectile_world()
    patch_skeletron_npc_projectile()
    patch_item_catalog()
    patch_progression_persistence()
    patch_combat_pipeline()
    patch_coverage_and_docs()
    print("skeletron_skull_death_loot_1458=staged")


if __name__ == "__main__":
    main()
