using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class ProjectileKeyWrapTests
{
    [Fact]
    public void Original_NewProjectileSetup_generations_match_projection_and_packet_27_bits()
    {
        using Stream resource = typeof(ProjectileKeyWrapTests).Assembly.GetManifestResourceStream("ProjectileKeyWrap1458")!;
        using var bytes = new MemoryStream();
        resource.CopyTo(bytes);
        Assert.Equal("7e5ce31590f4a1950c28c3e5bfc0f3c15b9f419c7b0fb8f0e099959dc4c7c885",
            Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        foreach (var row in json.RootElement.EnumerateArray())
        {
            ushort generation = RuntimeProjectilePacketProjection.ToProtocolGeneration(
                new ProjectileGeneration(row.GetProperty("after").GetUInt64()));
            Assert.Equal(row.GetProperty("generation").GetUInt16(), generation);
            var key = new TerrariaProjectileKeyState(row.GetProperty("owner").GetByte(), 0, generation);
            var state = new TerrariaProjectileUpdateState(key, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            Assert.True(TerrariaProjectileEncoder.TryEncodeUpdate(in state, out var encoded));
            Assert.Equal(row.GetProperty("bits").GetUInt32(), BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(3)));

            // Independent minimal packet 27 layout from original NetMessage.SendData: packed key,
            // four position/velocity floats, short type and one optional-field flag byte.
            byte[] payload = new byte[23];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, row.GetProperty("bits").GetUInt32());
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(20), 1);
            var sequence = new ReadOnlySequence<byte>(payload);
            var frame = new TerrariaFrame(26, 27, sequence, sequence);
            Assert.Equal(TerrariaProjectileDecodeResult.Decoded,
                TerrariaProjectileDecoder.TryDecodeUpdate(in frame, out var decoded));
            Assert.Equal(key, decoded.Key);
        }
    }

    [Fact]
    public void Zero_key_is_not_an_empty_binding_and_stale_runtime_generations_cannot_unbind_its_replacement()
    {
        var identities = new RuntimeProjectileWireIdentityRegistry(runtimeCapacity: 2);
        TerrariaProjectileKeyState zero = default;
        var beforeWrap = new TerrariaProjectileKeyState(0, 0, 16383);
        var old = new ProjectileHandle(0, new ProjectileGeneration(16383));
        var wrapped = new ProjectileHandle(0, new ProjectileGeneration(16384));
        Assert.False(identities.TryResolve(in zero, out _));
        Assert.True(identities.TryBind(in beforeWrap, old));
        Assert.True(identities.TryBind(in zero, wrapped));
        Assert.False(identities.TryResolve(in beforeWrap, out _));
        Assert.False(identities.TryUnbind(old, out _));
        Assert.True(identities.TryResolve(in zero, out var found));
        Assert.Equal(wrapped, found);
        Assert.True(identities.TryGetWireKey(wrapped, out var retained));
        Assert.Equal(zero, retained);
        Assert.True(identities.TryUnbind(wrapped, out _));
        Assert.False(identities.TryResolve(in zero, out _));
    }
}
