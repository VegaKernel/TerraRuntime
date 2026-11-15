using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.World;
using TerraRuntime.Protocol;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class LavaSlimeDeathTerrain1458Tests
{
    public static IEnumerable<object[]> OriginalRows()
    {
        using var input = typeof(LavaSlimeDeathTerrain1458Tests).Assembly.GetManifestResourceStream("LavaSlimeHitEffectTerrain1458");
        using var gzip = new GZipStream(input!, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        Assert.Equal("4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034", json.RootElement.GetProperty("officialSha256").GetString());
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.GetRawText()];
    }

    public static IEnumerable<object[]> TrapRows()
    {
        using var input = typeof(LavaSlimeDeathTerrain1458Tests).Assembly.GetManifestResourceStream("LavaSlimeTrapTerrain1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(TrapRows))]
    public void Trap_adjacent_death_terrain_matches_original_without_reframing_the_device(string observation) =>
        Prepared_terrain_matches_initialized_original_tiles_queue_network_and_rng_including_owned_platform_death(observation);

    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Prepared_terrain_matches_initialized_original_tiles_queue_network_and_rng_including_owned_platform_death(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        int seed = row.GetProperty("seed").GetInt32();
        bool lowTiles = row.GetProperty("lowTiles").GetBoolean();
        bool eligible = lowTiles || row.GetProperty("mode").GetInt32() > 0 || row.GetProperty("goodWorld").GetBoolean();
        string terrain = row.GetProperty("terrain").GetString()!;
        var tiles = new WorldTileStore(new(128, 128));
        var center = terrain switch
        {
            "empty" => default,
            "water" => new WorldTile { LiquidAmount = 1 },
            "stone" => new WorldTile { Type = 1, Flags = WorldTileFlags.Active },
            "halfbrick" => new WorldTile { Type = 1, Flags = WorldTileFlags.Active, Shape = 1 },
            "slope" => new WorldTile { Type = 1, Flags = WorldTileFlags.Active, Shape = 2 },
            "actuated" => new WorldTile { Type = 1, Flags = WorldTileFlags.Active | WorldTileFlags.Inactive },
            "platform" => new WorldTile { Type = 19, Flags = WorldTileFlags.Active },
            "trap-center" => new WorldTile { Type = 137, Flags = WorldTileFlags.Active },
            "trap-neighbor0" or "trap-neighbor5" => default,
            _ => throw new InvalidOperationException(terrain)
        };
        tiles.Set(65, 65, center);
        if (terrain.StartsWith("trap-neighbor", StringComparison.Ordinal))
            tiles.Set(64, 64, new WorldTile
            {
                Type = 137,
                Flags = WorldTileFlags.Active,
                FrameX = 18,
                FrameY = (short)(terrain.EndsWith('5') ? 90 : 0)
            });
        var before = tiles.Tiles.ToArray();
        var replication = new RuntimeTileManipulationReplicationRegistry();
        var state = new ServerRuntimeState(worldTiles: tiles, tileManipulationReplication: replication);
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        var live = new VanillaUnifiedRandom1458(seed); var planned = live.Clone();
        // Independently qualified original HitEffect caller choices precede the
        // terrain participant; the recorded postcursor is not computed here.
        for (int i = 0; i < 40; i++) _ = planned.Next(8);
        var drops = new List<WorldItemDropStateUpdate>();
        if (eligible)
        {
            bool accepted = graph.WorldTileAuthority.TryPrepareLavaSlimeDeath(1024.75f, 1024.25f, 47, 35, lowTiles, planned, out var preparation, drop => { drops.Add(drop); return true; });
            Assert.Equal(before, tiles.Tiles.ToArray()); Assert.Equal(0, tiles.LiquidUpdates.ActiveCount);
            Assert.Equal(0, replication.PendingLiquidUpdates);
            Assert.True(live.HasSameState(new(seed)));
            Assert.True(accepted); Assert.NotNull(preparation); Assert.True(preparation!.TryAdoptUnpublished());
            Assert.False(preparation.TryAdoptUnpublished());
        }
        var expectedItems = row.GetProperty("itemChanges").EnumerateArray().ToArray();
        Assert.Equal(expectedItems.Length, drops.Count);
        for (int i = 0; i < drops.Count; i++)
        {
            var item = expectedItems[i]; var drop = drops[i];
            Assert.Equal(item.GetProperty("type").GetInt16(), drop.ItemNetId);
            Assert.Equal(item.GetProperty("stack").GetInt16(), drop.Stack);
            Assert.Equal(item.GetProperty("prefix").GetByte(), drop.Prefix);
            Assert.Equal(item.GetProperty("x").GetSingle(), drop.PositionX);
            Assert.Equal(item.GetProperty("y").GetSingle(), drop.PositionY);
            Assert.Equal(item.GetProperty("vx").GetSingle(), drop.VelocityX);
            Assert.Equal(item.GetProperty("vy").GetSingle(), drop.VelocityY);
        }
        foreach (var change in row.GetProperty("changes").EnumerateArray())
        {
            int x = change.GetProperty("x").GetInt32(), y = change.GetProperty("y").GetInt32();
            before[tiles.GetUncheckedIndex(x, y)] = Normalize(change.GetProperty("after"));
        }
        Assert.Equal(before, tiles.Tiles.ToArray());
        var actual = tiles.LiquidUpdates.CaptureActiveSnapshot(); var expected = row.GetProperty("queued").EnumerateArray().ToArray();
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < actual.Length; i++)
        {
            Assert.Equal(expected[i].GetProperty("x").GetInt32() * 128 + expected[i].GetProperty("y").GetInt32(), actual[i].TileIndex);
            Assert.Equal(expected[i].GetProperty("delay").GetInt32(), actual[i].Delay); Assert.Equal(expected[i].GetProperty("kill").GetInt32(), actual[i].Kill);
        }
        Assert.Equal(row.GetProperty("netCells").GetArrayLength(), replication.PendingLiquidUpdates);
        // UnifiedRandom's independently recorded physical array and cursor.
        var rng = row.GetProperty("rng");
        var fields = typeof(VanillaUnifiedRandom1458).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        var array = Assert.Single(fields, f => f.FieldType == typeof(int[]));
        Assert.Equal(rng.GetProperty("SeedArray").EnumerateArray().Select(v => v.GetInt32()), Assert.IsType<int[]>(array.GetValue(planned)));
        var cursor = fields.Single(f => f.Name == "inext"); Assert.Equal(rng.GetProperty("inext").GetUInt32(), Assert.IsType<uint>(cursor.GetValue(planned)));
    }

    private static WorldTile Normalize(JsonElement raw)
    {
        int header = raw.GetProperty("sTileHeader").GetInt32(), small = raw.GetProperty("bTileHeader").GetInt32();
        int slope = (header >> 12) & 7; bool half = (header & 1024) != 0;
        return new WorldTile
        {
            Type = raw.GetProperty("type").GetUInt16(),
            Wall = raw.GetProperty("wall").GetUInt16(),
            FrameX = raw.GetProperty("type").GetInt32() == 0 ? raw.GetProperty("frameX").GetInt16() : (short)0,
            FrameY = raw.GetProperty("type").GetInt32() == 0 ? raw.GetProperty("frameY").GetInt16() : (short)0,
            Flags = ((header & 32) != 0 ? WorldTileFlags.Active : 0) | ((header & 64) != 0 ? WorldTileFlags.Inactive : 0) |
                (half && slope > 0 ? WorldTileFlags.HalfBrickWithSlope : 0),
            Shape = (byte)(slope > 0 ? slope + 1 : half ? 1 : 0),
            TileColor = (byte)(header & 31),
            WallColor = (byte)(small & 31),
            LiquidAmount = raw.GetProperty("liquid").GetByte(),
            LiquidKind = (WorldLiquidKind)((small >> 5) & 3)
            // Non-important frame coordinates/frameNumber are cosmetic, not retained
            // WorldTile state. checkingLiquid/skipLiquid belong to the ordered queue.
        };
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void Environment_death_with_owned_low_tile_fact_adopts_terrain_queue_loot_and_rng_before_item_observers(bool expert, bool master, bool lowTiles)
        => RunEnvironmentDeath(expert, master, lowTiles, false);

    [Fact]
    public void Reentrant_duplicate_death_cannot_dispose_the_outer_lava_transaction()
        => RunEnvironmentDeath(true, false, false, true);

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void Actual_strike_routes_adopt_wood_platform_item_and_terrain_with_the_same_death(int route, bool lowTiles)
        => RunEnvironmentDeath(true, false, lowTiles, false, route, true);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Classic_good_world_uses_effective_expert_lava_eligibility(int route)
        => RunEnvironmentDeath(false, false, false, false, route, true, true);

    private static void RunEnvironmentDeath(bool expert, bool master, bool lowTiles, bool reenter, int route = 0, bool platform = false, bool goodWorld = false)
    {
        var tiles = new WorldTileStore(new(128, 128)); Assert.True(tiles.TryAttachWorldSurface(64));
        if (platform) tiles.Set(65, 65, new WorldTile { Type = 19, Flags = WorldTileFlags.Active });
        var npcs = new RuntimeNpcStore(); var observer = new DeathObserver(); var items = new RuntimeWorldItemStore(observer);
        var random = new VanillaUnifiedRandom1458(1458); var replication = new RuntimeTileManipulationReplicationRegistry();
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles, worldItems: items,
            worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random), naturalSpawnRandom: new SystemVanillaNpcRandom(random),
            tileManipulationReplication: replication, expertMode: expert, masterMode: master, skyblockLowTiles: lowTiles,
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { SkyblockWorld = lowTiles, GoodWorld = goodWorld });
        var slots = new PlayerSlotPool(1); Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(924), session.Handle);
        if (route != 0)
        {
            state.Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 400, 400)));
            state.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 62, 62, 0, 0, 0, 0, 0)));
            state.Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, 1, 0, 39, 0)));
        }
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        var pipeline = (RuntimeNpcNetworkCombatPipeline)typeof(NpcAuthority).GetField("combat", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(graph.Npcs)!;
        var simulation = NpcSimulationState.Initial with { MoneyValue = 0, ExtraMoneyValue = 0, HitboxOverride = new(47, 35) };
        Assert.True(npcs.TrySpawnVanilla(new(59, 59, 1024.75f, 1024.25f, 0, 0, 255, new(0, 2, 0, 0), simulation), out var npc));
        bool eligible = expert || lowTiles || goodWorld;
        VanillaUnifiedRandom1458? observedRandom = null; int observed = 0;
        observer.Callback = () =>
        {
            observed++;
            Assert.True(npcs.TryGet(npc.Handle, out var dead)); Assert.Equal(0, dead.Simulation.Life);
            Assert.Equal(eligible ? lowTiles ? 9 : 1 : 0, tiles.LiquidUpdates.ActiveCount);
            Assert.Equal(eligible ? lowTiles ? 9 : 1 : 0, replication.PendingLiquidUpdates);
            if (eligible) Assert.Equal(WorldLiquidKind.Lava, tiles.Get(65, 65).LiquidKind);
            observedRandom ??= random.Clone(); if (eligible) Assert.True(observedRandom.HasSameState(random));
            if (reenter && observed == 1)
            {
                Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, pipeline.TryStrikeEnvironment(npc.Handle, 100000));
                Assert.True(observedRandom.HasSameState(random));
            }
        };
        if (route == 0) Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, pipeline.TryStrikeEnvironment(npc.Handle, 100000));
        else if (route == 1)
        {
            var before = random.Clone();
            var projectile = new ProjectileSnapshot(new(0, new(1)), new(1), new(1), 0, npc.PositionX, npc.PositionY, 1, 0, default, 0, 32767, 0, 32767);
            int published = 0;
            Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, pipeline.TryStrikePreparedProjectile(
                in projectile, in npc, 1, 32767, 0, false, before, before.Clone(), () => true, _ =>
                {
                    published++; Assert.Equal(eligible ? lowTiles ? 9 : 1 : 0, tiles.LiquidUpdates.ActiveCount);
                    Assert.False(tiles.Get(65, 65).IsActive);
                }));
            Assert.Equal(1, published);
        }
        else
        {
            var wire = new TerrariaNpcDamageState(npc.Handle.Slot, RuntimeNpcPacketProjection.ToProtocolGeneration(npc.Handle.Generation), 32767, 0, 2, 0);
            Assert.Equal(RuntimeNpcNetworkDamageResult.Killed, pipeline.TryApply(connection, in wire));
        }
        if (platform)
        {
            Assert.False(tiles.Get(65, 65).IsActive);
            Assert.True(items.TryGetActive(0, out var platformItem)); Assert.Equal(94, platformItem.ItemNetId);
        }
        Assert.False(npcs.TryGet(npc.Handle, out _)); Assert.True(observed > 0);
        if (eligible) Assert.True(observedRandom!.HasSameState(random));
        int count = items.ActiveCount; var after = random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, pipeline.TryStrikeEnvironment(npc.Handle, 100000));
        Assert.True(after.HasSameState(random)); Assert.Equal(count, items.ActiveCount);
    }

    private sealed class DeathObserver : IWorldItemStateCommitSink
    {
        internal Action? Callback;
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot item, int? reservationOffer = null,
            GameCommandSourceId excludedSource = default) => Callback?.Invoke();
    }

    [Fact]
    public void Replaced_tile_after_adoption_does_not_receive_a_stale_platform_kill_notice()
    {
        var tiles = new WorldTileStore(new(128, 128));
        tiles.Set(65, 65, new WorldTile { Type = 19, Flags = WorldTileFlags.Active });
        var replication = new RuntimeTileManipulationReplicationRegistry();
        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(64, 65536, 1024));
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(926), new(new(0), new(1)));
        Assert.True(replication.TryRegister(connection.Source, outbound));
        replication.PlayerSpawned(connection, new(connection.Player.Slot, 62, 62, 0, 0, 0, 0, 0));
        var state = new ServerRuntimeState(worldTiles: tiles, tileManipulationReplication: replication);
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        Assert.True(graph.WorldTileAuthority.TryPrepareLavaSlimeDeath(1024.75f, 1024.25f, 47, 35, false, new(42), out var prepared, _ => true));
        Assert.True(prepared!.TryAdoptUnpublished());
        // Item observers run before the source tile17 notice. An accepted
        // later mutation owns this cell; the old operation must not erase it.
        tiles.Set(65, 65, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        Assert.True(prepared.TryPublishTileKills());
        Assert.Equal(0, outbound.QueuedFrames);
        Assert.False(prepared.TryPublishTileKills());
        Assert.Equal((ushort)1, tiles.Get(65, 65).Type);
    }

    [Theory]
    [InlineData("terrain")]
    [InlineData("queue")]
    [InlineData("unsupported")]
    public void Refused_or_stale_terrain_preparation_never_adopts_a_partial_lava_image(string change)
    {
        var tiles = new WorldTileStore(new(128, 128));
        if (change == "unsupported") tiles.Set(65, 65, new WorldTile { Type = 21, Flags = WorldTileFlags.Active });
        var state = new ServerRuntimeState(worldTiles: tiles);
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        var source = new VanillaUnifiedRandom1458(42); var planned = source.Clone();
        bool accepted = graph.WorldTileAuthority.TryPrepareLavaSlimeDeath(1024.75f, 1024.25f, 47, 35, true, planned, out var preparation);
        if (change == "unsupported") { Assert.False(accepted); Assert.Null(preparation); }
        else
        {
            Assert.True(accepted);
            if (change == "terrain") tiles.Set(64, 64, new WorldTile { Wall = 1 });
            else Assert.True(tiles.LiquidUpdates.TryEnqueue(65, 65));
            Assert.False(preparation!.IsCurrent); Assert.False(preparation.TryAdoptUnpublished());
        }
        Assert.True(source.HasSameState(new(42)));
        foreach (var tile in tiles.Tiles) Assert.Equal(0, tile.LiquidAmount);
    }

    [Fact]
    public void Occupied_item_allocation_rejects_entire_lava_death_then_retry_commits_once()
    {
        var tiles = new WorldTileStore(new(128, 128));
        var npcs = new RuntimeNpcStore(); var items = new RuntimeWorldItemStore(); var random = new VanillaUnifiedRandom1458(42);
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles, worldItems: items, expertMode: true,
            worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random), naturalSpawnRandom: new SystemVanillaNpcRandom(random),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458));
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        var pipeline = (RuntimeNpcNetworkCombatPipeline)typeof(NpcAuthority).GetField("combat", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(graph.Npcs)!;
        Assert.True(npcs.TrySpawnVanilla(new(59, 59, 1024.75f, 1024.25f, 0, 0, 255, new(0, 2, 0, 0),
            NpcSimulationState.Initial with { MoneyValue = 0, ExtraMoneyValue = 0 }), out var npc));
        Assert.True(items.TryReserveDropSlot(out var reservation)); var before = random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, pipeline.TryStrikeEnvironment(npc.Handle, 100000));
        Assert.True(npcs.TryGet(npc.Handle, out var unchanged)); Assert.Equal(npc, unchanged);
        Assert.Equal(0, tiles.LiquidUpdates.ActiveCount); Assert.Equal(0, items.ActiveCount);
        Assert.True(before.HasSameState(random));
        foreach (var tile in tiles.Tiles) Assert.Equal(0, tile.LiquidAmount);
        Assert.True(items.TryReleaseDropReservation(in reservation));
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, pipeline.TryStrikeEnvironment(npc.Handle, 100000));
        Assert.Equal(1, tiles.LiquidUpdates.ActiveCount); Assert.True(items.ActiveCount > 0);
    }

    [Fact]
    public void Adopted_lava_uses_existing_replication_simulation_section_and_checkpoint_owners()
    {
        byte[] original = DurableWorldChestCustody.WorldFixture.CreateCompleteCurrentWorld(128, 128);
        var limits = DurableWorldChestCustody.WorldFixture.CreateLimits() with { MaxTileCount = 128 * 128 };
        Assert.True(WorldFileLoader.TryLoad(original, limits, out var world).IsLoaded); Assert.NotNull(world);
        Assert.True(WorldFilePreservedSections.TryCapture(original, world.Envelope, out var preserved));
        for (int x = 63; x < 69; x++) for (int y = 63; y < 69; y++) world.Tiles.Set(x, y, default);
        var replication = new RuntimeTileManipulationReplicationRegistry();
        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(128, 65536, 1024));
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(925), new(new(0), new(1)));
        Assert.True(replication.TryRegister(connection.Source, outbound));
        replication.PlayerSpawned(connection, new(connection.Player.Slot, 62, 62, 0, 0, 0, 0, 0));
        var state = new ServerRuntimeState(worldTiles: world.Tiles, tileManipulationReplication: replication);
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        Assert.True(graph.WorldTileAuthority.TryPrepareLavaSlimeDeath(1024.75f, 1024.25f, 47, 35, false, new(42), out var prepared));
        Assert.True(prepared!.TryAdoptUnpublished());
        byte amount = world.Tiles.Get(65, 65).LiquidAmount; Assert.InRange(amount, (byte)50, (byte)149);
        Assert.Equal(0, outbound.QueuedFrames); Assert.Equal(1, replication.PendingLiquidUpdates);
        replication.FlushPendingLiquids(); Assert.Equal(1, outbound.QueuedFrames); Assert.Equal(1, replication.EmittedLiquidUpdates);
        Assert.True(world.Tiles.TryCaptureSectionSnapshot(new(0, 0), out var section));
        Assert.Equal(WorldSectionPacketSnapshotCaptureResult.Captured, WorldSectionPacketSnapshotCapture.TryCapture(world, section!, WorldSectionEncodingContext.Capture(world), out var packet));
        Assert.NotNull(packet);
        var snapshots = new RuntimeWorldCheckpointSnapshotSource(world.Tiles, new RuntimeChestStore([]), 8);
        while (!snapshots.IsTileShadowReady) snapshots.CaptureTileBootstrap(8);
        Assert.True(snapshots.TryCapture(out var snapshot)); Assert.NotNull(snapshot);
        // Queue execution must actually move the accepted lava. The checkpoint
        // above remains immutable while the authoritative world continues.
        for (int tick = 0; tick < 20; tick++) graph.WorldTileAuthority.TickLiquids();
        Assert.Contains(Enumerable.Range(66, 62), y => world.Tiles.Get(65, y).LiquidAmount > 0);
        Assert.Equal(WorldSectionPacketSnapshotCaptureResult.StaleTileSnapshot, WorldSectionPacketSnapshotCapture.TryCapture(world, section!, WorldSectionEncodingContext.Capture(world), out _));
        using var target = new MemoryStream();
        Assert.Equal(WorldFileTileChestRewriteResult.Rewritten, WorldFileTileChestRewriter.TryRewrite(
            world.Envelope, world.Header, preserved!.Header.Span, preserved, snapshot.Tiles, snapshot.Chests,
            preserved.Signs.Span, preserved.Npcs.Span, preserved.TownRooms.Span, target, out _));
        Assert.True(WorldFileLoader.TryLoad(target.ToArray(), limits, out var restored).IsLoaded); Assert.NotNull(restored);
        Assert.Equal(amount, restored.Tiles.Get(65, 65).LiquidAmount);
        Assert.Equal(WorldLiquidKind.Lava, restored.Tiles.Get(65, 65).LiquidKind);
        Assert.Equal(0, restored.Tiles.Get(65, 66).LiquidAmount);
    }
}
