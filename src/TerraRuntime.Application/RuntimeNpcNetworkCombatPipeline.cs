using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal enum RuntimeNpcNetworkDamageResult : byte
{
    Rejected = 0,
    Relayed = 1,
    Committed = 2,
    Killed = 3
}

internal enum RuntimeProjectileNpcDamageResult : byte
{
    Rejected = 0,
    Committed = 1,
    Killed = 2
}

/// <summary>
/// Authoritative packet-28 bridge. It resolves wrapped wire generations against the live slot, preserves the
/// TerrariaServer ordering PlayerInteraction -> StrikeNPC -> imported loot -> boss death effects -> despawn -> packet 28
/// -> packet 23, including shared Eater-of-Worlds interaction credit and last-segment boss promotion. Socket threads never
/// mutate runtime entity state directly.
/// </summary>
internal sealed partial class RuntimeNpcNetworkCombatPipeline : IRuntimeTownNpcMeleeDamageSink1458, INpcAiStateCommitSink
{
    private const int MaxOrdinaryDrops = 16;
    private const float VanillaPlayerWidth = 20f;
    private const float VanillaPlayerHeight = 42f;

    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeWorldItemStore worldItems;
    private readonly RuntimeNpcPlayerInteractionLedger interactions;
    private readonly RuntimeNpcDamageExecutor damage;
    private readonly RuntimeNpcReplicationRegistry? npcReplication;
    private readonly IRuntimePlayerSlotSnapshotLookup players;
    private readonly RuntimeCombatIntegrity combatIntegrity;
    private readonly Func<long> tickProvider;
    private readonly RuntimeKingSlimeDifficultyLootDeliverySink? difficultyLoot;
    private readonly RuntimeEaterOfWorldsLootDeliverySink eaterLoot;
    private readonly RuntimeBrainOfCthulhuLootDeliverySink brainLoot;
    private readonly RuntimeSkeletronLootDeliverySink skeletronLoot;
    private readonly RuntimeQueenBeeLootDeliverySink queenBeeLoot;
    private readonly RuntimeDeerclopsLootDeliverySink deerclopsLoot;
    private readonly RuntimeWallOfFleshLootDeliverySink wallOfFleshLoot;
    private readonly VanillaNpcLootWorldItemMaterializer materializer = VanillaNpcLootWorldItemMaterializer.Instance;
    private readonly SystemNpcCombatRandom random = new();
    private readonly bool expertMode;
    private readonly bool masterMode;
    private readonly RuntimeWorldClock? worldClock;
    private readonly RuntimeWorldProgressionMutations progression;
    private readonly WorldTileStore? worldTiles;
    private readonly bool crimsonWorld;
    private readonly bool skyblockLowTiles;
    private readonly bool isThereAWorldSurface;
    private readonly bool evilBossDownedBaseline;
    private readonly PlayerSlotId[] interactionSlots =
        new PlayerSlotId[VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots];
    private readonly VanillaKingSlimeLootPlayer[] activeLootPlayers =
        new VanillaKingSlimeLootPlayer[VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots];
    private readonly VanillaEaterOfWorldsLootPlayer[] activeEaterLootPlayers =
        new VanillaEaterOfWorldsLootPlayer[VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots];
    private readonly VanillaBrainOfCthulhuLootPlayer[] activeBrainLootPlayers =
        new VanillaBrainOfCthulhuLootPlayer[VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots];
    private readonly VanillaSkeletronLootPlayer[] activeSkeletronLootPlayers =
        new VanillaSkeletronLootPlayer[VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots];
    private readonly VanillaQueenBeeLootPlayer[] activeQueenBeeLootPlayers =
        new VanillaQueenBeeLootPlayer[VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots];
    private readonly VanillaDeerclopsLootPlayer[] activeDeerclopsLootPlayers =
        new VanillaDeerclopsLootPlayer[VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots];
    private readonly VanillaWallOfFleshLootPlayer[] activeWallOfFleshLootPlayers =
        new VanillaWallOfFleshLootPlayer[VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots];
    private readonly NpcSnapshot[] npcFamilyBuffer;

    internal RuntimeNpcPlayerInteractionLedger Interactions => interactions;

    internal int CopyCombatIntegrityDiagnostics(Span<CombatIntegrityDiagnostic> destination) =>
        combatIntegrity.CopyRecentDiagnostics(destination);

    public RuntimeNpcNetworkCombatPipeline(
        RuntimeNpcStore npcs,
        RuntimeWorldItemStore worldItems,
        IRuntimePlayerSlotSnapshotLookup players,
        PlayerAuthority playerAuthority,
        Func<long> tickProvider,
        RuntimeNpcReplicationRegistry? npcReplication,
        RuntimeWorldItemInstancedLeaseStore instancedLeases,
        RuntimeWorldItemReplicationRegistry? worldItemReplication,
        RuntimeWorldClock? worldClock,
        RuntimeWorldProgressionMutations progression,
        bool expertMode,
        bool masterMode,
        WorldTileStore? worldTiles = null,
        bool crimsonWorld = false,
        bool skyblockLowTiles = false,
        bool isThereAWorldSurface = true,
        bool evilBossDownedBaseline = false,
        RuntimeProjectileStore? projectiles = null)
    {
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        moonLordProjectiles = projectiles;
        moonLordProjectileBuffer = projectiles is null ? [] : new ProjectileSnapshot[projectiles.Capacity];
        this.worldItems = worldItems ?? throw new ArgumentNullException(nameof(worldItems));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        ArgumentNullException.ThrowIfNull(playerAuthority);
        this.tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
        combatIntegrity = new RuntimeCombatIntegrity(playerAuthority, npcs.Capacity);
        this.npcReplication = npcReplication;
        this.worldClock = worldClock;
        this.progression = progression ?? throw new ArgumentNullException(nameof(progression));
        this.worldTiles = worldTiles;
        this.crimsonWorld = crimsonWorld;
        this.skyblockLowTiles = skyblockLowTiles;
        this.isThereAWorldSurface = isThereAWorldSurface;
        this.evilBossDownedBaseline = evilBossDownedBaseline;
        this.expertMode = expertMode;
        this.masterMode = masterMode;
        if (masterMode && !expertMode)
            throw new ArgumentException("Master mode is a strict subset of Expert mode.", nameof(masterMode));

        interactions = new RuntimeNpcPlayerInteractionLedger(npcs);
        damage = new RuntimeNpcDamageExecutor(npcs, expertMode, interactions);
        npcFamilyBuffer = new NpcSnapshot[npcs.Capacity];
        eaterLoot = new RuntimeEaterOfWorldsLootDeliverySink(
            worldItems,
            instancedLeases,
            worldItemReplication);
        brainLoot = new RuntimeBrainOfCthulhuLootDeliverySink(
            worldItems,
            instancedLeases,
            worldItemReplication);
        skeletronLoot = new RuntimeSkeletronLootDeliverySink(
            worldItems,
            instancedLeases,
            worldItemReplication);
        queenBeeLoot = new RuntimeQueenBeeLootDeliverySink(
            worldItems,
            instancedLeases,
            worldItemReplication);
        deerclopsLoot = new RuntimeDeerclopsLootDeliverySink(
            worldItems,
            instancedLeases,
            worldItemReplication);
        wallOfFleshLoot = new RuntimeWallOfFleshLootDeliverySink(
            worldItems,
            instancedLeases,
            worldItemReplication);
        if (worldItemReplication is not null)
        {
            difficultyLoot = new RuntimeKingSlimeDifficultyLootDeliverySink(
                worldItems,
                instancedLeases ?? throw new ArgumentNullException(nameof(instancedLeases)),
                worldItemReplication);
        }
    }

    public RuntimeNpcNetworkDamageResult TryApply(
        ConnectionHandle connection,
        in TerrariaNpcDamageState wireState)
    {
        if (!connection.IsAssigned || !wireState.IsStructurallyValid)
            return RuntimeNpcNetworkDamageResult.Rejected;

        // TerrariaServer emits packet 162 before resolving packet-28 NPC generation.
        npcReplication?.TryAcknowledgeDamage(connection.Source);

        if (wireState.NpcSlot >= TerrariaNpcDamageCodec.VanillaNpcSlots ||
            !npcs.TryGetActive(wireState.NpcSlot, out NpcSnapshot current) ||
            RuntimeNpcPacketProjection.ToProtocolGeneration(current.Handle.Generation) != wireState.Generation)
        {
            return RuntimeNpcNetworkDamageResult.Rejected;
        }

        NpcDamageRequest authoritativeRequest = default;
        CombatIntegrityResolveResult integrity = combatIntegrity.ResolveClientNpcHit(
            tickProvider(), connection, in current, in wireState, out authoritativeRequest, out _);
        if (integrity == CombatIntegrityResolveResult.Rejected)
            return RuntimeNpcNetworkDamageResult.Rejected;

        short normalizedDamage = integrity == CombatIntegrityResolveResult.Accepted
            ? checked((short)Math.Min(authoritativeRequest.BaseDamage, short.MaxValue))
            : Math.Max(wireState.Damage, (short)0);
        float normalizedKnockBack = integrity == CombatIntegrityResolveResult.Accepted
            ? authoritativeRequest.KnockBack
            : Math.Max(wireState.KnockBack, 0f);
        int normalizedHitDirection = integrity == CombatIntegrityResolveResult.Accepted
            ? authoritativeRequest.HitDirection
            : Math.Clamp(wireState.HitDirection, -1, 1);
        var normalizedWire = new TerrariaNpcDamageState(
            wireState.NpcSlot,
            wireState.Generation,
            normalizedDamage,
            normalizedKnockBack,
            checked((byte)(normalizedHitDirection + 1)),
            integrity == CombatIntegrityResolveResult.Accepted
                ? (authoritativeRequest.Critical ? (byte)1 : (byte)0)
                : (wireState.Critical ? (byte)1 : (byte)0));

        // PlayerInteraction occurs before StrikeNPC in MessageBuffer case 28. Keep credit even when the strike itself
        // is rejected by invulnerability or another authoritative combat guard.
        if (VanillaEaterOfWorldsLifecycle.IsSegment(current.TypeIdentity))
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
        else if (current.TypeIdentity == VanillaNpcIds.WallOfFlesh || current.TypeIdentity == VanillaNpcIds.WallOfFleshEye)
        {
            MarkWallOfFleshInteraction(in current, connection.Player);
        }
        else if (IsDestroyerMember(current.TypeIdentity))
        {
            MarkDestroyerInteraction(in current, connection.Player);
        }
        else
        {
            interactions.TryMark(current.Handle, connection.Player);
        }

        NpcSnapshot destroyerRoot = default;
        bool destroyerSharedLife = IsDestroyerMember(current.TypeIdentity) &&
            TryResolveDestroyerRoot(in current, out destroyerRoot);
        if (destroyerSharedLife && current.Handle != destroyerRoot.Handle && current.Simulation.Life != destroyerRoot.Simulation.Life)
        {
            if (!TrySetNpcLife(in current, destroyerRoot.Simulation.Life, out current))
                throw new InvalidOperationException("Destroyer segment could not synchronize shared root life before packet-28 damage.");
        }

        var request = integrity == CombatIntegrityResolveResult.Accepted
            ? authoritativeRequest
            : new NpcDamageRequest(
                current.Handle,
                DamageSource.FromPlayerItem(connection.Player),
                normalizedDamage,
                Critical: normalizedWire.Critical,
                KnockBack: normalizedWire.KnockBack,
                HitDirection: normalizedHitDirection);

        bool suppressing = npcReplication?.TryBeginClientDamage(current.Handle) == true;
        try
        {
            if (!damage.TryApply(in request, out NpcDamageResult result))
            {
                if (suppressing)
                    npcReplication!.CompleteClientDamage(current.Handle);
                npcReplication?.TryPublishDamage(connection.Source, in normalizedWire);
                return RuntimeNpcNetworkDamageResult.Relayed;
            }

            NpcSnapshot dead;
            if (current.TypeIdentity == VanillaNpcIds.WallOfFleshEye &&
                TryResolveWallOfFleshRoot(in current, out NpcSnapshot wallRoot))
            {
                if (!TrySetWallOfFleshRootLife(in wallRoot, result.LifeAfter, out NpcSnapshot updatedRoot))
                    throw new InvalidOperationException("Wall of Flesh eye damage could not commit shared root life.");
                if (!result.Lethal)
                {
                    if (suppressing)
                        npcReplication!.CompleteClientDamage(current.Handle);
                    npcReplication?.TryPublishDamage(connection.Source, in normalizedWire);
                    return RuntimeNpcNetworkDamageResult.Committed;
                }
                dead = updatedRoot;
            }
            else if (destroyerSharedLife)
            {
                if (current.Handle != destroyerRoot.Handle)
                {
                    if (!TrySetDestroyerRootLife(in destroyerRoot, result.LifeAfter, out NpcSnapshot updatedRoot))
                        throw new InvalidOperationException("Destroyer segment damage could not commit shared root life.");
                    if (!result.Lethal)
                    {
                        if (suppressing)
                            npcReplication!.CompleteClientDamage(current.Handle);
                        npcReplication?.TryPublishDamage(connection.Source, in normalizedWire);
                        return RuntimeNpcNetworkDamageResult.Committed;
                    }
                    dead = updatedRoot;
                }
                else
                {
                    if (!result.Lethal)
                    {
                        if (suppressing)
                            npcReplication!.CompleteClientDamage(current.Handle);
                        npcReplication?.TryPublishDamage(connection.Source, in normalizedWire);
                        return RuntimeNpcNetworkDamageResult.Committed;
                    }
                    if (!npcs.TryGet(current.Handle, out dead))
                        throw new InvalidOperationException("A lethal Destroyer root commit disappeared before death finalization.");
                }
            }
            else
            {
                if (!result.Lethal)
                {
                    if (suppressing)
                        npcReplication!.CompleteClientDamage(current.Handle);
                    npcReplication?.TryPublishDamage(connection.Source, in normalizedWire);
                    return RuntimeNpcNetworkDamageResult.Committed;
                }
                if (!npcs.TryGet(current.Handle, out dead))
                    throw new InvalidOperationException("A lethal packet-28 commit disappeared before death finalization.");
            }

            bool eaterBoss =
                VanillaEaterOfWorldsLifecycle.IsSegment(dead.TypeIdentity) &&
                VanillaEaterOfWorldsLifecycle.IsLastActiveSegment(npcs, in dead, npcFamilyBuffer);

            if (!TryExecuteImportedLoot(in dead, eaterBoss))
                throw new InvalidOperationException("Imported NPC loot could not be finalized after a lethal packet-28 commit.");

            if (dead.TypeIdentity == VanillaNpcIds.KingSlime)
                ApplyKingSlimeDeathEffects(in dead);
            else if (dead.TypeIdentity == VanillaNpcIds.SkeletronHead)
                ApplySkeletronDeathEffects();
            else if (dead.TypeIdentity == VanillaNpcIds.QueenBee)
                ApplyQueenBeeDeathEffects();
            else if (dead.TypeIdentity == VanillaNpcIds.Deerclops)
                ApplyDeerclopsDeathEffects();
            else if (dead.TypeIdentity == VanillaNpcIds.WallOfFlesh)
                ApplyWallOfFleshDeathEffects(in dead);
            else if (IsHardmodeBossRoot(dead.TypeIdentity))
                ApplyHardmodeBossDeathEffects(in dead);
            else if (eaterBoss || dead.TypeIdentity == VanillaNpcIds.BrainOfCthulhu)
                ApplyEvilBossDeathEffects(eaterBoss);

            if (VanillaEaterOfWorldsLifecycle.IsSegment(dead.TypeIdentity))
                DropEaterOfWorldsHealingHeartIfEligible(in dead);
            if (dead.TypeIdentity == VanillaNpcIds.WallOfFlesh)
                CleanupWallOfFleshChildren(dead.Handle.Slot);
            if (dead.TypeIdentity == VanillaNpcIds.Destroyer)
                CleanupDestroyerSegments(dead.Handle.Slot);
            if (!npcs.TryDespawn(dead.Handle))
                throw new InvalidOperationException("A lethal packet-28 NPC could not be despawned after death effects.");
            interactions.Forget(dead.Handle);

            if (suppressing)
                npcReplication!.CompleteClientDamage(current.Handle);
            npcReplication?.TryPublishDamage(connection.Source, in normalizedWire);
            npcReplication?.TryPublishDeath(in dead);
            return RuntimeNpcNetworkDamageResult.Killed;
        }
        catch
        {
            if (suppressing)
                npcReplication!.AbortClientDamage(current.Handle);
            throw;
        }
    }



    public RuntimeProjectileNpcDamageResult TryStrikeProjectile(
        in ProjectileSnapshot projectile,
        NpcHandle target,
        int hitDirection,
        int authoritativeDamage = -1,
        int armorPenetration = 0,
        bool critical = false)
    {
        int resolvedSourceDamage = authoritativeDamage > 0 ? authoritativeDamage : projectile.Damage;
        if (!projectile.IsActive || !target.IsAssigned || hitDirection is < -1 or > 1 ||
            !npcs.TryGet(target, out NpcSnapshot liveTarget) || !liveTarget.IsActive ||
            !ProjectileNpcHitIntentBuilder.TryCreateNpcHit(
                in projectile, liveTarget.Handle, hitDirection, resolvedSourceDamage, armorPenetration, critical,
                players, out ProjectileNpcHitIntent intent) ||
            !intent.TryCreateDamageRequest(out NpcDamageRequest request))
        {
            return RuntimeProjectileNpcDamageResult.Rejected;
        }

        if (VanillaEaterOfWorldsLifecycle.IsSegment(liveTarget.TypeIdentity))
        {
            VanillaEaterOfWorldsLifecycle.MarkPlayerInteractionAcrossActiveSegments(
                npcs, interactions, request.Source.Player, npcFamilyBuffer);
        }
        else if (liveTarget.TypeIdentity == VanillaNpcIds.SkeletronHead || liveTarget.TypeIdentity == VanillaNpcIds.SkeletronHand)
        {
            MarkSkeletronInteraction(request.Source.Player);
        }
        else if (liveTarget.TypeIdentity == VanillaNpcIds.WallOfFlesh || liveTarget.TypeIdentity == VanillaNpcIds.WallOfFleshEye)
        {
            MarkWallOfFleshInteraction(in liveTarget, request.Source.Player);
        }
        else if (IsDestroyerMember(liveTarget.TypeIdentity))
        {
            MarkDestroyerInteraction(in liveTarget, request.Source.Player);
        }

        NpcSnapshot destroyerRoot = default;
        bool destroyerSharedLife = IsDestroyerMember(liveTarget.TypeIdentity) &&
            TryResolveDestroyerRoot(in liveTarget, out destroyerRoot);
        if (destroyerSharedLife && liveTarget.Handle != destroyerRoot.Handle && liveTarget.Simulation.Life != destroyerRoot.Simulation.Life)
        {
            if (!TrySetNpcLife(in liveTarget, destroyerRoot.Simulation.Life, out liveTarget))
                throw new InvalidOperationException("Destroyer segment could not synchronize shared root life before projectile damage.");
            request = request with { Target = liveTarget.Handle };
        }

        if (!damage.TryApply(in request, out NpcDamageResult result))
            return RuntimeProjectileNpcDamageResult.Rejected;

        NpcSnapshot dead;
        if (liveTarget.TypeIdentity == VanillaNpcIds.WallOfFleshEye &&
            TryResolveWallOfFleshRoot(in liveTarget, out NpcSnapshot wallRoot))
        {
            if (!TrySetWallOfFleshRootLife(in wallRoot, result.LifeAfter, out NpcSnapshot updatedRoot))
                throw new InvalidOperationException("Projectile damage could not commit Wall of Flesh shared root life.");
            if (!result.Lethal)
                return RuntimeProjectileNpcDamageResult.Committed;
            dead = updatedRoot;
        }
        else if (destroyerSharedLife)
        {
            if (liveTarget.Handle != destroyerRoot.Handle)
            {
                if (!TrySetDestroyerRootLife(in destroyerRoot, result.LifeAfter, out NpcSnapshot updatedRoot))
                    throw new InvalidOperationException("Projectile damage could not commit Destroyer shared root life.");
                if (!result.Lethal)
                    return RuntimeProjectileNpcDamageResult.Committed;
                dead = updatedRoot;
            }
            else
            {
                if (!result.Lethal)
                    return RuntimeProjectileNpcDamageResult.Committed;
                if (!npcs.TryGet(liveTarget.Handle, out dead))
                    throw new InvalidOperationException("A lethal Destroyer projectile commit disappeared before death finalization.");
            }
        }
        else
        {
            if (!result.Lethal)
                return RuntimeProjectileNpcDamageResult.Committed;
            if (!npcs.TryGet(liveTarget.Handle, out dead))
                throw new InvalidOperationException("A lethal projectile commit disappeared before death finalization.");
        }

        bool eaterBoss =
            VanillaEaterOfWorldsLifecycle.IsSegment(dead.TypeIdentity) &&
            VanillaEaterOfWorldsLifecycle.IsLastActiveSegment(npcs, in dead, npcFamilyBuffer);
        if (!TryExecuteImportedLoot(in dead, eaterBoss))
            throw new InvalidOperationException("Imported NPC loot could not be finalized after projectile damage.");

        if (dead.TypeIdentity == VanillaNpcIds.KingSlime)
            ApplyKingSlimeDeathEffects(in dead);
        else if (dead.TypeIdentity == VanillaNpcIds.SkeletronHead)
            ApplySkeletronDeathEffects();
        else if (dead.TypeIdentity == VanillaNpcIds.QueenBee)
            ApplyQueenBeeDeathEffects();
        else if (dead.TypeIdentity == VanillaNpcIds.Deerclops)
            ApplyDeerclopsDeathEffects();
        else if (dead.TypeIdentity == VanillaNpcIds.WallOfFlesh)
            ApplyWallOfFleshDeathEffects(in dead);
        else if (IsHardmodeBossRoot(dead.TypeIdentity))
            ApplyHardmodeBossDeathEffects(in dead);
        else if (eaterBoss || dead.TypeIdentity == VanillaNpcIds.BrainOfCthulhu)
            ApplyEvilBossDeathEffects(eaterBoss);

        if (VanillaEaterOfWorldsLifecycle.IsSegment(dead.TypeIdentity))
            DropEaterOfWorldsHealingHeartIfEligible(in dead);
        if (dead.TypeIdentity == VanillaNpcIds.WallOfFlesh)
            CleanupWallOfFleshChildren(dead.Handle.Slot);
        if (dead.TypeIdentity == VanillaNpcIds.Destroyer)
            CleanupDestroyerSegments(dead.Handle.Slot);
        if (!npcs.TryDespawn(dead.Handle))
            throw new InvalidOperationException("A projectile kill could not despawn the exact NPC generation.");
        interactions.Forget(dead.Handle);
        npcReplication?.TryPublishDeath(in dead);
        return RuntimeProjectileNpcDamageResult.Killed;
    }

    public RuntimeTownNpcMeleeDamageResult1458 TryStrike(
        NpcHandle attacker,
        NpcHandle target,
        int baseDamage,
        float knockBack,
        int hitDirection)
    {
        if (!attacker.IsAssigned || !target.IsAssigned || baseDamage < 0 ||
            !float.IsFinite(knockBack) || knockBack < 0f || hitDirection is not (-1 or 1) ||
            !npcs.TryGet(attacker, out NpcSnapshot liveAttacker) || !liveAttacker.IsActive ||
            !npcs.TryGet(target, out NpcSnapshot liveTarget) || !liveTarget.IsActive)
        {
            return RuntimeTownNpcMeleeDamageResult1458.Rejected;
        }

        NpcSnapshot destroyerRoot = default;
        bool destroyerSharedLife = IsDestroyerMember(liveTarget.TypeIdentity) &&
            TryResolveDestroyerRoot(in liveTarget, out destroyerRoot);
        if (destroyerSharedLife && liveTarget.Handle != destroyerRoot.Handle && liveTarget.Simulation.Life != destroyerRoot.Simulation.Life)
        {
            if (!TrySetNpcLife(in liveTarget, destroyerRoot.Simulation.Life, out liveTarget))
                throw new InvalidOperationException("Destroyer segment could not synchronize shared root life before Town NPC melee.");
        }

        var request = new NpcDamageRequest(
            liveTarget.Handle,
            DamageSource.FromNpcContact(liveAttacker.Handle),
            baseDamage,
            KnockBack: knockBack,
            HitDirection: hitDirection);
        if (!damage.TryApply(in request, out NpcDamageResult result))
            return RuntimeTownNpcMeleeDamageResult1458.Rejected;

        NpcSnapshot dead;
        if (liveTarget.TypeIdentity == VanillaNpcIds.WallOfFleshEye && TryResolveWallOfFleshRoot(in liveTarget, out NpcSnapshot wallRoot))
        {
            if (!TrySetWallOfFleshRootLife(in wallRoot, result.LifeAfter, out NpcSnapshot updatedRoot))
                throw new InvalidOperationException("Town NPC melee could not commit Wall of Flesh shared root life.");
            if (!result.Lethal)
                return RuntimeTownNpcMeleeDamageResult1458.Committed;
            dead = updatedRoot;
        }
        else if (destroyerSharedLife)
        {
            if (liveTarget.Handle != destroyerRoot.Handle)
            {
                if (!TrySetDestroyerRootLife(in destroyerRoot, result.LifeAfter, out NpcSnapshot updatedRoot))
                    throw new InvalidOperationException("Town NPC melee could not commit Destroyer shared root life.");
                if (!result.Lethal)
                    return RuntimeTownNpcMeleeDamageResult1458.Committed;
                dead = updatedRoot;
            }
            else
            {
                if (!result.Lethal)
                    return RuntimeTownNpcMeleeDamageResult1458.Committed;
                if (!npcs.TryGet(liveTarget.Handle, out dead))
                    throw new InvalidOperationException("A lethal Destroyer root melee commit disappeared before death finalization.");
            }
        }
        else
        {
            if (!result.Lethal)
                return RuntimeTownNpcMeleeDamageResult1458.Committed;
            if (!npcs.TryGet(liveTarget.Handle, out dead))
                throw new InvalidOperationException("A lethal Town NPC melee commit disappeared before death finalization.");
        }

        bool eaterBoss =
            VanillaEaterOfWorldsLifecycle.IsSegment(dead.TypeIdentity) &&
            VanillaEaterOfWorldsLifecycle.IsLastActiveSegment(npcs, in dead, npcFamilyBuffer);
        if (!TryExecuteImportedLoot(in dead, eaterBoss))
            throw new InvalidOperationException("Imported NPC loot could not be finalized after Town NPC melee.");

        if (dead.TypeIdentity == VanillaNpcIds.KingSlime)
            ApplyKingSlimeDeathEffects(in dead);
        else if (dead.TypeIdentity == VanillaNpcIds.SkeletronHead)
            ApplySkeletronDeathEffects();
        else if (dead.TypeIdentity == VanillaNpcIds.QueenBee)
            ApplyQueenBeeDeathEffects();
        else if (dead.TypeIdentity == VanillaNpcIds.Deerclops)
            ApplyDeerclopsDeathEffects();
        else if (dead.TypeIdentity == VanillaNpcIds.WallOfFlesh)
            ApplyWallOfFleshDeathEffects(in dead);
        else if (IsHardmodeBossRoot(dead.TypeIdentity))
            ApplyHardmodeBossDeathEffects(in dead);
        else if (eaterBoss || dead.TypeIdentity == VanillaNpcIds.BrainOfCthulhu)
            ApplyEvilBossDeathEffects(eaterBoss);

        if (VanillaEaterOfWorldsLifecycle.IsSegment(dead.TypeIdentity))
            DropEaterOfWorldsHealingHeartIfEligible(in dead);
        if (dead.TypeIdentity == VanillaNpcIds.WallOfFlesh)
            CleanupWallOfFleshChildren(dead.Handle.Slot);
        if (dead.TypeIdentity == VanillaNpcIds.Destroyer)
            CleanupDestroyerSegments(dead.Handle.Slot);
        if (!npcs.TryDespawn(dead.Handle))
            throw new InvalidOperationException("A Town NPC melee kill could not despawn the exact NPC generation.");
        interactions.Forget(dead.Handle);
        npcReplication?.TryPublishDeath(in dead);
        return RuntimeTownNpcMeleeDamageResult1458.Killed;
    }

}
