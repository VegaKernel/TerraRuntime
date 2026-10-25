using Xunit;

namespace TerraRuntime.Tests;
public sealed class OwnedGemStaffAuthority1458Tests
{
    [Fact]
    public void Source_staff_births_and_paid_epochs_preserve_authority_across_ingress_and_observers() => GemStaffBoundaryChecks.Run();
}
