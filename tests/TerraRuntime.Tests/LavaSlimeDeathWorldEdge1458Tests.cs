using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class LavaSlimeDeathWorldEdge1458Tests
{
    private const int WorldSize = 128;

    public static IEnumerable<object[]> OriginalRows()
    {
        using var stream = typeof(LavaSlimeDeathWorldEdge1458Tests).Assembly
            .GetManifestResourceStream("LavaSlimeWorldEdge1458");
        using var compressed = new GZipStream(stream!, CompressionMode.Decompress);
        using var data = JsonDocument.Parse(compressed);
        Assert.Equal("4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034",
            data.RootElement.GetProperty("officialSha256").GetString());
        foreach (var row in data.RootElement.GetProperty("rows").EnumerateArray())
            yield return [row.GetProperty("terrain").GetString()!, row.GetProperty("lowTiles").GetBoolean(),
                row.GetProperty("seed").GetInt32(), row.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Lava_death_keeps_only_the_original_world_edge_scan_prefix(string terrain, bool lowTiles, int seed, string observation)
    {
        using var data = JsonDocument.Parse(observation);
        var row = data.RootElement;
        Assert.Equal(terrain, row.GetProperty("terrain").GetString());
        var tiles = new WorldTileStore(new(WorldSize, WorldSize));
        var replication = new RuntimeTileManipulationReplicationRegistry();
        var state = new ServerRuntimeState(worldTiles: tiles, tileManipulationReplication: replication);
        var composition = (ServerRuntimeComposition)typeof(ServerRuntimeState)
            .GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        var random = new VanillaUnifiedRandom1458(seed);
        // NPC.HitEffect offers these draws before its terrain branch on a dedicated server.
        for (int draw = 0; draw < 40; draw++) _ = random.Next(8);

        Assert.True(composition.WorldTileAuthority.TryPrepareLavaSlimeDeath(
            row.GetProperty("px").GetSingle(), row.GetProperty("py").GetSingle(), 47, 35,
            lowTiles, random, out var preparation));
        Assert.Equal(new WorldTile[WorldSize * WorldSize], tiles.Tiles.ToArray());
        Assert.Equal(0, tiles.LiquidUpdates.ActiveCount);
        Assert.Equal(0, replication.PendingLiquidUpdates);
        Assert.True(preparation!.TryAdoptUnpublished());

        var expectedTiles = new WorldTile[WorldSize * WorldSize];
        foreach (var change in row.GetProperty("changes").EnumerateArray())
        {
            int x = change.GetProperty("x").GetInt32();
            int y = change.GetProperty("y").GetInt32();
            var after = change.GetProperty("after");
            expectedTiles[tiles.GetUncheckedIndex(x, y)] = new WorldTile
            {
                LiquidAmount = after.GetProperty("liquid").GetByte(),
                LiquidKind = (WorldLiquidKind)((after.GetProperty("bTileHeader").GetByte() >> 5) & 3)
            };
        }
        Assert.Equal(expectedTiles, tiles.Tiles.ToArray());
        var expectedQueue = row.GetProperty("queued").EnumerateArray().Select(cell =>
            (tiles.GetUncheckedIndex(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32()),
                cell.GetProperty("delay").GetInt32(), cell.GetProperty("kill").GetInt32()));
        Assert.Equal(expectedQueue, tiles.LiquidUpdates.CaptureActiveSnapshot()
            .Select(cell => (cell.TileIndex, (int)cell.Delay, (int)cell.Kill)));
        Assert.Equal(row.GetProperty("netCells").GetArrayLength(), replication.PendingLiquidUpdates);

        var expectedRandom = row.GetProperty("rng");
        var fields = typeof(VanillaUnifiedRandom1458).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        var seedArray = Assert.Single(fields, field => field.FieldType == typeof(int[]));
        Assert.Equal(expectedRandom.GetProperty("SeedArray").EnumerateArray().Select(value => value.GetInt32()),
            Assert.IsType<int[]>(seedArray.GetValue(random)));
        var cursor = Assert.Single(fields, field => field.Name == "inext");
        Assert.Equal(expectedRandom.GetProperty("inext").GetUInt32(), Assert.IsType<uint>(cursor.GetValue(random)));
    }

    [Theory]
    [InlineData(1024.75f, 2023.25f, 2)]
    [InlineData(2023.75f, 1024.25f, 6)]
    [InlineData(2052.75f, 1024.25f, 0)]
    public void Expert_skyblock_edge_scan_adopts_with_the_actual_npc_death(float x, float y, int expectedCells)
    {
        var tiles = new WorldTileStore(new(WorldSize, WorldSize));
        Assert.True(tiles.TryAttachWorldSurface(64));
        var npcs = new RuntimeNpcStore();
        var random = new VanillaUnifiedRandom1458(42);
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles, expertMode: true, skyblockLowTiles: true,
            worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random), naturalSpawnRandom: new SystemVanillaNpcRandom(random),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { SkyblockWorld = true });
        var composition = (ServerRuntimeComposition)typeof(ServerRuntimeState)
            .GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        var pipeline = (RuntimeNpcNetworkCombatPipeline)typeof(NpcAuthority)
            .GetField("combat", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(composition.Npcs)!;
        var simulation = NpcSimulationState.Initial with { MoneyValue = 0, ExtraMoneyValue = 0, HitboxOverride = new(47, 35) };
        Assert.True(npcs.TrySpawnVanilla(new(59, 59, x, y, 0, 0, 255, new(0, 2, 0, 0), simulation), out var npc));

        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, pipeline.TryStrikeEnvironment(npc.Handle, 100000));
        Assert.False(npcs.TryGet(npc.Handle, out _));
        Assert.Equal(expectedCells, tiles.Tiles.ToArray().Count(tile => tile.LiquidAmount == 255 && tile.LiquidKind == WorldLiquidKind.Lava));
        Assert.Equal(0, tiles.LiquidUpdates.ActiveCount);
    }
}
