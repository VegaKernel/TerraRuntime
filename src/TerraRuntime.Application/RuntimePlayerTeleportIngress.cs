using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal enum RuntimePlayerTeleportRequestKind : byte
{
    TeleportationPotion = 0,
    MagicConch = 1,
    DemonConch = 2,
    ShellphoneSpawn = 3,
    PlayerNoSpaceTeleport = 4
}

internal sealed record PlayerTeleportRuntimeCommand(
    ConnectionHandle Connection,
    RuntimePlayerTeleportRequestKind Kind) : RuntimeCommand;

internal sealed class RuntimePlayerTeleportIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> ingress;

    public RuntimePlayerTeleportIngress(IGameCommandIngress<RuntimeCommand> ingress) =>
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));

    public bool TryPost(ConnectionHandle connection, RuntimePlayerTeleportRequestKind kind)
    {
        if (!connection.IsAssigned || kind is < RuntimePlayerTeleportRequestKind.MagicConch or > RuntimePlayerTeleportRequestKind.DemonConch)
            return false;
        return ingress.TryPost(connection.Source, new PlayerTeleportRuntimeCommand(connection, kind));
    }
}

internal sealed class PlayerTeleportRequestFrameSink : ITerrariaFrameSink
{
    private readonly GameCommandSourceId source;
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly ITerrariaFrameSink inner;
    private readonly RuntimePlayerTeleportIngress ingress;

    public PlayerTeleportRequestFrameSink(
        GameCommandSourceId source,
        PlayerBootstrapFrameSink bootstrap,
        ITerrariaFrameSink inner,
        RuntimePlayerTeleportIngress ingress)
    {
        this.source = source;
        this.bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
    }

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if ((TerrariaMessageId)frame.MessageId != TerrariaMessageId.TeleportRequest)
            return inner.OnFrame(in frame);

        // Packet 73 carries only the request subtype. Coordinates never come from the client.
        if (bootstrap.JoinState != TerraRuntime.Core.Players.PlayerJoinState.Playing ||
            bootstrap.AssignedPlayerHandle is not PlayerHandle player ||
            frame.Payload.Length != 1)
            return TerrariaFrameSinkResult.Continue;

        RuntimePlayerTeleportRequestKind kind = (RuntimePlayerTeleportRequestKind)frame.Payload.FirstSpan[0];
        if (kind is not (RuntimePlayerTeleportRequestKind.MagicConch or RuntimePlayerTeleportRequestKind.DemonConch))
            return inner.OnFrame(in frame);

        // Teleport requests are one-shot convenience actions. Dropping one during a saturated command queue
        // is safer than disconnecting the client or trusting a client-side teleport.
        _ = ingress.TryPost(new ConnectionHandle(source, player), kind);
        return TerrariaFrameSinkResult.Continue;
    }
}
