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

public sealed class RuntimeNpcServerStrikeWire1458Tests
{
    public static IEnumerable<object[]> OriginalCases()
    {
        using var stream = typeof(RuntimeNpcServerStrikeWire1458Tests).Assembly.GetManifestResourceStream("NonclientStrike1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray())
        {
            if (row.GetProperty("scenario").GetString() != "active" || row.GetProperty("pendingSpawn").GetBoolean() ||
                row.GetProperty("outer").GetBoolean() || row.GetProperty("fromNet").GetBoolean() ||
                row.GetProperty("damage").GetInt32() <= 0) continue;
            bool critical = row.GetProperty("crit").GetBoolean();
            if (critical ? row.GetProperty("direction").GetInt32() == 0 : row.GetProperty("method").GetString() != "noInteraction") continue;
            yield return [row.Clone(), false];
            yield return [row.Clone(), true];
        }
    }

    // Independent original NPC.StrikeNPC/StrikeNPCNoInteraction -> real SendData socket bytes. The raw source
    // pending-spawn, immortal and caller eligibility branches remain outside this already-published actor slice.
    [Theory]
    [MemberData(nameof(OriginalCases))]
    public void Accepted_server_strike_broadcasts_original_raw_damage_before_final_npc_state(JsonElement row, bool projectile)
    {
        var f = new Fixture(row.GetProperty("generation").GetByte());
        int damage = row.GetProperty("damage").GetInt32(), direction = row.GetProperty("direction").GetInt32();
        bool critical = row.GetProperty("crit").GetBoolean();
        bool killed = row.GetProperty("life").GetInt32() == 0;
        if (projectile)
        {
            var shot = new ProjectileSnapshot(new(0, new(1)), new(1), VanillaProjectileIds.WoodenArrowFriendly,
                0, 100, 100, 3, 0, default, 0, 15, 6f, 15);
            Assert.Equal(killed ? RuntimeProjectileNpcDamageResult.Killed : RuntimeProjectileNpcDamageResult.Committed,
                f.Pipeline.TryStrikeProjectile(in shot, f.Target.Handle, direction, damage, critical: critical));
        }
        else if (critical)
            Assert.Equal(killed ? RuntimeProjectileNpcDamageResult.Killed : RuntimeProjectileNpcDamageResult.Committed,
                f.Pipeline.TryStrikeServerPlayerMelee(f.Player, f.Target.Handle, damage, 0, true, 6f, direction));
        else
            Assert.Equal(killed ? RuntimeTownNpcMeleeDamageResult1458.Killed : RuntimeTownNpcMeleeDamageResult1458.Committed,
                f.Pipeline.TryStrikeEnvironment(f.Target.Handle, damage, 6f, direction));

        byte[][] actual = Drain(f.First), peer = Drain(f.Second);
        byte[] expected = Convert.FromHexString(row.GetProperty("frames").EnumerateArray().Single().GetString()!);
        Assert.Equal(expected, actual[0]);
        Assert.Equal(28, actual[0][2]); Assert.Equal(23, actual[^1][2]);
        Assert.Single(actual, x => x[2] == 28); Assert.Single(actual, x => x[2] == 23);
        // Source death achievement97 is addressed only to the credited player; strike/update and bestiary
        // events are broadcasts. A lethal player-owned strike must retain that recipient distinction.
        byte[][] broadcast = actual.Where(x => x[2] != 97).ToArray();
        Assert.Equal(broadcast.Length, peer.Length);
        for (int i = 0; i < broadcast.Length; i++) Assert.Equal(broadcast[i], peer[i]);
        Assert.Equal(killed ? 1 : 0, actual.Count(x => x[2] == 97));
        if (!killed)
        {
            Assert.True(f.Npcs.TryGet(f.Target.Handle, out var current));
            Assert.Equal(row.GetProperty("life").GetInt32(), current.Simulation.Life);
            Assert.Equal(row.GetProperty("justHit").GetBoolean(), current.Simulation.JustHit);
        }
        else Assert.False(f.Npcs.TryGet(f.Target.Handle, out _));
    }

    [Fact]
    public void Zero_damage_town_strike_keeps_raw_zero_wire_and_minimum_hp_damage()
    {
        var f = new Fixture(1);
        Assert.True(f.Npcs.TrySpawnVanilla(new(207, 207, 100, 100, 0, 0, 255, default, NpcSimulationState.Initial), out var attacker));
        Drain(f.First); Drain(f.Second);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Committed,
            f.Pipeline.TryStrike(attacker.Handle, f.Target.Handle, 0, 6f, 1));
        var frames = Drain(f.First);
        Assert.Equal(Convert.FromHexString("0D001C000100000000C0400200"), frames[0]);
        Assert.Equal(new byte[] { 28, 23 }, frames.Select(x => x[2]).ToArray());
        Assert.True(f.Npcs.TryGet(f.Target.Handle, out var current)); Assert.Equal(int.MaxValue - 1, current.Simulation.Life);
    }

    [Fact]
    public void Rejected_or_stale_strike_does_not_publish_and_cannot_hit_reused_slot()
    {
        var f = new Fixture(1);
        var invulnerable = Update(f.Target) with { Simulation = f.Target.Simulation with { DontTakeDamage = true } };
        Assert.True(f.Npcs.TryUpdate(f.Target.Handle, in invulnerable, out var before)); Drain(f.First); Drain(f.Second);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, f.Pipeline.TryStrikeEnvironment(before.Handle, 15));
        Assert.Empty(Drain(f.First)); Assert.Empty(Drain(f.Second));
        Assert.True(f.Npcs.TryGet(before.Handle, out var retained)); Assert.Equal(before, retained);
        Assert.True(f.Npcs.TryDespawn(before.Handle));
        Assert.True(f.Npcs.TrySpawn(0, Update(f.Target), out var replacement)); Drain(f.First); Drain(f.Second);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, f.Pipeline.TryStrikeEnvironment(before.Handle, 15));
        Assert.Empty(Drain(f.First)); Assert.True(f.Npcs.TryGet(replacement.Handle, out retained)); Assert.Equal(replacement, retained);
    }

    [Fact]
    public void Rejected_lethal_item_claim_is_silent_before_damage_commit()
    {
        var f = new Fixture(1);
        Assert.True(f.Npcs.TryDespawn(f.Target.Handle));
        Assert.True(f.Npcs.TrySpawnVanilla(new(4, 4, 100, 100, 0, 0, 0, default, NpcSimulationState.Initial), out var eye));
        for (int i = 0; i < 400; i++) Assert.True(f.Items.TryReserveDropSlot(out _));
        Drain(f.First); Drain(f.Second); var baseline = f.Random.Clone();
        Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected,
            f.Pipeline.TryStrikeServerPlayerMelee(f.Player, eye.Handle, 100000, 0, false, 6f, 1));
        Assert.True(f.Npcs.TryGet(eye.Handle, out var retained)); Assert.Equal(eye, retained);
        Assert.True(f.Random.HasSameState(baseline)); Assert.Empty(Drain(f.First)); Assert.Empty(Drain(f.Second));
    }

    private static NpcStateUpdate Update(in NpcSnapshot npc) => new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY,
        npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);
    private sealed class Fixture : IRuntimePlayerSlotSnapshotLookup
    {
        public RuntimeNpcStore Npcs { get; }
        public RuntimeNpcNetworkCombatPipeline Pipeline { get; }
        public RuntimeWorldItemStore Items { get; } = new();
        public VanillaUnifiedRandom1458 Random { get; } = new(1458);
        public NpcSnapshot Target { get; }
        public PlayerHandle Player { get; } = new(new(0), new(1));
        public TerrariaConnectionOutboundQueue First { get; }
        public TerrariaConnectionOutboundQueue Second { get; }
        public Fixture(byte generation)
        {
            var replication = new RuntimeNpcReplicationRegistry(); Npcs = new(commitSink: replication);
            NpcSnapshot npc = default;
            var input = new NpcStateUpdate(3, 3, 100, 100, 0, 0, 0, default,
                NpcSimulationState.Initial with { Life = int.MaxValue, LifeMax = int.MaxValue, DefenseOverride = 15 });
            for (int i = 0; i < generation; i++)
            {
                Assert.True(Npcs.TrySpawn(0, in input, out npc));
                if (i + 1 < generation) Assert.True(Npcs.TryDespawn(npc.Handle));
            }
            Target = npc;
            First = Endpoint(replication, 9300, 0); Second = Endpoint(replication, 9301, 1); Drain(First); Drain(Second);
            Pipeline = new(Npcs, Items, this, new PlayerAuthority(null, null), static () => 0, replication,
                new(Items), null, new RuntimeWorldClock(0, true, 0, 0, 1), new(), false, false,
                lootRandom: Random);
        }
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot player)
        {
            player = default(PlayerStateSnapshot) with { Player = Player, Revision = new(1), PositionX = 100, PositionY = 100,
                HasHealth = true, Life = 400, MaxLife = 400 };
            return slot == Player.Slot;
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
    private static byte[][] Drain(TerrariaConnectionOutboundQueue queue)
    {
        var result = new List<byte[]>();
        var owned = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
        while (owned.TryRead(out OutboundFrame frame)) result.Add(frame.Bytes.ToArray());
        return result.ToArray();
    }
}
