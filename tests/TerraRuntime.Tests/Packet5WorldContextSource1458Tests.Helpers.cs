using System.Reflection;
using System.IO.Compression;
using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.Core.Npcs;
using System.Text.Json.Nodes;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Network;
using TerraRuntime.World;
using TerraRuntime.Gameplay.Items;

using Xunit;
namespace TerraRuntime.Tests;

public sealed partial class Packet5WorldContextSource1458Tests
{
    static bool SameRandom(VanillaUnifiedRandom1458 random, JsonNode expected)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        uint cursor = (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!;
        var state = (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!;
        return cursor == expected["cursor"]!.GetValue<uint>() && state.SequenceEqual(expected["state"]!.AsArray().Select(n => n!.GetValue<int>()));
    }
    sealed class ReceiveFixture : IRuntimePlayerEventSink, IDisposable
    {
        private readonly List<PlayerJoinSession> sessions = [];
        internal readonly PlayerAuthority Players;
        internal ConnectionHandle Connection;
        internal VanillaUnifiedRandom1458 Random = new(0);
        internal Action? Observer;
        internal int Events;
        internal RuntimePlayerMember Member
        {
            get
            {
                Players.TryGet(Connection, out var member);
                return member;
            }
        }
        internal ReceiveFixture(int slot, bool spawn)
        {
            var slots = new PlayerSlotPool(slot + 1);
            for (int i = 0; i <= slot; i++)
            {
                if (!slots.TryAcquireConnection(out var lease)) throw new Exception("lease");
                sessions.Add(new(lease!));
            }
            var session = sessions[^1];
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(1), session.Handle);
            Players = new(this, new WorldTileStore(new WorldDimensions(400, 300)));
            if (spawn) Players.TryApply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
        }
        internal void Equip(short slot, int type, int prefix, short stack, byte flags) => Players.TryApply(new PlayerEquipmentRuntimeCommand(Connection, new(Connection.Player.Slot, slot, stack, (byte)prefix, (short)type, flags)));
        public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
        {
            Events++;
            Observer?.Invoke();
        }
        public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request) { }
        public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request) { }
        public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request) { }
        public void PlayerDisconnected(ConnectionHandle connection) { }
        public void Dispose()
        {
            foreach (var session in sessions) session.Dispose();
        }
    }

}
// Managed-only queue/private composition access. Native uses a separate typed fixture.
internal sealed class ContextBootstrapSupport : IRuntimePlayerEventSink, IDisposable
{
    internal readonly VanillaUnifiedRandom1458 Random;
    internal readonly ServerRuntimeState State;
    internal readonly RuntimeConnectionRegistry Registry = new();
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly TerrariaConnectionOutboundQueue ownerQueue = new(new(256, 131072, 2048));
    private readonly TerrariaConnectionOutboundQueue peerQueue = new(new(256, 131072, 2048));
    internal PlayerAuthority Players
    {
        get;
    }
    internal ConnectionHandle Connection => new(GameCommandSourceId.FromConnection(99112), bootstrap.AssignedPlayerHandle!.Value);
    internal Action? Observer;
    internal int Events;

    internal ContextBootstrapSupport(int seed, VanillaItemPrefixWorld1458? world)
    {
        Random = new(seed);
        State = new(playerEvents: this, worldTiles: new WorldTileStore(new WorldDimensions(400, 300)),
            npcs: new RuntimeNpcStore(8), naturalSpawnRandom: new SystemVanillaNpcRandom(Random), itemPrefixWorld: world);
        object runtime = typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(State)!;
        Players = (PlayerAuthority)runtime.GetType().GetProperty("Players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(runtime)!;
        // This must already be the composition's exact source owner; rebinding another cursor is forbidden.
        Players.BindReceiveEquipmentRandom(Random, OperatingSystem.IsWindows(), world);
        var source = GameCommandSourceId.FromConnection(99112);
        Assert.True(Registry.TryRegister(source, ownerQueue));
        var ingress = new ApplyingIngress(State);
        bootstrap = new(new PlayerSlotPool(1), ownerQueue,
            PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, 7 }, [], new byte[] { 3, 0, 49 }), source,
            new RuntimePlayerSpawnCommitIngress(ingress), appearanceIngress: null,
            new RuntimePlayerEquipmentIngress(ingress), new RuntimePlayerMovementIngress(ingress));
        Continue(1, [11, (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a', (byte)'3', (byte)'2', (byte)'6']);
        Assert.Equal(0, bootstrap.AssignedPlayerHandle!.Value.Slot.Value);
        // Actual bootstrap accepts packet5 before6, without inventing an active player.
        Report(Convert.FromHexString("0C00051F0000010000DB0000"));
        Report(Convert.FromHexString("0C00051F3600070000610000"));
        Assert.False(Players.TryGet(Connection, out _));
        Continue(6, []);
        Continue(8, new byte[9]);
        byte[] spawn = new byte[TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength];
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(spawn.AsSpan(1), 100);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(spawn.AsSpan(3), 103);
        Continue(12, spawn);
        Assert.Equal(PlayerJoinState.Playing, bootstrap.JoinState);
        var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(99113), new(new(1), new(1)));
        Assert.True(Registry.TryRegister(peer.Source, peerQueue));
        var peerSpawn = new PlayerSpawnCommitRequest(peer.Player.Slot, 100, 103, 0, 0, 0, 0, 0);
        Registry.PlayerSpawned(peer, in peerSpawn);
        Drain(ownerQueue);
        Drain(peerQueue);
        Events = 0;
    }

    internal void Report(byte[] frame)
    {
        Assert.Equal(12, frame.Length);
        Assert.Equal(5, frame[2]);
        Continue(5, frame[3..]);
    }

    internal IReadOnlyList<string> DrainPeer() => Drain(peerQueue);
    internal IReadOnlyList<string> DrainOwner() => Drain(ownerQueue);
    private static IReadOnlyList<string> Drain(TerrariaConnectionOutboundQueue outbound)
    {
        var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
        var frames = new List<string>();
        while (queue.TryRead(out var frame)) frames.Add(Convert.ToHexString(frame.Bytes.Span));
        return frames;
    }

    private void Continue(byte id, byte[] payload)
    {
        var frame = new TerrariaFrame(checked((ushort)(3 + payload.Length)), id, ReadOnlySequence<byte>.Empty, new(payload));
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(in frame));
    }

    public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
    {
        Registry.PlayerEquipmentUpdated(connection, in request);
        Events++;
        Observer?.Invoke();
    }
    public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request) => Registry.PlayerSpawned(connection, in request);
    public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request) => Registry.PlayerAppearanceUpdated(connection, in request);
    public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request) => Registry.PlayerMoved(connection, in request);
    public void PlayerDisconnected(ConnectionHandle connection) => Registry.PlayerDisconnected(connection);
    public void Dispose() => bootstrap.Dispose();

    private sealed class ApplyingIngress(ServerRuntimeState state) : IGameCommandIngress<RuntimeCommand>
    {
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command)
        {
            state.Apply(command);
            return true;
        }
    }
}
