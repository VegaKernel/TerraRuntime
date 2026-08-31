using System.Buffers.Binary;
using global::Multiplicity.Packets;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class MultiplicityPacketSerializerTests
{
    [Fact]
    public void Serializes_exact_declared_length_into_the_final_frame()
    {
        var packet = new TestPacket(3, [0xAA, 0xBB, 0xCC]);

        Assert.True(MultiplicityPacketSerializer.TrySerialize(packet, out byte[] frame));
        Assert.Equal(new byte[] { 6, 0, 250, 0xAA, 0xBB, 0xCC }, frame);
    }

    [Fact]
    public void Rejects_model_that_writes_past_its_declared_length()
    {
        var packet = new TestPacket(1, [0xAA, 0xBB, 0xCC]);

        Assert.False(MultiplicityPacketSerializer.TrySerialize(packet, out byte[] frame));
        Assert.Empty(frame);
    }

    [Fact]
    public void Rejects_model_that_writes_less_than_its_declared_length()
    {
        var packet = new TestPacket(3, [0xAA]);

        Assert.False(MultiplicityPacketSerializer.TrySerialize(packet, out byte[] frame));
        Assert.Empty(frame);
    }

    [Fact]
    public void Common_exact_size_path_does_not_reintroduce_a_second_frame_buffer()
    {
        const int iterations = 512;
        const long maximumAllocatedBytesPerIteration = 160;
        byte[] payload = new byte[64];
        for (int i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i + 1);

        var packet = new TestPacket((short)payload.Length, payload);
        for (int i = 0; i < 32; i++)
            _ = MultiplicityPacketSerializer.Serialize(packet);

        long before = GC.GetAllocatedBytesForCurrentThread();
        int checksum = 0;
        for (int i = 0; i < iterations; i++)
        {
            byte[] frame = MultiplicityPacketSerializer.Serialize(packet);
            checksum += frame[^1];
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(checksum > 0);
        Assert.True(
            allocated <= iterations * maximumAllocatedBytesPerIteration,
            $"Exact-size serialization allocated {allocated / (double)iterations:F1} bytes/frame; " +
            $"expected <= {maximumAllocatedBytesPerIteration} bytes/frame so an intermediate frame buffer cannot return unnoticed.");
    }

    private sealed class TestPacket(short declaredPayloadLength, byte[] payload) : TerrariaPacket(250)
    {
        public override short GetLength() => declaredPayloadLength;

        public override void ToStream(Stream stream, bool includeHeader = true)
        {
            if (includeHeader)
            {
                Span<byte> header = stackalloc byte[PacketHeaderLength];
                BinaryPrimitives.WriteInt16LittleEndian(
                    header,
                    checked((short)(declaredPayloadLength + PacketHeaderLength)));
                header[sizeof(short)] = Id;
                stream.Write(header);
            }

            stream.Write(payload);
        }
    }
}
