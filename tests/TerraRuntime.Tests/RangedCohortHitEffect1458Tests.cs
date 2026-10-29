using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class RangedCohortHitEffect1458Tests
{
    public static IEnumerable<object[]> Effects() => new[] { "parentHitEffects", "childHitEffects" }
        .SelectMany(g => RangedCohortLoot1458Tests.Rows(g, "cases")).Select(r => new object[] { r.GetRawText() });

    [Theory, MemberData(nameof(Effects))]
    public void Actual_hit_effect_components_match_all_physical_rng_and_maggot_allocations(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        int type = row.GetProperty("type").GetInt32(); bool lethal = row.GetProperty("lethal").GetBoolean();
        var original = row.GetProperty("before").GetProperty("npc");
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        RangedCohortLoot1458Tests.AssertRandom(row.GetProperty("before").GetProperty("rng"), random);
        var npc = new NpcSnapshot(new(0, new(1)), new(1), type, checked((short)type),
            Float(original.GetProperty("position")[0]), Float(original.GetProperty("position")[1]),
            Float(original.GetProperty("velocity")[0]), Float(original.GetProperty("velocity")[1]), 255, default,
            NpcSimulationState.Initial with { Life = original.GetProperty("life").GetInt32(), LifeMax = original.GetProperty("lifeMax").GetInt32() });
        var rolls = new SystemVanillaNpcRandom(random);
        if (!lethal)
        {
            var damage = row.GetProperty("damage");
            double raw = damage.ValueKind == JsonValueKind.String
                ? BitConverter.UInt64BitsToDouble(Convert.ToUInt64(damage.GetString(), 16)) : damage.GetDouble();
            RuntimeNpcNetworkCombatPipeline.ExecuteNpcNonlethalHitEffectsOnRandom1458(in npc, checked((int)raw), rolls);
        }
        else if (type == 632)
        {
            var store = new RuntimeNpcStore(200);
            store.SetVanillaSpawnContextSource(() => new(1f, 1, false));
            store.SetVanillaSpawnRandomSource(rolls);
            var state = new NpcStateUpdate(type, checked((short)type), npc.PositionX, npc.PositionY,
                npc.VelocityX, npc.VelocityY, 255, default, npc.Simulation);
            Assert.True(store.TrySpawn(0, in state, out var parent));
            Assert.True(store.TryStageMaggotDeathChildren(in parent, rolls));
            var children = row.GetProperty("after").GetProperty("activeNpcPeers").EnumerateArray().Where(p => p.GetProperty("slot").GetInt32() != 0).ToArray();
            Assert.Equal(children.Length + 1, store.ActiveCount);
            foreach (var expected in children)
            {
                Assert.True(store.TryGetActive(expected.GetProperty("slot").GetByte(), out var actual));
                var e = expected.GetProperty("state");
                Assert.Equal(e.GetProperty("type").GetInt32(), actual.Type);
                Assert.Equal(e.GetProperty("generation").GetUInt64(), actual.Handle.Generation.Value);
                Assert.Equal(e.GetProperty("position").EnumerateArray().Select(v => v.GetString()), new[] { RangedCohortLoot1458Tests.Bits(actual.PositionX), RangedCohortLoot1458Tests.Bits(actual.PositionY) });
                Assert.Equal(e.GetProperty("velocity").EnumerateArray().Select(v => v.GetString()), new[] { RangedCohortLoot1458Tests.Bits(actual.VelocityX), RangedCohortLoot1458Tests.Bits(actual.VelocityY) });
                Assert.Equal(e.GetProperty("life").GetInt32(), actual.Simulation.Life);
                Assert.Equal(e.GetProperty("lifeMax").GetInt32(), actual.Simulation.LifeMax);
                Assert.Equal(e.GetProperty("target").GetInt32(), actual.Target);
                Assert.Equal(e.GetProperty("timeLeft").GetInt32(), actual.Simulation.TimeLeft);
                Assert.Equal(e.GetProperty("wet").GetBoolean(), actual.Simulation.Wet);
            }
            Assert.True(store.TryGet(parent.Handle, out var retained)); Assert.Equal(parent, retained);
        }
        else VanillaRangedNpcDeath1458.ConsumeDedicatedLethalChoices(type, rolls);
        RangedCohortLoot1458Tests.AssertRandom(row.GetProperty("after").GetProperty("rng"), random);
    }

    [Fact]
    public void Unowned_ritual_peer_write_refuses_entire_strike_but_ordinary_archer_remains_hittable()
    {
        foreach (float ritual in new[] { -1f, 0f })
        {
            var store = new RuntimeNpcStore(200);
            var update = new NpcStateUpdate(379, 379, 800, 440, 0, 0, 255,
                new(0, 0, 0, ritual), NpcSimulationState.Initial);
            Assert.True(store.TrySpawnVanilla(in update, out var before));
            var executor = new RuntimeNpcDamageExecutor(store);
            var request = new NpcDamageRequest(before.Handle, DamageSource.Environment, 20);
            Assert.Equal(ritual == 0f, executor.TryApply(in request, out _));
            Assert.True(store.TryGet(before.Handle, out var after));
            if (ritual < 0f) Assert.Equal(before, after);
            else Assert.True(after.Simulation.Life < before.Simulation.Life);
        }
    }
    private static float Float(JsonElement value) => BitConverter.UInt32BitsToSingle(Convert.ToUInt32(value.GetString(), 16));
}
