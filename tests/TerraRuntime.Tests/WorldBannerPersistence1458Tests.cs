using System.IO.Compression;
using TerraRuntime.Application;
using TerraRuntime.World;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class WorldBannerPersistence1458Tests
{
    [Fact]
    public async Task Owner_checkpoint_rewrites_complete_world_and_restart_continues_credit()
    {
        var header = VanillaFreshWorldHeader326.Create("Banner restart", "source-1458", 128, 96,
            Guid.Parse("28a9bffd-462c-4001-bd85-2167853d7e2b"), 1234);
        var tiles = new WorldTileStore(header.Dimensions);
        var generation = new RuntimeWorldGenerationMetadataSnapshot(new(64, 40), new(12, 55), new(48, 64));
        long time = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc).ToBinary();
        Assert.True(WorldFileFreshComposer326.TryCompose(header, generation, tiles, 0, false, time, time,
            out byte[] file, out var source).Succeeded); Assert.NotNull(source);
        Assert.True(WorldFilePreservedSections.TryCapture(file, source.Envelope, out var preserved)); Assert.NotNull(preserved);
        var owner = new RuntimeNpcDeathPrelude1458(source.RuntimeMetadata.Banners,
            new([new("foreign-save-key", 17)], ["foreign-sight"], ["foreign-chat"]));
        Credit(owner);
        string directory = Path.Combine(Path.GetTempPath(), "terraruntime-banner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); string path = Path.Combine(directory, "world.wld");
        try
        {
            await using var service = new RuntimeWorldCheckpointCoordinator(path, source.Envelope, source.Header,
                preserved, tiles, new RuntimeChestStore([]), synchronizationSectionsPerTick: 1, deathPrelude: owner);
            service.RequestSave(); service.Tick(); Assert.Equal(RuntimeWorldCheckpointTickResult.SaveQueued, service.Tick());
            await service.CompleteAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, service.CaptureStatus().FailedWrites);
            var limits = new WorldFileLoadLimits(128 * 96, 0, 0, 0, 0, new(0, 0, 0, 0, 0, 0), 0, 0, 0,
                new(32, 32, 32, 4096, 16384), new(4096, 16384, 0, 293, 0, 0));
            Assert.True(WorldFileLoader.TryLoad(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken),
                limits, out var saved).IsLoaded); Assert.NotNull(saved);
            // A custom host without a mutable credit owner must preserve the complete existing bestiary.
            byte[] acceptedFile = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            Assert.True(WorldFilePreservedSections.TryCapture(acceptedFile, saved.Envelope, out var acceptedSections));
            Assert.NotNull(acceptedSections);
            await using (var passive = new RuntimeWorldCheckpointCoordinator(path, saved.Envelope, saved.Header,
                acceptedSections, saved.Tiles, new RuntimeChestStore([]), synchronizationSectionsPerTick: 1))
            {
                passive.RequestSave(); passive.Tick(); Assert.Equal(RuntimeWorldCheckpointTickResult.SaveQueued, passive.Tick());
                await passive.CompleteAsync(TestContext.Current.CancellationToken);
                Assert.Equal(0, passive.CaptureStatus().FailedWrites);
            }
            byte[] passiveFile = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            Assert.True(WorldFileLoader.TryLoad(passiveFile, limits, out var passiveSaved).IsLoaded); Assert.NotNull(passiveSaved);
            Assert.True(WorldFilePreservedSections.TryCapture(passiveFile, passiveSaved.Envelope, out var passiveSections));
            Assert.NotNull(passiveSections);
            Assert.Equal(acceptedSections.Bestiary.ToArray(), passiveSections.Bestiary.ToArray());
            Assert.Equal(acceptedSections.Header.ToArray(), passiveSections.Header.ToArray());
            Assert.Equal(saved.Bestiary.Kills, passiveSaved.Bestiary.Kills);
            Assert.Equal(saved.Bestiary.Sightings, passiveSaved.Bestiary.Sightings);
            Assert.Equal(saved.Bestiary.Chats, passiveSaved.Bestiary.Chats);
            var restarted = new RuntimeNpcDeathPrelude1458(saved.RuntimeMetadata.Banners, saved.Bestiary);
            Assert.Equal(owner.CaptureBanners().KillCounts, restarted.CaptureBanners().KillCounts);
            Assert.Equal(owner.CaptureBestiary().Kills, restarted.CaptureBestiary().Kills);
            Assert.Equal("foreign-sight", Assert.Single(restarted.CaptureBestiary().Sightings));
            Credit(restarted);
            Assert.Equal(2, restarted.CaptureBanners().KillCounts.Sum());
            Assert.Contains(restarted.CaptureBestiary().Kills, static entry => entry.PersistentId == "BlueSlime" && entry.KillCount == 2);
            Assert.Contains(restarted.CaptureBestiary().Kills, static entry => entry.PersistentId == "foreign-save-key" && entry.KillCount == 17);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Credit(RuntimeNpcDeathPrelude1458 owner)
    {
        var preview = owner.CreatePreview(); var npc = new NpcSnapshot(new(0, new(1)), new(1), 1, 1,
            100, 100, 0, 0, 0, default, NpcSimulationState.Initial);
        var context = new RuntimeNpcDeathPreludeContext1458(true, false, false, false, false, true, false,
            false, default, HasOwnInteractions: true);
        Assert.True(preview.TryApply(npc, context, new VanillaUnifiedRandom1458(42), out bool allow)); Assert.True(allow);
        Assert.True(owner.TryPublish(preview, owner.Revision));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(293)]
    public void Source_header_banner_replacement_preserves_foreign_bytes_and_reloads_variable_lengths(int count)
    {
        byte[] original = OfficialHeader(); var sourceEnvelope = Envelope(original.Length);
        Assert.Equal(WorldFileHeaderParseResult.Parsed, WorldFileHeaderParser.TryParse(original, sourceEnvelope, out var header));
        Assert.NotNull(header);
        Assert.Equal(WorldFileRuntimeMetadataParseResult.Parsed,
            WorldFileRuntimeMetadataParser.TryParse(original, sourceEnvelope, header, Limits(), out var source, out _));
        Assert.NotNull(source);
        int[] kills = new int[count]; ushort[] claims = new ushort[count];
        for (int i = 0; i < count; i++) { kills[i] = i * 73; claims[i] = (ushort)(i * 17); }
        var state = new WorldBannerData1458(kills, claims);
        Assert.True(WorldFileBannerHeaderPatcher1458.TryPatch(original, header, state, out byte[] patched));
        Assert.Equal(original.AsSpan(0, source.BannerSectionOffset).ToArray(), patched.AsSpan(0, source.BannerSectionOffset).ToArray());
        int tail = source.BannerSectionOffset + source.BannerSectionLength;
        Assert.Equal(original.AsSpan(tail).ToArray(), patched.AsSpan(source.BannerSectionOffset + state.Encode().Length).ToArray());
        Assert.Equal(WorldFileRuntimeMetadataParseResult.Parsed,
            WorldFileRuntimeMetadataParser.TryParse(patched, Envelope(patched.Length), header, Limits(), out var loaded, out _));
        Assert.NotNull(loaded); Assert.Equal(kills, loaded.Banners.KillCounts); Assert.Equal(claims, loaded.Banners.ClaimableCounts);
        var tiles = new WorldTileStore(header.Dimensions);
        var world = new WorldFileData(Envelope(patched.Length), header, loaded, tiles, [], [], new([], [], []),
            [], [], [], new([], [], []), new(false, 0, false, false, 0, false));
        Assert.True(RuntimeWorldPreparedStateCodec.TryDecode(RuntimeWorldPreparedStateCodec.Encode(world), tiles, out var cached));
        Assert.NotNull(cached); Assert.Equal(kills, cached.RuntimeMetadata.Banners.KillCounts);
        Assert.Equal(claims, cached.RuntimeMetadata.Banners.ClaimableCounts);
        Assert.Equal(loaded.BannerSectionOffset, cached.RuntimeMetadata.BannerSectionOffset);
        Assert.Equal(loaded.BannerSectionLength, cached.RuntimeMetadata.BannerSectionLength);
        var restarted = new RuntimeNpcDeathPrelude1458(loaded.Banners);
        Assert.Equal(kills, restarted.CaptureBanners().KillCounts.Take(count));
        Assert.Equal(claims, restarted.CaptureBanners().ClaimableCounts.Take(count));
        Assert.True(WorldFileBannerHeaderPatcher1458.TryPatch(patched, header, source.Banners, out byte[] restored));
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Unknown_loaded_bestiary_keys_survive_capture_without_invented_wire_identity()
    {
        var owner = new RuntimeNpcDeathPrelude1458(bestiary: new([new("foreign-world-key", 42), new("BlueSlime", 7)],
            ["foreign-sight", "BlueSlime"], ["foreign-chat", "Guide"]));
        var captured = owner.CaptureBestiary(); Assert.Equal("foreign-world-key", captured.Kills[0].PersistentId);
        Assert.Equal(4, owner.CaptureJoinFrames().Length); // Banner + one recognized entry from each tracker.
        captured.Kills[0] = new("tampered", 0); captured.Sightings[0] = "tampered";
        Assert.Equal("foreign-world-key", owner.CaptureBestiary().Kills[0].PersistentId);
        Assert.Equal("foreign-sight", owner.CaptureBestiary().Sightings[0]);
    }
    private static WorldFileRuntimeMetadataLimits Limits() => new(4096, 16384, 64, 293, 64, 16384);
    private static WorldFileEnvelope Envelope(int length) => new(WorldFileFormatPolicy.CurrentVersion, 1, 0,
        [0, length], VanillaWorldFrameImportance326.Count, VanillaWorldFrameImportance326.CopyPackedBits());
    private static byte[] OfficialHeader()
    {
        using var resource = typeof(WorldBannerPersistence1458Tests).Assembly.GetManifestResourceStream("LunarEventHeaders1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress); using var reader = new BinaryReader(gzip);
        return reader.ReadBytes(reader.ReadInt32());
    }
}
