using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Bots;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.HostContracts;

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

    private readonly ServerPlayerAuthority serverPlayers;
    private readonly PlayerAuthority players;
    private readonly RuntimePlayerSnapshotLookup playerSnapshots;
    private readonly NpcAuthority npcs;
    private readonly ProjectileAuthority projectiles;
    private readonly WorldItemAuthority worldItems;
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
            RuntimeBotClothingPreset.Classic,
            RuntimeBotArmorPreset.None,
            RuntimeBotMode.Idle,
            default,
            Body: request.Body,
            NpcType: request.NpcType,
            WeaponPolicy: RuntimeBotWeaponPolicy.Automatic,
            FlightEnabled: true,
            AutoPickup: true,
            AutoUseConsumables: true);
        var state = new BotState(id, serverId, controllerId, name, configuration, tickProvider());

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

        RuntimeBotConfiguration configuration = bot.Configuration;
        if (configuration.AutoPickup)
            TryPickupOneUsefulItem(bot, in self);
        if (configuration.AutoUseConsumables)
        {
            TryAutoHeal(bot, in self, tick);
            if (serverPlayers.TryGet(bot.Player, out PlayerStateSnapshot refreshed))
                self = refreshed;
        }

        if (!TryResolveLiveTarget(bot, out PlayerStateSnapshot target))
        {
            ResetUnavailableTarget(bot, tick);
            _ = serverPlayers.SetMovementIntent(bot.ServerPlayerId, ServerPlayerMovementIntent.Stop());
            if (self.Hostile)
                _ = serverPlayers.SetHostile(bot.ServerPlayerId, hostile: false);
            return;
        }

        bot.TargetAvailable = true;
        bool pvp = target.Hostile;
        bot.PvpEnabled = pvp;
        if (self.Hostile != pvp)
            _ = serverPlayers.SetHostile(bot.ServerPlayerId, pvp);

        _ = serverPlayers.SetMovementIntent(
            bot.ServerPlayerId,
            ServerPlayerMovementIntent.FollowPlayer(configuration.Target.Player));

        UpdatePlayerStuckRecovery(bot, in self, in target, tick);

        if (configuration.AutoUseConsumables && configuration.Mode == RuntimeBotMode.Guard)
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
        NpcActorIntent follow = NpcActorIntent.FollowPlayer(target.Player);
        _ = npcs.TrySetBotNpcIntent(bot.Npc, bot.ControllerId, in follow);
        UpdateNpcStuckRecovery(bot, in self, in target, tick);
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

        if (serverPlayers.TryTeleport(bot.ServerPlayerId, target.PositionX, target.PositionY))
            CommitTeleport(bot, tick);
    }

    private void UpdateNpcStuckRecovery(
        BotState bot,
        in NpcSnapshot self,
        in PlayerStateSnapshot target,
        long tick)
    {
        float distanceSquared = DistanceSquared(self.PositionX, self.PositionY, target.PositionX, target.PositionY);
        UpdateProgress(bot, distanceSquared, tick);
        if (!ShouldTeleport(bot, distanceSquared, tick))
            return;

        if (npcs.TryTeleportBotNpc(bot.Npc, bot.ControllerId, target.PositionX, target.PositionY))
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

    private void TryGuardAttack(BotState bot, in PlayerStateSnapshot self, in PlayerStateSnapshot protectedPlayer, long tick)
    {
        if (!TryFindGuardAim(bot, in protectedPlayer, out float aimX, out float aimY) ||
            !TryResolveRangedLoadout(bot.Configuration.WeaponPolicy, out ItemTypeId weaponItem, out ItemTypeId ammoItem,
                out VanillaProjectileWeaponCombatDefinition weapon, out VanillaProjectileAmmoCombatDefinition ammo) ||
            !TryFindAmmoSlot(bot.ServerPlayerId, ammoItem, out short ammoSlot, out ServerPlayerItemState ammoState) ||
            !VanillaProjectileWeaponCombatCatalog.TryResolveProjectileType(in weapon, in ammo, out ProjectileTypeId projectileType))
        {
            return;
        }

        VanillaPlayerCombatSnapshot combat = BuildBotCombatSnapshot(bot, tick);
        VanillaCombatPrefixModifiers prefix = VanillaCombatPrefixModifiers.Identity;
        VanillaLaunchSpeedEnvelope speedEnvelope = VanillaProjectileWeaponCombatCatalog.ResolveLaunchSpeedEnvelope(
            in weapon, in ammo, in prefix, in combat);
        int damage = VanillaProjectileWeaponCombatCatalog.ResolveDamage(in weapon, in ammo, in prefix, in combat);
        float knockBack = VanillaProjectileWeaponCombatCatalog.ResolveKnockBack(in weapon, in ammo, in prefix, in combat);
        if (!speedEnvelope.IsValid || damage <= 0 || damage > short.MaxValue || !float.IsFinite(knockBack))
            return;

        float originX = self.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float originY = self.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float dx = aimX - originX;
        float dy = aimY - originY;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (!(length > 0.001f) || !float.IsFinite(length))
            return;

        float speed = speedEnvelope.CanonicalMagnitude;
        if (weapon.AmmoFamily == VanillaProjectileAmmoFamily.Arrow &&
            IsBuffActive(bot, VanillaBuffIds.Archery, tick) && speed < 20f)
        {
            speed = Math.Min(20f, speed * 1.2f);
        }

        // Consume first, rollback if projectile allocation fails. This keeps the world-writer path duplication-safe.
        var consumed = ammoState.Stack == 1
            ? new ServerPlayerItemState(ammoSlot, VanillaItemIds.None, 0, VanillaPrefixIds.None, 0)
            : ammoState with { Stack = checked((short)(ammoState.Stack - 1)) };
        if (!serverPlayers.SetItem(bot.ServerPlayerId, in consumed))
            return;

        var projectile = new ProjectileStateUpdate(
            projectileType,
            bot.Player.Slot.Value,
            originX,
            originY,
            dx / length * speed,
            dy / length * speed,
            default,
            BannerIdToRespondTo: 0,
            Damage: checked((short)damage),
            KnockBack: knockBack,
            OriginalDamage: 0);
        if (!projectiles.TrySpawnTrustedServerPlayerProjectile(bot.Player, in projectile, out _))
        {
            if (!serverPlayers.SetItem(bot.ServerPlayerId, in ammoState))
                throw new InvalidOperationException("Bot ammo rollback failed after rejected trusted projectile spawn.");
            return;
        }

        bot.NextAttackTick = tick + Math.Max(1, weapon.UseTimeTicks);
        _ = weaponItem; // Documents the configured held weapon used to resolve this source-backed shot.
    }

    private bool TryFindGuardAim(
        BotState bot,
        in PlayerStateSnapshot protectedPlayer,
        out float aimX,
        out float aimY)
    {
        aimX = 0f;
        aimY = 0f;
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
            if (distanceSquared > GuardRadiusSquared || distanceSquared >= bestDistanceSquared)
                continue;
            bestDistanceSquared = distanceSquared;
            aimX = centerX;
            aimY = centerY;
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
                if (distanceSquared > GuardRadiusSquared || distanceSquared >= bestDistanceSquared)
                    continue;
                bestDistanceSquared = distanceSquared;
                aimX = centerX;
                aimY = centerY;
            }
        }

        return float.IsFinite(bestDistanceSquared);
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
                TryResolveRangedLoadout(bot.Configuration.WeaponPolicy, out _, out ItemTypeId requiredAmmo, out _, out _) &&
                itemType == requiredAmmo,
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
            snapshot = snapshot with { RangedDamage = snapshot.RangedDamage + 0.1f };
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
            return policy is RuntimeBotWeaponPolicy.Automatic or RuntimeBotWeaponPolicy.Bow or RuntimeBotWeaponPolicy.Gun;
        return buff == VanillaBuffIds.Archery && policy is RuntimeBotWeaponPolicy.Automatic or RuntimeBotWeaponPolicy.Bow;
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
        ServerPlayerAppearanceState appearance = CreateAppearance(bot.Name, configuration.Clothing);
        if (!serverPlayers.SetAppearance(bot.ServerPlayerId, in appearance))
            return false;

        ResolveArmor(configuration.Armor, out ItemTypeId head, out ItemTypeId body, out ItemTypeId legs);
        return SetArmorSlot(bot.ServerPlayerId, VanillaPlayerItemSlotCatalog.ArmorStart, head) &&
               SetArmorSlot(bot.ServerPlayerId, checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 1)), body) &&
               SetArmorSlot(bot.ServerPlayerId, checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 2)), legs);
    }

    private bool EnsurePlayerLoadout(BotState bot, RuntimeBotConfiguration configuration, bool addStarterAmmo)
    {
        if (!bot.Player.IsAssigned)
            return false;

        ItemTypeId weapon = configuration.WeaponPolicy switch
        {
            RuntimeBotWeaponPolicy.Melee => VanillaItemIds.CopperBroadsword,
            RuntimeBotWeaponPolicy.Gun => VanillaItemIds.Musket,
            RuntimeBotWeaponPolicy.Automatic or RuntimeBotWeaponPolicy.Bow => VanillaItemIds.WoodenBow,
            _ => VanillaItemIds.None
        };
        if (weapon.IsNone || !serverPlayers.SetItem(
                bot.ServerPlayerId,
                new ServerPlayerItemState(0, weapon, 1, VanillaPrefixIds.None, 0)))
        {
            return false;
        }

        if (!addStarterAmmo || configuration.WeaponPolicy == RuntimeBotWeaponPolicy.Melee ||
            !TryResolveRangedLoadout(configuration.WeaponPolicy, out _, out ItemTypeId ammoType, out _, out _))
        {
            return true;
        }
        if (TryFindAmmoSlot(bot.ServerPlayerId, ammoType, out _, out _))
            return true;

        for (short slot = VanillaPlayerItemSlotCatalog.AmmoSlotStart;
             slot < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive;
             slot++)
        {
            if (!serverPlayers.TryGetItem(bot.ServerPlayerId, slot, out ServerPlayerItemState current))
                return false;
            if (!current.IsEmpty)
                continue;
            var starter = new ServerPlayerItemState(slot, ammoType, StarterAmmoStack, VanillaPrefixIds.None, 0);
            return serverPlayers.SetItem(bot.ServerPlayerId, in starter);
        }

        return true;
    }

    private bool SetArmorSlot(ServerPlayerId id, short slot, ItemTypeId item)
    {
        ServerPlayerItemState state = item.IsNone
            ? new ServerPlayerItemState(slot, VanillaItemIds.None, 0, VanillaPrefixIds.None, 0)
            : new ServerPlayerItemState(slot, item, 1, VanillaPrefixIds.None, 0);
        return serverPlayers.SetItem(id, in state);
    }

    private static bool IsValidConfiguration(RuntimeBotConfiguration configuration) =>
        Enum.IsDefined(configuration.Clothing) &&
        Enum.IsDefined(configuration.Armor) &&
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
    }

    private void ResetUnavailableTarget(BotState bot, long tick)
    {
        bot.TargetAvailable = false;
        bot.PvpEnabled = false;
        bot.IsStuck = false;
        bot.LastDistance = float.PositiveInfinity;
        bot.LastProgressTick = tick;
    }

    private void StopActor(BotState bot)
    {
        if (bot.Configuration.Body == RuntimeBotBodyKind.Player && bot.Player.IsAssigned)
        {
            _ = serverPlayers.SetMovementIntent(bot.ServerPlayerId, ServerPlayerMovementIntent.Stop());
            _ = serverPlayers.SetHostile(bot.ServerPlayerId, hostile: false);
        }
        else if (bot.Configuration.Body == RuntimeBotBodyKind.Npc && bot.Npc.IsAssigned)
        {
            NpcActorIntent stop = NpcActorIntent.Stop();
            _ = npcs.TrySetBotNpcIntent(bot.Npc, bot.ControllerId, in stop);
        }
    }

    private static void ResolveArmor(
        RuntimeBotArmorPreset preset,
        out ItemTypeId head,
        out ItemTypeId body,
        out ItemTypeId legs)
    {
        (head, body, legs) = preset switch
        {
            RuntimeBotArmorPreset.None => (VanillaItemIds.None, VanillaItemIds.None, VanillaItemIds.None),
            RuntimeBotArmorPreset.Wood => (VanillaItemIds.WoodHelmet, VanillaItemIds.WoodBreastplate, VanillaItemIds.WoodGreaves),
            RuntimeBotArmorPreset.Copper => (VanillaItemIds.CopperHelmet, VanillaItemIds.CopperChainmail, VanillaItemIds.CopperGreaves),
            RuntimeBotArmorPreset.Iron => (VanillaItemIds.IronHelmet, VanillaItemIds.IronChainmail, VanillaItemIds.IronGreaves),
            RuntimeBotArmorPreset.Silver => (VanillaItemIds.SilverHelmet, VanillaItemIds.SilverChainmail, VanillaItemIds.SilverGreaves),
            RuntimeBotArmorPreset.Gold => (VanillaItemIds.GoldHelmet, VanillaItemIds.GoldChainmail, VanillaItemIds.GoldGreaves),
            _ => (VanillaItemIds.None, VanillaItemIds.None, VanillaItemIds.None)
        };
    }

    private static ServerPlayerAppearanceState CreateAppearance(string name, RuntimeBotClothingPreset preset)
    {
        (PlayerRgbColor shirt, PlayerRgbColor undershirt, PlayerRgbColor pants, PlayerRgbColor shoes, PlayerRgbColor hair) = preset switch
        {
            RuntimeBotClothingPreset.Forest => (
                new PlayerRgbColor(54, 110, 63), new PlayerRgbColor(103, 148, 91),
                new PlayerRgbColor(62, 74, 57), new PlayerRgbColor(43, 35, 29), new PlayerRgbColor(92, 64, 38)),
            RuntimeBotClothingPreset.Crimson => (
                new PlayerRgbColor(145, 45, 52), new PlayerRgbColor(92, 28, 36),
                new PlayerRgbColor(57, 48, 54), new PlayerRgbColor(36, 30, 33), new PlayerRgbColor(48, 30, 24)),
            RuntimeBotClothingPreset.Monochrome => (
                new PlayerRgbColor(110, 110, 110), new PlayerRgbColor(72, 72, 72),
                new PlayerRgbColor(48, 48, 48), new PlayerRgbColor(28, 28, 28), new PlayerRgbColor(38, 38, 38)),
            _ => (
                new PlayerRgbColor(30, 110, 170), new PlayerRgbColor(180, 180, 180),
                new PlayerRgbColor(45, 70, 120), new PlayerRgbColor(65, 45, 30), new PlayerRgbColor(80, 55, 35))
        };

        return new ServerPlayerAppearanceState(
            SkinVariant: 0,
            VoiceVariant: 0,
            VoicePitchOffset: 0f,
            Hair: 0,
            Name: name,
            HairDye: 0,
            HideVisibleAccessory: 0,
            HideMisc: 0,
            HairColor: hair,
            SkinColor: new PlayerRgbColor(255, 215, 180),
            EyeColor: new PlayerRgbColor(75, 105, 145),
            ShirtColor: shirt,
            UnderShirtColor: undershirt,
            PantsColor: pants,
            ShoeColor: shoes,
            DifficultyFlags: 0,
            TorchAndCartFlags: 0,
            ConsumableUnlockFlags: 0);
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

    private sealed class BotState(
        int id,
        ServerPlayerId serverPlayerId,
        ActorControllerId controllerId,
        string name,
        RuntimeBotConfiguration configuration,
        long createdAtTick)
    {
        public int Id { get; } = id;
        public ServerPlayerId ServerPlayerId { get; } = serverPlayerId;
        public ActorControllerId ControllerId { get; } = controllerId;
        public PlayerHandle Player { get; set; }
        public NpcHandle Npc { get; set; }
        public string Name { get; } = name;
        public RuntimeBotConfiguration Configuration { get; set; } = configuration;
        public bool TargetAvailable { get; set; }
        public bool PvpEnabled { get; set; }
        public bool IsStuck { get; set; }
        public long TeleportCount { get; set; }
        public long LastProgressTick { get; set; } = createdAtTick;
        public long TeleportCooldownUntil { get; set; }
        public long NextAttackTick { get; set; }
        public long PotionDelayUntilTick { get; set; }
        public float LastDistance { get; set; } = float.PositiveInfinity;
        public Dictionary<BuffTypeId, long> ActiveBuffs { get; } = [];
    }
}
