using System.Runtime.InteropServices;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

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

    private RuntimeConnectionWorldBinding(
        WorldRuntime runtime,
        GameCommandSourceId source,
        TerrariaConnectionOutboundQueue outbound,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink root)
    {
        Runtime = runtime;
        this.source = source;
        this.outbound = outbound;
        Bootstrap = bootstrap;
        Root = root;
    }

    public WorldRuntime Runtime { get; }
    public PlayerBootstrapFrameSink Bootstrap { get; }
    public ITerrariaFrameSink Root { get; }
    public PlayerHandle? Player => Bootstrap.AssignedPlayerHandle;
    public string? PlayerName => Bootstrap.PlayerName;
    public bool IsRegistered => Volatile.Read(ref registered) != 0;

    public static bool TryCreateInitial(
        WorldRuntime runtime,
        GameCommandSourceId source,
        TerrariaConnectionOutboundQueue outbound,
        out RuntimeConnectionWorldBinding? binding)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(outbound);
        var bootstrap = CreateBootstrap(runtime, source, outbound);
        var created = new RuntimeConnectionWorldBinding(runtime, source, outbound, bootstrap, CreateSinkChain(runtime, source, bootstrap));
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
        out RuntimeConnectionWorldBinding? binding)
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

            bootstrap = CreateBootstrap(runtime, source, outbound);
            bootstrap.AdoptPlayingSession(session, playerName);
            session = null; // ownership moved to bootstrap
            var created = new RuntimeConnectionWorldBinding(runtime, source, outbound, bootstrap, CreateSinkChain(runtime, source, bootstrap));
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

    public OutboundEnqueueResult TryQueueWorldBootstrap()
    {
        var frames = new List<OutboundFrame>(16);
        PlayerBootstrapPacketSet packets = Runtime.BootstrapPackets;
        frames.Add(new OutboundFrame(packets.WorldInfoFrame));
        frames.Add(new OutboundFrame(packets.StatusFrame));
        for (int i = 0; i < packets.BaseSectionFrames.Count; i++)
        {
            frames.Add(new OutboundFrame(packets.BaseSectionFrames[i]));
            foreach (ReadOnlyMemory<byte> post in packets.BaseSectionPostFrames[i])
                frames.Add(new OutboundFrame(post));
        }
        frames.Add(new OutboundFrame(packets.EnterWorldFrame));
        foreach (ReadOnlyMemory<byte> post in packets.GlobalPostSectionFrames)
            frames.Add(new OutboundFrame(post));
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
        TerrariaConnectionOutboundQueue outbound) =>
        new(
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

    private static ITerrariaFrameSink CreateSinkChain(
        WorldRuntime runtime,
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap)
    {
        var vitals = new PlayerVitalsFrameSink(source, bootstrap, runtime.HealthIngress, runtime.ManaIngress);
        var combat = new PlayerCombatFrameSink(source, bootstrap, vitals, runtime.PlayerCombatIngress);
        var items = new WorldItemFrameSink(source, bootstrap, combat, runtime.WorldItemIngress);
        var projectiles = new ProjectileLifecycleFrameSink(source, bootstrap, items, runtime.ProjectileIngress);
        var chests = new ChestInteractionFrameSink(source, bootstrap, projectiles, runtime.ChestIngress);
        var signs = new SignInteractionFrameSink(source, bootstrap, chests, runtime.SignIngress);
        var homes = new NpcHomeFrameSink(source, bootstrap, signs, runtime.TownNpcHomeIngress);
        var talk = new NpcTalkFrameSink(source, bootstrap, homes, runtime.NpcTalkIngress);
        return new NpcCatchFrameSink(source, bootstrap, talk, runtime.NpcCatchIngress);
    }
}
