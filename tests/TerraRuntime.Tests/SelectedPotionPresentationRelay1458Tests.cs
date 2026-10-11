namespace TerraRuntime.Tests;

public sealed class SelectedPotionPresentationRelay1458Tests
{
    [Xunit.Fact]
    public void Original_signed_effects_relay_every_occurrence_without_granting_vitals_or_payment()
    {
        Xunit.Assert.True(SelectedPotionPresentationRelay1458Checks.Run()>0);
    }
}
