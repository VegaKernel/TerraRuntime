namespace TerraRuntime.Tests;

public sealed class OwnedSelectedPotionAcknowledgement1458Tests
{
    [Xunit.Fact]
    public void Source_batches_acknowledge_paid_potions_without_replaying_vitals_buffs_or_funding()
    {
        SelectedPotionPaymentBoundary1458Checks.Run();
        SelectedPotionAckProbe.Run("Linux");
        SelectedPotionAckProbe.Run("Windows");
    }
}
