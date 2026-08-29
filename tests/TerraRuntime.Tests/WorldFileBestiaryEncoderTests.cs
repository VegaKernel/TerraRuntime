using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileBestiaryEncoderTests
{
    private static readonly WorldFileBestiaryLimits Limits = new(
        MaxKillEntries: 32,
        MaxSightEntries: 32,
        MaxChatEntries: 32,
        MaxPersistentIdBytes: 1024,
        MaxTotalPersistentIdBytes: 16 * 1024);

    [Fact]
    public void Roundtrips_bestiary_state_through_current_decoder()
    {
        var source = new WorldBestiaryData(
            [
                new WorldBestiaryKill("Terraria.Zombie", 12),
                new WorldBestiaryKill("Terraria.βeta", 999_999_999)
            ],
            ["Terraria.EyeOfCthulhu", "Terraria.Guide"],
            ["Terraria.Guide", "Terraria.Merchant"]);

        using var stream = new MemoryStream();
        Assert.Equal(
            WorldFileBestiaryEncodeResult.Encoded,
            WorldFileBestiaryEncoder.TryEncode(source, Limits, stream, out long bytesWritten));
        Assert.Equal(stream.Length, bytesWritten);

        byte[] section = stream.ToArray();
        var envelope = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 1,
            favoriteFlags: 0,
            sectionOffsets: [0, 0, 0, 0, 0, 0, 0, 0, 0, section.Length],
            frameImportanceCount: VanillaWorldFormat326.TileTypeCount,
            frameImportanceBits: new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3]);

        Assert.Equal(
            WorldFileBestiaryDecodeResult.Decoded,
            WorldFileBestiaryDecoder.TryDecode(
                section,
                envelope,
                Limits,
                out WorldBestiaryData? decoded,
                out int consumed));

        Assert.Equal(section.Length, consumed);
        Assert.NotNull(decoded);
        Assert.Equal(source.Kills, decoded!.Kills);
        Assert.Equal(source.Sightings, decoded.Sightings);
        Assert.Equal(source.Chats, decoded.Chats);
    }

    [Fact]
    public void Rejects_duplicate_ids_inside_same_collection_before_writing()
    {
        var source = new WorldBestiaryData(
            [
                new WorldBestiaryKill("Terraria.Zombie", 1),
                new WorldBestiaryKill("Terraria.Zombie", 2)
            ],
            [],
            []);
        using var stream = new MemoryStream();

        Assert.Equal(
            WorldFileBestiaryEncodeResult.DuplicatePersistentId,
            WorldFileBestiaryEncoder.TryEncode(source, Limits, stream, out long bytesWritten));
        Assert.Equal(0, bytesWritten);
        Assert.Equal(0, stream.Length);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_000_000)]
    public void Rejects_invalid_kill_count_before_writing(int killCount)
    {
        var source = new WorldBestiaryData(
            [new WorldBestiaryKill("Terraria.Zombie", killCount)],
            [],
            []);
        using var stream = new MemoryStream();

        Assert.Equal(
            WorldFileBestiaryEncodeResult.InvalidKillCount,
            WorldFileBestiaryEncoder.TryEncode(source, Limits, stream, out long bytesWritten));
        Assert.Equal(0, bytesWritten);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void Rejects_entry_and_total_string_budgets_before_writing()
    {
        using var entryStream = new MemoryStream();
        WorldFileBestiaryLimits oneKill = Limits with { MaxKillEntries = 1 };
        var tooManyKills = new WorldBestiaryData(
            [new WorldBestiaryKill("a", 1), new WorldBestiaryKill("b", 2)],
            [],
            []);
        Assert.Equal(
            WorldFileBestiaryEncodeResult.EntryBudgetExceeded,
            WorldFileBestiaryEncoder.TryEncode(tooManyKills, oneKill, entryStream, out long entryBytes));
        Assert.Equal(0, entryBytes);
        Assert.Equal(0, entryStream.Length);

        using var stringStream = new MemoryStream();
        WorldFileBestiaryLimits tinyTotal = Limits with { MaxTotalPersistentIdBytes = 3 };
        var tooMuchText = new WorldBestiaryData(
            [new WorldBestiaryKill("ab", 1)],
            ["cd"],
            []);
        Assert.Equal(
            WorldFileBestiaryEncodeResult.TotalStringBudgetExceeded,
            WorldFileBestiaryEncoder.TryEncode(tooMuchText, tinyTotal, stringStream, out long stringBytes));
        Assert.Equal(0, stringBytes);
        Assert.Equal(0, stringStream.Length);
    }
}
