namespace TerraRuntime.Tests;

public sealed class LocalWorldSelectorTests
{
    [Fact]
    public void Discovers_wld_files_case_insensitively_and_ignores_other_files()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"TerraRuntime.WorldSelector.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            string alpha = Path.Combine(directory, "Alpha.wld");
            string beta = Path.Combine(directory, "Beta.WLD");
            File.WriteAllText(alpha, string.Empty);
            File.WriteAllText(beta, string.Empty);
            File.WriteAllText(Path.Combine(directory, "notes.txt"), string.Empty);

            string[] worlds = LocalWorldSelector.DiscoverWorlds([directory, directory]);

            Assert.Equal(2, worlds.Length);
            Assert.Equal(Path.GetFullPath(alpha), worlds[0]);
            Assert.Equal(Path.GetFullPath(beta), worlds[1]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Product_title_contains_runtime_name_version_and_section()
    {
        Assert.StartsWith("TerraRuntime v", RuntimeProductInfo.DisplayName, StringComparison.Ordinal);
        Assert.Equal(
            $"{RuntimeProductInfo.DisplayName} · local world selection",
            RuntimeProductInfo.BuildTitle("local world selection"));
        Assert.DoesNotContain("  ", RuntimeProductInfo.DisplayName, StringComparison.Ordinal);
    }
}
