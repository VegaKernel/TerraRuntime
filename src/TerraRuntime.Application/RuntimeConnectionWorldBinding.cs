using System.Runtime.InteropServices;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

/// <summary>
/// One accepted socket's attachment to one WorldRuntime. The socket/outbound queue live above this type; this object
/// owns the runtime-local player-slot lease/session, frame routing chain and replication registrations only.
/// </summary>
internal sealed class RuntimeConnectionWorldBinding : IDisposable
{
    private readonly GameCommandSourceId source;
    private readonly TerrariaConnectionOutboundQueue outbound;
    private int registered;
    private int disposed;
    private readonly Func<string, bool>? playerNameAdmission;

    private RuntimeConnectionWorldBinding(
        WorldRuntime runtime,
        GameCommandSourceId source,
        TerrariaConnectionOutboundQueue outbound,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink root,
        Func<string, bool>? playerNameAdmission)
    {
        Runtime = runtime;
        this.source = source;
        this.outbound = outbound;
        Bootstrap = bootstrap;
        Root = root;
        this.playerNameAdmission = playerNameAdmission;
    }

    public WorldRuntime Runtime { get; }
    public PlayerBootstrapFrameSink Bootstrap { get; }
    public ITerrariaFrameSink Root { get; }
    public PlayerHandle? Player => Bootstrap.AssignedPlayerHandle;
    public string? PlayerName => Bootstrap.PlayerName;
    public bool IsRegistered => Volatile.Read(ref registered) != 0;
    internal Func<string, bool>? PlayerNameAdmission => playerNameAdmission;

    public static bool TryCreateInitial(
        WorldRuntime runtime,
        GameCommandSourceId source,
        TerrariaConnectionOutboundQueue outbound,
        out RuntimeConnectionWorldBinding? binding,
        Func<string, bool>? playerNameAdmission = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(outbound);
        var bootstrap = CreateBootstrap(runtime, source, outbound, playerNameAdmission);
        var created = new RuntimeConnectionWorldBinding(runtime, source, outbound, bootstrap, CreateSinkChain(runtime, source, bootstrap), playerNameAdmission);
        if (!created.TryRegister())
        {
            created.Dispose();
            binding = null;
            return false;
        }
        binding = created;
        return true;
    }

    public static bool TryCreateTransferred(
        WorldRuntime runtime,
        GameCommandSourceId source,
        TerrariaConnectionOutboundQueue outbound,
        PlayerSlotId wireSlot,
        string? playerName,
        out RuntimeConnectionWorldBinding? binding,
        Func<string, bool>? playerNameAdmission = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(outbound);
        binding = null;
        if (!runtime.Slots.TryAcquireConnection(wireSlot, out PlayerSlotPool.PlayerSlotLease? lease) || lease is null)
            return false;

        PlayerJoinSession? session = null;
        PlayerBootstrapFrameSink? bootstrap = null;
        try
        {
            session = new PlayerJoinSession(lease);
            if (session.ObserveWorldRequest() != PlayerJoinTransition.WorldRequestAccepted ||
                session.ObserveSectionRequest() != PlayerJoinTransition.SectionRequestAccepted ||
                session.ObserveSpawn() != PlayerJoinTransition.EnteredPlayingState)
            {
                throw new InvalidOperationException("Could not establish a transferred playing session.");
            }

            bootstrap = CreateBootstrap(runtime, source, outbound, playerNameAdmission);
            bootstrap.AdoptPlayingSession(session, playerName);
            session = null; // ownership moved to bootstrap
            var created = new RuntimeConnectionWorldBinding(runtime, source, outbound, bootstrap, CreateSinkChain(runtime, source, bootstrap), playerNameAdmission);
            bootstrap = null;
            binding = created;
            return true;
        }
        finally
        {
            bootstrap?.Dispose();
            session?.Dispose();
            if (session is null && bootstrap is null && binding is null && !lease.IsReleased)
                lease.Dispose();
        }
    }

    public bool TryRegister()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.CompareExchange(ref registered, 1, 0) != 0)
            return true;

        if (!Runtime.RuntimeConnections.TryRegister(source, outbound))
            return FailRegistration();
        if (!Runtime.NpcReplication.TryRegister(source, outbound))
        {
            Runtime.RuntimeConnections.TryUnregister(source, out _);
            return FailRegistration();
        }
        if (!Runtime.ProjectileReplication.TryRegister(source, outbound))
        {
            Runtime.NpcReplication.TryUnregister(source);
            Runtime.RuntimeConnections.TryUnregister(source, out _);
            return FailRegistration();
        }
        if (!Runtime.WorldItemReplication.TryRegister(source, outbound))
        {
            Runtime.ProjectileReplication.TryUnregister(source);
            Runtime.NpcReplication.TryUnregister(source);
            Runtime.RuntimeConnections.TryUnregister(source, out _);
            return FailRegistration();
        }
        if (!Runtime.VitalsReplication.TryRegister(source, outbound))
        {
            Runtime.WorldItemReplication.TryUnregister(source);
            Runtime.ProjectileReplication.TryUnregister(source);
            Runtime.NpcReplication.TryUnregister(source);
            Runtime.RuntimeConnections.TryUnregister(source, out _);
            return FailRegistration();
        }
        if (!Runtime.ChestReplication.TryRegister(source, outbound))
        {
            Runtime.VitalsReplication.TryUnregister(source);
            Runtime.WorldItemReplication.TryUnregister(source);
            Runtime.ProjectileReplication.TryUnregister(source);
            Runtime.NpcReplication.TryUnregister(source);
            Runtime.RuntimeConnections.TryUnregister(source, out _);
            return FailRegistration();
        }
        if (!Runtime.TileManipulationReplication.TryRegister(source, outbound))
        {
            Runtime.ChestReplication.TryUnregister(source);
            Runtime.VitalsReplication.TryUnregister(source);
            Runtime.WorldItemReplication.TryUnregister(source);
            Runtime.ProjectileReplication.TryUnregister(source);
            Runtime.NpcReplication.TryUnregister(source);
            Runtime.RuntimeConnections.TryUnregister(source, out _);
            return FailRegistration();
        }
        if (!Runtime.SignReplication.TryRegister(source, outbound))
        {
            Runtime.TileManipulationReplication.TryUnregister(source);
            Runtime.ChestReplication.TryUnregister(source);
            Runtime.VitalsReplication.TryUnregister(source);
            Runtime.WorldItemReplication.TryUnregister(source);
            Runtime.ProjectileReplication.TryUnregister(source);
            Runtime.NpcReplication.TryUnregister(source);
            Runtime.RuntimeConnections.TryUnregister(source, out _);
            return FailRegistration();
        }

        Bootstrap.SetRuntimeParticipation(active: true);
        return true;
    }

    public void Unregister()
    {
        if (Interlocked.Exchange(ref registered, 0) == 0)
            return;

        Bootstrap.SetRuntimeParticipation(active: false);
        Runtime.SignReplication.TryUnregister(source);
        Runtime.TileManipulationReplication.TryUnregister(source);
        Runtime.ChestReplication.TryUnregister(source);
        Runtime.VitalsReplication.TryUnregister(source);
        Runtime.WorldItemReplication.TryUnregister(source);
        Runtime.ProjectileReplication.TryUnregister(source);
        Runtime.NpcReplication.TryUnregister(source);
        Runtime.RuntimeConnections.TryUnregister(source, out _);
    }

    public OutboundEnqueueResult TryQueueWorldBootstrap() =>
        TryQueueWorldBootstrap(Runtime.BootstrapPackets.EnterWorldFrame);

    internal OutboundEnqueueResult TryQueueWorldTransferBootstrap(in PlayerSpawnCommitRequest ownerSpawn)
        => TryQueueWorldTransferBootstrap(in ownerSpawn, ReadOnlySpan<ReadOnlyMemory<byte>>.Empty);

    internal OutboundEnqueueResult TryQueueWorldTransferBootstrap(
        in PlayerSpawnCommitRequest ownerSpawn,
        ReadOnlySpan<ReadOnlyMemory<byte>> cleanupFrames)
    {
        OutboundEnqueueResult result = TryQueueWorldBootstrap(
            TerrariaPlayerReplicationFrameEncoder.EncodeSpawn(in ownerSpawn),
            cleanupFrames);
        if (result == OutboundEnqueueResult.Enqueued)
            Bootstrap.BeginWorldTransferLanding(in ownerSpawn);
        return result;
    }

    internal ReadOnlyMemory<byte>[] CaptureWorldTransferCleanupFrames()
    {
        ReadOnlyMemory<byte>[] npcs = Runtime.NpcReplication.CaptureWorldTransferDespawnFrames();
        var frames = new List<ReadOnlyMemory<byte>>(byte.MaxValue + npcs.Length);
        // A replacement packet 7 does not clear Main.player[]. MessageBuffer case 14 is the vanilla
        // remote-player reset boundary. Clear every remote wire slot, including projections whose source
        // actor despawned just after unregister; never deactivate the socket's own player or sentinel 255.
        for (int slot = 0; slot < byte.MaxValue; slot++)
        {
            if (Player is PlayerHandle owner && slot == owner.Slot.Value)
                continue;
            frames.Add(TerrariaPlayerActiveEncoder.Encode(checked((byte)slot), active: false));
        }
        frames.AddRange(npcs);
        return frames.ToArray();
    }

    private OutboundEnqueueResult TryQueueWorldBootstrap(
        ReadOnlyMemory<byte> finalHandoffFrame,
        ReadOnlySpan<ReadOnlyMemory<byte>> cleanupFrames = default)
    {
        if (finalHandoffFrame.IsEmpty)
            throw new ArgumentException("A world-bootstrap handoff frame is required.", nameof(finalHandoffFrame));

        PlayerBootstrapPacketSet packets = Runtime.BootstrapPackets;
        if (!packets.TryResolveLiveBaseSectionFrames(out ReadOnlyMemory<byte>[] liveBaseSectionFrames))
        {
            // Do not fall back to the immutable startup packet-10 frames. A temporary rebuild-capacity miss is
            // preferable to telling the vanilla client that authoritative edits disappeared after respawn/transfer.
            return OutboundEnqueueResult.FrameBudgetExceeded;
        }

        var frames = new List<OutboundFrame>(16 + cleanupFrames.Length);
        for (int i = 0; i < cleanupFrames.Length; i++)
            frames.Add(new OutboundFrame(cleanupFrames[i]));
        frames.Add(new OutboundFrame(Runtime.CreateLiveWorldInfoFrame()));
        frames.Add(new OutboundFrame(packets.StatusFrame));
        for (int i = 0; i < liveBaseSectionFrames.Length; i++)
        {
            frames.Add(new OutboundFrame(liveBaseSectionFrames[i]));
            foreach (ReadOnlyMemory<byte> post in packets.BaseSectionPostFrames[i])
                frames.Add(new OutboundFrame(post));
        }
        foreach (ReadOnlyMemory<byte> post in packets.GlobalPostSectionFrames)
            frames.Add(new OutboundFrame(post));
        // A live world replacement must not expose the player spawn before the destination's persisted global
        // baseline. Keep the packet-12/49 handoff last in the atomic batch.
        frames.Add(new OutboundFrame(finalHandoffFrame));
        return outbound.TryEnqueueBatch(CollectionsMarshal.AsSpan(frames));
    }

    public void MarkPlaying() => Bootstrap.SetRuntimeParticipation(active: true);

    public void SetPlayerName(string? name) => Bootstrap.SetTransferredPlayerName(name);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        Unregister();
        Bootstrap.Dispose();
    }

    private bool FailRegistration()
    {
        Volatile.Write(ref registered, 0);
        Bootstrap.SetRuntimeParticipation(active: false);
        return false;
    }

    private static PlayerBootstrapFrameSink CreateBootstrap(
        WorldRuntime runtime,
        GameCommandSourceId source,
        TerrariaConnectionOutboundQueue outbound,
        Func<string, bool>? playerNameAdmission)
    {
        var bootstrap = new PlayerBootstrapFrameSink(
            runtime.Slots,
            outbound,
            runtime.BootstrapPackets,
            source,
            runtime.SpawnIngress,
            runtime.AppearanceIngress,
            runtime.EquipmentIngress,
            runtime.MovementIngress,
            inner: null,
            worldItems: runtime.WorldItems);
        bootstrap.SetPlayerNameAdmission(playerNameAdmission);
        return bootstrap;
    }

    private static ITerrariaFrameSink CreateSinkChain(
        WorldRuntime runtime,
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap)
    {
        var vitals = new PlayerVitalsFrameSink(source, bootstrap, runtime.HealthIngress, runtime.ManaIngress);
        var buffs = new PlayerBuffFrameSink(source, bootstrap, vitals, runtime.PlayerBuffIngress);
        var combat = new PlayerCombatFrameSink(source, bootstrap, buffs, runtime.PlayerCombatIngress);
        var items = new WorldItemFrameSink(source, bootstrap, combat, runtime.WorldItemIngress);
        var projectiles = new ProjectileLifecycleFrameSink(source, bootstrap, items, runtime.ProjectileIngress);
        var chests = new ChestInteractionFrameSink(source, bootstrap, projectiles, runtime.ChestIngress);
        var signs = new SignInteractionFrameSink(source, bootstrap, chests, runtime.SignIngress);
        var homes = new NpcHomeFrameSink(source, bootstrap, signs, runtime.TownNpcHomeIngress);
        var talk = new NpcTalkFrameSink(source, bootstrap, homes, runtime.NpcTalkIngress);
        var catches = new NpcCatchFrameSink(source, bootstrap, talk, runtime.NpcCatchIngress);
        var teleports = new PlayerTeleportRequestFrameSink(source, bootstrap, catches, runtime.PlayerTeleportIngress);
        return new BossSummonFrameSink(source, bootstrap, teleports, runtime.BossSummonIngress);
    }
}
