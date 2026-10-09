using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

// These retained fields are not carried by packets16/42/41 or reconstructed from inventory reports.
// Player's actual constructor owns them; an imported payload with no fields remains unknown.
internal readonly record struct RuntimePlayerItemPhase1458(
    int BaseManaMaximum,
    PlayerManaRegenerationState1458 Mana,
    PlayerSelectedConsumableState1458 Selected,
    float ManaHeat)
{
    internal int DeadTime { get; init; }
    internal int RespawnTimer { get; init; }
    internal int ManaPotionDelay { get; init; }
    internal static RuntimePlayerItemPhase1458 Constructor => new(
        20, new(0, 0, 0f, 0, 0), new(0, 0, 0, 0, false, 0, 0), 0f);
}
