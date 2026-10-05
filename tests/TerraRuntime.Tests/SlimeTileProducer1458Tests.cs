using System.IO.Compression;
using System.Text.Json;
using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class SlimeTileProducer1458Tests
{
    public static IEnumerable<object[]> HerbCases()
    {
        using var stream = typeof(SlimeTileProducer1458Tests).Assembly.GetManifestResourceStream("SlimeHerbPlacement1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(HerbCases))]
    public void Source_all_seven_herb_styles_preserve_liquid_target_shape_and_support_coatings(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        var before = HerbCell(row.GetProperty("before"), row.GetProperty("liquid").GetInt32());
        var support = HerbCell(row.GetProperty("support"), 0);
        bool grew = VanillaHerbPlacement1458.TryPlan(row.GetProperty("x").GetInt32(),
            row.GetProperty("width").GetInt32(), in before, in support, out var after);
        Assert.Equal(row.GetProperty("after").GetProperty("active").GetBoolean(), grew);
        Assert.Equal(HerbCell(row.GetProperty("after"), row.GetProperty("liquid").GetInt32()), after);
        Assert.Equal(row.GetProperty("next").GetInt32(), new Random(1458).Next());
    }

    private static WorldTile HerbCell(JsonElement cell, int liquid) => new()
    {
        Type = cell.GetProperty("type").GetUInt16(), FrameX = cell.GetProperty("fx").GetInt16(),
        FrameY = cell.GetProperty("fy").GetInt16(), TileColor = cell.GetProperty("color").GetByte(),
        Shape = (byte)(cell.GetProperty("half").GetBoolean() ? 1 : 0),
        LiquidAmount = cell.GetProperty("liquid").GetByte(),
        LiquidKind = liquid == 2 ? WorldLiquidKind.Lava : WorldLiquidKind.Water,
        Flags = (cell.GetProperty("active").GetBoolean() ? WorldTileFlags.Active : 0) |
            (cell.GetProperty("invisible").GetBoolean() ? WorldTileFlags.InvisibleBlock : 0) |
            (cell.GetProperty("fullbright").GetBoolean() ? WorldTileFlags.FullbrightBlock : 0)
    };

    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(SlimeTileProducer1458Tests).Assembly.GetManifestResourceStream("SlimeTileProducer1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray())
            if (!row.TryGetProperty("section", out _)) yield return [row.GetRawText()];
    }

    public static IEnumerable<object[]> SectionCases()
    {
        using var stream = typeof(SlimeTileProducer1458Tests).Assembly.GetManifestResourceStream("SlimeTileProducer1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray())
            if (row.TryGetProperty("section", out _)) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(SectionCases))]
    public void Source_packet20_uses_OR_of_four_plus_size_section_corners_and_exact_endpoint_generation(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        var tiles = new WorldTileStore(new(1200, 500));
        var queue = new TerrariaConnectionOutboundQueue(new(16, 4096, 4096));
        var registry = new RuntimeTileManipulationReplicationRegistry();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1), new(new(0), new(1)));
        Assert.True(registry.TryRegister(connection.Source, queue,
            new SectionOwner(row.GetProperty("ownedX").GetInt32(), row.GetProperty("ownedY").GetInt32())));
        var spawn = new PlayerSpawnCommitRequest(new(0), 1, 1, 0, 0, 0, 0, 0);
        registry.PlayerSpawned(connection, in spawn);
        Publish(); Assert.Equal(Expected(), Drain());
        var replacement = connection with { Player = new(new(0), new(2)) };
        registry.PlayerSpawned(replacement, in spawn); registry.PlayerDisconnected(connection);
        Publish(); Assert.Equal(Expected(), Drain());
        registry.PlayerDisconnected(replacement); Publish(); Assert.Empty(Drain());

        void Publish() => Assert.True(registry.TryPublishTileSquareToAll(tiles, row.GetProperty("x").GetInt32(),
            row.GetProperty("y").GetInt32(), 1, 1, VanillaTileChangeType1458.None, respectSectionRange: true));
        IEnumerable<string> Expected() => row.GetProperty("frames").EnumerateArray().Select(static f => f.GetString()!);
        List<string> Drain()
        {
            var frames = new List<string>(); var inner = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
                .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
            while (inner.TryRead(out var frame)) frames.Add(Convert.ToHexString(frame.Bytes.Span));
            return frames;
        }
    }

    private sealed class SectionOwner(int x, int y) : IPlayerSectionVisibility
    { public bool OwnsSectionAtTile(int tileX, int tileY) => tileX / 200 == x && tileY / 150 == y; }

    [Theory, MemberData(nameof(Cases))]
    public void Actual_AI_and_outer_NPC_update_match_physical_cells_local_clock_and_shared_rng(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        var before = row.GetProperty("before"); var after = row.GetProperty("after");
        var tiles = Tiles(row.GetProperty("scenario").GetString()!, row.GetProperty("item").GetInt32());
        var random = new SystemVanillaNpcRandom(row.GetProperty("seed").GetInt32());
        var store = new RuntimeNpcStore(200);
        store.SetVanillaSpawnRandomSource(random);
        var input = new NpcStateUpdate(1, 1, before.GetProperty("x").GetSingle(), before.GetProperty("y").GetSingle(),
            before.GetProperty("vx").GetSingle(), before.GetProperty("vy").GetSingle(), 0,
            Ai(before.GetProperty("ai")), NpcSimulationState.Initial with {
                DirectionX = before.GetProperty("direction").GetInt32(), DirectionY = before.GetProperty("directionY").GetInt32(),
                Life = 25, LifeMax = 25, BaseLifeMax = 25, BaseDamage = 7, BaseDefense = 2,
                DamageOverride = 7, DefenseOverride = 2, Alpha = 175, MoneyValue = 25,
                HitboxOverride = new(24, 18), LocalAi = Ai(before.GetProperty("localAi")) });
        Assert.True(store.TrySpawn(0, in input, out var parent));
        var outbound = new TerrariaConnectionOutboundQueue(new(32, 16384, 4096));
        var tileReplication = new RuntimeTileManipulationReplicationRegistry();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1), new(new(0), new(1)));
        Assert.True(tileReplication.TryRegister(connection.Source, outbound));
        var spawn = new PlayerSpawnCommitRequest(new(0), 1, 1, 0, 0, 0, 0, 0);
        tileReplication.PlayerSpawned(connection, in spawn);
        var targeting = Targeting(store, tiles, random, tileReplication);
        INpcAiStateStepper stepper = row.GetProperty("outer").GetBoolean()
            ? new VanillaNpcWorldMotionAiStepper(targeting, tiles, 140) : targeting;
        if (row.GetProperty("outer").GetBoolean())
        {
            var executor = new RuntimeNpcAiStateExecutor(store);
            Assert.Equal(1, executor.Tick(stepper).Applied);
        }
        else
        {
            Assert.True(targeting.TryStepState(in parent, out var placeholder));
            Assert.True(store.TryUpdateUnpublished(parent.Handle, in placeholder, out var accepted));
            Assert.True(targeting.TryGetSlimeContainedPlan(in parent, in accepted, out var planned));
            targeting.CompleteSlimeContainedPlan(in parent, in accepted, in planned);
        }
        Assert.True(store.TryGet(parent.Handle, out var completed));
        Assert.Equal(Ai(after.GetProperty("ai")), completed.Ai);
        Assert.Equal(Ai(after.GetProperty("localAi")), completed.Simulation.LocalAi);
        Assert.Equal(after.GetProperty("x").GetSingle(), completed.PositionX);
        Assert.Equal(after.GetProperty("y").GetSingle(), completed.PositionY);
        Assert.Equal(after.GetProperty("vx").GetSingle(), completed.VelocityX);
        Assert.Equal(after.GetProperty("vy").GetSingle(), completed.VelocityY);
        Assert.Equal(row.GetProperty("next").GetInt32(), random.SourceRandom.Next());
        var actualFrames = new List<string>();
        var innerQueue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
        while (innerQueue.TryRead(out var frame)) actualFrames.Add(Convert.ToHexString(frame.Bytes.Span));
        Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(static f => f.GetString()!)
            .Where(static hex => hex.Substring(4, 2) == "14"), actualFrames);
        foreach (var cell in row.GetProperty("tiles").EnumerateArray())
        {
            var actual = tiles.Get(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32());
            Assert.Equal(cell.GetProperty("type").GetUInt16(), actual.Type);
            Assert.Equal(cell.GetProperty("active").GetBoolean(), actual.IsActive);
            Assert.Equal(cell.GetProperty("fx").GetInt16(), actual.FrameX);
            Assert.Equal(cell.GetProperty("fy").GetInt16(), actual.FrameY);
            Assert.Equal(cell.GetProperty("color").GetByte(), actual.TileColor);
            Assert.Equal(cell.GetProperty("liquid").GetByte(), actual.LiquidAmount);
            Assert.Equal(cell.GetProperty("half").GetBoolean() ? 1 : cell.GetProperty("slope").GetInt32() == 0
                ? 0 : cell.GetProperty("slope").GetInt32() + 1, actual.Shape);
            Assert.Equal(cell.GetProperty("invisible").GetBoolean(), actual.IsBlockInvisible);
            Assert.Equal(cell.GetProperty("fullbright").GetBoolean(), actual.IsBlockFullbright);
            Assert.Equal(cell.GetProperty("wire").GetBoolean(), (actual.Flags & WorldTileFlags.WireRed) != 0);
            Assert.Equal(cell.GetProperty("wall").GetUInt16(), actual.Wall);
        }
    }

    [Fact]
    public void Equal_tile_bytes_do_not_hide_a_changed_source_checking_liquid_claim()
    {
        var tiles = Tiles("grass", 150); var random = new SystemVanillaNpcRandom(347);
        var store = new RuntimeNpcStore(); var state = Input(150);
        Assert.True(store.TrySpawn(0, in state, out var parent));
        var world = new VanillaSlimeContainedWorld1458(tiles);
        var target = new VanillaNpcTargetCandidate(0, 1110, 1259, 0, true, false, false, false);
        Assert.True(world.TryCapture(in parent, in target, out var captured));
        Assert.True(captured.TryPlanTileProducer(150, in parent, random, out _));
        var before = tiles.Get(63, 78); long version = tiles.GetSectionVersion(new(0, 0));
        Assert.True(tiles.LiquidUpdates.TryBuffer(63, 78));
        Assert.Equal(before, tiles.Get(63, 78)); Assert.Equal(version, tiles.GetSectionVersion(new(0, 0)));
        Assert.False(captured.IsCurrent);
    }

    [Fact]
    public void Selected_unowned_neighbor_framing_rejects_before_actor_tiles_or_shared_rng_change()
    {
        var tiles = Tiles("grass", 150); tiles.Set(64, 78, new() { Type = 1, Flags = WorldTileFlags.Active });
        var random = new SystemVanillaNpcRandom(347); var expected = random.SourceRandom.Clone();
        var store = new RuntimeNpcStore(); var state = Input(150);
        Assert.True(store.TrySpawn(0, in state, out var parent));
        var targeting = Targeting(store, tiles, random);
        var tile = tiles.Get(63, 78); var neighbor = tiles.Get(64, 78);
        Assert.False(targeting.TryStepState(in parent, out _));
        Assert.True(store.TryGet(parent.Handle, out var retained)); Assert.Equal(parent, retained);
        Assert.Equal(tile, tiles.Get(63, 78)); Assert.Equal(neighbor, tiles.Get(64, 78));
        Assert.True(random.SourceRandom.HasSameState(expected));
    }

    [Fact]
    public void Checking_liquid_tracks_active_and_buffered_owners_and_retirement()
    {
        var queue = new WorldLiquidUpdateQueue(new(100, 100));
        Assert.False(queue.IsCheckingLiquid(-1, 4)); Assert.False(queue.IsCheckingLiquid(4, 4));
        Assert.True(queue.TryEnqueue(4, 4)); Assert.True(queue.IsCheckingLiquid(4, 4));
        Assert.True(queue.TryDequeue(out _)); Assert.False(queue.IsCheckingLiquid(4, 4));
        Assert.True(queue.TryBuffer(4, 4)); Assert.True(queue.IsCheckingLiquid(4, 4));
        Assert.True(queue.TryDequeueBuffered(out _, out _)); Assert.False(queue.IsCheckingLiquid(4, 4));
        Assert.True(queue.TryEnqueue(4, 4)); Assert.True(queue.TryBuffer(5, 4)); queue.Clear();
        Assert.False(queue.IsCheckingLiquid(4, 4)); Assert.False(queue.IsCheckingLiquid(5, 4));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tile_or_equal_bytes_liquid_claim_changed_after_AI_preview_cannot_adopt_physical_effects(bool checking)
    {
        var tiles = Tiles("grass", 150); var random = new SystemVanillaNpcRandom(347);
        var expectedRandom = random.SourceRandom.Clone(); var store = new RuntimeNpcStore(); var input = Input(150);
        Assert.True(store.TrySpawn(0, in input, out var parent));
        var targeting = Targeting(store, tiles, random);
        Assert.True(targeting.TryStepState(in parent, out var placeholder));
        Assert.True(store.TryUpdateUnpublished(parent.Handle, in placeholder, out var accepted));
        if (checking) Assert.True(tiles.LiquidUpdates.TryBuffer(63, 78));
        else tiles.Set(63, 78, new() { Type = 1, Flags = WorldTileFlags.Active });
        var external = tiles.Get(63, 78);
        Assert.False(targeting.TryGetSlimeContainedPlan(in parent, in accepted, out _));
        Assert.True(store.TryGet(parent.Handle, out var retained)); Assert.Equal(accepted, retained);
        Assert.Equal(external, tiles.Get(63, 78)); Assert.True(random.SourceRandom.HasSameState(expectedRandom));
    }

    private static NpcStateUpdate Input(int item) => new(1, 1, 1000, 1262, .5f, 0, 0,
        new(-100, item, 0, 0), NpcSimulationState.Initial with {
            Life = 25, LifeMax = 25, BaseLifeMax = 25, MoneyValue = 25, HitboxOverride = new(24, 18) });
    private static NpcAiState Ai(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle(), a[3].GetSingle());
    private static WorldTileStore Tiles(string scenario, int item)
    {
        var tiles = new WorldTileStore(new(600, 500)); Assert.True(tiles.TryAttachWorldSurface(140));
        ushort support = scenario switch { "grass" => 2, "hallow" => 109, "jungle" => 60, "dirt" => 0,
            "mud" => 59, "corrupt" => 23, "crimson" => 199, "sand" => 53, "ash" => 57, "snow" => 147, _ => 1 };
        for (int x = 0; x < 600; x++) tiles.Set(x, 80, new() { Type = 1, Flags = WorldTileFlags.Active });
        for (int x = 58; x <= 68; x++) tiles.Set(x, 80, new() { Type = support, Flags = WorldTileFlags.Active });
        var ground = tiles.Get(63, 80);
        if (scenario == "half") ground.Shape = 1;
        if (scenario == "slope") ground.Shape = 2;
        if (scenario == "actuated") ground.Flags |= WorldTileFlags.Inactive;
        tiles.Set(63, 80, in ground);
        if (scenario is "water" or "lava") tiles.Set(63, item == 314 ? 79 : 78, new() {
            LiquidAmount = 100, LiquidKind = scenario == "lava" ? WorldLiquidKind.Lava : WorldLiquidKind.Water });
        if (scenario == "occupied") tiles.Set(63, item == 314 ? 79 : 78, new() { Type = 1, Flags = WorldTileFlags.Active });
        if (scenario == "checking") Assert.True(tiles.LiquidUpdates.TryBuffer(63, 78));
        if (scenario == "metadata")
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                tiles.Set(63 + dx, 78 + dy, new() { Shape = 1, TileColor = 7, Wall = 2,
                    FrameX = 123, FrameY = 45, Flags = WorldTileFlags.WireRed |
                        WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock });
        return tiles;
    }
    private static VanillaNpcTargetingAiStepper Targeting(RuntimeNpcStore store, WorldTileStore tiles, IVanillaNpcRandom random,
        RuntimeTileManipulationReplicationRegistry? tileReplication = null)
    {
        store.SetVanillaSpawnRandomSource(random);
        var target = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        target.EnableBlueSlimeMotion(140); target.SetWorldBounds(600, 140, 200, 500);
        target.SetWorldConditions(true, false, false, false, false);
        target.SetPlayerSnapshotLookup(new Players());
        target.SetCandidates([new(0, 1110, 1259, 0, true, false, false, false)]);
        var facts = new VanillaSlimeContainedFacts1458(140, 200, false, false, false, false, false,
            false, false, true, false, false, false, false, false, (int)VanillaMoonPhase.Full);
        target.SetSlimeContainedOwner(store, () => facts, new VanillaSlimeContainedWorld1458(tiles, tileReplication));
        return target;
    }
    private sealed class Players : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot player)
        {
            player = new(new(new(0), new(1)), new(1), 0, 0, 0, 0, 0, 0, 1100, 1238,
                0, 0, 0, 0, 0, 0, 0, 0, 0); return slot.Value == 0;
        }
    }
    private sealed class Rejecting : INpcAiStateStepper
    { public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; } }
}
