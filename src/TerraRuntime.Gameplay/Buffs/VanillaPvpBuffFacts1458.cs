using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Buffs;

/// <summary>
/// Exact TerrariaServer 1.4.5.8 Main.pvpBuff entries. Packet 55 is server-relayable only for these buff types.
/// </summary>
public static class VanillaPvpBuffFacts1458
{
    public static bool IsRelayable(BuffTypeId type) => type.Value switch
    {
        20 or 70 or 24 or 323 or 31 or 39 or 44 or 324 or 69 or 103 or
        119 or 120 or 137 or 320 or 30 or 36 or 397 or 398 or 399 or 400 => true,
        _ => false
    };
}
