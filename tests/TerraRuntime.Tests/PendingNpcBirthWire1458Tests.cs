using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class PendingNpcBirthWire1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string name in new[] { "BeePendingWire1458", "BlueSlimePendingWire1458" })
        {
            using var stream = typeof(PendingNpcBirthWire1458Tests).Assembly.GetManifestResourceStream(name)!;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var json = JsonDocument.Parse(gzip);
            foreach (var row in json.RootElement.EnumerateArray())
                // An inactive original slot carrying raw StrikeNPC(255) credit is not an owned player identity.
                if (!row.TryGetProperty("playerActive", out var active) || active.GetBoolean())
                    yield return [row.Clone()];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Genuine_source_pending_birth_strike_join_and_replacement_wire(JsonElement row)
    {
        var fixture = new Fixture(row);
        var action = row.GetProperty("action").GetString();
        byte[][] sourceFrames = row.GetProperty("frames").EnumerateArray()
            .Select(value => Convert.FromHexString(value.GetString()!)).ToArray();
        byte[][] expected = sourceFrames.Where(frame => frame[2] is 23 or 28).ToArray();
        if (action == "join")
        {
            var newcomer = Endpoint(fixture.Replication, 9302, 2);
            var baseline = Drain(newcomer).Where(frame => frame[2] == 23).ToArray();
            Assert.Equal(expected, baseline);
            Assert.True(fixture.Npcs.HasPendingBirth(fixture.Target.Handle));
            Assert.Empty(Drain(fixture.First));
            Assert.True(fixture.Npcs.TryPublishPendingBirth(fixture.Target.Handle));
            Assert.Equal(expected, Drain(fixture.First));
            return;
        }
        if (action == "replace")
        {
            var input = Update(fixture.Target);
            Assert.True(fixture.Npcs.TrySpawnVanillaPending(in input, out var replacement));
            Assert.Equal(expected, Drain(fixture.First));
            Assert.NotEqual(fixture.Target.Handle, replacement.Handle);
            Assert.False(fixture.Npcs.TryPublishPendingBirth(fixture.Target.Handle));
            Assert.True(fixture.Npcs.HasPendingBirth(replacement.Handle));
            var newcomer = Endpoint(fixture.Replication, 9302, 2);
            var baseline = Assert.Single(Drain(newcomer), frame => frame[2] == 23);
            Assert.Equal(RuntimeNpcPacketProjection.ToProtocolGeneration(replacement.Handle.Generation), baseline[4]);
            Assert.True(fixture.Npcs.HasPendingBirth(replacement.Handle));
            return;
        }

        int suppliedDamage = row.GetProperty("damage").GetInt32();
        if (action == "client")
        {
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9300), fixture.Player);
            var wire = new TerrariaNpcDamageState(0, row.GetProperty("generation").GetByte(),
                (short)Math.Min(suppliedDamage, short.MaxValue), 0f, 1, 0);
            Assert.NotEqual(RuntimeNpcNetworkDamageResult.Rejected, fixture.Pipeline.TryApply(connection, in wire));
        }
        else
        {
            Assert.NotEqual(RuntimeTownNpcMeleeDamageResult1458.Rejected,
                fixture.Pipeline.TryStrikeEnvironment(fixture.Target.Handle, suppliedDamage, 0f, 0));
        }
        byte[][] actual = Drain(fixture.Second);
        byte[][] npcFrames = actual.Where(frame => frame[2] is 23 or 28).ToArray();
        // Source's final lethal 23 is an explicit harness send, representing the later outer pass;
        // the runtime owns finalization now. Compare its bytes, without calling this a full UpdateNPC oracle.
        Assert.Equal(expected[0], npcFrames[0]);
        Assert.Equal(expected[1], npcFrames[1]);
        if (!row.GetProperty("active").GetBoolean())
            Assert.Equal(expected[^1], npcFrames[^1]);
        Assert.Single(npcFrames, frame => frame[2] == 28);
        byte[] expectedOrder = sourceFrames.Where(frame => frame[2] is 21 or 22 or 88 or 23 or 28)
            .Select(frame => frame[2]).ToArray();
        byte[] actualOrder = actual.Where(frame => frame[2] is 21 or 22 or 88 or 23 or 28)
            .Select(frame => frame[2]).ToArray();
        if (!row.GetProperty("active").GetBoolean())
            Assert.Equal(expectedOrder, actualOrder);
        Assert.Equal(row.GetProperty("next").GetInt32(), fixture.Random.Next());
        if (action == "client" && sourceFrames.Any(frame => frame[2] == 21))
            Assert.True(Array.FindLastIndex(actual, frame => frame[2] == 21) <
                Array.FindIndex(actual, frame => frame[2] == 23));
        if (action == "nonclient" && sourceFrames.Any(frame => frame[2] == 21))
            Assert.True(Array.FindIndex(actual, frame => frame[2] == 28) <
                Array.FindIndex(actual, frame => frame[2] == 21));
    }

    private static NpcStateUpdate Update(in NpcSnapshot npc) => new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY,
        npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);
    private sealed class Fixture : IRuntimePlayerSlotSnapshotLookup
    {
        public RuntimeNpcStore Npcs { get; }
        public RuntimeNpcReplicationRegistry Replication { get; }
        private bool PlayerActive { get; }
        public RuntimeNpcNetworkCombatPipeline Pipeline { get; }
        public RuntimeWorldItemStore Items { get; }
        public VanillaUnifiedRandom1458 Random { get; } = new(1458);
        public NpcSnapshot Target { get; }
        public PlayerHandle Player { get; } = new(new(0), new(1));
        public TerrariaConnectionOutboundQueue First { get; }
        public TerrariaConnectionOutboundQueue Second { get; }
        public Fixture(JsonElement row)
        {
            Replication = new RuntimeNpcReplicationRegistry();
            var replication = Replication;
            var itemReplication = new RuntimeWorldItemReplicationRegistry();
            Items = new(itemReplication);
            Npcs = new(1, replication);
            NpcSnapshot npc = default;
            byte generation = row.GetProperty("generation").GetByte();
            int type = row.GetProperty("type").GetInt32();
            PlayerActive = !row.TryGetProperty("playerActive", out var active) || active.GetBoolean();
            int held = row.TryGetProperty("held", out var heldValue) ? heldValue.GetInt32() : 0;
            float value = row.TryGetProperty("value", out var money) ? money.GetSingle() : 0f;
            var input = new NpcStateUpdate(type, checked((short)type), 639, 440, 0, 0, 255,
                new(0, held, 0, 0), NpcSimulationState.Initial with
                { MoneyValue = value, CanBeReplacedByOtherNpcs = true, DirectionY = 1 });
            for (int i = 0; i < generation; i++)
            {
                Assert.True(Npcs.TrySpawn(0, in input, out npc));
                if (i + 1 < generation) Assert.True(Npcs.TryDespawn(npc.Handle));
            }
            Target = npc;
            Assert.True(Npcs.TryRetainPendingBirth(in npc));
            First = Endpoint(replication, 9300, 0); Second = Endpoint(replication, 9301, 1); Drain(First); Drain(Second);
            var authority = new PlayerAuthority(null, null);
            var slots = new PlayerSlotPool(2);
            Assert.True(slots.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9300), session.Handle);
            Assert.True(authority.TryApply(new PlayerSpawnRuntimeCommand(connection, session,
                new PlayerSpawnCommitRequest(session.Slot, 20, 20, 0, 0, 0, 0, 0))));
            Assert.True(authority.TryApply(new PlayerHealthRuntimeCommand(connection,
                new PlayerHealthCommitRequest(session.Slot, 100, 100))));
            Assert.True(authority.TryApply(new PlayerMovementRuntimeCommand(connection,
                new PlayerMovementCommitRequest(session.Slot, 0, 0, 0, 0, 0, 639, 439, false, 0, 0,
                    false, 0, false, 0, 0, 0, 0, false, 0, 0))));
            for (short slot = 0; slot < 59; slot++)
                Assert.True(authority.TryApply(new PlayerEquipmentRuntimeCommand(connection,
                    new PlayerEquipmentCommitRequest(session.Slot, slot, 0, 0, 0, 0))));
            // An observed non-weapon item selects the existing explicit legacy packet-28 compatibility path.
            Assert.True(authority.TryApply(new PlayerEquipmentRuntimeCommand(connection,
                new PlayerEquipmentCommitRequest(session.Slot, 0, 1, 0, 2, 0))));
            Items.AttachOwnerFactsProvider(new RuntimeWorldItemOwnerFactsProvider1458(authority, null, false, false));
            MarkItemEndpoint(itemReplication, First, 9300, 0);
            MarkItemEndpoint(itemReplication, Second, 9301, 1);
            Pipeline = new(Npcs, Items, this, authority, static () => 0, replication,
                new(Items), itemReplication, new RuntimeWorldClock(0, true, 0, 0, 1), new(), false, false,
                lootRandom: Random);
            // Original StrikeNPC(...playerIndex:255) attributes the hit to eligible active nearby players.
            // Retain the equivalent known slot before the environment/no-interaction transport test.
            Assert.True(Pipeline.Interactions.TryMark(Target.Handle, Player));
        }
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot player)
        {
            player = default(PlayerStateSnapshot) with { Player = Player, Revision = new(1), PositionX = 639, PositionY = 439,
                HasHealth = true, Life = 100, MaxLife = 100 };
            return PlayerActive && slot == Player.Slot;
        }
    }
    private static TerrariaConnectionOutboundQueue Endpoint(RuntimeNpcReplicationRegistry registry, long id, byte slot)
    {
        var source = GameCommandSourceId.FromConnection(id);
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(128, 65536, 1024));
        Assert.True(registry.TryRegister(source, queue));
        var connection = new ConnectionHandle(source, new(new(slot), new(1)));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        registry.PlayerSpawned(connection, in spawn); return queue;
    }
    private static void MarkItemEndpoint(RuntimeWorldItemReplicationRegistry registry,
        TerrariaConnectionOutboundQueue queue, long id, byte slot)
    {
        var source = GameCommandSourceId.FromConnection(id);
        Assert.True(registry.TryRegister(source, queue));
        var connection = new ConnectionHandle(source, new(new(slot), new(1)));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        registry.PlayerSpawned(connection, in spawn);
    }

    private static byte[][] Drain(TerrariaConnectionOutboundQueue queue)
    {
        var result = new List<byte[]>();
        var owned = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
        while (owned.TryRead(out OutboundFrame frame)) result.Add(frame.Bytes.ToArray());
        return result.ToArray();
    }
}
