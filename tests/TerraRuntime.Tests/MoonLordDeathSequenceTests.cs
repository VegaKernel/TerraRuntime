using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MoonLordDeathSequenceTests
{
    [Fact]
    public void Committed_death_clears_attacks_at_60_and_records_progression_only_at_600_without_players()
    {
        var npcs = new RuntimeNpcStore();
        var projectiles = new RuntimeProjectileStore();
        var progression = new RuntimeWorldProgressionMutations();
        var pipeline = CreatePipeline(npcs, projectiles, progression);
        NpcSnapshot core = SpawnNpc(npcs, VanillaNpcIds.MoonLordCore, new NpcAiState(2f, 0f, 0f, 0f));
        NpcSnapshot eye = SpawnNpc(npcs, VanillaNpcIds.MoonLordFreeEye, new NpcAiState(0f, 0f, 0f, core.Handle.Slot));
        NpcSnapshot unrelated = SpawnNpc(npcs, VanillaNpcIds.BlueSlime, default);
        ProjectileTypeId[] attackTypes =
        [
            VanillaProjectileIds.MoonLeech, VanillaProjectileIds.PhantasmalBolt,
            VanillaProjectileIds.PhantasmalDeathray, VanillaProjectileIds.PhantasmalEye,
            VanillaProjectileIds.PhantasmalSphere
        ];
        ProjectileSnapshot[] attacks = attackTypes.Select(type => SpawnProjectile(projectiles, type)).ToArray();
        ProjectileSnapshot other = SpawnProjectile(projectiles, VanillaProjectileIds.WoodenArrowFriendly);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        var executor = new RuntimeNpcAiStateExecutor(npcs, projectiles);

        // Exercise the same post-commit sink used by NpcAuthority, with no live target candidates.
        for (int tick = 1; tick <= 600; tick++)
        {
            executor.Tick(stepper, pipeline);
            npcs.DespawnExpired();
            Assert.Equal(tick < 600, npcs.TryGet(core.Handle, out _));
            Assert.Equal(tick >= 600, progression.IsCompleted(VanillaWorldProgressionId.MoonLord));
            Assert.Equal(tick < 60, npcs.TryGet(eye.Handle, out _));
            foreach (ProjectileSnapshot attack in attacks)
                Assert.Equal(tick < 60, projectiles.TryGet(attack.Handle, out _));
            Assert.True(npcs.TryGet(unrelated.Handle, out _));
            Assert.True(projectiles.TryGet(other.Handle, out _));
        }

        RuntimeWorldProgressionMutationSnapshot saved = progression.CaptureSnapshot();
        executor.Tick(stepper, pipeline);
        Assert.Equal(saved, progression.CaptureSnapshot());
    }

    [Theory]
    [InlineData(396)]
    [InlineData(397)]
    [InlineData(400)]
    public void Orphaned_moon_lord_parts_expire_without_adopting_another_core(int rawType)
    {
        var npcs = new RuntimeNpcStore();
        NpcSnapshot otherCore = SpawnNpc(npcs, VanillaNpcIds.MoonLordCore, default);
        NpcSnapshot child = SpawnNpc(npcs, new NpcTypeId(rawType), new NpcAiState(0f, 0f, 0f, 7f));
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.SetNpcPeers([otherCore, child]);

        Assert.True(stepper.TryStepState(in child, out NpcStateUpdate next));
        Assert.Equal(0, next.Simulation.Life);
        Assert.Equal(0, next.Simulation.TimeLeft);
        Span<NpcAiProjectileIntent> attacks = stackalloc NpcAiProjectileIntent[8];
        Assert.Equal(0, stepper.PlanProjectileSpawns(in child, in next, attacks));
    }

    [Fact]
    public void Stale_terminal_snapshot_cannot_complete_progression_or_despawn_a_reused_slot()
    {
        var npcs = new RuntimeNpcStore();
        var projectiles = new RuntimeProjectileStore();
        var progression = new RuntimeWorldProgressionMutations();
        var pipeline = CreatePipeline(npcs, projectiles, progression);
        NpcSnapshot core = SpawnNpc(npcs, VanillaNpcIds.MoonLordCore, new NpcAiState(2f, 600f, 0f, 0f));
        NpcSnapshot terminal = core with { Simulation = core.Simulation with { Life = 0 } };
        Assert.True(npcs.TryDespawn(core.Handle));
        NpcSnapshot replacement = SpawnNpc(npcs, VanillaNpcIds.BlueSlime, default);
        Assert.Equal(core.Handle.Slot, replacement.Handle.Slot);

        pipeline.NpcAiStateCommitted(in terminal);

        Assert.True(npcs.TryGet(replacement.Handle, out _));
        Assert.False(progression.IsCompleted(VanillaWorldProgressionId.MoonLord));
    }

    private static RuntimeNpcNetworkCombatPipeline CreatePipeline(
        RuntimeNpcStore npcs, RuntimeProjectileStore projectiles, RuntimeWorldProgressionMutations progression)
    {
        var items = new RuntimeWorldItemStore();
        return new RuntimeNpcNetworkCombatPipeline(
            npcs, items, new EmptyPlayers(), new PlayerAuthority(events: null, worldTiles: null),
            tickProvider: static () => 0, npcReplication: null,
            instancedLeases: new RuntimeWorldItemInstancedLeaseStore(items), worldItemReplication: null,
            worldClock: null, progression, expertMode: false, masterMode: false, projectiles: projectiles);
    }

    private static NpcSnapshot SpawnNpc(RuntimeNpcStore store, NpcTypeId type, NpcAiState ai)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(type, out VanillaNpcDefinition definition));
        var update = new NpcStateUpdate(type.Value, checked((short)type.Value), 100f, 100f, 0f, 0f, 0, ai,
            NpcSimulationState.Initial with
            {
                Life = definition.LifeMax, LifeMax = definition.LifeMax,
                LocalAi = new NpcAiState(0f, 0f, 0f, 1f)
            });
        Assert.True(store.TrySpawnVanilla(in update, out NpcSnapshot spawned));
        return spawned;
    }

    private static ProjectileSnapshot SpawnProjectile(RuntimeProjectileStore store, ProjectileTypeId type)
    {
        var intent = new NpcAiProjectileIntent(type, 100f, 100f, 1f, 0f, 20, 0f);
        Assert.True(RuntimeNpcProjectileIntentApplier.TryApply(store, in intent, out ProjectileSnapshot spawned));
        return spawned;
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }

    private sealed class EmptyPlayers : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            snapshot = default;
            return false;
        }
    }
}
