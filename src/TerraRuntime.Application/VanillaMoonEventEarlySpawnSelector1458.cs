using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Application;

/// <summary>Source-order Pumpkin/Snow Moon choices for waves one through five in NPC.Spawner.SpawnAnNPC.</summary>
internal static class VanillaMoonEventEarlySpawnSelector1458
{
    public static NpcTypeId Select(bool snowMoon, int wave, IVanillaNpcRandom random, Func<short, int> count)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(count);
        return snowMoon ? SelectSnow(wave, random, count) : SelectPumpkin(wave, random, count);
    }

    private static NpcTypeId SelectSnow(int wave, IVanillaNpcRandom random, Func<short, int> count)
    {
        if (random.NextInt32(0, 30) == 0 && count(341) < 4)
            return new NpcTypeId(341);

        return wave switch
        {
            2 => random.NextInt32(0, 3) == 0 ? new NpcTypeId(350) : FrostBase(random),
            3 => random.NextInt32(0, 8) == 0 ? new NpcTypeId(348) :
                 random.NextInt32(0, 4) == 0 ? new NpcTypeId(350) :
                 random.NextInt32(0, 3) == 0 ? new NpcTypeId(342) : FrostBase(random),
            4 => random.NextInt32(0, 10) == 0 && count(344) == 0 ? new NpcTypeId(344) :
                 random.NextInt32(0, 4) == 0 ? new NpcTypeId(350) :
                 random.NextInt32(0, 3) == 0 ? new NpcTypeId(342) : FrostBase(random),
            5 => random.NextInt32(0, 10) == 0 && count(344) == 0 ? new NpcTypeId(344) :
                 random.NextInt32(0, 4) == 0 ? new NpcTypeId(350) :
                 random.NextInt32(0, 8) == 0 ? new NpcTypeId(348) : FrostBase(random),
            _ => random.NextInt32(0, 3) == 0 ? new NpcTypeId(342) : FrostBase(random)
        };
    }

    private static NpcTypeId SelectPumpkin(int wave, IVanillaNpcRandom random, Func<short, int> count) => wave switch
    {
        2 => random.NextInt32(0, 3) == 0 ? new NpcTypeId(326) : PumpkinBase(random),
        3 => random.NextInt32(0, 3) == 0 ? new NpcTypeId(329) : new NpcTypeId(326),
        4 => random.NextInt32(0, 8) == 0 && count(325) == 0 ? new NpcTypeId(330) :
             random.NextInt32(0, 2) == 0 ? new NpcTypeId(326) : PumpkinBase(random),
        5 => random.NextInt32(0, 10) == 0 && count(315) == 0 ? new NpcTypeId(315) : new NpcTypeId(329),
        _ => PumpkinBase(random)
    };

    private static NpcTypeId FrostBase(IVanillaNpcRandom random) => new((short)random.NextInt32(338, 341));

    private static NpcTypeId PumpkinBase(IVanillaNpcRandom random) => new((short)random.NextInt32(305, 315));
}
