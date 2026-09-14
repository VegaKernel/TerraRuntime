using System.Buffers.Binary;
using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerPlayerMoonLeechTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Real_ticks_preserve_both_buff_orders_and_expire_independently(bool leechFirst)
    {
        using var f = new Fixture();
        if (leechFirst) Assert.True(f.Authority.TryApplyMoonLeech(f.Player, 840));
        f.Tick();
        if (!leechFirst) Assert.True(f.Authority.TryApplyMoonLeech(f.Player, 840));
        f.AssertLastBuff(leechFirst ? [145, 24] : [24, 145]);
        Assert.True(f.Authority.TryTeleport(f.Id, 500, 500));
        f.Tick(420);
        f.AssertLastBuff(145);
        int remaining = f.Authority.GetMoonLeechDuration(f.Player);
        f.Tick(remaining - 1);
        Assert.Equal(1, f.Authority.GetMoonLeechDuration(f.Player));
        f.Tick();
        Assert.Equal(0, f.Authority.GetMoonLeechDuration(f.Player));
        f.AssertLastBuff();
    }

    [Fact]
    public void Refresh_death_and_slot_reuse_keep_duration_generation_scoped()
    {
        using var f = new Fixture();
        var application = new ProjectilePlayerBuffApplication(f.Player, VanillaBuffIds.MoonLeech, 960);
        var players = new PlayerAuthority(null, null, serverPlayers: f.Authority);
        Assert.True(players.TryApplyProjectileBuff(in application));
        Assert.True(f.Authority.TryApplyMoonLeech(f.Player, 840));
        Assert.Equal(960, f.Authority.GetMoonLeechDuration(f.Player));
        Assert.True(f.Authority.SetVitals(f.Id, new ServerPlayerVitalsState(0, 500, 200, 200)));
        Assert.Equal(0, f.Authority.GetMoonLeechDuration(f.Player));
        f.AssertLastBuff();
        Assert.True(f.Authority.Despawn(f.Id));
        var replacement = f.Authority.Create(f.Id, 500, 500);
        Assert.True(replacement.IsCreated);
        Assert.Equal(f.Player.Slot, replacement.Player.Slot);
        Assert.False(players.TryApplyProjectileBuff(in application));
        Assert.Equal(0, f.Authority.GetMoonLeechDuration(replacement.Player));
    }

    private sealed class Fixture : IDisposable
    {
        public ServerPlayerId Id { get; } = new("test:moon-leech");
        public ServerPlayerAuthority Authority { get; }
        public PlayerHandle Player { get; }
        private readonly ServerRuntimeState runtime;
        private readonly PlayerSlotPool.PlayerSlotLease occupied;
        private readonly BoundedOutboundQueue queue;

        public Fixture()
        {
            var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(2048, 2_000_000, 4096));
            queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
                .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
            var registry = new RuntimeConnectionRegistry();
            var source = GameCommandSourceId.FromConnection(8501);
            var real = new ConnectionHandle(source, new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
            Assert.True(registry.TryRegister(source, outbound));
            var spawn = new PlayerSpawnCommitRequest(real.Player.Slot, 100, 100, 0, 0, 0, 0, 0);
            registry.PlayerSpawned(real, in spawn);
            var slots = new PlayerSlotPool(2);
            Assert.True(slots.TryAcquireConnection(out var acquired)); occupied = acquired!;
            var identities = new ServerPlayerSlotRegistry(slots);
            Authority = new ServerPlayerAuthority(new ServerPlayerStateStore(identities, slots.Capacity), identities, events: registry);
            var created = Authority.Create(Id, 160, 160); Assert.True(created.IsCreated); Player = created.Player;
            Assert.True(Authority.SetVitals(Id, new ServerPlayerVitalsState(500, 500, 200, 200)));
            var tiles = new WorldTileStore(new WorldDimensions(80, 200));
            for (int x = 8; x <= 15; x++)
                for (int y = 8; y <= 16; y++)
                    tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = new WorldTile { LiquidAmount = 255, LiquidKind = WorldLiquidKind.Lava };
            runtime = new ServerRuntimeState(serverPlayers: Authority, worldTiles: tiles,
                townCommerceWorldFacts: new RuntimeTownCommerceWorldFacts1458());
            while (queue.TryRead(out _)) { }
        }

        public void Tick(int count = 1) { for (int i = 0; i < count; i++) runtime.Tick(); }
        public void AssertLastBuff(params ushort[] expected)
        {
            ushort[]? last = null;
            while (queue.TryRead(out var frame))
            {
                ReadOnlySpan<byte> bytes = frame.Bytes.Span;
                Assert.NotEqual(55, bytes[2]);
                if (bytes[2] != 50) continue;
                Assert.Equal(Player.Slot.Value, bytes[3]);
                Assert.Equal(bytes.Length, BinaryPrimitives.ReadUInt16LittleEndian(bytes));
                Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes[^2..]));
                var types = new List<ushort>();
                for (int offset = 4; offset < bytes.Length - 2; offset += 2)
                    types.Add(BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]));
                last = types.ToArray();
            }
            Assert.NotNull(last);
            Assert.Equal(expected, last);
        }
        public void Dispose() { Authority.Despawn(Id); occupied.Dispose(); }
    }
}
