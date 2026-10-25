namespace TerraRuntime.Application;
// Transient preparation of the existing paid attempt. No independent cursor/history owner.
internal sealed record RuntimePlayerMagicUsePreparation(RuntimePlayerMagicPayment1458 Payment,
    PlayerBuffState BeforeBuffs,
    PlayerBuffState AfterBuffs,
    RuntimePlayerInventoryStore.RemotePhasePreparation Inventory)
{
    // A detached before-image inside this existing transient preparation, not a
    // second pending owner. It can recognize only its exact committed phase refill.
    internal RuntimePlayerInventoryStore.ShopJournalCapture? GemInventoryBefore { get; init; }
}
