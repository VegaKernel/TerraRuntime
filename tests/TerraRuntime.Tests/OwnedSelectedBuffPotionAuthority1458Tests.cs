using Xunit;

namespace TerraRuntime.Tests;

public sealed class OwnedSelectedBuffPotionAuthority1458Tests
{
    [Fact]
    public void Funded_selected_buff_potions_and_quiver_preserve_source_boundaries_and_owned_custody()
    {
        BuffPotionBoundaryProbe.Run();
        OwnedMagicQuiverPhaseProof.Run();
    }
}
