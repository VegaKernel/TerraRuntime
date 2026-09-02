using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.HostContracts;

namespace TerraRuntime.Tests;

public sealed class ServerPlayerAuthorityTests
{
    [Fact]
    public void Authority_owns_exact_generation_lifecycle_and_clears_generation_scoped_control_state()
    {
        var slots = new PlayerSlotPool(1);
        var identities = new ServerPlayerSlotRegistry(slots);
        var states = new ServerPlayerStateStore(identities, slots.Capacity);
        var authority = new ServerPlayerAuthority(states, identities);
        var id = new ServerPlayerId("test:authority-generation");

        ServerPlayerCreateResult first = authority.Create(id, 10f, 20f);
        Assert.True(first.IsCreated);
        Assert.True(authority.SetHorizontalIntent(id, ServerPlayerHorizontalIntent.Right));
        Assert.Equal(ServerPlayerHorizontalIntent.Right, authority.GetHorizontalIntent(first.Player));

        Span<PlayerStateSnapshot> snapshots = stackalloc PlayerStateSnapshot[1];
        Assert.Equal(1, authority.CopySnapshots(snapshots));
        Assert.Equal(first.Player, snapshots[0].Player);

        Assert.True(authority.Despawn(id));
        Assert.False(authority.TryGet(first.Player, out _));
        Assert.Equal(ServerPlayerHorizontalIntent.Stop, authority.GetHorizontalIntent(first.Player));

        ServerPlayerCreateResult second = authority.Create(id, 30f, 40f);
        Assert.True(second.IsCreated);
        Assert.Equal(first.Player.Slot, second.Player.Slot);
        Assert.True(second.Player.Generation.Value > first.Player.Generation.Value);
        Assert.False(authority.TryGet(first.Player, out _));
        Assert.True(authority.TryGet(second.Player, out PlayerStateSnapshot current));
        Assert.Equal(30f, current.PositionX);
        Assert.Equal(40f, current.PositionY);
    }

    [Fact]
    public void Independent_authorities_do_not_share_state()
    {
        static ServerPlayerAuthority CreateAuthority()
        {
            var slots = new PlayerSlotPool(1);
            var identities = new ServerPlayerSlotRegistry(slots);
            var states = new ServerPlayerStateStore(identities, slots.Capacity);
            return new ServerPlayerAuthority(states, identities);
        }

        ServerPlayerAuthority first = CreateAuthority();
        ServerPlayerAuthority second = CreateAuthority();
        var id = new ServerPlayerId("test:authority-isolation");

        ServerPlayerCreateResult created = first.Create(id, 1f, 2f);
        Assert.True(created.IsCreated);
        Assert.True(first.TryGet(created.Player, out _));
        Assert.False(second.TryGet(created.Player, out _));
        Assert.Equal(0, second.CopySnapshots(Span<PlayerStateSnapshot>.Empty));
    }
}
