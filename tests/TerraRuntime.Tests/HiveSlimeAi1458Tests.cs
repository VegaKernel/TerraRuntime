using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class HiveSlimeAi1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(HiveSlimeAi1458Tests).Assembly.GetManifestResourceStream("HiveSlimeAi1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray())
            yield return [row.Clone()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Actual_AI001_Hive_producer_matches_LOS_capacity_difficulty_and_shared_stream(JsonElement row)
    {
        bool B(string key) => row.GetProperty(key).GetBoolean();
        int I(string key) => row.GetProperty(key).GetInt32();
        var random = new SystemVanillaNpcRandom(I("seed"));
        var store = new RuntimeNpcStore(200);
        store.SetVanillaSpawnRandomSource(random);
        float difficulty = I("mode") + 1 + (B("good") ? 1 : 0);
        store.SetVanillaSpawnContextSource(() => new(difficulty, 1, B("good")));
        var input = new NpcStateUpdate(1, 1, 1000.75f, 1000.25f, .5f, 0f, 0,
            new(-100f, 1124f, 0f, 0f), NpcSimulationState.Initial with { DirectionX = 1, DirectionY = 1 });
        Assert.True(store.TrySpawn(150, in input, out var before));
        if (B("full"))
            for (byte slot = 0; slot < 200; slot++)
                if (slot != 150)
                    Assert.True(store.TrySpawn(slot, in input, out _));

        var tiles = new WorldTileStore(new(600, 500));
        if (B("blocked"))
            for (int y = 59; y <= 70; y++)
                tiles.Set(67, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var players = new Players();
        var targeting = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        targeting.EnableBlueSlimeMotion(140d);
        targeting.SetWorldConditions(true, false, B("good"), difficulty >= 2f, difficulty >= 3f);
        targeting.SetCandidates([new(0, 1110f, 1021f, 0, true, false, false, false)]);
        targeting.SetPlayerSnapshotLookup(players);
        var facts = new VanillaSlimeContainedFacts1458(140d, 200d, B("good"), false, false,
            false, false, false, false, false, false, false, false, false, false, (int)VanillaMoonPhase.Full);
        var world = new VanillaSlimeContainedWorld1458(tiles);
        Assert.True(targeting.TryGetCandidate(0, out var target));
        Assert.True(world.TryCapture(in before, in target, out var fence));
        Assert.Equal(B("sight"), fence.CanHit);
        targeting.SetSlimeContainedOwner(store, () => facts, world);
        Assert.True(targeting.TryStepState(in before, out var placeholder));
        Assert.True(store.TryUpdateUnpublished(before.Handle, in placeholder, out var accepted));
        Assert.True(targeting.TryGetSlimeContainedPlan(in before, in accepted, out var planned));
        var completed = targeting.CompleteSlimeContainedPlan(in before, in accepted, in planned);
        Assert.True(completed.IsActive);
        for (int i = 0; i < 4; i++)
            Assert.Equal(row.GetProperty("ai")[i].GetSingle(), i switch
            {
                0 => completed.Ai.Ai0, 1 => completed.Ai.Ai1, 2 => completed.Ai.Ai2, _ => completed.Ai.Ai3
            });
        var children = row.GetProperty("children");
        Assert.Equal(B("full") ? 200 : 1 + children.GetArrayLength(), store.ActiveCount);
        foreach (var childRow in children.EnumerateArray())
        {
            Assert.True(store.TryGetActive((byte)childRow.GetProperty("slot").GetInt32(), out var child));
            Assert.Equal(childRow.GetProperty("type").GetInt32(), child.Type);
            Assert.Equal(childRow.GetProperty("x").GetSingle(), child.PositionX);
            Assert.Equal(childRow.GetProperty("y").GetSingle(), child.PositionY);
            Assert.Equal(childRow.GetProperty("vx").GetSingle(), child.VelocityX);
            Assert.Equal(childRow.GetProperty("vy").GetSingle(), child.VelocityY);
            Assert.Equal(childRow.GetProperty("life").GetInt32(), child.Simulation.Life);
            Assert.Equal(childRow.GetProperty("lifeMax").GetInt32(), child.Simulation.LifeMax);
            Assert.Equal(childRow.GetProperty("scale").GetSingle(), child.Simulation.Scale);
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(child.TypeIdentity, child.NetIdentity, out var definition));
            Assert.True(definition.TryResolveHitbox(child.Simulation, out var body));
            Assert.Equal(childRow.GetProperty("width").GetInt32(), body.Width);
            Assert.Equal(childRow.GetProperty("height").GetInt32(), body.Height);
            Assert.Equal(childRow.GetProperty("replace").GetBoolean(), child.Simulation.CanBeReplacedByOtherNpcs);
            Assert.Equal(60f, child.Ai.Ai1);
            Assert.Equal(60f, child.Simulation.LocalAi.Ai0);
            Assert.False(child.Simulation.Wet);
        }
        Assert.Equal(I("next"), random.SourceRandom.Next());
    }

    private sealed class Players : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot state)
        {
            state = new(new(new(0), new(1)), new(1), 0, 0, 0, 0, 0, 0, 1100, 1000,
                0, 0, 0, 0, 0, 0, 0, 0, 0);
            return slot.Value == 0;
        }
    }

    private sealed class Rejecting : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        { next = default; return false; }
    }
}
