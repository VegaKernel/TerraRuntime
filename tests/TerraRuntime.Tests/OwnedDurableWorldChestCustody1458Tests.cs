using Xunit;

namespace TerraRuntime.Tests;

public sealed class OwnedDurableWorldChestCustody1458Tests
{
    [Fact]
    public void Source_transfers_and_managed_world_restarts_preserve_exact_private_custody()
    {
        int checks = DurableWorldChestCustody.DurableChestCustodyChecks.Run();
        Console.WriteLine($"DURABLE_CUSTODY checks={checks}");
        Assert.True(checks > 0);
        Assert.True(DurableWorldChestCustody.SandboxTrustContractChecks.Run() > 0);
        Assert.True(RemotePartialMaterials.RemotePartialMaterialChecks.Run() > 0);
    }
}
