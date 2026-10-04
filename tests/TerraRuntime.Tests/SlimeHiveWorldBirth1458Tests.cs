using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SlimeHiveWorldBirth1458Tests
{
    public static IEnumerable<object[]> BirthCases()
    {
        using var stream = typeof(SlimeHiveWorldBirth1458Tests).Assembly.GetManifestResourceStream("BeeBirthWet1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray())
            yield return [row.Clone()];
    }

    [Theory]
    [MemberData(nameof(BirthCases))]
    public void Actual_NewNPC_constructor_body_wet_and_full_capacity_creation_draw_match(JsonElement row)
    {
        int seed = row.GetProperty("seed").GetInt32();
        bool good = row.GetProperty("good").GetBoolean();
        bool full = row.GetProperty("full").GetBoolean();
        int liquid = row.GetProperty("liquid").GetInt32();
        var random = new VanillaUnifiedRandom1458(seed);
        var store = new RuntimeNpcStore(200);
        store.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random));
        store.SetVanillaSpawnContextSource(() => new(good ? 2f : 1f, 1, good));
        var input = new NpcStateUpdate(1, 1, 1000, 1000, 0, 0, 0, default, NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(199, in input, out var parent));
        if (full)
            for (byte slot = 0; slot < 199; slot++)
                Assert.True(store.TrySpawn(slot, in input, out _));
        random.CopyStateFrom(new VanillaUnifiedRandom1458(seed));
        var tiles = new WorldTileStore(new(256, 256));
        for (int x = 59; x < 65; x++)
            for (int y = 59; y < 65; y++)
            {
                var tile = new WorldTile
                {
                    LiquidAmount = liquid == 0 ? (byte)0 : liquid == 5 ? (byte)1 : byte.MaxValue,
                    LiquidKind = liquid switch
                    { 2 => WorldLiquidKind.Lava, 3 => WorldLiquidKind.Honey, 4 => WorldLiquidKind.Shimmer, _ => WorldLiquidKind.Water }
                };
                if (liquid == 6 && y == 62)
                {
                    tile.LiquidAmount = 0;
                    tile.Type = 1;
                    tile.Flags = WorldTileFlags.Active;
                    tile.Shape = 2;
                }
                tiles.Set(x, y, tile);
            }
        var world = new VanillaSlimeContainedWorld1458(tiles);
        var target = new VanillaNpcTargetCandidate(0, 1110, 1021, 0, true, false, false, false);
        Assert.True(world.TryCapture(in parent, in target, out var fence));
        Assert.True(store.TryCreateAiSpawnPreview(in parent, random, out var preview));
        var intent = new NpcAiSpawnIntent(new(row.GetProperty("type").GetInt32()), 1000, 1000, 0, 0, 255);
        bool allocated = preview!.TryStageHiveChild(in intent, out var birth);
        Assert.Equal(!full, allocated);
        if (allocated)
        {
            Assert.True(fence.TryReadBirthWet(in birth, out bool wet));
            Assert.Equal(row.GetProperty("wet").GetBoolean(), wet);
            Assert.True(preview.TryFinishHiveChild(in birth, 0, 0, wet));
        }
        var placeholder = Update(parent);
        Assert.True(store.TryUpdateUnpublished(parent.Handle, in placeholder, out var accepted));
        Assert.True(preview.TryAdopt(in accepted, in placeholder, out _));
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
        if (!allocated)
            return;
        Assert.True(store.TryGet(birth.Handle, out var child));
        Assert.Equal(row.GetProperty("x").GetSingle(), child.PositionX);
        Assert.Equal(row.GetProperty("y").GetSingle(), child.PositionY);
        Assert.Equal(row.GetProperty("scale").GetSingle(), child.Simulation.Scale);
        Assert.Equal(row.GetProperty("honeyWet").GetBoolean(), child.Simulation.LiquidContact == NpcLiquidContactKind.Honey);
        Assert.Equal(row.GetProperty("lavaWet").GetBoolean(), child.Simulation.LiquidContact == NpcLiquidContactKind.Lava);
        Assert.Equal(row.GetProperty("noTile").GetBoolean(), child.Simulation.NoTileCollide);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(child.TypeIdentity, child.NetIdentity, out var definition));
        Assert.True(definition.TryResolveHitbox(child.Simulation, out var body));
        Assert.Equal(row.GetProperty("width").GetInt32(), body.Width);
        Assert.Equal(row.GetProperty("height").GetInt32(), body.Height);
    }

    [Theory]
    [InlineData((byte)0, (byte)1, true)]
    [InlineData((byte)2, (byte)0, false)]
    public void Real_world_executor_updates_a_new_higher_slot_this_tick_and_retains_a_lower_slot_birth(
        byte parentSlot, byte childSlot, bool updatedSameTick)
    {
        var sink = new Sink();
        var random = new SystemVanillaNpcRandom(18);
        var store = new RuntimeNpcStore(4, sink);
        store.SetVanillaSpawnRandomSource(random);
        store.SetVanillaSpawnContextSource(() => new(1f, 1, false));
        var input = new NpcStateUpdate(1, 1, 1000.75f, 1000.25f, .5f, 0, 0,
            new(-100, 1124, 0, 0), NpcSimulationState.Initial with { DirectionX = 1, DirectionY = 1 });
        Assert.True(store.TrySpawn(parentSlot, in input, out var before));
        sink.Events.Clear();
        var tiles = new WorldTileStore(new(600, 500));
        var targeting = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        targeting.EnableBlueSlimeMotion(140d);
        targeting.SetWorldConditions(true, false, false, false, false);
        targeting.SetCandidates([new(0, 1110f, 1021f, 0, true, false, false, false)]);
        targeting.SetPlayerSnapshotLookup(new Players());
        var facts = new VanillaSlimeContainedFacts1458(140, 200, false, false, false, false,
            false, false, false, false, false, false, false, false, false, (int)VanillaMoonPhase.Full);
        targeting.SetSlimeContainedOwner(store, () => facts, new VanillaSlimeContainedWorld1458(tiles));
        var world = new VanillaNpcWorldMotionAiStepper(targeting, tiles, 140d);
        var summary = new RuntimeNpcAiStateExecutor(store).Tick(world);
        Assert.Equal(updatedSameTick ? 2 : 1, summary.Applied);
        Assert.Equal(0, summary.Rejected);
        Assert.True(store.TryGetActive(childSlot, out var child));
        Assert.Equal(VanillaNpcIds.Bee.Value, child.Type);
        Assert.Equal(updatedSameTick ? 2UL : 1UL, child.Revision.Value);
        Assert.Equal(!updatedSameTick, store.HasPendingBirth(child.Handle));
        Assert.Equal(updatedSameTick ? 1 : 0, sink.Events.Count(e => e.Npc.Handle == child.Handle));
        if (!updatedSameTick)
        {
            Assert.Equal(60f, child.Ai.Ai1);
            Assert.Equal(60f, child.Simulation.LocalAi.Ai0);
            new RuntimeNpcAiStateExecutor(store).Tick(world);
            Assert.Single(sink.Events, e => e.Npc.Handle == child.Handle);
        }
    }

    private static NpcStateUpdate Update(in NpcSnapshot npc) => new(npc.Type, npc.NetId,
        npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);
    private sealed class Sink : INpcStateCommitSink
    {
        internal readonly List<(NpcStateCommitKind Kind, NpcSnapshot Npc)> Events = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc) => Events.Add((kind, npc));
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
