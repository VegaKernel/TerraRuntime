using Xunit;
namespace TerraRuntime.Tests;
public sealed class OwnedCrafting1458Tests
{
    [Fact]
    public void Local_crafting_keeps_source_images_funding_and_current_container_context()
        => CraftStateChecks.Run();
    [Fact]
    public void Currency_conversion_preserves_unique_final_copper_and_refuses_ambiguous_remainders()
        => CraftStateChecks.RunCurrencyIngress();
}
internal static partial class CraftStateChecks
{
    private static Stream CraftSource(string name) => typeof(OwnedCrafting1458Tests).Assembly
        .GetManifestResourceStream(name) ?? throw new InvalidOperationException("Missing numeric crafting fixture " + name);
}
