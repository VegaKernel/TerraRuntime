using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;
using TerraRuntime.World;
using TerraRuntime.Core.Players;

namespace TerraRuntime.Tests;

public sealed class NpcDeathPreludePipeline1458Tests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Suppressed_statue_death_accepts_even_full_item_pool_and_publishes_credit_once(int ingress)
    {
        var fixture = new Fixture(); var npc = fixture.Spawn(82, true);
        fixture.Pipeline.Interactions.TryMark(npc.Handle, fixture.Player);
        for (int index = 0; index < 400; index++)
            Assert.True(fixture.Items.TryAllocateDrop(new(0, 0, 0, 0, 1, 0, WorldItemOwnershipMode.None, 1, false, 0, 0), out _));
        var before = fixture.Random.Clone();
        Assert.True(fixture.Kill(npc, ingress));
        Assert.False(fixture.Npcs.TryGet(npc.Handle, out _)); Assert.Equal(400, fixture.Items.ActiveCount);
        Assert.True(before.HasSameState(fixture.Random));
        Assert.Equal(1, fixture.Prelude.CaptureBanners().KillCounts.Sum());
        Assert.Single(fixture.Prelude.CaptureBestiary().Kills);
        Assert.False(fixture.Kill(npc, ingress)); Assert.True(before.HasSameState(fixture.Random));
        Assert.Equal(1, fixture.Prelude.CaptureBanners().KillCounts.Sum());
        Assert.False(fixture.Progression.CaptureSnapshot().HasAny);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Ordinary_pressure_rejection_cannot_publish_preview_credit_or_advance_random(int ingress)
    {
        var fixture = new Fixture(); var npc = fixture.Spawn(4, false);
        for (int index = 0; index < 400; index++)
            Assert.True(fixture.Items.TryReserveDropSlot(out _));
        var before = fixture.Random.Clone();
        Assert.False(fixture.Kill(npc, ingress));
        Assert.True(fixture.Npcs.TryGet(npc.Handle, out var retained)); Assert.Equal(npc, retained);
        Assert.True(before.HasSameState(fixture.Random)); Assert.Empty(fixture.Prelude.CaptureBestiary().Kills);
        Assert.Equal(0, fixture.Prelude.CaptureBanners().KillCounts.Sum()); Assert.Equal(1UL, fixture.Prelude.Revision);
        Assert.False(fixture.Progression.CaptureSnapshot().HasAny);
    }

    [Fact]
    public void Prelude_revision_change_during_preview_rejects_before_live_damage_and_preserves_new_owner_state()
    {
        var owner = new RuntimeNpcDeathPrelude1458(); bool advanced = false;
        var fixture = new Fixture(owner, () =>
        {
            if (!advanced)
            {
                advanced = true; var preview = owner.CreatePreview();
                var dead = new NpcSnapshot(new(0, new(1)), new(1), 1, 1, 100, 100, 0, 0, 0, default, NpcSimulationState.Initial);
                var context = new RuntimeNpcDeathPreludeContext1458(true, false, false, false, false, true, false, false, default);
                Assert.True(preview.TryApply(dead, context, new VanillaUnifiedRandom1458(42), out _));
                Assert.True(owner.TryPublish(preview, owner.Revision));
            }
            return default;
        });
        var npc = fixture.Spawn(4, false); var before = fixture.Random.Clone();
        Assert.False(fixture.Kill(npc, 0)); Assert.True(advanced);
        Assert.True(fixture.Npcs.TryGet(npc.Handle, out var retained)); Assert.Equal(npc, retained);
        Assert.True(before.HasSameState(fixture.Random)); Assert.Equal(0, fixture.Items.ActiveCount);
        Assert.Equal(2UL, owner.Revision); Assert.Equal("BlueSlime", Assert.Single(owner.CaptureBestiary().Kills).PersistentId);
        Assert.False(fixture.Progression.CaptureSnapshot().HasAny);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)] [InlineData(null)]
    public void Dungeon_death_uses_owned_shimmer_projection_before_credits_and_loot(bool? shimmer)
    {
        var fixture = new Fixture(goodWorld: true, shimmer: shimmer); var npc = fixture.Spawn(32, false);
        Assert.True(fixture.Pipeline.Interactions.TryMark(npc.Handle, fixture.Player));
        var before = fixture.Random.Clone();
        Assert.Equal(shimmer.HasValue, fixture.Kill(npc, 0));
        Assert.Equal(!shimmer.HasValue, fixture.Npcs.TryGet(npc.Handle, out _));
        Assert.Equal(shimmer == true ? 1 : 0, fixture.Prelude.CaptureBestiary().Kills.Length);
        Assert.Equal(shimmer == true ? 1 : 0, fixture.Prelude.CaptureBanners().KillCounts.Sum());
        if (shimmer != true) { Assert.True(before.HasSameState(fixture.Random)); Assert.Equal(0, fixture.Items.ActiveCount); }
        Assert.False(fixture.Progression.CaptureSnapshot().HasAny);
    }

    private sealed class Fixture : IRuntimePlayerSlotSnapshotLookup
    {
        public RuntimeNpcStore Npcs { get; } = new();
        public RuntimeWorldItemStore Items { get; } = new();
        public VanillaUnifiedRandom1458 Random { get; } = new(1458);
        public RuntimeWorldProgressionMutations Progression { get; } = new();
        public RuntimeNpcDeathPrelude1458 Prelude { get; }
        public RuntimeNpcNetworkCombatPipeline Pipeline { get; }
        public PlayerHandle Player { get; } = new(new(0), new(1));
        public Fixture(RuntimeNpcDeathPrelude1458? owner = null, Func<TerraRuntime.Gameplay.Items.VanillaSeasonalItemDropContext1458>? seasonal = null, bool goodWorld = false, bool? shimmer = null)
        {
            Prelude = owner ?? new();
            var authority = new PlayerAuthority(null, null);
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
            Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1), session.Handle);
            var spawn = new PlayerSpawnCommitRequest(session.Slot, 100, 200, 0, 0, 0, 0, 0);
            Assert.True(authority.TryApply(new PlayerSpawnRuntimeCommand(connection, session, spawn)));
            // Packet28 follows the established explicit unsupported direct-melee/bow compatibility admission.
            var equipment = new PlayerEquipmentCommitRequest(session.Slot, 0, 1, 0, 39, 0);
            Assert.True(authority.TryApply(new PlayerEquipmentRuntimeCommand(connection, equipment)));
            Pipeline = new(Npcs, Items, this, authority, static () => 0, null, new(Items),
                null, new RuntimeWorldClock(0, true, 0, 0, 1, getGoodWorld: goodWorld), Progression, goodWorld, false,
                lootRandom: Random, seasonalItemContext: seasonal, deathPrelude: Prelude, onlyShimmerOceanWorlds: shimmer);
        }
        public NpcSnapshot Spawn(int type, bool statue)
        {
            var state = NpcSimulationState.Initial with { SpawnedFromStatue = statue };
            Assert.True(Npcs.TrySpawnVanilla(new(type, (short)type, 1000, 1000, 0, 0, 0, default, state), out var npc)); return npc;
        }
        public bool Kill(in NpcSnapshot npc, int ingress) => ingress switch
        {
            0 => Pipeline.TryStrikeServerPlayerMelee(Player, npc.Handle, 100000, 0, false, 0, 1) == RuntimeProjectileNpcDamageResult.Killed,
            1 => Pipeline.TryStrikeEnvironment(npc.Handle, 100000) == RuntimeTownNpcMeleeDamageResult1458.Killed,
            _ => Pipeline.TryApply(new(GameCommandSourceId.FromConnection(1), Player), new TerrariaNpcDamageState(npc.Handle.Slot,
                RuntimeNpcPacketProjection.ToProtocolGeneration(npc.Handle.Generation), 10000, 0, 2, 0)) == RuntimeNpcNetworkDamageResult.Killed
        };
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot player)
        {
            player = default(PlayerStateSnapshot) with { Player = Player, Revision = new(1), PositionX = 1000, PositionY = 1000,
                HasHealth = true, Life = 400, MaxLife = 400 };
            return slot == Player.Slot;
        }
    }
}
