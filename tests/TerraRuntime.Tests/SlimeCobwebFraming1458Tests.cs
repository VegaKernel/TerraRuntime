using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SlimeCobwebFraming1458Tests
{
    public static IEnumerable<object[]> FrameCases() => Rows("WebFrameMetadata1458", "frames");
    public static IEnumerable<object[]> AttachmentCases() => Rows("WebFrameMetadata1458", "attachments");
    public static IEnumerable<object[]> WholeCases() => Rows("SlimeWebFraming1458");
    private static JsonDocument SourceRow(string name) => JsonDocument.Parse(
        WholeCases().Select(static row => (string)row[0]).First(json =>
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("name").GetString() == name &&
                !document.RootElement.GetProperty("outer").GetBoolean();
        }));
    private static IEnumerable<object[]> Rows(string resource, string? property = null)
    {
        using var source = typeof(SlimeCobwebFraming1458Tests).Assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in (property is null ? document.RootElement : document.RootElement.GetProperty(property)).EnumerateArray())
            yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(FrameCases))]
    public void Every_source_attachment_mask_and_retained_bank_has_exact_cosmetic_coordinates(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        Assert.True(VanillaCobwebFrames1458.TryGetFrame(row.GetProperty("mask").GetByte(), row.GetProperty("bank").GetByte(), out short x, out short y));
        Assert.Equal(row.GetProperty("fx").GetInt16(), x); Assert.Equal(row.GetProperty("fy").GetInt16(), y);
        Assert.Equal(row.GetProperty("next").GetInt32(), new Random(1458).Next());
    }

    [Theory, MemberData(nameof(AttachmentCases))]
    public void Every_source_tile_identity_merges_or_excludes_from_cobweb_by_exact_metadata(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        Span<WorldTile> neighbors = stackalloc WorldTile[8];
        neighbors[1] = new() { Type = row.GetProperty("type").GetUInt16(), Flags = WorldTileFlags.Active };
        Assert.True(VanillaCobwebFrames1458.TryGetAttachmentMask(neighbors, out byte mask));
        Assert.True(VanillaCobwebFrames1458.TryGetFrame(mask, 0, out short x, out short y));
        Assert.Equal(row.GetProperty("fx").GetInt16(), x); Assert.Equal(row.GetProperty("fy").GetInt16(), y);
    }

    [Fact]
    public void Recursive_cosmetic_important_returns_use_the_complete_source_metadata()
    {
        using var source = typeof(SlimeCobwebFraming1458Tests).Assembly.GetManifestResourceStream("WebFrameMetadata1458")!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        var important = document.RootElement.GetProperty("important");
        for (ushort type = 0; type < important.GetArrayLength(); type++)
        {
            Assert.True(VanillaCobwebFrames1458.TryGetFrameImportant(type, out bool actual));
            Assert.Equal(important[type].GetBoolean(), actual);
        }
        Assert.False(VanillaCobwebFrames1458.TryGetFrameImportant(ushort.MaxValue, out _));
    }

    [Fact]
    public void Selected_pre_reset_recursive_center_requires_its_real_inactive_bank()
    {
        using var source = SourceRow("wetUpWater");
        var fixture = new Fixture(source.RootElement, ownInactiveBanks: false);
        var random = fixture.Random.SourceRandom.Clone();
        Assert.False(fixture.Targeting.TryStepState(in fixture.Parent, out _));
        Assert.True(fixture.Random.SourceRandom.HasSameState(random));
        Assert.True(fixture.Npcs.TryGet(fixture.Parent.Handle, out var current)); Assert.Equal(fixture.Parent, current);
        Assert.Equal(0, fixture.Items.ActiveCount); Assert.Empty(Drain(fixture.Outbound));
    }

    [Theory]
    [InlineData("bank")]
    [InlineData("liquid")]
    [InlineData("allocation")]
    [InlineData("protection")]
    public void Planned_physical_effects_recheck_non_tile_source_owners_before_claim(string mutation)
    {
        using var source = SourceRow(mutation == "bank" ? "noAttachOuter" : "wetRightWater");
        var fixture = new Fixture(source.RootElement);
        var cloned = new SystemVanillaNpcRandom(fixture.Random.SourceRandom.Clone());
        var environment = new VanillaSlimeContainedWorld1458(fixture.Tiles, worldItems: fixture.Items, protectionNpcs: fixture.Npcs);
        var target = new VanillaNpcTargetCandidate(0, 1102, 1259, 0, true, false, false, false);
        Assert.True(environment.TryCapture(in fixture.Parent, in target, out var world));
        Assert.True(world.TryPlanTileProducer(VanillaItemIds.Cobweb.Value, in fixture.Parent, cloned, out _));
        Assert.True(world.IsCurrent);
        int x = source.RootElement.GetProperty("cx").GetInt32(), y = source.RootElement.GetProperty("cy").GetInt32();
        if (mutation == "bank")
        {
            var tile = fixture.Tiles.Get(x + 1, y);
            Assert.True(fixture.Tiles.TryRetainCobwebFrameNumber(x + 1, y, in tile, 1));
        }
        else if (mutation == "liquid") Assert.True(fixture.Tiles.LiquidUpdates.TryEnqueue(200, 200));
        else if (mutation == "allocation")
        {
            var item = VanillaSimpleTileBreakResolver1458.MaterializeItemState(VanillaItemIds.Cobweb, 1, x, y,
                new SystemWorldItemSpawnRandom(1));
            Assert.True(fixture.Items.TryAllocateDrop(in item, out _));
        }
        else
        {
            var plant = new NpcStateUpdate(43, 43, 100, 100, 0, 0, 0, default,
                NpcSimulationState.Initial with { Life = 40, LifeMax = 40 });
            Assert.True(fixture.Npcs.TrySpawn(1, in plant, out _));
        }
        _ = Drain(fixture.Outbound);
        Assert.False(world.IsCurrent); Assert.False(world.TryClaimTileProducer()); world.CancelTileProducer();
        Assert.Empty(Drain(fixture.Outbound));
        Assert.True(fixture.Npcs.TryGet(fixture.Parent.Handle, out var current)); Assert.Equal(fixture.Parent, current);
        foreach (var cell in source.RootElement.GetProperty("beforeTiles").EnumerateArray())
            Assert.Equal(Cell(cell), fixture.Tiles.Get(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32()));
    }

    [Theory, MemberData(nameof(WholeCases))]
    public void Actual_source_AI_and_outer_physics_preserve_connected_frames_liquid_deaths_items_and_wire_order(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        var f = new Fixture(row);
        bool admitted = row.GetProperty("name").GetString() is not ("unsupportedSolid" or "unknownShape" or "invisibleRight") &&
            !RequiresUnownedCosmeticMutation(row);
        var beforeRandom = f.Random.SourceRandom.Clone();
        if (row.GetProperty("outer").GetBoolean())
        {
            var result = new RuntimeNpcAiStateExecutor(f.Npcs).Tick(new VanillaNpcWorldMotionAiStepper(f.Targeting, f.Tiles, 140));
            Assert.Equal(admitted ? 1 : 0, result.Applied);
        }
        else
        {
            Assert.Equal(admitted, f.Targeting.TryStepState(in f.Parent, out var placeholder));
            if (admitted)
            {
                Assert.True(f.Npcs.TryUpdateUnpublished(f.Parent.Handle, in placeholder, out var accepted));
                Assert.True(f.Targeting.TryGetSlimeContainedPlan(in f.Parent, in accepted, out var final));
                f.Targeting.CompleteSlimeContainedPlan(in f.Parent, in accepted, in final);
            }
        }
        Assert.True(f.Npcs.TryGet(f.Parent.Handle, out var current));
        if (!admitted)
        {
            Assert.Equal(f.Parent, current); Assert.True(f.Random.SourceRandom.HasSameState(beforeRandom));
            Assert.Empty(Drain(f.Outbound)); Assert.Equal(0, f.Items.ActiveCount);
            foreach (var cell in row.GetProperty("beforeTiles").EnumerateArray()) Assert.Equal(Cell(cell), f.Tiles.Get(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32()));
            return;
        }
        var expected = row.GetProperty("after");
        Assert.Equal(expected.GetProperty("x").GetSingle(), current.PositionX); Assert.Equal(expected.GetProperty("y").GetSingle(), current.PositionY);
        Assert.Equal(expected.GetProperty("vx").GetSingle(), current.VelocityX); Assert.Equal(expected.GetProperty("vy").GetSingle(), current.VelocityY);
        Assert.Equal(Ai(expected.GetProperty("ai")), current.Ai); Assert.Equal(Ai(expected.GetProperty("localAi")), current.Simulation.LocalAi);
        Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.SourceRandom.Next());
        Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(static x => x.GetString()!)
            .Where(static x => x.Substring(4, 2) != "17"), Drain(f.Outbound)); // Outer actor23 is owned by the enclosing replication pass.
        foreach (var cell in row.GetProperty("tiles").EnumerateArray())
        {
            int x = cell.GetProperty("x").GetInt32(), y = cell.GetProperty("y").GetInt32();
            var expectedCell = Cell(cell); var actualCell = f.Tiles.Get(x, y);
            Assert.True(expectedCell.Equals(actualCell), $"{row.GetProperty("name").GetString()} {x},{y}: expected {expectedCell.Type}/{expectedCell.FrameX},{expectedCell.FrameY}/{expectedCell.Flags}/{expectedCell.Shape}; actual {actualCell.Type}/{actualCell.FrameX},{actualCell.FrameY}/{actualCell.Flags}/{actualCell.Shape}");
            Assert.Equal(cell.GetProperty("checking").GetBoolean(), f.Tiles.LiquidUpdates.IsCheckingLiquid(x, y));
            Assert.Equal(cell.GetProperty("skip").GetBoolean(), f.Tiles.LiquidUpdates.IsSkipNextUpdate(x, y));
            if (cell.GetProperty("active").GetBoolean() && cell.GetProperty("type").GetInt32() == VanillaTileIds.Cobweb.Value)
            { Assert.True(f.Tiles.TryGetCobwebFrameNumber(x, y, out byte bank)); Assert.Equal(cell.GetProperty("bank").GetByte(), bank); }
        }
        var liquids = new List<WorldLiquidUpdate>();
        while (f.Tiles.LiquidUpdates.TryDequeue(out var liquid)) liquids.Add(liquid);
        Assert.Equal(row.GetProperty("liquids").EnumerateArray().Select(static x => new WorldLiquidUpdate(
            x.GetProperty("x").GetInt32(), x.GetProperty("y").GetInt32(), x.GetProperty("delay").GetInt32(), x.GetProperty("kill").GetInt32())), liquids);
        foreach (var item in row.GetProperty("items").EnumerateArray())
        {
            Assert.True(f.Items.TryGetActive(item.GetProperty("slot").GetInt16(), out var physical));
            Assert.Equal(item.GetProperty("type").GetInt16(), physical.ItemNetId); Assert.Equal(item.GetProperty("stack").GetInt16(), physical.Stack);
            Assert.Equal(item.GetProperty("x").GetSingle(), physical.PositionX); Assert.Equal(item.GetProperty("y").GetSingle(), physical.PositionY);
            Assert.Equal(item.GetProperty("vx").GetSingle(), physical.VelocityX); Assert.Equal(item.GetProperty("vy").GetSingle(), physical.VelocityY);
        }
        Assert.Equal(row.GetProperty("items").GetArrayLength(), f.Items.ActiveCount);
    }

    private static WorldTile Cell(JsonElement cell) => new()
    {
        Type = cell.GetProperty("type").GetUInt16(), FrameX = cell.GetProperty("fx").GetInt16(), FrameY = cell.GetProperty("fy").GetInt16(),
        Shape = (byte)(cell.GetProperty("shape").GetBoolean() ? 1 : cell.GetProperty("slope").GetInt32() == 0 ? 0 : cell.GetProperty("slope").GetInt32() + 1),
        TileColor = cell.GetProperty("color").GetByte(), Wall = cell.GetProperty("wall").GetUInt16(), LiquidAmount = cell.GetProperty("liquid").GetByte(),
        LiquidKind = (WorldLiquidKind)cell.GetProperty("kind").GetInt32(), Flags =
            (cell.GetProperty("active").GetBoolean() ? WorldTileFlags.Active : 0) |
            (cell.GetProperty("invisible").GetBoolean() ? WorldTileFlags.InvisibleBlock : 0) |
            (cell.GetProperty("fullbright").GetBoolean() ? WorldTileFlags.FullbrightBlock : 0) |
            (cell.GetProperty("wire").GetBoolean() ? WorldTileFlags.WireRed : 0)
    };
    private static bool RequiresUnownedCosmeticMutation(JsonElement row)
    {
        var before = row.GetProperty("beforeTiles").EnumerateArray().ToDictionary(
            static cell => (cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32()), static cell => Cell(cell));
        return row.GetProperty("tiles").EnumerateArray().Any(cell =>
        {
            var after = Cell(cell);
            return after.IsActive && after.Type != VanillaTileIds.Cobweb.Value &&
                !after.Equals(before[(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32())]);
        });
    }
    private static NpcAiState Ai(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle(), a[3].GetSingle());
    private static List<string> Drain(TerrariaConnectionOutboundQueue queue)
    {
        var output = new List<string>(); var inner = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
        while (inner.TryRead(out var frame)) output.Add(Convert.ToHexString(frame.Bytes.Span)); return output;
    }

    private sealed class Fixture
    {
        public WorldTileStore Tiles { get; } = new(new(600, 500));
        public RuntimeNpcStore Npcs { get; } = new(200);
        public RuntimeWorldItemStore Items { get; }
        public SystemVanillaNpcRandom Random { get; }
        public readonly NpcSnapshot Parent;
        public VanillaNpcTargetingAiStepper Targeting { get; }
        public TerrariaConnectionOutboundQueue Outbound { get; } = new(new(128, 65536, 4096));
        public Fixture(JsonElement row, bool ownInactiveBanks = true)
        {
            Assert.True(Tiles.TryAttachWorldSurface(140));
            foreach (var cell in row.GetProperty("beforeTiles").EnumerateArray())
            {
                int x = cell.GetProperty("x").GetInt32(), y = cell.GetProperty("y").GetInt32(); var tile = Cell(cell); Tiles.Set(x, y, in tile);
                if (tile.IsActive && tile.Type == VanillaTileIds.Cobweb.Value) Assert.True(Tiles.TryRetainCobwebFrameNumber(x, y, in tile, cell.GetProperty("bank").GetByte()));
                if (!tile.IsActive && ownInactiveBanks) Assert.True(Tiles.TryRetainCobwebPlacementFrameNumber(x, y, in tile, cell.GetProperty("bank").GetByte()));
                if (cell.GetProperty("checking").GetBoolean()) Assert.True(Tiles.LiquidUpdates.TryEnqueue(x, y));
                if (cell.GetProperty("skip").GetBoolean()) Tiles.LiquidUpdates.SetSkipNextUpdate(x, y);
            }
            var tilesRegistry = new RuntimeTileManipulationReplicationRegistry(); var itemRegistry = new RuntimeWorldItemReplicationRegistry();
            var authority = new PlayerAuthority(null, Tiles);
            var slots = new PlayerSlotPool(1); Assert.True(slots.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease)); session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(4150), session.Handle);
            var spawn = new PlayerSpawnCommitRequest(session.Slot, 20, 20, 0, 0, 0, 0, 0);
            Assert.True(authority.TryApply(new PlayerSpawnRuntimeCommand(connection, session, spawn)));
            int cx = row.GetProperty("cx").GetInt32(), cy = row.GetProperty("cy").GetInt32();
            float playerX = row.GetProperty("px").GetSingle(), playerY = row.GetProperty("py").GetSingle();
            Assert.True(authority.TryApply(new PlayerMovementRuntimeCommand(connection,
                new PlayerMovementCommitRequest(session.Slot, 0, 0, 0, 0, 0, playerX, playerY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0))));
            Assert.True(authority.TryGet(session.Slot, out var member));
            Assert.Equal(playerX, member.PositionX); Assert.Equal(playerY, member.PositionY);
            for (short i = 0; i < 59; i++) Assert.True(authority.TryApply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, i, 0, 0, 0, 0))));
            Items = new(itemRegistry); Items.AttachOwnerFactsProvider(new RuntimeWorldItemOwnerFactsProvider1458(authority, Tiles, false, false));
            Assert.True(tilesRegistry.TryRegister(connection.Source, Outbound)); Assert.True(itemRegistry.TryRegister(connection.Source, Outbound));
            tilesRegistry.PlayerSpawned(connection, in spawn); itemRegistry.PlayerSpawned(connection, in spawn);
            var b = row.GetProperty("before"); Random = new(row.GetProperty("seed").GetInt32()); Npcs.SetVanillaSpawnRandomSource(Random);
            var input = new NpcStateUpdate(1, 1, b.GetProperty("x").GetSingle(), b.GetProperty("y").GetSingle(),
                b.GetProperty("vx").GetSingle(), b.GetProperty("vy").GetSingle(), 0, Ai(b.GetProperty("ai")), NpcSimulationState.Initial with
                { Life = 25, LifeMax = 25, BaseLifeMax = 25, BaseDamage = 7, BaseDefense = 2, MoneyValue = 25,
                  DamageOverride = 7, DefenseOverride = 2, HitboxOverride = new(24, 18), Alpha = 175,
                  DirectionX = b.GetProperty("direction").GetInt32(), DirectionY = b.GetProperty("directionY").GetInt32(), LocalAi = Ai(b.GetProperty("localAi")) });
            Assert.True(Npcs.TrySpawn(0, in input, out Parent));
            Targeting = new(new Rejecting(), random: Random); Targeting.EnableBlueSlimeMotion(140); Targeting.SetWorldBounds(600, 140, 200, 500);
            Targeting.SetWorldConditions(true, false, false, false, false); Targeting.SetPlayerSnapshotLookup(new Players(authority));
            Targeting.SetCandidates([new(0, playerX + 10, playerY + 21, 0, true, false, false, false)]);
            var facts = new VanillaSlimeContainedFacts1458(140, 200, false, false, false, false, false, false, false, true, false, false, false, false, false, 0);
            Targeting.SetSlimeContainedOwner(Npcs, () => facts, new VanillaSlimeContainedWorld1458(Tiles, tilesRegistry, Items, Npcs));
        }
    }
    private sealed class Players(PlayerAuthority owner) : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot player)
        { player = default; if (!owner.TryGet(slot, out var member)) return false; player = member.CaptureSnapshot(); return true; }
    }
    private sealed class Rejecting : INpcAiStateStepper
    { public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; } }
}
