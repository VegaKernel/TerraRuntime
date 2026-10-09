using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

// Retains constructor-owned clocks alongside packet-updated mana, animation and respawn fields.
// Inventory reports cannot reconstruct missing clocks; an import without this phase stays unknown.
internal readonly record struct RuntimePlayerItemPhase1458(
    int BaseManaMaximum,
    PlayerManaRegenerationState1458 Mana,
    PlayerSelectedConsumableState1458 Selected,
    float ManaHeat)
{
    internal int DeadTime { get; init; }
    internal int RespawnTimer { get; init; }
    internal int ManaPotionDelay { get; init; }
    internal bool PendingItemReuse { get; init; }
    internal int? ToolTime { get; init; }
    internal int? AttackCD { get; init; }
    internal PlayerDerivedCritState1458? DerivedCrit { get; init; }
    internal static RuntimePlayerItemPhase1458 Constructor => new(
        20, new(0, 0, 0f, 0, 0), new(0, 0, 0, 0, false, 0, 0), 0f)
    {
        ToolTime = 0, AttackCD = 0, DerivedCrit = PlayerDerivedCritState1458.SourceBaseline
    };
}
