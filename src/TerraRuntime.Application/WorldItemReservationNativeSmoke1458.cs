using System.Buffers;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal static class WorldItemReservationNativeSmoke1458
{
    internal static bool Run()
    {
        var replication = new RuntimeWorldItemReplicationRegistry();
        var store = new RuntimeWorldItemStore(replication);
        var state = new ServerRuntimeState(playerEvents: replication, worldItems: store);
        var slots = new PlayerSlotPool(1);
        if (!slots.TryAcquireConnection(out var lease) || lease is null) return false;
        using var session = new PlayerJoinSession(lease);
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(145822), session.Handle);
        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(16, 8192, 1024));
        if (!replication.TryRegister(connection.Source, outbound)) return false;
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 40, 40, 0, 0, 0, 0, 0)));
        if (state.LastSpawnCommitResult != PlayerSpawnCommitResult.Committed) return false;

        foreach (bool deferred in new[] { false, true })
        {
            var drop = new WorldItemDropStateUpdate(640, 640, -.5f, -2.5f, 1, 0,
                WorldItemOwnershipMode.ReserveForLocalPlayer, 530, false, 0, 100);
            short slot;
            using (var plan = store.CreateAllocationPreview())
            {
                if (!plan.TrySpawnSource(drop, 0, out slot, sourceLocalPlayerId: session.Slot.Value) || !plan.TryClaim()) return false;
                if (deferred)
                {
                    if (!plan.TryAdoptUnpublished(out var publication) || publication is null ||
                        !publication.TryPublishNext(out _)) return false;
                }
                else if (!plan.TryCommitNext(out _, out _)) return false;
            }
            // The live commit retains its slot claim until disposal. Release it before testing
            // an independent ReserveFor transition, as TryAllocateSourceDrop does for callers.
            if (!store.TryGetActive(slot, out var item) || item.TimeToKeepReservation != 0 ||
                item.OwnerPlayerId != session.Slot.Value ||
                !ReadFrame(outbound, out var liveDrop) || liveDrop.MessageId != (byte)TerrariaMessageId.WorldItemDrop ||
                !ReadFrame(outbound, out var liveOwner) || !HasHold(liveOwner, 100)) return false;
            if (WorldItemBootstrapPacketEncoder.TryEncode([item], out var baseline) != WorldItemBootstrapPacketEncodeResult.Encoded ||
                !Decode(baseline[1], out var joinedOwner) || !HasHold(joinedOwner, 0)) return false;
            var release = new WorldItemOwnerStateUpdate(byte.MaxValue, 0, item.GrabDelayPlayer,
                item.GrabDelayTime, item.PositionX, item.PositionY);
            if (!store.TryApplySourceOwner(item, release, true, out var accepted, connection.Source) ||
                accepted.TimeToKeepReservation != 0 || outbound.QueuedFrames != 0) return false;
            var offer = release with { OwnerPlayerId = item.OwnerPlayerId, TimeToKeepReservation = 15 };
            if (!store.TryApplySourceOwner(accepted, offer, false, out _) ||
                !ReadFrame(outbound, out var nextOwner) || !HasHold(nextOwner, 15)) return false;
        }
        return true;
    }

    private static bool ReadFrame(TerrariaConnectionOutboundQueue outbound, out TerrariaFrame frame)
    {
        frame = default;
        return outbound.InnerQueue.TryRead(out var value) && Decode(value.Bytes, out frame);
    }

    private static bool Decode(ReadOnlyMemory<byte> bytes, out TerrariaFrame frame)
    {
        var sequence = new ReadOnlySequence<byte>(bytes);
        return TerrariaFrameDecoder.TryRead(ref sequence, out frame) == TerrariaFrameReadResult.Frame;
    }

    private static bool HasHold(in TerrariaFrame frame, int expected) =>
        TerrariaWorldItemOwnerDecoder.TryDecode(frame, out var owner) == TerrariaWorldItemOwnerDecodeResult.Decoded &&
        owner.TimeToKeepReservation == expected;
}
