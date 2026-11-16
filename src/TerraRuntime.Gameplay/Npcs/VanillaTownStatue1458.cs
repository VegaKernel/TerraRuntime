using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Fixed King/Queen statue identity lists in Wiring.HitWireSingle, Terraria 1.4.5.8.</summary>
public static class VanillaTownStatue1458
{
    public const int Cooldown = 300;

    // NPC.UpdateNPC_TeleportVisuals has no style14 particle branch, but still
    // ages its retained history. Style13's immediate reset does not apply.
    public static float AdvanceTeleportTime(float time) => Math.Max(0f, time - .005f);

    public static bool IsEligible(int style, NpcTypeId type) => style switch
    {
        40 => type.Value is 17 or 19 or 22 or 38 or 54 or 107 or 108 or 142 or 160 or 207 or 209 or 227 or 228 or 229 or 368 or 369 or 550 or 441 or 588,
        41 => type.Value is 18 or 20 or 124 or 178 or 208 or 353 or 633 or 663,
        _ => false
    };
}
