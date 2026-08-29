using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Operations;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

/// <summary>
/// Per-server transport relay for protocol chat. The relay is scoped by the server's PlayerSlotPool,
/// so separate worlds/process hosts cannot accidentally share recipients. Chat does not mutate
/// authoritative world state; plugin interception can later be layered above this transport primitive.
/// </summary>
internal sealed class RuntimeChatRelay
{
    private static readonly ConditionalWeakTable<PlayerSlotPool, RuntimeChatRelay> Relays = new();

    private readonly ConcurrentDictionary<GameCommandSourceId, Endpoint> endpoints = new();
    private readonly RuntimeChatFanoutBudget fanoutBudget;

    public RuntimeChatRelay(RuntimeChatFanoutBudget? fanoutBudget = null)
    {
        this.fanoutBudget = fanoutBudget ?? new RuntimeChatFanoutBudget();
    }

    public static RuntimeChatRelay For(PlayerSlotPool slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        return Relays.GetValue(slots, static _ => new RuntimeChatRelay());
    }

    public void Register(GameCommandSourceId source, TerrariaConnectionOutboundQueue outbound)
    {
        if (source.IsSystem)
            return;

        ArgumentNullException.ThrowIfNull(outbound);
        endpoints[source] = new Endpoint(outbound);
    }

    public void MarkPlaying(GameCommandSourceId source, PlayerHandle player)
    {
        if (endpoints.TryGetValue(source, out Endpoint? endpoint))
            endpoint.MarkPlaying(player);
    }

    public void Unregister(GameCommandSourceId source)
    {
        if (!source.IsSystem)
            endpoints.TryRemove(source, out _);
    }

    public int Broadcast(
        GameCommandSourceId source,
        PlayerHandle author,
        ReadOnlyMemory<byte> encodedFrame)
    {
        if (!endpoints.TryGetValue(source, out Endpoint? origin) || !origin.IsPlaying(author))
            return 0;

        if (!fanoutBudget.TryConsume())
        {
            TerrariaFrameRejectionTelemetry.Record(TerrariaFrameRejectionCategory.RateLimited);
            return 0;
        }

        if (TerrariaChatCodec.TryDecodeServerFrame(encodedFrame, out TerrariaServerChatMessage message))
            RuntimeChatTelemetry.Publish(author.Slot.Value, message.Text);

        var frame = new OutboundFrame(encodedFrame);
        int enqueued = 0;
        foreach (Endpoint endpoint in endpoints.Values)
        {
            if (!endpoint.IsPlaying())
                continue;

            if (endpoint.Outbound.TryEnqueue(frame) == OutboundEnqueueResult.Enqueued)
                enqueued++;
        }

        return enqueued;
    }

    internal RuntimeChatFanoutBudgetSnapshot CaptureBudgetSnapshot() => fanoutBudget.CaptureSnapshot();

    private sealed class Endpoint(TerrariaConnectionOutboundQueue outbound)
    {
        private int playingSlot = -1;
        private ulong playingGeneration;

        public TerrariaConnectionOutboundQueue Outbound { get; } = outbound;

        public void MarkPlaying(PlayerHandle value)
        {
            Volatile.Write(ref playingGeneration, value.Generation.Value);
            Volatile.Write(ref playingSlot, value.Slot.Value);
        }

        public bool IsPlaying() =>
            Volatile.Read(ref playingSlot) >= 0 && Volatile.Read(ref playingGeneration) != 0;

        public bool IsPlaying(PlayerHandle expected) =>
            Volatile.Read(ref playingSlot) == expected.Slot.Value &&
            Volatile.Read(ref playingGeneration) == expected.Generation.Value;
    }
}
