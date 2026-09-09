using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Tests;

public sealed class VanillaGenerationSeed1458Tests
{
    // Independent official WorldFileData.TranslateSeed results, including ReLogic
    // Crc32's low UTF-16-byte behavior (not UTF-8) and signed numeric normalization.
    [Theory]
    [InlineData("1458", 1458)]
    [InlineData("-1458", 1458)]
    [InlineData("-2147483648", 2147483647)]
    [InlineData("2147483647", 2147483647)]
    [InlineData("2147483648", -2056316095)]
    [InlineData("18446744073709551615", -1073852134)]
    [InlineData("desert", 243448890)]
    [InlineData("пустыня", -297671210)]
    [InlineData("", 0)]
    [InlineData("  -42  ", 42)]
    [InlineData("😀", 724654657)]
    [InlineData("seed🌵", 197197792)]
    [InlineData("é", 198489425)]
    [InlineData("Ā", -771559539)]
    public void Request_identity_matches_official_world_file_seed(string text, int expected)
    {
        var request = new WorldGenerationRequest(new WorldGeneratorId("test:seed"), "Seed", 0, 4200, 1200) { SeedText = text };
        Assert.Equal(expected, request.ResolveVanillaSeed1458());
        Assert.Equal(0UL, request.Seed);
        Assert.Equal(text, request.SeedText);
    }

    [Fact]
    public void Missing_text_uses_the_complete_unsigned_decimal_seed()
    {
        var request = new WorldGenerationRequest(new WorldGeneratorId("test:seed"), "Seed", ulong.MaxValue, 4200, 1200);
        Assert.Equal(-1073852134, request.ResolveVanillaSeed1458());
    }
}
