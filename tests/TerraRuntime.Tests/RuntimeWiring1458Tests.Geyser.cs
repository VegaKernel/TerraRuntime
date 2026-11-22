using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class RuntimeWiring1458Tests
{
    public static IEnumerable<object[]> OriginalGeyserRows()
    {
        foreach (string resource in new[] { "WiringGeyserLinux1458", "WiringGeyserNpcLinux1458" })
        {
            using var input = typeof(RuntimeWiring1458Tests).Assembly.GetManifestResourceStream(resource)!;
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var json = JsonDocument.Parse(gzip);
            foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
                yield return [row.GetRawText()];
        }
    }

    [Theory, MemberData(nameof(OriginalGeyserRows))]
    public void Geyser_contacts_warmup_and_dynamic_damage_match_original(string observation)
    {
        using var json = JsonDocument.Parse(observation);
        var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var tiles = new WorldTileStore(new(80, 100));
        Assert.True(tiles.TryAttachWorldSurface(15));
        foreach (var entry in row.GetProperty("first").EnumerateArray())
            tiles.Set(entry.GetProperty("x").GetInt32(), entry.GetProperty("y").GetInt32(), WiringLogicGate1458Tests.Decode(entry.GetProperty("tile")));
        if (row.GetProperty("scene").GetInt32() == 2)
        {
            int sign = row.GetProperty("style").GetInt32() < 2 ? -1 : 1;
            for (int x = 29; x <= 32; x++)
                for (int offset = 1; offset <= 3; offset++)
                    tiles.Set(x, 40 + sign * offset, new WorldTile { LiquidAmount = 255, LiquidKind = WorldLiquidKind.Water });
        }

        var random = new VanillaUnifiedRandom1458(1458);
        var projectileReplication = new RuntimeProjectileReplicationRegistry();
        var projectiles = new RuntimeProjectileStore(1001, projectileReplication);
        bool combat = row.TryGetProperty("targetBefore", out var targetBefore);
        var npcReplication = combat ? new RuntimeNpcReplicationRegistry() : null;
        var npcs = new RuntimeNpcStore(200, npcReplication);
        using var scene = new Scene(npcs: npcs, npcReplication: npcReplication, wiringRandom: random,
            worldTiles: tiles, projectiles: projectiles, projectileReplication: projectileReplication);
        var graph = Graph(scene);
        NpcHandle target = default;
        if (combat)
        {
            var initial = targetBefore[0];
            int type = initial.GetProperty("type").GetInt32();
            var simulation = NpcSimulationState.Initial with
            {
                Life = 1000, LifeMax = 1000, DefenseOverride = 0,
                HitboxOverride = new(initial.GetProperty("width").GetInt32(), initial.GetProperty("height").GetInt32()),
                MoneyValue = 0, ExtraMoneyValue = 0, Midas = false, Immortal = false, Friendly = false,
                KnockBackResist = initial.GetProperty("resist").GetSingle()
            };
            var request = new NpcStateUpdate(type, (short)type, initial.GetProperty("x").GetSingle(), initial.GetProperty("y").GetSingle(),
                0, 0, byte.MaxValue, default, simulation);
            Assert.True(npcs.TrySpawnVanilla(in request, out var spawned));
            target = spawned.Handle;
        }

        _ = GemBunnyWire1458Tests.Drain(scene.PeerOutbound);
        _ = GemBunnyWire1458Tests.Drain(scene.OwnerOutbound);
        random.CopyStateFrom(new VanillaUnifiedRandom1458(1458));
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", hidden)!.GetValue(graph.WorldTileAuthority)!;
        int contactX = 30 + row.GetProperty("part").GetInt32();
        int trigger = row.GetProperty("trigger").GetInt32();
        var points = row.GetProperty("points");
        int nextPoint = 0;
        int ticks = combat ? 4 : 263;
        for (int tick = 0; tick < ticks; tick++)
        {
            graph.WorldTileAuthority.TickMechanisms();
            if (tick == 0 || !combat && tick is 1 or 198 or 199 or 200 or 201)
            {
                if (trigger == 0)
                {
                    byte[] packet = [7, 0, 59, (byte)contactX, 0, 40, 0];
                    var frame = Decode(packet);
                    Assert.Equal(TerrariaFrameSinkResult.Continue, scene.Sink.OnFrame(in frame));
                    scene.Drain();
                }
                else if (trigger == 1)
                    Assert.True(circuit.HitSwitch(26, 40));
                else
                {
                    var before = default(PlayerStateSnapshot) with { Player = scene.Owner.Player,
                        PositionX = contactX * 16 + 1, PositionY = 40 * 16 + 11 - 42 };
                    graph.WorldTileAuthority.ApplyPlayerPressurePlate(before, before with { PositionY = before.PositionY + 2 });
                }
            }

            Assert.True(graph.Projectiles.TryTickState(combat ? graph.Npcs.ProjectileNpcCombat : null));
            var frames = GemBunnyWire1458Tests.Drain(scene.PeerOutbound);
            _ = GemBunnyWire1458Tests.Drain(scene.OwnerOutbound);
            var point = points[nextPoint];
            if (point.GetProperty("tick").GetInt32() != tick)
            {
                Assert.Empty(frames);
                continue;
            }
            nextPoint++;
            Assert.Equal(point.GetProperty("frames").EnumerateArray().Select(f => f.GetString()!), frames.Select(Convert.ToHexString));
            Assert.Equal(point.GetProperty("mechanisms").GetInt32(), circuit.RegisteredMechanismCount);
            CompareGeyserBodies(point.GetProperty("shots"), projectiles);
            var rng = point.GetProperty("random");
            Assert.Equal(rng.GetProperty("cursor").GetInt32(),
                Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", hidden)!.GetValue(random)));
            Assert.Equal(rng.GetProperty("state").EnumerateArray().Select(v => v.GetInt32()),
                (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", hidden)!.GetValue(random)!);
            if (combat)
            {
                Assert.True(npcs.TryGet(target, out var npc));
                var expected = point.GetProperty("npcs")[0];
                Assert.Equal(expected.GetProperty("life").GetInt32(), npc.Simulation.Life);
                Assert.Equal(expected.GetProperty("vx").GetSingle(), npc.VelocityX);
                Assert.Equal(expected.GetProperty("vy").GetSingle(), npc.VelocityY);
            }
        }
        Assert.Equal(points.GetArrayLength(), nextPoint);
    }

    private static void CompareGeyserBodies(JsonElement expected, RuntimeProjectileStore projectiles)
    {
        Assert.Equal(expected.GetArrayLength(), projectiles.ActiveCount);
        foreach (var body in expected.EnumerateArray())
        {
            Assert.True(projectiles.TryGetActive(body.GetProperty("slot").GetUInt16(), out var shot));
            Assert.True(projectiles.TryGetLifecycle(shot.Handle, out var life));
            Assert.Equal((ushort)VanillaWiringTrap1458.GeyserProjectileType, shot.Type.Value);
            Assert.Equal(body.GetProperty("owner").GetByte(), shot.Spawner);
            Assert.Equal(body.GetProperty("x").GetSingle(), shot.PositionX);
            Assert.Equal(body.GetProperty("y").GetSingle(), shot.PositionY);
            Assert.Equal(body.GetProperty("vx").GetSingle(), shot.VelocityX);
            Assert.Equal(body.GetProperty("vy").GetSingle(), shot.VelocityY);
            Assert.Equal(body.GetProperty("timeLeft").GetInt32(), life.TimeLeft);
            Assert.Equal(body.GetProperty("height").GetUInt16(), life.GeyserHeight);
            var ai = body.GetProperty("ai");
            var local = body.GetProperty("localAi");
            Assert.Equal(new ProjectileAiState(ai[0].GetSingle(), ai[1].GetSingle(), ai[2].GetSingle()), shot.Ai);
            Assert.Equal(new ProjectileLocalAiState(local[0].GetSingle(), local[1].GetSingle(), local[2].GetSingle()), life.LocalAi);
            Assert.Equal(body.GetProperty("wet").GetBoolean(), life.Liquid.Wet);
            Assert.Equal(body.GetProperty("wetCount").GetByte(), life.SourceWetCount);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Geyser_extended_body_routes_server_player_damage_through_the_existing_owner(bool obstructed)
    {
        var tiles = new WorldTileStore(new(80, 100));
        tiles.Set(30, 40, new WorldTile { Type = 443, Flags = WorldTileFlags.Active });
        tiles.Set(31, 40, new WorldTile { Type = 443, FrameX = 18, Flags = WorldTileFlags.Active });
        if (obstructed)
        {
            tiles.Set(30, 35, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            tiles.Set(31, 35, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        }
        var slots = new PlayerSlotPool(2);
        var identities = new ServerPlayerSlotRegistry(slots);
        var states = new ServerPlayerStateStore(identities, 2);
        var players = new ServerPlayerAuthority(states, identities, tiles);
        using var scene = new Scene(serverPlayers: players, playerSlots: slots, worldTiles: tiles,
            wiringRandom: new VanillaUnifiedRandom1458(1458));
        var target = players.Create(new ServerPlayerId("test:geyser"), 488, 470);
        Assert.True(target.IsCreated);
        Assert.True(states.TrySetVitals(target.Player, new(100, 100, 20, 20), out _));
        scene.Press(30, 40);
        var graph = Graph(scene);
        Assert.True(graph.Projectiles.TryTickState(graph.Npcs.ProjectileNpcCombat));
        Assert.True(players.TryGet(target.Player, out var after));
        Assert.Equal(obstructed, after.Life == 100);
    }

    [Fact]
    public void Unowned_geyser_cannot_advance_or_draw_from_the_source_stream()
    {
        var shots = new RuntimeProjectileStore(1);
        var random = new VanillaUnifiedRandom1458(1458);
        using var scene = new Scene(worldTiles: new(new(80, 100)), projectiles: shots, wiringRandom: random);
        var shot = new ProjectileStateUpdate(new(VanillaWiringTrap1458.GeyserProjectileType), byte.MaxValue, 481, 625,
            0, -8, default, 0, 20, 2, 0);
        Assert.True(shots.TrySpawn(0, in shot, out var initial));
        var before = random.Clone();
        Assert.True(Graph(scene).Projectiles.TryTickState());
        Assert.True(shots.TryGet(initial.Handle, out var after));
        Assert.Equal(initial, after);
        Assert.True(random.HasSameState(before));
    }
}
