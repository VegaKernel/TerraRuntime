using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;

namespace TerraRuntime;

internal sealed record NpcActorAcquireRuntimeCommand(
    NpcHandle Npc,
    ActorControllerId ControllerId,
    TaskCompletionSource<NpcActorAcquireStatus> Completion) : RuntimeCommand;

internal sealed record NpcActorSetIntentRuntimeCommand(
    NpcHandle Npc,
    ActorControllerId ControllerId,
    NpcActorIntent Intent,
    TaskCompletionSource<bool> Completion) : RuntimeCommand;

internal sealed record NpcActorReleaseRuntimeCommand(
    NpcHandle Npc,
    ActorControllerId ControllerId,
    TaskCompletionSource<bool> Completion) : RuntimeCommand;

internal sealed record NpcActorReleaseControllerRuntimeCommand(
    ActorControllerId ControllerId,
    TaskCompletionSource<int> Completion) : RuntimeCommand;

internal sealed record NpcActorSpawnRuntimeCommand(
    NpcActorSpawnRequest Request,
    TaskCompletionSource<NpcActorSpawnResult> Completion) : RuntimeCommand;

internal sealed record NpcBehaviorRegisterRuntimeCommand(
    GameplayExtensionId Id,
    INpcBehaviorProvider Provider,
    TaskCompletionSource<NpcBehaviorRegistrationResult> Completion) : RuntimeCommand;

internal sealed record NpcPresentationBehaviorRegisterRuntimeCommand(
    GameplayExtensionId Id,
    NpcTypeId PresentationType,
    NpcBehaviorStage Stage,
    int Order,
    INpcBehaviorProvider Provider,
    TaskCompletionSource<NpcBehaviorRegistrationResult> Completion) : RuntimeCommand;

/// <summary>
/// Authoritative-thread owner of actor leases and host behavior registrations. Host calls never touch live behavior
/// snapshots directly: commands arrive through ServerRuntimeState, stage registry changes on the game-loop thread,
/// and CommitPending publishes immutable dispatch plans immediately before the authoritative NPC tick.
/// </summary>
internal sealed class RuntimeNpcActorControlOwner
{
    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeNpcActorControlRegistry controls;
    private readonly RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper> presentationBehaviors;
    private readonly RuntimeArchetypeBehaviorRegistry<INpcAiStateStepper> archetypeBehaviors;
    private readonly INpcBehaviorQueries behaviorQueries;
    private readonly RuntimeNpcArchetypeRegistry archetypes;
    private readonly RuntimeNpcArchetypeIdentityStore identities;
    private readonly NpcActorControlLease?[] leases;

    public RuntimeNpcActorControlOwner(
        RuntimeNpcStore npcs,
        RuntimeNpcActorControlRegistry controls,
        RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper> presentationBehaviors,
        RuntimeArchetypeBehaviorRegistry<INpcAiStateStepper> archetypeBehaviors,
        INpcBehaviorQueries behaviorQueries,
        RuntimeNpcArchetypeRegistry archetypes,
        RuntimeNpcArchetypeIdentityStore identities)
    {
        ArgumentNullException.ThrowIfNull(npcs);
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(presentationBehaviors);
        ArgumentNullException.ThrowIfNull(archetypeBehaviors);
        ArgumentNullException.ThrowIfNull(behaviorQueries);
        ArgumentNullException.ThrowIfNull(archetypes);
        ArgumentNullException.ThrowIfNull(identities);
        this.npcs = npcs;
        this.controls = controls;
        this.presentationBehaviors = presentationBehaviors;
        this.archetypeBehaviors = archetypeBehaviors;
        this.behaviorQueries = behaviorQueries;
        this.archetypes = archetypes;
        this.identities = identities;
        leases = new NpcActorControlLease?[npcs.Capacity];
    }

    public bool TryApply(RuntimeCommand command)
    {
        switch (command)
        {
            case NpcActorAcquireRuntimeCommand acquire:
                acquire.Completion.TrySetResult(Acquire(acquire.Npc, acquire.ControllerId));
                return true;

            case NpcActorSetIntentRuntimeCommand setIntent:
                NpcActorIntent intent = setIntent.Intent;
                setIntent.Completion.TrySetResult(
                    SetIntent(setIntent.Npc, setIntent.ControllerId, in intent));
                return true;

            case NpcActorReleaseRuntimeCommand release:
                release.Completion.TrySetResult(Release(release.Npc, release.ControllerId));
                return true;

            case NpcActorReleaseControllerRuntimeCommand releaseController:
                releaseController.Completion.TrySetResult(ReleaseController(releaseController.ControllerId));
                return true;

            case NpcBehaviorRegisterRuntimeCommand behavior:
                behavior.Completion.TrySetResult(RegisterBehavior(behavior.Id, behavior.Provider));
                return true;

            case NpcPresentationBehaviorRegisterRuntimeCommand behavior:
                behavior.Completion.TrySetResult(RegisterPresentationBehavior(
                    behavior.Id,
                    behavior.PresentationType,
                    behavior.Stage,
                    behavior.Order,
                    behavior.Provider));
                return true;

            default:
                return false;
        }
    }

    public NpcActorAcquireStatus Acquire(NpcHandle npc, ActorControllerId controllerId)
    {
        if (!npc.IsAssigned || npc.Slot >= leases.Length)
            return NpcActorAcquireStatus.InvalidActor;
        if (!controllerId.IsAssigned)
            return NpcActorAcquireStatus.InvalidController;

        NpcActorControlLease? current = leases[npc.Slot];
        if (current is not null)
        {
            if (current.Npc == npc)
                return NpcActorAcquireStatus.AlreadyControlled;

            current.Dispose();
            leases[npc.Slot] = null;
        }

        NpcActorControlAcquireResult result = controls.TryAcquire(npc, controllerId, out NpcActorControlLease? lease);
        if (result == NpcActorControlAcquireResult.Acquired)
            leases[npc.Slot] = lease ?? throw new InvalidOperationException("Actor registry acquired control without returning a lease.");

        return result switch
        {
            NpcActorControlAcquireResult.Acquired => NpcActorAcquireStatus.Acquired,
            NpcActorControlAcquireResult.InvalidActor => NpcActorAcquireStatus.InvalidActor,
            NpcActorControlAcquireResult.InvalidController => NpcActorAcquireStatus.InvalidController,
            NpcActorControlAcquireResult.UnsupportedNpcType => NpcActorAcquireStatus.UnsupportedNpcType,
            NpcActorControlAcquireResult.AlreadyControlled => NpcActorAcquireStatus.AlreadyControlled,
            _ => throw new InvalidOperationException("Unknown actor-control acquire result.")
        };
    }

    public bool SetIntent(NpcHandle npc, ActorControllerId controllerId, in NpcActorIntent intent)
    {
        if (!npc.IsAssigned || npc.Slot >= leases.Length || !controllerId.IsAssigned || !intent.IsValid)
            return false;

        NpcActorControlLease? lease = leases[npc.Slot];
        if (lease is null || lease.Npc != npc || lease.ControllerId != controllerId)
            return false;

        return intent.Kind switch
        {
            NpcActorIntentKind.Stop => lease.TryStop(intent.Motion),
            NpcActorIntentKind.MoveTo => lease.TryMoveTo(intent.TargetX, intent.TargetY, intent.Motion),
            NpcActorIntentKind.FollowPlayer => lease.TryFollowPlayer(intent.TargetPlayer, intent.Motion),
            _ => false
        };
    }

    public bool Release(NpcHandle npc, ActorControllerId controllerId)
    {
        if (!npc.IsAssigned || npc.Slot >= leases.Length || !controllerId.IsAssigned)
            return false;

        NpcActorControlLease? lease = leases[npc.Slot];
        if (lease is null || lease.Npc != npc || lease.ControllerId != controllerId)
            return false;

        leases[npc.Slot] = null;
        lease.Dispose();
        return true;
    }

    public int ReleaseController(ActorControllerId controllerId)
    {
        if (!controllerId.IsAssigned)
            return 0;

        int released = 0;
        for (int slot = 0; slot < leases.Length; slot++)
        {
            NpcActorControlLease? lease = leases[slot];
            if (lease is null || lease.ControllerId != controllerId)
                continue;

            leases[slot] = null;
            lease.Dispose();
            released++;
        }

        return released;
    }

    public void CommitPending()
    {
        for (int slot = 0; slot < leases.Length; slot++)
        {
            NpcActorControlLease? lease = leases[slot];
            if (lease is null || npcs.TryGet(lease.Npc, out _))
                continue;

            leases[slot] = null;
            lease.Dispose();
        }

        presentationBehaviors.CommitPending();
        archetypeBehaviors.CommitPending();
        controls.CommitPending();
    }

    private NpcBehaviorRegistrationResult RegisterBehavior(
        GameplayExtensionId id,
        INpcBehaviorProvider? provider)
    {
        if (!id.IsAssigned)
            return new NpcBehaviorRegistrationResult(NpcBehaviorRegistrationStatus.InvalidId, null);
        if (provider is null)
            return new NpcBehaviorRegistrationResult(NpcBehaviorRegistrationStatus.InvalidProvider, null);

        var stepper = new RuntimeHostNpcBehaviorStepper(id, provider, behaviorQueries, archetypes, identities);
        GameplayBehaviorRegistrationResult result = archetypeBehaviors.TryRegister(
            id,
            stepper,
            out IGameplayBehaviorRegistrationLease? lease);
        return ToHostRegistrationResult(result, lease);
    }

    private NpcBehaviorRegistrationResult RegisterPresentationBehavior(
        GameplayExtensionId id,
        NpcTypeId presentationType,
        NpcBehaviorStage stage,
        int order,
        INpcBehaviorProvider? provider)
    {
        if (!id.IsAssigned)
            return new NpcBehaviorRegistrationResult(NpcBehaviorRegistrationStatus.InvalidId, null);
        if (!presentationType.IsAssigned || !VanillaNpcDefinitionCatalog.TryGet(presentationType, out _))
            return new NpcBehaviorRegistrationResult(NpcBehaviorRegistrationStatus.InvalidTarget, null);
        if (!Enum.IsDefined(stage))
            return new NpcBehaviorRegistrationResult(NpcBehaviorRegistrationStatus.InvalidStage, null);
        if (provider is null)
            return new NpcBehaviorRegistrationResult(NpcBehaviorRegistrationStatus.InvalidProvider, null);

        GameplayBehaviorStage runtimeStage = stage switch
        {
            NpcBehaviorStage.Pre => GameplayBehaviorStage.Pre,
            NpcBehaviorStage.Replacement => GameplayBehaviorStage.Replacement,
            NpcBehaviorStage.Post => GameplayBehaviorStage.Post,
            _ => throw new InvalidOperationException("Validated NPC behavior stage was not mapped.")
        };
        var stepper = new RuntimeHostNpcBehaviorStepper(id, provider, behaviorQueries, archetypes, identities);
        GameplayBehaviorRegistrationResult result = presentationBehaviors.TryRegister(
            id,
            presentationType,
            runtimeStage,
            order,
            stepper,
            out IGameplayBehaviorRegistrationLease? lease);
        return ToHostRegistrationResult(result, lease);
    }

    private static NpcBehaviorRegistrationResult ToHostRegistrationResult(
        GameplayBehaviorRegistrationResult result,
        IGameplayBehaviorRegistrationLease? lease)
    {
        NpcBehaviorRegistrationStatus status = result switch
        {
            GameplayBehaviorRegistrationResult.Registered => NpcBehaviorRegistrationStatus.Registered,
            GameplayBehaviorRegistrationResult.InvalidId => NpcBehaviorRegistrationStatus.InvalidId,
            GameplayBehaviorRegistrationResult.InvalidStage => NpcBehaviorRegistrationStatus.InvalidStage,
            GameplayBehaviorRegistrationResult.DuplicateId => NpcBehaviorRegistrationStatus.DuplicateId,
            GameplayBehaviorRegistrationResult.ReplacementConflict => NpcBehaviorRegistrationStatus.ReplacementConflict,
            _ => throw new InvalidOperationException($"Unknown NPC behavior registration result '{result}'.")
        };
        INpcBehaviorRegistration? registration = lease is null ? null : new BehaviorRegistration(lease);
        return new NpcBehaviorRegistrationResult(status, registration);
    }

    private sealed class BehaviorRegistration(IGameplayBehaviorRegistrationLease lease) : INpcBehaviorRegistration
    {
        public GameplayExtensionId Id => lease.Id;
        public bool IsRetirementPending => lease.IsRetirementPending;
        public bool IsRetired => lease.IsRetired;
        public void Dispose() => lease.Dispose();
    }
}

/// <summary>Trusted-host facade that serializes actor control and behavior registration through the command queue.</summary>
internal sealed class RuntimeNpcActorOperations : INpcActorOperations
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;
    private readonly RuntimeNpcArchetypeRegistry archetypes;

    public RuntimeNpcActorOperations(
        IGameCommandIngress<RuntimeCommand> ingress,
        RuntimeNpcArchetypeRegistry archetypes)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        ArgumentNullException.ThrowIfNull(archetypes);
        this.ingress = ingress;
        this.archetypes = archetypes;
    }

    public NpcArchetypeRegistrationStatus TryRegisterArchetype(
        NpcArchetypeDescriptor descriptor,
        out INpcArchetypeRegistration? registration)
    {
        GameplayArchetypeRegistrationResult result = archetypes.TryRegister(descriptor, out IGameplayArchetypeRegistrationLease? lease);
        registration = lease is null ? null : new ArchetypeRegistration(lease);
        return result switch
        {
            GameplayArchetypeRegistrationResult.Registered => NpcArchetypeRegistrationStatus.Registered,
            GameplayArchetypeRegistrationResult.InvalidDescriptor => NpcArchetypeRegistrationStatus.InvalidDescriptor,
            GameplayArchetypeRegistrationResult.DuplicateId => NpcArchetypeRegistrationStatus.DuplicateId,
            _ => throw new InvalidOperationException($"Unknown NPC archetype registration result '{result}'.")
        };
    }

    public async ValueTask<NpcBehaviorRegistrationResult> RegisterBehaviorAsync(
        GameplayExtensionId id,
        INpcBehaviorProvider provider,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (provider is null)
            return new NpcBehaviorRegistrationResult(NpcBehaviorRegistrationStatus.InvalidProvider, null);

        var completion = NewCompletion<NpcBehaviorRegistrationResult>();
        if (!ingress.TryPost(GameCommandSourceId.System, new NpcBehaviorRegisterRuntimeCommand(id, provider, completion)))
            return new NpcBehaviorRegistrationResult(NpcBehaviorRegistrationStatus.QueueRejected, null);

        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<NpcBehaviorRegistrationResult> RegisterPresentationBehaviorAsync(
        GameplayExtensionId id,
        NpcTypeId presentationType,
        NpcBehaviorStage stage,
        int order,
        INpcBehaviorProvider provider,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (provider is null)
            return new NpcBehaviorRegistrationResult(NpcBehaviorRegistrationStatus.InvalidProvider, null);

        var completion = NewCompletion<NpcBehaviorRegistrationResult>();
        var command = new NpcPresentationBehaviorRegisterRuntimeCommand(
            id,
            presentationType,
            stage,
            order,
            provider,
            completion);
        if (!ingress.TryPost(GameCommandSourceId.System, command))
            return new NpcBehaviorRegistrationResult(NpcBehaviorRegistrationStatus.QueueRejected, null);

        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<NpcActorSpawnResult> SpawnAsync(
        NpcActorSpawnRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = NewCompletion<NpcActorSpawnResult>();
        if (!ingress.TryPost(GameCommandSourceId.System, new NpcActorSpawnRuntimeCommand(request, completion)))
            return new NpcActorSpawnResult(NpcActorSpawnStatus.QueueRejected, default);

        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<bool> DespawnAsync(
        NpcHandle npc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = NewCompletion<bool>();
        if (!ingress.TryPost(GameCommandSourceId.System, new NpcDespawnRuntimeCommand(npc, completion)))
            return false;

        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<NpcActorAcquireStatus> AcquireAsync(
        NpcHandle npc,
        ActorControllerId controllerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = NewCompletion<NpcActorAcquireStatus>();
        if (!ingress.TryPost(GameCommandSourceId.System, new NpcActorAcquireRuntimeCommand(npc, controllerId, completion)))
            return NpcActorAcquireStatus.QueueRejected;

        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<bool> SetIntentAsync(
        NpcHandle npc,
        ActorControllerId controllerId,
        NpcActorIntent intent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = NewCompletion<bool>();
        if (!ingress.TryPost(GameCommandSourceId.System, new NpcActorSetIntentRuntimeCommand(npc, controllerId, intent, completion)))
            throw new InvalidOperationException("The authoritative command queue rejected the actor intent command.");

        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<bool> ReleaseAsync(
        NpcHandle npc,
        ActorControllerId controllerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = NewCompletion<bool>();
        if (!ingress.TryPost(GameCommandSourceId.System, new NpcActorReleaseRuntimeCommand(npc, controllerId, completion)))
            throw new InvalidOperationException("The authoritative command queue rejected the actor release command.");

        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask<int> ReleaseControllerAsync(
        ActorControllerId controllerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = NewCompletion<int>();
        if (!ingress.TryPost(GameCommandSourceId.System, new NpcActorReleaseControllerRuntimeCommand(controllerId, completion)))
            throw new InvalidOperationException("The authoritative command queue rejected the controller release command.");

        return await completion.Task.ConfigureAwait(false);
    }

    private static TaskCompletionSource<T> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ArchetypeRegistration(IGameplayArchetypeRegistrationLease lease) : INpcArchetypeRegistration
    {
        public GameplayArchetypeId Id => lease.Id;

        public void Dispose() => lease.Dispose();
    }
}
