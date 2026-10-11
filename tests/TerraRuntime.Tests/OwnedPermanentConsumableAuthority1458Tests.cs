using Xunit;
namespace TerraRuntime.Tests;
public sealed class OwnedPermanentConsumableAuthority1458Tests
{
    [Fact]
    public void Paid_local_permanent_consumables_keep_source_images_and_next_phase_projections()
    {
        PermanentEightBoundaryChecks.Run();
        PermanentEightBoundaryChecks.SequentialFamilyBoundary();
        PermanentEightBoundaryChecks.LifecycleBoundaries();
        PermanentEightBoundaryChecks.ImportedGalaxyBoundary();
    }
}
