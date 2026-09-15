using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class SkeletronHandPhaseTests
{
    private static readonly JsonElement[] Rows = Read();
    public static TheoryData<int> Cases => new(Enumerable.Range(0, Rows.Length));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Hand_phase_matches_original_with_frozen_parent(int index)
    {
        var row = Rows[index];
        int mode = row.GetProperty("mode").GetInt32();
        bool good = row.GetProperty("good").GetBoolean();
        float difficulty = mode + 1 + (good ? 1 : 0);
        var store = new RuntimeNpcStore();
        store.SetVanillaSpawnContextSource(() => new(difficulty, 1, good));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHead, 1000, 1000, 0, 0, 0)
            { StartSlot = 10, InitialAi = new(1, row.GetProperty("parentPhase").GetSingle(), 0, 0) }, out _));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHand, 1000, 1000, 0, 0, 0)
            { StartSlot = 11, InitialAi = new(row.GetProperty("side").GetSingle(), 10, row.GetProperty("state").GetSingle(), row.GetProperty("timer").GetSingle()) }, out var hand));
        var input = new NpcStateUpdate(hand.Type, hand.NetId,
            row.GetProperty("beforeX").GetSingle(), row.GetProperty("beforeY").GetSingle(),
            row.GetProperty("beforeVx").GetSingle(), row.GetProperty("beforeVy").GetSingle(), hand.Target, hand.Ai, hand.Simulation with { LocalAi = Ai(row.GetProperty("beforeLocal")) });
        Assert.True(store.TryUpdate(hand.Handle, in input, out _));
        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        ai.SetWorldConditions(false, false, goodWorld: good, expertMode: difficulty >= 2, masterMode: difficulty >= 3);
        ai.SetCandidates([new(0, row.GetProperty("targetX").GetSingle(), row.GetProperty("targetY").GetSingle(), 0, true, false, false, false)]);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(store).Tick(new HandOnly(ai)).Applied);
        Assert.True(store.TryGet(hand.Handle, out var after));
        Assert.Equal(Ai(row.GetProperty("ai")), after.Ai);
        Assert.Equal(Ai(row.GetProperty("local")), after.Simulation.LocalAi);
        Assert.Equal(row.GetProperty("x").GetSingle(), after.PositionX);
        Assert.Equal(row.GetProperty("y").GetSingle(), after.PositionY);
        Assert.Equal(row.GetProperty("vx").GetSingle(), after.VelocityX);
        Assert.Equal(row.GetProperty("vy").GetSingle(), after.VelocityY);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(after.TypeIdentity, out var definition));
        Assert.Equal(row.GetProperty("damage").GetInt32(), after.Simulation.DamageOverride ?? definition.Damage);
        Assert.Equal(row.GetProperty("defense").GetInt32(), after.Simulation.DefenseOverride ?? definition.Defense);
        Assert.Equal(row.GetProperty("life").GetInt32(), after.Simulation.Life);
        Assert.Equal(row.GetProperty("lifeMax").GetInt32(), after.Simulation.LifeMax);
        Assert.Equal(row.GetProperty("timeLeft").GetInt32(), after.Simulation.TimeLeft);
        Assert.Equal(row.GetProperty("target").GetInt32(), after.Target);
        Assert.Equal(row.GetProperty("direction").GetInt32(), after.Simulation.DirectionX);
        Assert.Equal(row.GetProperty("directionY").GetInt32(), after.Simulation.DirectionY);
        Assert.Equal(row.GetProperty("spriteDirection").GetInt32(), after.Simulation.SpriteDirection);
        Assert.True(row.GetProperty("active").GetBoolean());
    }

    private static NpcAiState Ai(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle(), value[3].GetSingle());

    [Fact]
    public void Vanilla_creation_initializes_vertical_direction_without_changing_explicit_storage_or_updates()
    {
        var store = new RuntimeNpcStore();
        var state = new NpcStateUpdate(1, 1, 0, 0, 0, 0, 0, default, NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(0, in state, out var explicitNpc));
        Assert.Equal(0, explicitNpc.Simulation.DirectionY);
        Assert.True(store.TrySpawnVanilla(in state, out var vanilla, startSlot: 10));
        Assert.Equal(1, vanilla.Simulation.DirectionY);
        Assert.True(store.TryUpdate(vanilla.Handle, in state, out var updated));
        Assert.Equal(0, updated.Simulation.DirectionY);
        var upward = state with { Simulation = state.Simulation with { DirectionY = -1 } };
        Assert.True(store.TrySpawnVanilla(in upward, out var supplied, startSlot: 11));
        Assert.Equal(-1, supplied.Simulation.DirectionY);
        Assert.True(store.TryDespawn(vanilla.Handle));
        store.UpdateProtectedSpawnSlots(); store.UpdateProtectedSpawnSlots();
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHand, 1000, 1000, 0, 0, 0)
            { StartSlot = 10 }, out var reused));
        Assert.Equal(vanilla.Handle.Slot, reused.Handle.Slot);
        Assert.NotEqual(vanilla.Handle.Generation, reused.Handle.Generation);
        Assert.Equal(1, reused.Simulation.DirectionY);
    }

    private static JsonElement[] Read()
    {
        using var resource = typeof(SkeletronHandPhaseTests).Assembly.GetManifestResourceStream("SkeletronHandPhase1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal("31dafc494d3e1c61ca2c6fb4b8f2bc991528d9caea72ba14d57a83970d3e1592", Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    private sealed class HandOnly(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return npc.Type == 36 && inner.TryStepState(in npc, out next);
        }
    }
}
