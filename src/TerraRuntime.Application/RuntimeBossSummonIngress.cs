using System.Buffers;
using TerraRuntime.Network;
using System.Buffers.Binary;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed record ClientBossSummonRuntimeCommand(
    ConnectionHandle Connection,
    short NpcType) : RuntimeCommand;

internal sealed class RuntimeBossSummonIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;
    public RuntimeBossSummonIngress(IGameCommandIngress<RuntimeCommand> ingress) =>
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));

    public bool TryPost(ConnectionHandle connection, short npcType) =>
        connection.IsAssigned && ingress.TryPost(connection.Source, new ClientBossSummonRuntimeCommand(connection, npcType));
}

internal sealed class BossSummonFrameSink : ITerrariaFrameSink
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly RuntimeBossSummonIngress ingress;

    public BossSummonFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        RuntimeBossSummonIngress ingress)
    {
        this.source = source;
        this.bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
    }

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if ((TerrariaMessageId)frame.MessageId != TerrariaMessageId.SpawnBoss)
            return inner.OnFrame(in frame);
        if (bootstrap.JoinState != TerraRuntime.Core.Players.PlayerJoinState.Playing ||
            bootstrap.AssignedPlayerHandle is not PlayerHandle player ||
            frame.Payload.Length != 4)
            return TerrariaFrameSinkResult.Continue;

        Span<byte> payload = stackalloc byte[4];
        frame.Payload.CopyTo(payload);
        // Vanilla overwrites the claimed packet player with the connection owner for player-directed actions.
        short npcType = BinaryPrimitives.ReadInt16LittleEndian(payload[2..]);
        _ = ingress.TryPost(new ConnectionHandle(source, player), npcType);
        return TerrariaFrameSinkResult.Continue;
    }
}
