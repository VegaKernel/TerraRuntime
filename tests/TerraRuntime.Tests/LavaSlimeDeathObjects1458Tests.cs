using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class LavaSlimeDeathObjects1458Tests
{
    public static IEnumerable<object[]> OriginalRows()
    {
        using var input = typeof(LavaSlimeDeathObjects1458Tests).Assembly.GetManifestResourceStream("LavaSlimeDeathObjects1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        Assert.Equal("4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034",
            json.RootElement.GetProperty("officialSha256").GetString());
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
        {
            string label = $"{row.GetProperty("terrain").GetString()} / {row.GetProperty("mode").GetInt32()} " +
                $"/ good={row.GetProperty("goodWorld").GetBoolean()} / low={row.GetProperty("lowTiles").GetBoolean()} " +
                $"/ seed={row.GetProperty("seed").GetInt32()}";
            yield return [label, row.GetRawText()];
        }
    }

    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Dedicated_HitEffect_matches_retained_tiles_liquid_queue_item_order_and_RNG(string label, string observation)
    {
        using var json = JsonDocument.Parse(observation);
        var row = json.RootElement;
        Assert.False(string.IsNullOrEmpty(label));
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var tiles = new WorldTileStore(new(128, 128));
        foreach (var initial in row.GetProperty("initialTiles").EnumerateArray())
            tiles.Set(initial.GetProperty("x").GetInt32(), initial.GetProperty("y").GetInt32(),
                ReadRetainedTile(initial.GetProperty("tile")));
        var before = tiles.Tiles.ToArray();
        var replication = new RuntimeTileManipulationReplicationRegistry();
        var state = new ServerRuntimeState(worldTiles: tiles, tileManipulationReplication: replication);
        var owner = Composition(state).WorldTileAuthority;
        bool lowTiles = row.GetProperty("lowTiles").GetBoolean();
        bool eligible = lowTiles || row.GetProperty("mode").GetInt32() > 0 || row.GetProperty("goodWorld").GetBoolean();
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        for (int i = 0; i < 40; i++) random.Next(8);
        var drops = new List<WorldItemDropStateUpdate>();
        if (eligible)
        {
            Assert.True(owner.TryPrepareLavaSlimeDeath(1024.75f, 1024.25f, 47, 35, lowTiles, random,
                out var preparation, drop => { drops.Add(drop); return true; }), label);
            Assert.Equal(before, tiles.Tiles.ToArray());
            Assert.Equal(0, tiles.LiquidUpdates.ActiveCount);
            Assert.Equal(0, replication.PendingLiquidUpdates);
            Assert.True(preparation!.TryAdoptUnpublished());
        }

        var sourceDrops = row.GetProperty("itemChanges").EnumerateArray().ToArray();
        Assert.Equal(sourceDrops.Length, drops.Count);
        for (int i = 0; i < drops.Count; i++)
        {
            var source = sourceDrops[i];
            var actual = drops[i];
            Assert.Equal(source.GetProperty("type").GetInt16(), actual.ItemNetId);
            Assert.Equal(source.GetProperty("stack").GetInt16(), actual.Stack);
            Assert.Equal(source.GetProperty("prefix").GetByte(), actual.Prefix);
            Assert.Equal(source.GetProperty("x").GetSingle(), actual.PositionX);
            Assert.Equal(source.GetProperty("y").GetSingle(), actual.PositionY);
            Assert.Equal(source.GetProperty("vx").GetSingle(), actual.VelocityX);
            Assert.Equal(source.GetProperty("vy").GetSingle(), actual.VelocityY);
        }
        foreach (var change in row.GetProperty("changes").EnumerateArray())
        {
            int x = change.GetProperty("x").GetInt32(), y = change.GetProperty("y").GetInt32();
            before[tiles.GetUncheckedIndex(x, y)] = ReadRetainedTile(change.GetProperty("after"));
        }
        Assert.Equal(before, tiles.Tiles.ToArray());
        var queue = tiles.LiquidUpdates.CaptureActiveSnapshot();
        var sourceQueue = row.GetProperty("queued").EnumerateArray().ToArray();
        Assert.Equal(sourceQueue.Length, queue.Length);
        for (int i = 0; i < queue.Length; i++)
        {
            var source = sourceQueue[i];
            Assert.Equal(tiles.GetUncheckedIndex(source.GetProperty("x").GetInt32(), source.GetProperty("y").GetInt32()),
                queue[i].TileIndex);
            Assert.Equal(source.GetProperty("delay").GetInt32(), queue[i].Delay);
            Assert.Equal(source.GetProperty("kill").GetInt32(), queue[i].Kill);
        }
        Assert.Equal(row.GetProperty("netCells").GetArrayLength(), replication.PendingLiquidUpdates);

        var fields = typeof(VanillaUnifiedRandom1458).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        var sourceRandom = row.GetProperty("rng");
        Assert.Equal(sourceRandom.GetProperty("SeedArray").EnumerateArray().Select(x => x.GetInt32()),
            (int[])Assert.Single(fields, f => f.FieldType == typeof(int[])).GetValue(random)!);
        var cursor = Assert.Single(fields, f => f.Name == "inext");
        Assert.Equal(sourceRandom.GetProperty("inext").GetUInt32(), Assert.IsType<uint>(cursor.GetValue(random)));
    }

    [Fact]
    public void Platform_and_dependent_torch_drops_adopt_together_before_observers()
    {
        var tiles = new WorldTileStore(new(128, 128));
        tiles.Set(65, 64, new WorldTile { Type = 4, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        tiles.Set(65, 65, new WorldTile { Type = 19, FrameY = 30 * 18, Flags = WorldTileFlags.Active });
        var npcs = new RuntimeNpcStore();
        var observer = new ItemObserver();
        var items = new RuntimeWorldItemStore(observer);
        var random = new VanillaUnifiedRandom1458(1458);
        var replication = new RuntimeTileManipulationReplicationRegistry();
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles, worldItems: items,
            worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random), naturalSpawnRandom: new SystemVanillaNpcRandom(random),
            tileManipulationReplication: replication, expertMode: true,
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458));
        var graph = Composition(state);
        var pipeline = (RuntimeNpcNetworkCombatPipeline)typeof(NpcAuthority)
            .GetField("combat", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(graph.Npcs)!;
        var simulation = NpcSimulationState.Initial with { MoneyValue = 0, ExtraMoneyValue = 0, HitboxOverride = new(47, 35) };
        Assert.True(npcs.TrySpawnVanilla(new(59, 59, 1024.75f, 1024.25f, 0, 0, 255, new(0, 2, 0, 0), simulation), out var npc));
        var randomBefore = random.Clone();
        var tileBefore = tiles.Tiles.ToArray();

        Assert.True(items.TryReserveDropSlot(out var reservation));
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, pipeline.TryStrikeEnvironment(npc.Handle, 100000));
        Assert.True(npcs.TryGet(npc.Handle, out var unchanged));
        Assert.Equal(npc, unchanged);
        Assert.Equal(tileBefore, tiles.Tiles.ToArray());
        Assert.True(randomBefore.HasSameState(random));
        Assert.Equal(0, items.ActiveCount);
        Assert.Equal(0, tiles.LiquidUpdates.ActiveCount);
        Assert.Equal(0, replication.PendingLiquidUpdates);
        Assert.Empty(observer.ItemTypes);

        Assert.True(items.TryReleaseDropReservation(in reservation));
        observer.OnCommit = () =>
        {
            Assert.True(npcs.TryGet(npc.Handle, out var dead));
            Assert.Equal(0, dead.Simulation.Life);
            Assert.Equal(WorldTileFlags.WireRed, tiles.Get(65, 64).Flags);
            Assert.False(tiles.Get(65, 65).IsActive);
            Assert.Equal(WorldLiquidKind.Lava, tiles.Get(65, 65).LiquidKind);
            Assert.Equal(1, tiles.LiquidUpdates.ActiveCount);
            Assert.Equal(1, replication.PendingLiquidUpdates);
            Assert.False(randomBefore.HasSameState(random));
        };
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, pipeline.TryStrikeEnvironment(npc.Handle, 100000));
        Assert.Equal(new short[] { 3903, 8 }, observer.ItemTypes.Take(2));
        Assert.False(npcs.TryGet(npc.Handle, out _));
        var randomAfter = random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, pipeline.TryStrikeEnvironment(npc.Handle, 100000));
        Assert.True(randomAfter.HasSameState(random));
    }

    private static ServerRuntimeComposition Composition(ServerRuntimeState state) =>
        (ServerRuntimeComposition)typeof(ServerRuntimeState)
            .GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;

    private static WorldTile ReadRetainedTile(JsonElement raw)
    {
        ushort type = raw.GetProperty("type").GetUInt16();
        int header = raw.GetProperty("sTileHeader").GetInt32(), small = raw.GetProperty("bTileHeader").GetInt32();
        // The runtime retains furniture frames and cleared-cell coordinates.
        // Stone and rope atlas coordinates remain cosmetic.
        bool retainFrame = type == 0 || VanillaTileDefinitionCatalog.TryGet(new(type), out var definition) && definition.IsFrameImportant;
        return new WorldTile
        {
            Type = type, Wall = raw.GetProperty("wall").GetUInt16(),
            FrameX = retainFrame ? raw.GetProperty("frameX").GetInt16() : (short)0,
            FrameY = retainFrame ? raw.GetProperty("frameY").GetInt16() : (short)0,
            LiquidAmount = raw.GetProperty("liquid").GetByte(), LiquidKind = (WorldLiquidKind)((small >> 5) & 3),
            TileColor = (byte)(header & 31), WallColor = (byte)(small & 31),
            Flags = ((header & 32) != 0 ? WorldTileFlags.Active : 0) |
                ((header & 64) != 0 ? WorldTileFlags.Inactive : 0) |
                ((header & 128) != 0 ? WorldTileFlags.WireRed : 0)
        };
    }

    private sealed class ItemObserver : IWorldItemStateCommitSink
    {
        internal List<short> ItemTypes { get; } = [];
        internal Action? OnCommit { get; set; }

        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot item,
            int? reservationOffer = null, GameCommandSourceId excludedSource = default)
        {
            ItemTypes.Add(item.ItemNetId);
            OnCommit?.Invoke();
        }
    }
}
