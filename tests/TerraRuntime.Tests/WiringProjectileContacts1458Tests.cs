using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WiringProjectileContacts1458Tests
{
    public static IEnumerable<object[]> OriginalFlights()
    {
        using var input = typeof(WiringProjectileContacts1458Tests).Assembly.GetManifestResourceStream("WiringProjectileContactsLinux1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
            yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalFlights))]
    public void Dry_trap_projectiles_trigger_directional_pads_before_later_slots_update(string observation)
    {
        using var json = JsonDocument.Parse(observation);
        var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var tiles = new WorldTileStore(new(80, 100));
        Assert.True(tiles.TryAttachWorldSurface(15));
        var wire = WorldTileFlags.WireRed;
        if (row.GetProperty("colors").GetInt32() == 15)
            wire |= WorldTileFlags.WireBlue | WorldTileFlags.WireGreen | WorldTileFlags.WireYellow;
        for (int x = 30; x <= 33; x++) tiles.Set(x, 40, new WorldTile { Flags = wire });
        tiles.Set(30, 40, new WorldTile { Type = 442, Flags = wire | WorldTileFlags.Active,
            FrameX = checked((short)(22 * row.GetProperty("facing").GetInt32())) });
        tiles.Set(33, 40, new WorldTile { Type = 4, Flags = wire | WorldTileFlags.Active });
        if (row.GetProperty("birth").GetBoolean())
            tiles.Set(32, 40, new WorldTile { Type = 137, Flags = wire | WorldTileFlags.Active, FrameY = 36 });

        var random = new VanillaUnifiedRandom1458(1458);
        var tileReplication = new RuntimeTileManipulationReplicationRegistry();
        var shotReplication = new RuntimeProjectileReplicationRegistry();
        var shots = new RuntimeProjectileStore(1001, shotReplication);
        var queue = new TerrariaConnectionOutboundQueue(new(512, 262144, 8192));
        var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(1458220), new(new PlayerSlotId(1), new(1)));
        Assert.True(tileReplication.TryRegister(peer.Source, queue));
        Assert.True(shotReplication.TryRegister(peer.Source, queue));
        var request = new PlayerSpawnCommitRequest(peer.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        tileReplication.PlayerSpawned(peer, request);
        shotReplication.PlayerSpawned(peer, request);
        var state = new ServerRuntimeState(worldTiles: tiles, projectiles: shots, projectileReplication: shotReplication,
            worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random), tileManipulationReplication: tileReplication);
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", hidden)!.GetValue(state)!;
        var circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", hidden)!.GetValue(graph.WorldTileAuthority)!;

        var before = row.GetProperty("before");
        using (var birth = shots.CreateWorldSpawnPreparation())
        {
            var input = new ProjectileStateUpdate(new(before.GetProperty("type").GetUInt16()), byte.MaxValue,
                before.GetProperty("x").GetSingle(), before.GetProperty("y").GetSingle(),
                before.GetProperty("vx").GetSingle(), before.GetProperty("vy").GetSingle(), default, 0, 40, 2, 0);
            Assert.True(birth.TryStage(input, false, out var parent));
            Assert.Equal(0, parent.Handle.Slot);
            Assert.True(birth.TryAdoptUnpublished(out var publication));
            Assert.True(publication!.TryPublishNext());
        }
        _ = GemBunnyWire1458Tests.Drain(queue);
        random.CopyStateFrom(new VanillaUnifiedRandom1458(1458));
        foreach (var point in row.GetProperty("points").EnumerateArray())
        {
            graph.WorldTileAuthority.TickMechanisms();
            Assert.True(graph.Projectiles.TryTickState());
            Assert.Equal(WiringLogicGate1458Tests.Decode(point.GetProperty("torch")), tiles.Get(33, 40));
            Assert.Equal(point.GetProperty("mechanisms").GetInt32(), circuit.RegisteredMechanismCount);
            Assert.Equal(point.GetProperty("frames").EnumerateArray().Select(f => f.GetString()),
                GemBunnyWire1458Tests.Drain(queue).Select(Convert.ToHexString));
            var expectedShots = point.GetProperty("shots");
            Assert.Equal(expectedShots.GetArrayLength(), shots.ActiveCount);
            foreach (var expected in expectedShots.EnumerateArray())
            {
                Assert.True(shots.TryGetActive(expected.GetProperty("slot").GetUInt16(), out var shot));
                Assert.True(shots.TryGetLifecycle(shot.Handle, out var life));
                Assert.Equal(expected.GetProperty("type").GetUInt16(), shot.Type.Value);
                Assert.Equal(expected.GetProperty("owner").GetByte(), shot.Spawner);
                Assert.Equal(expected.GetProperty("damage").GetInt16(), shot.Damage);
                Assert.Equal(expected.GetProperty("originalDamage").GetInt16(), shot.OriginalDamage);
                Assert.Equal(expected.GetProperty("knockBack").GetSingle(), shot.KnockBack);
                Assert.Equal(expected.GetProperty("x").GetSingle(), shot.PositionX);
                Assert.Equal(expected.GetProperty("y").GetSingle(), shot.PositionY);
                Assert.Equal(expected.GetProperty("vx").GetSingle(), shot.VelocityX);
                Assert.Equal(expected.GetProperty("vy").GetSingle(), shot.VelocityY);
                Assert.Equal(expected.GetProperty("timeLeft").GetInt32(), life.TimeLeft);
                var ai = expected.GetProperty("ai");
                var local = expected.GetProperty("localAi");
                Assert.Equal(new ProjectileAiState(ai[0].GetSingle(), ai[1].GetSingle(), ai[2].GetSingle()), shot.Ai);
                Assert.Equal(new ProjectileLocalAiState(local[0].GetSingle(), local[1].GetSingle(), local[2].GetSingle()), life.LocalAi);
            }
            var rng = point.GetProperty("random");
            Assert.Equal(rng.GetProperty("cursor").GetInt32(),
                Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", hidden)!.GetValue(random)));
            Assert.Equal(rng.GetProperty("state").EnumerateArray().Select(v => v.GetInt32()),
                (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", hidden)!.GetValue(random)!);
        }
    }
}
