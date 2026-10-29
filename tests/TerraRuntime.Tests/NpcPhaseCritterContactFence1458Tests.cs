using System.Reflection;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class NpcPhaseCritterContactFence1458Tests
{
    [Theory]
    [InlineData(230)]
    [InlineData(593)]
    [InlineData(687)]
    public void Live_owned_eclipse_supersedes_false_startup_metadata_without_stalling_the_actor(int type)
    {
        var clock = new RuntimeWorldClock(0, true, (VanillaMoonPhase)0, 0, 0);
        var metadata = new RuntimeTownCommerceWorldFacts1458();
        Assert.False(metadata.Eclipse);
        using var fixture = new Fixture(type, clock, metadata);
        var before = fixture.Current();
        var random = fixture.Random.Clone();
        Assert.True(clock.TryStartSolarEclipse());
        NonresidentBunnyNpcPhase1458.Tick(fixture.World);
        var after = fixture.Current();
        Assert.True(after.Revision.Value >= before.Revision.Value + 2,
            $"NPC {type} stalled against stale startup eclipse metadata");
        Assert.Equal(before.Handle, after.Handle);
        Assert.Equal(before.Ai.Ai1 - 1f, after.Ai.Ai1);
        // The existing dry walking branch has no offer on this flat, threat-free preimage.
        Assert.True(fixture.Random.HasSameState(random));
    }

    [Theory]
    [InlineData(230)]
    [InlineData(593)]
    [InlineData(687)]
    [InlineData(55)]
    [InlineData(57)]
    [InlineData(465)]
    [InlineData(592)]
    public void Unqualified_peer_contact_refuses_the_captured_phase_without_adopting_npc_or_random(int type)
    {
        using var fixture = new Fixture(type);
        var npcs = fixture.Npcs;
        var before = fixture.Current();
        var hostile = new NpcStateUpdate(3, 3, before.PositionX, before.PositionY, 0f, 0f, 255,
            default, NpcSimulationState.Initial with { Friendly = false, DamageOverride = 1 });
        Assert.True(npcs.TrySpawn(1, in hostile, out _));
        var checkpoint = fixture.Random.Clone();
        Assert.False(fixture.Targeting.TryStepState(in before, out _));
        Assert.True(npcs.TryGet(before.Handle, out var after));
        Assert.Equal(before, after);
        Assert.True(fixture.Random.HasSameState(checkpoint));
    }

    [Theory]
    [InlineData(198, false)]
    [InlineData(230, true)]
    [InlineData(593, true)]
    public void Refused_shimmer_history_preserves_status_slots_flags_and_the_shared_stream(int type, bool wet)
    {
        using var fixture = new Fixture(type);
        var current = fixture.Current();
        Assert.True(fixture.Status.TryApply(current.Handle, 90));
        Assert.True(fixture.Status.TryGetStinky(current.Handle, out bool beforeStinky));
        Assert.False(beforeStinky);
        var unowned = new NpcStateUpdate(current.Type, current.NetId, current.PositionX, current.PositionY,
            current.VelocityX, current.VelocityY, current.Target, current.Ai, current.Simulation with
            { ShimmerTransparency = .375f, Wet = wet, LiquidContact = wet ? NpcLiquidContactKind.Water : NpcLiquidContactKind.None });
        Assert.True(fixture.Npcs.TryUpdate(current.Handle, in unowned, out var before));
        Assert.True(fixture.Status.TryGetWireDuration(before.Handle, out int beforeDuration));
        Assert.Equal(90, beforeDuration);
        Assert.True(fixture.Status.TryGetVisualOffer(before.Handle, out var beforeOffer));
        var randomBefore = fixture.Random.Clone();
        NonresidentBunnyNpcPhase1458.Tick(fixture.World);
        Assert.Equal(before, fixture.Current());
        Assert.True(fixture.Random.HasSameState(randomBefore));
        Assert.True(fixture.Status.TryGetStinky(before.Handle, out bool afterStinky));
        Assert.Equal(beforeStinky, afterStinky);
        Assert.True(fixture.Status.TryGetWireDuration(before.Handle, out int afterDuration));
        Assert.Equal(beforeDuration, afterDuration);
        Assert.True(fixture.Status.TryGetVisualOffer(before.Handle, out var afterOffer));
        Assert.Equal(beforeOffer, afterOffer);
    }

    private sealed class Fixture : IDisposable
    {
        internal RuntimeNpcStore Npcs { get; } = new();
        internal VanillaUnifiedRandom1458 Random { get; } = new(1458);
        internal ServerRuntimeState World { get; }
        internal VanillaNpcTargetingAiStepper Targeting { get; }
        internal RuntimeNpcBuffStatus1458 Status { get; }
        private readonly PlayerJoinSession session;
        private readonly NpcHandle handle;

        internal Fixture(int type, RuntimeWorldClock? clock = null, RuntimeTownCommerceWorldFacts1458? metadata = null)
        {
        var tiles = new WorldTileStore(new(192, 200));
        Assert.True(tiles.TryAttachWorldSurface(80d));
        for (int x = 0; x < 192; x++) tiles.Set(x, 100, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var npcs = Npcs;
        var random = Random;
        var world = World = new ServerRuntimeState(npcs: npcs, projectiles: new RuntimeProjectileStore(), worldTiles: tiles,
            worldClock: clock ?? new RuntimeWorldClock(0, false, (VanillaMoonPhase)0, 0, 0),
            townCommerceWorldFacts: metadata,
            naturalSpawnRandom: new SystemVanillaNpcRandom(random));
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9257), session.Handle);
        world.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new(session.Slot, 81, 100, 0, 0, 0, 0, 0)));
        world.Apply(new PlayerMovementRuntimeCommand(connection,
            new(session.Slot, 0, 0, 0, 0, 0, 1300f, 1558f, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        world.Apply(new PlayerStealthRuntimeCommand(connection, 1f));
        world.Apply(new PlayerZonesRuntimeCommand(connection, default));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), out var definition));
        var creation = new NpcStateUpdate(type, (short)type, 1023.25f, 1600f - definition.Height,
            .9f, 0f, 255, new(1f, 60f, 0f, 0f), NpcSimulationState.Initial with
            { DirectionX = 1, DirectionY = 1, SpriteDirection = 1,
                OldPositionX = 1022.25f, OldPositionY = 1600f - definition.Height, OldVelocityX = .9f });
        Assert.True(npcs.TrySpawnVanilla(in creation, out var birth));
        NonresidentBunnyNpcPhase1458.Tick(world);
        Assert.True(npcs.TryGet(birth.Handle, out var before));
        Assert.True(before.Revision.Value >= birth.Revision.Value + 2,
            $"NPC {type} did not complete its admitted neutral phase");
        handle = birth.Handle;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var graph = Assert.IsType<ServerRuntimeComposition>(typeof(ServerRuntimeState).GetField("_runtime", flags)!.GetValue(world));
        Targeting = Assert.IsType<VanillaNpcTargetingAiStepper>(typeof(NpcAuthority)
            .GetField("vanillaTargeting", flags)!.GetValue(graph.Npcs));
        Status = graph.Npcs.NpcBuffStatus;
        }

        internal NpcSnapshot Current()
        {
            Assert.True(Npcs.TryGet(handle, out var snapshot));
            return snapshot;
        }
        public void Dispose() => session.Dispose();
    }
}
