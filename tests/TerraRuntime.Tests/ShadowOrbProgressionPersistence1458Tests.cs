using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ShadowOrbProgressionPersistence1458Tests
{
    [Fact]
    public void Existing_journal_mutation_matches_independent_original_shadow_orb_header_byte()
    {
        using var resource = typeof(ShadowOrbProgressionPersistence1458Tests).Assembly
            .GetManifestResourceStream("MoonSeasonHeader1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var reference = JsonDocument.Parse(gzip);
        Assert.Equal("4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034",
            reference.RootElement.GetProperty("source").GetString());
        Assert.Equal(0, reference.RootElement.GetProperty("alteredMethodBodies").GetInt32());
        var rows = reference.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(64, rows.Length);
        foreach (var row in rows)
        foreach (bool mark in new[] { false, true })
        {
            byte[] before = Convert.FromBase64String(row.GetProperty("headerBase64").GetString()!);
            var envelope = new WorldFileEnvelope(326, 0, 0, [0, before.Length], 0, []);
            Assert.Equal(WorldFileHeaderParseResult.Parsed,
                WorldFileHeaderParser.TryParse(before, envelope, out var header));
            int shadow = mark ? 1 : row.GetProperty("shadow").GetInt32();
            var expected = rows.Single(candidate => candidate.GetProperty("shadow").GetInt32() == shadow &&
                candidate.GetProperty("variable").GetInt32() == row.GetProperty("variable").GetInt32() &&
                candidate.GetProperty("forever").GetInt32() == row.GetProperty("forever").GetInt32() &&
                candidate.GetProperty("today").GetInt32() == row.GetProperty("today").GetInt32());
            var mutation = new RuntimeWorldProgressionMutationSnapshot(mark
                ? 1UL << (int)VanillaWorldProgressionId.ShadowOrbSmashed : 0UL);
            Assert.Equal(WorldFileProgressionHeaderPatchResult.Patched,
                WorldFileProgressionHeaderPatcher.TryPatch(before, header!, in mutation, out var actual));
            Assert.Equal(Convert.FromBase64String(expected.GetProperty("headerBase64").GetString()!), actual);
            Assert.Equal(mark && row.GetProperty("shadow").GetInt32() == 0 ? 1 : 0,
                before.Zip(actual).Count(pair => pair.First != pair.Second));
            Assert.Equal(WorldFileProgressionHeaderPatchResult.Patched,
                WorldFileProgressionHeaderPatcher.TryPatch(actual, header!, in mutation, out var repeated));
            Assert.Equal(actual, repeated);
            Assert.Equal(Convert.FromBase64String(row.GetProperty("headerBase64").GetString()!), before);
        }
    }

    [Fact]
    public async Task Accepted_shadow_orb_journal_survives_detached_checkpoint_restart_and_prepared_cache()
    {
        // The existing Skyblock Eater death integration proves this journal producer.
        // Its accepted flag previously made the save owner return UnsupportedMutation.
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        byte[] bytes = (byte[])typeof(WorldFileLoaderTests).GetMethod("CreateCompleteCurrentWorld", flags)!.Invoke(null, null)!;
        var limits = (WorldFileLoadLimits)typeof(WorldFileLoaderTests).GetMethod("CreateLimits", flags)!.Invoke(null, null)!;
        Assert.True(WorldFileLoader.TryLoad(bytes, limits, out var world).IsLoaded);
        Assert.False(world!.RuntimeMetadata.ShadowOrbSmashed);
        Assert.True(WorldFilePreservedSections.TryCapture(bytes, world.Envelope, out var preserved));
        var journal = new RuntimeWorldProgressionMutations();
        var source = new RuntimeWorldCheckpointSnapshotSource(world.Tiles, new RuntimeChestStore(world.Chests), 8,
            progressionMutations: journal);
        source.CaptureTileBootstrap(8);
        Assert.True(source.TryCapture(out var before));
        Assert.True(journal.MarkCompleted(VanillaWorldProgressionId.ShadowOrbSmashed));
        Assert.True(source.TryCapture(out var accepted));
        Assert.True(journal.MarkCompleted(VanillaWorldProgressionId.PirateInvasion));
        async Task<WorldFileData> Save(RuntimeWorldCheckpointSnapshot snapshot)
        {
            using var stream = new MemoryStream();
            await (Task)typeof(RuntimeWorldCheckpointCoordinator).GetMethod("SerializeAsync", flags)!
                .Invoke(null, [world.Envelope, world.Header, preserved!, snapshot, stream, CancellationToken.None])!;
            Assert.True(WorldFileLoader.TryLoad(stream.ToArray(), limits, out var saved).IsLoaded);
            return saved!;
        }
        Assert.False((await Save(before!)).RuntimeMetadata.ShadowOrbSmashed);
        var reloaded = await Save(accepted!);
        Assert.True(reloaded.RuntimeMetadata.ShadowOrbSmashed);
        Assert.True(reloaded.RuntimeMetadata.Progression.IsComplete(VanillaWorldProgressionId.ShadowOrbSmashed));
        Assert.False(reloaded.RuntimeMetadata.DownedPirates); // Later owner writes do not leak into the snapshot.
        Assert.Equal(1, WorldInfoPacketMapper.Create(reloaded.Header, reloaded.RuntimeMetadata).EventInfo & 1);
        Assert.True(RuntimeWorldPreparedStateCodec.TryDecode(RuntimeWorldPreparedStateCodec.Encode(reloaded),
            reloaded.Tiles, out var cached));
        Assert.True(cached!.RuntimeMetadata.ShadowOrbSmashed);
        Assert.True(cached.RuntimeMetadata.Progression.IsComplete(VanillaWorldProgressionId.ShadowOrbSmashed));
    }
}
