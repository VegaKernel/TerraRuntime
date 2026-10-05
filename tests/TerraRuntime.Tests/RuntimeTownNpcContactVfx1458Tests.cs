using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcContactVfx1458Tests
{
    public static IEnumerable<object[]> OriginalCases()
    {
        using Stream stream = typeof(RuntimeTownNpcContactVfx1458Tests).Assembly.GetManifestResourceStream("TownContactVfx1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using JsonDocument json = JsonDocument.Parse(gzip);
        foreach (JsonElement row in json.RootElement.EnumerateArray()) yield return [row.Clone()];
    }

    [Theory]
    [MemberData(nameof(OriginalCases))]
    public void Original_pre_ai_visual_offer_and_town_vitals_are_owned(JsonElement row)
    {
        if (row.GetProperty("family").GetString() == "vitals") AssertVitals(row);
        else AssertVisual(row);
    }

    public static IEnumerable<object[]> OriginalRegenerationCases()
    {
        using Stream stream = typeof(RuntimeTownNpcContactVfx1458Tests).Assembly.GetManifestResourceStream("TownRegeneration1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using JsonDocument json = JsonDocument.Parse(gzip);
        foreach (JsonElement row in json.RootElement.EnumerateArray()) yield return [row.Clone()];
    }

    [Theory]
    [MemberData(nameof(OriginalRegenerationCases))]
    public void Original_friendly_regeneration_pins_strict_threshold_and_full_health_retention(JsonElement row)
    {
        int type = row.GetProperty("type").GetInt32();
        var input = new NpcStateUpdate(type, (short)type, 100, 100, 0, 0, 255, default,
            NpcSimulationState.Initial with { Life = row.GetProperty("life").GetInt32(), LifeMax = 250,
                FriendlyRegenerationCounter = row.GetProperty("counter").GetInt32() });
        var result = RuntimeTownNpcCombat1458.PlanFriendlyRegeneration(in input);
        Assert.Equal(row.GetProperty("resultLife").GetInt32(), result.Simulation.Life);
        Assert.Equal(row.GetProperty("resultCounter").GetInt32(), result.Simulation.FriendlyRegenerationCounter);
    }

    private static void AssertVisual(JsonElement row)
    {
        int type = row.GetProperty("type").GetInt32();
        var npcs = new RuntimeNpcStore();
        var input = new NpcStateUpdate(type, (short)type, 639, 440, 0, 0, 255, default,
            NpcSimulationState.Initial with { Life = 100, LifeMax = 100 });
        Assert.True(npcs.TrySpawn(0, in input, out var npc));
        var status = new RuntimeNpcBuffStatus1458(npcs);
        if (row.GetProperty("apply").GetBoolean()) Assert.True(status.TryApply(npc.Handle, 180));
        status.BeginWorldTick();
        Assert.True(status.TryGetStinky(npc.Handle, out bool flag)); Assert.Equal(row.GetProperty("flag").GetBoolean(), flag);
        var stream = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var inner = new ObservingStep(stream);
        var wrapper = new RuntimeNpcBuffAiStepper1458(inner, status, new SystemVanillaNpcRandom(stream),
            row.GetProperty("good").GetBoolean());
        Assert.Same(inner, NpcAiStateStepperComposition.FindCapability<ObservingStep>(wrapper));
        Assert.True(wrapper.TryStepState(in npc, out _)); Assert.Equal(1, inner.Calls);
        Assert.Equal(row.GetProperty("next").GetInt32(), inner.ObservedNext);
        Assert.True(status.TryGetVisualOffer(npc.Handle, out var visual));
        Assert.Equal(row.GetProperty("offered").GetBoolean(), visual.Offered);
        Assert.True(npcs.TryGet(npc.Handle, out var retained)); Assert.Equal(npc, retained);
    }

    private static void AssertVitals(JsonElement row)
    {
        var f = new RuntimeTownNpcContact1458Tests.Fixture("friendly", 0f, row.GetProperty("seed").GetInt32());
        VanillaWorldProgressionId[] milestones = [VanillaWorldProgressionId.KingSlime, VanillaWorldProgressionId.EyeOfCthulhu,
            VanillaWorldProgressionId.Deerclops, VanillaWorldProgressionId.EvilBoss, VanillaWorldProgressionId.Skeletron,
            VanillaWorldProgressionId.QueenBee, VanillaWorldProgressionId.Hardmode, VanillaWorldProgressionId.QueenSlime,
            VanillaWorldProgressionId.Destroyer, VanillaWorldProgressionId.Twins, VanillaWorldProgressionId.SkeletronPrime,
            VanillaWorldProgressionId.Plantera, VanillaWorldProgressionId.EmpressOfLight, VanillaWorldProgressionId.DukeFishron,
            VanillaWorldProgressionId.Golem, VanillaWorldProgressionId.LunaticCultist];
        int milestone = row.GetProperty("milestone").GetInt32(), books = row.GetProperty("books").GetInt32();
        var progression = new VanillaWorldProgressionState(milestone < 0 ? 0UL : 1UL << (int)milestones[milestone]);
        var facts = new RuntimeTownNpcCombatWorldFacts1458(progression, (books & 1) != 0, (books & 2) != 0);
        var combat = new RuntimeTownNpcCombat1458(f.Town, f.Npcs, new RuntimeProjectileStore(), f.Tiles,
            in facts, new(), false, false, new NpcRuntimeTownCombatRandom1458(f.Adapter));
        var input = new NpcStateUpdate(f.Before.Type, f.Before.NetId, 639, 440, .5f, 0f, 255, f.Before.Ai,
            f.Before.Simulation with { BaseDefense = 7, DefenseOverride = 99,
                Life = row.GetProperty("full").GetBoolean() ? 250 : 50 });
        Assert.True(f.Npcs.TryUpdate(f.Before.Handle, in input, out _)); f.Sink.Commits.Clear();
        var conditions = new RuntimeTownNpcScheduleConditions1458(true, false, false, false, false);
        Assert.Equal(0, f.Schedule.Tick(in conditions, [], f.Status, combat).RejectedCommits);
        Assert.True(f.Npcs.TryGet(f.Before.Handle, out var actual)); Assert.Single(f.Sink.Commits);
        Assert.Equal(row.GetProperty("defense").GetInt32(), actual.Simulation.DefenseOverride);
        Assert.Equal(row.GetProperty("life").GetInt32(), actual.Simulation.Life);
        Assert.Equal(row.GetProperty("lifeMax").GetInt32(), actual.Simulation.LifeMax);
        Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Next());
    }

    private sealed class ObservingStep(VanillaUnifiedRandom1458 random) : INpcAiStateStepper
    {
        internal int Calls, ObservedNext;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            Calls++; ObservedNext = random.Next();
            next = new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY,
                npc.Target, npc.Ai, npc.Simulation); return true;
        }
    }
}
