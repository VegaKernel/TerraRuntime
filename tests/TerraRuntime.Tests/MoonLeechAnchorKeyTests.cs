using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class MoonLeechAnchorKeyTests
{
    private static readonly JsonElement[] Rows = ReadCases();
    public static TheoryData<int> Cases => new(Enumerable.Range(0, 36));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Original_anchor_bits_survive_state_and_follow_packet_23_presence_rules(int index)
    {
        uint bits = Rows[index].GetProperty("bits").GetUInt32();
        float key = BitConverter.UInt32BitsToSingle(bits);
        Assert.Equal(Rows[index].GetProperty("nan").GetBoolean(), float.IsNaN(key));
        Assert.Equal(Rows[index].GetProperty("infinity").GetBoolean(), float.IsInfinity(key));
        var update = CreateNpc(key);
        var store = new RuntimeNpcStore();
        Assert.True(store.TrySpawn(0, in update, out var spawned));
        Assert.Equal(bits, BitConverter.SingleToUInt32Bits(spawned.Ai.Ai1));
        var composite = new RuntimeNpcBehaviorStateStepper(new FixedStepper(update),
            new RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper>());
        Assert.True(composite.TryStepState(in spawned, out var proposed));
        Assert.True(store.TryUpdate(spawned.Handle, in proposed, out var committed));
        Assert.Equal(bits, BitConverter.SingleToUInt32Bits(committed.Ai.Ai1));
        var wire = CreateWire(key);
        Assert.True(TerrariaNpcUpdateEncoder.TryEncode(in wire, out var encoded));
        Assert.Equal(23, encoded[2]);
        // Original NetMessage.SendData(23): slot/generation, four floats, ushort target,
        // two flag bytes, then the present AI fields. Only AI[1] is present in this fixture.
        if (bits == 0x80000000)
        {
            // NetMessage's ai[1] != 0f presence test omits negative zero as well. The original
            // key (spawner0,index0,generation8192) consequently loses its sign bit on the wire.
            Assert.Equal(0, encoded[23] & (1 << 3));
            Assert.Equal(27, encoded.Length);
        }
        else Assert.Equal(bits, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(25, 4)));
    }

    [Fact]
    public void Opaque_anchor_exception_does_not_admit_invalid_geometry_other_ai_or_out_of_range_indices()
    {
        float key = BitConverter.UInt32BitsToSingle(0x7fc000ff);
        var good = CreateNpc(key);
        var store = new RuntimeNpcStore();
        var wrongType = good with { Type = VanillaNpcIds.MoonLordHead.Value };
        Assert.False(store.TrySpawn(0, in wrongType, out _));
        var badPosition = good with { PositionX = float.NaN };
        Assert.False(store.TrySpawn(0, in badPosition, out _));
        var badAi = good with { Ai = good.Ai with { Ai2 = float.PositiveInfinity } };
        Assert.False(store.TrySpawn(0, in badAi, out _));
        float invalidIndex = BitConverter.UInt32BitsToSingle((1u << 18) | (1001u << 8));
        var badKey = good with { Ai = good.Ai with { Ai1 = invalidIndex } };
        Assert.False(store.TrySpawn(0, in badKey, out _));
        var wire = CreateWire(invalidIndex);
        Assert.False(TerrariaNpcUpdateEncoder.TryEncode(in wire, out _));
        wire = CreateWire(key) with { NpcType = VanillaNpcIds.MoonLordHead.Value, NpcNetId = 396 };
        Assert.False(TerrariaNpcUpdateEncoder.TryEncode(in wire, out _));
    }

    private static NpcStateUpdate CreateNpc(float key) => new(
        VanillaNpcIds.MoonLordLeechBlob.Value, 401, 100, 200, 0, 0, 0, new NpcAiState(0, key, 0, 0),
        NpcSimulationState.Initial with { Life = 400, LifeMax = 400 });

    private static TerrariaNpcUpdateState CreateWire(float key) => new(
        0, 1, 401, 100, 200, 0, 0, 0, 1, 1, 1, 0, key, 0, 0, 401, 400, 400, true);

    private sealed class FixedStepper(NpcStateUpdate update) : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = update; return true; }
    }

    private static JsonElement[] ReadCases()
    {
        using Stream resource = typeof(MoonLeechAnchorKeyTests).Assembly.GetManifestResourceStream("MoonLeechAnchorKey1458")!;
        using var bytes = new MemoryStream(); resource.CopyTo(bytes);
        Assert.Equal("a92ff556dfa67d4534132a842ee7cc211de3f428b8f45b7b4a2197b4547bb3dc",
            Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }
}
