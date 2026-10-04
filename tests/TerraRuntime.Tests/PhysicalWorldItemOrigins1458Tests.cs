using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class PhysicalWorldItemOrigins1458Tests
{
    // Independent original NPC.CatchNPC/WorldGen.KillTile and Projectile.Kill captures.
    // Binary SHA256 4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034.
    // Physical reference bodies16x16; Item catalog dimensions remain independent.
    [Theory]
    [InlineData(0, 1000f, "catch", 2019, 1002f, 1013f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 1000f, "dirt", 2, 160f, 160f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 1000f, "wood", 9, 160f, 160f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 1000f, "stone", 3, 160f, 160f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 1000.75f, "catch", 2019, 1002f, 1013f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 1000.75f, "dirt", 2, 160f, 160f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 1000.75f, "wood", 9, 160f, 160f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 1000.75f, "stone", 3, 160f, 160f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 1001.25f, "catch", 2019, 1003f, 1013f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 1001.25f, "dirt", 2, 160f, 160f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 1001.25f, "wood", 9, 160f, 160f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 1001.25f, "stone", 3, 160f, 160f, 1.4f, -2f, 1649316166)]
    [InlineData(1458, 1000f, "catch", 2019, 1002f, 1013f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 1000f, "dirt", 2, 160f, 160f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 1000f, "wood", 9, 160f, 160f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 1000f, "stone", 3, 160f, 160f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 1000.75f, "catch", 2019, 1002f, 1013f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 1000.75f, "dirt", 2, 160f, 160f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 1000.75f, "wood", 9, 160f, 160f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 1000.75f, "stone", 3, 160f, 160f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 1001.25f, "catch", 2019, 1003f, 1013f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 1001.25f, "dirt", 2, 160f, 160f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 1001.25f, "wood", 9, 160f, 160f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 1001.25f, "stone", 3, 160f, 160f, -0.5f, -2.5f, 1916656655)]
    public void Capture_and_simple_tiles_match_original(int seed, float px, string kind, int id,
        float x, float y, float vx, float vy, int next)
    {
        var random = new SystemWorldItemSpawnRandom(seed);
        var actual = kind == "catch"
            ? VanillaNpcCatchWorldItem1458.Create(px + 10, 1021.25f, new(id), random)
            : VanillaSimpleTileBreakResolver1458.MaterializeItemState(new(id), 1, 10, 10, random);
        Assert.Equal((x, y, vx, vy), (actual.PositionX, actual.PositionY, actual.VelocityX, actual.VelocityY));
        Assert.Equal((short)id, actual.ItemNetId);
        Assert.Equal(next, random.SourceRandom.Next());
    }

    [Theory]
    [InlineData(0, 31, 160f, 169, 157f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 31, 160.75f, 169, 157.75f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 31, 161.25f, 169, 158.25f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 56, 160f, 370, 157f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 56, 160.75f, 370, 157.75f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 56, 161.25f, 370, 158.25f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 67, 160f, 408, 157f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 67, 160.75f, 408, 157.75f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 67, 161.25f, 408, 158.25f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 71, 160f, 424, 157f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 71, 160.75f, 424, 157.75f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 71, 161.25f, 424, 158.25f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 179, 160f, 1103, 157f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 179, 160.75f, 1103, 157.75f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 179, 161.25f, 1103, 158.25f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 241, 160f, 1246, 157f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 241, 160.75f, 1246, 157.75f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 241, 161.25f, 1246, 158.25f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 812, 160f, 4090, 157f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 812, 160.75f, 4090, 157.75f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 812, 161.25f, 4090, 158.25f, 157.25f, 1.4f, -2f, 1649316166)]
    [InlineData(1458, 31, 160f, 169, 157f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 31, 160.75f, 169, 157.75f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 31, 161.25f, 169, 158.25f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 56, 160f, 370, 157f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 56, 160.75f, 370, 157.75f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 56, 161.25f, 370, 158.25f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 67, 160f, 408, 157f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 67, 160.75f, 408, 157.75f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 67, 161.25f, 408, 158.25f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 71, 160f, 424, 157f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 71, 160.75f, 424, 157.75f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 71, 161.25f, 424, 158.25f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 179, 160f, 1103, 157f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 179, 160.75f, 1103, 157.75f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 179, 161.25f, 1103, 158.25f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 241, 160f, 1246, 157f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 241, 160.75f, 1246, 157.75f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 241, 161.25f, 1246, 158.25f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 812, 160f, 4090, 157f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 812, 160.75f, 4090, 157.75f, 157.25f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 812, 161.25f, 4090, 158.25f, 157.25f, -0.5f, -2.5f, 1916656655)]
    public void Occupied_falling_landing_matches_original(int seed, int type, float px, int id,
        float x, float y, float vx, float vy, int next)
    {
        var tiles = new WorldTileStore(new WorldDimensions(256, 256));
        tiles.Set(10, 10, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var random = new SystemWorldItemSpawnRandom(seed);
        var items = new RuntimeWorldItemStore();
        var projectiles = new RuntimeProjectileStore();
        var runtime = new ServerRuntimeState(worldTiles: tiles, worldItems: items,
            projectiles: projectiles, worldItemSpawnRandom: random);
        Assert.True(projectiles.TrySpawnVanilla(new(new ProjectileTypeId(type), 255, px, 160.25f,
            0, 1, default, 0, 10, 0, 10), out var projectile));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var composition = Assert.IsType<ServerRuntimeComposition>(typeof(ServerRuntimeState).GetField("_runtime", flags)!.GetValue(runtime));
        var land = typeof(WorldTileAuthority).GetMethod("TryLandFallingBlock", flags)!;
        Assert.True((bool)land.Invoke(composition.WorldTileAuthority, [projectile])!);
        var buffer = new WorldItemSnapshot[400];
        Assert.Equal(1, items.CopyActive(buffer));
        var actual = buffer[0];
        Assert.Equal((x, y, vx, vy), (actual.PositionX, actual.PositionY, actual.VelocityX, actual.VelocityY));
        Assert.Equal((short)id, actual.ItemNetId);
        Assert.Equal(next, random.SourceRandom.Next());
        Assert.True(tiles.Get(10, 10).IsActive);
        Assert.Equal(1, tiles.Get(10, 10).Type);
    }
    // Original NPC.CatchNPC uses live player.Center, including the independently verified Rudolph20x62 body.
    [Theory]
    [InlineData(0, 1000f, false, 1002f, 1013f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 1000f, true, 1002f, 1023f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 1000.75f, false, 1002f, 1013f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 1000.75f, true, 1002f, 1023f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 1001.25f, false, 1003f, 1013f, 1.4f, -2f, 1649316166)]
    [InlineData(0, 1001.25f, true, 1003f, 1023f, 1.4f, -2f, 1649316166)]
    [InlineData(1458, 1000f, false, 1002f, 1013f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 1000f, true, 1002f, 1023f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 1000.75f, false, 1002f, 1013f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 1000.75f, true, 1002f, 1023f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 1001.25f, false, 1003f, 1013f, -0.5f, -2.5f, 1916656655)]
    [InlineData(1458, 1001.25f, true, 1003f, 1023f, -0.5f, -2.5f, 1916656655)]
    public void Authenticated_catch_uses_live_player_body_and_owner_reservation(int seed, float px, bool mounted,
        float x, float y, float vx, float vy, int next)
    {
        var random = new SystemWorldItemSpawnRandom(seed);
        var publication = new CatchPublication();
        var npcs = new RuntimeNpcStore(commitSink: publication);
        var items = new RuntimeWorldItemStore(publication);
        var state = new ServerRuntimeState(npcs: npcs, worldItems: items, worldItemSpawnRandom: random);
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(733), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Slot, 100, 200, 0, 0, 0, 0, 0)));
        state.Apply(new PlayerMovementRuntimeCommand(connection,
            new PlayerMovementCommitRequest(session.Slot, 0,
                mounted ? VanillaPlayerMovementNormalizer.MovementMountPresentFlag : (byte)0,
                0, 0, 0, px, 1000.25f, false, 0, 0, mounted, 0,
                false, 0, 0, 0, 0, false, 0, 0)));
        Assert.True(npcs.TrySpawnVanilla(new(46, 46, 1000, 1000, 0, 0, 255, default, NpcSimulationState.Initial), out var npc));
        var command = new ClientNpcCatchRuntimeCommand(connection, new TerrariaNpcCatchState(npc.Handle.Slot));
        publication.Events.Clear();
        state.Apply(command);
        Assert.Equal(new byte[] { 21, 22, 23 }, publication.Events.ToArray());
        Assert.False(npcs.TryGet(npc.Handle, out _));
        Assert.Equal(1, items.ActiveCount);
        Assert.True(items.TryGetActive(0, out var item));
        Assert.Equal((x, y, vx, vy), (item.PositionX, item.PositionY, item.VelocityX, item.VelocityY));
        Assert.Equal((short)2019, item.ItemNetId);
        Assert.Equal(connection.Player.Slot.Value, item.OwnerPlayerId);
        Assert.Equal(100, item.TimeToKeepReservation);
        Assert.Equal((byte)0, item.GrabDelayPlayer);
        Assert.Equal(next, random.SourceRandom.Next());
        state.Apply(command);
        Assert.Equal(1, items.ActiveCount);
    }

    [Fact]
    public void Live_authenticated_catch_at_full_source_capacity_publishes_sentinel_before_npc_removal()
    {
        var random = new SystemWorldItemSpawnRandom(1458);
        var publication = new CatchPublication();
        var npcs = new RuntimeNpcStore(commitSink: publication);
        var items = WorldItemSourceAllocation1458Tests.Arrange(10, publication);
        var state = new ServerRuntimeState(npcs: npcs, worldItems: items, worldItemSpawnRandom: random);
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(733), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Slot, 100, 200, 0, 0, 0, 0, 0)));
        state.Apply(new PlayerMovementRuntimeCommand(connection,
            new PlayerMovementCommitRequest(session.Slot, 0, 0, 0, 0, 0, 1000.25f, 1000.75f,
                false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        Assert.True(npcs.TrySpawnVanilla(new(46, 46, 1000, 1000, 0, 0, 255, default, NpcSimulationState.Initial), out var npc));
        publication.Events.Clear();
        state.Apply(new ClientNpcCatchRuntimeCommand(connection, new TerrariaNpcCatchState(npc.Handle.Slot)));
        Assert.Equal(new byte[] { 21, 22, 23 }, publication.Events.ToArray());
        Assert.False(npcs.TryGet(npc.Handle, out _));
        Assert.Equal(400, items.ActiveCount);
        Assert.False(items.TryGetActive(400, out _));
        var sentinel = Assert.Single(publication.Sentinels);
        Assert.Equal((short)2019, sentinel.Drop.ItemNetId);
        Assert.Equal((1002f, 1013f, -.5f, -2.5f),
            (sentinel.Drop.PositionX, sentinel.Drop.PositionY, sentinel.Drop.VelocityX, sentinel.Drop.VelocityY));
        Assert.True(sentinel.Owner.HasValue);
        Assert.Equal((byte)0, sentinel.Owner.Value.OwnerPlayerId);
        Assert.Equal(100, sentinel.Owner.Value.TimeToKeepReservation);
        Assert.Equal((byte)0, sentinel.Owner.Value.GrabDelayPlayer);
        Assert.Equal(1916656655, random.SourceRandom.Next());
    }

    private sealed class CatchPublication : INpcStateCommitSink, IWorldItemStateCommitSink, IWorldItemSentinelCommitSink1458
    {
        public List<byte> Events { get; } = [];
        public List<WorldItemSentinelCommit1458> Sentinels { get; } = [];
        public void WorldItemSentinelCommitted(in WorldItemSentinelCommit1458 commit)
        { Sentinels.Add(commit); Events.Add(21); if (commit.Owner.HasValue) Events.Add(22); }
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc)
        { if (kind == NpcStateCommitKind.Despawn) Events.Add(23); }
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot item)
        { if (kind == WorldItemStateCommitKind.Drop) Events.Add(21); else if (kind == WorldItemStateCommitKind.Owner) Events.Add(22); }
    }

}
