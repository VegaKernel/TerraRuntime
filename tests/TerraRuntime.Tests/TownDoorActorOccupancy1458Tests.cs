using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class TownDoorActorOccupancy1458Tests
{
    // Official Collision.EmptyTile(ignoreTiles:true): integer rectangles, living nonghost players0..254.
    [Theory]
    [InlineData(656f, 0, false, false, false)]
    [InlineData(656f, 0, true, false, true)]
    [InlineData(656f, 0, false, true, true)]
    [InlineData(656f, 255, false, false, true)]
    [InlineData(672f, 0, false, false, true)]
    [InlineData(671.9f, 0, false, false, false)]
    [InlineData(636.9f, 0, false, false, true)]
    [InlineData(637f, 0, false, false, false)]
    public void Player_occupancy_matches_original_dead_ghost_slot_and_subpixel_edges(float x, byte slot,
        bool dead, bool ghost, bool free)
    {
        var players = new PlayerAuthority(null, null);
        RuntimePlayerMember member = Member(players, slot, x, 432f);
        member.IsDead = dead; member.MovementFlags = ghost ? (byte)64 : (byte)0;
        var probe = new RuntimeTallGateOccupancyProbe(players, null, new RuntimeNpcStore());
        Assert.Equal(free, probe.IsActorFree(41, 27));
    }

    [Theory]
    [InlineData(false, true)] [InlineData(true, false)]
    public void Player_occupancy_uses_active_mountzero_height(bool mounted, bool free)
    {
        var players = new PlayerAuthority(null, null);
        RuntimePlayerMember member = Member(players, 0, 656f, 380f);
        member.HasMount = mounted; member.MountType = 0;
        var probe = new RuntimeTallGateOccupancyProbe(players, null, new RuntimeNpcStore());
        Assert.Equal(free, probe.IsActorFree(41, 27)); // row432: baseends422, Rudolphends442.
    }

    [Theory]
    [InlineData(638.9f, false, true)] [InlineData(639f, false, false)]
    [InlineData(639f, true, false)]
    public void Npc_occupancy_truncates_and_includes_active_zero_life(float x, bool dead, bool free)
    {
        var npcs = new RuntimeNpcStore();
        var update = new NpcStateUpdate(17, 17, x, 440f, 0f, 0f, 255, default,
            NpcSimulationState.Initial with { Life = dead ? 0 : 250, LifeMax = 250 });
        Assert.True(npcs.TrySpawn(0, in update, out _));
        var probe = new RuntimeTallGateOccupancyProbe(new PlayerAuthority(null, null), null, npcs);
        Assert.Equal(free, probe.IsActorFree(41, 27));
    }

    [Fact]
    public void Explicit_custom_npc_body_precedes_unavailable_definition_and_unowned_body_fails_closed()
    {
        var npcs = new RuntimeNpcStore();
        var update = new NpcStateUpdate(5000, 5000, 600f, 440f, 0f, 0f, 255, default,
            NpcSimulationState.Initial with { HitboxOverride = new NpcHitboxDimensions(80, 40) });
        Assert.True(npcs.TrySpawn(0, in update, out var npc));
        var probe = new RuntimeTallGateOccupancyProbe(new PlayerAuthority(null, null), null, npcs);
        Assert.False(probe.IsActorFree(41, 27));
        Assert.True(probe.IsActorFree(60, 27));
        update = update with { Type = 5001, NetId = 5001, Simulation = update.Simulation with { HitboxOverride = null } };
        Assert.True(npcs.TryUpdate(npc.Handle, in update, out _));
        Assert.False(probe.IsActorFree(60, 27)); // Missing physical metadata cannot prove actor-free.
    }

    [Fact]
    public void Actual_town_authority_uses_owned_sitting_player_projection_to_reject_offer()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 80));
        for (int x = 0; x < 100; x++) tiles.Set(x, 30, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        tiles.Set(40, 29, new WorldTile { Type = 15, FrameY = 20, Flags = WorldTileFlags.Active });
        var town = new RuntimeTownNpcStateStore(new WorldNpcPersistence([], [new WorldTownNpc(17, "Merchant", 639f, 440f, false, 40, 30, null, false)], []),
            [new WorldTownRoom(17, 40, 30)], tiles.Dimensions);
        var npcs = new RuntimeNpcStore(); Assert.True(town.TryReserveRuntimeSlots(npcs));
        Assert.True(npcs.TryGetActive(0, out var npc));
        var update = new NpcStateUpdate(17, 17, 639f, 440f, .5f, 0f, 255, new(1f, 20f, 0f, 0f), npc.Simulation);
        Assert.True(npcs.TryUpdate(npc.Handle, in update, out _));
        var players = new PlayerAuthority(null, tiles); RuntimePlayerMember player = Member(players, 0, 638f, 443f);
        player.MiscFlags1 = 4; player.MovementFlags = 64; // Sitting ghost blocks chair; ghost does not block doors.
        var rng = new Random();
        var authority = new TownNpcAuthority(players, npcs, new RuntimeProjectileStore(), tiles,
            new RuntimeWorldProgressionMutations(), town, null, null, null, null, false, false, null, false, false, rng);
        authority.TickLifecycle(null);
        Assert.True(npcs.TryGetActive(0, out npc)); Assert.Equal(1f, npc.Ai.Ai0);
        Assert.Equal(1409701741, rng.Stream.Next());
    }

    private static RuntimePlayerMember Member(PlayerAuthority players, byte slot, float x, float y)
    {
        var member = new RuntimePlayerMember { Slot = new PlayerSlotId(slot),
            Connection = new ConnectionHandle(GameCommandSourceId.FromConnection(3200 + slot),
                new PlayerHandle(new PlayerSlotId(slot), new PlayerSessionGeneration(1))), PositionX = x, PositionY = y };
        var membership = (RuntimePlayerMembership)typeof(PlayerAuthority).GetField("membership", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(players)!;
        membership.Commit(member); return member;
    }
    private sealed class Random : IVanillaNpcRandom
    { public VanillaUnifiedRandom1458 Stream { get; } = new(146); public int NextInt32(int min, int max) => Stream.Next(min, max); }
}
