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

/// <summary>
/// Authoritative-thread owner of actor leases. Host calls never touch the registry directly: commands arrive through
/// ServerRuntimeState, mutate this service on the game-loop thread, and become visible to simulation at CommitPending.
/// </summary>
internal sealed class RuntimeNpcActorControlCommandService
{
    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeNpcActorControlRegistry controls;
    private readonly NpcActorControlLease?[] leases;

    public RuntimeNpcActorControlCommandService(
        RuntimeNpcStore npcs,
        RuntimeNpcActorControlRegistry controls)
    {
        ArgumentNullException.ThrowIfNull(npcs);
        ArgumentNullException.ThrowIfNull(controls);
        this.npcs = npcs;
        this.controls = controls;
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

        controls.CommitPending();
    }
}

/// <summary>Trusted-host facade that serializes actor control through the authoritative command queue.</summary>
internal sealed class RuntimeNpcActorOperations : INpcActorOperations
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimeNpcActorOperations(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        this.ingress = ingress;
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
}
