using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Application;

/// <summary>Source-order Pumpkin/Snow Moon choices through Snow Moon wave twenty in NPC.Spawner.SpawnAnNPC.</summary>
internal static class VanillaMoonEventEarlySpawnSelector1458
{
    internal readonly record struct Selection(NpcTypeId? First, NpcTypeId? Second);

    public static NpcTypeId? Select(bool snowMoon, int wave, IVanillaNpcRandom random, Func<short, int> count,
        bool reachedInvasionBossCap = false) =>
        SelectPlan(snowMoon, wave, random, count, reachedInvasionBossCap).First;

    public static Selection SelectPlan(bool snowMoon, int wave, IVanillaNpcRandom random, Func<short, int> count,
        bool reachedInvasionBossCap = false)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(count);
        return snowMoon
            ? new Selection(SelectSnow(wave, random, count, reachedInvasionBossCap), null)
            : SelectPumpkinPlan(wave, random, count, reachedInvasionBossCap);
    }

    private static NpcTypeId? SelectSnow(int wave, IVanillaNpcRandom random, Func<short, int> count,
        bool reachedInvasionBossCap)
    {
        if (random.NextInt32(0, 30) == 0 && count(341) < 4)
            return new NpcTypeId(341);

        if (wave >= 20)
        {
            int choice = random.NextInt32(0, 3);
            return reachedInvasionBossCap ? null : choice switch
            {
                0 => new NpcTypeId(345),
                1 => new NpcTypeId(346),
                _ => new NpcTypeId(344)
            };
        }
        if (wave >= 19)
        {
            if (random.NextInt32(0, 10) == 0 && count(345) < 4) return new NpcTypeId(345);
            if (random.NextInt32(0, 10) == 0 && count(346) < 5) return new NpcTypeId(346);
            return random.NextInt32(0, 10) == 0 && count(344) < 7 ? new NpcTypeId(344) : new NpcTypeId(343);
        }
        if (wave >= 18)
        {
            if (random.NextInt32(0, 10) == 0 && count(345) < 3) return new NpcTypeId(345);
            if (random.NextInt32(0, 10) == 0 && count(346) < 4) return new NpcTypeId(346);
            if (random.NextInt32(0, 10) == 0 && count(344) < 6) return new NpcTypeId(344);
            if (random.NextInt32(0, 3) == 0) return new NpcTypeId(348);
            return random.NextInt32(0, 3) == 0 ? new NpcTypeId(351) : new NpcTypeId(343);
        }
        if (wave >= 17)
        {
            if (random.NextInt32(0, 10) == 0 && count(345) < 2) return new NpcTypeId(345);
            if (random.NextInt32(0, 10) == 0 && count(346) < 3) return new NpcTypeId(346);
            if (random.NextInt32(0, 10) == 0 && count(344) < 5) return new NpcTypeId(344);
            if (random.NextInt32(0, 4) == 0) return new NpcTypeId(347);
            return random.NextInt32(0, 2) == 0 ? new NpcTypeId(351) : new NpcTypeId(343);
        }
        if (wave >= 16)
        {
            if (random.NextInt32(0, 10) == 0 && count(345) < 2) return new NpcTypeId(345);
            if (random.NextInt32(0, 10) == 0 && count(346) < 2) return new NpcTypeId(346);
            if (random.NextInt32(0, 10) == 0 && count(344) < 4) return new NpcTypeId(344);
            return random.NextInt32(0, 2) == 0 ? new NpcTypeId(352) : new NpcTypeId(343);
        }
        if (wave >= 15)
        {
            if (random.NextInt32(0, 10) == 0 && count(345) == 0) return new NpcTypeId(345);
            if (random.NextInt32(0, 10) == 0 && count(346) < 2) return new NpcTypeId(346);
            if (random.NextInt32(0, 10) == 0 && count(344) < 3) return new NpcTypeId(344);
            return random.NextInt32(0, 3) == 0 ? new NpcTypeId(347) : new NpcTypeId(343);
        }

        if (wave == 6)
        {
            if (random.NextInt32(0, 10) == 0 && count(344) < 2)
                return new NpcTypeId(344);
            if (random.NextInt32(0, 4) == 0)
                return new NpcTypeId(347);
            return random.NextInt32(0, 2) == 0 ? new NpcTypeId(348) : new NpcTypeId(350);
        }
        if (wave == 7)
        {
            if (random.NextInt32(0, 10) == 0 && count(346) == 0)
                return new NpcTypeId(346);
            if (random.NextInt32(0, 3) == 0)
                return new NpcTypeId(342);
            return random.NextInt32(0, 4) == 0 ? new NpcTypeId(350) : FrostBase(random);
        }
        if (wave == 8)
        {
            if (random.NextInt32(0, 10) == 0 && count(346) == 0) return new NpcTypeId(346);
            if (random.NextInt32(0, 8) == 0) return new NpcTypeId(351);
            if (random.NextInt32(0, 3) == 0) return new NpcTypeId(348);
            return random.NextInt32(0, 3) == 0 ? new NpcTypeId(347) : new NpcTypeId(350);
        }
        if (wave == 9)
        {
            if (random.NextInt32(0, 10) == 0 && count(346) == 0) return new NpcTypeId(346);
            if (random.NextInt32(0, 10) == 0 && count(344) == 0) return new NpcTypeId(344);
            if (random.NextInt32(0, 2) == 0) return new NpcTypeId(348);
            return random.NextInt32(0, 3) == 0 ? new NpcTypeId(347) : new NpcTypeId(342);
        }
        if (wave == 10)
        {
            if (random.NextInt32(0, 10) == 0 && count(346) == 0) return new NpcTypeId(346);
            if (random.NextInt32(0, 10) == 0 && count(344) < 2) return new NpcTypeId(344);
            if (random.NextInt32(0, 6) == 0) return new NpcTypeId(351);
            if (random.NextInt32(0, 3) == 0) return new NpcTypeId(348);
            return random.NextInt32(0, 3) == 0 ? new NpcTypeId(347) : FrostBase(random);
        }
        if (wave == 11)
        {
            if (random.NextInt32(0, 10) == 0 && count(345) == 0) return new NpcTypeId(345);
            if (random.NextInt32(0, 6) == 0) return new NpcTypeId(352);
            return random.NextInt32(0, 2) == 0 ? new NpcTypeId(342) : FrostBase(random);
        }
        if (wave == 12)
        {
            if (random.NextInt32(0, 10) == 0 && count(345) == 0) return new NpcTypeId(345);
            if (random.NextInt32(0, 10) == 0 && count(344) == 0) return new NpcTypeId(344);
            if (random.NextInt32(0, 8) == 0) return new NpcTypeId(343);
            return random.NextInt32(0, 3) == 0 ? new NpcTypeId(342) : FrostBase(random);
        }
        if (wave == 13)
        {
            if (random.NextInt32(0, 10) == 0 && count(345) == 0) return new NpcTypeId(345);
            if (random.NextInt32(0, 10) == 0 && count(346) == 0) return new NpcTypeId(346);
            if (random.NextInt32(0, 3) == 0) return new NpcTypeId(352);
            if (random.NextInt32(0, 6) == 0) return new NpcTypeId(343);
            return random.NextInt32(0, 3) == 0 ? new NpcTypeId(342) : new NpcTypeId(347);
        }
        if (wave == 14)
        {
            if (random.NextInt32(0, 10) == 0 && count(345) == 0) return new NpcTypeId(345);
            if (random.NextInt32(0, 10) == 0 && count(346) == 0) return new NpcTypeId(346);
            if (random.NextInt32(0, 10) == 0 && count(344) == 0) return new NpcTypeId(344);
            return random.NextInt32(0, 3) == 0 ? new NpcTypeId(343) : null;
        }

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

    private static Selection SelectPumpkinPlan(int wave, IVanillaNpcRandom random, Func<short, int> count,
        bool reachedInvasionBossCap)
    {
        if (wave >= 20)
        {
            if (reachedInvasionBossCap) return default;
            if (random.NextInt32(0, 2) == 0 && count(327) < 2) return new Selection(new NpcTypeId(327), null);
            if (random.NextInt32(0, 3) != 0 && count(325) < 2) return new Selection(new NpcTypeId(325), null);
            return count(315) < 3 ? new Selection(new NpcTypeId(315), null) : default;
        }
        if (wave == 19)
        {
            if (random.NextInt32(0, 5) == 0 && count(327) < 2) return new Selection(new NpcTypeId(327), null);
            if (random.NextInt32(0, 5) == 0 && count(325) < 2) return new Selection(new NpcTypeId(325), null);
            return !reachedInvasionBossCap && count(315) < 5 ? new Selection(new NpcTypeId(315), null) : default;
        }
        if (wave == 18)
        {
            NpcTypeId? first = random.NextInt32(0, 7) == 0 && count(327) < 2 ? new NpcTypeId(327) : null;
            NpcTypeId second = random.NextInt32(0, 7) == 0 && count(325) < 2 ? new NpcTypeId(325) :
                random.NextInt32(0, 7) == 0 && count(315) < 3 ? new NpcTypeId(315) : new NpcTypeId(330);
            return new Selection(first, second);
        }
        if (wave == 17)
        {
            NpcTypeId? first = random.NextInt32(0, 7) == 0 && count(327) < 2 ? new NpcTypeId(327) : null;
            NpcTypeId second = random.NextInt32(0, 7) == 0 && count(325) < 2 ? new NpcTypeId(325) :
                random.NextInt32(0, 7) == 0 && count(315) < 2 ? new NpcTypeId(315) :
                random.NextInt32(0, 3) == 0 ? new NpcTypeId(330) : new NpcTypeId(329);
            return new Selection(first, second);
        }
        if (wave == 16)
        {
            if (random.NextInt32(0, 10) == 0 && count(327) < 2) return new Selection(new NpcTypeId(327), null);
            if (random.NextInt32(0, 10) == 0 && count(315) < 2) return new Selection(new NpcTypeId(315), null);
            if (random.NextInt32(0, 6) == 0) return new Selection(new NpcTypeId(330), null);
            return new Selection(random.NextInt32(0, 3) == 0 ? new NpcTypeId(329) : new NpcTypeId(326), null);
        }
        if (wave == 15)
        {
            NpcTypeId? first = random.NextInt32(0, 10) == 0 && count(327) == 0 ? new NpcTypeId(327) : null;
            NpcTypeId second = random.NextInt32(0, 7) == 0 && count(325) < 2 ? new NpcTypeId(325) :
                random.NextInt32(0, 5) == 0 ? new NpcTypeId(330) :
                random.NextInt32(0, 3) == 0 ? new NpcTypeId(326) : PumpkinBase(random);
            return new Selection(first, second);
        }
        if (wave == 14)
        {
            NpcTypeId? first = random.NextInt32(0, 10) == 0 && count(327) == 0 ? new NpcTypeId(327) : null;
            NpcTypeId second = random.NextInt32(0, 7) == 0 && count(325) < 2 ? new NpcTypeId(325) :
                random.NextInt32(0, 10) == 0 && count(315) == 0 ? new NpcTypeId(315) :
                random.NextInt32(0, 10) == 0 ? new NpcTypeId(330) :
                random.NextInt32(0, 7) == 0 ? new NpcTypeId(329) :
                random.NextInt32(0, 3) == 0 ? new NpcTypeId(326) : PumpkinBase(random);
            return new Selection(first, second);
        }

        return new Selection(SelectPumpkin(wave, random, count), null);
    }

    private static NpcTypeId SelectPumpkin(int wave, IVanillaNpcRandom random, Func<short, int> count)
    {
        if (wave == 6)
        {
            if (random.NextInt32(0, 7) == 0 && count(325) < 2) return new NpcTypeId(325);
            return random.NextInt32(0, 2) == 0 ? new NpcTypeId(326) : PumpkinBase(random);
        }
        if (wave == 7)
        {
            if (random.NextInt32(0, 7) == 0 && count(325) < 2) return new NpcTypeId(325);
            return random.NextInt32(0, 4) == 0 ? new NpcTypeId(330) : new NpcTypeId(329);
        }
        if (wave == 8)
        {
            if (random.NextInt32(0, 8) == 0 && count(315) < 2) return new NpcTypeId(315);
            return random.NextInt32(0, 4) == 0 ? new NpcTypeId(330) : new NpcTypeId(329);
        }
        if (wave == 9)
        {
            if (random.NextInt32(0, 10) == 0 && count(325) < 2) return new NpcTypeId(325);
            if (random.NextInt32(0, 8) == 0) return new NpcTypeId(330);
            if (random.NextInt32(0, 5) == 0) return new NpcTypeId(329);
            return random.NextInt32(0, 2) == 0 ? new NpcTypeId(326) : PumpkinBase(random);
        }
        if (wave == 10)
        {
            if (random.NextInt32(0, 10) == 0 && count(327) == 0) return new NpcTypeId(327);
            return random.NextInt32(0, 3) == 0 ? new NpcTypeId(329) : PumpkinBase(random);
        }
        if (wave == 11)
        {
            if (random.NextInt32(0, 7) == 0 && count(325) < 2) return new NpcTypeId(325);
            return random.NextInt32(0, 3) == 0 ? new NpcTypeId(330) : new NpcTypeId(326);
        }
        if (wave == 12)
            return random.NextInt32(0, 5) == 0 && count(327) == 0 ? new NpcTypeId(327) : new NpcTypeId(330);
        if (wave == 13)
        {
            if (random.NextInt32(0, 7) == 0 && count(325) < 2) return new NpcTypeId(325);
            if (random.NextInt32(0, 10) == 0 && count(315) < 2) return new NpcTypeId(315);
            if (random.NextInt32(0, 6) == 0) return new NpcTypeId(330);
            return random.NextInt32(0, 3) == 0 ? new NpcTypeId(329) : new NpcTypeId(326);
        }

        return wave switch
        {
            2 => random.NextInt32(0, 3) == 0 ? new NpcTypeId(326) : PumpkinBase(random),
            3 => random.NextInt32(0, 3) == 0 ? new NpcTypeId(329) : new NpcTypeId(326),
            4 => random.NextInt32(0, 8) == 0 && count(325) == 0 ? new NpcTypeId(330) :
                 random.NextInt32(0, 2) == 0 ? new NpcTypeId(326) : PumpkinBase(random),
            5 => random.NextInt32(0, 10) == 0 && count(315) == 0 ? new NpcTypeId(315) : new NpcTypeId(329),
            _ => PumpkinBase(random)
        };
    }
    private static NpcTypeId FrostBase(IVanillaNpcRandom random) => new((short)random.NextInt32(338, 341));

    private static NpcTypeId PumpkinBase(IVanillaNpcRandom random) => new((short)random.NextInt32(305, 315));
}
