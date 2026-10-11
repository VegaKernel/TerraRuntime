namespace TerraRuntime.Application;

// Transient preparation of the existing paid attempt. No independent cursor/history owner.
internal sealed record RuntimePlayerMagicUsePreparation(RuntimePlayerMagicPayment1458 Payment,
    PlayerBuffState BeforeBuffs, PlayerBuffState AfterBuffs,
    RuntimePlayerInventoryStore.RemotePhasePreparation Inventory);
