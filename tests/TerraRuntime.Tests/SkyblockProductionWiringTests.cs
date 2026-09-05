using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SkyblockProductionWiringTests
{
    [Fact]
    public void Skyblock_bootstrap_world_info_reflects_low_tiles_and_guide_persists()
    {
        var request = new WorldGenerationRequest(
            SkyblockProvider.GeneratorId,
            "SkyblockWiring",
            Seed: 0x5A17B10CUL,
            WidthTiles: 512,
            HeightTiles: 256);
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);
        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Generation.Execution?.Error?.ToString());
        Workspace candidate = Assert.IsType<Workspace>(result.Candidate);

        WorldNpcPersistence npcs = candidate.CaptureGeneratedNpcs();
        WorldTownNpc guide = Assert.Single(npcs.TownNpcs);
        Assert.Equal(22, guide.NetId);
        Assert.Equal("Andrew", guide.GivenName);
        Assert.Equal(result.Metadata.Spawn.X * 16f, guide.X);
        Assert.Equal(result.Metadata.Spawn.Y * 16f, guide.Y);

        var oreTypes = new HashSet<ushort> { 7, 6, 9, 8 };
        bool hasOre = false;
        for (int x = 0; x < candidate.WidthTiles; x++)
        {
            for (int y = 0; y < candidate.HeightTiles; y++)
            {
                Assert.True(candidate.TryGetTile(x, y, out WorldGenerationTile tile));
                if ((tile.Flags & WorldGenerationTileFlags.Active) != 0 && oreTypes.Contains(tile.Type))
                {
                    hasOre = true;
                    break;
                }
            }
            if (hasOre) break;
        }
        Assert.True(hasOre, "Skyblock world should contain at least one ore tier cluster.");

        string directory = Path.Combine(Path.GetTempPath(), "TerraRuntime.Tests", Guid.NewGuid().ToString("N"));
        string worldPath = Path.Combine(directory, "skyblock-wiring.wld");
        var persistPipeline = new RuntimeWorldCreationPersistencePipeline(
            new StartupWorldGeneratorSource(host: null),
            maxTileCount: 32_000_000);
        long timestamp = new DateTime(2026, 8, 30, 10, 0, 0, DateTimeKind.Utc).ToBinary();
        try
        {
            RuntimeWorldCreationPersistenceResult creation = persistPipeline.TryCreateAndPersist(
                request,
                worldPath,
                Guid.Parse("2eb98abe-dd68-4a52-af67-e43a84f37011"),
                worldId: 246813580,
                creationTimeBinary: timestamp,
                lastPlayedBinary: timestamp,
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(creation.Succeeded, creation.ToString());
            byte[] bytes = File.ReadAllBytes(worldPath);
            WorldFileLoadDiagnostic load = WorldFileLoader.TryLoad(
                bytes,
                new WorldFileLoadLimits(
                    MaxTileCount: 512L * 256L,
                    MaxItemsPerChest: WorldGenerationChestRules.VanillaItemSlotCount,
                    MaxTotalChestItems: (long)VanillaWorldFormat326.MaximumChestSlots * WorldGenerationChestRules.VanillaItemSlotCount,
                    MaxTextBytesPerSign: 0,
                    MaxTotalSignTextBytes: 0,
                    Npcs: new WorldFileNpcDecodeOptions(1, 2, 1, 1, 64, 64),
                    MaxTileEntities: 0,
                    MaxPressurePlates: 0,
                    MaxTownRooms: 0,
                    Bestiary: new WorldFileBestiaryLimits(0, 0, 0, 0, 0),
                    RuntimeMetadata: new WorldFileRuntimeMetadataLimits(4096, 12288, 0, 0, 0, 0)),
                out WorldFileData? world);
            Assert.True(load.IsLoaded, load.ToString());
            Assert.NotNull(world);
            Assert.True(world!.RuntimeMetadata.SkyblockWorld);
            Assert.Single(world.Npcs.TownNpcs);
            Assert.Equal(22, world.Npcs.TownNpcs[0].NetId);

            VanillaSkyblockRuntimeState1458 state = VanillaSkyblockRuntimePolicy1458.Evaluate(world);
            Assert.True(state.LowTiles);
            PlayerBootstrapPacketSet bootstrap = PlayerBootstrapPacketSet.Create(world);
            ReadOnlySpan<byte> frame = bootstrap.WorldInfoFrame.Span;
            Assert.True(frame.Length >= 3);
            Assert.Equal((byte)TerrariaMessageId.WorldData, frame[2]);
            ReadOnlySpan<byte> payload = frame.Slice(3);
            var packet = (global::Multiplicity.Packets.WorldInfo)global::Multiplicity.Packets.TerrariaPacket.DeserializePayload(
                global::Multiplicity.Packets.PacketTypes.WorldInfo, payload.ToArray());
            Assert.True((packet.EventInfo11 & 0x01) != 0, "WorldInfo EventInfo11 lowTiles bit must be set for fresh skyblock.");

            WorldChest starter = Assert.Single(world.Chests, c => c.Name == "Skyblock Starter");
            Assert.Contains(starter.Items, i => i.ItemType == VanillaItemIds.StoneBlock.Value);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Vanilla_skyblock_fallback_produces_valid_nonempty_world()
    {
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "VanillaSkyblockFallback",
            Seed: 12345,
            WidthTiles: 640,
            HeightTiles: 240)
        {
            SeedText = "skyblock"
        };
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);
        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(in request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.Generation.Execution?.Error?.ToString());
        Assert.NotNull(result.Candidate);
        int active = 0;
        for (int x = 0; x < result.Candidate!.WidthTiles; x++)
            for (int y = 0; y < result.Candidate.HeightTiles; y++)
                if (result.Candidate.TryGetTile(x, y, out WorldGenerationTile tile) && (tile.Flags & WorldGenerationTileFlags.Active) != 0)
                    active++;
        Assert.True(active > 100);
        Assert.True(result.Metadata.Spawn.X > 0 && result.Metadata.Spawn.X < 640);
    }
}
