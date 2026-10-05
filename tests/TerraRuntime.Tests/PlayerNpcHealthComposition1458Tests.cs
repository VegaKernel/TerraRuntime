using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class PlayerNpcHealthComposition1458Tests
{
    [Theory]
    [InlineData("empty", true)]
    [InlineData("arrow", true)]
    [InlineData("unknown-own", false)]
    [InlineData("unknown-foreign", true)]
    public void Real_world_tick_uses_owned_projectile_context_at_the_remote_regeneration_boundary(string scenario, bool known)
    {
        using var f = new Fixture(knownWorld: true, vampire: false);
        for (int i = 0; i < 299; i++) f.State.Tick();
        Assert.NotNull(f.Capture().NpcHealth);
        if (scenario != "empty")
        {
            ProjectileTypeId type = scenario == "arrow" ? VanillaProjectileIds.WoodenArrowFriendly : new(13);
            byte owner = scenario == "unknown-foreign" ? (byte)1 : f.Session.Slot.Value;
            var update = new ProjectileStateUpdate(type, owner, 100, 100, 0, 0, default, 0, 0, 0, 0);
            Assert.True(f.Projectiles.TrySpawn(0, in update, out _));
        }
        f.State.Tick();
        var snapshot = f.Capture();
        Assert.Equal(known, snapshot.NpcHealth.HasValue);
        Assert.Equal(known, snapshot.NpcLifeCurrent);
        Assert.Equal(133, snapshot.Life);
        Assert.Equal(400, snapshot.DerivedLifeMax);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public void Real_composition_requires_world_provenance_and_does_not_invent_non_vampire_rules(bool knownWorld, bool vampire, bool known)
    {
        using var f = new Fixture(knownWorld, vampire);
        f.State.Tick();
        var snapshot = f.Capture();
        Assert.Equal(known, snapshot.NpcHealth.HasValue);
        Assert.Equal(known, snapshot.NpcLifeCurrent);
        Assert.True(snapshot.HasHealth);
        Assert.Equal(133, snapshot.Life);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool pool = new(2);
        internal readonly RuntimeProjectileStore Projectiles = new();
        internal readonly PlayerJoinSession Session;
        internal readonly ServerRuntimeState State;

        internal Fixture(bool knownWorld, bool vampire)
        {
            var tiles = new WorldTileStore(new WorldDimensions(100, 80));
            RuntimeTownCommerceWorldFacts1458? facts = knownWorld
                ? default(RuntimeTownCommerceWorldFacts1458) with { VampireSeed = vampire }
                : null;
            State = new(worldTiles: tiles, projectiles: Projectiles, townCommerceWorldFacts: facts);
            Assert.True(pool.TryAcquireConnection(out var lease));
            Session = new(lease!);
            Session.ObserveWorldRequest();
            Session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(331017), Session.Handle);
            State.Apply(new PlayerHealthRuntimeCommand(connection, new(Session.Slot, 133, 400)));
            State.Apply(new PlayerSpawnRuntimeCommand(connection, Session, new(Session.Slot, 40, 30, 0, 0, 0, 0, 0)));
            State.Apply(new PlayerMovementRuntimeCommand(connection,
                new(Session.Slot, 0, VanillaPlayerMovementNormalizer.MovementVelocityPresentFlag, 0, 0, 0,
                    800, 438, true, 1, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            Assert.Equal(0, State.RejectedPlayerMovements);
            Assert.Equal(1f, Capture().VelocityX);
        }

        internal PlayerStateSnapshot Capture()
        {
            Assert.True(State.TryCapturePlayerSnapshot(Session.Handle, out var snapshot));
            return snapshot;
        }

        public void Dispose() => Session.Dispose();
    }
}
