using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed partial class WiringDartTrap1458Tests
{
    public static IEnumerable<object[]> HellfireNpcDots() => Rows("WiringTrapHellfireNpcDot1458");
    [Theory, MemberData(nameof(HellfireNpcDots))]
    public void Hellfire_combined_npc_counters_and_visual_offers_match_original(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        int type = row.GetProperty("type").GetInt32(), mask = row.GetProperty("flagsMask").GetInt32();
        var store = new RuntimeNpcStore();
        Assert.True(store.TrySpawnVanilla(new(type, (short)type, 1040, 1040, 0, 0, 255,
            new(0, row.GetProperty("held").GetInt32(), 0, 0), NpcSimulationState.Initial with
            {
                Life = 1000,
                LifeMax = 1000,
                LifeRegenCounter = row.GetProperty("counter").GetInt32(),
                Immortal = row.GetProperty("protection").GetInt32() == 1,
                DontTakeDamage = row.GetProperty("protection").GetInt32() == 2,
                LiquidContact = row.GetProperty("lava").GetBoolean() ? NpcLiquidContactKind.Lava : NpcLiquidContactKind.None
            }), out var npc));
        Assert.True(VanillaNpcDebuffRegeneration1458.TryStep(in npc, row.GetProperty("good").GetBoolean(),
            (mask & 2) != 0, (mask & 4) != 0, (mask & 1) != 0, out var after, (mask & 8) != 0));
        Assert.Equal(row.GetProperty("life").GetInt32(), after.Life);
        Assert.Equal(row.GetProperty("afterCounter").GetInt32(), after.Counter);
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var flags = new PlayerDebuffSnapshot1458((mask & 4) != 0, false, (mask & 2) != 0) { OnFire3 = (mask & 8) != 0 };
        var plan = new RuntimeNpcBuffPlan1458(npc, 1, default, flags, false, false, after.Life, after.Counter, after.Damage)
        { Venom = (mask & 1) != 0 };
        RuntimeNpcBuffStatus1458.PlanVisualOffers(in plan, new NpcRuntimeTownCombatRandom1458(new SystemVanillaNpcRandom(random)), row.GetProperty("good").GetBoolean());
        const BindingFlags access = BindingFlags.Instance | BindingFlags.NonPublic;
        var rng = row.GetProperty("rng");
        Assert.Equal(rng.GetProperty("inext").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", access)!.GetValue(random)!);
        Assert.Equal(rng.GetProperty("SeedArray").EnumerateArray().Select(v => v.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", access)!.GetValue(random)!);
    }
}
