using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcBehaviorHostBoundaryTests
{
    [Fact]
    public async Task Archetype_behavior_runs_on_production_runtime_without_changing_vanilla_presentation()
    {
        var identities = new RuntimeNpcArchetypeIdentityStore(RuntimeNpcStore.MaximumAddressableCapacity);
        var npcs = new RuntimeNpcStore(commitSink: identities);
        var archetypes = new RuntimeNpcArchetypeRegistry();
        var state = new ServerRuntimeState(
            npcs: npcs,
            npcArchetypes: archetypes,
            npcArchetypeIdentities: identities);
        var behaviorId = new GameplayExtensionId("test:resident-zombie-ai");
        var archetypeId = new GameplayArchetypeId("test:resident-zombie");
        var descriptor = new NpcArchetypeDescriptor(
            archetypeId,
            VanillaNpcIds.Zombie,
            behaviorId);
        Assert.Equal(
            GameplayArchetypeRegistrationResult.Registered,
            archetypes.TryRegister(descriptor, out IGameplayArchetypeRegistrationLease? archetypeRegistration));

        var provider = new RecordingBehaviorProvider(velocityX: 2.75f);
        var behaviorCompletion = new TaskCompletionSource<NpcBehaviorRegistrationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new NpcBehaviorRegisterRuntimeCommand(behaviorId, provider, behaviorCompletion));
        NpcBehaviorRegistrationResult behaviorRegistration = await behaviorCompletion.Task;
        Assert.Equal(NpcBehaviorRegistrationStatus.Registered, behaviorRegistration.Status);
        Assert.NotNull(behaviorRegistration.Registration);

        var spawnCompletion = new TaskCompletionSource<NpcActorSpawnResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new NpcActorSpawnRuntimeCommand(
            new NpcActorSpawnRequest(archetypeId, 100f, 120f),
            spawnCompletion));
        NpcActorSpawnResult spawned = await spawnCompletion.Task;
        Assert.True(spawned.IsSpawned);

        state.Tick();

        Assert.True(state.TryCaptureNpcSnapshot(spawned.Npc, out NpcSnapshot afterTick));
        Assert.Equal(1, provider.Calls);
        Assert.Equal(archetypeId, provider.LastArchetypeId);
        Assert.True(provider.ResolvedCurrentNpc);
        Assert.Equal(VanillaNpcIds.Zombie.Value, afterTick.Type);
        Assert.Equal(checked((short)VanillaNpcIds.Zombie.Value), afterTick.NetId);
        Assert.Equal(2.75f, afterTick.VelocityX);

        behaviorRegistration.Registration!.Dispose();
        Assert.True(behaviorRegistration.Registration.IsRetirementPending);
        state.Tick();

        Assert.Equal(1, provider.Calls);
        Assert.True(behaviorRegistration.Registration.IsRetired);
        archetypeRegistration!.Dispose();
    }

    [Fact]
    public async Task Presentation_replacement_is_published_at_tick_boundary_for_vanilla_npc()
    {
        var state = new ServerRuntimeState();
        var behaviorId = new GameplayExtensionId("test:zombie-presentation-replacement");
        var provider = new RecordingBehaviorProvider(velocityX: -1.5f);
        var behaviorCompletion = new TaskCompletionSource<NpcBehaviorRegistrationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new NpcPresentationBehaviorRegisterRuntimeCommand(
            behaviorId,
            VanillaNpcIds.Zombie,
            NpcBehaviorStage.Replacement,
            Order: 0,
            provider,
            behaviorCompletion));
        NpcBehaviorRegistrationResult registration = await behaviorCompletion.Task;
        Assert.True(registration.IsRegistered);

        var spawnCompletion = new TaskCompletionSource<NpcSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var spawn = new NpcStateUpdate(
            VanillaNpcIds.Zombie.Value,
            checked((short)VanillaNpcIds.Zombie.Value),
            PositionX: 40f,
            PositionY: 50f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with { DirectionX = 1 });
        state.Apply(new NpcSpawnRuntimeCommand(3, spawn, spawnCompletion));
        NpcSnapshot created = Assert.IsType<NpcSnapshot>(await spawnCompletion.Task);

        Assert.Equal(0, provider.Calls);
        state.Tick();

        Assert.Equal(1, provider.Calls);
        Assert.Equal(default(GameplayArchetypeId), provider.LastArchetypeId);
        Assert.True(state.TryCaptureNpcSnapshot(created.Handle, out NpcSnapshot afterTick));
        Assert.Equal(-1.5f, afterTick.VelocityX);
        Assert.Equal(created.Type, afterTick.Type);
        Assert.Equal(created.NetId, afterTick.NetId);

        registration.Registration!.Dispose();
    }

    private sealed class RecordingBehaviorProvider(float velocityX) : INpcBehaviorProvider
    {
        public int Calls { get; private set; }
        public GameplayArchetypeId LastArchetypeId { get; private set; }
        public bool ResolvedCurrentNpc { get; private set; }

        public bool TryStep(in NpcBehaviorContext context, out NpcBehaviorState next)
        {
            Calls++;
            LastArchetypeId = context.ArchetypeId;
            NpcSnapshot callbackNpc = context.Npc;
            ResolvedCurrentNpc = context.TryGetNpc(callbackNpc.Handle, out NpcSnapshot current) &&
                current.Handle == callbackNpc.Handle;
            next = NpcBehaviorState.FromSnapshot(in callbackNpc) with
            {
                VelocityX = velocityX
            };
            return true;
        }
    }
}
