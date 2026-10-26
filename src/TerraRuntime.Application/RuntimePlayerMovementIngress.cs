using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

// Source caller provenance, not a client-controlled packet bit. Unknown keeps
// the existing dedicated/strict component projection. A trusted direct producer
// may explicitly qualify the same local input role as authenticated client13.
internal enum RuntimeItemUseCaller1458 : byte { Unknown, DedicatedRemote, LocalOwnerProjection }

internal sealed record PlayerMovementRuntimeCommand(
    ConnectionHandle Connection,
    PlayerMovementCommitRequest Request) : RuntimeCommand
{
    internal RuntimeItemUseCaller1458 ItemUseCaller { get; init; }
}

internal sealed class RuntimePlayerMovementIngress : IPlayerMovementIngress
{
    private readonly IGameCommandIngress<RuntimeCommand> _ingress;
    private readonly RuntimePlayerMovementAuthority _authority;
    private readonly RuntimeItemUseCaller1458 itemUseCaller;

    public RuntimePlayerMovementIngress(IGameCommandIngress<RuntimeCommand> ingress)
        : this(ingress, new RuntimePlayerMovementAuthority())
    {
    }

    internal RuntimePlayerMovementIngress(
        IGameCommandIngress<RuntimeCommand> ingress,
        RuntimePlayerMovementAuthority authority, RuntimeItemUseCaller1458 itemUseCaller = RuntimeItemUseCaller1458.LocalOwnerProjection)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        ArgumentNullException.ThrowIfNull(authority);
        _ingress = ingress;
        _authority = authority;
        if(itemUseCaller is not (RuntimeItemUseCaller1458.Unknown or RuntimeItemUseCaller1458.DedicatedRemote or RuntimeItemUseCaller1458.LocalOwnerProjection))
            throw new ArgumentOutOfRangeException(nameof(itemUseCaller));
        this.itemUseCaller=itemUseCaller;
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

        var command = new PlayerMovementRuntimeCommand(connection, normalized) {ItemUseCaller=itemUseCaller};
        return _authority.TryValidateAndPost(
            connection,
            in normalized,
            () => _ingress.TryPost(connection.Source, command));
    }
}
