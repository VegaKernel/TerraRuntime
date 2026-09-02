using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime;

internal static class RuntimeActorCommerceSmoke
{
    public static bool Run(out string failure)
    {
        failure = string.Empty;
        var identities = new RuntimeNpcArchetypeIdentityStore(RuntimeNpcStore.MaximumAddressableCapacity);
        var npcs = new RuntimeNpcStore(commitSink: identities);
        var archetypes = new RuntimeNpcArchetypeRegistry();
        var shops = new RuntimeNpcShopCatalogRegistry();
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var playerSlots = new PlayerSlotPool(8);
        var serverPlayerIdentities = new RuntimeServerPlayerSlotRegistry(playerSlots);
        var serverPlayerStates = new RuntimeServerPlayerStateStore(serverPlayerIdentities, playerSlots.Capacity);
        var targetId = new ServerPlayerId("smoke:npc-target");
        if (serverPlayerIdentities.TryAcquire(
                targetId,
                out RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease? acquiredTarget) !=
                ServerPlayerSlotAcquireResult.Acquired ||
            acquiredTarget is null ||
            !serverPlayerStates.TrySpawn(targetId, 180f, 100f, out _))
        {
            acquiredTarget?.Dispose();
            failure = "merchant target player setup failed";
            return false;
        }

        using RuntimeServerPlayerSlotRegistry.ServerPlayerSlotLease target = acquiredTarget;
        var state = new ServerRuntimeState(
            npcs: npcs,
            worldTiles: tiles,
            serverPlayers: new ServerPlayerAuthority(serverPlayerStates, worldTiles: tiles),
            npcShops: shops,
            npcArchetypes: archetypes,
            npcArchetypeIdentities: identities);
        var archetypeId = new GameplayArchetypeId("smoke:merchant");
        var descriptor = new NpcArchetypeDescriptor(
            archetypeId,
            VanillaNpcIds.Zombie,
            Role: NpcArchetypeRole.Town);

        if (archetypes.TryRegister(descriptor, out IGameplayArchetypeRegistrationLease? archetype) !=
                GameplayArchetypeRegistrationResult.Registered ||
            archetype is null)
        {
            failure = "merchant archetype registration failed";
            return false;
        }

        using (archetype)
        {
            var catalog = new NpcShopCatalog(
                new ShopId("smoke:merchant"),
                archetypeId,
                [new ShopOffer(new ShopOfferId("dirt"), VanillaItemIds.DirtBlock, Stack: 1, UnitPrice: 25)]);
            if (shops.TryRegister(catalog, out NpcShopRegistrationLease? shop) != NpcShopRegistrationResult.Registered ||
                shop is null)
            {
                failure = "merchant shop registration failed";
                return false;
            }

            using (shop)
            {
                state.Tick();
                if (!shops.Snapshot.TryGetByArchetype(archetypeId, out _))
                {
                    failure = "merchant shop was not published at the tick boundary";
                    return false;
                }

                var spawnCompletion = new TaskCompletionSource<NpcActorSpawnResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                state.Apply(new NpcActorSpawnRuntimeCommand(
                    new NpcActorSpawnRequest(archetypeId, 100f, 100f),
                    spawnCompletion));
                NpcActorSpawnResult spawned = spawnCompletion.Task.Result;
                if (!spawned.IsSpawned || !identities.TryGet(spawned.Npc, out GameplayArchetypeId identity) || identity != archetypeId)
                {
                    failure = $"merchant actor spawn or identity bind failed: {spawned.Status}";
                    return false;
                }

                var controller = new ActorControllerId("smoke:merchant");
                var acquireCompletion = new TaskCompletionSource<NpcActorAcquireStatus>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                state.Apply(new NpcActorAcquireRuntimeCommand(spawned.Npc, controller, acquireCompletion));
                if (acquireCompletion.Task.Result != NpcActorAcquireStatus.Acquired)
                {
                    failure = "merchant actor control acquisition failed";
                    return false;
                }

                var intentCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                state.Apply(new NpcActorSetIntentRuntimeCommand(
                    spawned.Npc,
                    controller,
                    NpcActorIntent.MoveTo(180f, 100f),
                    intentCompletion));
                if (!intentCompletion.Task.Result || !state.TryCaptureNpcSnapshot(spawned.Npc, out NpcSnapshot before))
                {
                    failure = "merchant actor intent was rejected";
                    return false;
                }

                NpcSnapshot after = before;
                for (int tick = 0; tick < 8 && after.PositionX <= before.PositionX; tick++)
                {
                    state.Tick();
                    if (!state.TryCaptureNpcSnapshot(spawned.Npc, out after))
                        break;
                }

                if (after.PositionX <= before.PositionX)
                {
                    failure = $"merchant actor did not move through runtime-owned simulation: x={after.PositionX}, vx={after.VelocityX}";
                    return false;
                }

                var releaseCompletion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                state.Apply(new NpcActorReleaseControllerRuntimeCommand(controller, releaseCompletion));
                var despawnCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                state.Apply(new NpcDespawnRuntimeCommand(spawned.Npc, despawnCompletion));
                if (releaseCompletion.Task.Result != 1 || !despawnCompletion.Task.Result || identities.TryGet(spawned.Npc, out _))
                {
                    failure = "merchant actor cleanup failed";
                    return false;
                }
            }

            state.Tick();
            if (shops.Snapshot.Count != 0)
            {
                failure = "merchant shop retirement was not published";
                return false;
            }
        }

        state.Tick();
        if (archetypes.Snapshot.TryGet(archetypeId, out _))
        {
            failure = "merchant archetype retirement was not published";
            return false;
        }

        return true;
    }
}
