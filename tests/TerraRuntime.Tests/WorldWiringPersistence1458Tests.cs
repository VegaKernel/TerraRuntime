using System.Reflection;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldWiringPersistence1458Tests
{
    [Fact]
    public async Task Authorized_switch_effects_survive_real_checkpoint_and_reload_without_live_timer_clocks()
    {
        byte[] canonical = CreateWorld(out WorldFileLoadLimits limits);
        Assert.True(WorldFileLoader.TryLoad(canonical, limits, out var loaded).IsLoaded);
        WorldFileData world = Assert.IsType<WorldFileData>(loaded);
        ConfigureCircuit(world.Tiles);
        using var player = new SpawnedPlayer(world.Tiles);
        player.State.Apply(new ClientHitSwitchRuntimeCommand1458(player.Connection, new(20, 20)));
        AssertCircuit(world.Tiles, timerFrame: 18);
        Assert.True(WorldFilePreservedSections.TryCapture(canonical, world.Envelope, out var preserved));
        string directory = Path.Combine(Path.GetTempPath(), $"TerraRuntime-WiringSave-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "world.wld");
        var coordinator = new RuntimeWorldCheckpointCoordinator(path, world.Envelope, world.Header, preserved!,
            world.Tiles, new RuntimeChestStore(world.Chests), synchronizationSectionsPerTick: 1,
            checkpointValidationLimits: limits);
        try
        {
            coordinator.CaptureFinalSaveAfterOwnerStopped();
            await coordinator.CompleteAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, coordinator.CaptureStatus().CompletedWrites);
            byte[] saved = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            Assert.True(WorldFileLoader.TryLoad(saved, limits, out var restored).IsLoaded);
            AssertCircuit(Assert.IsType<WorldFileData>(restored).Tiles, timerFrame: 0);
            Assert.True(RuntimeWorldSnapshotCache.TryLoadValidatedSource(RuntimeWorldSnapshotCache.GetCachePath(path),
                path, limits, out var cached).IsLoaded);
            WorldTileStore reloaded = Assert.IsType<WorldFileData>(cached).Tiles;
            AssertCircuit(reloaded, timerFrame: 0);
            using var newPlayer = new SpawnedPlayer(reloaded);
            for (int tick = 0; tick < 65; tick++) newPlayer.State.Tick();
            AssertCircuit(reloaded, timerFrame: 0); // No registration survives either load path.
            newPlayer.State.Apply(new ClientHitSwitchRuntimeCommand1458(newPlayer.Connection, new(20, 20)));
            Assert.Equal((short)44, reloaded.Get(21, 20).FrameX);
            Assert.False(reloaded.Get(22, 20).IsActuated);
            Assert.Equal((short)18, reloaded.Get(24, 20).FrameY);
            AssertCircuit(world.Tiles, timerFrame: 18); // New world does not retain the old store.
        }
        finally
        {
            await coordinator.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Official_timer_save_bytes_preserve_live_frame_but_both_load_paths_reset_only_the_clock_frame()
    {
        // Actual unmodified official Linux-edition WorldFile.SaveWorldTiles and LoadWorldTiles under
        // Windows CoreCLR, binary SHA4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034.
        var timerTiles = new WorldTileStore(new WorldDimensions(1, 1));
        var timer = new WorldTile { Type = 144, FrameX = 54, FrameY = 18,
            Flags = WorldTileFlags.Active | WorldTileFlagMasks.Wires | WorldTileFlags.Actuator | WorldTileFlags.Inactive };
        timerTiles.Set(0, 0, in timer);
        byte[] importance = Importance();
        using var payload = new MemoryStream();
        Assert.Equal(WorldFileTileEncodeResult.Encoded, WorldFileTileEncoder.TryEncode(timerTiles,
            VanillaWorldFormat326.TileTypeCount, importance, payload, out _));
        Assert.Equal(Convert.FromBase64String("Aw8mkDYAEgA="), payload.ToArray());

        byte[] canonical = CreateWorld(out var limits);
        Assert.True(WorldFileLoader.TryLoad(canonical, limits, out var loaded).IsLoaded);
        WorldFileData world = Assert.IsType<WorldFileData>(loaded);
        ConfigureCircuit(world.Tiles);
        using var player = new SpawnedPlayer(world.Tiles);
        player.State.Apply(new ClientHitSwitchRuntimeCommand1458(player.Connection, new(20, 20)));
        world.Tiles.MarkPostLoadLiquidPrepared();
        string path = Path.Combine(Path.GetTempPath(), $"TerraRuntime-WiringCache-{Guid.NewGuid():N}.runtime-world");
        var stamp = new RuntimeWorldSourceStamp(canonical.LongLength, DateTime.UtcNow.Ticks);
        try
        {
            Assert.True(RuntimeWorldSnapshotCache.TryWriteAtomic(path, canonical, stamp, world).IsWritten);
            Assert.True(RuntimeWorldSnapshotCache.TryLoad(path, stamp, limits, out var cached).IsLoaded);
            WorldTileStore tiles = Assert.IsType<WorldFileData>(cached).Tiles;
            AssertCircuit(tiles, timerFrame: 0);
            Assert.Equal(0, tiles.DirtySections.DirtyCount);
            Assert.Equal(0, tiles.PersistenceDirtySections.DirtyCount);
            AssertCircuit(world.Tiles, timerFrame: 18);
            using var resumed = new SpawnedPlayer(tiles);
            resumed.State.Apply(new ClientHitSwitchRuntimeCommand1458(resumed.Connection, new(24, 20)));
            for (int tick = 0; tick < 59; tick++) resumed.State.Tick();
            Assert.Equal((short)110, tiles.Get(21, 20).FrameX);
            resumed.State.Tick();
            Assert.Equal((short)44, tiles.Get(21, 20).FrameX);
        }
        finally { File.Delete(path); File.Delete(path + ".tmp"); }
    }

    private static void ConfigureCircuit(WorldTileStore tiles)
    {
        tiles.Set(20, 20, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        tiles.Set(21, 20, new WorldTile { Type = 4, FrameX = 44, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        tiles.Set(22, 20, new WorldTile { Type = 1, Flags = WorldTileFlags.Active | WorldTileFlagMasks.Wires | WorldTileFlags.Actuator });
        tiles.Set(23, 20, new WorldTile { Flags = WorldTileFlags.WireRed });
        tiles.Set(24, 20, new WorldTile { Type = 144, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
    }

    private static void AssertCircuit(WorldTileStore tiles, short timerFrame)
    {
        Assert.Equal((short)18, tiles.Get(20, 20).FrameY);
        Assert.Equal((short)110, tiles.Get(21, 20).FrameX);
        Assert.True(tiles.Get(22, 20).IsActuated);
        Assert.True(tiles.Get(22, 20).HasActuator);
        Assert.Equal(WorldTileFlagMasks.Wires, tiles.Get(22, 20).Flags & WorldTileFlagMasks.Wires);
        Assert.Equal(timerFrame, tiles.Get(24, 20).FrameY);
    }

    private static byte[] Importance()
    {
        var importance = new byte[(VanillaWorldFormat326.TileTypeCount + 7) / 8];
        foreach (int type in new[] { 4, 136, 144 }) importance[type >> 3] |= (byte)(1 << (type & 7));
        return importance;
    }

    private static byte[] CreateWorld(out WorldFileLoadLimits limits)
    {
        const BindingFlags binding = BindingFlags.NonPublic | BindingFlags.Static;
        byte[] original = (byte[])typeof(WorldFileLoaderTests).GetMethod("CreateCompleteCurrentWorld", binding)!.Invoke(null, null)!;
        limits = (WorldFileLoadLimits)typeof(WorldFileLoaderTests).GetMethod("CreateLimits", binding)!.Invoke(null, null)!;
        limits = limits with { MaxTileCount = 8_000 };
        Assert.Equal(WorldFileEnvelopeParseResult.Parsed, WorldFileEnvelopeParser.TryParse(original, out var oldEnvelope, out _));
        byte[] header = (byte[])typeof(WorldFileLoaderTests).GetMethod("CreateHeader", binding)!.Invoke(null, ["full-loader", 77, 80, 100])!;
        byte[] importance = Importance();
        using var tilePayload = new MemoryStream();
        Assert.Equal(WorldFileTileEncodeResult.Encoded, WorldFileTileEncoder.TryEncode(new WorldTileStore(new WorldDimensions(80, 100)),
            VanillaWorldFormat326.TileTypeCount, importance, tilePayload, out _));
        int[] offsets = new int[VanillaWorldFormat326.SectionCount];
        offsets[0] = WorldFileEnvelopeEncoder.CurrentEncodedLength;
        offsets[1] = offsets[0] + header.Length;
        offsets[2] = offsets[1] + (int)tilePayload.Length;
        for (int section = 3; section < offsets.Length; section++)
            offsets[section] = offsets[section - 1] + oldEnvelope!.SectionOffsets[section] - oldEnvelope.SectionOffsets[section - 1];
        var envelope = new WorldFileEnvelope(WorldFileFormatPolicy.CurrentVersion, 1, 0, offsets,
            VanillaWorldFormat326.TileTypeCount, importance);
        using var file = new MemoryStream();
        Assert.Equal(WorldFileEnvelopeEncodeResult.Encoded, WorldFileEnvelopeEncoder.TryEncode(envelope, file, out _));
        file.Write(header); tilePayload.Position = 0; tilePayload.CopyTo(file);
        file.Write(original.AsSpan(oldEnvelope!.SectionOffsets[2]));
        return file.ToArray();
    }

    private sealed class SpawnedPlayer : IDisposable
    {
        private readonly PlayerJoinSession session;
        public SpawnedPlayer(WorldTileStore tiles)
        {
            var sourceWorld = new RuntimePlayerUpdateWorld1458(tiles.Dimensions.WidthTiles, tiles.Dimensions.HeightTiles, 60, false, false)
            {
                GetGoodWorld = false, InfectedSeed = false, NotTheBeesWorld = false, NoTrapsWorld = false,
                TenthAnniversaryWorld = false, DontStarveWorld = false, DrunkWorld = false,
                DualDungeonsSeed = false, JourneyMode = false, HallowBackground = 0, OnlyShimmerOceanWorlds = false
            };
            State = new ServerRuntimeState(worldTiles: tiles, sourcePlayerWorld: sourceWorld,
                worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 60 },
                townSpawnWorldFacts: default(TerraRuntime.Gameplay.Npcs.VanillaTownSpawnWorldFacts1458));
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
            Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
            Connection = new(GameCommandSourceId.FromConnection(1458), session.Handle);
            State.Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 100, 100)));
            State.Apply(new PlayerEquipmentRuntimeCommand(Connection, new(session.Slot, 0, 0, 0, 0, 0)));
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 20, 20, 0, 0, 0, 0, 0)));
            Assert.Equal(PlayerSpawnCommitResult.Committed, State.LastSpawnCommitResult);
        }
        public ServerRuntimeState State { get; }
        public ConnectionHandle Connection { get; }
        public void Dispose() => session.Dispose();
    }
}
