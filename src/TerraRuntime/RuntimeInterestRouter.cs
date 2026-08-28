using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Runtime-owned routing boundary between authoritative entity state and outbound fan-out.
/// External hosts can only toggle the control surface; the active policy and spatial indexes remain
/// TerraRuntime implementation details.
/// </summary>
internal sealed class RuntimeInterestRouter
{
    private readonly IInterestManagementControl _control;
    private readonly IRuntimeInterestPolicy _enabledPolicy;
    private readonly RuntimePlayerSpatialIndex? _players;

    public RuntimeInterestRouter(
        IInterestManagementControl control,
        WorldDimensions? dimensions = null,
        IRuntimeInterestPolicy? enabledPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        _control = control;
        _enabledPolicy = enabledPolicy ?? PassthroughInterestPolicy.Instance;
        _players = dimensions is null ? null : new RuntimePlayerSpatialIndex(dimensions);
    }

    public RuntimePlayerSpatialIndexSnapshot? PlayerSpatialSnapshot => _players?.Snapshot;

    public void TrackPlayer(PlayerSlotId slot, float positionX, float positionY) =>
        _players?.Update(slot, positionX, positionY);

    public void RemovePlayer(PlayerSlotId slot) => _players?.Remove(slot);

    public int CollectNearbyPlayers(
        PlayerSlotId subject,
        int radiusSections,
        Span<PlayerSlotId> destination,
        bool includeSubject = false) =>
        _players?.CollectNearbyPlayers(subject, radiusSections, destination, includeSubject) ?? 0;

    public bool ShouldRelayPlayerMovement(
        in RuntimePlayerInterestState observer,
        in RuntimePlayerInterestState subject)
    {
        if (!_control.IsEnabled)
            return true;

        return _enabledPolicy.ShouldObservePlayer(in observer, in subject);
    }
}

/// <summary>
/// Internal spatial input prepared by the synchronization layer. Unknown positions deliberately
/// remain representable so a future spatial policy can fail open until it has enough state to make
/// a safe visibility decision.
/// </summary>
internal readonly record struct RuntimePlayerInterestState(
    PlayerSlotId Slot,
    bool HasPosition,
    float PositionX,
    float PositionY);

internal interface IRuntimeInterestPolicy
{
    bool ShouldObservePlayer(
        in RuntimePlayerInterestState observer,
        in RuntimePlayerInterestState subject);
}

/// <summary>
/// Foundation policy used until enter/leave snapshots, hysteresis and forced resync semantics are
/// implemented. Enabling interest management today therefore changes routing ownership and builds
/// the spatial index, but does not yet suppress player-visible movement updates.
/// </summary>
internal sealed class PassthroughInterestPolicy : IRuntimeInterestPolicy
{
    public static PassthroughInterestPolicy Instance { get; } = new();

    private PassthroughInterestPolicy()
    {
    }

    public bool ShouldObservePlayer(
        in RuntimePlayerInterestState observer,
        in RuntimePlayerInterestState subject) => true;
}
