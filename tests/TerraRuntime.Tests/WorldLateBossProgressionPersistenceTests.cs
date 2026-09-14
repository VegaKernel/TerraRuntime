using System.IO.Compression;
using System.Security.Cryptography;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldLateBossProgressionPersistenceTests
{
    private static readonly VanillaWorldProgressionId[] Milestones =
    [
        VanillaWorldProgressionId.DukeFishron, VanillaWorldProgressionId.LunaticCultist,
        VanillaWorldProgressionId.EmpressOfLight, VanillaWorldProgressionId.MoonLord
    ];

    public static TheoryData<int, int> Masks
    {
        get
        {
            var result = new TheoryData<int, int>();
            for (int baseline = 0; baseline < 16; baseline++)
                for (int mutation = 0; mutation < 16; mutation++) result.Add(baseline, mutation);
            return result;
        }
    }

    [Theory]
    [MemberData(nameof(Masks))]
    public void Late_boss_save_matches_official_header_and_preserves_other_bytes(int baseline, int mutation)
    {
        byte[][] headers = ReadOfficialHeaders();
        byte[] source = headers[baseline];
        var envelope = Envelope(source.Length);
        Assert.Equal(WorldFileHeaderParseResult.Parsed, WorldFileHeaderParser.TryParse(source, envelope, out var header));
        Assert.NotNull(header);
        var journal = new RuntimeWorldProgressionMutations();
        for (int bit = 0; bit < Milestones.Length; bit++)
            if ((mutation & (1 << bit)) != 0) journal.MarkCompleted(Milestones[bit]);
        var snapshot = journal.CaptureSnapshot();

        Assert.Equal(WorldFileProgressionHeaderPatchResult.Patched,
            WorldFileProgressionHeaderPatcher.TryPatch(source, header, snapshot, out byte[] patched));
        Assert.Equal(headers[baseline | mutation], patched);
        Assert.Equal(WorldFileProgressionHeaderPatchResult.Patched,
            WorldFileProgressionHeaderPatcher.TryPatch(patched, header, snapshot, out byte[] repeated));
        Assert.Equal(patched, repeated);
        Assert.Equal(headers[baseline], source);

        var limits = new WorldFileRuntimeMetadataLimits(4096, 16384, 64, 1024, 64, 16384);
        Assert.Equal(WorldFileRuntimeMetadataParseResult.Parsed,
            WorldFileRuntimeMetadataParser.TryParse(patched, envelope, header, limits, out var metadata, out _));
        Assert.NotNull(metadata);
        Assert.Equal(((baseline | mutation) & 1) != 0, metadata.DownedFishron);
        Assert.Equal(((baseline | mutation) & 2) != 0, metadata.DownedAncientCultist);
        Assert.Equal(((baseline | mutation) & 4) != 0, metadata.DownedEmpressOfLight);
        Assert.Equal(((baseline | mutation) & 8) != 0, metadata.DownedMoonlord);
        Assert.True(metadata.TowerActiveSolar && metadata.TowerActiveVortex && metadata.TowerActiveNebula &&
            metadata.TowerActiveStardust && metadata.LunarApocalypseIsUp);
    }

    [Fact]
    public void Truncated_late_boss_header_is_rejected_without_partial_output()
    {
        byte[] source = ReadOfficialHeaders()[0];
        Assert.Equal(WorldFileHeaderParseResult.Parsed,
            WorldFileHeaderParser.TryParse(source, Envelope(source.Length), out var header));
        var journal = new RuntimeWorldProgressionMutations();
        foreach (var milestone in Milestones) journal.MarkCompleted(milestone);
        var snapshot = journal.CaptureSnapshot();
        Assert.Equal(WorldFileProgressionHeaderPatchResult.InvalidHeader,
            WorldFileProgressionHeaderPatcher.TryPatch(source.AsSpan(0, source.Length / 2), header!, snapshot, out var patched));
        Assert.Empty(patched);
    }

    private static WorldFileEnvelope Envelope(int length)
    {
        int[] pointers = new int[VanillaWorldFormat326.SectionCount];
        for (int i = 1; i < pointers.Length; i++) pointers[i] = length + i - 1;
        return new WorldFileEnvelope(WorldFileFormatPolicy.CurrentVersion, 1, 0, pointers,
            VanillaWorldFrameImportance326.Count, VanillaWorldFrameImportance326.CopyPackedBits());
    }

    private static byte[][] ReadOfficialHeaders()
    {
        // Original 1.4.5.8 SaveWorldHeader, synthetic world, all 16 late-boss combinations.
        // The supplied BinaryWriter fixes signed Int64 timestamps to 2020-01-01 UTC;
        // the original method and every gameplay field are unchanged.
        using Stream resource = typeof(WorldLateBossProgressionPersistenceTests).Assembly.GetManifestResourceStream("LateBossHeaders1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        Assert.Equal("879E72A84AAFF9EA12BA1C025665C6125FED8CEAA87F437180290C45A3BDAD2D",
            Convert.ToHexString(SHA256.HashData(bytes.ToArray())));
        bytes.Position = 0;
        using var reader = new BinaryReader(bytes);
        byte[][] headers = new byte[16][];
        for (int i = 0; i < headers.Length; i++) headers[i] = reader.ReadBytes(reader.ReadInt32());
        Assert.Equal(bytes.Length, bytes.Position);
        return headers;
    }
}
