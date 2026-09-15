using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SkeletronHandPhaseTests
{
    private static readonly JsonElement[] Rows = Read("SkeletronHandPhase1458", "31dafc494d3e1c61ca2c6fb4b8f2bc991528d9caea72ba14d57a83970d3e1592");
    private static readonly JsonElement[] RedHatRows = Read("SkeletronHandRedHat1458", "aa4e8799b58db7c391c77e8a3a53fb8bdbc82679a299e8facc9da18d5a9451dc");
    public static TheoryData<int> RedHatCases => new(Enumerable.Range(0, RedHatRows.Length));
    public static TheoryData<int> Cases => new(Enumerable.Range(0, Rows.Length));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Hand_phase_matches_original_with_frozen_parent(int index)
    {
        CheckPhase(Rows[index], false);
    }

    [Theory]
    [MemberData(nameof(RedHatCases))]
    public void RedHat_hand_phase_matches_original_with_frozen_parent(int index) => CheckPhase(RedHatRows[index], true);

    private static void CheckPhase(JsonElement row, bool redHat)
    {
        int mode = row.GetProperty("mode").GetInt32();
        bool good = row.GetProperty("good").GetBoolean();
        float difficulty = mode + 1 + (good ? 1 : 0);
        var store = new RuntimeNpcStore();
        store.SetVanillaSpawnContextSource(() => new(difficulty, 1, good));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHead, 1000, 1000, 0, 0, 0)
            { StartSlot = 10, InitialAi = new(1, row.GetProperty("parentPhase").GetSingle(), 0, redHat ? 1 : 0) }, out _));
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

    private static readonly JsonElement[] ParentRows = Read("SkeletronHandParent1458", "1662a404af87b6de3eeeea184af16173facd77af938af4bf5384e2904f16c76e");
    public static TheoryData<int> ParentCases => new(Enumerable.Range(0, ParentRows.Length));

    [Theory]
    [MemberData(nameof(ParentCases))]
    public void Parent_lifecycle_matches_original_and_publishes_only_terminal_despawn(int index)
    {
        var row = ParentRows[index];
        int mode = row.GetProperty("mode").GetInt32();
        bool good = row.GetProperty("good").GetBoolean();
        float difficulty = mode + 1 + (good ? 1 : 0);
        var sink = new Capture();
        var store = new RuntimeNpcStore(commitSink: sink);
        store.SetVanillaSpawnContextSource(() => new(difficulty, 1, good));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(new TerraRuntime.Contracts.Gameplay.NpcTypeId(row.GetProperty("parentType").GetInt32()), 1000, 1000, 0, 0, 0)
            { StartSlot = 10, InitialAi = new(1, 0, 0, 0) }, out var parent));
        if (!row.GetProperty("parentActive").GetBoolean()) Assert.True(store.TryDespawn(parent.Handle));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHand, 1000, 1000, 0, 0, 0)
            { StartSlot = 11, InitialAi = new(row.GetProperty("side").GetSingle(), 10, row.GetProperty("state").GetSingle(), 0),
                InitialLocalAi = new(0, 0, 0, 77) }, out var hand));
        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        ai.SetWorldConditions(false, false, goodWorld: good, expertMode: difficulty >= 2, masterMode: difficulty >= 3);
        ai.SetCandidates([new(0, 1510, 1021, 0, true, false, false, false)]);
        var executor = new RuntimeNpcAiStateExecutor(store);
        foreach (var frame in row.GetProperty("frames").EnumerateArray())
        {
            sink.Commits.Clear();
            Assert.Equal(1, executor.Tick(new HandOnly(ai)).Applied);
            var (kind, after) = Assert.Single(sink.Commits);
            bool active = frame.GetProperty("active").GetBoolean();
            Assert.Equal(active ? NpcStateCommitKind.Update : NpcStateCommitKind.Despawn, kind);
            Assert.Equal(active, store.TryGet(hand.Handle, out _));
            Assert.Equal(Ai(frame.GetProperty("ai")), after.Ai);
            Assert.Equal(Ai(frame.GetProperty("local")), after.Simulation.LocalAi);
            Assert.Equal(frame.GetProperty("x").GetSingle(), after.PositionX);
            Assert.Equal(frame.GetProperty("y").GetSingle(), after.PositionY);
            Assert.Equal(frame.GetProperty("vx").GetSingle(), after.VelocityX);
            Assert.Equal(frame.GetProperty("vy").GetSingle(), after.VelocityY);
            // Vanilla's internal -1 death sentinel is represented by zero life plus immediate slot removal.
            Assert.Equal(Math.Max(0, frame.GetProperty("life").GetInt32()), after.Simulation.Life);
            Assert.Equal(frame.GetProperty("lifeMax").GetInt32(), after.Simulation.LifeMax);
            Assert.Equal(frame.GetProperty("timeLeft").GetInt32(), after.Simulation.TimeLeft);
            Assert.Equal(frame.GetProperty("target").GetInt32(), after.Target);
            Assert.Equal(frame.GetProperty("direction").GetInt32(), after.Simulation.DirectionX);
            Assert.Equal(frame.GetProperty("directionY").GetInt32(), after.Simulation.DirectionY);
            Assert.Equal(frame.GetProperty("spriteDirection").GetInt32(), after.Simulation.SpriteDirection);
        }
    }

    [Fact]
    public void Clearing_parent_marker_preserves_live_damage_and_orphan_retains_local_marker()
    {
        var store = new RuntimeNpcStore();
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHead, 1000, 1000, 0, 0, 0)
            { StartSlot = 10, InitialAi = new(1, 0, 0, 1) }, out var parent));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHand, 1000, 1000, 0, 0, 0)
            { StartSlot = 11, InitialAi = new(-1, 10, 0, 0) }, out var hand));
        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        ai.SetCandidates([new(0, 1510, 1021, 0, true, false, false, false)]);
        var executor = new RuntimeNpcAiStateExecutor(store);
        Assert.Equal(1, executor.Tick(new HandOnly(ai)).Applied);
        Assert.True(store.TryGet(hand.Handle, out hand));
        Assert.Equal(26, hand.Simulation.DamageOverride);
        var parentUpdate = new NpcStateUpdate(parent.Type, parent.NetId, parent.PositionX, parent.PositionY, 0, 0,
            parent.Target, parent.Ai with { Ai3 = 0 }, parent.Simulation);
        Assert.True(store.TryUpdate(parent.Handle, in parentUpdate, out _));
        Assert.Equal(1, executor.Tick(new HandOnly(ai)).Applied);
        Assert.True(store.TryGet(hand.Handle, out hand));
        Assert.Equal(0, hand.Simulation.LocalAi.Ai3);
        Assert.Equal(26, hand.Simulation.DamageOverride);
        Assert.Equal(3, hand.Ai.Ai3);
        Assert.True(store.TryDespawn(parent.Handle));
        var orphan = new NpcStateUpdate(hand.Type, hand.NetId, hand.PositionX, hand.PositionY, 0, 0, hand.Target,
            hand.Ai, hand.Simulation with { LocalAi = new(0, 0, 0, 1), DamageOverride = 777 });
        Assert.True(store.TryUpdate(hand.Handle, in orphan, out _));
        Assert.Equal(1, executor.Tick(new HandOnly(ai)).Applied);
        Assert.True(store.TryGet(hand.Handle, out hand));
        Assert.Equal(1, hand.Simulation.LocalAi.Ai3);
        Assert.Equal(26, hand.Simulation.DamageOverride);
        Assert.Equal(10, hand.Ai.Ai2);
    }

    [Theory]
    [InlineData(1, 20, -20, 19, -15)]
    [InlineData(4, 20, -20, 15, -19)]
    [InlineData(4, -20, 20, -15, 19)]
    public void RedHat_windup_caps_take_precedence_over_expert(int state, float vx, float vy, float expectedVx, float expectedVy)
    {
        var store = new RuntimeNpcStore();
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHead, 1000, 1000, 0, 0, 0)
            { StartSlot = 10, InitialAi = new(1, 0, 0, 1) }, out _));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHand, 1000, 1000, vx, vy, 0)
            { StartSlot = 11, InitialAi = new(-1, 10, state, 0) }, out var hand));
        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        ai.SetWorldConditions(false, false, expertMode: true);
        ai.SetCandidates([new(0, 1510, 1021, 0, true, false, false, false)]);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(store).Tick(new HandOnly(ai)).Applied);
        Assert.True(store.TryGet(hand.Handle, out hand));
        Assert.Equal(expectedVx, hand.VelocityX);
        Assert.Equal(expectedVy, hand.VelocityY);
    }

    [Fact]
    public void Orphan_terminal_world_motion_removes_slot_before_next_npc()
    {
        var sink = new Capture();
        var store = new RuntimeNpcStore(commitSink: sink);
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHand, 1000, 1000, 2, 3, 0)
            { StartSlot = 11, InitialAi = new(-1, 10, 50, 0) }, out var hand));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronHand, 1000, 1000, 0, 0, 0)
            { StartSlot = 12, InitialAi = new(-1, 10, 0, 0) }, out _));
        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        var motion = new VanillaNpcWorldMotionAiStepper(ai, new WorldTileStore(new WorldDimensions(400, 400)));
        var watch = new WatchRemoval(motion, store, hand.Handle);
        sink.Commits.Clear();
        Assert.Equal(2, new RuntimeNpcAiStateExecutor(store).Tick(watch).Applied);
        Assert.True(watch.Checked);
        var (kind, terminal) = sink.Commits[0];
        Assert.Equal(NpcStateCommitKind.Despawn, kind);
        Assert.Equal(hand.PositionX + 2, terminal.PositionX);
        Assert.Equal(hand.PositionY + 3, terminal.PositionY);
        Assert.Equal(937, terminal.Simulation.TimeLeft);
        Assert.Equal(0, terminal.Simulation.Life);
    }

    private sealed class WatchRemoval(INpcAiStateStepper inner, RuntimeNpcStore store, NpcHandle removed)
        : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public bool Checked { get; private set; }
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            if (npc.Handle.Slot == 12)
            {
                Assert.False(store.TryGet(removed, out _));
                Checked = true;
            }
            return inner.TryStepState(in npc, out next);
        }
    }

    private sealed class Capture : INpcStateCommitSink
    {
        public List<(NpcStateCommitKind Kind, NpcSnapshot Snapshot)> Commits { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Commits.Add((kind, snapshot));
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

    private static JsonElement[] Read(string name, string hash)
    {
        using var resource = typeof(SkeletronHandPhaseTests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
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
