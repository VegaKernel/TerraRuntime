using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class MoonLordDeathSequenceTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void Missing_exact_shell_slot_removes_core_without_loot_or_progression(int missingPart, bool withPlayer)
    {
        var npcs = new RuntimeNpcStore();
        var items = new RuntimeWorldItemStore();
        var projectiles = new RuntimeProjectileStore();
        var progression = new RuntimeWorldProgressionMutations();
        var pipeline = CreatePipeline(npcs, projectiles, progression, items);
        NpcSnapshot core = SpawnNpc(npcs, VanillaNpcIds.MoonLordCore, new NpcAiState(-1f, 59f, 0f, 0f));
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 500f, 300f, 0, true, false, false, false)]);
        var executor = new RuntimeNpcAiStateExecutor(npcs, projectiles);
        executor.Tick(stepper, pipeline);
        Assert.True(npcs.TryGet(core.Handle, out core));
        Assert.Equal(0f, core.Ai.Ai0);
        float[] slots = [core.Simulation.LocalAi.Ai0, core.Simulation.LocalAi.Ai1, core.Simulation.LocalAi.Ai2];
        Assert.Equal(3, slots.Distinct().Count());
        NpcSnapshot removed = default;
        for (int i = 0; i < slots.Length; i++)
        {
            Assert.True(npcs.TryGetActive((byte)slots[i], out NpcSnapshot part));
            Assert.Equal(i == 2 ? VanillaNpcIds.MoonLordHead : VanillaNpcIds.MoonLordHand, part.TypeIdentity);
            Assert.Equal(core.Handle.Slot, part.Ai.Ai3);
            if (i == missingPart) removed = part;
        }

        Assert.True(npcs.TryDespawn(removed.Handle));
        NpcSnapshot blocker = SpawnNpc(npcs, VanillaNpcIds.BlueSlime, default, removed.Handle.Slot);
        Assert.Equal(removed.Handle.Slot, blocker.Handle.Slot);
        // An otherwise matching owned part elsewhere must not replace the original shell slot.
        SpawnNpc(npcs, removed.TypeIdentity, removed.Ai);
        if (!withPlayer) stepper.SetCandidates([]);
        executor.Tick(stepper, pipeline);
        npcs.DespawnExpired();
        Assert.False(npcs.TryGet(core.Handle, out _));
        Assert.True(npcs.TryGet(blocker.Handle, out _));
        Assert.Equal(0, items.ActiveCount);
        Assert.False(progression.IsCompleted(VanillaWorldProgressionId.MoonLord));
    }

    [Fact]
    public void Partial_shell_allocation_retains_missing_slot_and_expires_core()
    {
        var npcs = new RuntimeNpcStore(4);
        SpawnNpc(npcs, VanillaNpcIds.BlueSlime, default);
        NpcSnapshot core = SpawnNpc(npcs, VanillaNpcIds.MoonLordCore, default);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 500f, 300f, 0, true, false, false, false)]);
        // A real new core must initialize missing links before best-effort shell allocation.
        var initial = new NpcStateUpdate(core.Type, core.NetId, core.PositionX, core.PositionY,
            0f, 0f, core.Target, default, core.Simulation with { LocalAi = default });
        Assert.True(npcs.TryUpdate(core.Handle, in initial, out core));
        var executor = new RuntimeNpcAiStateExecutor(npcs);
        for (int tick = 0; tick < 60; tick++) executor.Tick(stepper);
        Assert.True(npcs.TryGet(core.Handle, out core));
        Assert.Equal(-1f, core.Simulation.LocalAi.Ai2);
        executor.Tick(stepper);
        npcs.DespawnExpired();
        Assert.False(npcs.TryGet(core.Handle, out _));
    }

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
        NpcSnapshot replacement = SpawnNpc(npcs, VanillaNpcIds.BlueSlime, default, core.Handle.Slot);
        Assert.Equal(core.Handle.Slot, replacement.Handle.Slot);

        pipeline.NpcAiStateCommitted(in terminal);

        Assert.True(npcs.TryGet(replacement.Handle, out _));
        Assert.False(progression.IsCompleted(VanillaWorldProgressionId.MoonLord));
    }

    private static RuntimeNpcNetworkCombatPipeline CreatePipeline(
        RuntimeNpcStore npcs, RuntimeProjectileStore projectiles, RuntimeWorldProgressionMutations progression,
        RuntimeWorldItemStore? items = null, RuntimeWorldClock? clock = null,
        RuntimeProjectileReplicationRegistry? projectileReplication = null)
    {
        items ??= new RuntimeWorldItemStore();
        return new RuntimeNpcNetworkCombatPipeline(
            npcs, items, new EmptyPlayers(), new PlayerAuthority(events: null, worldTiles: null),
            tickProvider: static () => 0, npcReplication: null,
            instancedLeases: new RuntimeWorldItemInstancedLeaseStore(items), worldItemReplication: null,
            worldClock: clock, progression, expertMode: false, masterMode: false, projectiles: projectiles,
            projectileReplication: projectileReplication);
    }

    private static NpcSnapshot SpawnNpc(RuntimeNpcStore store, NpcTypeId type, NpcAiState ai, byte? forceSlot = null)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(type, out VanillaNpcDefinition definition));
        var update = new NpcStateUpdate(type.Value, checked((short)type.Value), 100f, 100f, 0f, 0f, 0, ai,
            NpcSimulationState.Initial with
            {
                Life = definition.LifeMax, LifeMax = definition.LifeMax,
                LocalAi = new NpcAiState(0f, 0f, 0f, 1f)
            });
        // Adversarial stale-handle tests explicitly replace a slot; ordinary vanilla allocation observes protection.
        bool created = forceSlot is byte slot ? store.TrySpawn(slot, in update, out NpcSnapshot spawned) :
            store.TrySpawnVanilla(in update, out spawned);
        Assert.True(created);
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
