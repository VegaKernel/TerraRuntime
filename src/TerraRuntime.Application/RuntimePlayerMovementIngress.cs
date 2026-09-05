using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

internal sealed record PlayerMovementRuntimeCommand(
    ConnectionHandle Connection,
    PlayerMovementCommitRequest Request) : RuntimeCommand;

internal sealed class RuntimePlayerMovementIngress : IPlayerMovementIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> _ingress;
    private readonly RuntimePlayerMovementAuthority _authority;

    public RuntimePlayerMovementIngress(IGameCommandIngress<RuntimeCommand> ingress)
        : this(ingress, new RuntimePlayerMovementAuthority())
    {
    }

    internal RuntimePlayerMovementIngress(
        IGameCommandIngress<RuntimeCommand> ingress,
        RuntimePlayerMovementAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        ArgumentNullException.ThrowIfNull(authority);
        _ingress = ingress;
        _authority = authority;
    }

    internal RuntimePlayerMovementAuthoritySnapshot CaptureAuthoritySnapshot() =>
        _authority.CaptureSnapshot();

    internal bool TryGrantMovementException(
        ConnectionHandle connection,
        RuntimePlayerMovementExceptionKind kind,
        TimeSpan validity,
        float? targetX = null,
        float? targetY = null,
        float targetRadiusPixels = 512f) =>
        _authority.TryGrantException(
            connection,
            kind,
            validity,
            targetX,
            targetY,
            targetRadiusPixels);

    public bool TryPost(ConnectionHandle connection, in PlayerMovementCommitRequest request)
    {
        if (!connection.IsAssigned || connection.Player.Slot != request.PlayerSlot)
            return false;

        if (!VanillaPlayerMovementNormalizer.TryNormalize(in request, out PlayerMovementCommitRequest normalized))
            return false;

        var command = new PlayerMovementRuntimeCommand(connection, normalized);
        return _authority.TryValidateAndPost(
            connection,
            in normalized,
            () => _ingress.TryPost(connection.Source, command));
    }
}
