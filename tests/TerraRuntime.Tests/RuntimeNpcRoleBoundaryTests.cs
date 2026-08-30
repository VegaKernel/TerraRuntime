using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcRoleBoundaryTests
{
    [Theory]
    [InlineData(NpcArchetypeRole.Ordinary, false, false, true)]
    [InlineData(NpcArchetypeRole.Town, true, false, false)]
    [InlineData(NpcArchetypeRole.Boss, false, true, false)]
    public void Published_runtime_role_selects_exact_policy_boundary(
        NpcArchetypeRole role,
        bool town,
        bool boss,
        bool ordinary)
    {
        Fixture fixture = new(role);

        Assert.True(fixture.Boundary.TryClassify(
            fixture.Npc.Handle,
            out RuntimeNpcRoleClassification classification));
        Assert.True(classification.IsValid);
        Assert.Equal(fixture.Npc.Handle, classification.Npc);
        Assert.Equal(fixture.ArchetypeId, classification.ArchetypeId);
        Assert.Equal(role, classification.Role);
        Assert.True(classification.RegistryRevision > 0);
        Assert.Equal(town, classification.AllowsTownInteraction);
        Assert.Equal(boss, classification.RequiresBossLifecycle);
        Assert.Equal(ordinary, classification.UsesOrdinaryLifecycle);
    }

    [Fact]
    public void Stale_npc_generation_cannot_reuse_role_binding()
    {
        Fixture fixture = new(NpcArchetypeRole.Boss);
        NpcHandle stale = fixture.Npc.Handle;
        Assert.True(fixture.Npcs.TryDespawn(stale));
        Assert.True(fixture.Npcs.TrySpawn(stale.Slot, Fixture.CreateNpc(), out NpcSnapshot replacement));
        Assert.NotEqual(stale.Generation, replacement.Handle.Generation);

        Assert.False(fixture.Boundary.TryClassify(stale, out _));
        Assert.False(fixture.Boundary.TryClassify(replacement.Handle, out _));
    }

    [Fact]
    public void Undefined_role_is_rejected_before_publication()
    {
        var registry = new RuntimeNpcArchetypeRegistry();
        var descriptor = new NpcArchetypeDescriptor(
            new GameplayArchetypeId("test:invalid-role"),
            VanillaNpcIds.Zombie,
            Role: (NpcArchetypeRole)byte.MaxValue);

        Assert.Equal(
            GameplayArchetypeRegistrationResult.InvalidDescriptor,
            registry.TryRegister(descriptor, out IGameplayArchetypeRegistrationLease? lease));
        Assert.Null(lease);
        Assert.Equal(0, registry.CommitPending().Count);
    }

    private sealed class Fixture
    {
        public Fixture(NpcArchetypeRole role)
        {
            ArchetypeId = new GameplayArchetypeId($"test:{role.ToString().ToLowerInvariant()}");
            var identities = new RuntimeNpcArchetypeIdentityStore(4);
            Npcs = new RuntimeNpcStore(capacity: 4, commitSink: identities);
            var archetypes = new RuntimeNpcArchetypeRegistry();
            var descriptor = new NpcArchetypeDescriptor(
                ArchetypeId,
                VanillaNpcIds.Zombie,
                Role: role);
            Assert.Equal(
                GameplayArchetypeRegistrationResult.Registered,
                archetypes.TryRegister(descriptor, out IGameplayArchetypeRegistrationLease? lease));
            Assert.NotNull(lease);
            archetypes.CommitPending();
            Assert.True(Npcs.TrySpawn(1, CreateNpc(), out NpcSnapshot npc));
            Assert.True(identities.TryBind(npc.Handle, ArchetypeId));
            Npc = npc;
            Boundary = new RuntimeNpcRoleBoundary(Npcs, identities, archetypes);
        }

        public GameplayArchetypeId ArchetypeId { get; }
        public RuntimeNpcStore Npcs { get; }
        public NpcSnapshot Npc { get; }
        public RuntimeNpcRoleBoundary Boundary { get; }

        public static NpcStateUpdate CreateNpc() =>
            new(
                Type: VanillaNpcIds.Zombie.Value,
                NetId: checked((short)VanillaNpcIds.Zombie.Value),
                PositionX: 100f,
                PositionY: 100f,
                VelocityX: 0f,
                VelocityY: 0f,
                Target: VanillaNpcDefinitionCatalog.DefaultTarget,
                Ai: default,
                Simulation: NpcSimulationState.Initial);
    }
}
