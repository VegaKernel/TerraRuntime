using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Bots;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime.Application.Bots;

/// <summary>
/// Authoritative policy controller for operator-created runtime bots. Actor state remains owned by the existing
/// server-player / NPC / projectile / world-item authorities; this layer owns only bot lifecycle and behavior policy.
/// </summary>
internal sealed class RuntimeBotAuthority
{
    private const float GuardRadiusPixels = 640f;
    private const float GuardRadiusSquared = GuardRadiusPixels * GuardRadiusPixels;
    private const float StuckTeleportMinimumDistancePixels = 320f;
    private const float StuckTeleportMinimumDistanceSquared = StuckTeleportMinimumDistancePixels * StuckTeleportMinimumDistancePixels;
    private const float HardTeleportDistancePixels = 1_280f;
    private const float HardTeleportDistanceSquared = HardTeleportDistancePixels * HardTeleportDistancePixels;
    private const float ProgressDistancePixels = 8f;
    private const long StuckTicks = 180;
    private const long TeleportCooldownTicks = 120;
    private const long TelemetryPeriodTicks = 6;
    private const int MaximumNpcSlots = 256;
    private const short StarterAmmoStack = 100;
    private const byte MeleeWeaponSlot = 0;
    private const byte BowWeaponSlot = 1;
    private const byte GunWeaponSlot = 2;
    private const byte ControlUseItemFlag = 1 << 5;
    // Tactical switch thresholds belong to bot policy. Weapon timing, launch velocity, projectile motion,
    // and damage below continue to come from the verified 1.4.5.8 catalogs.
    private const float ConservativeMeleeCenterDistancePixels = 64f;
    private const float AutomaticGunDistancePixels = 256f;
    private const float AutomaticGunTargetSpeedPixelsPerTick = 3f;
    private const int MaximumPredictiveAimTicks = 120;
    private const float MinimumPlayerEscortOffsetPixels = 64f;
    private const float EscortWanderRadiusPixels = 12f;
    private const long EscortWanderPeriodTicks = 360;
    private const float FlightAscentThresholdPixels = 32f;
    private const float ObstacleClimbTargetPixels = 96f;
    private const long FlightDecisionHoldTicks = 45;

    private readonly ServerPlayerAuthority serverPlayers;
    private readonly PlayerAuthority players;
    private readonly RuntimePlayerSnapshotLookup playerSnapshots;
    private readonly NpcAuthority npcs;
    private readonly ProjectileAuthority projectiles;
    private readonly WorldItemAuthority worldItems;
    private readonly WorldTileStore worldTiles;
    private readonly RuntimeBotTelemetry telemetry;
    private readonly Func<long> tickProvider;
    private readonly float spawnX;
    private readonly float spawnY;
    private readonly Dictionary<int, BotState> bots = [];
    private readonly NpcSnapshot[] npcBuffer = new NpcSnapshot[MaximumNpcSlots];
    private readonly WorldItemSnapshot[] worldItemBuffer = new WorldItemSnapshot[RuntimeWorldItemStore.VanillaCapacity];
    private int nextId = 1;
    private long lastTelemetryTick = long.MinValue;

    public RuntimeBotAuthority(
        ServerPlayerAuthority serverPlayers,
        PlayerAuthority players,
        RuntimePlayerSnapshotLookup playerSnapshots,
        NpcAuthority npcs,
        ProjectileAuthority projectiles,
        WorldItemAuthority worldItems,
        WorldTileStore worldTiles,
        RuntimeBotTelemetry telemetry,
        Func<long> tickProvider,
        float spawnX,
        float spawnY)
    {
        this.serverPlayers = serverPlayers ?? throw new ArgumentNullException(nameof(serverPlayers));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.playerSnapshots = playerSnapshots ?? throw new ArgumentNullException(nameof(playerSnapshots));
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        this.worldItems = worldItems ?? throw new ArgumentNullException(nameof(worldItems));
        this.worldTiles = worldTiles ?? throw new ArgumentNullException(nameof(worldTiles));
        this.telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        this.tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
        if (!float.IsFinite(spawnX) || !float.IsFinite(spawnY))
            throw new ArgumentOutOfRangeException(nameof(spawnX));
        this.spawnX = spawnX;
        this.spawnY = spawnY;
    }

    public bool TryApply(RuntimeCommand command)
    {
        switch (command)
        {
            case RuntimeBotCreateCommand create:
                create.Completion.TrySetResult(Create(create.Request));
                return true;
            case RuntimeBotConfigureCommand configure:
                configure.Completion.TrySetResult(Configure(configure.Id, configure.Configuration));
                return true;
            case RuntimeBotDespawnCommand despawn:
                despawn.Completion.TrySetResult(Despawn(despawn.Id));
                return true;
            default:
                return false;
        }
    }

    public void Tick()
    {
        long tick = tickProvider();
        foreach (BotState bot in bots.Values)
            TickBot(bot, tick);
        PublishTelemetry(tick, force: false);
    }

    private RuntimeBotSnapshot? Create(RuntimeBotCreateRequest request)
    {
        if (!request.IsValid || !IsSupportedBody(request.Body, request.NpcType))
            return null;

        int id = nextId;
        if (id <= 0 || id == int.MaxValue)
            return null;

        var serverId = new ServerPlayerId($"bot:{id}");
        var controllerId = new ActorControllerId($"bot:{id}");
        string name = $"Bot {id}";
        var configuration = new RuntimeBotConfiguration(
            RuntimeBotMode.Idle,
            default,
            Body: request.Body,
            NpcType: request.NpcType,
            WeaponPolicy: RuntimeBotWeaponPolicy.Automatic,
            FlightEnabled: request.Body == RuntimeBotBodyKind.Player);
        var state = new BotState(
            id,
            serverId,
            controllerId,
            name,
            configuration,
            CreateVisualIdentity(id),
            CreatePersonality(id),
            tickProvider());

        bool created = request.Body switch
        {
            RuntimeBotBodyKind.Player => TryCreatePlayerActor(state, spawnX, spawnY),
            RuntimeBotBodyKind.Npc => TryCreateNpcActor(state, request.NpcType, spawnX, spawnY),
            _ => false
        };
        if (!created)
            return null;

        nextId++;
        bots.Add(id, state);
        PublishTelemetry(tickProvider(), force: true);
        return Capture(state);
    }

    private RuntimeBotSnapshot? Configure(int id, RuntimeBotConfiguration configuration)
    {
        if (!bots.TryGetValue(id, out BotState? bot) || !IsValidConfiguration(configuration))
            return null;

        if (configuration.Mode is RuntimeBotMode.Follow or RuntimeBotMode.Guard && !configuration.Target.IsAssigned)
            return null;
        if (configuration.Target.IsAssigned &&
            (!playerSnapshots.TryGetPlayer(configuration.Target.Player, out PlayerStateSnapshot target) ||
             target.Player != configuration.Target.Player ||
             target.Player == bot.Player))
        {
            return null;
        }

        RuntimeBotConfiguration previous = bot.Configuration;
        if ((previous.Body != configuration.Body ||
             configuration.Body == RuntimeBotBodyKind.Npc && previous.NpcType != configuration.NpcType) &&
            !TryReplaceActor(bot, configuration))
        {
            return null;
        }

        if (configuration.Body == RuntimeBotBodyKind.Player &&
            (!ApplyPlayerPresentation(bot, configuration) || !EnsurePlayerLoadout(bot, configuration, addStarterAmmo: true)))
        {
            // A replacement actor has already crossed an authoritative boundary. Do not attempt a lossy reverse
            // resurrection here; reject only before replacement wherever possible, and treat this as an invariant.
            throw new InvalidOperationException("A live bot player rejected its source-backed presentation/loadout.");
        }

        bot.Configuration = configuration;
        ResetBehaviorState(bot, tickProvider());
        if (configuration.Mode == RuntimeBotMode.Idle)
            StopActor(bot);

        PublishTelemetry(tickProvider(), force: true);
        return Capture(bot);
    }

    private bool Despawn(int id)
    {
        if (!bots.Remove(id, out BotState? bot))
            return false;

        bool despawned = bot.Configuration.Body switch
        {
            RuntimeBotBodyKind.Player when bot.Player.IsAssigned => serverPlayers.Despawn(bot.ServerPlayerId),
            RuntimeBotBodyKind.Npc when bot.Npc.IsAssigned => npcs.TryDespawnBotNpc(bot.Npc, bot.ControllerId),
            _ => false
        };
        PublishTelemetry(tickProvider(), force: true);
        return despawned;
    }

    private void TickBot(BotState bot, long tick)
    {
        switch (bot.Configuration.Body)
        {
            case RuntimeBotBodyKind.Player:
                TickPlayerBot(bot, tick);
                break;
            case RuntimeBotBodyKind.Npc:
                TickNpcBot(bot, tick);
                break;
            default:
                bot.TargetAvailable = false;
                bot.PvpEnabled = false;
                bot.IsStuck = false;
                break;
        }
    }

    private void TickPlayerBot(BotState bot, long tick)
    {
        ExpireBuffs(bot, tick);
        if (!serverPlayers.TryGet(bot.Player, out PlayerStateSnapshot self) || self.IsDead)
        {
            bot.TargetAvailable = false;
            bot.PvpEnabled = false;
            bot.IsStuck = false;
            return;
        }

        if (tick >= bot.UseItemUntilTick && (self.ControlFlags & ControlUseItemFlag) != 0)
        {
            _ = serverPlayers.SetHeldItem(bot.ServerPlayerId, self.SelectedItem, useItem: false);
            if (serverPlayers.TryGet(bot.Player, out PlayerStateSnapshot refreshedHeldState))
                self = refreshedHeldState;
        }

        RuntimeBotConfiguration configuration = bot.Configuration;
        // PlayerBot quality-of-life behavior is an invariant, not an operator switch: disabling pickup/healing
        // created deceptively half-functional actors and split one authoritative inventory path into UI variants.
        TryPickupOneUsefulItem(bot, in self);
        TryAutoHeal(bot, in self, tick);
        if (serverPlayers.TryGet(bot.Player, out PlayerStateSnapshot refreshed))
            self = refreshed;

        if (!TryResolveLiveTarget(bot, out PlayerStateSnapshot target))
        {
            ResetUnavailableTarget(bot, tick);
            _ = serverPlayers.SetMovementIntent(bot.ServerPlayerId, ServerPlayerMovementIntent.Stop());
            _ = serverPlayers.SetHeldItem(bot.ServerPlayerId, self.SelectedItem, useItem: false);
            if (self.Hostile)
                _ = serverPlayers.SetHostile(bot.ServerPlayerId, hostile: false);
            return;
        }

        bot.TargetAvailable = true;
        bool pvp = target.Hostile;
        bot.PvpEnabled = pvp;
        if (self.Hostile != pvp)
            _ = serverPlayers.SetHostile(bot.ServerPlayerId, pvp);

        ResolvePlayerEscortDestination(bot, in self, in target, tick, out float escortX, out float escortY);
        escortX = ClampWorldCenterX(escortX, PlayerAuthority.VanillaBasePlayerWidth * 0.5f);
        escortY = ClampWorldCenterY(escortY, PlayerAuthority.VanillaBasePlayerHeight * 0.5f);
        _ = serverPlayers.SetMovementIntent(
            bot.ServerPlayerId,
            ServerPlayerMovementIntent.MoveTo(
                escortX,
                escortY,
                ServerPlayerMovementOptions.Default with
                {
                    StopDistance = 20f,
                    AutoJumpObstacles = true,
                    FlightEnabled = configuration.FlightEnabled
                }));

        UpdatePlayerStuckRecovery(bot, in self, in target, tick);

        if (configuration.Mode == RuntimeBotMode.Guard)
            TryAutoUseCombatBuffs(bot, tick);

        if (configuration.Mode == RuntimeBotMode.Guard && tick >= bot.NextAttackTick)
            TryGuardAttack(bot, in self, in target, tick);
    }

    private void TickNpcBot(BotState bot, long tick)
    {
        if (!bot.Npc.IsAssigned || !npcs.TryCapture(bot.Npc, out NpcSnapshot self) ||
            !self.IsActive || self.Simulation.Life <= 0)
        {
            bot.TargetAvailable = false;
            bot.PvpEnabled = false;
            bot.IsStuck = false;
            return;
        }

        if (!TryResolveLiveTarget(bot, out PlayerStateSnapshot target))
        {
            ResetUnavailableTarget(bot, tick);
            NpcActorIntent stop = NpcActorIntent.Stop();
            _ = npcs.TrySetBotNpcIntent(bot.Npc, bot.ControllerId, in stop);
            return;
        }

        bot.TargetAvailable = true;
        bot.PvpEnabled = false; // No source-backed NPC-owned PvP provenance exists; fail closed.
        if (!VanillaNpcDefinitionCatalog.TryGet(self.TypeIdentity, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(self.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            NpcActorIntent failClosed = NpcActorIntent.Stop();
            _ = npcs.TrySetBotNpcIntent(bot.Npc, bot.ControllerId, in failClosed);
            return;
        }

        // The selected player is never the hostile NPC.target. A presentation NPC cannot publish a per-instance
        // friendly/contact-damage override to an unmodified Terraria client, so it must also stay physically outside
        // the player's collision rectangle; the server-side DamageOverride=0 remains the final authority backstop.
        float noContactX = hitbox.Width * 0.5f + PlayerAuthority.VanillaBasePlayerWidth * 0.5f + 8f;
        float noContactY = hitbox.Height * 0.5f + PlayerAuthority.VanillaBasePlayerHeight * 0.5f + 8f;
        bool flying = VanillaNpcActorControlSupport1458.TryGetMotionFamily(
                self.TypeIdentity,
                out VanillaNpcActorControlMotionFamily1458 motionFamily) &&
            motionFamily != VanillaNpcActorControlMotionFamily1458.GroundFighter;
        ResolveEscortDestination(bot, in target, tick, allowVerticalWander: flying,
            out float escortX, out float escortY);
        escortX = ClampWorldCenterX(escortX, hitbox.Width * 0.5f);
        escortY = ClampWorldCenterY(escortY, hitbox.Height * 0.5f);
        if (RectanglesIntersect(
                self.PositionX,
                self.PositionY,
                hitbox.Width,
                hitbox.Height,
                target.PositionX - 16f,
                target.PositionY - 16f,
                PlayerAuthority.VanillaBasePlayerWidth + 32f,
                PlayerAuthority.VanillaBasePlayerHeight + 32f))
        {
            float separatedY = flying
                ? escortY - hitbox.Height * 0.5f
                : target.PositionY + PlayerAuthority.VanillaBasePlayerHeight - hitbox.Height;
            if (npcs.TryTeleportBotNpc(
                    bot.Npc,
                    bot.ControllerId,
                    escortX - hitbox.Width * 0.5f,
                    separatedY) &&
                npcs.TryCapture(bot.Npc, out NpcSnapshot separated))
            {
                self = separated;
                CommitTeleport(bot, tick);
            }
        }
        float targetCenterX = target.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float npcCenterX = self.PositionX + hitbox.Width * 0.5f;
        if (MathF.Abs(npcCenterX - targetCenterX) < noContactX + 24f)
        {
            float side = npcCenterX < targetCenterX ? -1f : 1f;
            escortX = targetCenterX + side * Math.Max(MathF.Abs(bot.Personality.EscortOffsetX), noContactX + 48f);
            escortX = ClampWorldCenterX(escortX, hitbox.Width * 0.5f);
        }
        var motion = NpcActorMotionOptions.Default with
        {
            StopDistance = MathF.Sqrt(noContactX * noContactX + noContactY * noContactY)
        };
        NpcActorIntent follow = NpcActorIntent.MoveTo(
            escortX,
            flying ? escortY : target.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f,
            motion);
        if (!npcs.TrySetBotNpcIntent(bot.Npc, bot.ControllerId, in follow))
        {
            bot.TargetAvailable = false;
            bot.IsStuck = false;
            return;
        }
        UpdateNpcStuckRecovery(bot, in self, in target, in hitbox, tick);
        // Guard intentionally shares follow/stuck behavior only. NPC offensive combat remains fail-closed until a
        // source-backed NPC bot combat provenance path exists; do not fake player-owned projectiles from an NPC body.
    }

    private bool TryResolveLiveTarget(BotState bot, out PlayerStateSnapshot target)
    {
        RuntimeBotConfiguration configuration = bot.Configuration;
        if (configuration.Mode == RuntimeBotMode.Idle ||
            !configuration.Target.IsAssigned ||
            !playerSnapshots.TryGetPlayer(configuration.Target.Player, out target) ||
            target.Player != configuration.Target.Player || !target.HasHealth || target.Life <= 0 || target.IsDead)
        {
            target = default;
            return false;
        }

        return true;
    }

    private void UpdatePlayerStuckRecovery(
        BotState bot,
        in PlayerStateSnapshot self,
        in PlayerStateSnapshot target,
        long tick)
    {
        float distanceSquared = DistanceSquared(self.PositionX, self.PositionY, target.PositionX, target.PositionY);
        UpdateProgress(bot, distanceSquared, tick);
        if (!ShouldTeleport(bot, distanceSquared, tick))
            return;

        ResolveEscortDestination(bot, in target, tick, allowVerticalWander: bot.Configuration.FlightEnabled,
            out float destinationX, out float destinationY);
        destinationX = ClampWorldCenterX(destinationX, PlayerAuthority.VanillaBasePlayerWidth * 0.5f);
        destinationY = ClampWorldCenterY(destinationY, PlayerAuthority.VanillaBasePlayerHeight * 0.5f);
        if (serverPlayers.TryTeleport(
                bot.ServerPlayerId,
                destinationX - PlayerAuthority.VanillaBasePlayerWidth * 0.5f,
                destinationY - PlayerAuthority.VanillaBasePlayerHeight * 0.5f))
            CommitTeleport(bot, tick);
    }

    private void UpdateNpcStuckRecovery(
        BotState bot,
        in NpcSnapshot self,
        in PlayerStateSnapshot target,
        in VanillaNpcHitboxSize hitbox,
        long tick)
    {
        float distanceSquared = DistanceSquared(self.PositionX, self.PositionY, target.PositionX, target.PositionY);
        UpdateProgress(bot, distanceSquared, tick);
        if (!ShouldTeleport(bot, distanceSquared, tick))
            return;

        bool flying = VanillaNpcActorControlSupport1458.TryGetMotionFamily(
                self.TypeIdentity,
                out VanillaNpcActorControlMotionFamily1458 motionFamily) &&
            motionFamily != VanillaNpcActorControlMotionFamily1458.GroundFighter;
        ResolveEscortDestination(bot, in target, tick, allowVerticalWander: flying,
            out float destinationX, out float destinationY);
        destinationX = ClampWorldCenterX(destinationX, hitbox.Width * 0.5f);
        destinationY = ClampWorldCenterY(destinationY, hitbox.Height * 0.5f);
        float safeX = destinationX - hitbox.Width * 0.5f;
        float safeY = flying
            ? destinationY - hitbox.Height * 0.5f
            : target.PositionY + PlayerAuthority.VanillaBasePlayerHeight - hitbox.Height;
        if (npcs.TryTeleportBotNpc(bot.Npc, bot.ControllerId, safeX, safeY))
            CommitTeleport(bot, tick);
    }

    private static void UpdateProgress(BotState bot, float distanceSquared, long tick)
    {
        float distance = MathF.Sqrt(Math.Max(0f, distanceSquared));
        if (!float.IsFinite(bot.LastDistance) || bot.LastDistance - distance >= ProgressDistancePixels ||
            distanceSquared < StuckTeleportMinimumDistanceSquared)
        {
            bot.LastDistance = distance;
            bot.LastProgressTick = tick;
            bot.IsStuck = false;
        }
        else if (distance < bot.LastDistance)
        {
            bot.LastDistance = distance;
        }
    }

    private static bool ShouldTeleport(BotState bot, float distanceSquared, long tick)
    {
        bool hardDistance = distanceSquared >= HardTeleportDistanceSquared;
        bool stalledFarAway = distanceSquared >= StuckTeleportMinimumDistanceSquared && tick - bot.LastProgressTick >= StuckTicks;
        bot.IsStuck = stalledFarAway;
        return tick >= bot.TeleportCooldownUntil && (hardDistance || stalledFarAway);
    }

    private static void CommitTeleport(BotState bot, long tick)
    {
        bot.TeleportCount++;
        bot.TeleportCooldownUntil = tick + TeleportCooldownTicks;
        bot.LastProgressTick = tick;
        bot.LastDistance = 0f;
        bot.IsStuck = false;
    }

    private void ResolvePlayerEscortDestination(
        BotState bot,
        in PlayerStateSnapshot self,
        in PlayerStateSnapshot target,
        long tick,
        out float centerX,
        out float centerY)
    {
        // Walking is the normal state. Wings are a traversal capability: they become useful only when the protected
        // player is materially above the bot or a solid obstacle blocks the direct route. Keeping the ordinary target
        // on the target player's ground level also prevents the old permanent low hover caused by a negative formation
        // offset on every Follow tick.
        ResolveEscortDestination(bot, in target, tick, allowVerticalWander: false, out centerX, out centerY);
        if (!bot.Configuration.FlightEnabled)
        {
            bot.FlightDecisionUntilTick = 0;
            return;
        }

        float selfCenterY = self.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float targetCenterY = target.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        bool targetRequiresAscent = targetCenterY < selfCenterY - FlightAscentThresholdPixels;
        bool routeBlocked = !VanillaWorldCanHit.HasLineOfSight(
            worldTiles,
            self.PositionX,
            self.PositionY,
            (int)PlayerAuthority.VanillaBasePlayerWidth,
            (int)PlayerAuthority.VanillaBasePlayerHeight,
            centerX - PlayerAuthority.VanillaBasePlayerWidth * 0.5f,
            target.PositionY,
            (int)PlayerAuthority.VanillaBasePlayerWidth,
            (int)PlayerAuthority.VanillaBasePlayerHeight);

        if (targetRequiresAscent || routeBlocked)
            bot.FlightDecisionUntilTick = tick + FlightDecisionHoldTicks;
        if (!targetRequiresAscent && !routeBlocked && tick >= bot.FlightDecisionUntilTick)
            return;

        float phase = ((tick + bot.Personality.WanderPhaseTicks) % EscortWanderPeriodTicks) /
            (float)EscortWanderPeriodTicks * MathF.Tau;
        float airborneFormationY = targetCenterY + bot.Personality.EscortOffsetY +
            MathF.Cos(phase * 0.75f) * EscortWanderRadiusPixels;
        centerY = routeBlocked
            ? MathF.Min(airborneFormationY, selfCenterY - ObstacleClimbTargetPixels)
            : airborneFormationY;
    }

    private static void ResolveEscortDestination(
        BotState bot,
        in PlayerStateSnapshot target,
        long tick,
        bool allowVerticalWander,
        out float centerX,
        out float centerY)
    {
        float phase = ((tick + bot.Personality.WanderPhaseTicks) % EscortWanderPeriodTicks) /
            (float)EscortWanderPeriodTicks * MathF.Tau;
        float wanderX = MathF.Sin(phase) * EscortWanderRadiusPixels;
        float wanderY = allowVerticalWander
            ? MathF.Cos(phase * 0.75f) * EscortWanderRadiusPixels
            : 0f;
        centerX = target.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f +
            bot.Personality.EscortOffsetX + wanderX;
        centerY = target.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f +
            (allowVerticalWander ? bot.Personality.EscortOffsetY : 0f) + wanderY;
    }

    private float ClampWorldCenterX(float centerX, float halfWidth) =>
        Math.Clamp(centerX, halfWidth, worldTiles.Dimensions.WidthTiles * 16f - halfWidth);

    private float ClampWorldCenterY(float centerY, float halfHeight) =>
        Math.Clamp(centerY, halfHeight, worldTiles.Dimensions.HeightTiles * 16f - halfHeight);

    private void TryGuardAttack(BotState bot, in PlayerStateSnapshot self, in PlayerStateSnapshot protectedPlayer, long tick)
    {
        if (!TryFindGuardTarget(bot, in self, in protectedPlayer, out BotGuardTarget target))
            return;

        RuntimeBotAttackKind attack = ResolveAttackKind(bot.Configuration.WeaponPolicy, in self, in target);
        if (attack == RuntimeBotAttackKind.Melee)
        {
            TryGuardMeleeAttack(bot, in self, in target, tick);
            return;
        }

        if (TryGuardRangedAttack(bot, in self, in target, attack, tick))
            return;

        if (bot.Configuration.WeaponPolicy == RuntimeBotWeaponPolicy.Automatic)
        {
            RuntimeBotAttackKind fallback = attack == RuntimeBotAttackKind.Gun
                ? RuntimeBotAttackKind.Bow
                : RuntimeBotAttackKind.Gun;
            _ = TryGuardRangedAttack(bot, in self, in target, fallback, tick);
        }
    }

    private bool TryGuardRangedAttack(
        BotState bot,
        in PlayerStateSnapshot self,
        in BotGuardTarget target,
        RuntimeBotAttackKind attack,
        long tick)
    {
        if (!TryResolveRangedLoadout(attack, out _, out ItemTypeId ammoItem,
                out VanillaProjectileWeaponCombatDefinition weapon, out VanillaProjectileAmmoCombatDefinition ammo) ||
            !TryFindAmmoSlot(bot.ServerPlayerId, ammoItem, out short ammoSlot, out ServerPlayerItemState ammoState) ||
            !VanillaProjectileWeaponCombatCatalog.TryResolveProjectileType(in weapon, in ammo, out ProjectileTypeId projectileType) ||
            !TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(
                projectileType,
                out VanillaProjectileDefinition projectileDefinition))
        {
            return false;
        }

        VanillaPlayerCombatSnapshot combat = BuildBotCombatSnapshot(bot, tick);
        VanillaCombatPrefixModifiers prefix = VanillaCombatPrefixModifiers.Identity;
        VanillaLaunchSpeedEnvelope speedEnvelope = VanillaProjectileWeaponCombatCatalog.ResolveLaunchSpeedEnvelope(
            in weapon, in ammo, in prefix, in combat);
        int damage = VanillaProjectileWeaponCombatCatalog.ResolveDamage(in weapon, in ammo, in prefix, in combat);
        float knockBack = VanillaProjectileWeaponCombatCatalog.ResolveKnockBack(in weapon, in ammo, in prefix, in combat);
        if (!speedEnvelope.IsValid || damage <= 0 || damage > short.MaxValue || !float.IsFinite(knockBack))
            return false;

        float speed = speedEnvelope.CanonicalMagnitude;
        if (weapon.AmmoFamily == VanillaProjectileAmmoFamily.Arrow &&
            IsBuffActive(bot, VanillaBuffIds.Archery, tick) && speed < 20f)
        {
            speed = Math.Min(20f, speed * 1.2f);
        }

        float originX = self.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float originY = self.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        if (!TryResolveProjectileLaunch(
                originX,
                originY,
                projectileType,
                in projectileDefinition,
                speed,
                in target,
                out float velocityX,
                out float velocityY))
        {
            return false;
        }

        byte weaponSlot = ResolveWeaponSlot(bot.Configuration.WeaponPolicy, attack);
        if (!serverPlayers.SetHeldItem(bot.ServerPlayerId, weaponSlot, useItem: true))
            return false;

        // Consume first, rollback if projectile allocation fails. This keeps the world-writer path duplication-safe.
        var consumed = ammoState.Stack == 1
            ? new ServerPlayerItemState(ammoSlot, VanillaItemIds.None, 0, VanillaPrefixIds.None, 0)
            : ammoState with { Stack = checked((short)(ammoState.Stack - 1)) };
        if (!serverPlayers.SetItem(bot.ServerPlayerId, in consumed))
        {
            _ = serverPlayers.SetHeldItem(bot.ServerPlayerId, weaponSlot, useItem: false);
            return false;
        }

        var projectile = new ProjectileStateUpdate(
            projectileType,
            bot.Player.Slot.Value,
            originX,
            originY,
            velocityX,
            velocityY,
            default,
            BannerIdToRespondTo: 0,
            Damage: checked((short)damage),
            KnockBack: knockBack,
            OriginalDamage: 0);
        if (!projectiles.TrySpawnTrustedServerPlayerProjectile(bot.Player, in projectile, out _))
        {
            if (!serverPlayers.SetItem(bot.ServerPlayerId, in ammoState))
                throw new InvalidOperationException("Bot ammo rollback failed after rejected trusted projectile spawn.");
            _ = serverPlayers.SetHeldItem(bot.ServerPlayerId, weaponSlot, useItem: false);
            return false;
        }

        bot.NextAttackTick = tick + Math.Max(1, weapon.UseTimeTicks);
        bot.UseItemUntilTick = tick + Math.Max(1, weapon.AnimationTicks);
        return true;
    }

    private void TryGuardMeleeAttack(
        BotState bot,
        in PlayerStateSnapshot self,
        in BotGuardTarget target,
        long tick)
    {
        if (!target.Npc.IsAssigned ||
            !VanillaItemCombatCatalog.TryGetDirectMelee(
                VanillaItemIds.CopperBroadsword,
                out VanillaDirectMeleeCombatDefinition weapon))
        {
            return;
        }

        float sourceCenterX = self.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float sourceCenterY = self.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float dx = target.CenterX - sourceCenterX;
        float dy = target.CenterY - sourceCenterY;
        float distanceSquared = dx * dx + dy * dy;
        if (distanceSquared > ConservativeMeleeCenterDistancePixels * ConservativeMeleeCenterDistancePixels)
            return;

        VanillaResolvedDirectMeleeUse resolved = VanillaDirectMeleeCombatMath.Resolve(
            in weapon,
            VanillaCombatPrefixModifiers.Identity,
            BuildBotCombatSnapshot(bot, tick),
            Random.Shared.Next(-15, 16),
            Random.Shared.Next(1, 101),
            pvp: false);
        int hitDirection = dx < 0f ? -1 : 1;
        byte weaponSlot = ResolveWeaponSlot(bot.Configuration.WeaponPolicy, RuntimeBotAttackKind.Melee);
        if (!serverPlayers.SetHeldItem(bot.ServerPlayerId, weaponSlot, useItem: true) ||
            !npcs.TryStrikeBotPlayerMelee(
                bot.Player,
                target.Npc,
                resolved.Damage,
                resolved.ArmorPenetration,
                resolved.Critical,
                resolved.KnockBack,
                hitDirection))
        {
            _ = serverPlayers.SetHeldItem(bot.ServerPlayerId, weaponSlot, useItem: false);
            return;
        }

        bot.NextAttackTick = tick + Math.Max(1, resolved.UseTimeTicks);
        bot.UseItemUntilTick = tick + Math.Max(1, resolved.AnimationTicks);
    }

    private bool TryFindGuardTarget(
        BotState bot,
        in PlayerStateSnapshot self,
        in PlayerStateSnapshot protectedPlayer,
        out BotGuardTarget target)
    {
        target = default;
        float protectedCenterX = protectedPlayer.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float protectedCenterY = protectedPlayer.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float bestDistanceSquared = float.PositiveInfinity;

        int npcCount = npcs.CopyActive(npcBuffer);
        for (int i = 0; i < npcCount; i++)
        {
            NpcSnapshot npc = npcBuffer[i];
            if (!npc.IsActive || npc.Handle == bot.Npc || npc.Simulation.Life <= 0 || npc.Simulation.DontTakeDamage ||
                !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition) ||
                definition.Role == NpcArchetypeRole.Town || definition.Damage <= 0 ||
                !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
            {
                continue;
            }

            float centerX = npc.PositionX + hitbox.Width * 0.5f;
            float centerY = npc.PositionY + hitbox.Height * 0.5f;
            float distanceSquared = DistanceSquared(protectedCenterX, protectedCenterY, centerX, centerY);
            if (distanceSquared > GuardRadiusSquared || distanceSquared >= bestDistanceSquared ||
                !VanillaWorldCanHit.HasLineOfSight(
                    worldTiles,
                    self.PositionX,
                    self.PositionY,
                    (int)PlayerAuthority.VanillaBasePlayerWidth,
                    (int)PlayerAuthority.VanillaBasePlayerHeight,
                    npc.PositionX,
                    npc.PositionY,
                    hitbox.Width,
                    hitbox.Height))
            {
                continue;
            }
            bestDistanceSquared = distanceSquared;
            target = new BotGuardTarget(
                npc.Handle,
                default,
                centerX,
                centerY,
                npc.VelocityX,
                npc.VelocityY,
                hitbox.Width,
                hitbox.Height);
        }

        // PvP is admitted only when both the protected target and the opponent have vanilla hostile enabled.
        if (bot.PvpEnabled)
        {
            foreach (RuntimePlayerMember candidate in players.Members)
            {
                PlayerHandle candidateHandle = candidate.Connection.Player;
                if (candidateHandle == protectedPlayer.Player || candidateHandle == bot.Player ||
                    !candidate.Hostile || candidate.IsDead || !candidate.HasHealth || candidate.Life <= 0)
                {
                    continue;
                }

                float centerX = candidate.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
                float centerY = candidate.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
                float distanceSquared = DistanceSquared(protectedCenterX, protectedCenterY, centerX, centerY);
                if (distanceSquared > GuardRadiusSquared || distanceSquared >= bestDistanceSquared ||
                    !VanillaWorldCanHit.HasLineOfSight(
                        worldTiles,
                        self.PositionX,
                        self.PositionY,
                        (int)PlayerAuthority.VanillaBasePlayerWidth,
                        (int)PlayerAuthority.VanillaBasePlayerHeight,
                        candidate.PositionX,
                        candidate.PositionY,
                        (int)PlayerAuthority.VanillaBasePlayerWidth,
                        (int)PlayerAuthority.VanillaBasePlayerHeight))
                {
                    continue;
                }
                bestDistanceSquared = distanceSquared;
                target = new BotGuardTarget(
                    default,
                    candidateHandle,
                    centerX,
                    centerY,
                    candidate.VelocityX,
                    candidate.VelocityY,
                    PlayerAuthority.VanillaBasePlayerWidth,
                    PlayerAuthority.VanillaBasePlayerHeight);
            }
        }

        return float.IsFinite(bestDistanceSquared);
    }

    private static RuntimeBotAttackKind ResolveAttackKind(
        RuntimeBotWeaponPolicy policy,
        in PlayerStateSnapshot self,
        in BotGuardTarget target)
    {
        if (policy == RuntimeBotWeaponPolicy.Melee)
            return RuntimeBotAttackKind.Melee;
        if (policy == RuntimeBotWeaponPolicy.Gun)
            return RuntimeBotAttackKind.Gun;
        if (policy == RuntimeBotWeaponPolicy.Bow)
            return RuntimeBotAttackKind.Bow;

        float sourceCenterX = self.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float sourceCenterY = self.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float distanceSquared = DistanceSquared(sourceCenterX, sourceCenterY, target.CenterX, target.CenterY);
        if (target.Npc.IsAssigned &&
            distanceSquared <= ConservativeMeleeCenterDistancePixels * ConservativeMeleeCenterDistancePixels)
        {
            return RuntimeBotAttackKind.Melee;
        }

        float targetSpeedSquared = target.VelocityX * target.VelocityX + target.VelocityY * target.VelocityY;
        return distanceSquared >= AutomaticGunDistancePixels * AutomaticGunDistancePixels ||
               targetSpeedSquared >= AutomaticGunTargetSpeedPixelsPerTick * AutomaticGunTargetSpeedPixelsPerTick
            ? RuntimeBotAttackKind.Gun
            : RuntimeBotAttackKind.Bow;
    }

    private static byte ResolveWeaponSlot(RuntimeBotWeaponPolicy policy, RuntimeBotAttackKind attack)
    {
        if (policy != RuntimeBotWeaponPolicy.Automatic)
            return MeleeWeaponSlot;

        return attack switch
        {
            RuntimeBotAttackKind.Melee => MeleeWeaponSlot,
            RuntimeBotAttackKind.Bow => BowWeaponSlot,
            RuntimeBotAttackKind.Gun => GunWeaponSlot,
            _ => MeleeWeaponSlot
        };
    }

    private bool TryResolveProjectileLaunch(
        float originX,
        float originY,
        ProjectileTypeId projectileType,
        in VanillaProjectileDefinition definition,
        float launchSpeed,
        in BotGuardTarget target,
        out float velocityX,
        out float velocityY)
    {
        velocityX = 0f;
        velocityY = 0f;
        if (definition.AiStyle != VanillaProjectileAiStyles.Arrow ||
            !(launchSpeed > 0f) || !float.IsFinite(launchSpeed))
        {
            return false;
        }

        int subupdates = VanillaProjectileUpdateFacts.GetSubupdatesPerWorldTick(projectileType);
        float projectileCenterX = originX + definition.Width * 0.5f;
        float projectileCenterY = originY + definition.Height * 0.5f;
        for (int ticks = 1; ticks <= MaximumPredictiveAimTicks; ticks++)
        {
            int steps = checked(ticks * subupdates);
            float predictedTargetX = target.CenterX + target.VelocityX * ticks;
            float predictedTargetY = target.CenterY + target.VelocityY * ticks;
            int gravitySteps = Math.Max(0, steps - 14);
            float gravityDisplacement = gravitySteps * (gravitySteps + 1) * 0.05f;
            float requiredX = (predictedTargetX - projectileCenterX) / steps;
            float requiredY = (predictedTargetY - projectileCenterY - gravityDisplacement) / steps;
            float requiredLength = MathF.Sqrt(requiredX * requiredX + requiredY * requiredY);
            if (!(requiredLength > 0.001f) || !float.IsFinite(requiredLength))
                continue;

            float candidateVelocityX = requiredX / requiredLength * launchSpeed;
            float candidateVelocityY = requiredY / requiredLength * launchSpeed;
            if (!TrajectoryReachesTarget(
                    originX,
                    originY,
                    candidateVelocityX,
                    candidateVelocityY,
                    subupdates,
                    ticks,
                    in definition,
                    in target))
            {
                continue;
            }

            velocityX = candidateVelocityX;
            velocityY = candidateVelocityY;
            return true;
        }

        return false;
    }

    private bool TrajectoryReachesTarget(
        float originX,
        float originY,
        float initialVelocityX,
        float initialVelocityY,
        int subupdates,
        int maximumTicks,
        in VanillaProjectileDefinition definition,
        in BotGuardTarget target)
    {
        float positionX = originX;
        float positionY = originY;
        float velocityX = initialVelocityX;
        float velocityY = initialVelocityY;
        float ai0 = 0f;
        int maximumSteps = checked(subupdates * maximumTicks);
        for (int step = 0; step < maximumSteps; step++)
        {
            ai0 += 1f;
            if (ai0 >= 15f)
            {
                ai0 = 15f;
                velocityY = Math.Min(16f, velocityY + 0.1f);
            }

            if (!definition.IgnoreWater &&
                VanillaWorldCollision.GetLiquidContacts(
                    worldTiles,
                    positionX,
                    positionY,
                    definition.Width,
                    definition.Height).Wet)
            {
                return false;
            }

            VanillaTileCollisionResult collision = VanillaWorldCollision.TileCollision(
                worldTiles,
                positionX + definition.CollisionOffsetX,
                positionY + definition.CollisionOffsetY,
                velocityX,
                velocityY,
                definition.CollisionWidth,
                definition.CollisionHeight,
                fallThrough: true,
                fall2: true);
            if (collision.VelocityX != velocityX || collision.VelocityY != velocityY)
                return false;

            positionX += velocityX;
            positionY += velocityY;
            float elapsedTicks = (step + 1f) / subupdates;
            float targetLeft = target.CenterX + target.VelocityX * elapsedTicks - target.Width * 0.5f;
            float targetTop = target.CenterY + target.VelocityY * elapsedTicks - target.Height * 0.5f;
            if (RectanglesIntersect(
                    positionX,
                    positionY,
                    definition.Width,
                    definition.Height,
                    targetLeft,
                    targetTop,
                    target.Width,
                    target.Height))
            {
                return true;
            }
        }

        return false;
    }

    private void TryPickupOneUsefulItem(BotState bot, in PlayerStateSnapshot self)
    {
        int count = worldItems.CopyActive(worldItemBuffer);
        for (int index = 0; index < count; index++)
        {
            WorldItemSnapshot item = worldItemBuffer[index];
            if (!TryClassifyUsefulWorldItem(bot, in item, out VanillaBotItemDefinition1458 definition) ||
                !IsTrustedPickupEligible(bot, in self, in item, in definition) ||
                !TryPlanInventoryDeposit(bot, in item, in definition, out short slot, out ServerPlayerItemState before,
                    out ServerPlayerItemState after))
            {
                continue;
            }

            if (!worldItems.TryCapture(item.Handle.Slot, out WorldItemSnapshot current) || current.Handle != item.Handle)
                continue;
            if (!serverPlayers.SetItem(bot.ServerPlayerId, in after))
                continue;
            if (worldItems.TryTakeTrusted(item.Handle, out _))
                return;
            if (!serverPlayers.SetItem(bot.ServerPlayerId, in before))
                throw new InvalidOperationException("Bot inventory rollback failed after stale world-item pickup.");
        }
    }

    private bool TryClassifyUsefulWorldItem(
        BotState bot,
        in WorldItemSnapshot item,
        out VanillaBotItemDefinition1458 definition)
    {
        definition = default;
        if (!item.IsActive || item.Prefix != 0 || !item.TryGetItemType(out ItemTypeId itemType) ||
            !VanillaBotItemDefinitionCatalog1458.TryGet(itemType, out definition))
        {
            return false;
        }

        return definition.Kind switch
        {
            VanillaBotItemKind.RequiredAmmo =>
                IsRequiredAmmo(bot.Configuration.WeaponPolicy, itemType),
            VanillaBotItemKind.HealingPotion => true,
            VanillaBotItemKind.UsefulBuffPotion =>
                VanillaBotItemDefinitionCatalog1458.IsSupportedCombatBuff(definition.BuffType) &&
                IsBuffUsefulForWeapon(bot.Configuration.WeaponPolicy, definition.BuffType),
            _ => false
        };
    }

    private static bool IsTrustedPickupEligible(
        BotState bot,
        in PlayerStateSnapshot self,
        in WorldItemSnapshot item,
        in VanillaBotItemDefinition1458 definition)
    {
        byte botSlot = bot.Player.Slot.Value;
        if (item.ShimmerTime != 0f ||
            item.OwnerPlayerId != byte.MaxValue && item.OwnerPlayerId != botSlot ||
            item.GrabDelayTime > 0 && (item.GrabDelayPlayer == botSlot || item.GrabDelayPlayer == byte.MaxValue) ||
            item.Shimmered && MathF.Sqrt(item.VelocityX * item.VelocityX + item.VelocityY * item.VelocityY) >= 0.2f)
        {
            return false;
        }

        return RectanglesIntersect(
            self.PositionX,
            self.PositionY,
            PlayerAuthority.VanillaBasePlayerWidth,
            PlayerAuthority.VanillaBasePlayerHeight,
            item.PositionX,
            item.PositionY,
            definition.Width,
            definition.Height);
    }

    private bool TryPlanInventoryDeposit(
        BotState bot,
        in WorldItemSnapshot worldItem,
        in VanillaBotItemDefinition1458 definition,
        out short slot,
        out ServerPlayerItemState before,
        out ServerPlayerItemState after)
    {
        slot = -1;
        before = default;
        after = default;
        if (!worldItem.TryGetItemType(out ItemTypeId itemType) || worldItem.Stack <= 0)
            return false;

        Span<short> candidateSlots = stackalloc short[VanillaPlayerItemSlotCatalog.OrdinaryInventoryCount - 1];
        int candidateCount = BuildStorageSlotOrder(definition.Kind, candidateSlots);
        short firstEmpty = -1;
        ServerPlayerItemState firstEmptyState = default;
        for (int i = 0; i < candidateCount; i++)
        {
            short candidate = candidateSlots[i];
            if (!serverPlayers.TryGetItem(bot.ServerPlayerId, candidate, out ServerPlayerItemState state))
                return false;
            if (state.IsEmpty)
            {
                if (firstEmpty < 0)
                {
                    firstEmpty = candidate;
                    firstEmptyState = state;
                }
                continue;
            }
            if (state.ItemType != itemType || state.Prefix != VanillaPrefixIds.None ||
                state.Stack + worldItem.Stack > VanillaBotItemDefinitionCatalog1458.CommonMaxStack)
            {
                continue;
            }

            slot = candidate;
            before = state;
            after = state with { Stack = checked((short)(state.Stack + worldItem.Stack)) };
            return true;
        }

        if (firstEmpty < 0)
            return false;

        slot = firstEmpty;
        before = firstEmptyState;
        after = new ServerPlayerItemState(
            firstEmpty,
            itemType,
            worldItem.Stack,
            VanillaPrefixIds.None,
            0);
        return true;
    }

    private static int BuildStorageSlotOrder(VanillaBotItemKind kind, Span<short> destination)
    {
        int count = 0;
        if (kind == VanillaBotItemKind.RequiredAmmo)
        {
            for (short slot = VanillaPlayerItemSlotCatalog.AmmoSlotStart;
                 slot < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive;
                 slot++)
            {
                destination[count++] = slot;
            }
        }

        // Slot 0 is the bot's held weapon. QuickHeal/QuickBuff source scans the same ordinary inventory span.
        for (short slot = 1; slot < VanillaPlayerItemSlotCatalog.MainInventoryEndExclusive; slot++)
            destination[count++] = slot;
        return count;
    }

    private void TryAutoHeal(BotState bot, in PlayerStateSnapshot self, long tick)
    {
        if (!self.HasHealth || self.Life <= 0 || self.Life >= self.MaxLife || tick < bot.PotionDelayUntilTick)
            return;

        int missingLife = self.MaxLife - self.Life;
        int bestDifference = -self.MaxLife;
        short bestSlot = -1;
        ServerPlayerItemState bestItem = default;
        VanillaBotItemDefinition1458 bestDefinition = default;
        for (short slot = VanillaPlayerItemSlotCatalog.InventoryStart;
             slot < VanillaPlayerItemSlotCatalog.OrdinaryInventoryEndExclusive;
             slot++)
        {
            if (!serverPlayers.TryGetItem(bot.ServerPlayerId, slot, out ServerPlayerItemState item) ||
                item.IsEmpty || item.Prefix != VanillaPrefixIds.None ||
                !VanillaBotItemDefinitionCatalog1458.TryGet(item.ItemType, out VanillaBotItemDefinition1458 definition) ||
                definition.Kind != VanillaBotItemKind.HealingPotion ||
                !VanillaBotItemDefinitionCatalog1458.IsBetterQuickHealCandidate(
                    missingLife, in definition, bestDifference, out int candidateDifference))
            {
                continue;
            }

            bestDifference = candidateDifference;
            bestSlot = slot;
            bestItem = item;
            bestDefinition = definition;
        }

        if (bestSlot < 0)
            return;

        ServerPlayerItemState consumed = bestItem.Stack == 1
            ? new ServerPlayerItemState(bestSlot, VanillaItemIds.None, 0, VanillaPrefixIds.None, 0)
            : bestItem with { Stack = checked((short)(bestItem.Stack - 1)) };
        if (!serverPlayers.SetItem(bot.ServerPlayerId, in consumed))
            return;

        short healedLife = checked((short)Math.Min(self.MaxLife, self.Life + bestDefinition.HealLife));
        var vitals = new ServerPlayerVitalsState(healedLife, self.MaxLife, self.Mana, self.MaxMana);
        if (!serverPlayers.SetVitals(bot.ServerPlayerId, in vitals))
        {
            if (!serverPlayers.SetItem(bot.ServerPlayerId, in bestItem))
                throw new InvalidOperationException("Bot healing-item rollback failed after vitals rejection.");
            return;
        }

        bot.PotionDelayUntilTick = tick + VanillaBotItemDefinitionCatalog1458.OrdinaryHealingPotionDelayTicks;
    }

    private void TryAutoUseCombatBuffs(BotState bot, long tick)
    {
        for (short slot = VanillaPlayerItemSlotCatalog.InventoryStart;
             slot < VanillaPlayerItemSlotCatalog.OrdinaryInventoryEndExclusive;
             slot++)
        {
            if (!serverPlayers.TryGetItem(bot.ServerPlayerId, slot, out ServerPlayerItemState item) ||
                item.IsEmpty || item.Prefix != VanillaPrefixIds.None ||
                !VanillaBotItemDefinitionCatalog1458.TryGet(item.ItemType, out VanillaBotItemDefinition1458 definition) ||
                definition.Kind != VanillaBotItemKind.UsefulBuffPotion ||
                !VanillaBotItemDefinitionCatalog1458.IsSupportedCombatBuff(definition.BuffType) ||
                !IsBuffUsefulForWeapon(bot.Configuration.WeaponPolicy, definition.BuffType) ||
                IsBuffActive(bot, definition.BuffType, tick))
            {
                continue;
            }

            ServerPlayerItemState consumed = item.Stack == 1
                ? new ServerPlayerItemState(slot, VanillaItemIds.None, 0, VanillaPrefixIds.None, 0)
                : item with { Stack = checked((short)(item.Stack - 1)) };
            if (!serverPlayers.SetItem(bot.ServerPlayerId, in consumed))
                continue;

            // TerrariaServer 1.4.5.8 MessageBuffer case 55 is targeted PvP-buff delivery: a client
            // applies it only when the encoded player slot is Main.myPlayer. A server-owned fake player
            // therefore keeps supported combat-buff state inside the trusted simulation instead of
            // broadcasting packet 55 as if it were remote-player buff replication.
            bot.ActiveBuffs[definition.BuffType] = tick + definition.BuffTimeTicks;
        }
    }

    private static VanillaPlayerCombatSnapshot BuildBotCombatSnapshot(BotState bot, long tick)
    {
        VanillaPlayerCombatSnapshot snapshot = VanillaPlayerCombatSnapshot.Baseline;
        if (IsBuffActive(bot, VanillaBuffIds.Archery, tick))
            snapshot = snapshot with { ArrowDamage = snapshot.ArrowDamage * 1.1f };
        if (IsBuffActive(bot, VanillaBuffIds.Wrath, tick))
            snapshot = snapshot with
            {
                MeleeDamage = snapshot.MeleeDamage + 0.1f,
                RangedDamage = snapshot.RangedDamage + 0.1f
            };
        return snapshot;
    }

    private static bool IsBuffActive(BotState bot, BuffTypeId buff, long tick) =>
        bot.ActiveBuffs.TryGetValue(buff, out long until) && until > tick;

    private static void ExpireBuffs(BotState bot, long tick)
    {
        if (bot.ActiveBuffs.Count == 0)
            return;
        foreach (BuffTypeId buff in bot.ActiveBuffs.Where(pair => pair.Value <= tick).Select(pair => pair.Key).ToArray())
            bot.ActiveBuffs.Remove(buff);
    }

    private static bool IsBuffUsefulForWeapon(RuntimeBotWeaponPolicy policy, BuffTypeId buff)
    {
        if (buff == VanillaBuffIds.Wrath)
            return policy is RuntimeBotWeaponPolicy.Automatic or RuntimeBotWeaponPolicy.Melee or RuntimeBotWeaponPolicy.Bow or RuntimeBotWeaponPolicy.Gun;
        return buff == VanillaBuffIds.Archery && policy is RuntimeBotWeaponPolicy.Automatic or RuntimeBotWeaponPolicy.Bow;
    }

    private static bool IsRequiredAmmo(RuntimeBotWeaponPolicy policy, ItemTypeId itemType)
    {
        if (policy == RuntimeBotWeaponPolicy.Automatic)
            return itemType == VanillaItemIds.WoodenArrow || itemType == VanillaItemIds.MusketBall;
        return TryResolveRangedLoadout(policy, out _, out ItemTypeId requiredAmmo, out _, out _) &&
               itemType == requiredAmmo;
    }

    private static bool TryResolveRangedLoadout(
        RuntimeBotWeaponPolicy policy,
        out ItemTypeId weaponItem,
        out ItemTypeId ammoItem,
        out VanillaProjectileWeaponCombatDefinition weapon,
        out VanillaProjectileAmmoCombatDefinition ammo)
    {
        weaponItem = policy == RuntimeBotWeaponPolicy.Gun ? VanillaItemIds.Musket : VanillaItemIds.WoodenBow;
        ammoItem = policy == RuntimeBotWeaponPolicy.Gun ? VanillaItemIds.MusketBall : VanillaItemIds.WoodenArrow;
        if (policy == RuntimeBotWeaponPolicy.Melee || !Enum.IsDefined(policy) ||
            !VanillaProjectileWeaponCombatCatalog.TryGetWeapon(weaponItem, out weapon))
        {
            weapon = default;
            ammo = default;
            return false;
        }

        return weapon.AmmoFamily switch
        {
            VanillaProjectileAmmoFamily.Arrow => VanillaProjectileWeaponCombatCatalog.TryGetArrowAmmo(ammoItem, out ammo),
            VanillaProjectileAmmoFamily.Bullet => VanillaProjectileWeaponCombatCatalog.TryGetBulletAmmo(ammoItem, out ammo),
            _ => FailAmmo(out ammo)
        };
    }

    private static bool TryResolveRangedLoadout(
        RuntimeBotAttackKind attack,
        out ItemTypeId weaponItem,
        out ItemTypeId ammoItem,
        out VanillaProjectileWeaponCombatDefinition weapon,
        out VanillaProjectileAmmoCombatDefinition ammo) =>
        TryResolveRangedLoadout(
            attack switch
            {
                RuntimeBotAttackKind.Bow => RuntimeBotWeaponPolicy.Bow,
                RuntimeBotAttackKind.Gun => RuntimeBotWeaponPolicy.Gun,
                _ => RuntimeBotWeaponPolicy.Melee
            },
            out weaponItem,
            out ammoItem,
            out weapon,
            out ammo);

    private static bool FailAmmo(out VanillaProjectileAmmoCombatDefinition ammo)
    {
        ammo = default;
        return false;
    }

    private bool TryFindAmmoSlot(
        ServerPlayerId id,
        ItemTypeId requiredAmmo,
        out short slot,
        out ServerPlayerItemState item)
    {
        for (short candidate = VanillaPlayerItemSlotCatalog.AmmoSlotStart;
             candidate < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive;
             candidate++)
        {
            if (serverPlayers.TryGetItem(id, candidate, out item) &&
                !item.IsEmpty && item.ItemType == requiredAmmo && item.Prefix == VanillaPrefixIds.None)
            {
                slot = candidate;
                return true;
            }
        }
        for (short candidate = VanillaPlayerItemSlotCatalog.MainInventoryStart;
             candidate < VanillaPlayerItemSlotCatalog.MainInventoryEndExclusive;
             candidate++)
        {
            if (serverPlayers.TryGetItem(id, candidate, out item) &&
                !item.IsEmpty && item.ItemType == requiredAmmo && item.Prefix == VanillaPrefixIds.None)
            {
                slot = candidate;
                return true;
            }
        }

        slot = -1;
        item = default;
        return false;
    }

    private bool TryReplaceActor(BotState bot, RuntimeBotConfiguration next)
    {
        float x = spawnX;
        float y = spawnY;
        if (bot.Player.IsAssigned && serverPlayers.TryGet(bot.Player, out PlayerStateSnapshot player))
        {
            x = player.PositionX;
            y = player.PositionY;
        }
        else if (bot.Npc.IsAssigned && npcs.TryCapture(bot.Npc, out NpcSnapshot npc))
        {
            x = npc.PositionX;
            y = npc.PositionY;
        }

        if (next.Body == RuntimeBotBodyKind.Player)
        {
            if (!TryCreatePlayerActor(bot, x, y, mutateHandles: false, out PlayerHandle replacement))
                return false;
            if (bot.Npc.IsAssigned && !npcs.TryDespawnBotNpc(bot.Npc, bot.ControllerId))
            {
                _ = serverPlayers.Despawn(bot.ServerPlayerId);
                return false;
            }
            bot.Player = replacement;
            bot.Npc = default;
            return true;
        }

        if (!TryCreateNpcActor(bot, next.NpcType, x, y, mutateHandles: false, out NpcHandle replacementNpc))
            return false;
        if (bot.Player.IsAssigned && !serverPlayers.Despawn(bot.ServerPlayerId))
        {
            _ = npcs.TryDespawnBotNpc(replacementNpc, bot.ControllerId);
            return false;
        }
        if (bot.Npc.IsAssigned && !npcs.TryDespawnBotNpc(bot.Npc, bot.ControllerId))
        {
            _ = npcs.TryDespawnBotNpc(replacementNpc, bot.ControllerId);
            return false;
        }
        bot.Player = default;
        bot.Npc = replacementNpc;
        return true;
    }

    private bool TryCreatePlayerActor(BotState bot, float x, float y) =>
        TryCreatePlayerActor(bot, x, y, mutateHandles: true, out _);

    private bool TryCreatePlayerActor(BotState bot, float x, float y, bool mutateHandles, out PlayerHandle player)
    {
        player = default;
        ServerPlayerCreateResult created = serverPlayers.Create(bot.ServerPlayerId, x, y);
        if (created.Status != ServerPlayerCreateStatus.Created || !created.Player.IsAssigned)
            return false;

        PlayerHandle previous = bot.Player;
        bot.Player = created.Player;
        bool initialized = ApplyPlayerPresentation(bot, bot.Configuration) &&
            serverPlayers.SetVitals(bot.ServerPlayerId, new ServerPlayerVitalsState(100, 100, 20, 20)) &&
            serverPlayers.SetHostile(bot.ServerPlayerId, hostile: false) &&
            serverPlayers.SetMovementIntent(bot.ServerPlayerId, ServerPlayerMovementIntent.Stop()) &&
            EnsurePlayerLoadout(bot, bot.Configuration, addStarterAmmo: true);
        if (!initialized)
        {
            bot.Player = previous;
            _ = serverPlayers.Despawn(bot.ServerPlayerId);
            return false;
        }

        player = created.Player;
        if (!mutateHandles)
            bot.Player = previous;
        return true;
    }

    private bool TryCreateNpcActor(BotState bot, NpcTypeId type, float x, float y) =>
        TryCreateNpcActor(bot, type, x, y, mutateHandles: true, out _);

    private bool TryCreateNpcActor(BotState bot, NpcTypeId type, float x, float y, bool mutateHandles, out NpcHandle npc)
    {
        npc = default;
        if (!npcs.TrySpawnBotNpc(type, bot.ControllerId, x, y, out NpcHandle created))
            return false;
        npc = created;
        if (mutateHandles)
            bot.Npc = created;
        return true;
    }

    private bool ApplyPlayerPresentation(BotState bot, RuntimeBotConfiguration configuration)
    {
        if (!bot.Player.IsAssigned)
            return false;
        PlayerBotVisualIdentity visual = bot.VisualIdentity;
        ServerPlayerAppearanceState appearance = CreateAppearance(bot.Name, in visual);
        if (!serverPlayers.SetAppearance(bot.ServerPlayerId, in appearance))
            return false;

        ItemTypeId head = bot.VisualIdentity.HeadArmor;
        ItemTypeId body = bot.VisualIdentity.BodyArmor;
        ItemTypeId legs = bot.VisualIdentity.LegArmor;
        ItemTypeId wings = configuration.FlightEnabled ? VanillaItemIds.FishronWings : VanillaItemIds.None;
        ItemTypeId flightBooster = configuration.FlightEnabled ? VanillaItemIds.EmpressFlightBooster : VanillaItemIds.None;
        ItemTypeId boots = configuration.FlightEnabled ? VanillaItemIds.TerrasparkBoots : VanillaItemIds.None;
        ItemTypeId acceleration = configuration.FlightEnabled ? VanillaItemIds.Magiluminescence : VanillaItemIds.None;
        ItemTypeId agility = configuration.FlightEnabled ? VanillaItemIds.MasterNinjaGear : VanillaItemIds.None;
        return SetArmorSlot(bot.ServerPlayerId, VanillaPlayerItemSlotCatalog.ArmorStart, head) &&
               SetArmorSlot(bot.ServerPlayerId, checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 1)), body) &&
               SetArmorSlot(bot.ServerPlayerId, checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 2)), legs) &&
               SetArmorSlot(bot.ServerPlayerId, checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 3)), wings) &&
               SetArmorSlot(bot.ServerPlayerId, checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 4)), flightBooster) &&
               SetArmorSlot(bot.ServerPlayerId, checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 5)), boots) &&
               SetArmorSlot(bot.ServerPlayerId, checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 6)), acceleration) &&
               SetArmorSlot(bot.ServerPlayerId, checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 7)), agility);
    }

    private bool EnsurePlayerLoadout(BotState bot, RuntimeBotConfiguration configuration, bool addStarterAmmo)
    {
        if (!bot.Player.IsAssigned)
            return false;

        ItemTypeId firstWeapon = configuration.WeaponPolicy switch
        {
            RuntimeBotWeaponPolicy.Melee => VanillaItemIds.CopperBroadsword,
            RuntimeBotWeaponPolicy.Gun => VanillaItemIds.Musket,
            RuntimeBotWeaponPolicy.Bow => VanillaItemIds.WoodenBow,
            RuntimeBotWeaponPolicy.Automatic => VanillaItemIds.CopperBroadsword,
            _ => VanillaItemIds.None
        };
        if (firstWeapon.IsNone || !serverPlayers.SetItem(
                bot.ServerPlayerId,
                new ServerPlayerItemState(MeleeWeaponSlot, firstWeapon, 1, VanillaPrefixIds.None, 0)))
        {
            return false;
        }

        ItemTypeId secondWeapon = configuration.WeaponPolicy == RuntimeBotWeaponPolicy.Automatic
            ? VanillaItemIds.WoodenBow
            : VanillaItemIds.None;
        ItemTypeId thirdWeapon = configuration.WeaponPolicy == RuntimeBotWeaponPolicy.Automatic
            ? VanillaItemIds.Musket
            : VanillaItemIds.None;
        if (!SetInventorySlot(bot.ServerPlayerId, BowWeaponSlot, secondWeapon) ||
            !SetInventorySlot(bot.ServerPlayerId, GunWeaponSlot, thirdWeapon) ||
            !serverPlayers.SetHeldItem(bot.ServerPlayerId, MeleeWeaponSlot, useItem: false))
        {
            return false;
        }

        if (!addStarterAmmo || configuration.WeaponPolicy == RuntimeBotWeaponPolicy.Melee)
            return true;

        if (configuration.WeaponPolicy == RuntimeBotWeaponPolicy.Automatic)
        {
            return EnsureStarterAmmo(bot.ServerPlayerId, VanillaItemIds.WoodenArrow) &&
                   EnsureStarterAmmo(bot.ServerPlayerId, VanillaItemIds.MusketBall);
        }

        return TryResolveRangedLoadout(configuration.WeaponPolicy, out _, out ItemTypeId ammoType, out _, out _) &&
               EnsureStarterAmmo(bot.ServerPlayerId, ammoType);
    }

    private bool EnsureStarterAmmo(ServerPlayerId id, ItemTypeId ammoType)
    {
        if (TryFindAmmoSlot(id, ammoType, out _, out _))
            return true;

        for (short slot = VanillaPlayerItemSlotCatalog.AmmoSlotStart;
             slot < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive;
             slot++)
        {
            if (!serverPlayers.TryGetItem(id, slot, out ServerPlayerItemState current))
                return false;
            if (!current.IsEmpty)
                continue;
            var starter = new ServerPlayerItemState(slot, ammoType, StarterAmmoStack, VanillaPrefixIds.None, 0);
            return serverPlayers.SetItem(id, in starter);
        }

        return false;
    }

    private bool SetInventorySlot(ServerPlayerId id, short slot, ItemTypeId item)
    {
        ServerPlayerItemState state = item.IsNone
            ? new ServerPlayerItemState(slot, VanillaItemIds.None, 0, VanillaPrefixIds.None, 0)
            : new ServerPlayerItemState(slot, item, 1, VanillaPrefixIds.None, 0);
        return serverPlayers.SetItem(id, in state);
    }

    private bool SetArmorSlot(ServerPlayerId id, short slot, ItemTypeId item)
    {
        ServerPlayerItemState state = item.IsNone
            ? new ServerPlayerItemState(slot, VanillaItemIds.None, 0, VanillaPrefixIds.None, 0)
            : new ServerPlayerItemState(slot, item, 1, VanillaPrefixIds.None, 0);
        return serverPlayers.SetItem(id, in state);
    }

    private static bool IsValidConfiguration(RuntimeBotConfiguration configuration) =>
        Enum.IsDefined(configuration.Mode) &&
        Enum.IsDefined(configuration.Body) &&
        Enum.IsDefined(configuration.WeaponPolicy) &&
        IsSupportedBody(configuration.Body, configuration.NpcType);

    private static bool IsSupportedBody(RuntimeBotBodyKind body, NpcTypeId npcType)
    {
        if (body == RuntimeBotBodyKind.Player)
            return npcType == default;
        return body == RuntimeBotBodyKind.Npc && VanillaBotNpcPresetCatalog1458.IsSupported(npcType);
    }

    private static void ResetBehaviorState(BotState bot, long tick)
    {
        bot.TargetAvailable = false;
        bot.PvpEnabled = false;
        bot.IsStuck = false;
        bot.LastDistance = float.PositiveInfinity;
        bot.LastProgressTick = tick;
        bot.NextAttackTick = 0;
        bot.UseItemUntilTick = 0;
        bot.FlightDecisionUntilTick = 0;
    }

    private void ResetUnavailableTarget(BotState bot, long tick)
    {
        bot.TargetAvailable = false;
        bot.PvpEnabled = false;
        bot.IsStuck = false;
        bot.LastDistance = float.PositiveInfinity;
        bot.LastProgressTick = tick;
        bot.FlightDecisionUntilTick = 0;
    }

    private void StopActor(BotState bot)
    {
        if (bot.Configuration.Body == RuntimeBotBodyKind.Player && bot.Player.IsAssigned)
        {
            _ = serverPlayers.SetMovementIntent(bot.ServerPlayerId, ServerPlayerMovementIntent.Stop());
            _ = serverPlayers.SetHostile(bot.ServerPlayerId, hostile: false);
            if (serverPlayers.TryGet(bot.Player, out PlayerStateSnapshot player))
                _ = serverPlayers.SetHeldItem(bot.ServerPlayerId, player.SelectedItem, useItem: false);
        }
        else if (bot.Configuration.Body == RuntimeBotBodyKind.Npc && bot.Npc.IsAssigned)
        {
            NpcActorIntent stop = NpcActorIntent.Stop();
            _ = npcs.TrySetBotNpcIntent(bot.Npc, bot.ControllerId, in stop);
        }
    }

    private static ServerPlayerAppearanceState CreateAppearance(string name, in PlayerBotVisualIdentity visual)
    {
        return new ServerPlayerAppearanceState(
            SkinVariant: visual.SkinVariant,
            VoiceVariant: visual.VoiceVariant,
            VoicePitchOffset: visual.VoicePitchOffset,
            Hair: visual.Hair,
            Name: name,
            HairDye: 0,
            HideVisibleAccessory: 0,
            HideMisc: 0,
            HairColor: visual.HairColor,
            SkinColor: visual.SkinColor,
            EyeColor: visual.EyeColor,
            ShirtColor: visual.ShirtColor,
            UnderShirtColor: visual.UnderShirtColor,
            PantsColor: visual.PantsColor,
            ShoeColor: visual.ShoeColor,
            DifficultyFlags: 0,
            TorchAndCartFlags: 0,
            ConsumableUnlockFlags: 0);
    }

    private static PlayerBotPersonality CreatePersonality(int id)
    {
        uint state = Mix(unchecked((uint)id) ^ 0xA511E9B3u);
        int ring = (id - 1) / 2;
        float side = (id & 1) == 0 ? 1f : -1f;
        float radialJitter = Next(ref state, 0, 17);
        float offsetX = side * (MinimumPlayerEscortOffsetPixels + ring % 5 * 32f + radialJitter);
        float offsetY = -72f - Next(ref state, 0, 49);
        long phase = Next(ref state, 0, checked((int)EscortWanderPeriodTicks));
        return new PlayerBotPersonality(offsetX, offsetY, phase);
    }

    private static PlayerBotVisualIdentity CreateVisualIdentity(int id)
    {
        uint state = Mix(unchecked((uint)id) ^ 0x6D2B79F5u);
        int armor = Next(ref state, 0, 6);
        (ItemTypeId head, ItemTypeId body, ItemTypeId legs) = armor switch
        {
            1 => (VanillaItemIds.WoodHelmet, VanillaItemIds.WoodBreastplate, VanillaItemIds.WoodGreaves),
            2 => (VanillaItemIds.CopperHelmet, VanillaItemIds.CopperChainmail, VanillaItemIds.CopperGreaves),
            3 => (VanillaItemIds.IronHelmet, VanillaItemIds.IronChainmail, VanillaItemIds.IronGreaves),
            4 => (VanillaItemIds.SilverHelmet, VanillaItemIds.SilverChainmail, VanillaItemIds.SilverGreaves),
            5 => (VanillaItemIds.GoldHelmet, VanillaItemIds.GoldChainmail, VanillaItemIds.GoldGreaves),
            _ => (VanillaItemIds.None, VanillaItemIds.None, VanillaItemIds.None)
        };

        (PlayerRgbColor shirt, PlayerRgbColor undershirt, PlayerRgbColor pants, PlayerRgbColor shoes) =
            Next(ref state, 0, 6) switch
            {
                0 => (new PlayerRgbColor(30, 110, 170), new PlayerRgbColor(180, 180, 180), new PlayerRgbColor(45, 70, 120), new PlayerRgbColor(65, 45, 30)),
                1 => (new PlayerRgbColor(54, 110, 63), new PlayerRgbColor(103, 148, 91), new PlayerRgbColor(62, 74, 57), new PlayerRgbColor(43, 35, 29)),
                2 => (new PlayerRgbColor(145, 45, 52), new PlayerRgbColor(92, 28, 36), new PlayerRgbColor(57, 48, 54), new PlayerRgbColor(36, 30, 33)),
                3 => (new PlayerRgbColor(110, 110, 110), new PlayerRgbColor(72, 72, 72), new PlayerRgbColor(48, 48, 48), new PlayerRgbColor(28, 28, 28)),
                4 => (new PlayerRgbColor(120, 72, 168), new PlayerRgbColor(203, 170, 228), new PlayerRgbColor(50, 43, 95), new PlayerRgbColor(40, 28, 62)),
                _ => (new PlayerRgbColor(205, 128, 34), new PlayerRgbColor(244, 211, 121), new PlayerRgbColor(58, 92, 105), new PlayerRgbColor(48, 34, 25))
            };
        PlayerRgbColor skin = Next(ref state, 0, 5) switch
        {
            0 => new(255, 215, 180),
            1 => new(232, 190, 151),
            2 => new(198, 142, 105),
            3 => new(141, 92, 65),
            _ => new(92, 61, 48)
        };
        var hair = new PlayerRgbColor(
            checked((byte)Next(ref state, 30, 221)),
            checked((byte)Next(ref state, 25, 196)),
            checked((byte)Next(ref state, 20, 171)));
        var eyes = new PlayerRgbColor(
            checked((byte)Next(ref state, 40, 181)),
            checked((byte)Next(ref state, 60, 201)),
            checked((byte)Next(ref state, 70, 221)));
        return new PlayerBotVisualIdentity(
            checked((byte)Next(ref state, 0, VanillaPlayerAppearanceNormalizer.PlayerVariantCount)),
            checked((byte)Next(ref state, 1, 5)),
            (Next(ref state, -20, 21) / 100f),
            checked((byte)Next(ref state, 0, VanillaPlayerAppearanceNormalizer.HairCount)),
            hair,
            skin,
            eyes,
            shirt,
            undershirt,
            pants,
            shoes,
            head,
            body,
            legs);
    }

    private static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        return value ^ (value >> 16);
    }

    private static int Next(ref uint state, int minimumInclusive, int maximumExclusive)
    {
        state = Mix(state + 0x9E3779B9u);
        return minimumInclusive + (int)(state % checked((uint)(maximumExclusive - minimumInclusive)));
    }

    private void PublishTelemetry(long tick, bool force)
    {
        if (!force && tick - lastTelemetryTick < TelemetryPeriodTicks)
            return;
        lastTelemetryTick = tick;
        RuntimeBotSnapshot[] snapshots = new RuntimeBotSnapshot[bots.Count];
        int index = 0;
        foreach (BotState bot in bots.Values.OrderBy(static value => value.Id))
            snapshots[index++] = Capture(bot);
        telemetry.Publish(snapshots);
    }

    private static RuntimeBotSnapshot Capture(BotState bot) => new(
        bot.Id,
        bot.ServerPlayerId,
        bot.Player,
        bot.Npc,
        bot.Name,
        bot.Configuration,
        bot.TargetAvailable,
        bot.PvpEnabled,
        bot.IsStuck,
        bot.TeleportCount,
        DateTimeOffset.UtcNow);

    private static float DistanceSquared(float x1, float y1, float x2, float y2)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        return dx * dx + dy * dy;
    }

    private static bool RectanglesIntersect(
        float leftA, float topA, float widthA, float heightA,
        float leftB, float topB, float widthB, float heightB) =>
        leftA < leftB + widthB && leftA + widthA > leftB &&
        topA < topB + heightB && topA + heightA > topB;

    private enum RuntimeBotAttackKind : byte
    {
        Melee,
        Bow,
        Gun
    }

    private readonly record struct BotGuardTarget(
        NpcHandle Npc,
        PlayerHandle Player,
        float CenterX,
        float CenterY,
        float VelocityX,
        float VelocityY,
        float Width,
        float Height);

    private readonly record struct PlayerBotPersonality(
        float EscortOffsetX,
        float EscortOffsetY,
        long WanderPhaseTicks);

    private readonly record struct PlayerBotVisualIdentity(
        byte SkinVariant,
        byte VoiceVariant,
        float VoicePitchOffset,
        byte Hair,
        PlayerRgbColor HairColor,
        PlayerRgbColor SkinColor,
        PlayerRgbColor EyeColor,
        PlayerRgbColor ShirtColor,
        PlayerRgbColor UnderShirtColor,
        PlayerRgbColor PantsColor,
        PlayerRgbColor ShoeColor,
        ItemTypeId HeadArmor,
        ItemTypeId BodyArmor,
        ItemTypeId LegArmor);

    private sealed class BotState(
        int id,
        ServerPlayerId serverPlayerId,
        ActorControllerId controllerId,
        string name,
        RuntimeBotConfiguration configuration,
        PlayerBotVisualIdentity visualIdentity,
        PlayerBotPersonality personality,
        long createdAtTick)
    {
        public int Id { get; } = id;
        public ServerPlayerId ServerPlayerId { get; } = serverPlayerId;
        public ActorControllerId ControllerId { get; } = controllerId;
        public PlayerHandle Player { get; set; }
        public NpcHandle Npc { get; set; }
        public string Name { get; } = name;
        public RuntimeBotConfiguration Configuration { get; set; } = configuration;
        public PlayerBotVisualIdentity VisualIdentity { get; } = visualIdentity;
        public PlayerBotPersonality Personality { get; } = personality;
        public bool TargetAvailable { get; set; }
        public bool PvpEnabled { get; set; }
        public bool IsStuck { get; set; }
        public long TeleportCount { get; set; }
        public long LastProgressTick { get; set; } = createdAtTick;
        public long TeleportCooldownUntil { get; set; }
        public long FlightDecisionUntilTick { get; set; }
        public long NextAttackTick { get; set; }
        public long UseItemUntilTick { get; set; }
        public long PotionDelayUntilTick { get; set; }
        public float LastDistance { get; set; } = float.PositiveInfinity;
        public Dictionary<BuffTypeId, long> ActiveBuffs { get; } = [];
    }
}
