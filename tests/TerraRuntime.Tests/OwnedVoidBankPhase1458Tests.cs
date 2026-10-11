using Xunit;
namespace TerraRuntime.Tests;
public sealed class OwnedVoidBankPhase1458Tests
{
    [Fact]
    public void Source_neutral_bank_roles_admit_without_ignoring_active_writers()
        => VoidBankOwnedPhase1458Checks.Run();
}
