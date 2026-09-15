using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>TerrariaServer 1.4.5.8 InitData and NPCID.Sets allocation rules.</summary>
public static class VanillaNpcSpawnRules
{
    public const int PhysicalSlotCount = 200;
    public const int SpawnProtectionUpdates = 2;

    /// <summary>NewNPC's Good World substitution, after its unconditional Next(3) draw.</summary>
    public static NpcTypeId ApplyGoodWorldRoll(NpcTypeId type, int roll) => roll == 0 ? type :
        type == VanillaNpcIds.Bunny ? VanillaNpcIds.ExplosiveBunny :
        type == VanillaNpcIds.Demon ? VanillaNpcIds.VoodooDemon : type;

    public static bool SearchesInReverse(NpcTypeId type) =>
        type == VanillaNpcIds.QueenBee || type == VanillaNpcIds.Golem;

    public static bool CannotSpawnInSlotZero(NpcTypeId type) => type.Value is
        7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 39 or 40 or 41 or
        87 or 88 or 89 or 90 or 91 or 92 or 95 or 96 or 97 or 98 or 99 or 100 or
        117 or 118 or 119 or 134 or 135 or 136 or 375 or 402 or 412 or 413 or 414 or
        454 or 455 or 456 or 457 or 458 or 459 or 510 or 511 or 512 or 513 or 514 or
        515 or 621 or 622 or 623;
}
