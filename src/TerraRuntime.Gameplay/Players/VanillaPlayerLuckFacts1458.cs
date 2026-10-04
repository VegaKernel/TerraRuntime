using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Players;

public static class VanillaPlayerLuckFacts1458
{
    public static VanillaPlayerLuckComponents1458 AdvanceRemoteFactors(in VanillaPlayerLuckComponents1458 components, int dayRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dayRate);
        int ladyBug = components.LadyBugLuckTimeLeft;
        if (ladyBug > 0) ladyBug = Math.Max(0, ladyBug - dayRate);
        else if (ladyBug < 0) ladyBug = Math.Min(0, ladyBug + dayRate);
        float coin = components.CoinLuck;
        if (coin > 0f)
        {
            coin *= (float)Math.Pow(0.9999, dayRate);
            if ((double)coin < 0.25) coin = 0f;
        }
        return components with { LadyBugLuckTimeLeft = ladyBug, CoinLuck = coin };
    }
    // Player.RecalculateLuck/GetLadyBugLuck/CalculateCoinLuck. Flags come from their genuine owners.
    public static float Recalculate(in VanillaPlayerLuckComponents1458 components,
        bool usedGalaxyPearl, bool lanternsUp, bool stinky)
    {
        float ladyBug = components.LadyBugLuckTimeLeft > 0
            ? components.LadyBugLuckTimeLeft / 43200f
            : components.LadyBugLuckTimeLeft < 0 ? (0f - components.LadyBugLuckTimeLeft) / -10800f : 0f;
        float luck = ladyBug * 0.2f + components.TorchLuck * 0.2f;
        luck += components.LuckPotion * 0.1f;
        luck += components.KiteLuckLevel * 0.1f / 3f;
        if (usedGalaxyPearl) luck += 0.03f;
        if (lanternsUp) luck += 0.3f;
        if (components.HasGardenGnomeNearby) luck += 0.2f;
        if (stinky) luck -= 0.25f;
        luck += components.EquipmentBasedLuckBonus;
        luck += CalculateCoinLuck(components.CoinLuck);
        if (components.BrokenMirrorBadLuck) luck -= 0.25f;
        return luck;
    }

    private static float CalculateCoinLuck(float value)
    {
        if (value == 0f) return 0f;
        if (value > 249000f) return 0.2f;
        if (value > 24900f) return 0.175f;
        if (value > 2490f) return 0.15f;
        if (value > 249f) return 0.125f;
        if ((double)value > 24.9) return 0.1f;
        if ((double)value > 2.49) return 0.075f;
        if ((double)value > 0.249) return 0.05f;
        return 0.025f;
    }
}
