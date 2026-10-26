using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaMoonBossLootContext1458(
    NpcTypeId Type, bool? PumpkinMoon, bool? SnowMoon, int? WaveNumber,
    bool IsExpertMode, bool IsMasterMode, bool? Christmas = null);

public readonly record struct VanillaMoonBossLootPlayer1458(PlayerSlotId Slot, float CenterX, float CenterY)
{
    public bool IsValid => Slot.Value < VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots &&
        float.IsFinite(CenterX) && float.IsFinite(CenterY);
    public NpcLootWorldItemOrigin Origin => new(CenterX, CenterY);
}

public interface IMoonBossLootDeliverySink1458
{
    bool CanDeliverWorldItem(ItemTypeId type);
    bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random);
}

public readonly record struct MoonBossLootExecutionResult1458(int WorldItemCount, int MasterPetDropCount, bool MoneyCleared);

/// <summary>
/// ItemDropDatabase.RegisterBoss_PumpkinMoon/FrostMoon, TerrariaServer 1.4.5.8.
/// The caller supplies the wave before checkDead credits this death and the ascending active interacting census.
/// Item materialization remains on the same random stream between individual rule evaluations.
/// All 31 point-bearing identities have an explicit registration path, including verified empty specific tables.
/// </summary>
public static class VanillaMoonBossLootEvaluator1458
{
    private static readonly int[] MourningWoodItems = [1829, 1831, 1835, 1836, 1837, 1845, 1855, 4444, 4793, 4941, 1729];
    private static readonly int[] PumpkingItems = [1782, 1783, 1784, 1785, 1811, 1826, 1801, 1802, 4680, 1798, 1856, 4942, 4812];
    private static readonly int[] EverscreamItems = [1962, 1871, 1916, 1928, 1930, 4944, 4813];
    private static readonly int[] IceQueenItems = [1960, 1914, 1959, 1931, 1946, 1947, 4943, 4814];
    private static readonly int[] SantaItems = [1961, 1910, 1929, 4945, 4794];

    public static bool IsBoss(NpcTypeId type) => type.Value is 325 or 327 or 344 or 345 or 346;

    public static bool IsParticipant(NpcTypeId type) =>
        type.Value is >= 305 and <= 315 or >= 325 and <= 327 or 329 or 330 or >= 338 and <= 352;

    public static bool TryExecute(in VanillaMoonBossLootContext1458 context,
        in NpcLootWorldItemOrigin npcOrigin, ReadOnlySpan<VanillaMoonBossLootPlayer1458> activeInteractingPlayers,
        INpcLootRollSource rolls, IMoonBossLootDeliverySink1458 sink, out MoonBossLootExecutionResult1458 result)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(sink);
        result = default;
        bool? active = context.Type.Value < 338 ? context.PumpkinMoon : context.SnowMoon;
        if (!IsParticipant(context.Type) || !active.HasValue || context.IsMasterMode && !context.IsExpertMode ||
            active.Value && context.WaveNumber is not (>= 1 and <= 20) || !npcOrigin.IsValid ||
            !PlayersAreSourceOrdered(activeInteractingPlayers)) return false;
        if (!IsBoss(context.Type)) return TryExecuteOrdinary(in context, in npcOrigin, active.Value, rolls, sink, out result);
        if (!CanDeliverAll(in context, active.Value, sink)) return false;

        var origin = npcOrigin;
        int count = 0;
        int petCount = 0;
        bool moneyCleared = false;
        bool Common(int item, int denominator = 1, int minimum = 1, int maximum = 1)
        {
            if (rolls.RollLuck(denominator) != 0) return false;
            Deliver(new(item), checked((short)rolls.NextInt32(minimum, maximum + 1)), in origin, rolls, sink);
            count++;
            return true;
        }
        void Options(ReadOnlySpan<int> items)
        {
            // OneFromOptionsDropRule uses RollLuck, not the nested OneFromRulesRule's direct Next.
            if (rolls.RollLuck(1) != 0) return;
            Deliver(new(items[rolls.NextInt32(0, items.Length)]), 1, in origin, rolls, sink);
            count++;
        }
        void Trophy(int item, int wave, bool expert)
        {
            if (wave < 15) return;
            int denominator = wave >= 19 ? 2 : wave >= 17 ? 3 : 4;
            // Conditions.*MoonDropGateForTrophies uses Main.rand for this Expert offer,
            // followed by info.rng for its trophy offer. Live composition shares one source stream.
            if (expert && rolls.NextInt32(0, 3) == 0) denominator--;
            if (rolls.NextInt32(0, denominator) == 0) Common(item);
        }

        int wave = context.WaveNumber ?? 0;
        bool gate = false;
        if (active.Value)
        {
            int adjustedWave = wave + (context.IsExpertMode ? 5 : 0);
            int denominator = (int)((context.Type.Value < 338 ? 24d - adjustedWave : 28d - adjustedWave) / 2.5d);
            if (context.IsExpertMode) denominator -= context.Type.Value < 338 ? 1 : 2;
            gate = rolls.RollLuck(Math.Max(denominator, 1)) == 0;
        }
        if (gate)
        {
            switch (context.Type.Value)
            {
                case 325:
                    rolls.NextInt32(0, 1);
                    int mourningChoice = rolls.NextInt32(0, 5);
                    Common(mourningChoice switch { 0 => 1829, 1 => 1831, 2 => 1835, 3 => 1837, _ => 1845 });
                    if (mourningChoice == 2) Common(1836, minimum: 30, maximum: 60);
                    Trophy(1855, wave, context.IsExpertMode);
                    if (context.IsExpertMode) Common(4444, 5);
                    break;
                case 327:
                    rolls.NextInt32(0, 1);
                    int pumpkingChoice = rolls.NextInt32(0, 8);
                    Common(pumpkingChoice switch { 0 => 1782, 1 => 1784, 2 => 1811, 3 => 1826, 4 => 1801, 5 => 1802, 6 => 4680, _ => 1798 });
                    if (pumpkingChoice == 0) Common(1783, minimum: 50, maximum: 100);
                    if (pumpkingChoice == 1) Common(1785, minimum: 25, maximum: 50);
                    Trophy(1856, wave, context.IsExpertMode);
                    break;
                case 344:
                    Trophy(1962, wave, context.IsExpertMode);
                    if (!Common(1871, 15)) Options([1916, 1928, 1930]);
                    break;
                case 345:
                    Trophy(1960, wave, context.IsExpertMode);
                    if (wave >= 15) Common(1914, 15);
                    if (!Common(1959, 15)) Options([1931, 1946, 1947]);
                    break;
                case 346:
                    Trophy(1961, wave, context.IsExpertMode);
                    Options([1910, 1929]);
                    break;
            }
            if (context.IsMasterMode)
            {
                Common(context.Type.Value switch { 325 => 4941, 327 => 4942, 344 => 4944, 345 => 4943, _ => 4945 });
                var pet = new ItemTypeId(context.Type.Value switch { 325 => 4793, 327 => 4812, 344 => 4813, 345 => 4814, _ => 4794 });
                short stack = checked((short)rolls.NextInt32(1, 2));
                foreach (var player in activeInteractingPlayers)
                {
                    if (rolls.NextInt32(0, 4) != 0) continue;
                    var playerOrigin = player.Origin;
                    Deliver(pet, stack, in playerOrigin, rolls, sink);
                    count++;
                    petCount++;
                }
                // CommonCode.DropItemForEachInteractingPlayerOnThePlayer clears NPC.value
                // even when the census is empty or every recipient's offer fails.
                moneyCleared = true;
            }
        }
        if (context.Type.Value == 325)
            Common(1729, minimum: context.IsMasterMode ? 30 : context.IsExpertMode ? 25 : 15,
                maximum: context.IsMasterMode ? 50 : context.IsExpertMode ? 40 : 30);
        result = new(count, petCount, moneyCleared);
        return true;
    }

    private static bool TryExecuteOrdinary(in VanillaMoonBossLootContext1458 context,
        in NpcLootWorldItemOrigin origin, bool active, INpcLootRollSource rolls,
        IMoonBossLootDeliverySink1458 sink, out MoonBossLootExecutionResult1458 result)
    {
        result = default;
        ReadOnlySpan<int> possible = context.Type.Value switch
        {
            >= 305 and <= 314 when active => [1788, 1789, 1790],
            315 when active => [1857],
            326 => [1729],
            >= 338 and <= 340 => [1943, 1944, 1945],
            341 when context.Christmas == true => [1869],
            _ => []
        };
        if (context.Type.Value == 341 && !context.Christmas.HasValue) return false;
        foreach (int item in possible)
            if (!sink.CanDeliverWorldItem(new(item))) return false;
        int count = 0;
        if (context.Type.Value is >= 305 and <= 315 && active)
        {
            int wave = context.WaveNumber!.Value + (context.IsExpertMode ? 5 : 0);
            int denominator = (int)((24d - wave) / 2.5d) - (context.IsExpertMode ? 1 : 0);
            if (rolls.RollLuck(Math.Max(denominator, 1)) == 0)
            {
                if (context.Type.Value == 315)
                {
                    if (rolls.RollLuck(20) == 0)
                    {
                        Deliver(new(1857), checked((short)rolls.NextInt32(1, 2)), in origin, rolls, sink);
                        count++;
                    }
                }
                else if (rolls.RollLuck(10) == 0)
                {
                    ReadOnlySpan<int> options = [1788, 1789, 1790];
                    Deliver(new(options[rolls.NextInt32(0, options.Length)]), 1, in origin, rolls, sink);
                    count++;
                }
            }
        }
        else if (context.Type.Value == 326)
        {
            if (rolls.RollLuck(1) == 0)
            {
                short stack = checked((short)rolls.NextInt32(context.IsMasterMode ? 2 : 1,
                    context.IsExpertMode ? 5 : 4));
                Deliver(new(1729), stack, in origin, rolls, sink);
                count++;
            }
        }
        else if (context.Type.Value is >= 338 and <= 340)
        {
            // Elf variants share this table even outside Snow Moon; no Moon luck gate wraps it.
            if (rolls.RollLuck(200) == 0)
            {
                ReadOnlySpan<int> options = [1943, 1944, 1945];
                Deliver(new(options[rolls.NextInt32(0, options.Length)]), 1, in origin, rolls, sink);
                count++;
            }
        }
        else if (context.Type.Value == 341 && context.Christmas == true && rolls.RollLuck(1) == 0)
        {
            Deliver(new(1869), checked((short)rolls.NextInt32(1, 2)), in origin, rolls, sink);
            count++;
        }
        // 329/330,342/343,347..352 have verified empty NPC-specific registration.
        // Their global loot, source HitEffect and money/healing producers remain on the death owner.
        result = new(count, 0, false);
        return true;
    }

    private static void Deliver(ItemTypeId item, short stack, in NpcLootWorldItemOrigin origin,
        INpcLootRollSource rolls, IMoonBossLootDeliverySink1458 sink)
    {
        var drop = new NpcLootDrop(item, stack);
        if (!sink.TryDeliverWorldItem(in origin, in drop, rolls))
            throw new InvalidOperationException($"Moon-boss loot sink failed advertised item {item.Value} delivery.");
    }

    private static bool PlayersAreSourceOrdered(ReadOnlySpan<VanillaMoonBossLootPlayer1458> players)
    {
        if (players.Length > VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots) return false;
        int previous = -1;
        foreach (var player in players)
        {
            if (!player.IsValid || player.Slot.Value <= previous) return false;
            previous = player.Slot.Value;
        }
        return true;
    }

    private static bool CanDeliverAll(in VanillaMoonBossLootContext1458 context, bool active, IMoonBossLootDeliverySink1458 sink)
    {
        if (!active) return context.Type.Value != 325 || sink.CanDeliverWorldItem(new(1729));
        ReadOnlySpan<int> items = context.Type.Value switch
        {
            325 => MourningWoodItems, 327 => PumpkingItems, 344 => EverscreamItems,
            345 => IceQueenItems, 346 => SantaItems, _ => []
        };
        foreach (int item in items)
        {
            if (item is 4793 or 4941 or 4812 or 4942 or 4813 or 4944 or 4814 or 4943 or 4794 or 4945)
            { if (!context.IsMasterMode) continue; }
            if (item == 4444 && !context.IsExpertMode) continue;
            if (item is 1855 or 1856 or 1962 or 1960 or 1961 or 1914 && context.WaveNumber < 15) continue;
            if (!sink.CanDeliverWorldItem(new(item))) return false;
        }
        return true;
    }
}
