using System.Security.Cryptography;
using System.Reflection;
using System.Buffers.Binary;
using TerraRuntime.Network;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MoonLeechClotTests
{
    private static readonly JsonElement[] Rows = ReadCases();
    public static TheoryData<int, bool> Cases
    {
        get
        {
            var cases = new TheoryData<int, bool>();
            for (int i = 0; i < 80; i++) { cases.Add(i, false); cases.Add(i, true); }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Original_AI82_motion_healing_and_deactivation_match_committed_runtime(int index, bool worldMotion)
    {
        var row = Rows[index];
        var replication = new RuntimeNpcReplicationRegistry();
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 32768, 1024));
        var reader = Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue));
        var source = GameCommandSourceId.FromConnection(1);
        var connection = new ConnectionHandle(source, new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
        Assert.True(replication.TryRegister(source, queue));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 100, 200, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(connection, in spawn);
        var store = new RuntimeNpcStore(8, replication);
        int[] types = [396, 398, 397, 397], maxima = [45000, 50000, 25000, 25000];
        var deficits = row.GetProperty("deficits");
        for (byte i = 0; i < 4; i++)
        {
            if (i == 0 && !row.GetProperty("activeHead").GetBoolean()) continue;
            var state = new NpcStateUpdate(types[i], (short)types[i], 1000, 1000, 0, 0, 0,
                new NpcAiState(0, 0, i == 3 ? 1 : 0, 1),
                NpcSimulationState.Initial with { Life = maxima[i] - deficits[i].GetInt32(), LifeMax = maxima[i] });
            Assert.True(store.TrySpawn(i, in state, out _));
        }
        float key = BitConverter.UInt32BitsToSingle(0x000400ff);
        var clotState = new NpcStateUpdate(401, 401, 300, 400, 2, -3, 0,
            new NpcAiState(1, key, row.GetProperty("age").GetInt32(), 0),
            NpcSimulationState.Initial with { Life = 400, LifeMax = 400 });
        Assert.True(store.TrySpawn(4, in clotState, out var clot));
        var projectiles = new RuntimeProjectileStore(2);
        var projectileState = new ProjectileStateUpdate(VanillaProjectileIds.MoonLeech, 255, 1200, 1000, 0, 0,
            default, 0, 0, 0, 0);
        Assert.True(projectiles.TrySpawn(0, in projectileState, out var anchor));
        var identities = new RuntimeProjectileWireIdentityRegistry(2);
        var wire = new TerrariaProjectileKeyState(255, 0, 1);
        Assert.True(identities.TryBind(in wire, anchor.Handle));
        var vanilla = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        vanilla.SetProjectileAnchors(new RuntimeNpcProjectileAnchors(projectiles, identities));
        var stepper = new ClotOnlyStepper(vanilla);
        var recording = new RecordingStepper(worldMotion
            ? new VanillaNpcWorldMotionAiStepper(stepper, new WorldTileStore(new WorldDimensions(100, 100)))
            : stepper);
        while (reader.TryRead(out _)) { }
        new RuntimeNpcAiStateExecutor(store, healing: replication).Tick(recording);
        var actual = recording.Proposed;
        Assert.Equal(row.GetProperty("x").GetSingle() + (worldMotion ? row.GetProperty("vx").GetSingle() : 0), actual.PositionX);
        Assert.Equal(row.GetProperty("y").GetSingle() + (worldMotion ? row.GetProperty("vy").GetSingle() : 0), actual.PositionY);
        Assert.Equal(row.GetProperty("vx").GetSingle(), actual.VelocityX);
        Assert.Equal(row.GetProperty("vy").GetSingle(), actual.VelocityY);
        Assert.Equal(row.GetProperty("life").GetInt32(), actual.Simulation.Life);
        Assert.Equal(row.GetProperty("ai")[2].GetSingle(), actual.Ai.Ai2);
        Assert.Equal(row.GetProperty("active").GetBoolean(), store.TryGet(clot.Handle, out _));
        for (byte i = 0; i < 4; i++)
            if (store.TryGetActive(i, out var part))
                Assert.Equal(row.GetProperty("bossLife")[i].GetInt32(), part.Simulation.Life);
        var packets = new List<byte[]>();
        while (reader.TryRead(out var frame)) packets.Add(frame.Bytes.ToArray());
        if (!row.GetProperty("active").GetBoolean())
        {
            var heals = new List<int>();
            if (row.GetProperty("activeHead").GetBoolean())
                for (int i = 0; i < 4; i++)
                {
                    int amount = row.GetProperty("bossLife")[i].GetInt32() - (maxima[i] - deficits[i].GetInt32());
                    if (amount > 0) heals.Add(amount);
                }
            Assert.Equal(heals.Count + 1, packets.Count);
            for (int i = 0; i < heals.Count; i++)
            {
                Assert.Equal(81, packets[i][2]);
                Assert.Equal(heals[i], BinaryPrimitives.ReadInt32LittleEndian(packets[i].AsSpan(14)));
            }
            Assert.Equal(23, packets[^1][2]);
        }
    }

    private sealed class RecordingStepper(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public NpcStateUpdate Proposed { get; private set; }
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            bool success = inner.TryStepState(in npc, out next);
            if (success) Proposed = next;
            return success;
        }
    }

    private sealed class ClotOnlyStepper(VanillaNpcTargetingAiStepper vanilla)
        : INpcAiStateStepper, INpcAiPeerSnapshotConsumer, INpcAiStatePostCommitEffect
    {
        public NpcStateUpdate Proposed { get; private set; }
        public void SetNpcPeers(ReadOnlySpan<NpcSnapshot> peers) => vanilla.SetNpcPeers(peers);
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            if (npc.TypeIdentity != VanillaNpcIds.MoonLordLeechBlob) return false;
            bool success = vanilla.TryStepState(in npc, out next);
            Proposed = next;
            return success;
        }
        public bool DeactivatesAfterStep(in NpcSnapshot before, in NpcStateUpdate proposed) =>
            vanilla.DeactivatesAfterStep(in before, in proposed);
        public void ApplyCommittedEffect(in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations) =>
            vanilla.ApplyCommittedEffect(in before, in committed, mutations);
    }

    private static JsonElement[] ReadCases()
    {
        using Stream resource = typeof(MoonLeechClotTests).Assembly.GetManifestResourceStream("MoonLeechClot1458")!;
        using var bytes = new MemoryStream(); resource.CopyTo(bytes);
        Assert.Equal("d782fd9d563a84cebd66c993ccf3b311ead62a56ecd9745f8e5e8b1cde4bb713", Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }
}
