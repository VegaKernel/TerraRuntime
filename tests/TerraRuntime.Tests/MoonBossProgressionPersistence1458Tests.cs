using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MoonBossProgressionPersistence1458Tests
{
    private static readonly VanillaWorldProgressionId[] Milestones =
    [VanillaWorldProgressionId.Pumpking, VanillaWorldProgressionId.MourningWood,
        VanillaWorldProgressionId.IceQueen, VanillaWorldProgressionId.SantaNk1, VanillaWorldProgressionId.Everscream];

    [Fact]
    public void Every_moon_flag_combination_matches_independent_original_headers_and_preserves_unrelated_bytes()
    {
        using var reference = LoadHeaders();
        var rows = reference.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(64, rows.Length);
        foreach (var row in rows)
        {
            byte[] raw = Convert.FromBase64String(row.GetProperty("headerBase64").GetString()!);
            Assert.Equal(row.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(raw)));
            var (header, metadata) = Parse(raw);
            int originalMask = row.GetProperty("mask").GetInt32();
            var facts = RuntimeTownCommerceWorldFacts1458.FromMetadata(metadata);
            Assert.True(facts.ProgressionBaseline.HasValue);
            for (int index = 0; index < Milestones.Length; index++)
                Assert.Equal((originalMask & (1 << index)) != 0,
                    facts.ProgressionBaseline!.Value.IsComplete(Milestones[index]));
            for (int mutationMask = 0; mutationMask < 32; mutationMask++)
            {
                var mutation = new RuntimeWorldProgressionMutationSnapshot(Mask(mutationMask));
                var expected = rows.Single(candidate => candidate.GetProperty("variable").GetInt32() ==
                    row.GetProperty("variable").GetInt32() && candidate.GetProperty("mask").GetInt32() == (originalMask | mutationMask));
                byte[] golden = Convert.FromBase64String(expected.GetProperty("headerBase64").GetString()!);
                Assert.Equal(WorldFileProgressionHeaderPatchResult.Patched,
                    WorldFileProgressionHeaderPatcher.TryPatch(raw, header, in mutation, out var actual));
                Assert.Equal(golden, actual);
                Assert.Equal(WorldFileProgressionHeaderPatchResult.Patched,
                    WorldFileProgressionHeaderPatcher.TryPatch(actual, header, in mutation, out var repeated));
                Assert.Equal(actual, repeated);
                Assert.Equal(Convert.FromBase64String(row.GetProperty("headerBase64").GetString()!), raw);
                var (_, parsed) = Parse(actual);
                for (int index = 0; index < Milestones.Length; index++)
                    Assert.Equal(((originalMask | mutationMask) & (1 << index)) != 0,
                        parsed.Progression.IsComplete(Milestones[index]));
            }
        }
    }

    [Fact]
    public void Retained_milestone_adoption_preserves_all_facts_and_rejects_every_stale_journal_kind()
    {
        foreach (var milestone in Milestones)
        foreach (bool mark in new[] { false, true })
        {
            var journal = InitialJournal();
            var before = journal.CaptureSnapshot();
            Assert.True(journal.CanAdoptCompletedMilestone(in before, milestone, mark));
            Assert.True(journal.TryAdoptCompletedMilestone(in before, milestone, mark));
            Assert.Equal(before with { CompletedMask = before.CompletedMask | (mark ? 1UL << (int)milestone : 0) },
                journal.CaptureSnapshot());
        }
        Action<RuntimeWorldProgressionMutations>[] mutations =
        [journal => journal.MarkCompleted(VanillaWorldProgressionId.PirateInvasion),
            journal => journal.MarkSlimeBlueSpawnUnlocked(), journal => journal.MarkTruffleSpawnUnlocked(),
            journal => journal.MarkSlimeYellowSpawnUnlocked(), journal => journal.MarkTownNpcRescued(RuntimeTownRescueFacts1458.Bartender),
            journal => journal.SetLunarApocalypseIsUp(false)];
        foreach (var mutate in mutations)
        foreach (var milestone in Milestones)
        {
            var journal = InitialJournal();
            var before = journal.CaptureSnapshot();
            mutate(journal);
            var newer = journal.CaptureSnapshot();
            Assert.NotEqual(before, newer);
            Assert.False(journal.CanAdoptCompletedMilestone(in before, milestone, true));
            Assert.False(journal.TryAdoptCompletedMilestone(in before, milestone, true));
            Assert.Equal(newer, journal.CaptureSnapshot());
        }
        foreach (var unsupported in new[] { VanillaWorldProgressionId.KingSlime, (VanillaWorldProgressionId)255 })
        {
            var journal = InitialJournal();
            var before = journal.CaptureSnapshot();
            Assert.False(journal.TryAdoptCompletedMilestone(in before, unsupported, true));
            Assert.Equal(before, journal.CaptureSnapshot());
        }
    }

    [Fact]
    public async Task Detached_five_boss_checkpoint_preserves_milestones_lantern_and_seasons_across_cold_restart()
    {
        const BindingFlags binding = BindingFlags.Static | BindingFlags.NonPublic;
        byte[] bytes = (byte[])typeof(WorldFileLoaderTests).GetMethod("CreateCompleteCurrentWorld", binding)!.Invoke(null, null)!;
        var limits = (WorldFileLoadLimits)typeof(WorldFileLoaderTests).GetMethod("CreateLimits", binding)!.Invoke(null, null)!;
        Assert.True(WorldFileLoader.TryLoad(bytes, limits, out var world).IsLoaded);
        Assert.True(WorldFilePreservedSections.TryCapture(bytes, world!.Envelope, out var preserved));
        var journal = new RuntimeWorldProgressionMutations();
        var invasion = new RuntimeWorldInvasion1458(default(InvasionState1458));
        var clock = new RuntimeWorldClock(123, true, 0, 0, 0, forceHalloweenForToday: true,
            forceXMasForToday: true, forceHalloweenForever: false, forceXMasForever: false);
        var source = new RuntimeWorldCheckpointSnapshotSource(world.Tiles, new RuntimeChestStore(world.Chests), 8,
            clock, progressionMutations: journal, invasionSaveStateSource: invasion.CaptureSaveState);
        source.CaptureTileBootstrap(8);
        foreach (var milestone in Milestones)
        {
            var before = journal.CaptureSnapshot();
            Assert.True(journal.TryAdoptCompletedMilestone(in before, milestone, true));
        }
        Assert.True(invasion.TryCapture(out var initial));
        var transition = new InvasionTransition1458(initial.State with { LanternNextNight = true }, default);
        Assert.True(invasion.TryAdopt(in initial, in transition, out _));
        Assert.True(source.TryCapture(out var snapshot));
        journal.MarkCompleted(VanillaWorldProgressionId.KingSlime);
        using var stream = new MemoryStream();
        await (Task)typeof(RuntimeWorldCheckpointCoordinator).GetMethod("SerializeAsync", binding)!
            .Invoke(null, [world.Envelope, world.Header, preserved!, snapshot, stream, CancellationToken.None])!;
        Assert.True(WorldFileLoader.TryLoad(stream.ToArray(), limits, out var saved).IsLoaded);
        foreach (var milestone in Milestones) Assert.True(saved!.RuntimeMetadata.Progression.IsComplete(milestone));
        Assert.False(saved!.RuntimeMetadata.Progression.IsComplete(VanillaWorldProgressionId.KingSlime));
        Assert.True(saved.RuntimeMetadata.LanternNightNextNight);
        Assert.True(saved.RuntimeMetadata.ForceHalloweenForToday);
        Assert.True(saved.RuntimeMetadata.ForceXMasForToday);
        Assert.Equal(0x3e, WorldInfoPacketMapper.Create(saved).EventInfo4 & 0x3e);
        Assert.True(RuntimeWorldPreparedStateCodec.TryDecode(RuntimeWorldPreparedStateCodec.Encode(saved), saved.Tiles, out var cached));
        foreach (var milestone in Milestones) Assert.True(cached!.RuntimeMetadata.Progression.IsComplete(milestone));
        Assert.True(cached!.RuntimeMetadata.LanternNightNextNight);
        Assert.True(RuntimeTownCommerceWorldFacts1458.FromMetadata(cached.RuntimeMetadata).ProgressionBaseline!.Value
            .IsComplete(VanillaWorldProgressionId.IceQueen));
    }

    private static RuntimeWorldProgressionMutations InitialJournal()
    {
        var journal = new RuntimeWorldProgressionMutations();
        journal.MarkCompleted(VanillaWorldProgressionId.EyeOfCthulhu);
        journal.MarkTownNpcRescued(RuntimeTownRescueFacts1458.Goblin);
        journal.SetLunarApocalypseIsUp(true);
        return journal;
    }

    private static ulong Mask(int bits)
    {
        ulong result = 0;
        for (int index = 0; index < Milestones.Length; index++)
            if ((bits & (1 << index)) != 0) result |= 1UL << (int)Milestones[index];
        return result;
    }

    private static JsonDocument LoadHeaders()
    {
        using var resource = typeof(MoonBossProgressionPersistence1458Tests).Assembly.GetManifestResourceStream("MoonBossHeader1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        var document = JsonDocument.Parse(gzip);
        Assert.Equal("4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034", document.RootElement.GetProperty("source").GetString());
        Assert.Equal(0, document.RootElement.GetProperty("alteredMethodBodies").GetInt32());
        Assert.Equal(326, document.RootElement.GetProperty("version").GetInt32());
        return document;
    }

    private static (WorldFileHeader Header, WorldFileRuntimeMetadata Metadata) Parse(byte[] raw)
    {
        var envelope = new WorldFileEnvelope(326, 0, 0, [0, raw.Length], 0, []);
        Assert.Equal(WorldFileHeaderParseResult.Parsed, WorldFileHeaderParser.TryParse(raw, envelope, out var header));
        var limits = new WorldFileRuntimeMetadataLimits(16384, 16 * 1024 * 1024, 255, 762, 255, 16 * 1024 * 1024);
        Assert.Equal(WorldFileRuntimeMetadataParseResult.Parsed,
            WorldFileRuntimeMetadataParser.TryParse(raw, envelope, header!, limits, out var metadata, out _));
        return (header!, metadata!);
    }
}
