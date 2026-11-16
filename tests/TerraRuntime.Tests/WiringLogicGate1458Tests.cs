using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WiringLogicGate1458Tests
{
    public static IEnumerable<object[]> OriginalRows()
    {
        using var resource = typeof(WiringLogicGate1458Tests).Assembly.GetManifestResourceStream("WiringLogicGate1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
            foreach (bool composed in new[] { false, true }) yield return [row.GetRawText(), composed];
    }

    [Theory, MemberData(nameof(OriginalRows))]
    public void Original_logic_columns_waves_feedback_entropy_and_trap_births_match(string observation, bool composed)
    {
        using var document = JsonDocument.Parse(observation); var row = document.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var tiles = new WorldTileStore(new(80, 100));
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var projectiles = new RuntimeProjectileStore(capacity: 1001);
        var state = new ServerRuntimeState(worldTiles: tiles, projectiles: projectiles,
            worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random), naturalSpawnRandom: new SystemVanillaNpcRandom(random));
        const BindingFlags access = BindingFlags.Instance | BindingFlags.NonPublic;
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", access)!.GetValue(state)!;
        var authority = graph.WorldTileAuthority;
        var circuitField = typeof(WorldTileAuthority).GetField("wiring", access)!;
        var circuit = (WorldWiringCircuit1458)circuitField.GetValue(authority)!;
        var frames = new List<string>();
        if (!composed)
        {
            void Publish(int x, int y, byte width, byte height)
            {
                Assert.True(TerrariaTileSquareCodec.TryEncode(tiles, x, y, width, height, VanillaTileChangeType1458.None, out var frame));
                frames.Add(Convert.ToHexString(frame));
            }
            var trap = typeof(WorldTileAuthority).GetMethod("TryFireWiringTrap", access)!;
            var devices = new WorldWiringDeviceExecutor1458(tiles, publishTileSquare: Publish,
                fireTrap: (x, y) => (bool)trap.Invoke(authority, [x, y])!);
            circuit = new(tiles, devices, Publish, random.Next, (x, y) =>
            {
                VanillaPoofOfSmoke1458.ConsumeDedicatedSourceOffers(new SystemVanillaNpcRandom(random));
                Assert.True(TerrariaPoofOfSmokeCodec1458.TryEncode(x, y, out var frame));
                frames.Add(Convert.ToHexString(frame));
            });
            circuitField.SetValue(authority, circuit);
        }
        var points = row.GetProperty("points").EnumerateArray().ToArray();
        foreach (var entry in points[0].GetProperty("before").GetProperty("cells").EnumerateArray())
        {
            var tile = Decode(entry.GetProperty("tile"));
            tiles.Set(entry.GetProperty("x").GetInt32(), entry.GetProperty("y").GetInt32(), in tile);
        }
        foreach (var point in points)
        {
            frames.Clear();
            string kind = row.GetProperty("kind").GetString()!;
            Assert.True(kind is "empty" or "repair" ? circuit.PokeLogicGate(30, kind == "empty" ? 30 : 29) :
                circuit.HitSwitch(row.GetProperty("originX").GetInt32(), row.GetProperty("originY").GetInt32()));
            Assert.False(circuit.LastLogicBudgetExceeded);
            var after = point.GetProperty("after");
            foreach (var entry in after.GetProperty("cells").EnumerateArray())
            {
                var expected = Decode(entry.GetProperty("tile"));
                Assert.Equal(expected, tiles.Get(entry.GetProperty("x").GetInt32(), entry.GetProperty("y").GetInt32()));
            }
            int relevant = 0;
            for (int x = 0; x < 80; x++) for (int y = 0; y < 100; y++)
            {
                var tile = tiles.Get(x, y);
                if (tile.IsActive || (tile.Flags & WorldTileFlagMasks.Wires) != 0) relevant++;
            }
            Assert.Equal(after.GetProperty("cells").GetArrayLength(), relevant);
            var rng = after.GetProperty("random");
            Assert.Equal(rng.GetProperty("cursor").GetInt32(), Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", access)!.GetValue(random)));
            Assert.Equal(rng.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()),
                (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", access)!.GetValue(random)!);
            Assert.Empty(after.GetProperty("drops").EnumerateArray());
            Assert.Equal(after.GetProperty("shots").GetArrayLength(), projectiles.ActiveCount);
            foreach (var shot in after.GetProperty("shots").EnumerateArray())
            {
                Assert.True(projectiles.TryGetActive(shot.GetProperty("slot").GetUInt16(), out var actual));
                Assert.Equal(shot.GetProperty("type").GetInt32(), actual.Type.Value);
                Assert.Equal(shot.GetProperty("owner").GetInt32(), actual.Spawner);
                Assert.Equal(shot.GetProperty("x").GetSingle(), actual.PositionX); Assert.Equal(shot.GetProperty("y").GetSingle(), actual.PositionY);
                Assert.Equal(shot.GetProperty("vx").GetSingle(), actual.VelocityX); Assert.Equal(shot.GetProperty("vy").GetSingle(), actual.VelocityY);
                Assert.Equal(shot.GetProperty("damage").GetInt32(), actual.Damage); Assert.Equal(shot.GetProperty("knockBack").GetSingle(), actual.KnockBack);
                Assert.True(projectiles.TryGetLifecycle(actual.Handle, out var life)); Assert.Equal(shot.GetProperty("timeLeft").GetInt32(), life.TimeLeft);
            }
            // Packet27 uses the separately qualified projectile publication owner; compare
            // its physical birth above, and compare original tile/smoke bytes here.
            if (!composed) Assert.Equal(point.GetProperty("frames").EnumerateArray().Select(x => x.GetString()!)
                .Where(x => Convert.FromHexString(x)[2] != 27), frames);
        }
    }

    [Fact]
    public void Logic_work_ceiling_bounds_retention_and_clears_for_the_next_pulse()
    {
        var tiles = new WorldTileStore(new(600, 128)); var random = new VanillaUnifiedRandom1458(1458);
        for (int x = 2; x < 598; x++) tiles.Set(x, 2, new WorldTile { Flags = WorldTileFlags.WireRed });
        tiles.Set(2, 2, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        for (int x = 3; x < 597; x++)
        {
            tiles.Set(x, 118, new WorldTile { Type = 420, FrameY = 18, Flags = WorldTileFlags.Active });
            for (int y = 3; y < 118; y++) tiles.Set(x, y, new WorldTile { Type = 419, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        }
        var before = random.Clone();
        var circuit = new WorldWiringCircuit1458(tiles, new(tiles), nextLogicSample: random.Next, publishLogicSmoke: (_, _) => throw new InvalidOperationException());
        Assert.True(circuit.HitSwitch(2, 2)); Assert.True(circuit.LastLogicBudgetExceeded);
        Assert.Equal(0, tiles.Get(300, 118).FrameX); Assert.True(before.HasSameState(random));
        Assert.True(circuit.TripWire(0, 0, 1, 1)); Assert.False(circuit.LastLogicBudgetExceeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Absent_random_capability_or_unqualified_framing_cannot_toggle_a_logic_lamp(bool unqualifiedFrame)
    {
        var tiles = new WorldTileStore(new(80, 100)); var random = new VanillaUnifiedRandom1458(1458);
        PutLogic(tiles, false);
        if (unqualifiedFrame) tiles.Set(19, 18, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var circuit = new WorldWiringCircuit1458(tiles, new(tiles), nextLogicSample: unqualifiedFrame ? random.Next : null,
            publishLogicSmoke: (_, _) => throw new InvalidOperationException());
        var before = random.Clone(); Assert.True(circuit.HitSwitch(10, 18));
        Assert.Equal(0, tiles.Get(20, 18).FrameX); Assert.Equal(0, tiles.Get(22, 20).FrameX);
        Assert.True(before.HasSameState(random));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publication_observers_cannot_reenter_or_authorize_a_replaced_gate(bool replace)
    {
        var tiles = new WorldTileStore(new(80, 100)); var random = new VanillaUnifiedRandom1458(1458); PutLogic(tiles, true);
        WorldWiringCircuit1458 circuit = null!; int callbacks = 0;
        void Publish(int x, int y, byte width, byte height)
        {
            Assert.False(circuit.HitSwitch(10, 18)); Assert.False(circuit.PokeLogicGate(20, 18));
            Assert.False(circuit.TripWire(20, 20, 1, 1));
            if (x == 20 && y == 20) { callbacks++; if (replace) tiles.Set(x, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed }); }
        }
        circuit = new(tiles, new(tiles, publishTileSquare: Publish), Publish, random.Next,
            (_, _) => VanillaPoofOfSmoke1458.ConsumeDedicatedSourceOffers(new SystemVanillaNpcRandom(random)));
        var before = random.Clone(); Assert.True(circuit.HitSwitch(10, 18)); Assert.Equal(1, callbacks);
        Assert.Equal(replace ? 0 : 54, tiles.Get(22, 20).FrameX);
        Assert.Equal(replace, before.HasSameState(random));
    }

    private static void PutLogic(WorldTileStore tiles, bool faulty)
    {
        for (int x = 10; x <= 20; x++) tiles.Set(x, 18, new WorldTile { Flags = WorldTileFlags.WireBlue });
        tiles.Set(10, 18, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireBlue });
        tiles.Set(20, 18, new WorldTile { Type = 419, FrameX = (short)(faulty ? 36 : 0), Flags = WorldTileFlags.Active | WorldTileFlags.WireBlue });
        tiles.Set(20, 19, new WorldTile { Type = 419, FrameX = 18, Flags = WorldTileFlags.Active });
        tiles.Set(20, 20, new WorldTile { Type = 420, FrameY = 18, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        tiles.Set(21, 20, new WorldTile { Flags = WorldTileFlags.WireRed });
        tiles.Set(22, 20, new WorldTile { Type = 149, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
    }

    internal static WorldTile Decode(JsonElement image)
    {
        WorldTileFlags flags = default;
        if (image.GetProperty("active").GetBoolean()) flags |= WorldTileFlags.Active;
        if (image.GetProperty("actuator").GetBoolean()) flags |= WorldTileFlags.Actuator;
        if (image.GetProperty("inactive").GetBoolean()) flags |= WorldTileFlags.Inactive;
        if (image.GetProperty("red").GetBoolean()) flags |= WorldTileFlags.WireRed;
        if (image.GetProperty("blue").GetBoolean()) flags |= WorldTileFlags.WireBlue;
        if (image.GetProperty("green").GetBoolean()) flags |= WorldTileFlags.WireGreen;
        if (image.GetProperty("yellow").GetBoolean()) flags |= WorldTileFlags.WireYellow;
        return new() { Type = image.GetProperty("type").GetUInt16(), FrameX = image.GetProperty("frameX").GetInt16(),
            FrameY = image.GetProperty("frameY").GetInt16(), Flags = flags };
    }
}
