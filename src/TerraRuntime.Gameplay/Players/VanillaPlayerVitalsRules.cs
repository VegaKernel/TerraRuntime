using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Players;

/// <summary>
/// Source-backed Terraria 1.4.5.8 player-vitals acceptance rules that do not own runtime state.
/// </summary>
public static class VanillaPlayerVitalsRules
{
    /// <summary>
    /// Terraria 1.4.5.8 clamps statLifeMax to at least 20 when packet 16 is accepted.
    /// It does not clamp current life to that maximum in the packet handler.
    /// </summary>
    public const short MinimumMaxLife = 20;

    public static PlayerHealthCommitRequest NormalizeHealth(in PlayerHealthCommitRequest request) =>
        request with { MaxLife = Math.Max(request.MaxLife, MinimumMaxLife) };
}
