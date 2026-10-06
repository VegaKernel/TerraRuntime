using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class PirateSpawnBinding1458Tests
{
    [Fact]
    public void Real_tick_births_each_supported_ordinary_pirate_once_through_pending_publication()
    {
        foreach (int type in new[] { 212, 213, 214, 215, 252 })
        {
            using var fixture = new Fixture(FindSeed(type), large: false);
            fixture.Runtime.Tick();
            Assert.Equal(1, fixture.Store.ActiveCount);
            Assert.True(fixture.Store.TryGetActive(0, out var born));
            Assert.Equal(type, born.Type);
            Assert.Equal(1, fixture.Runtime.AppliedNpcSpawns);
            Assert.Single(fixture.Drain(), frame => frame[2] == 23);
            Assert.True(born.Simulation.TownNpc == false);
        }
    }

    [Fact]
    public void Selected_ship_and_unowned_Captain_refuse_without_substitution_or_rng_adoption()
    {
        foreach (int type in new[] { 491, 216 })
        {
            using var fixture = new Fixture(FindSeed(type), large: false, size: type == 491 ? 59 : 120);
            var before = fixture.Random.SourceRandom.Clone();
            fixture.Runtime.Tick();
            Assert.Equal(0, fixture.Store.ActiveCount);
            Assert.Equal(0, fixture.Runtime.AppliedNpcSpawns);
            Assert.True(fixture.Random.SourceRandom.HasSameState(before));
            Assert.Empty(fixture.Drain());
        }
    }

    [Fact]
    public void Late_context_event_and_expanded_terrain_section_changes_refuse_before_birth_or_rng()
    {
        foreach (bool terrain in new[] { false, true })
        {
            using var fixture = new Fixture(FindSeed(212, large: true), large: true);
            int captures = 0;
            fixture.Store.SetVanillaSpawnContextSource(() =>
            {
                if (++captures == 2)
                {
                    if (terrain)
                    {
                        // Center tile300/238: this section lies outside old +/-87/-63 but inside
                        // source floor +/-84/-52 plus ship rectangle +/-20/-40 ownership.
                        fixture.Tiles.Set(196, 146, fixture.Tiles.Get(196, 146));
                    }
                    else
                    {
                        Assert.True(fixture.Invasion.TryCapture(out var current));
                        Assert.True(fixture.Invasion.TryAdopt(in current,
                            new InvasionTransition1458(current.State with { Size = current.State.Size - 1 }, default), out _));
                    }
                }
                return new(1f, 1, false);
            });
            var before = fixture.Random.SourceRandom.Clone();
            fixture.Runtime.Tick();
            Assert.True(captures >= 2);
            Assert.Equal(0, fixture.Store.ActiveCount);
            Assert.Equal(0, fixture.Runtime.AppliedNpcSpawns);
            Assert.True(fixture.Random.SourceRandom.HasSameState(before));
            Assert.Empty(fixture.Drain());
        }
    }

    [Fact]
    public void Ship_rectangle_matches_source_inclusive_platform_actuation_and_world_margin_semantics()
    {
        // Official Collision.SolidTiles(startX,endX,startY,endY), 1.4.5.8, is inclusive and
        // rejects endY>=maxTilesY-40. Dense storage excludes its null-Tile branch.
        using var fixture = new Fixture(0, large: false);
        var method = typeof(NpcAuthority).GetMethod("IsPirateShipRectangleBlocked", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var composition = (ServerRuntimeComposition)typeof(ServerRuntimeState)
            .GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Runtime)!;
        bool Blocked(int x, int y) => (bool)method.Invoke(composition.Npcs, [x, y])!;
        Assert.False(Blocked(200, 100));
        Assert.True(Blocked(19, 100));
        Assert.True(Blocked(380, 100));
        Assert.True(Blocked(200, 39));
        Assert.True(Blocked(200, 190));
        foreach (var tile in new[]
        {
            new WorldTile { Type = 1, Flags = WorldTileFlags.Active },
            new WorldTile { Type = 19, Flags = WorldTileFlags.Active },
            new WorldTile { Type = 1, Flags = WorldTileFlags.Active | WorldTileFlags.Inactive },
            new WorldTile { Type = 1 }
        })
        {
            fixture.Tiles.Set(220, 90, tile); // Exact inclusive right/bottom endpoint.
            Assert.Equal(tile.IsActive && !tile.IsActuated && tile.Type == 1, Blocked(200, 100));
        }
    }

    private static int FindSeed(int desired, bool large = false)
    {
        // Select a deterministic producer profile; independent source selector/birth goldens live in
        // PirateNaturalSpawn1458Tests. These checks exercise actual host binding and owner rejection.
        int playerX = large ? 300 : 200, playerY = large ? 238 : 58, floor = large ? 260 : 95;
        var invasion = default(InvasionState1458) with { Type = 3, Size = desired == 491 ? 59 : 120, SizeStart = 120 };
        for (int seed = 0; seed < 100000; seed++)
        {
            var random = new SystemVanillaNpcRandom(seed);
            if (random.NextInt32(0, 20) != 0)
                continue;
            bool found = false;
            for (int attempt = 0; attempt < 50; attempt++)
            {
                int x = playerX + random.NextInt32(-84, 85);
                int y = playerY + random.NextInt32(-52, 53);
                if (y < 10 || y >= floor || Math.Abs(x - playerX) < 62)
                    continue;
                found = true;
                break;
            }
            if (found && RuntimeInvasionSpawn1458.SelectPirate(in invasion, false, false, () => false, random).Value == desired)
                return seed;
        }
        throw new InvalidOperationException("No deterministic Pirate producer seed within the bounded search.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(1);
        private readonly PlayerJoinSession session;
        private readonly TerrariaConnectionOutboundQueue queue;
        internal RuntimeNpcStore Store { get; }
        internal WorldTileStore Tiles { get; }
        internal SystemVanillaNpcRandom Random { get; }
        internal RuntimeWorldInvasion1458 Invasion { get; }
        internal ServerRuntimeState Runtime { get; }

        internal Fixture(int seed, bool large, int size = 120)
        {
            var registry = new RuntimeNpcReplicationRegistry();
            queue = new(new OutboundQueueOptions(128, 65536, 1024));
            var source = GameCommandSourceId.FromConnection(9918);
            Assert.True(registry.TryRegister(source, queue));
            Store = new(commitSink: registry);
            Tiles = new(new WorldDimensions(large ? 800 : 400, large ? 450 : 220));
            int floor = large ? 260 : 95, playerX = large ? 300 : 200, playerY = large ? 240 : 60;
            for (int x = 0; x < Tiles.Dimensions.WidthTiles; x++)
                Tiles.Set(x, floor, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            Random = new(seed);
            Invasion = new(default(InvasionState1458) with { Type = 3, Size = size, SizeStart = 120, X = playerX });
            Runtime = new(npcs: Store, npcAiStepper: new Quiet(), npcReplication: registry, worldTiles: Tiles,
                worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with
                { WorldSurface = large ? 300 : 120, RockLayer = large ? 350 : 160, SpawnTileY = floor },
                invasion: Invasion, naturalSpawnRandom: Random, invasionProgressPublisher: _ => { });
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new(lease!);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            var connection = new ConnectionHandle(source, session.Handle);
            Runtime.Apply(new PlayerSpawnRuntimeCommand(connection, session,
                new PlayerSpawnCommitRequest(session.Handle.Slot, (short)playerX, (short)playerY, 0, 0, 0, 0, 0)));
            registry.PlayerSpawned(connection, new PlayerSpawnCommitRequest(session.Handle.Slot, 20, 20, 0, 0, 0, 0, 0));
            Drain();
        }

        internal byte[][] Drain()
        {
            var frames = new List<byte[]>();
            var owned = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
                .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
            while (owned.TryRead(out OutboundFrame frame))
                frames.Add(frame.Bytes.ToArray());
            return frames.ToArray();
        }

        public void Dispose() => session.Dispose();
    }

    private sealed class Quiet : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }
}
