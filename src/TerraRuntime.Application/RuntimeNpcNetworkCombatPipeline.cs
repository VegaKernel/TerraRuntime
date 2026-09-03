using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

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
internal sealed class RuntimeNpcNetworkCombatPipeline : IRuntimeTownNpcMeleeDamageSink1458
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
        bool evilBossDownedBaseline = false)
    {
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
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

        bool destroyerSharedLife = IsDestroyerMember(current.TypeIdentity) &&
            TryResolveDestroyerRoot(in current, out NpcSnapshot destroyerRoot);
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
        int hitDirection)
    {
        if (!projectile.IsActive || !target.IsAssigned || hitDirection is < -1 or > 1 ||
            !npcs.TryGet(target, out NpcSnapshot liveTarget) || !liveTarget.IsActive ||
            !ProjectileNpcHitIntentBuilder.TryCreateNpcHit(
                in projectile, liveTarget.Handle, hitDirection, players, out ProjectileNpcHitIntent intent) ||
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

        bool destroyerSharedLife = IsDestroyerMember(liveTarget.TypeIdentity) &&
            TryResolveDestroyerRoot(in liveTarget, out NpcSnapshot destroyerRoot);
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

        bool destroyerSharedLife = IsDestroyerMember(liveTarget.TypeIdentity) &&
            TryResolveDestroyerRoot(in liveTarget, out NpcSnapshot destroyerRoot);
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

    private bool TryExecuteImportedLoot(in NpcSnapshot npc, bool eaterBoss)
    {
        if (VanillaEaterOfWorldsLifecycle.IsSegment(npc.TypeIdentity))
            return TryExecuteEaterOfWorldsLoot(in npc, eaterBoss);
        if (npc.TypeIdentity == VanillaNpcIds.BrainOfCthulhu || npc.TypeIdentity == VanillaNpcIds.BrainCreeper)
            return TryExecuteBrainOfCthulhuLoot(in npc);
        if (npc.TypeIdentity == VanillaNpcIds.SkeletronHead)
            return TryExecuteSkeletronLoot(in npc);
        if (npc.TypeIdentity == VanillaNpcIds.QueenBee)
            return TryExecuteQueenBeeLoot(in npc);
        if (npc.TypeIdentity == VanillaNpcIds.Deerclops)
            return TryExecuteDeerclopsLoot(in npc);
        if (npc.TypeIdentity == VanillaNpcIds.WallOfFlesh)
            return TryExecuteWallOfFleshLoot(in npc);

        if (npc.TypeIdentity == VanillaNpcIds.KingSlime && expertMode)
            return TryExecuteKingSlimeDifficultyLoot(in npc);

        bool kingSlimeNormal = npc.TypeIdentity == VanillaNpcIds.KingSlime;
        VanillaNpcLootTable genericTable = default;
        if (!kingSlimeNormal && !VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(npc.TypeIdentity, out genericTable))
            return true;

        int maximumDropCount = kingSlimeNormal
            ? VanillaKingSlimeNormalLootCatalog.MaximumDropCount
            : genericTable.MaximumDropCount;
        if (maximumDropCount > MaxOrdinaryDrops ||
            !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition))
        {
            return false;
        }

        Span<WorldItemDropReservation> capacity = stackalloc WorldItemDropReservation[MaxOrdinaryDrops];
        Span<WorldItemDropReservation> staged = stackalloc WorldItemDropReservation[MaxOrdinaryDrops];
        int reserved = 0;
        for (; reserved < maximumDropCount; reserved++)
        {
            if (worldItems.TryReserveDropSlot(out capacity[reserved]))
                continue;
            ReleaseReservations(capacity[..reserved]);
            return false;
        }

        var origin = new NpcLootWorldItemOrigin(
            (int)npc.PositionX + definition.Width * 0.5f,
            (int)npc.PositionY + definition.Height * 0.5f);
        int stagedCount = 0;
        var context = new VanillaNpcLootContext(expertMode, DropExtraGel: false);

        if (kingSlimeNormal)
        {
            ReadOnlySpan<VanillaKingSlimeNormalLootRule> rules = VanillaKingSlimeNormalLootCatalog.Rules;
            for (int index = 0; index < rules.Length; index++)
            {
                if (!VanillaKingSlimeNormalLootEvaluator.TryEvaluateRule(
                        in rules[index], random, out bool dropped, out NpcLootDrop drop))
                {
                    ReleaseReservations(capacity);
                    ReleaseReservations(staged[..stagedCount]);
                    return false;
                }
                if (dropped && !StageDrop(in origin, in drop, capacity, staged, ref stagedCount))
                    return false;
            }
        }
        else
        {
            ReadOnlySpan<VanillaNpcLootRule> rules = genericTable.Rules;
            for (int index = 0; index < rules.Length; index++)
            {
                if (!VanillaNpcLootEvaluator.TryEvaluateRule(
                        in rules[index], in context, random, out bool dropped, out NpcLootDrop drop))
                {
                    ReleaseReservations(capacity);
                    ReleaseReservations(staged[..stagedCount]);
                    return false;
                }
                if (dropped && !StageDrop(in origin, in drop, capacity, staged, ref stagedCount))
                    return false;
            }
        }

        ReleaseReservations(capacity[stagedCount..maximumDropCount]);
        for (int index = 0; index < stagedCount; index++)
        {
            if (!worldItems.TryCommitReservedDrop(in staged[index], out _))
                throw new InvalidOperationException("A staged NPC-loot reservation failed after source-ordered evaluation.");
        }
        return true;
    }

    private bool StageDrop(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        Span<WorldItemDropReservation> capacity,
        Span<WorldItemDropReservation> staged,
        ref int stagedCount)
    {
        if (!materializer.TryMaterialize(in origin, in drop, random, out WorldItemDropStateUpdate materialized))
        {
            ReleaseReservations(capacity);
            ReleaseReservations(staged[..stagedCount]);
            return false;
        }

        int capacityIndex = stagedCount;
        if (!worldItems.TryReleaseDropReservation(in capacity[capacityIndex]))
            throw new InvalidOperationException("Failed to release an exact NPC-loot capacity reservation.");
        capacity[capacityIndex] = default;
        if (!worldItems.TryReserveDrop(in materialized, out staged[stagedCount]))
            throw new InvalidOperationException("Preflighted NPC loot lost reserved world-item capacity.");
        stagedCount++;
        return true;
    }

    private bool TryExecuteEaterOfWorldsLoot(in NpcSnapshot npc, bool isBoss)
    {
        if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition))
        {
            return false;
        }

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player))
                continue;
            activeEaterLootPlayers[activeCount++] = new VanillaEaterOfWorldsLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = new NpcLootWorldItemOrigin(
            (int)npc.PositionX + definition.Width * 0.5f,
            (int)npc.PositionY + definition.Height * 0.5f);
        var context = new VanillaEaterOfWorldsLootContext(expertMode, masterMode, isBoss);
        return VanillaEaterOfWorldsLootEvaluator.TryExecute(
            in context,
            in origin,
            activeEaterLootPlayers.AsSpan(0, activeCount),
            random,
            eaterLoot,
            out _);
    }

    private bool TryExecuteBrainOfCthulhuLoot(in NpcSnapshot npc)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition))
            return false;

        int activeCount = 0;
        if (npc.TypeIdentity == VanillaNpcIds.BrainOfCthulhu)
        {
            if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount))
                return false;

            for (int index = 0; index < interactionCount; index++)
            {
                PlayerSlotId slot = interactionSlots[index];
                if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player))
                    continue;
                activeBrainLootPlayers[activeCount++] = new VanillaBrainOfCthulhuLootPlayer(
                    slot,
                    player.PositionX + VanillaPlayerWidth * 0.5f,
                    player.PositionY + VanillaPlayerHeight * 0.5f);
            }
        }

        var origin = new NpcLootWorldItemOrigin(
            (int)npc.PositionX + definition.Width * 0.5f,
            (int)npc.PositionY + definition.Height * 0.5f);
        var context = new VanillaBrainOfCthulhuLootContext(expertMode, masterMode, npc.TypeIdentity);
        return VanillaBrainOfCthulhuLootEvaluator.TryExecute(
            in context,
            in origin,
            activeBrainLootPlayers.AsSpan(0, activeCount),
            random,
            brainLoot,
            out _);
    }

    private bool TryExecuteSkeletronLoot(in NpcSnapshot npc)
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
            RedHatAdjustmentsEnabled: false);
        return VanillaSkeletronLootEvaluator.TryExecute(
            in context,
            in origin,
            activeSkeletronLootPlayers.AsSpan(0, activeCount),
            random,
            skeletronLoot,
            out _);
    }

    private bool TryExecuteQueenBeeLoot(in NpcSnapshot npc)
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

    private bool TryExecuteDeerclopsLoot(in NpcSnapshot npc)
    {
        if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Deerclops, out VanillaNpcDefinition definition))
        {
            return false;
        }

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player))
                continue;

            activeDeerclopsLootPlayers[activeCount++] = new VanillaDeerclopsLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = new NpcLootWorldItemOrigin(
            (int)npc.PositionX + definition.Width * 0.5f,
            (int)npc.PositionY + definition.Height * 0.5f);
        var context = new VanillaDeerclopsLootContext(expertMode, masterMode);
        return VanillaDeerclopsLootEvaluator.TryExecute(
            in context,
            in origin,
            activeDeerclopsLootPlayers.AsSpan(0, activeCount),
            random,
            deerclopsLoot,
            out _);
    }

    private bool TryExecuteWallOfFleshLoot(in NpcSnapshot npc)
    {
        if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.WallOfFlesh, out VanillaNpcDefinition definition))
            return false;

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player))
                continue;
            activeWallOfFleshLootPlayers[activeCount++] = new VanillaWallOfFleshLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = new NpcLootWorldItemOrigin(
            (int)npc.PositionX + definition.Width * 0.5f,
            (int)npc.PositionY + definition.Height * 0.5f);
        var context = new VanillaWallOfFleshLootContext(expertMode, masterMode);
        return VanillaWallOfFleshLootEvaluator.TryExecute(
            in context,
            in origin,
            activeWallOfFleshLootPlayers.AsSpan(0, activeCount),
            random,
            wallOfFleshLoot,
            out _);
    }

    private static bool IsDestroyerMember(NpcTypeId type) =>
        type == VanillaNpcIds.Destroyer || type == VanillaNpcIds.DestroyerBody || type == VanillaNpcIds.DestroyerTail;

    private static bool IsHardmodeBossRoot(NpcTypeId type) =>
        type == VanillaNpcIds.QueenSlime || type == VanillaNpcIds.Destroyer ||
        type == VanillaNpcIds.Retinazer || type == VanillaNpcIds.Spazmatism ||
        type == VanillaNpcIds.SkeletronPrime || type == VanillaNpcIds.Plantera ||
        type == VanillaNpcIds.Golem || type == VanillaNpcIds.DukeFishron ||
        type == VanillaNpcIds.LunaticCultist || type == VanillaNpcIds.EmpressOfLight ||
        type == VanillaNpcIds.MoonLordCore;

    private bool TryResolveDestroyerRoot(in NpcSnapshot member, out NpcSnapshot root)
    {
        if (member.TypeIdentity == VanillaNpcIds.Destroyer)
        {
            root = member;
            return true;
        }
        if (IsDestroyerMember(member.TypeIdentity) && float.IsFinite(member.Ai.Ai3) &&
            member.Ai.Ai3 >= 0f && member.Ai.Ai3 < byte.MaxValue && member.Ai.Ai3 == MathF.Truncate(member.Ai.Ai3) &&
            npcs.TryGetActive((byte)member.Ai.Ai3, out NpcSnapshot linked) && linked.TypeIdentity == VanillaNpcIds.Destroyer)
        {
            root = linked;
            return true;
        }
        int count = npcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            if (npcFamilyBuffer[index].TypeIdentity == VanillaNpcIds.Destroyer)
            {
                root = npcFamilyBuffer[index];
                return true;
            }
        }
        root = default;
        return false;
    }

    private bool TrySetNpcLife(in NpcSnapshot npc, int life, out NpcSnapshot committed)
    {
        committed = default;
        if (life < 0 || life > npc.Simulation.LifeMax)
            return false;
        var update = new NpcStateUpdate(
            npc.Type, npc.NetId, npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai,
            npc.Simulation with { Life = life });
        return npcs.TryUpdate(npc.Handle, in update, out committed);
    }

    private bool TrySetDestroyerRootLife(in NpcSnapshot root, int life, out NpcSnapshot committed)
    {
        committed = default;
        if (root.TypeIdentity != VanillaNpcIds.Destroyer || life < 0 || life > root.Simulation.LifeMax)
            return false;
        var update = new NpcStateUpdate(
            root.Type, root.NetId, root.PositionX, root.PositionY, root.VelocityX, root.VelocityY, root.Target, root.Ai,
            root.Simulation with { Life = life, JustHit = true });
        return npcs.TryUpdate(root.Handle, in update, out committed);
    }

    private void MarkDestroyerInteraction(in NpcSnapshot member, PlayerHandle player)
    {
        if (!TryResolveDestroyerRoot(in member, out NpcSnapshot root))
        {
            interactions.TryMark(member.Handle, player);
            return;
        }
        int count = npcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot peer = npcFamilyBuffer[index];
            if (peer.TypeIdentity == VanillaNpcIds.Destroyer ||
                (IsDestroyerMember(peer.TypeIdentity) && float.IsFinite(peer.Ai.Ai3) &&
                 peer.Ai.Ai3 >= 0f && peer.Ai.Ai3 < byte.MaxValue && (byte)peer.Ai.Ai3 == root.Handle.Slot))
                interactions.TryMark(peer.Handle, player);
        }
    }

    private void CleanupDestroyerSegments(byte rootSlot)
    {
        int count = npcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot peer = npcFamilyBuffer[index];
            if ((peer.TypeIdentity != VanillaNpcIds.DestroyerBody && peer.TypeIdentity != VanillaNpcIds.DestroyerTail) ||
                !float.IsFinite(peer.Ai.Ai3) || peer.Ai.Ai3 < 0f || peer.Ai.Ai3 >= byte.MaxValue || (byte)peer.Ai.Ai3 != rootSlot)
                continue;
            if (npcs.TryDespawn(peer.Handle))
            {
                interactions.Forget(peer.Handle);
                npcReplication?.TryPublishDeath(in peer);
            }
        }
    }

    private bool TryResolveWallOfFleshRoot(in NpcSnapshot member, out NpcSnapshot root)
    {
        if (member.TypeIdentity == VanillaNpcIds.WallOfFlesh)
        {
            root = member;
            return true;
        }
        if (member.TypeIdentity == VanillaNpcIds.WallOfFleshEye && float.IsFinite(member.Ai.Ai3) &&
            member.Ai.Ai3 >= 0f && member.Ai.Ai3 < byte.MaxValue &&
            npcs.TryGetActive((byte)member.Ai.Ai3, out NpcSnapshot linked) && linked.TypeIdentity == VanillaNpcIds.WallOfFlesh)
        {
            root = linked;
            return true;
        }
        int count = npcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            if (npcFamilyBuffer[index].TypeIdentity == VanillaNpcIds.WallOfFlesh)
            {
                root = npcFamilyBuffer[index];
                return true;
            }
        }
        root = default;
        return false;
    }

    private bool TrySetWallOfFleshRootLife(in NpcSnapshot root, int life, out NpcSnapshot committed)
    {
        committed = default;
        if (root.TypeIdentity != VanillaNpcIds.WallOfFlesh || life < 0 || life > root.Simulation.LifeMax)
            return false;
        var update = new NpcStateUpdate(
            root.Type, root.NetId, root.PositionX, root.PositionY, root.VelocityX, root.VelocityY, root.Target, root.Ai,
            root.Simulation with { Life = life, JustHit = true });
        return npcs.TryUpdate(root.Handle, in update, out committed);
    }

    private void MarkWallOfFleshInteraction(in NpcSnapshot member, PlayerHandle player)
    {
        if (TryResolveWallOfFleshRoot(in member, out NpcSnapshot root))
            interactions.TryMark(root.Handle, player);
        interactions.TryMark(member.Handle, player);
        int count = npcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot peer = npcFamilyBuffer[index];
            if (peer.TypeIdentity == VanillaNpcIds.WallOfFleshEye && (byte)peer.Ai.Ai3 == root.Handle.Slot)
                interactions.TryMark(peer.Handle, player);
        }
    }

    private void CleanupWallOfFleshChildren(byte rootSlot)
    {
        int count = npcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot peer = npcFamilyBuffer[index];
            bool child = (peer.TypeIdentity == VanillaNpcIds.WallOfFleshEye || peer.TypeIdentity == VanillaNpcIds.TheHungry) &&
                         float.IsFinite(peer.Ai.Ai3) && peer.Ai.Ai3 >= 0f && peer.Ai.Ai3 < byte.MaxValue && (byte)peer.Ai.Ai3 == rootSlot;
            if (!child)
                continue;
            if (npcs.TryDespawn(peer.Handle))
            {
                interactions.Forget(peer.Handle);
                npcReplication?.TryPublishDeath(in peer);
            }
        }
    }

    private void MarkSkeletronInteraction(PlayerHandle player)
    {
        int count = npcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot peer = npcFamilyBuffer[index];
            if (peer.TypeIdentity == VanillaNpcIds.SkeletronHead || peer.TypeIdentity == VanillaNpcIds.SkeletronHand)
                interactions.TryMark(peer.Handle, player);
        }
    }

    private bool TryExecuteKingSlimeDifficultyLoot(in NpcSnapshot npc)
    {
        if (difficultyLoot is null ||
            !interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.KingSlime, out VanillaNpcDefinition definition))
        {
            return false;
        }

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player))
                continue;
            activeLootPlayers[activeCount++] = new VanillaKingSlimeLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = new NpcLootWorldItemOrigin(
            (int)npc.PositionX + definition.Width * 0.5f,
            (int)npc.PositionY + definition.Height * 0.5f);
        var context = new VanillaKingSlimeDifficultyLootContext(expertMode, masterMode);
        return VanillaKingSlimeDifficultyLootEvaluator.TryExecute(
            in context,
            in origin,
            activeLootPlayers.AsSpan(0, activeCount),
            random,
            difficultyLoot,
            out _);
    }

    private void ApplyHardmodeBossDeathEffects(in NpcSnapshot dead)
    {
        if (dead.TypeIdentity == VanillaNpcIds.QueenSlime)
        {
            progression.MarkCompleted(VanillaWorldProgressionId.QueenSlime);
            return;
        }
        if (dead.TypeIdentity == VanillaNpcIds.Destroyer)
        {
            progression.MarkCompleted(VanillaWorldProgressionId.Destroyer);
            progression.MarkCompleted(VanillaWorldProgressionId.AnyMechanicalBoss);
            return;
        }
        if (dead.TypeIdentity == VanillaNpcIds.Retinazer || dead.TypeIdentity == VanillaNpcIds.Spazmatism)
        {
            NpcTypeId other = dead.TypeIdentity == VanillaNpcIds.Retinazer ? VanillaNpcIds.Spazmatism : VanillaNpcIds.Retinazer;
            int count = npcs.CopyActive(npcFamilyBuffer);
            for (int index = 0; index < count; index++)
            {
                NpcSnapshot peer = npcFamilyBuffer[index];
                if (peer.Handle != dead.Handle && peer.TypeIdentity == other && peer.Simulation.Life > 0)
                    return;
            }
            progression.MarkCompleted(VanillaWorldProgressionId.Twins);
            progression.MarkCompleted(VanillaWorldProgressionId.AnyMechanicalBoss);
            return;
        }
        if (dead.TypeIdentity == VanillaNpcIds.SkeletronPrime)
        {
            progression.MarkCompleted(VanillaWorldProgressionId.SkeletronPrime);
            progression.MarkCompleted(VanillaWorldProgressionId.AnyMechanicalBoss);
            return;
        }
        if (dead.TypeIdentity == VanillaNpcIds.Plantera)
            progression.MarkCompleted(VanillaWorldProgressionId.Plantera);
        else if (dead.TypeIdentity == VanillaNpcIds.Golem)
            progression.MarkCompleted(VanillaWorldProgressionId.Golem);
        else if (dead.TypeIdentity == VanillaNpcIds.DukeFishron)
            progression.MarkCompleted(VanillaWorldProgressionId.DukeFishron);
        else if (dead.TypeIdentity == VanillaNpcIds.LunaticCultist)
            progression.MarkCompleted(VanillaWorldProgressionId.LunaticCultist);
        else if (dead.TypeIdentity == VanillaNpcIds.EmpressOfLight)
            progression.MarkCompleted(VanillaWorldProgressionId.EmpressOfLight);
        else if (dead.TypeIdentity == VanillaNpcIds.MoonLordCore)
            progression.MarkCompleted(VanillaWorldProgressionId.MoonLord);
    }

    private void ApplySkeletronDeathEffects()
    {
        progression.MarkCompleted(VanillaWorldProgressionId.Skeletron);
    }

    private void ApplyQueenBeeDeathEffects()
    {
        progression.MarkCompleted(VanillaWorldProgressionId.QueenBee);
    }

    private void ApplyDeerclopsDeathEffects()
    {
        progression.MarkCompleted(VanillaWorldProgressionId.Deerclops);
    }

    private void ApplyWallOfFleshDeathEffects(in NpcSnapshot wallOfFlesh)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.WallOfFlesh, out VanillaNpcDefinition definition))
            throw new InvalidOperationException("Wall of Flesh definition disappeared during its committed death path.");

        if (worldTiles is not null)
        {
            VanillaWallOfFleshDeathWorldMutation.Apply(
                worldTiles,
                wallOfFlesh.PositionX,
                wallOfFlesh.PositionY,
                definition.Width,
                definition.Height,
                crimsonWorld);
        }

        DropWallOfFleshRecoveryItems(in wallOfFlesh, in definition);
        progression.MarkCompleted(VanillaWorldProgressionId.Hardmode);
    }

    private void DropWallOfFleshRecoveryItems(in NpcSnapshot wallOfFlesh, in VanillaNpcDefinition definition)
    {
        var origin = new NpcLootWorldItemOrigin(
            wallOfFlesh.PositionX + definition.Width * 0.5f,
            wallOfFlesh.PositionY + definition.Height * 0.5f);

        var potions = new NpcLootDrop(
            VanillaWallOfFleshItemIds.HealingPotion,
            checked((short)random.NextInt32(5, 16)));
        if (!wallOfFleshLoot.TryDeliverWorldItem(in origin, in potions, random))
            throw new InvalidOperationException("Wall of Flesh recovery Healing Potion drop could not be materialized.");

        int heartCount = random.NextInt32(0, 5) + 5;
        for (int index = 0; index < heartCount; index++)
        {
            var heart = new NpcLootDrop(VanillaWallOfFleshItemIds.Heart, 1);
            if (!wallOfFleshLoot.TryDeliverWorldItem(in origin, in heart, random))
                throw new InvalidOperationException("Wall of Flesh recovery Heart drop could not be materialized.");
        }
    }

    private void ApplyEvilBossDeathEffects(bool eaterBoss)
    {
        if (eaterBoss)
        {
            // NPC.DoDeathEvents evaluates these branches before SetEventFlagCleared(downedBoss2).
            bool wasAlreadyDowned = evilBossDownedBaseline || progression.IsCompleted(VanillaWorldProgressionId.EvilBoss);
            if (skyblockLowTiles)
                progression.MarkCompleted(VanillaWorldProgressionId.ShadowOrbSmashed);
            if (isThereAWorldSurface && (!wasAlreadyDowned || random.NextInt32(0, 2) == 0))
                worldClock?.ScheduleMeteor();
        }

        progression.MarkCompleted(VanillaWorldProgressionId.EvilBoss);
    }

    private void DropEaterOfWorldsHealingHeartIfEligible(in NpcSnapshot eaterSegment)
    {
        if (!TryFindClosestPlayer(in eaterSegment, out PlayerStateSnapshot closest) ||
            !closest.HasHealth || closest.Life >= closest.MaxLife ||
            random.NextInt32(0, 4) != 0 ||
            !VanillaNpcDefinitionCatalog.TryGet(
                eaterSegment.TypeIdentity,
                eaterSegment.NetIdentity,
                out VanillaNpcDefinition definition))
        {
            return;
        }

        var origin = new NpcLootWorldItemOrigin(
            eaterSegment.PositionX + definition.Width * 0.5f,
            eaterSegment.PositionY + definition.Height * 0.5f);
        var heart = new NpcLootDrop(VanillaWallOfFleshItemIds.Heart, 1);
        if (!eaterLoot.TryDeliverWorldItem(in origin, in heart, random))
            throw new InvalidOperationException("Eater of Worlds healing Heart drop could not be materialized.");
    }

    private bool TryFindClosestPlayer(in NpcSnapshot npc, out PlayerStateSnapshot closest)
    {
        closest = default;
        if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition))
            return false;

        float npcCenterX = npc.PositionX + definition.Width * 0.5f;
        float npcCenterY = npc.PositionY + definition.Height * 0.5f;
        float bestDistance = -1f;
        bool foundAny = false;

        for (int index = 0; index < VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots; index++)
        {
            var slot = new PlayerSlotId(checked((byte)index));
            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player))
                continue;

            if (!foundAny)
            {
                closest = player;
                foundAny = true;
            }
            if (player.IsDead)
                continue;

            float playerCenterX = player.PositionX + VanillaPlayerWidth * 0.5f;
            float playerCenterY = player.PositionY + VanillaPlayerHeight * 0.5f;
            float distance = MathF.Abs(playerCenterX - npcCenterX) + MathF.Abs(playerCenterY - npcCenterY);
            if (bestDistance >= 0f && distance >= bestDistance)
                continue;

            bestDistance = distance;
            closest = player;
        }

        return foundAny;
    }

    private void ApplyKingSlimeDeathEffects(in NpcSnapshot kingSlime)
    {
        progression.SetSlimeBlueSpawnBaseline(worldClock?.SlimeBlueSpawnUnlocked == true);

        worldClock?.TryStopSlimeRain(random);
        if (worldClock is not null && progression.MarkSlimeBlueSpawnUnlocked())
        {
            worldClock.MarkSlimeBlueSpawnUnlocked();
            if (TryCreateNerdySlimeSpawnIntent(in kingSlime, out NpcAiSpawnIntent intent) &&
                npcs.TrySpawnIntent(in intent, out NpcSnapshot nerdy))
            {
                float velocityX = random.NextFloatDirection() * 3f;
                var update = new NpcStateUpdate(
                    nerdy.Type,
                    nerdy.NetId,
                    nerdy.PositionX,
                    nerdy.PositionY,
                    velocityX,
                    -10f,
                    nerdy.Target,
                    nerdy.Ai,
                    nerdy.Simulation);
                if (!npcs.TryUpdate(nerdy.Handle, in update, out _))
                    throw new InvalidOperationException("Nerdy Slime death spawn could not receive launch velocity.");
            }
        }
        progression.MarkCompleted(VanillaWorldProgressionId.KingSlime);
    }

    private static bool TryCreateNerdySlimeSpawnIntent(in NpcSnapshot source, out NpcAiSpawnIntent intent)
    {
        intent = default;
        if (!VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.KingSlime, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            return false;
        }

        float centerX = source.PositionX + hitbox.Width * 0.5f;
        float centerY = source.PositionY + hitbox.Height * 0.5f;
        intent = new NpcAiSpawnIntent(
            VanillaNpcIds.TownSlimeBlue,
            BottomX: (int)centerX - 10,
            BottomY: (int)centerY,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: checked((ushort)VanillaNpcDefinitionCatalog.DefaultTarget));
        return true;
    }

    private void ReleaseReservations(Span<WorldItemDropReservation> reservations)
    {
        for (int index = 0; index < reservations.Length; index++)
        {
            if (reservations[index].IsAssigned)
                worldItems.TryReleaseDropReservation(in reservations[index]);
        }
    }

    private sealed class SystemNpcCombatRandom : INpcLootRollSource, IKingSlimeDeathRandom
    {
        private readonly Random random = new();

        public int RollLuck(int chanceDenominator)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(chanceDenominator, 1);
            return random.Next(chanceDenominator);
        }

        public int NextInt32(int inclusiveMin, int exclusiveMax) => random.Next(inclusiveMin, exclusiveMax);

        public float NextFloatDirection() => random.NextSingle() * 2f - 1f;
    }
}
