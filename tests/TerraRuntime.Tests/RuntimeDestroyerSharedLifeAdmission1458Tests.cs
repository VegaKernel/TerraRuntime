using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class RuntimeDestroyerSharedLifeAdmission1458Tests
{
    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)]
    public void Rejected_lethal_shared_life_preserves_both_actors_pending_birth_and_rng(int ingress, bool pressure)
    {
        var f = new Fixture();
        if (pressure) Assert.True(f.Items.TryReserveDropSlot(out _));
        else
        {
            var excessiveMoney = State(f.Root) with { Simulation = f.Root.Simulation with { MoneyValue = float.MaxValue } };
            Assert.True(f.Npcs.TryUpdate(f.Root.Handle, in excessiveMoney, out f.Root));
            Assert.Equal(float.MaxValue, f.Root.Simulation.MoneyValue);
        }
        Assert.True(f.Npcs.TryRetainPendingBirth(in f.Segment));
        f.Sink.Events.Clear();
        var random = f.Random.Clone(); var root = f.Root; var segment = f.Segment;
        f.Hit(ingress, 1000, accepted: false);
        Assert.True(f.Npcs.TryGet(root.Handle, out var retainedRoot));
        Assert.True(f.Npcs.TryGet(segment.Handle, out var retainedSegment));
        Assert.Equal(root, retainedRoot); Assert.Equal(segment, retainedSegment);
        Assert.True(f.Npcs.HasPendingBirth(segment.Handle));
        Assert.Empty(f.Sink.Events); Assert.Equal(0, f.Items.ActiveCount);
        Assert.True(random.HasSameState(f.Random));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Accepted_shared_life_uses_root_and_commits_segment_only_once(int ingress)
    {
        var f = new Fixture(); Assert.True(f.Npcs.TryRetainPendingBirth(in f.Segment));
        f.Sink.Events.Clear(); var random = f.Random.Clone(); var before = f.Segment;
        f.Hit(ingress, 50, accepted: true);
        Assert.True(f.Npcs.TryGet(f.Root.Handle, out var root));
        Assert.True(f.Npcs.TryGet(before.Handle, out var segment));
        Assert.Equal(50, root.Simulation.Life); Assert.Equal(50, segment.Simulation.Life);
        Assert.Equal(before.Revision.Value + 1, segment.Revision.Value);
        Assert.False(f.Npcs.HasPendingBirth(segment.Handle));
        var birth = Assert.Single(f.Sink.Events, e => e.Kind == NpcStateCommitKind.Spawn);
        Assert.Equal(ingress == 0 ? 50 : 10, birth.State.Simulation.Life);
        Assert.True(random.HasSameState(f.Random));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Stale_shared_root_before_or_during_admission_cannot_commit_segment(bool during)
    {
        var f = new Fixture(); Assert.True(f.Npcs.TryRetainPendingBirth(in f.Segment));
        var originalRoot = f.Root; var originalSegment = f.Segment;
        var changed = State(f.Root) with { Simulation = f.Root.Simulation with { Life = 99 } };
        bool Admit(in NpcSnapshot pending) { Assert.True(f.Npcs.TryUpdate(originalRoot.Handle, in changed, out _)); return true; }
        var executor = new RuntimeNpcDamageExecutor(f.Npcs, lethalAdmission: during ? Admit : null);
        if (!during) Assert.True(f.Npcs.TryUpdate(f.Root.Handle, in changed, out _));
        f.Sink.Events.Clear();
        var request = new NpcDamageRequest(f.Segment.Handle, DamageSource.Environment, 1000);
        Assert.False(executor.TryApplyUnpublished(in request, out _, out _, out _, out _, originalRoot));
        Assert.True(f.Npcs.TryGet(originalSegment.Handle, out var segment)); Assert.Equal(originalSegment, segment);
        Assert.True(f.Npcs.HasPendingBirth(segment.Handle));
        Assert.DoesNotContain(f.Sink.Events, e => e.State.Handle == segment.Handle);
    }

    private static NpcStateUpdate State(in NpcSnapshot n) => new(n.Type, n.NetId, n.PositionX, n.PositionY,
        n.VelocityX, n.VelocityY, n.Target, n.Ai, n.Simulation);

    private sealed class Sink : INpcStateCommitSink
    {
        public List<(NpcStateCommitKind Kind, NpcSnapshot State)> Events { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Events.Add((kind, snapshot));
    }
    private sealed class Fixture : IRuntimePlayerSlotSnapshotLookup
    {
        public Sink Sink { get; } = new();
        public RuntimeNpcStore Npcs { get; }
        public RuntimeWorldItemStore Items { get; } = new();
        public VanillaUnifiedRandom1458 Random { get; } = new(1458);
        public NpcSnapshot Root;
        public NpcSnapshot Segment;
        private readonly RuntimeNpcNetworkCombatPipeline pipeline;
        private readonly PlayerHandle player = new(new(0), new(1));
        public Fixture()
        {
            Npcs = new(commitSink: Sink);
            Assert.True(Npcs.TrySpawnVanilla(new(VanillaNpcIds.Destroyer.Value, (short)VanillaNpcIds.Destroyer.Value,
                1000, 1000, 0, 0, 0, default, NpcSimulationState.Initial), out Root));
            var root = State(Root) with { Simulation = Root.Simulation with { Life = 100, LifeMax = 100, DefenseOverride = 0 } };
            Assert.True(Npcs.TryUpdate(Root.Handle, in root, out Root));
            Assert.True(Npcs.TrySpawnVanilla(new(VanillaNpcIds.DestroyerBody.Value, (short)VanillaNpcIds.DestroyerBody.Value,
                1000, 1000, 0, 0, 0, new(0, 0, 0, Root.Handle.Slot), NpcSimulationState.Initial), out Segment));
            var segment = State(Segment) with { Simulation = Segment.Simulation with { Life = 10, LifeMax = 100, DefenseOverride = 0 } };
            Assert.True(Npcs.TryUpdate(Segment.Handle, in segment, out Segment));
            var owner = new PlayerAuthority(null, null);
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1), session.Handle);
            Assert.Equal(player, session.Handle);
            Assert.True(owner.TryApply(new PlayerSpawnRuntimeCommand(connection, session,
                new PlayerSpawnCommitRequest(session.Slot, 1000, 1000, 0, 0, 0, 0, 0))));
            Assert.Equal(PlayerSpawnCommitResult.Committed, owner.LastSpawnCommitResult);
            // A genuine bow is outside the direct-melee calculation: client28 takes its explicit compatibility path.
            Assert.True(owner.TryApply(new PlayerEquipmentRuntimeCommand(connection,
                new PlayerEquipmentCommitRequest(player.Slot, 0, 1, 0, (short)VanillaItemIds.WoodenBow.Value, 0))));
            Assert.Equal(0, owner.RejectedEquipmentUpdates);
            pipeline = new(Npcs, Items, this, owner, static () => 0, null,
                new(Items), null, new RuntimeWorldClock(0, true, 0, 0, 1), new(), false, false, lootRandom: Random);
        }
        public void Hit(int ingress, int damage, bool accepted)
        {
            if (ingress == 0)
            {
                var wire = new TerrariaNpcDamageState(Segment.Handle.Slot,
                    RuntimeNpcPacketProjection.ToProtocolGeneration(Segment.Handle.Generation), (short)damage, 0, 2, 0);
                Assert.Equal(accepted ? RuntimeNpcNetworkDamageResult.Committed : RuntimeNpcNetworkDamageResult.Rejected,
                    pipeline.TryApply(new(GameCommandSourceId.FromConnection(1), player), in wire));
            }
            else if (ingress == 1)
                Assert.Equal(accepted ? RuntimeProjectileNpcDamageResult.Committed : RuntimeProjectileNpcDamageResult.Rejected,
                    pipeline.TryStrikeServerPlayerMelee(player, Segment.Handle, damage, 0, false, 0, 1));
            else
                Assert.Equal(accepted ? RuntimeTownNpcMeleeDamageResult1458.Committed : RuntimeTownNpcMeleeDamageResult1458.Rejected,
                    pipeline.TryStrikeEnvironment(Segment.Handle, damage));
        }
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            snapshot = default(PlayerStateSnapshot) with { Player = player, Revision = new(1), PositionX = 1000,
                PositionY = 1000, HasHealth = true, Life = 400, MaxLife = 400 };
            return slot == player.Slot;
        }
    }
}
