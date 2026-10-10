using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

internal static class ArrowNpcKernelSourceSupport1458
{
    private static NpcStateUpdate Actor(int type = 3, int life = 45) => new(type, (short)type, 1800, 1606, 0, 0, 0, default,
        NpcSimulationState.Initial with { Life = life, LifeMax = life, Immortal = false, MoneyValue = 0,
            ExtraMoneyValue = 0, ShimmerTransparency = 0 });
    private static ProjectileStateUpdate Shot(int type) => new(new(type), 0, 1800, 1606, 8, 0, default, 0, 40, 2, 40);
    private static PlayerMovementCommitRequest Movement(PlayerSlotId slot) => new(slot, 0, 0, 0, 0, 0,
        1600, 1606, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0);

    internal sealed class Fixture : IDisposable
    {
        private readonly PlayerJoinSession session;
        private readonly TerrariaConnectionOutboundQueue outbound = new(new OutboundQueueOptions(128, 131072, 1024));
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeProjectileStore Shots;
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly PlayerAuthority Players;
        internal readonly RuntimeNpcBuffStatus1458 Status;
        internal readonly RuntimeProjectileNpcCombatPass Pass;
        internal readonly RuntimeNpcNetworkCombatPipeline Combat;
        internal readonly NpcSnapshot Actor;
        internal readonly ProjectileSnapshot Shot;
        internal readonly ConnectionHandle Connection;
        internal Action? BeforeTick;
        internal Action? OnStatus;

        internal Fixture(int seed, int projectileType, int npcType = 3, int life = 45)
        {
            Random = new(seed);
            var registry = new RuntimeNpcReplicationRegistry();
            var projectileRegistry = new RuntimeProjectileReplicationRegistry();
            Shots = new(commitSink: projectileRegistry);
            Npcs = new(commitSink: registry);
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            Players = new PlayerAuthority(null, tiles);
            var lookup = new RuntimePlayerSnapshotLookup(Players, null);
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new(lease!);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(7272), session.Handle);
            Players.TryApply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 400, 400)));
            Players.TryApply(new PlayerManaRuntimeCommand(Connection, new(session.Slot, 200, 200)));
            Players.TryApply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 51, 27, 0, 0, 0, 0, 0)));
            Players.TryApply(new PlayerMovementRuntimeCommand(Connection, Movement(session.Slot)));
            Players.TryApply(new PlayerEquipmentRuntimeCommand(Connection,
                new(session.Slot, VanillaPlayerItemSlotCatalog.AmmoSlotStart, 5, 0, 97, 0)));
            Players.TickHealthContext();
            Assert.True(Npcs.TrySpawn(0, ArrowNpcKernelSourceSupport1458.Actor(npcType, life), out Actor));
            Status = new(Npcs, handle =>
            {
                registry.PublishNpcBuffs(handle);
                var callback = OnStatus;
                OnStatus = null;
                callback?.Invoke();
            });
            registry.BindNpcBuffStatus(Status);
            Combat = new RuntimeNpcNetworkCombatPipeline(Npcs, Items, lookup, Players,
                () => 100, registry, new(Items), null, new(1000, false, default, 0, 0), new(), false, false,
                lootRandom: Random, seasonalItemContext: () => default);
            Assert.True(Shots.TrySpawn(0, ArrowNpcKernelSourceSupport1458.Shot(projectileType), out Shot));
            Assert.True(Shots.TryMarkCombatTrusted(Shot.Handle, session.Handle));
            Pass = new(Shots, Npcs, Combat, Players,
                () => { var callback = BeforeTick; BeforeTick = null; callback?.Invoke(); return 100; }, status: Status);
            Assert.True(registry.TryRegister(Connection.Source, outbound));
            Assert.True(projectileRegistry.TryRegister(Connection.Source, outbound));
            registry.PlayerSpawned(Connection, new(session.Slot, 51, 27, 0, 0, 0, 0, 0));
            projectileRegistry.PlayerSpawned(Connection, new(session.Slot, 51, 27, 0, 0, 0, 0, 0));
            _ = Drain();
        }

        internal byte[][] Drain()
        {
            var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
                .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
            var frames = new List<byte[]>();
            while (queue.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
            return frames.ToArray();
        }
        public void Dispose() => session.Dispose();
    }
}
