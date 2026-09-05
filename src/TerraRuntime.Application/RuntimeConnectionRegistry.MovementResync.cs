using TerraRuntime.Gameplay.Items;
using System.Collections.Concurrent;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeConnectionRegistry
{
    internal RuntimePlayerMovementResyncPlan PlanPlayerMovementResyncs(
        PlayerSlotId subject,
        ReadOnlySpan<PlayerSlotId> enteredPeers,
        Span<RuntimePlayerMovementResyncOperation> destination)
    {
        int requiredCapacity = checked(enteredPeers.Length * 2);
        if (destination.Length < requiredCapacity)
        {
            throw new ArgumentException(
                $"Destination must have room for {requiredCapacity} possible resync operations.",
                nameof(destination));
        }

        int planned = 0;
        int missingSnapshots = 0;
        int missingEndpoints = 0;

        for (int i = 0; i < enteredPeers.Length; i++)
        {
            PlayerSlotId peer = enteredPeers[i];
            PlanOnePlayerMovementResync(
                peer,
                subject,
                destination,
                ref planned,
                ref missingSnapshots,
                ref missingEndpoints);
            PlanOnePlayerMovementResync(
                subject,
                peer,
                destination,
                ref planned,
                ref missingSnapshots,
                ref missingEndpoints);
        }

        return new RuntimePlayerMovementResyncPlan(planned, missingSnapshots, missingEndpoints);
    }

    internal bool TryEnqueuePlayerMovementResync(in RuntimePlayerMovementResyncOperation operation)
    {
        if (operation.Recipient == operation.Subject ||
            !_interestRouter.IsPlayerVisible(operation.Recipient.Slot, operation.Subject.Slot) ||
            !TryGetPlayingEndpoint(operation.Recipient, out RuntimeConnectionEndpoint recipient) ||
            !TryGetPlayingEndpoint(operation.Subject, out RuntimeConnectionEndpoint subject) ||
            !subject.TryGetLatestMovementFrame(operation.Subject, out OutboundFrame frame))
        {
            return false;
        }

        if (recipient.Outbound.TryEnqueue(frame) != OutboundEnqueueResult.Enqueued)
            return false;

        _movementVisibilityReadiness.MarkReady(operation.Recipient.Slot, operation.Subject.Slot);
        Interlocked.Increment(ref _movementResyncFrames);
        return true;
    }

    private void ResetMovementVisibilityReadiness(
        PlayerSlotId subject,
        RuntimePlayerVisibilityUpdate visibility,
        ReadOnlySpan<PlayerSlotId> entered,
        ReadOnlySpan<PlayerSlotId> left)
    {
        for (int i = 0; i < visibility.Entered; i++)
            _movementVisibilityReadiness.ClearPair(subject, entered[i]);

        for (int i = 0; i < visibility.Left; i++)
            _movementVisibilityReadiness.ClearPair(subject, left[i]);
    }

    private void PlanOnePlayerMovementResync(
        PlayerSlotId recipientSlot,
        PlayerSlotId subjectSlot,
        Span<RuntimePlayerMovementResyncOperation> destination,
        ref int planned,
        ref int missingSnapshots,
        ref int missingEndpoints)
    {
        if (!TryGetPlayingEndpoint(recipientSlot, out RuntimeConnectionEndpoint recipientEndpoint) ||
            !TryGetPlayingEndpoint(subjectSlot, out RuntimeConnectionEndpoint subjectEndpoint) ||
            !recipientEndpoint.TryGetPlayingPlayer(out PlayerHandle recipient) ||
            !subjectEndpoint.TryGetPlayingPlayer(out PlayerHandle subject))
        {
            missingEndpoints++;
            return;
        }

        if (!subjectEndpoint.TryGetLatestMovementFrame(subject, out _))
        {
            missingSnapshots++;
            return;
        }

        destination[planned++] = new RuntimePlayerMovementResyncOperation(recipient, subject);
    }
}

internal readonly record struct RuntimePlayerMovementResyncOperation(
    PlayerHandle Recipient,
    PlayerHandle Subject);

internal readonly record struct RuntimePlayerMovementResyncPlan(
    int Planned,
    int MissingSnapshots,
    int MissingEndpoints);
