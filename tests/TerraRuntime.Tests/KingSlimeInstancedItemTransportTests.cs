using System.Buffers.Binary;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class KingSlimeInstancedItemTransportTests
{
    [Fact]
    public void Packet90_reuses_exact_packet21_payload_and_changes_only_message_id()
    {
        var state = new TerrariaWorldItemDropState(
            ItemIndex: 17,
            PositionX: 12.5f,
            PositionY: 24.5f,
            VelocityX: -1.25f,
            VelocityY: 2.5f,
            Stack: 1,
            Prefix: 3,
            ItemNetId: 3318,
            Ownership: TerrariaWorldItemOwnership.None,
            Shimmered: true,
            ShimmerTime: 7.5f,
            EnemyGrabDelayTime: 9);

        Assert.Equal(TerrariaWorldItemFrameEncodeResult.Encoded,
            TerrariaWorldItemFrameEncoder.TryEncodeDrop(in state, out ReadOnlyMemory<byte> packet21));
        Assert.Equal(TerrariaWorldItemFrameEncodeResult.Encoded,
            TerrariaWorldItemFrameEncoder.TryEncodeInstancedDrop(in state, out ReadOnlyMemory<byte> packet90));

        byte[] ordinary = packet21.ToArray();
        byte[] instanced = packet90.ToArray();
        Assert.Equal(ordinary.Length, instanced.Length);
        Assert.Equal((byte)21, ordinary[2]);
        Assert.Equal((byte)90, instanced[2]);
        ordinary[2] = 90;
        Assert.Equal(ordinary, instanced);
    }

    [Fact]
    public void Packet151_is_canonical_length_message_and_item_slot()
    {
        Assert.Equal(TerrariaWorldItemFrameEncodeResult.Encoded,
            TerrariaWorldItemFrameEncoder.TryEncodeInstancedSlotRelease(321, out ReadOnlyMemory<byte> frame));

        byte[] bytes = frame.ToArray();
        Assert.Equal(5, bytes.Length);
        Assert.Equal((ushort)5, BinaryPrimitives.ReadUInt16LittleEndian(bytes));
        Assert.Equal((byte)151, bytes[2]);
        Assert.Equal((short)321, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(3)));
    }

    [Fact]
    public void Leased_unpublished_slot_is_not_reused_until_exact_expiry_tick()
    {
        var store = new RuntimeWorldItemStore();
        var leases = new RuntimeWorldItemInstancedLeaseStore(store);
        WorldItemDropStateUpdate drop = Drop(3318);

        Assert.True(leases.TryLease(in drop, leaseTicks: 2, out WorldItemDropReservation leased));
        Assert.Equal((short)0, leased.Slot);
        Assert.False(store.TryGetActive(leased.Slot, out _));
        Assert.True(leases.TryGetRemainingTicks(leased.Slot, out int remaining));
        Assert.Equal(2, remaining);

        for (int index = 1; index < RuntimeWorldItemStore.VanillaCapacity; index++)
            Assert.True(store.TryAllocateDrop(in drop, out _));
        Assert.False(store.TryAllocateDrop(in drop, out _));

        Span<short> expired = stackalloc short[RuntimeWorldItemStore.VanillaCapacity];
        Assert.Equal(0, leases.Tick(expired));
        Assert.True(leases.TryGetRemainingTicks(leased.Slot, out remaining));
        Assert.Equal(1, remaining);
        Assert.False(store.TryAllocateDrop(in drop, out _));

        Assert.Equal(1, leases.Tick(expired));
        Assert.Equal((short)0, expired[0]);
        Assert.False(leases.TryGetRemainingTicks(leased.Slot, out _));
        Assert.True(store.TryAllocateDrop(in drop, out WorldItemSnapshot reused));
        Assert.Equal((short)0, reused.Handle.Slot);
    }

    private static WorldItemDropStateUpdate Drop(short itemNetId) =>
        new(
            PositionX: 10f,
            PositionY: 20f,
            VelocityX: 0f,
            VelocityY: 0f,
            Stack: 1,
            Prefix: 0,
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: itemNetId,
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0);
}
